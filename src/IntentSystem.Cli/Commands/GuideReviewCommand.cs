using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G248: Read-only <c>intent-cli guide review</c> command. Emits PR-specific
/// review guidance so an AI reviewer can ask intent-cli what to inspect
/// without reading local skill files. Resolves the execution unit via
/// the queue item's <c>linked_pr</c> field, lists packet refs, surfaces
/// the head of <c>review-context.md</c> when present, and emits a
/// deterministic review checklist, boundaries, and validation
/// suggestions. Never launches an AI provider, never posts comments,
/// never mutates state.
/// </summary>
internal static class GuideReviewCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const int ReviewContextHeadLines = 12;

    private const string UsageLine =
        "Usage: intent-cli guide review --pr <n> --repo <owner/repo> [--domain <name>] [--format markdown|json]";

    private static readonly IReadOnlyList<string> ReviewChecklist = new[]
    {
        "PR scope matches the issue body's In Scope / Out Of Scope sections.",
        "Acceptance Criteria from the issue body are satisfied by the changeset.",
        "Verification steps named in the issue body are runnable and pass.",
        "No prompt-specific label mutation knowledge leaked into the change.",
        "No `intent-target` or `intent-pr-created` are added to the PR by the change.",
        "No AI provider is launched by `intent-cli` in the change.",
        "Generated artifacts alone are not treated as the solution when real code changes are required."
    };

    private static readonly IReadOnlyList<string> ReviewBoundaries = new[]
    {
        "Review is read-only; do not push commits or mutate labels from this loop.",
        "Do not merge the PR from this command; merge happens via the host closeout path.",
        "Do not invent label transitions; rely on installed `intent-cli` claim/complete recommendations.",
        "Do not read parent host packet files to fill contract gaps; flag the gap and stop instead."
    };

    private static readonly IReadOnlyList<string> DefaultValidationSuggestions = new[]
    {
        "Run focused tests named in the packet's Verification section.",
        "Run `git diff --check` against the merge result.",
        "Confirm the PR head SHA before and after the review pass."
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

        // G267: nested `guide review run` paste-ready review-prompt subcommand.
        if (args.Length >= 1 && string.Equals(args[0], "run", StringComparison.Ordinal))
        {
            return GuideReviewRunCommand.Execute(context, args[1..], writer);
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
            matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item.LinkedPr, repo!, prToken));
            if (matchedItem is null)
            {
                gaps.Add($"no queue item found with linked_pr matching #{pr}.");
            }
        }

        string? packetDirectory = null;
        IReadOnlyList<string> packetFiles = Array.Empty<string>();
        string? reviewContextHead = null;
        if (matchedItem is not null)
        {
            packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", matchedItem.ExecutionUnit);
            if (Directory.Exists(packetDirectory))
            {
                packetFiles = Directory.EnumerateFiles(packetDirectory)
                    .Select(file => Path.GetFileName(file))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                var reviewContextPath = Path.Combine(packetDirectory, "review-context.md");
                if (File.Exists(reviewContextPath))
                {
                    reviewContextHead = string.Join('\n', File.ReadAllLines(reviewContextPath).Take(ReviewContextHeadLines));
                }
            }
            else
            {
                gaps.Add($"packet directory not found: {packetDirectory}");
            }
        }

        var result = new GuideReviewResult
        {
            Domain = domain,
            Repo = repo!,
            Pr = pr!.Value,
            QueueStatePath = queueStatePath,
            ExecutionUnit = matchedItem?.ExecutionUnit,
            QueueItemTitle = matchedItem?.Title,
            QueueItemState = matchedItem?.State.ToString().ToLowerInvariant(),
            PacketDirectory = packetDirectory,
            PacketFiles = packetFiles,
            ReviewContextHead = reviewContextHead,
            ReviewChecklist = ReviewChecklist,
            ReviewBoundaries = ReviewBoundaries,
            ValidationSuggestions = DefaultValidationSuggestions,
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

    private static bool MatchesLinkedPr(string? linkedPr, string repo, string prToken)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        if (string.Equals(linkedPr, prToken, StringComparison.Ordinal))
        {
            return true;
        }

        if (linkedPr!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return linkedPr.StartsWith($"https://github.com/{repo}/pull/", StringComparison.OrdinalIgnoreCase)
                && linkedPr.EndsWith($"/{prToken}", StringComparison.Ordinal);
        }

        return linkedPr!.EndsWith($"/{prToken}", StringComparison.Ordinal);
    }

    private static void WriteMarkdown(TextWriter writer, GuideReviewResult result)
    {
        writer.WriteLine($"# Guide review — {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- execution unit: {(result.ExecutionUnit ?? "(unresolved)")}");
        if (!string.IsNullOrWhiteSpace(result.QueueItemTitle))
        {
            writer.WriteLine($"- queue item title: {result.QueueItemTitle}");
        }
        if (!string.IsNullOrWhiteSpace(result.QueueItemState))
        {
            writer.WriteLine($"- queue item state: {result.QueueItemState}");
        }
        writer.WriteLine($"- ready: {(result.Ready ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Packet");
        writer.WriteLine($"- packet directory: {(result.PacketDirectory ?? "(unknown)")}");
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
        writer.WriteLine();

        if (!string.IsNullOrWhiteSpace(result.ReviewContextHead))
        {
            writer.WriteLine($"## review-context.md head ({ReviewContextHeadLines} lines)");
            writer.WriteLine();
            writer.WriteLine("```");
            writer.WriteLine(result.ReviewContextHead);
            writer.WriteLine("```");
            writer.WriteLine();
        }

        writer.WriteLine("## Review checklist");
        foreach (var item in result.ReviewChecklist)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Review boundaries");
        foreach (var item in result.ReviewBoundaries)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Validation suggestions");
        foreach (var item in result.ValidationSuggestions)
        {
            writer.WriteLine($"- {item}");
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
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
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
        writer.WriteLine("guide review");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only PR-specific review guidance: execution unit, packet refs, review-context excerpt, deterministic review checklist, boundaries, and validation suggestions.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record GuideReviewResult
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

    [JsonPropertyName("queue_item_title")]
    public string? QueueItemTitle { get; init; }

    [JsonPropertyName("queue_item_state")]
    public string? QueueItemState { get; init; }

    [JsonPropertyName("packet_directory")]
    public string? PacketDirectory { get; init; }

    [JsonPropertyName("packet_files")]
    public required IReadOnlyList<string> PacketFiles { get; init; }

    [JsonPropertyName("review_context_head")]
    public string? ReviewContextHead { get; init; }

    [JsonPropertyName("review_checklist")]
    public required IReadOnlyList<string> ReviewChecklist { get; init; }

    [JsonPropertyName("review_boundaries")]
    public required IReadOnlyList<string> ReviewBoundaries { get; init; }

    [JsonPropertyName("validation_suggestions")]
    public required IReadOnlyList<string> ValidationSuggestions { get; init; }

    [JsonPropertyName("gaps")]
    public required IReadOnlyList<string> Gaps { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }
}
