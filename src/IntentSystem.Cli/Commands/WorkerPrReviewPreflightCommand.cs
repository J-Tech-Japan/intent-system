using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G203: Read-only <c>intent-cli worker pr-review-preflight</c> command.
/// Answers "is this GitHub PR actionable for deterministic review in the
/// current implementation repository right now?" with a single deterministic
/// classification. Never mutates queue state, runs, GitHub labels/PRs, source
/// files, or any on-disk state. Exposes <see cref="PrLookupFactory"/>,
/// <see cref="IssueLookupFactory"/>, and <see cref="NestedProviderLauncher"/>
/// so tests can inject fakes and assert that no nested provider is launched.
/// </summary>
internal static class WorkerPrReviewPreflightCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test seam: tests inject a fake <see cref="IGitHubPrLookup"/> here so no
    /// real GitHub network call is made. Production callers leave this null
    /// and the default <see cref="GhCliGitHubPrLookup"/> is used.
    /// </summary>
    public static Func<IGitHubPrLookup>? PrLookupFactory { get; set; }

    /// <summary>
    /// Test seam for the source-issue lookup. Reuses the G202
    /// <see cref="IGitHubIssueLookup"/> seam so tests inject a fake to return
    /// canned issue data. Production callers leave this null and the default
    /// <see cref="GhCliGitHubIssueLookup"/> is used.
    /// </summary>
    public static Func<IGitHubIssueLookup>? IssueLookupFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked by this command. Tests assert that
    /// it remains uninvoked across all preflight code paths to lock in the
    /// "no nested provider launch" guarantee.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var prNumber, out var workdir, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = string.IsNullOrWhiteSpace(workdir)
            ? Directory.GetCurrentDirectory()
            : workdir!;

        IGitHubPrLookup prLookup;
        try
        {
            prLookup = PrLookupFactory?.Invoke() ?? new GhCliGitHubPrLookup();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub PR lookup: {exception.Message}");
            return 1;
        }

        GitHubPrLookupResult prPayload;
        try
        {
            prPayload = prLookup.Lookup(repo!, prNumber);
        }
        catch (Exception exception)
        {
            writer.WriteLine(
                $"failed to look up GitHub PR {repo}#{prNumber}: {exception.Message}");
            return 1;
        }

        if (prPayload is null)
        {
            writer.WriteLine(
                $"GitHub PR lookup returned no payload for {repo}#{prNumber}.");
            return 1;
        }

        SourceIssueCandidate? candidate;
        try
        {
            candidate = WorkerPrReviewPreflightAnalyzer.TraceSourceIssue(prPayload, repo!);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or FormatException)
        {
            writer.WriteLine(
                $"failed to trace source issue for PR {repo}#{prNumber}: {exception.Message}");
            return 1;
        }

        // G370: split the source-issue lookup failure mode into two
        // contracts so the non-actionable exit-code is stable across
        // CI environments without weakening the fail-closed posture for
        // genuinely OPEN actionable PRs. See
        // <see cref="IsStateLevelNonActionable"/>.
        GitHubIssueLookupResult? sourceIssuePayload = null;
        var preLookupNonActionable = IsStateLevelNonActionable(prPayload);
        if (candidate is { } traced && !preLookupNonActionable)
        {
            IGitHubIssueLookup issueLookup;
            try
            {
                issueLookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubIssueLookup();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine(
                    $"failed to initialize GitHub issue lookup for source issue {traced.Repo}#{traced.Number}: {exception.Message}");
                return 1;
            }

            try
            {
                sourceIssuePayload = issueLookup.Lookup(traced.Repo, traced.Number);
            }
            catch (Exception exception)
            {
                writer.WriteLine(
                    $"failed to look up source issue {traced.Repo}#{traced.Number}: {exception.Message}");
                return 1;
            }
        }

        WorkerPrReviewPreflightResult result;
        try
        {
            result = WorkerPrReviewPreflightAnalyzer.Analyze(
                prPayload,
                repo!,
                prNumber,
                resolvedWorkdir,
                candidate,
                sourceIssuePayload);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or FormatException)
        {
            writer.WriteLine(
                $"failed to classify GitHub PR {repo}#{prNumber}: {exception.Message}");
            return 1;
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

    /// <summary>
    /// G370: returns <c>true</c> when the PR payload alone already
    /// determines non-actionability (closed, merged, or a fresh draft
    /// without any review-cycle label). In those cases the source-issue
    /// lookup is unnecessary -- the analyzer can classify the PR as
    /// non-actionable from the PR payload -- so we skip the lookup
    /// rather than letting a transient `gh` failure flip the exit code.
    /// </summary>
    private static bool IsStateLevelNonActionable(GitHubPrLookupResult pr)
    {
        if (pr.Closed || pr.Merged)
        {
            return true;
        }
        if (string.Equals(pr.State, "CLOSED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pr.State, "MERGED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (pr.IsDraft && !HasReviewCycleLabel(pr))
        {
            return true;
        }
        return false;
    }

    private static bool HasReviewCycleLabel(GitHubPrLookupResult pr)
    {
        // Match the analyzer's `HasAnyReviewLabel` set exactly so the
        // command-layer short-circuit cannot diverge from the
        // classifier's draft gate. Includes `intent-pr-reviewing`
        // because the review-side analyzer treats it as an active
        // review-cycle marker; the comment-side analyzer omits it.
        foreach (var label in pr.Labels)
        {
            if (label.Name is not { Length: > 0 } name)
            {
                continue;
            }
            if (string.Equals(name, WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing, StringComparison.Ordinal)
                || string.Equals(name, WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate, StringComparison.Ordinal)
                || string.Equals(name, WorkerPrReviewPreflightConstants.Labels.IntentPrUpdateInProgress, StringComparison.Ordinal)
                || string.Equals(name, WorkerPrReviewPreflightConstants.Labels.IntentPrApproved, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void WriteText(TextWriter writer, WorkerPrReviewPreflightResult result)
    {
        writer.WriteLine($"# Worker pr-review-preflight for {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- title: {result.Title}");
        writer.WriteLine($"- pr_state: {result.PrState}");
        writer.WriteLine($"- is_draft: {result.IsDraft.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- classification: {result.Classification}");
        writer.WriteLine($"- actionable: {result.Actionable.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- recommended_action: {result.RecommendedAction}");
        writer.WriteLine(
            $"- source_issue: {(result.SourceIssue.HasValue ? result.SourceIssue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(none)")}");
        writer.WriteLine();

        writer.WriteLine("## Labels");
        if (result.Labels.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var label in result.Labels)
            {
                writer.WriteLine($"- {label}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Source issue labels");
        if (result.SourceIssueLabels is null || result.SourceIssueLabels.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var label in result.SourceIssueLabels)
            {
                writer.WriteLine($"- {label}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Reasons");
        if (result.Reasons.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var reason in result.Reasons)
            {
                writer.WriteLine($"- {reason}");
            }
        }
        writer.WriteLine();

        writer.WriteLine(result.SummaryLine);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int prNumber,
        out string? workdir,
        out string format,
        out string error)
    {
        repo = null;
        prNumber = 0;
        workdir = null;
        format = FormatText;
        error = string.Empty;
        var sawPr = false;

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

                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value (PR number).";
                        return false;
                    }
                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out prNumber)
                        || prNumber <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }
                    sawPr = true;
                    index++;
                    break;

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
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

                default:
                    error = $"Unknown argument '{argument}'. Supported: --repo <owner/repo> --pr <number> [--workdir <path>] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        if (!sawPr)
        {
            error = "--pr is required (e.g. --pr 123).";
            return false;
        }

        return true;
    }
}
