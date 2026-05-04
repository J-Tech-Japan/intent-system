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
                        wip.Add(item.ExecutionUnit);
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

        var packetRoot = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        IntentNextSliceCandidate? candidate = null;
        if (Directory.Exists(packetRoot))
        {
            // Pick the first queued execution_unit in queue-state order whose packet
            // directory exists. Falls back to the alphabetically-first packet
            // directory not in completed if no queued match exists.
            foreach (var executionUnit in queued)
            {
                var directory = Path.Combine(packetRoot, executionUnit);
                if (!Directory.Exists(directory))
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
