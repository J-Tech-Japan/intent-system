using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G205: Read-only <c>intent-cli worker result-summary</c> command. Normalizes
/// the outcome of an external Claude/Codex worker run (issue-to-PR, PR comment
/// fix) into a small machine-readable summary that automation loops can use
/// for logging, label cleanup decisions, and next-action selection.
///
/// No-mutation invariants (verified by tests):
/// - never invokes <c>NestedProviderLauncher</c>;
/// - never calls <c>Process.Start</c>, <c>gh</c>, or any GitHub network path;
/// - never creates branches/PRs/comments;
/// - never resolves review threads / merges / closes;
/// - never edits <c>.intent-cli</c> queue or runs state.
///
/// The command is a pure function of its CLI flags into the result emitted by
/// <see cref="WorkerResultSummaryAnalyzer"/>.
/// </summary>
internal static class WorkerResultSummaryCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test sentinel: must NEVER be invoked by this command. Tests assert
    /// it remains uninvoked across all command paths to lock in the
    /// "no nested provider launch" guarantee.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

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

        if (!TryParseArguments(args,
                out var kind,
                out var repo,
                out var issue,
                out var pr,
                out var outcome,
                out var prDraft,
                out var prBody,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        WorkerResultSummaryResult result;
        try
        {
            result = WorkerResultSummaryAnalyzer.Analyze(
                kind!,
                outcome!,
                repo!,
                issue,
                pr,
                prDraft);
        }
        catch (ArgumentException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        // G311: when the caller supplied the PR body, run the closing-
        // reference analyzer and surface a hard warning when the source
        // issue is not deterministically closed by the PR. result-summary
        // remains pure (no GitHub fetch); it only annotates the verdict
        // when the caller pre-fetched the body. The block at `worker
        // complete` is the actual write gate.
        if (prBody is not null
            && string.Equals(kind, WorkerResultSummaryConstants.Kinds.IssueToPr, StringComparison.Ordinal)
            && string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.PrCreated, StringComparison.Ordinal)
            && issue is { } sourceIssueNumber)
        {
            var closingRef = PrClosingReferenceAnalyzer.Analyze(
                prBody,
                sourceIssueNumber: sourceIssueNumber,
                repo: repo!);
            if (!closingRef.Ok)
            {
                var augmentedWarnings = result.Warnings.ToList();
                augmentedWarnings.Add($"closing-reference (G311): {closingRef.Summary}");
                foreach (var step in closingRef.Remediation)
                {
                    augmentedWarnings.Add($"closing-reference (G311) remediation: {step}");
                }
                result = result with { Warnings = augmentedWarnings };
            }
        }

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

    private static void WriteText(TextWriter writer, WorkerResultSummaryResult result)
    {
        writer.WriteLine($"# Worker result-summary for {result.Repo} ({result.Kind})");
        writer.WriteLine();
        writer.WriteLine($"- kind: {result.Kind}");
        writer.WriteLine($"- repo: {result.Repo}");
        if (result.Issue is { } issueNum)
        {
            writer.WriteLine($"- issue: {issueNum}");
        }
        if (result.Pr is { } prNum)
        {
            writer.WriteLine($"- pr: {prNum}");
        }
        writer.WriteLine($"- outcome: {result.Outcome}");
        writer.WriteLine($"- status: {result.Status}");
        if (result.PrDraft is { } draft)
        {
            writer.WriteLine($"- pr_draft: {(draft ? "true (draft — host merge will be blocked)" : "false (ready for review)")}");
        }
        writer.WriteLine();

        writer.WriteLine("## Recommended label actions (advisory only)");
        if (result.RecommendedLabelActions.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var action in result.RecommendedLabelActions)
            {
                writer.WriteLine($"- {action.Action} {action.Label} on {action.Target}");
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
        writer.WriteLine();

        writer.WriteLine(result.Summary);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string? repo,
        out int? issue,
        out int? pr,
        out string? outcome,
        out bool? prDraft,
        out string? prBody,
        out string format,
        out string error)
    {
        kind = null;
        repo = null;
        issue = null;
        pr = null;
        outcome = null;
        prDraft = null;
        prBody = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value (issue-to-pr or pr-comment-fix).";
                        return false;
                    }
                    kind = args[index + 1];
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--issue":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--issue requires a value (issue number).";
                        return false;
                    }
                    if (!int.TryParse(args[index + 1],
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var issueNumber)
                        || issueNumber <= 0)
                    {
                        error = $"--issue must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }
                    issue = issueNumber;
                    index++;
                    break;

                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value (PR number).";
                        return false;
                    }
                    if (!int.TryParse(args[index + 1],
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var prNumber)
                        || prNumber <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }
                    pr = prNumber;
                    index++;
                    break;

                case "--outcome":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--outcome requires a value.";
                        return false;
                    }
                    outcome = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;

                case "--pr-body":
                    if (index + 1 >= args.Length)
                    {
                        error = "--pr-body requires a value (PR body text — empty string permitted to assert 'no body').";
                        return false;
                    }
                    prBody = args[index + 1];
                    index++;
                    break;

                case "--pr-body-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr-body-file requires a value (path to a file containing the PR body).";
                        return false;
                    }
                    var bodyFilePath = args[index + 1];
                    try
                    {
                        prBody = File.ReadAllText(bodyFilePath);
                    }
                    catch (Exception exception) when (
                        exception is IOException
                        or UnauthorizedAccessException
                        or FileNotFoundException
                        or DirectoryNotFoundException)
                    {
                        error = $"--pr-body-file '{bodyFilePath}' could not be read: {exception.Message}";
                        return false;
                    }
                    index++;
                    break;

                case "--pr-draft":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr-draft requires a value (true or false).";
                        return false;
                    }
                    var requestedDraft = args[index + 1].Trim().ToLowerInvariant();
                    if (string.Equals(requestedDraft, "true", StringComparison.Ordinal))
                    {
                        prDraft = true;
                    }
                    else if (string.Equals(requestedDraft, "false", StringComparison.Ordinal))
                    {
                        prDraft = false;
                    }
                    else
                    {
                        error = $"--pr-draft must be 'true' or 'false' (got '{args[index + 1]}').";
                        return false;
                    }
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --kind <kind> --repo <owner/repo> --outcome <outcome> [--issue <n>] [--pr <n>] [--pr-draft true|false] [--pr-body <text>|--pr-body-file <path>] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required (e.g. --kind issue-to-pr).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            error = "--outcome is required (e.g. --outcome pr-created).";
            return false;
        }

        return true;
    }
}
