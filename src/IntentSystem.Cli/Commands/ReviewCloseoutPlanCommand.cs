using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G247: Read-only <c>intent-cli review closeout-plan</c> command. Reports
/// what a review-pass closeout would mutate before any merge or parent
/// state write. Resolves the queue item via <c>linked_pr</c>, lists the
/// packet directory contents, validates Child Issue Contract sections,
/// derives the expected submodule path, and emits the deterministic
/// validation/closeout steps the operator must run separately. Reports
/// blocking ambiguities as actionable gaps. Never mutates state. Never
/// launches an AI provider.
/// </summary>
internal static class ReviewCloseoutPlanCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli review closeout-plan --pr <n> --repo <owner/repo> [--domain <name>] [--format json|markdown]";

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

        if (!TryParseArguments(args, out var pr, out var repo, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var queueStatePath = context.GetQueueStatePath();
        var gaps = new List<string>();

        QueueState? queueState = null;
        if (!File.Exists(queueStatePath))
        {
            gaps.Add($"queue-state file not found: {queueStatePath}");
        }
        else
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException jsonException)
            {
                gaps.Add($"queue-state JSON could not be parsed: {jsonException.Message}");
            }
            catch (InvalidOperationException invalidOperation)
            {
                gaps.Add($"queue-state payload was invalid: {invalidOperation.Message}");
            }
        }

        QueueItem? matchedItem = null;
        if (queueState is not null)
        {
            var prToken = pr!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item.LinkedPr, prToken));
            if (matchedItem is null)
            {
                gaps.Add($"no queue item found with linked_pr matching #{pr}.");
            }
        }

        string? packetDirectory = null;
        IReadOnlyList<string> packetFiles = Array.Empty<string>();
        IReadOnlyList<string> missingSections = Array.Empty<string>();
        ReviewCloseoutLinkedIssue? linkedIssue = null;
        if (matchedItem is not null)
        {
            packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", matchedItem.ExecutionUnit);
            if (Directory.Exists(packetDirectory))
            {
                packetFiles = Directory.EnumerateFiles(packetDirectory)
                    .Select(file => Path.GetFileName(file))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
                if (!File.Exists(githubBodyPath))
                {
                    gaps.Add($"github-body.md not found in packet directory: {packetDirectory}");
                    missingSections = PacketDraftCommand.RequiredContractSections;
                }
                else
                {
                    var content = File.ReadAllText(githubBodyPath);
                    missingSections = PacketDraftCommand.RequiredContractSections
                        .Where(section => !ContainsSectionHeading(content, section))
                        .ToArray();
                    if (missingSections.Count > 0)
                    {
                        gaps.Add("Child Issue Contract is incomplete; sections missing: " + string.Join(", ", missingSections));
                    }
                }
            }
            else
            {
                gaps.Add($"packet directory not found: {packetDirectory}");
                missingSections = PacketDraftCommand.RequiredContractSections;
            }

            if (matchedItem.LinkedIssue is null)
            {
                gaps.Add("queue item has no linked_issue; closeout requires a linked issue to close.");
            }
            else
            {
                linkedIssue = new ReviewCloseoutLinkedIssue
                {
                    Repo = matchedItem.LinkedIssue.Repo,
                    Number = matchedItem.LinkedIssue.Number,
                    Url = matchedItem.LinkedIssue.Url
                };
            }
        }

        var expectedSubmodulePath = DeriveSubmodulePath(repo!);

        var validationSteps = new List<string>
        {
            "Run focused tests for the touched packet area.",
            "Run `git diff --check` against the merge result.",
            "Confirm the PR head SHA before and after the review pass."
        };

        var closeoutSteps = new List<string>
        {
            $"Sync the parent submodule pointer for {repo} at `{expectedSubmodulePath}` to the merge commit.",
            "Mark the queue item completed and append `pr-merged` + `closeout-recorded` runs events (see `intent-cli closeout pr --write`).",
            "Close the linked child issue once the merge is durable.",
            "Commit and push the parent durable state (queue-state, runs, submodule pointer)."
        };

        var result = new ReviewCloseoutPlanResult
        {
            Domain = domain,
            Repo = repo!,
            Pr = pr!.Value,
            QueueStatePath = queueStatePath,
            ExecutionUnit = matchedItem?.ExecutionUnit,
            QueueItemState = matchedItem?.State.ToString().ToLowerInvariant(),
            LinkedIssue = linkedIssue,
            PacketDirectory = packetDirectory,
            PacketFiles = packetFiles,
            MissingContractSections = missingSections,
            ExpectedSubmodulePath = expectedSubmodulePath,
            ValidationSteps = validationSteps,
            ClosingSteps = closeoutSteps,
            Gaps = gaps,
            Ready = gaps.Count == 0 && matchedItem is not null
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return result.Ready ? 0 : 1;
    }

    private static bool MatchesLinkedPr(string? linkedPr, string prToken)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        if (string.Equals(linkedPr, prToken, StringComparison.Ordinal))
        {
            return true;
        }

        return linkedPr!.EndsWith($"/{prToken}", StringComparison.Ordinal);
    }

    private static string DeriveSubmodulePath(string repo)
    {
        var segments = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        var name = segments.Length == 2 ? segments[1] : repo;
        return $"submodules/{name}";
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

    private static void WriteMarkdown(TextWriter writer, ReviewCloseoutPlanResult result)
    {
        writer.WriteLine($"# Review closeout plan — {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- execution unit: {(result.ExecutionUnit ?? "(unresolved)")}");
        if (!string.IsNullOrWhiteSpace(result.QueueItemState))
        {
            writer.WriteLine($"- queue item state: {result.QueueItemState}");
        }
        writer.WriteLine($"- expected submodule path: {result.ExpectedSubmodulePath}");
        writer.WriteLine($"- ready: {(result.Ready ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Linked issue");
        if (result.LinkedIssue is null)
        {
            writer.WriteLine("- (none recorded)");
        }
        else
        {
            writer.WriteLine($"- repo: {result.LinkedIssue.Repo}");
            if (result.LinkedIssue.Number is not null)
            {
                writer.WriteLine($"- number: {result.LinkedIssue.Number}");
            }
            if (!string.IsNullOrWhiteSpace(result.LinkedIssue.Url))
            {
                writer.WriteLine($"- url: {result.LinkedIssue.Url}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Packet");
        if (string.IsNullOrWhiteSpace(result.PacketDirectory))
        {
            writer.WriteLine("- packet directory: (unknown)");
        }
        else
        {
            writer.WriteLine($"- packet directory: {result.PacketDirectory}");
            if (result.PacketFiles.Count == 0)
            {
                writer.WriteLine("- files: (none)");
            }
            else
            {
                writer.WriteLine("- files:");
                foreach (var file in result.PacketFiles)
                {
                    writer.WriteLine($"  - {file}");
                }
            }
            if (result.MissingContractSections.Count == 0)
            {
                writer.WriteLine("- missing contract sections: none");
            }
            else
            {
                writer.WriteLine("- missing contract sections:");
                foreach (var section in result.MissingContractSections)
                {
                    writer.WriteLine($"  - {section}");
                }
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Validation steps");
        foreach (var step in result.ValidationSteps)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Closeout steps");
        foreach (var step in result.ClosingSteps)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        if (result.Gaps.Count > 0)
        {
            writer.WriteLine("## Gaps");
            foreach (var gap in result.Gaps)
            {
                writer.WriteLine($"- {gap}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int? pr,
        out string? repo,
        out string? domainOverride,
        out string format,
        out string error)
    {
        pr = null;
        repo = null;
        domainOverride = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue) || prValue <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    pr = prValue;
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
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

        if (pr is null)
        {
            error = "--pr is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("review closeout-plan");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only review-pass closeout planning facts. Reports execution unit, linked issue, packet refs, expected submodule path, validation steps, and any blocking gaps without mutating state.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record ReviewCloseoutPlanResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("queue_item_state")]
    public string? QueueItemState { get; init; }

    [JsonPropertyName("linked_issue")]
    public ReviewCloseoutLinkedIssue? LinkedIssue { get; init; }

    [JsonPropertyName("packet_directory")]
    public string? PacketDirectory { get; init; }

    [JsonPropertyName("packet_files")]
    public required IReadOnlyList<string> PacketFiles { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }

    [JsonPropertyName("expected_submodule_path")]
    public required string ExpectedSubmodulePath { get; init; }

    [JsonPropertyName("validation_steps")]
    public required IReadOnlyList<string> ValidationSteps { get; init; }

    [JsonPropertyName("closeout_steps")]
    public required IReadOnlyList<string> ClosingSteps { get; init; }

    [JsonPropertyName("gaps")]
    public required IReadOnlyList<string> Gaps { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }
}

internal sealed record ReviewCloseoutLinkedIssue
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("number")]
    public int? Number { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
