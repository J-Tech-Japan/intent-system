using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G243: Read-only <c>intent-cli intent next-slice --dry-run</c> command.
/// Reports the deterministic planning facts an AI agent needs before
/// drafting next-slice packet content: queue-state WIP, open clarification
/// blockers, preloaded packet candidates, and Child Issue Contract
/// missing-field findings. Never mutates state. Never creates GitHub
/// issues. Never applies labels. Never launches an AI provider.
/// </summary>
internal static class IntentNextSliceCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string OutcomeIssueCutReady = "issue-cut-ready";
    private const string OutcomeSkipDueToWip = "skip-next-slice-due-to-wip";
    private const string OutcomeClarificationRequired = "clarification-required";
    private const string OutcomeNoActionableItem = "no-actionable-item";

    private const string UsageLine =
        "Usage: intent-cli intent next-slice --dry-run [--domain <name>] [--target-repo <owner/repo>] [--format json|markdown]";

    private static readonly IReadOnlyList<string> RequiredContractSections =
        new[]
        {
            "Goal",
            "Why This Slice Exists Now",
            "Current Observed State",
            "Accepted Baseline You May Assume",
            "Target Repo / Path / Part",
            "In Scope",
            "Out Of Scope",
            "Acceptance Criteria",
            "Verification",
            "Related Links"
        };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var dryRun, out var domainOverride, out var targetRepo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!dryRun)
        {
            writer.WriteLine("--dry-run is required. Mutating modes are not implemented in this command.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Analyze(context, domainOverride, targetRepo);

        if (string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
        {
            WriteMarkdown(writer, result);
        }
        else
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }

        return 0;
    }

    internal static IntentNextSliceResult Analyze(CliContext context, string? domainOverride, string? targetRepo)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var queueStatePath = context.GetQueueStatePath();
        QueueState? queueState = null;
        var notes = new List<string>();

        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException jsonException)
            {
                notes.Add($"queue-state JSON could not be parsed: {jsonException.Message}");
            }
            catch (InvalidOperationException invalidOperation)
            {
                notes.Add($"queue-state payload was invalid: {invalidOperation.Message}");
            }
        }
        else
        {
            notes.Add($"no queue-state file at {queueStatePath}");
        }

        var packetRoot = Path.Combine(context.RepoRoot, ".intent-cli", "issues");

        var wip = new List<string>();
        var queued = new List<string>();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        if (queueState is not null)
        {
            foreach (var item in queueState.Items)
            {
                switch (item.State)
                {
                    case QueueItemState.Active:
                    case QueueItemState.Review:
                    case QueueItemState.Fixing:
                        // G275: filter WIP by the same domain/repo predicates as candidate selection
                        // so cross-domain in-flight items do not block the requested lane.
                        if (MatchesDomainAndRepoFilter(domain, targetRepo, queueState, item.ExecutionUnit, Path.Combine(packetRoot, item.ExecutionUnit)))
                        {
                            wip.Add(item.ExecutionUnit);
                        }

                        break;

                    case QueueItemState.Queued:
                        queued.Add(item.ExecutionUnit);
                        break;

                    case QueueItemState.Completed:
                        completed.Add(item.ExecutionUnit);
                        break;
                }
            }
        }

        var clarificationPath = Path.Combine(context.RepoRoot, "intents", domain, "clarifications", "open.md");
        var clarificationFilePresent = File.Exists(clarificationPath);
        var clarificationOpen = clarificationFilePresent
            && ClarificationOpenDetector.HasOpenBlocker(File.ReadAllText(clarificationPath));
        IntentNextSliceCandidate? candidate = null;
        if (Directory.Exists(packetRoot))
        {
            // Pick the first queued execution_unit in queue-state order whose packet
            // directory exists AND whose domain/target-repo matches the filter.
            // Falls back to the alphabetically-first packet directory not in completed
            // if no queued match exists.
            //
            // G275: When --domain and/or --target-repo are specified, packets are
            // filtered by those values. Domain is derived from the queue item's
            // clarification_return_path (shape: intents/<domain>/clarifications/open.md).
            // Target-repo is read from the packet.yaml file inside the packet directory.
            foreach (var executionUnit in queued)
            {
                var directory = Path.Combine(packetRoot, executionUnit);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                if (!MatchesDomainAndRepoFilter(domain, targetRepo, queueState, executionUnit, directory))
                {
                    continue;
                }

                candidate = BuildCandidate(executionUnit, directory);
                break;
            }

            if (candidate is null)
            {
                foreach (var directory in Directory
                    .EnumerateDirectories(packetRoot)
                    .OrderBy(path => path, StringComparer.Ordinal))
                {
                    var executionUnit = Path.GetFileName(directory)!;
                    if (completed.Contains(executionUnit))
                    {
                        continue;
                    }

                    if (!MatchesDomainAndRepoFilter(domain, targetRepo, queueState, executionUnit, directory))
                    {
                        continue;
                    }

                    candidate = BuildCandidate(executionUnit, directory);
                    break;
                }
            }
        }

        var recommendedOutcome = ComputeRecommendedOutcome(
            clarificationOpen,
            wip.Count > 0,
            candidate);

        return new IntentNextSliceResult
        {
            Domain = domain,
            TargetRepo = targetRepo,
            DryRun = true,
            QueueStatePath = queueStatePath,
            QueueStatePresent = queueState is not null,
            Wip = wip,
            ClarificationPath = clarificationPath,
            ClarificationFilePresent = clarificationFilePresent,
            ClarificationOpen = clarificationOpen,
            PacketRoot = packetRoot,
            Candidate = candidate,
            RecommendedOutcome = recommendedOutcome,
            Notes = notes
        };
    }

    private static IntentNextSliceCandidate BuildCandidate(string executionUnit, string directory)
    {
        var githubBodyPath = Path.Combine(directory, "github-body.md");
        var githubBodyPresent = File.Exists(githubBodyPath);
        var missing = new List<string>();

        if (githubBodyPresent)
        {
            var content = File.ReadAllText(githubBodyPath);
            foreach (var section in RequiredContractSections)
            {
                if (!ContainsSectionHeading(content, section))
                {
                    missing.Add(section);
                }
            }
        }
        else
        {
            missing.AddRange(RequiredContractSections);
        }

        return new IntentNextSliceCandidate
        {
            ExecutionUnit = executionUnit,
            PacketDirectory = directory,
            GithubBodyPresent = githubBodyPresent,
            MissingContractSections = missing
        };
    }

    private static bool ContainsSectionHeading(string content, string section)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = line.TrimStart('#').Trim();
            if (string.Equals(heading, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeRecommendedOutcome(
        bool clarificationOpen,
        bool wipPresent,
        IntentNextSliceCandidate? candidate)
    {
        if (clarificationOpen)
        {
            return OutcomeClarificationRequired;
        }

        if (wipPresent)
        {
            return OutcomeSkipDueToWip;
        }

        if (candidate is null)
        {
            return OutcomeNoActionableItem;
        }

        if (candidate.MissingContractSections.Count > 0)
        {
            return OutcomeClarificationRequired;
        }

        return OutcomeIssueCutReady;
    }

    /// <summary>
    /// G275: Returns true when the given execution unit / packet directory matches
    /// the requested domain and target-repo filters.
    ///
    /// Domain is inferred from the queue item's <c>clarification_return_path</c>
    /// field (shape: <c>intents/&lt;domain&gt;/clarifications/open.md</c>).
    /// When no queue item exists for the unit, domain filtering is skipped for
    /// that unit (it remains a candidate).
    ///
    /// Target-repo is read from <c>packet.yaml</c> in the packet directory.
    /// When the file is absent or unreadable, target-repo filtering is skipped
    /// (the unit remains a candidate).
    /// </summary>
    private static bool MatchesDomainAndRepoFilter(
        string domain,
        string? targetRepo,
        QueueState? queueState,
        string executionUnit,
        string directory)
    {
        // Domain filter: derive domain from queue item's clarification_return_path.
        // Path shape: intents/<domain>/clarifications/open.md
        if (queueState is not null)
        {
            QueueItem? item = null;
            foreach (var candidate in queueState.Items)
            {
                if (string.Equals(candidate.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                {
                    item = candidate;
                    break;
                }
            }

            if (item is not null)
            {
                var itemDomain = ExtractDomainFromReturnPath(item.ClarificationReturnPath);
                if (itemDomain is not null
                    && !string.Equals(itemDomain, domain, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // Target-repo filter: read packet.yaml to get target_repo field.
        if (!string.IsNullOrWhiteSpace(targetRepo))
        {
            var packetYamlPath = Path.Combine(directory, "packet.yaml");
            if (File.Exists(packetYamlPath))
            {
                var packetTargetRepo = TryReadPacketTargetRepo(packetYamlPath);
                if (packetTargetRepo is not null
                    && !string.Equals(packetTargetRepo, targetRepo, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Extracts the domain segment from a clarification return path of the form
    /// <c>intents/&lt;domain&gt;/clarifications/open.md</c>.
    /// Returns null when the path does not match the expected shape.
    /// </summary>
    private static string? ExtractDomainFromReturnPath(string returnPath)
    {
        if (string.IsNullOrWhiteSpace(returnPath))
        {
            return null;
        }

        // Normalise path separators so this works on all platforms.
        var normalised = returnPath.Replace('\\', '/');
        var parts = normalised.Split('/');

        // Expected: ["intents", "<domain>", "clarifications", "open.md"]
        if (parts.Length < 4)
        {
            return null;
        }

        if (!string.Equals(parts[0], "intents", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[1];
    }

    /// <summary>
    /// Reads the <c>target_repo</c> scalar from a <c>packet.yaml</c> file using a
    /// lightweight line scanner. Returns null when the file cannot be parsed or the
    /// field is absent.
    /// </summary>
    private static string? TryReadPacketTargetRepo(string packetYamlPath)
    {
        try
        {
            var content = File.ReadAllText(packetYamlPath);
            using var reader = new StringReader(content);
            var inImplementationSection = false;
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                var trimmedLine = line.TrimEnd();

                // Track section headers (zero-indent, ends with colon).
                if (!char.IsWhiteSpace(trimmedLine.Length > 0 ? trimmedLine[0] : ' ')
                    && trimmedLine.EndsWith(":", StringComparison.Ordinal))
                {
                    var sectionName = trimmedLine[..^1];
                    inImplementationSection = string.Equals(
                        sectionName,
                        "implementation_issue_packet",
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inImplementationSection)
                {
                    continue;
                }

                // Look for `  target_repo: <value>` (two-space indent).
                if (!trimmedLine.StartsWith("  target_repo:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = trimmedLine["  target_repo:".Length..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1];
                }

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (IOException)
        {
            // File unreadable — skip filter.
        }
        catch (UnauthorizedAccessException)
        {
            // File unreadable — skip filter.
        }

        return null;
    }

    private static void WriteMarkdown(TextWriter writer, IntentNextSliceResult result)
    {
        writer.WriteLine($"# Intent next-slice dry-run — {result.Domain}");
        writer.WriteLine();
        writer.WriteLine($"- target repo: {(result.TargetRepo ?? "(unspecified)")}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- queue-state present: {(result.QueueStatePresent ? "yes" : "no")}");
        writer.WriteLine($"- recommended outcome: {result.RecommendedOutcome}");
        writer.WriteLine();

        writer.WriteLine("## WIP (in-flight)");
        if (result.Wip.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var unit in result.Wip)
            {
                writer.WriteLine($"- {unit}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Open clarifications");
        writer.WriteLine($"- file: {result.ClarificationPath}");
        writer.WriteLine($"- file present: {(result.ClarificationFilePresent ? "yes" : "no")}");
        writer.WriteLine($"- has open blocker: {(result.ClarificationOpen ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Candidate");
        if (result.Candidate is null)
        {
            writer.WriteLine("- none");
        }
        else
        {
            writer.WriteLine($"- execution unit: {result.Candidate.ExecutionUnit}");
            writer.WriteLine($"- packet directory: {result.Candidate.PacketDirectory}");
            writer.WriteLine($"- github-body.md present: {(result.Candidate.GithubBodyPresent ? "yes" : "no")}");
            if (result.Candidate.MissingContractSections.Count == 0)
            {
                writer.WriteLine("- missing contract sections: none");
            }
            else
            {
                writer.WriteLine("- missing contract sections:");
                foreach (var section in result.Candidate.MissingContractSections)
                {
                    writer.WriteLine($"  - {section}");
                }
            }
        }
        writer.WriteLine();

        if (result.Notes.Count > 0)
        {
            writer.WriteLine("## Notes");
            foreach (var note in result.Notes)
            {
                writer.WriteLine($"- {note}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out bool dryRun,
        out string? domainOverride,
        out string? targetRepo,
        out string format,
        out string error)
    {
        dryRun = false;
        domainOverride = null;
        targetRepo = null;
        format = FormatJson;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--dry-run":
                    dryRun = true;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (json or markdown).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'json' or 'markdown' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent next-slice");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only next-slice planning facts. Reports WIP, clarification blockers, candidate packets, and missing contract fields without mutating state.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record IntentNextSliceResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("dry_run")]
    public required bool DryRun { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("queue_state_present")]
    public required bool QueueStatePresent { get; init; }

    [JsonPropertyName("wip")]
    public required IReadOnlyList<string> Wip { get; init; }

    [JsonPropertyName("clarification_path")]
    public required string ClarificationPath { get; init; }

    [JsonPropertyName("clarification_file_present")]
    public required bool ClarificationFilePresent { get; init; }

    [JsonPropertyName("clarification_open")]
    public required bool ClarificationOpen { get; init; }

    [JsonPropertyName("packet_root")]
    public required string PacketRoot { get; init; }

    [JsonPropertyName("candidate")]
    public IntentNextSliceCandidate? Candidate { get; init; }

    [JsonPropertyName("recommended_outcome")]
    public required string RecommendedOutcome { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record IntentNextSliceCandidate
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("github_body_present")]
    public required bool GithubBodyPresent { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }
}
