using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G363: <c>intent-cli automation queue-seed-from-packet
/// --execution-unit &lt;unit&gt; [--target-repo &lt;owner/repo&gt;]
/// [--domain &lt;name&gt;] [--write] [--format markdown|json]</c> —
/// seed <c>.intent-cli/queue-state.json</c> with a queued item for
/// a validated prepared packet directory so downstream
/// <c>issue publish-flow</c> and closeout can find the execution
/// unit.
///
/// The seed is gated by
/// <see cref="PreparedPacketCommitReadyAnalyzer"/>: the packet must
/// have the four canonical files, packet.yaml must parse, the
/// directory-derived execution-unit must match the active domain
/// binding regex (when configured), and the declared
/// <c>target_repo</c> must match the requested one. Anything else
/// is a structured unsafe stop — the seed is REFUSED rather than
/// silently inserting a wrong-domain / malformed item.
///
/// Without <c>--write</c> the command emits the planned seed
/// (dry-run); with <c>--write</c> it inserts the item, persists
/// <c>queue-state.json</c>, and appends a
/// <c>queue_seeded_from_packet</c> event to
/// <c>.intent-cli/runs.jsonl</c>. Existing items are left
/// untouched — re-running on an already-seeded unit returns
/// <c>already-seeded</c>.
/// </summary>
internal static class AutomationQueueSeedFromPacketCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string ClassificationReady = "queue-seed-ready";
    public const string ClassificationAlreadySeeded = "already-seeded";
    public const string ClassificationApplied = "queue-seed-applied";
    public const string ClassificationUnsafe = "unsafe-prepared-packet";
    public const string ClassificationPacketDirectoryMissing = "packet-directory-missing";

    public const string SeedEventName = "queue_seeded_from_packet";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var executionUnit, out var domain, out var targetRepo,
                out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var packetDirectoryRelative = $".intent-cli/issues/{executionUnit}/";
        var packetDirectoryAbsolute = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
        if (!Directory.Exists(packetDirectoryAbsolute))
        {
            var missing = new QueueSeedFromPacketResult
            {
                Classification = ClassificationPacketDirectoryMissing,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                Summary = $"prepared packet directory `{packetDirectoryRelative}` does not exist on disk; nothing to seed.",
            };
            EmitResult(writer, format, missing);
            return 1;
        }

        // Validate via PreparedPacketCommitReadyAnalyzer (G361). The
        // probe reads the four canonical files and feeds them to the
        // pure analyzer.
        var validation = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = executionUnit,
            PacketYaml = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml)),
            ImplementationMarkdown = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNameImplementationMarkdown)),
            ReviewContextMarkdown = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNameReviewContextMarkdown)),
            GithubBodyMarkdown = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNameGithubBodyMarkdown)),
            ExecutionUnitRegex = TryResolveExecutionUnitRegex(context, domain),
            RequestedTargetRepo = targetRepo,
            RequireDomainBinding = !string.IsNullOrWhiteSpace(domain),
        });

        if (validation.Classification != PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady)
        {
            var unsafeResult = new QueueSeedFromPacketResult
            {
                Classification = ClassificationUnsafe,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                UnsafeReason = validation.Reason,
                Summary = $"refusing to seed queue-state from `{packetDirectoryRelative}`: "
                    + validation.Summary,
            };
            EmitResult(writer, format, unsafeResult);
            return 1;
        }

        var packetFields = ReadPacketFields(packetDirectoryAbsolute);
        var seed = BuildSeedItem(executionUnit, packetFields);

        // Read current queue-state (if present). Missing file is OK —
        // we'll create one with this seed as the sole item.
        var queueStatePath = context.GetQueueStatePath();
        QueueState? existing = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                existing = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException exception)
            {
                writer.WriteLine($"queue-state.json at `{queueStatePath}` is unparseable; refusing to seed. {exception.Message}");
                return 1;
            }
        }

        // Already-seeded check is keyed on execution_unit (the
        // canonical identifier). Operators re-running the command on
        // a unit that's already in the queue get a no-op signal so
        // they can move on rather than treating the run as failure.
        if (existing is not null
            && existing.Items.Any(item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)))
        {
            var already = new QueueSeedFromPacketResult
            {
                Classification = ClassificationAlreadySeeded,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                Summary = $"queue-state already contains an entry for `{executionUnit}`; nothing to seed.",
                SeededItem = seed,
            };
            EmitResult(writer, format, already);
            return 0;
        }

        if (!write)
        {
            var readyDryRun = new QueueSeedFromPacketResult
            {
                Classification = ClassificationReady,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = false,
                SeededItem = seed,
                Summary = $"prepared packet `{packetDirectoryRelative}` validated; queue-state would be seeded with a new queued item for `{executionUnit}`. "
                    + "Re-run with `--write` to persist.",
                RecommendedActions = new[]
                {
                    $"intent-cli automation queue-seed-from-packet --execution-unit {executionUnit}"
                        + (string.IsNullOrWhiteSpace(targetRepo) ? string.Empty : $" --target-repo {targetRepo}")
                        + (string.IsNullOrWhiteSpace(domain) ? string.Empty : $" --domain {domain}")
                        + " --write",
                },
            };
            EmitResult(writer, format, readyDryRun);
            return 0;
        }

        // --write: insert seed, persist queue-state, append runs.jsonl event.
        var newItems = new List<QueueItem>(existing?.Items ?? Array.Empty<QueueItem>()) { seed };
        var updated = new QueueState
        {
            SchemaVersion = existing?.SchemaVersion ?? "1",
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = newItems,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updated));

        var runsPath = Path.Combine(context.RepoRoot, ".intent-cli", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        var runEvent = new RunEvent
        {
            Ts = DateTimeOffset.UtcNow,
            ExecutionUnit = executionUnit,
            Event = SeedEventName,
            By = "automation queue-seed-from-packet (G363)",
            PacketRef = packetDirectoryRelative,
        };
        File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");

        var applied = new QueueSeedFromPacketResult
        {
            Classification = ClassificationApplied,
            ExecutionUnit = executionUnit,
            PacketDirectory = packetDirectoryRelative,
            Write = true,
            SeededItem = seed,
            Summary = $"seeded queue-state with a new queued item for `{executionUnit}` from validated packet `{packetDirectoryRelative}`. "
                + $"Appended `{SeedEventName}` event to `.intent-cli/runs.jsonl`.",
        };
        EmitResult(writer, format, applied);
        return 0;
    }

    /// <summary>
    /// Build the queued <see cref="QueueItem"/> for a validated
    /// packet. Fields that are not present in packet.yaml are filled
    /// with deterministic defaults so the seed has a complete shape;
    /// the operator can override later via metadata-update if
    /// needed.
    /// </summary>
    internal static QueueItem BuildSeedItem(string executionUnit, IReadOnlyDictionary<string, string> packetFields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(packetFields);

        var packetDir = $".intent-cli/issues/{executionUnit}/";
        var title = LookupScalar(packetFields,
            "implementation_issue_packet.issue_title",
            "implementation_issue.issue_title",
            "issue_title",
            "title")
            ?? executionUnit;

        var targetRepo = LookupScalar(packetFields,
            "implementation_issue_packet.target_repo",
            "implementation_issue.target_repo",
            "target_repo");
        // ClarificationReturnPath is conventionally
        // `intents/<domain>/clarifications/open.md`. Without a packet
        // field we leave it empty — downstream consumers degrade
        // gracefully and the operator can metadata-update later.
        var clarificationReturnPath = LookupScalar(packetFields,
            "clarification_return_path",
            "implementation_issue_packet.clarification_return_path")
            ?? string.Empty;
        var workerRole = LookupScalar(packetFields,
            "worker_role",
            "implementation_issue_packet.worker_role")
            ?? "coder";
        var reviewRole = LookupScalar(packetFields,
            "review_role",
            "implementation_issue_packet.review_role")
            ?? "reviewer";
        var priority = LookupScalar(packetFields,
            "priority",
            "implementation_issue_packet.priority")
            ?? "normal";

        // PR #830 review repair: preserve packet.yaml dependency /
        // blocked_by data when the packet declares them. Previously
        // these fields were hardcoded empty, which silently dropped
        // dependency metadata the operator already authored into
        // the prepared packet. The G361 scalar parser stores
        // bracketed inline-list values as raw strings (e.g.
        // `dependencies: [G1, G2]` ends up keyed by
        // `dependencies` with value `[G1, G2]`); we expand them
        // here. Empty arrays remain the safe default when the
        // packet truly carries no dependencies — never guess.
        var dependencies = ParsePacketArrayField(packetFields,
            "implementation_issue_packet.dependencies",
            "implementation_issue.dependencies",
            "dependencies");
        var blockedBy = ParsePacketArrayField(packetFields,
            "implementation_issue_packet.blocked_by",
            "implementation_issue.blocked_by",
            "blocked_by");

        var item = new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = title,
            State = QueueItemState.Queued,
            Dependencies = dependencies,
            BlockedBy = blockedBy,
            ClarificationReturnPath = clarificationReturnPath,
            PacketPaths = new PacketPaths
            {
                Implementation = packetDir + PreparedPacketCommitReadyAnalyzer.FileNameImplementationMarkdown,
                ReviewContext = packetDir + PreparedPacketCommitReadyAnalyzer.FileNameReviewContextMarkdown,
                Yaml = packetDir + PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml,
            },
            WorkerRole = workerRole,
            ReviewRole = reviewRole,
            Priority = priority,
        };
        return item;
    }

    /// <summary>
    /// PR #830 review repair: parse a packet.yaml field whose value
    /// is an inline list (<c>[G1, G2]</c>) or a comma-separated
    /// scalar. The G361 PreparedPacketYamlScalarParser stores
    /// list-shaped values as the raw bracketed text; this helper
    /// strips brackets, splits on commas, and trims surrounding
    /// whitespace / quotes. Returns an empty list when no key
    /// resolves — packets carrying no dependencies legitimately
    /// produce queue items with empty arrays (no guessing).
    /// </summary>
    internal static IReadOnlyList<string> ParsePacketArrayField(
        IReadOnlyDictionary<string, string> packetFields,
        params string[] keys)
    {
        var raw = LookupScalar(packetFields, keys);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Array.Empty<string>();
        }
        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().Trim('"', '\''))
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadPacketFields(string packetDirectoryAbsolute)
    {
        var packetYamlPath = Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml);
        var content = TryReadFile(packetYamlPath);
        if (string.IsNullOrEmpty(content))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        try
        {
            return PreparedPacketYamlScalarParser.Parse(content);
        }
        catch (FormatException)
        {
            // Validation upstream already enforced parseable YAML;
            // defensive fallback returns empty map so the seed uses
            // the deterministic defaults.
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string? LookupScalar(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string? TryReadFile(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return null;
        }
        try
        {
            return File.ReadAllText(absolutePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryResolveExecutionUnitRegex(CliContext context, string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }
        var parentRoot = context.ResolveParentIntentRepoRootPath();
        if (!string.IsNullOrWhiteSpace(parentRoot))
        {
            var parentPath = Path.Combine(parentRoot, "intents", domain, "automation", "bindings.md");
            if (File.Exists(parentPath))
            {
                return ExtractExecutionUnitRegex(TryReadFile(parentPath));
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(context.RepoRoot))
        {
            return null;
        }
        var childPath = Path.Combine(context.RepoRoot, "intents", domain, "automation", "bindings.md");
        if (!File.Exists(childPath))
        {
            return null;
        }
        return ExtractExecutionUnitRegex(TryReadFile(childPath));
    }

    private static string? ExtractExecutionUnitRegex(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var rawLine in normalized.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0) continue;
            if (line[0] == ' ' || line[0] == '\t') continue;
            if (line.StartsWith('#') || line.StartsWith("- ", StringComparison.Ordinal)) continue;
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal)) continue;
            var colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0) continue;
            var key = line[..colonIndex].Trim();
            if (!string.Equals(key, "execution_unit_regex", StringComparison.Ordinal)) continue;
            var value = line[(colonIndex + 1)..].Trim();
            if (value.Length >= 2
                && ((value[0] == '\'' && value[^1] == '\'')
                    || (value[0] == '"' && value[^1] == '"')))
            {
                value = value[1..^1];
            }
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    private static void EmitResult(TextWriter writer, string format, QueueSeedFromPacketResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }
        writer.WriteLine($"# automation queue-seed-from-packet (G363) — `{result.ExecutionUnit}`");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- packet directory: `{result.PacketDirectory}`");
        writer.WriteLine($"- write: {(result.Write ? "yes" : "no (dry-run)")}");
        if (!string.IsNullOrWhiteSpace(result.UnsafeReason))
        {
            writer.WriteLine($"- unsafe reason: `{result.UnsafeReason}`");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.RecommendedActions is { Count: > 0 })
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended actions");
            foreach (var action in result.RecommendedActions)
            {
                writer.WriteLine($"- {action}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string executionUnit,
        out string? domain,
        out string? targetRepo,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = string.Empty;
        domain = null;
        targetRepo = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }
                    executionUnit = args[++index].Trim();
                    break;
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value (owner/repo).";
                        return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }
        return true;
    }
}

internal sealed record QueueSeedFromPacketResult
{
    public required string Classification { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string PacketDirectory { get; init; }
    public required bool Write { get; init; }
    public required string Summary { get; init; }
    public string? UnsafeReason { get; init; }
    public QueueItem? SeededItem { get; init; }
    public IReadOnlyList<string>? RecommendedActions { get; init; }
}
