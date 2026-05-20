using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: <c>intent-cli review collect-signals --repo &lt;r&gt;</c>. Host
/// review/design side scan: lists issues and PRs carrying
/// <c>intent-signal-sent</c>, reads their comments to find the latest
/// structured signal marker, and reports the pending set so a host loop
/// can convert each into clarification / packet / metadata-repair work.
/// Items already carrying <c>intent-signal-handled</c> (with the pending
/// marker cleared) drop out of the label query, so they are never
/// reprocessed. Read-only — never mutates GitHub.
/// </summary>
internal static class ReviewCollectSignalsCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>Test seam: inject a fake candidate lister.</summary>
    public static Func<IGitHubAutomationCandidateLister>? ListerFactory { get; set; }

    /// <summary>Test seam: inject a fake comment gateway.</summary>
    public static Func<IGitHubSignalGateway>? GatewayFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        IGitHubSignalGateway gateway;
        try
        {
            lister = ListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            gateway = GatewayFactory?.Invoke() ?? new GhCliGitHubSignalGateway();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub adapters: {exception.Message}");
            return 1;
        }

        var requiredLabels = new[] { WorkerSignalContract.Labels.SignalSent };
        List<SignalCandidateInput> candidates = new();
        try
        {
            foreach (var issue in lister.ListIssues(repo!, requiredLabels))
            {
                candidates.Add(BuildCandidate(
                    gateway,
                    repo!,
                    GhCliGitHubSignalGateway.Kinds.Issue,
                    issue.Number,
                    issue.Title,
                    issue.Url,
                    issue.Labels));
            }

            foreach (var pr in lister.ListPullRequests(repo!, requiredLabels))
            {
                candidates.Add(BuildCandidate(
                    gateway,
                    repo!,
                    GhCliGitHubSignalGateway.Kinds.Pr,
                    pr.Number,
                    pr.Title,
                    pr.Url,
                    pr.Labels));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to collect signals from {repo}: {exception.Message}");
            return 1;
        }

        var result = ReviewCollectSignalsAnalyzer.Analyze(repo!, candidates);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static SignalCandidateInput BuildCandidate(
        IGitHubSignalGateway gateway,
        string repo,
        string target,
        int number,
        string title,
        string url,
        IReadOnlyList<GitHubAutomationLabel> labels)
    {
        var comments = gateway.ListComments(repo, target, number);
        return new SignalCandidateInput
        {
            Target = target,
            Number = number,
            Title = title,
            Url = url,
            Labels = labels.Select(l => l.Name).Where(n => !string.IsNullOrEmpty(n)).ToArray(),
            Comments = comments,
        };
    }

    private static void WriteText(TextWriter writer, ReviewCollectSignalsResult result)
    {
        writer.WriteLine($"# Worker signals pending in {result.Repo}");
        writer.WriteLine();
        writer.WriteLine($"- pending: {result.PendingCount}");
        writer.WriteLine($"- handled (skipped): {result.HandledSkippedCount}");
        writer.WriteLine($"- unmarked (labelled, no marker): {result.UnmarkedCount}");
        writer.WriteLine();

        writer.WriteLine("## Pending signals");
        if (result.PendingSignals.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var signal in result.PendingSignals)
            {
                writer.WriteLine(
                    $"- {signal.SignalKind} on {signal.Target} #{signal.Number}: {signal.Title} ({signal.CommentRef})");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string format,
        out string error)
    {
        repo = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --repo <owner/repo> [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        return true;
    }
}
