using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

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

        if (args.Length == 0
            && string.Equals(Environment.GetEnvironmentVariable("INTENT_CLI_SURFACE_PROBE"), "1", StringComparison.Ordinal))
        {
            writer.WriteLine("automation host-review-preflight");
            writer.WriteLine("Usage: intent-cli automation host-review-preflight [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--clarification-required] [--format text|json]");
            return 1;
        }

        if (!TryParseArguments(args, out var repo, out var workdir, out var candidate, out var clarificationRequired, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = WorkdirResolver.Resolve(context, workdir);
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
        // G553: a unit parked in the CONVERGED blocked state cannot progress
        // until it is unblocked, so counting it toward WIP starves publication
        // exactly when the operator deliberately set work aside. Exemptions are
        // computed before the gate and reported in diagnostics — never silent.
        var wipExemptions = ResolveBlockedWipExemptions(
            context, resolvedWorkdir, repo!, intentTargetIssues, out var exemptionWarnings);
        var gatedIntentTargetIssues = wipExemptions.Count == 0
            ? intentTargetIssues
            : intentTargetIssues
                .Where(issue => !wipExemptions.Any(exemption => exemption.Issue == issue.Number))
                .ToArray();

        var result = Analyze(
            repo!,
            reviewCandidatePrs,
            inFlightPrs,
            gatedIntentTargetIssues,
            candidate,
            clarificationRequired,
            wipExemptions,
            exemptionWarnings);

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
        bool clarificationRequired,
        IReadOnlyList<HostReviewWipExemption>? wipExemptBlockedUnits = null,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(reviewCandidatePrs);
        ArgumentNullException.ThrowIfNull(inFlightPrCandidates);
        ArgumentNullException.ThrowIfNull(intentTargetIssues);

        var exemptions = wipExemptBlockedUnits ?? Array.Empty<HostReviewWipExemption>();
        var resolvedWarnings = warnings ?? Array.Empty<string>();

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
                candidateExecutionUnit,
                exemptions,
                resolvedWarnings);
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
                candidateExecutionUnit,
                exemptions,
                resolvedWarnings);
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
                candidateExecutionUnit,
                exemptions,
                resolvedWarnings);
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
                candidateExecutionUnit,
                exemptions,
                resolvedWarnings);
        }

        return BuildResult(
            repo,
            "no-actionable-item",
            "no review PR, no child WIP, and no next-slice candidate was provided",
            null,
            null,
            inFlightPrs,
            inFlightIssues,
            candidateExecutionUnit,
            exemptions,
            resolvedWarnings);
    }

    private static AutomationHostReviewPreflightResult BuildResult(
        string repo,
        string action,
        string reason,
        int? targetPr,
        string? targetPrUrl,
        IReadOnlyList<int> inFlightPrs,
        IReadOnlyList<int> inFlightIssues,
        string? candidateExecutionUnit,
        IReadOnlyList<HostReviewWipExemption> wipExemptBlockedUnits,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Action = action,
            Repo = repo,
            TargetPr = targetPr,
            TargetPrUrl = targetPrUrl,
            InFlightPrs = inFlightPrs,
            InFlightIssues = inFlightIssues,
            WipExemptBlockedUnits = wipExemptBlockedUnits,
            CandidateExecutionUnit = string.IsNullOrWhiteSpace(candidateExecutionUnit) ? null : candidateExecutionUnit,
            Reason = reason,
            Warnings = warnings,
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
            WipExemptBlockedUnits = Array.Empty<HostReviewWipExemption>(),
            CandidateExecutionUnit = null,
            Reason = $"installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for required automation command surfaces; abort before label transitions and refresh the installed CLI instead of falling back to raw gh label mutation",
            Warnings = missing
                .Select(check => $"{check.Command}{(string.IsNullOrWhiteSpace(check.Transition) ? string.Empty : $" --transition {check.Transition}")}: {check.Reason}")
                .ToArray(),
            InstalledCliPath = surfaceReport.InstalledCliPath,
            MissingCommandSurfaces = missing,
        };
    }

    /// <summary>
    /// G553: resolves which OPEN <c>intent-target</c> issues are exempt from the
    /// WIP gate because their queue item is in the CONVERGED blocked state.
    ///
    /// Convergence is G545's two-sided rule and nothing looser: queue
    /// <c>state=blocked</c> AND a non-empty <c>blocked_by</c>. A half-converged
    /// item — blocked without a recorded reason, or a reason without the state —
    /// is DRIFT to be repaired, not a unit to excuse, so it keeps counting
    /// toward WIP. Fail-closed in every uncertain direction: a missing or
    /// unparseable queue-state warns and exempts nothing, leaving the pre-G553
    /// gate behavior byte for byte.
    ///
    /// Linkage is the queue item's own <c>linked_issue</c> (repo + number) —
    /// the canonical record <c>issue publish-flow</c> writes — never a title
    /// guess. An issue this command cannot link to a queue item is simply not
    /// exempt.
    ///
    /// Field finding (sekiban-as-a-service, 2026-07-26, on 0.5.0):
    /// <c>host-review-preflight</c> returned <c>skip-next-slice-due-to-wip</c>
    /// citing issue #1783, whose unit SKS-G818 had been parked through the
    /// supported claim-preserving block transition. G545 exempted blocked units
    /// from <c>claimed-but-silent</c> but not from this gate.
    /// </summary>
    private static IReadOnlyList<HostReviewWipExemption> ResolveBlockedWipExemptions(
        CliContext context,
        string workdir,
        string repo,
        IReadOnlyList<GitHubAutomationIssueCandidate> intentTargetIssues,
        out IReadOnlyList<string> warnings)
    {
        var collectedWarnings = new List<string>();
        warnings = collectedWarnings;

        if (intentTargetIssues.Count == 0)
        {
            return Array.Empty<HostReviewWipExemption>();
        }

        var queueState = TryLoadQueueStateForWipExemption(context, workdir, repo, collectedWarnings);
        if (queueState is null)
        {
            return Array.Empty<HostReviewWipExemption>();
        }

        var openIssueNumbers = intentTargetIssues.Select(issue => issue.Number).ToHashSet();
        var exemptions = new List<HostReviewWipExemption>();

        foreach (var item in queueState.Items)
        {
            if (item.State != QueueItemState.Blocked)
            {
                // REVERSE half-convergence: a recorded blocked_by reason on an
                // item that is not state=blocked. Convergence is two-sided, so
                // this is drift in the other direction — counted, and reported
                // rather than passed over silently, exactly like the forward
                // case below.
                if (item.BlockedBy.Count > 0 && LinksToOpenIssue(item, repo, openIssueNumbers))
                {
                    // Both repairs name the ONE canonical surface that converges
                    // queue-state and the GitHub label together, using the
                    // linkage this item already carries. A non-blocking `queue
                    // transition` is deliberately NOT offered as a clear: it
                    // preserves BlockedBy (QueueManager only rewrites the field
                    // when a reason is supplied), so it changes the state and
                    // leaves the drift exactly where it was.
                    var issueNumber = item.LinkedIssue!.Number!.Value
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                    collectedWarnings.Add(
                        $"queue item `{item.ExecutionUnit}` records blocked_by "
                        + $"({string.Join("; ", item.BlockedBy)}) but its state is `{FormatQueueState(item.State)}`, "
                        + "not `blocked`; half-converged items still count toward WIP — converge it with "
                        + $"`intent-cli automation issue-block {item.ExecutionUnit} --repo {repo} --issue {issueNumber} "
                        + $"--reason \"{string.Join("; ", item.BlockedBy)}\" --write`, or clear the stale reason with "
                        + $"`intent-cli automation issue-block {item.ExecutionUnit} --repo {repo} --issue {issueNumber} "
                        + "--clear --write`.");
                }

                continue;
            }

            if (item.BlockedBy.Count == 0)
            {
                // FORWARD half-convergence: blocked state with no recorded
                // reason. G545 calls this drift; the fail-closed posture is to
                // keep counting it, and to say so rather than exempt it quietly.
                if (LinksToOpenIssue(item, repo, openIssueNumbers))
                {
                    var issueNumber = item.LinkedIssue!.Number!.Value
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                    collectedWarnings.Add(
                        $"queue item `{item.ExecutionUnit}` is state=blocked with an empty blocked_by; "
                        + "half-converged items still count toward WIP — record the reason with "
                        + $"`intent-cli automation issue-block {item.ExecutionUnit} --repo {repo} --issue {issueNumber} "
                        + $"--reason \"<why>\" --write`, or release the unit with "
                        + $"`intent-cli automation issue-block {item.ExecutionUnit} --repo {repo} --issue {issueNumber} "
                        + "--clear --write`.");
                }

                continue;
            }

            if (item.LinkedIssue is not { Number: { } linkedNumber } linkedIssue)
            {
                continue;
            }

            // G553 repair: canonical linkage is repo AND number. A blank or
            // missing repo is NOT a wildcard — issue numbers are only unique
            // within a repository, so a same-numbered issue in another repo
            // would otherwise be excused by an unattributed queue item. Skip
            // the exemption and say why.
            if (string.IsNullOrWhiteSpace(linkedIssue.Repo))
            {
                if (openIssueNumbers.Contains(linkedNumber))
                {
                    collectedWarnings.Add(
                        $"queue item `{item.ExecutionUnit}` is converged-blocked but its linked_issue records no repo "
                        + $"(number {linkedNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)} only); "
                        + "an issue number alone is not canonical linkage, so the WIP exemption was skipped and the "
                        + "issue still counts. Repair the linkage by re-running the canonical publish/closeout surface "
                        + "that records `linked_issue.repo`.");
                }

                continue;
            }

            if (!string.Equals(linkedIssue.Repo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!openIssueNumbers.Contains(linkedNumber))
            {
                continue;
            }

            exemptions.Add(new HostReviewWipExemption
            {
                ExecutionUnit = item.ExecutionUnit,
                Issue = linkedNumber,
                BlockedBy = item.BlockedBy,
            });
        }

        return exemptions
            .OrderBy(exemption => exemption.Issue)
            .ToArray();
    }

    /// <summary>
    /// G553 repair: a half-converged item is only worth reporting when it
    /// actually bears on THIS repo's gate — i.e. its linked issue is one of the
    /// open <c>intent-target</c> issues being counted. Diagnostics about
    /// unrelated queue rows would be noise, not visibility.
    /// </summary>
    private static bool LinksToOpenIssue(QueueItem item, string repo, IReadOnlySet<int> openIssueNumbers) =>
        item.LinkedIssue is { Number: { } number }
        && !string.IsNullOrWhiteSpace(item.LinkedIssue.Repo)
        && string.Equals(item.LinkedIssue.Repo, repo, StringComparison.OrdinalIgnoreCase)
        && openIssueNumbers.Contains(number);

    private static string FormatQueueState(QueueItemState state) =>
        state.ToString().ToLowerInvariant();

    /// <summary>
    /// G553: tolerant queue-state read for the WIP exemption, mirroring G545's
    /// own convention — a missing file is silent (nothing to exempt), an
    /// unparseable one warns and exempts nothing. Neither is ever fatal to the
    /// preflight, which must keep answering even when host state is unreadable.
    /// </summary>
    private static QueueState? TryLoadQueueStateForWipExemption(
        CliContext context,
        string workdir,
        string repo,
        List<string> warnings)
    {
        var location = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            workdir,
            context.Config.Project.Domain ?? string.Empty,
            repo);

        if (!File.Exists(location.Path))
        {
            return null;
        }

        try
        {
            return QueueStateSerializer.Deserialize(File.ReadAllText(location.Path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            warnings.Add(
                $"queue-state at '{location.Path}' could not be parsed: {exception.Message}; "
                + "the blocked-unit WIP exemption was skipped and every open intent-target issue still counts.");
            return null;
        }
    }

    private static bool IsReadyForHostReview(GitHubAutomationPrCandidate pr)
    {
        var labels = LabelNames(pr.Labels);
        var hasBlockingState =
            labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate, StringComparer.Ordinal)
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
        foreach (var exemption in result.WipExemptBlockedUnits)
        {
            writer.WriteLine(
                $"- wip_exempt_blocked_unit: {exemption.ExecutionUnit} (issue #{exemption.Issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}; blocked_by: {string.Join("; ", exemption.BlockedBy)})");
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

/// <summary>
/// G553: one unit excluded from the WIP gate because its queue item is in the
/// converged blocked state, reported with the reason that parked it so the
/// exemption is auditable from the preflight output alone.
/// </summary>
internal sealed record HostReviewWipExemption
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("executionUnit")]
    public string ExecutionUnitCamel => ExecutionUnit;

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("blocked_by")]
    public required IReadOnlyList<string> BlockedBy { get; init; }

    [JsonPropertyName("blockedBy")]
    public IReadOnlyList<string> BlockedByCamel => BlockedBy;
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

    /// <summary>
    /// G553: units excluded from <see cref="InFlightIssues"/> because their
    /// queue item is in the CONVERGED blocked state (queue <c>state=blocked</c>
    /// AND non-empty <c>blocked_by</c>). Reported so the exemption is always
    /// visible — a WIP gate that silently stopped counting something would be
    /// its own failure mode.
    /// </summary>
    [JsonPropertyName("wip_exempt_blocked_units")]
    public required IReadOnlyList<HostReviewWipExemption> WipExemptBlockedUnits { get; init; }

    [JsonPropertyName("wipExemptBlockedUnits")]
    public IReadOnlyList<HostReviewWipExemption> WipExemptBlockedUnitsCamel => WipExemptBlockedUnits;

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
