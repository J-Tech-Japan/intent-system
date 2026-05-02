using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only selector for the host review loop's next mechanical action.
/// It reports PR review targets, WIP blocks, or a preloaded next-slice
/// candidate without mutating labels, issues, PRs, files, or provider state.
/// </summary>
internal static class AutomationHostReviewPreflightCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly Regex ClosesIssueRegex = new(
        @"(?i)\b(?:close[sd]?|fix(?:es|ed)?|resolve[sd]?)\s+(?:(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+))?#(?<number>\d+)\b",
        RegexOptions.Compiled);

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
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

        if (!TryParseArguments(args, out var repo, out var workdir, out var candidate, out var clarificationRequired, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(context);
        if (!surfaceReport.Available)
        {
            var staleResult = BuildStaleHostCliResult(repo!, surfaceReport);
            if (string.Equals(format, FormatJson, StringComparison.Ordinal))
            {
                writer.WriteLine(JsonSerializer.Serialize(staleResult, JsonOptions));
            }
            else
            {
                WriteText(writer, staleResult);
            }

            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        try
        {
            lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> intentTargetPrs;
        IReadOnlyList<GitHubAutomationPrCandidate> allOpenPrs;
        IReadOnlyList<GitHubAutomationIssueCandidate> intentTargetIssues;
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIntentTargetIssues;
        try
        {
            intentTargetPrs = lister.ListPullRequests(repo!, [WorkerNextActionConstants.Labels.IntentTarget]);
            allOpenPrs = lister.ListPullRequests(repo!, []);
            intentTargetIssues = lister.ListIssues(repo!, [WorkerNextActionConstants.Labels.IntentTarget]);
            publishedIntentTargetIssues = lister.ListIssues(
                repo!,
                [
                    WorkerNextActionConstants.Labels.IntentTarget,
                    WorkerNextActionConstants.Labels.IntentPrCreated
                ]);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to list host review candidates for {repo}: {exception.Message}");
            return 1;
        }

        var eligiblePrimaryPrs = intentTargetPrs
            .Where(IsReadyForHostReview)
            .ToArray();
        var reviewCandidatePrs = eligiblePrimaryPrs.Length > 0
            ? eligiblePrimaryPrs
            : FindIssueLinkedReviewFallbacks(
                repo!,
                allOpenPrs,
                publishedIntentTargetIssues);
        var inFlightPrs = intentTargetPrs
            .Concat(reviewCandidatePrs)
            .GroupBy(pr => pr.Number)
            .Select(group => group.First())
            .ToArray();
        var result = Analyze(repo!, reviewCandidatePrs, inFlightPrs, intentTargetIssues, candidate, clarificationRequired);

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

    internal static AutomationHostReviewPreflightResult Analyze(
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> reviewCandidatePrs,
        IReadOnlyList<GitHubAutomationPrCandidate> inFlightPrCandidates,
        IReadOnlyList<GitHubAutomationIssueCandidate> intentTargetIssues,
        string? candidateExecutionUnit,
        bool clarificationRequired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(reviewCandidatePrs);
        ArgumentNullException.ThrowIfNull(inFlightPrCandidates);
        ArgumentNullException.ThrowIfNull(intentTargetIssues);

        var inFlightPrs = inFlightPrCandidates.Select(pr => pr.Number).Order().ToArray();
        var inFlightIssues = intentTargetIssues.Select(issue => issue.Number).Order().ToArray();

        if (clarificationRequired)
        {
            return BuildResult(
                repo,
                "clarification-required",
                "host review preflight was told a clarification is required",
                null,
                null,
                inFlightPrs,
                inFlightIssues,
                candidateExecutionUnit);
        }

        var reviewPr = reviewCandidatePrs
            .Where(IsReadyForHostReview)
            .OrderBy(GetReviewSortTime, StringComparer.Ordinal)
            .FirstOrDefault();
        if (reviewPr is not null)
        {
            return BuildResult(
                repo,
                "review-pr",
                "oldest updated open review PR with no blocking review-side state",
                reviewPr.Number,
                reviewPr.Url,
                inFlightPrs,
                inFlightIssues,
                candidateExecutionUnit);
        }

        if (inFlightIssues.Length > 0 || inFlightPrs.Length > 0)
        {
            return BuildResult(
                repo,
                "skip-next-slice-due-to-wip",
                "open intent-target issue or PR is still in flight; WIP cap blocks new issue publication",
                null,
                null,
                inFlightPrs,
                inFlightIssues,
                candidateExecutionUnit);
        }

        if (!string.IsNullOrWhiteSpace(candidateExecutionUnit))
        {
            return BuildResult(
                repo,
                "candidate-ready",
                "no child WIP is open and a host next-slice candidate was provided",
                null,
                null,
                inFlightPrs,
                inFlightIssues,
                candidateExecutionUnit);
        }

        return BuildResult(
            repo,
            "no-actionable-item",
            "no review PR, no child WIP, and no next-slice candidate was provided",
            null,
            null,
            inFlightPrs,
            inFlightIssues,
            candidateExecutionUnit);
    }

    private static AutomationHostReviewPreflightResult BuildResult(
        string repo,
        string action,
        string reason,
        int? targetPr,
        string? targetPrUrl,
        IReadOnlyList<int> inFlightPrs,
        IReadOnlyList<int> inFlightIssues,
        string? candidateExecutionUnit) =>
        new()
        {
            Action = action,
            Repo = repo,
            TargetPr = targetPr,
            TargetPrUrl = targetPrUrl,
            InFlightPrs = inFlightPrs,
            InFlightIssues = inFlightIssues,
            CandidateExecutionUnit = string.IsNullOrWhiteSpace(candidateExecutionUnit) ? null : candidateExecutionUnit,
            Reason = reason,
            Warnings = Array.Empty<string>(),
            InstalledCliPath = null,
            MissingCommandSurfaces = Array.Empty<InstalledCliSurfaceCheck>(),
        };

    private static AutomationHostReviewPreflightResult BuildStaleHostCliResult(
        string repo,
        InstalledCliSurfaceReport surfaceReport)
    {
        var missing = surfaceReport.Checks
            .Where(check => !check.Available)
            .ToArray();

        return new AutomationHostReviewPreflightResult
        {
            Action = "stale-host-cli",
            Repo = repo,
            TargetPr = null,
            TargetPrUrl = null,
            InFlightPrs = Array.Empty<int>(),
            InFlightIssues = Array.Empty<int>(),
            CandidateExecutionUnit = null,
            Reason = $"installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for required automation command surfaces; abort before label transitions and refresh the installed CLI instead of falling back to raw gh label mutation",
            Warnings = missing
                .Select(check => $"{check.Command}{(string.IsNullOrWhiteSpace(check.Transition) ? string.Empty : $" --transition {check.Transition}")}: {check.Reason}")
                .ToArray(),
            InstalledCliPath = surfaceReport.InstalledCliPath,
            MissingCommandSurfaces = missing,
        };
    }

    private static bool IsReadyForHostReview(GitHubAutomationPrCandidate pr)
    {
        var labels = LabelNames(pr.Labels);
        var hasBlockingState =
            labels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing, StringComparer.Ordinal)
            || labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal)
            || labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, StringComparer.Ordinal)
            || labels.Contains(WorkerNextActionConstants.Labels.IntentPrApproved, StringComparer.Ordinal);

        return !hasBlockingState;
    }

    private static string GetReviewSortTime(GitHubAutomationPrCandidate pr) =>
        string.IsNullOrWhiteSpace(pr.UpdatedAt) ? pr.CreatedAt : pr.UpdatedAt;

    private static IReadOnlyList<GitHubAutomationPrCandidate> FindIssueLinkedReviewFallbacks(
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> allOpenPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIntentTargetIssues)
    {
        var reviewCandidates = new List<GitHubAutomationPrCandidate>();
        var seenPrNumbers = new HashSet<int>();
        var publishedIssueNumbers = publishedIntentTargetIssues
            .Where(IsPublishedIntentTargetIssue)
            .Select(issue => issue.Number)
            .ToHashSet();

        if (publishedIssueNumbers.Count == 0)
        {
            return reviewCandidates;
        }

        foreach (var pr in allOpenPrs)
        {
            if (!seenPrNumbers.Add(pr.Number))
            {
                continue;
            }

            var labels = LabelNames(pr.Labels);
            if (labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            {
                reviewCandidates.Add(pr);
                continue;
            }

            if (LinksPublishedIntentTargetIssue(pr, repo, publishedIssueNumbers))
            {
                reviewCandidates.Add(pr);
            }
        }

        return reviewCandidates;
    }

    private static bool IsPublishedIntentTargetIssue(GitHubAutomationIssueCandidate issue)
    {
        var labels = LabelNames(issue.Labels);
        return labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal)
            && labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal);
    }

    private static bool LinksPublishedIntentTargetIssue(
        GitHubAutomationPrCandidate pr,
        string repo,
        IReadOnlySet<int> publishedIssueNumbers)
    {
        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number <= 0 || !publishedIssueNumbers.Contains(reference.Number))
            {
                continue;
            }

            var referenceRepo = repo;
            if (reference.Repository is { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
            {
                referenceRepo = $"{repository.Owner.Login}/{repository.Name}";
            }

            if (string.Equals(referenceRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (Match match in ClosesIssueRegex.Matches(pr.Body ?? string.Empty))
        {
            var linkedRepo = match.Groups["repo"].Value;
            if (!string.IsNullOrWhiteSpace(linkedRepo)
                && !string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(
                    match.Groups["number"].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var issueNumber)
                && publishedIssueNumbers.Contains(issueNumber))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyCollection<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        return labels
            .Select(label => label.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToArray();
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out string? candidate,
        out bool clarificationRequired,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        candidate = null;
        clarificationRequired = false;
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
                    repo = args[index + 1].Trim();
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
                case "--candidate":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--candidate requires an execution unit value.";
                        return false;
                    }
                    candidate = args[index + 1].Trim();
                    index++;
                    break;
                case "--clarification-required":
                    clarificationRequired = true;
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--clarification-required] [--format text|json].";
                    return false;
            }
        }

        return true;
    }

    private static string ResolveWorkdir(CliContext context, string? workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
        {
            return context.RepoRoot;
        }

        return Path.IsPathRooted(workdir)
            ? workdir
            : Path.GetFullPath(Path.Combine(context.RepoRoot, workdir));
    }

    private static void WriteText(TextWriter writer, AutomationHostReviewPreflightResult result)
    {
        writer.WriteLine($"# Host review preflight for {result.Repo}");
        writer.WriteLine($"- action: {result.Action}");
        writer.WriteLine($"- reason: {result.Reason}");
        if (result.TargetPr is { } targetPr)
        {
            writer.WriteLine($"- target_pr: {targetPr.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        if (!string.IsNullOrEmpty(result.CandidateExecutionUnit))
        {
            writer.WriteLine($"- candidate_execution_unit: {result.CandidateExecutionUnit}");
        }
        if (!string.IsNullOrEmpty(result.InstalledCliPath))
        {
            writer.WriteLine($"- installed_cli_path: {result.InstalledCliPath}");
        }
        foreach (var warning in result.Warnings)
        {
            writer.WriteLine($"- warning: {warning}");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation host-review-preflight");
        writer.WriteLine("Usage: intent-cli automation host-review-preflight [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--clarification-required] [--format text|json]");
        writer.WriteLine("Checks installed CLI command surfaces before selecting host review-loop work.");
    }
}

internal sealed record AutomationHostReviewPreflightResult
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("target_pr")]
    public required int? TargetPr { get; init; }

    [JsonPropertyName("targetPr")]
    public int? TargetPrCamel => TargetPr;

    [JsonPropertyName("target_pr_url")]
    public required string? TargetPrUrl { get; init; }

    [JsonPropertyName("targetPrUrl")]
    public string? TargetPrUrlCamel => TargetPrUrl;

    [JsonPropertyName("in_flight_prs")]
    public required IReadOnlyList<int> InFlightPrs { get; init; }

    [JsonPropertyName("inFlightPrs")]
    public IReadOnlyList<int> InFlightPrsCamel => InFlightPrs;

    [JsonPropertyName("in_flight_issues")]
    public required IReadOnlyList<int> InFlightIssues { get; init; }

    [JsonPropertyName("inFlightIssues")]
    public IReadOnlyList<int> InFlightIssuesCamel => InFlightIssues;

    [JsonPropertyName("candidate_execution_unit")]
    public required string? CandidateExecutionUnit { get; init; }

    [JsonPropertyName("candidateExecutionUnit")]
    public string? CandidateExecutionUnitCamel => CandidateExecutionUnit;

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("installed_cli_path")]
    public required string? InstalledCliPath { get; init; }

    [JsonPropertyName("installedCliPath")]
    public string? InstalledCliPathCamel => InstalledCliPath;

    [JsonPropertyName("missing_command_surfaces")]
    public required IReadOnlyList<InstalledCliSurfaceCheck> MissingCommandSurfaces { get; init; }

    [JsonPropertyName("missingCommandSurfaces")]
    public IReadOnlyList<InstalledCliSurfaceCheck> MissingCommandSurfacesCamel => MissingCommandSurfaces;
}
