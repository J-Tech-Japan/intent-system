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

        // PR #830 review repair (19:07 comment): `--domain` is
        // optional on the CLI, but the underlying domain-binding
        // regex check MUST ALWAYS RUN — otherwise a wrong-domain
        // packet could be seeded as long as `target_repo` matches
        // (fail-open). When `--domain` is omitted, default to the
        // host config's `Project.Domain`. Refuse to proceed if
        // neither is available: there is no safe deterministic way
        // to pick a domain in that case, and silently skipping the
        // regex check is exactly the gap the reviewer flagged.
        var effectiveDomain = !string.IsNullOrWhiteSpace(domain)
            ? domain!
            : context.Config?.Project?.Domain;
        if (string.IsNullOrWhiteSpace(effectiveDomain))
        {
            writer.WriteLine(
                "--domain is required when `[project] domain` is not set in `.intent-cli/config.toml`. "
                + "The domain-binding regex check refuses to silently fail open on wrong-domain packets.");
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

        // G485: resolve the domain-binding `execution_unit_regex` through the
        // SAME shared resolver the host loop and `automation summary` use
        // (NextSliceDomainBindingsExecutionUnitRegex), instead of a duplicate
        // local parser that could disagree with summary on the active domain's
        // regex. The structured outcome also lets the diagnostic distinguish a
        // missing bindings file from a present-but-empty regex field.
        var regexResolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, effectiveDomain);

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
            ExecutionUnitRegex = regexResolution.Pattern,
            RequestedTargetRepo = targetRepo,
            // PR #830 review repair (19:07 comment): always require
            // a domain binding now that `effectiveDomain` is always
            // populated (either from `--domain` or the host config).
            // Closes the fail-open path where omitting `--domain`
            // skipped the regex check.
            RequireDomainBinding = true,
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
                    + validation.Summary
                    + DescribeBindingResolution(validation.Reason, effectiveDomain, regexResolution),
            };
            EmitResult(writer, format, unsafeResult);
            return 1;
        }

        var packetFields = ReadPacketFields(packetDirectoryAbsolute);
        // PR #830 review repair #2: resolve the canonical clarification
        // return path for the host domain so packets that omit the
        // `clarification_return_path` field still seed with a usable
        // route. The convention used across the codebase (see
        // BugIntentEnqueueCommand, ProjectionPacketRuntimeReader,
        // AutomationHostQueueItemRecoveryCommand) is
        // `intents/<domain>/clarifications/open.md`. Falling back to
        // `string.Empty` here would silently break the packet ↔
        // queue-item clarification path contract enforced by
        // ClarifyOpenCommand and MetadataValidateAnalyzer.
        // PR #830 review repair (19:07 comment): `effectiveDomain`
        // is already resolved above (--domain → host config Domain,
        // with a hard refusal when neither is set) so the
        // clarification path computation reuses the same value
        // rather than re-deriving it.
        var defaultClarificationReturnPath = $"intents/{effectiveDomain}/clarifications/open.md";

        // PR #830 review repair #3 (08:27 comment): align role /
        // priority fallbacks with the established
        // `QueueEnqueueCommand` contract so seeded queue items look
        // the same as packets enqueued through the standard path.
        // - WorkerRole / ReviewRole default to the host's configured
        //   roles (`context.Config.Roles.Implement` / `Review`,
        //   which default to "Claude" / "Codex" per
        //   `CliRuntimeContracts.DefaultImplementRole` /
        //   `DefaultReviewRole`).
        // - Priority defaults to "high" to match
        //   `QueueEnqueueCommand.DefaultPriority`.
        // Packets that explicitly declare any of these still win via
        // `LookupScalar` precedence inside BuildSeedItem.
        var defaultWorkerRole = !string.IsNullOrWhiteSpace(context.Config.Roles?.Implement)
            ? context.Config.Roles!.Implement
            : CliRuntimeContracts.DefaultImplementRole;
        var defaultReviewRole = !string.IsNullOrWhiteSpace(context.Config.Roles?.Review)
            ? context.Config.Roles!.Review
            : CliRuntimeContracts.DefaultReviewRole;
        const string defaultPriority = "high";

        var seed = BuildSeedItem(
            executionUnit,
            packetFields,
            defaultClarificationReturnPath,
            defaultWorkerRole,
            defaultReviewRole,
            defaultPriority);

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
    internal static QueueItem BuildSeedItem(
        string executionUnit,
        IReadOnlyDictionary<string, string> packetFields,
        string defaultClarificationReturnPath,
        string defaultWorkerRole,
        string defaultReviewRole,
        string defaultPriority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(packetFields);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultClarificationReturnPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultWorkerRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultReviewRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPriority);

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
        // PR #830 review repair #2: ClarificationReturnPath
        // convention is `intents/<domain>/clarifications/open.md`. If
        // the packet declares one, honor it; otherwise fall back to
        // the caller-provided per-domain default so downstream
        // consumers (ClarifyOpenCommand, MetadataValidateAnalyzer)
        // see a real path. An empty path would violate the packet ↔
        // queue-item clarification path contract.
        var clarificationReturnPath = LookupScalar(packetFields,
            "clarification_return_path",
            "implementation_issue_packet.clarification_return_path")
            ?? defaultClarificationReturnPath;
        // PR #830 review repair #3: align role / priority fallbacks
        // with the established `QueueEnqueueCommand` contract
        // (config-driven roles, priority "high"). Hardcoded
        // "coder" / "reviewer" / "normal" diverged from that
        // contract, so packets enqueued via this lane looked
        // different from packets enqueued via the standard path.
        // Packets that DO declare any field still win via
        // LookupScalar.
        var workerRole = LookupScalar(packetFields,
            "worker_role",
            "implementation_issue_packet.worker_role")
            ?? defaultWorkerRole;
        var reviewRole = LookupScalar(packetFields,
            "review_role",
            "implementation_issue_packet.review_role")
            ?? defaultReviewRole;
        var priority = LookupScalar(packetFields,
            "priority",
            "implementation_issue_packet.priority")
            ?? defaultPriority;

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

    /// <summary>
    /// G485: turn the shared binding resolution outcome into a precise,
    /// appended diagnostic so the operator can tell apart a missing bindings
    /// file, a present-but-empty <c>execution_unit_regex</c> field, and an
    /// invalid pattern — but only when the refusal is actually about the
    /// domain binding (<c>missing-domain-binding-regex</c>). Other refusal
    /// reasons (missing contract sections, wrong target repo, etc.) keep the
    /// analyzer's own summary unchanged.
    /// </summary>
    private static string DescribeBindingResolution(
        string? reason,
        string domain,
        ExecutionUnitRegexResolution resolution)
    {
        if (!string.Equals(reason, PreparedPacketCommitReadyAnalyzer.ReasonMissingDomainBindingRegex, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // A `missing-domain-binding-regex` refusal always corresponds to a
        // MissingOrAbsent resolution (a present pattern compiles to commit-ready
        // or to the distinct `invalid-domain-binding-regex` reason the analyzer
        // owns), so point the operator at the exact bindings source that was
        // consulted — the same one `automation summary` reads.
        return resolution.Kind == ExecutionUnitRegexResolutionKind.MissingOrAbsent
            ? $" (domain `{domain}` binding: no `execution_unit_regex` resolved from `{resolution.BindingsPath}`"
                + " — confirm the bindings file exists for this domain and declares a top-level `execution_unit_regex`;"
                + $" `intent-cli automation summary --domain {domain} --format json` reads the same source.)"
            : string.Empty;
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
