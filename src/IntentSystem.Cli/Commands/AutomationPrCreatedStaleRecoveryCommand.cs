using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G836: <c>intent-cli automation pr-created-stale-recovery</c> — bounded
/// recovery for an open intent issue that still carries
/// <c>intent-pr-created</c> after its linked PR was closed without merging.
/// Removes only the stale label when identity-bound evidence proves the case,
/// leaves a write-ahead audit trail in <c>runs.jsonl</c>, and never mutates
/// queue-state, publish.yaml, or claims.
/// </summary>
internal static class AutomationPrCreatedStaleRecoveryCommand
{
    public const string EventStarted = "pr-created-stale-recovery-started";
    public const string EventRecovered = "pr-created-stale-recovered";
    public const string EventAborted = "pr-created-stale-recovery-aborted";
    public const string EventSuperseded = "pr-created-stale-recovery-superseded";
    public const string By = "automation-pr-created-stale-recovery";
    public const int OpenPrListingLimit = 200;

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    private const string UsageLine =
        "Usage: intent-cli automation pr-created-stale-recovery --repo <owner/repo> --issue <n> --execution-unit <unit> --team <team> --ruling <text> [--write] [--format json|markdown]";

    public static Func<IGitHubIssueLookup>? IssueLookupFactory { get; set; }
    public static Func<IGitHubPrLookup>? PrLookupFactory { get; set; }
    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }
    public static Func<IGitHubLabelMutator>? LabelMutatorFactory { get; set; }
    public static Func<string, string, string?, bool, ClaimOwnershipVerification>? ClaimVerifierFactory { get; set; }
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly string[] NamedRecoveryEvents =
    [
        EventStarted,
        EventRecovered,
        EventAborted,
        EventSuperseded,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly Regex LinkedPrUrlPattern = new(
        @"^https://github\.com/(?<repo>[^/]+/[^/]+)/pull/(?<number>\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LinkedIssueUrlPattern = new(
        @"^https://github\.com/(?<repo>[^/]+/[^/]+)/issues/(?<number>\d+)/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingExecutionUnitPattern = new(
        @"^(?:[A-Z][A-Z0-9]*-G?[0-9]+|G[0-9]+)(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(
                args,
                out var repo,
                out var issue,
                out var executionUnit,
                out var team,
                out var ruling,
                out var write,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var mode = write ? ModeWrite : ModeDryRun;
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(ruling))
        {
            return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "ruling-missing",
                null, false, warnings,
                "refusing: --ruling is required and must be non-empty after trimming.");
        }

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "host-state-missing",
                null, false, warnings,
                "refusing: queue-state.json is missing at the host root (host-state-missing).");
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "host-state-missing",
                null, false, warnings,
                $"refusing: queue-state.json could not be read or parsed ({exception.Message}).");
        }

        RecoveryRunLog runLog;
        try
        {
            runLog = ReadRecoveryRunLog(context);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "runs-log-unreadable",
                null, false, warnings,
                $"refusing: runs.jsonl is unreadable ({exception.Message}).");
        }

        var resumeSameKey = false;
        RecoveryKey? resumeKey = null;
        var openStartedEvents = runLog.FindOpenStartedEvents(repo!, executionUnit!, issue!.Value);
        var currentKeyForAmbiguity = TryResolveCurrentKey(queueState, repo!, executionUnit!, issue!.Value);
        var openForCurrentKey = currentKeyForAmbiguity is null
            ? Array.Empty<OpenStartedEvent>()
            : openStartedEvents.Where(entry => RecoveryKey.Matches(entry.Key, currentKeyForAmbiguity.Value)).ToArray();
        if (openForCurrentKey.Length > 1)
        {
            return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "started-ambiguous",
                null, false, warnings,
                "refusing: more than one open pr-created-stale-recovery-started event exists for the current key.");
        }

        if (openStartedEvents.Count > 0)
        {
            IReadOnlyList<string> labelNames;
            try
            {
                labelNames = ReadIssueLabelNames(repo!, issue!.Value);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "issue-unavailable",
                    null, false, warnings,
                    $"refusing: could not read issue labels ({exception.Message}).");
            }

            if (!labelNames.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
            {
                if (openStartedEvents.Select(entry => entry.Key).DistinctBy(RecoveryKeyDistinctText).Count() > 1)
                {
                    return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "started-ambiguous",
                        null, false, warnings,
                        "refusing: label absent with open started events for more than one distinct PR key.");
                }

                return HandleCompletionPath(
                    writer,
                    context,
                    format,
                    repo!,
                    issue!.Value,
                    executionUnit!,
                    team!,
                    ruling!,
                    write,
                    openStartedEvents[0],
                    warnings);
            }

            var sameKeyStarted = currentKeyForAmbiguity is null
                ? null
                : openStartedEvents.FirstOrDefault(entry =>
                    RecoveryKey.Matches(entry.Key, currentKeyForAmbiguity.Value));
            if (sameKeyStarted is not null)
            {
                resumeSameKey = true;
                resumeKey = currentKeyForAmbiguity;
            }
            else
            {
                warnings.Add("superseded_started_event: an open started event exists for a different PR than the current linked_pr; the earlier run will be superseded on --write.");
            }
        }

        GitHubIssueLookupResult issueSnapshot;
        try
        {
            issueSnapshot = LookupIssue(repo!, issue!.Value);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "issue-unavailable",
                $"refusing: issue lookup failed ({exception.Message}).");
        }

        if (!IsOpen(issueSnapshot.State))
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "issue-not-open",
                "refusing: issue is not OPEN.");
        }

        var labelSet = LabelNames(issueSnapshot.Labels);
        if (!labelSet.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "target-absent",
                "refusing: issue does not carry intent-target.");
        }

        var queueMatches = queueState.Items
            .Where(item => MatchesLinkedIssue(item.LinkedIssue, repo!, issue!.Value))
            .ToList();
        if (queueMatches.Count == 0)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "queue-item-missing",
                "refusing: no queue item matches the requested issue.");
        }
        if (queueMatches.Count > 1)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "queue-item-ambiguous",
                "refusing: more than one queue item matches the requested issue.");
        }

        var queueItem = queueMatches[0];
        var titleUnit = ParseTitleExecutionUnit(issueSnapshot.Title);
        if (!UnitsAgree(queueItem.ExecutionUnit, executionUnit!, titleUnit))
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "unit-mismatch",
                "refusing: execution unit does not match the queue item, --execution-unit, and issue title token.");
        }

        var publishArtifact = TryReadPublishArtifact(context, executionUnit!);
        var createdIssueUrlRefusal = EvaluateCreatedIssueUrlBinding(publishArtifact, repo!, issue!.Value);
        if (createdIssueUrlRefusal is not null)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, createdIssueUrlRefusal,
                "refusing: publish.yaml created_issue_url does not match --repo and --issue.");
        }

        if (!TryParseLinkedPr(queueItem.LinkedPr, out var linkedPrRepo, out var linkedPrNumber))
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "pr-linkage-missing",
                "refusing: queue-state linked_pr is missing or unparseable.");
        }

        if (!AutomationPublishLifecycleRepairCommand.RepositoryEquals(linkedPrRepo!, repo!))
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "repo-mismatch",
                $"refusing: queue-state linked_pr is in '{linkedPrRepo}', not --repo '{repo}'.");
        }

        if (publishArtifact?.LinkedPrNumber is { } publishPr
            && publishPr != linkedPrNumber)
        {
            warnings.Add("publish_yaml_linked_pr_differs: publish.yaml linked_pr_number differs from queue-state linked_pr; acting on queue-state only.");
        }

        GitHubPrLookupResult prState;
        try
        {
            prState = LookupPr(repo!, linkedPrNumber);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "pr-state-unavailable",
                $"refusing: PR state lookup failed ({exception.Message}).");
        }

        var prRefusal = EvaluateClosedUnmergedPrState(prState);
        if (prRefusal is not null)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, prRefusal, "refusing: linked PR is not closed unmerged.");
        }

        NoteOpenClosingPrListingLimit(warnings);
        try
        {
            if (HasOpenClosingPr(repo!, issue!.Value))
            {
                return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                    resumeSameKey, resumeKey, ruling!, warnings, "open-closing-pr",
                    $"refusing: an OPEN PR closes this issue (checked at most the first {OpenPrListingLimit} OPEN pull requests).");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "open-pr-list-unavailable",
                $"refusing: open PR listing failed ({exception.Message}).");
        }

        var claim = VerifyClaim(context.RepoRoot, executionUnit!, team!);
        var claimRefusal = EvaluateClaimStatus(claim);
        if (claimRefusal is not null)
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, claimRefusal,
                $"refusing: claim status '{claim.Status}' blocks recovery.");
        }

        if (labelSet.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal))
        {
            return MaybeAbortSameKeyResume(writer, context, format, repo!, issue!.Value, executionUnit!, team!, mode,
                resumeSameKey, resumeKey, ruling!, warnings, "in-progress-present",
                "refusing: issue carries intent-issue-in-progress.");
        }

        var currentRecoveryKey = new RecoveryKey(repo!, executionUnit!, issue!.Value, linkedPrNumber);
        var label12Outcome = EvaluateLabelRecoveryState(
            runLog,
            currentRecoveryKey,
            labelSet.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal),
            openStartedEvents);
        switch (label12Outcome.Kind)
        {
            case LabelRecoveryOutcomeKind.Refuse:
                return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, label12Outcome.Cause!,
                    linkedPrNumber, false, warnings, label12Outcome.Summary!);
            case LabelRecoveryOutcomeKind.AlreadyRecovered:
                return EmitSuccess(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "already-recovered",
                    linkedPrNumber, false, warnings, label12Outcome.Summary!);
            case LabelRecoveryOutcomeKind.ClosedThenLabelRemoved:
                return EmitSuccess(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "closed-then-label-removed",
                    linkedPrNumber, false, warnings, label12Outcome.Summary!);
            case LabelRecoveryOutcomeKind.AuditOnly:
                if (!write)
                {
                    return EmitSuccess(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "event-completed",
                        linkedPrNumber, false, warnings, label12Outcome.Summary!);
                }

                try
                {
                    AppendRecoveredEvent(
                        context,
                        currentRecoveryKey,
                        ruling!,
                        claim.Status,
                        resumed: true,
                        extraReasonNote: "label absent at completion; removal not observed by this run");
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
                {
                    return Refuse(writer, format, repo!, issue!.Value, executionUnit!, team!, ModeWrite,
                        "recovered-event-append-failed", currentRecoveryKey.PrNumber, false, warnings,
                        $"audit-only completion: intent-pr-created was already absent and no GitHub mutation was made, but the recovered event could not be appended: {exception.Message}. Re-run `intent-cli automation pr-created-stale-recovery --write` to complete the audit trail.");
                }

                return EmitSuccess(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "event-completed",
                    linkedPrNumber, true, warnings,
                    "appended pr-created-stale-recovered with resumed: true; the label was not observed or removed by this run.");
            case LabelRecoveryOutcomeKind.Proceed:
                break;
            default:
                throw new InvalidOperationException("unexpected label recovery outcome");
        }

        var plannedMutations = new List<string>
        {
            write
                ? $"remove only '{WorkerNextActionConstants.Labels.IntentPrCreated}' from issue #{issue}"
                : $"would remove only '{WorkerNextActionConstants.Labels.IntentPrCreated}' from issue #{issue}",
        };
        if (write)
        {
            plannedMutations.Add($"append `{EventStarted}` (or reuse open started) then `{EventRecovered}` to runs.jsonl");
        }

        if (!write)
        {
            return EmitSuccess(writer, format, repo!, issue!.Value, executionUnit!, team!, mode, "proceed",
                linkedPrNumber, false, warnings,
                $"dry-run: would recover stale intent-pr-created for PR #{linkedPrNumber}.",
                plannedMutations);
        }

        return ExecuteWrite(
            writer,
            context,
            format,
            repo!,
            issue!.Value,
            executionUnit!,
            team!,
            ruling!,
            currentRecoveryKey,
            claim.Status,
            runLog,
            warnings,
            plannedMutations,
            resumeSameKey);
    }

    private static int ExecuteWrite(
        TextWriter writer,
        CliContext context,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        string ruling,
        RecoveryKey currentKey,
        string claimStatus,
        RecoveryRunLog runLog,
        List<string> warnings,
        List<string> plannedMutations,
        bool resumeSameKey)
    {
        var openStarted = runLog.FindOpenStartedEvents(repo, executionUnit, issue);
        var supersedeTargets = openStarted
            .Where(entry => !RecoveryKey.Matches(entry.Key, currentKey))
            .ToList();
        foreach (var target in supersedeTargets)
        {
            try
            {
                AppendSupersededEvent(context, target.Key, ruling, claimStatus);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                return FailPostStartedWrite(writer, format, repo, issue, executionUnit, team, currentKey.PrNumber, warnings,
                    "superseded-event-append-failed", $"failed to append superseded event: {exception.Message}");
            }
        }

        var hasOpenForCurrent = openStarted.Any(entry => RecoveryKey.Matches(entry.Key, currentKey));
        if (!hasOpenForCurrent)
        {
            try
            {
                AppendStartedEvent(context, currentKey, ruling, claimStatus);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                return FailPostStartedWrite(writer, format, repo, issue, executionUnit, team, currentKey.PrNumber, warnings,
                    "started-event-append-failed", $"failed to append started event: {exception.Message}");
            }
        }

        GitHubIssueLookupResult issueSnapshot;
        try
        {
            issueSnapshot = LookupIssue(repo, issue);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "issue-unavailable", $"re-check failed closed: {exception.Message}");
        }

        if (!IsOpen(issueSnapshot.State))
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "issue-not-open", "re-check refused: issue is no longer OPEN.");
        }

        var labels = LabelNames(issueSnapshot.Labels);
        if (!labels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "target-absent", "re-check refused: intent-target is missing.");
        }
        if (labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal))
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "in-progress-present", "re-check refused: intent-issue-in-progress is present.");
        }
        if (!labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "label-changed", "re-check refused: intent-pr-created is already absent.");
        }

        var claim = VerifyClaim(context.RepoRoot, executionUnit, team);
        var claimRefusal = EvaluateClaimStatus(claim);
        if (claimRefusal is not null)
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                claimRefusal, $"re-check refused: claim status '{claim.Status}'.");
        }

        GitHubPrLookupResult prState;
        try
        {
            prState = LookupPr(repo, currentKey.PrNumber);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "pr-state-unavailable", $"re-check failed closed: {exception.Message}");
        }

        var prRefusal = EvaluateClosedUnmergedPrState(prState);
        if (prRefusal is not null)
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                prRefusal, "re-check refused: linked PR state changed.");
        }

        NoteOpenClosingPrListingLimit(warnings);
        try
        {
            if (HasOpenClosingPr(repo, issue))
            {
                return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                    "open-closing-pr",
                    $"re-check refused: an OPEN PR closes this issue (checked at most the first {OpenPrListingLimit} OPEN pull requests).");
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, currentKey, ruling, claimStatus, warnings,
                "open-pr-list-unavailable", $"re-check failed closed: {exception.Message}");
        }

        var mutator = LabelMutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
        try
        {
            mutator.ApplyLabelTransitions(
                repo,
                GhCliGitHubLabelMutator.Kinds.Issue,
                issue,
                Array.Empty<string>(),
                new[] { WorkerNextActionConstants.Labels.IntentPrCreated });
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return FailPostStartedWrite(writer, format, repo, issue, executionUnit, team, currentKey.PrNumber, warnings,
                "label-removal-failed", $"label removal failed: {exception.Message}");
        }

        IReadOnlyList<GitHubAutomationLabel> readBack;
        try
        {
            readBack = mutator.ReadLabels(repo, GhCliGitHubLabelMutator.Kinds.Issue, issue);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return FailPostStartedWrite(writer, format, repo, issue, executionUnit, team, currentKey.PrNumber, warnings,
                "label-readback-failed", $"label readback failed: {exception.Message}");
        }

        if (readBack.Any(label => string.Equals(label.Name, WorkerNextActionConstants.Labels.IntentPrCreated, StringComparison.Ordinal)))
        {
            return FailPostStartedWrite(writer, format, repo, issue, executionUnit, team, currentKey.PrNumber, warnings,
                "label-readback-unconfirmed",
                "label readback unconfirmed: intent-pr-created is still present after removal.");
        }

        try
        {
            AppendRecoveredEvent(context, currentKey, ruling, claimStatus, resumed: resumeSameKey, extraReasonNote: null);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            return FailPostStartedWrite(writer, format, repo, issue, executionUnit, team, currentKey.PrNumber, warnings,
                "recovered-event-append-failed",
                $"label removed but recovered event append failed: {exception.Message}. "
                + "Re-run `intent-cli automation pr-created-stale-recovery --write` to complete the audit trail.");
        }

        return EmitSuccess(writer, format, repo, issue, executionUnit, team, ModeWrite, "recovered",
            currentKey.PrNumber, true, warnings,
            $"removed intent-pr-created for PR #{currentKey.PrNumber} and appended recovered event.",
            plannedMutations);
    }

    private static int HandleCompletionPath(
        TextWriter writer,
        CliContext context,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        string ruling,
        bool write,
        OpenStartedEvent started,
        List<string> warnings)
    {
        if (!string.Equals(started.Key.Repo, repo, StringComparison.OrdinalIgnoreCase)
            || started.Key.IssueNumber != issue
            || !string.Equals(started.Key.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            return Refuse(writer, format, repo, issue, executionUnit, team, write ? ModeWrite : ModeDryRun,
                started.Key.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase) ? "unit-mismatch" : "repo-mismatch",
                started.Key.PrNumber, false, warnings,
                "refusing: completion path identity does not match --repo, --execution-unit, and --issue.");
        }

        var claimStatus = ExtractClaimStatusFromReason(started.Event.Reason) ?? "unknown";
        warnings.Add("the earlier run may have aborted.");
        if (!write)
        {
            return EmitSuccess(writer, format, repo, issue, executionUnit, team, ModeDryRun, "recovery-completed",
                started.Key.PrNumber, false, warnings,
                "dry-run: would complete the interrupted recovery from the open started event without GitHub mutation.");
        }

        try
        {
            AppendRecoveredEvent(
                context,
                started.Key,
                ruling,
                claimStatus,
                resumed: true,
                extraReasonNote: "label absent at completion; removal not observed by this run");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            return Refuse(writer, format, repo, issue, executionUnit, team, ModeWrite,
                "recovered-event-append-failed", started.Key.PrNumber, false, warnings,
                $"interrupted recovery not completed: no GitHub mutation was made and the started event for PR #{started.Key.PrNumber} stays open, because the recovered event could not be appended: {exception.Message}. Re-run `intent-cli automation pr-created-stale-recovery --write` to complete the audit trail.");
        }

        return EmitSuccess(writer, format, repo, issue, executionUnit, team, ModeWrite, "recovery-completed",
            started.Key.PrNumber, true, warnings,
            "appended recovered event for the open started key; label removal was not observed by this run.");
    }

    private static int MaybeAbortSameKeyResume(
        TextWriter writer,
        CliContext context,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        string mode,
        bool resumeSameKey,
        RecoveryKey? resumeKey,
        string ruling,
        List<string> warnings,
        string cause,
        string summary)
    {
        if (resumeSameKey && string.Equals(mode, ModeWrite, StringComparison.Ordinal) && resumeKey is not null)
        {
            var claimStatus = "unknown";
            try
            {
                var claim = VerifyClaim(context.RepoRoot, executionUnit, team);
                claimStatus = claim.Status;
            }
            catch
            {
                // keep unknown
            }

            return AbortWrite(writer, context, format, repo, issue, executionUnit, team, resumeKey.Value, ruling, claimStatus, warnings, cause, summary);
        }

        return Refuse(writer, format, repo, issue, executionUnit, team, mode, cause, resumeKey?.PrNumber, false, warnings, summary);
    }

    private static int AbortWrite(
        TextWriter writer,
        CliContext context,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        RecoveryKey key,
        string ruling,
        string claimStatus,
        List<string> warnings,
        string cause,
        string summary)
    {
        try
        {
            AppendAbortedEvent(context, key, ruling, claimStatus, $"re-check refusal: {cause}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            var gluedRecheckSummary = GlueRecheckSummaryForAbortedAppendFailure(summary);
            return Refuse(writer, format, repo, issue, executionUnit, team, ModeWrite,
                "aborted-event-append-failed", key.PrNumber, false, warnings,
                $"{gluedRecheckSummary} No GitHub mutation was made, but the aborted event could not be appended: {exception.Message}; the started event for PR #{key.PrNumber} stays open. Re-run `intent-cli automation pr-created-stale-recovery --write` to close it.",
                recheckCause: cause);
        }

        return Refuse(writer, format, repo, issue, executionUnit, team, ModeWrite, cause, key.PrNumber, false, warnings, summary);
    }

    private static string GlueRecheckSummaryForAbortedAppendFailure(string recheckSummary)
    {
        var trimmed = recheckSummary.TrimEnd();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var last = trimmed[^1];
        if (last is not '.' and not '!' and not '?' and not '…')
        {
            trimmed += '.';
        }

        return trimmed;
    }

    private static int FailPostStartedWrite(
        TextWriter writer,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        int linkedPr,
        List<string> warnings,
        string cause,
        string summary) =>
        Refuse(writer, format, repo, issue, executionUnit, team, ModeWrite, cause, linkedPr, false, warnings, summary);

    private static LabelRecoveryOutcome EvaluateLabelRecoveryState(
        RecoveryRunLog runLog,
        RecoveryKey key,
        bool hasPrCreatedLabel,
        IReadOnlyList<OpenStartedEvent> openStartedEvents)
    {
        if (runLog.HasRecoveredForKey(key))
        {
            if (hasPrCreatedLabel)
            {
                return LabelRecoveryOutcome.Refuse(
                    "linkage-stale",
                    "refusing: intent-pr-created returned after recovery. Re-link the new PR through worker complete or automation host-queue-item-recovery, then rerun.");
            }

            return LabelRecoveryOutcome.Success("already-recovered", "already recovered for this key; no mutation needed.");
        }

        if (!hasPrCreatedLabel)
        {
            if (runLog.HasAbortedOrSupersededForKey(key) && !runLog.HasRecoveredForKey(key))
            {
                return LabelRecoveryOutcome.Success(
                    "closed-then-label-removed",
                    "an earlier run was closed without recovered; the label is already absent. No further recovery is needed; a fresh `worker claim --team` can proceed if the current PR was closed unmerged, but not if it merged. If the removal itself needs an audit record, record the ruling in the host because this command does not invent a recovered event.");
            }

            if (!runLog.HasAnyRecoveryEventForKey(key))
            {
                return LabelRecoveryOutcome.AuditOnly(
                    "event-completed",
                    "label already absent with no prior recovery events; audit-only completion is available under --write.");
            }
        }

        return LabelRecoveryOutcome.Proceed();
    }

    private static string? EvaluateClaimStatus(ClaimOwnershipVerification claim)
    {
        if (claim.Status == ClaimOwnershipVerification.StatusUnheld
            || claim.Status == ClaimOwnershipVerification.StatusUnheldAvailable)
        {
            return null;
        }

        if (claim.Status == ClaimOwnershipVerification.StatusOwned
            || claim.Status == ClaimOwnershipVerification.StatusHeldByOtherTeam
            || claim.Status == ClaimOwnershipVerification.StatusTeamRequired)
        {
            return "claim-held";
        }

        return "claim-unavailable";
    }

    private static string? EvaluateClosedUnmergedPrState(GitHubPrLookupResult prState)
    {
        if (!string.Equals(prState.State, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(prState.State, "OPEN", StringComparison.OrdinalIgnoreCase) ? "pr-open" : "pr-merged";
        }

        if (!string.IsNullOrWhiteSpace(prState.MergedAt)
            || prState.Merged)
        {
            return "pr-merged";
        }

        return null;
    }

    private static bool HasOpenClosingPr(string repo, int issueNumber)
    {
        var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
        var openPrs = lister.ListPullRequests(repo, Array.Empty<string>());
        if (openPrs.Count > OpenPrListingLimit)
        {
            openPrs = openPrs.Take(OpenPrListingLimit).ToList();
        }

        return openPrs.Any(pr => IsOpen(pr.State) && HasClosingReference(pr, issueNumber, repo));
    }

    private static bool HasClosingReference(GitHubAutomationPrCandidate pr, int issueNumber, string repo)
    {
        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number != issueNumber)
            {
                continue;
            }

            if (reference.Repository is not { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
            {
                return true;
            }

            var candidateRepo = $"{repository.Owner!.Login}/{repository.Name}";
            if (string.Equals(candidateRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static RecoveryRunLog ReadRecoveryRunLog(CliContext context)
    {
        var runsPath = context.GetRunLogPath();
        if (!File.Exists(runsPath))
        {
            return new RecoveryRunLog(Array.Empty<RunEvent>());
        }

        var text = File.ReadAllText(runsPath);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RecoveryRunLog(Array.Empty<RunEvent>());
        }

        return new RecoveryRunLog(RunLogSerializer.DeserializeAll(text));
    }

    private static void AppendStartedEvent(CliContext context, RecoveryKey key, string ruling, string claimStatus)
    {
        AppendRunEvent(context, key, EventStarted, BuildReason(ruling, claimStatus, resumed: false, extra: null));
    }

    private static void AppendRecoveredEvent(
        CliContext context,
        RecoveryKey key,
        string ruling,
        string claimStatus,
        bool resumed,
        string? extraReasonNote)
    {
        AppendRunEvent(context, key, EventRecovered, BuildReason(ruling, claimStatus, resumed, extraReasonNote));
    }

    private static void AppendAbortedEvent(CliContext context, RecoveryKey key, string ruling, string claimStatus, string detail)
    {
        AppendRunEvent(context, key, EventAborted, $"{BuildReason(ruling, claimStatus, resumed: false, extra: null)}; {detail}");
    }

    private static void AppendSupersededEvent(CliContext context, RecoveryKey key, string ruling, string claimStatus)
    {
        AppendRunEvent(
            context,
            key,
            EventSuperseded,
            $"{BuildReason(ruling, claimStatus, resumed: false, extra: null)}; outcome of the earlier run unknown");
    }

    private static void AppendRunEvent(CliContext context, RecoveryKey key, string eventName, string reason)
    {
        var runsPath = context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        var runEvent = new RunEvent
        {
            Ts = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            ExecutionUnit = key.ExecutionUnit,
            Event = eventName,
            By = By,
            Repo = key.Repo,
            LinkedIssue = $"https://github.com/{key.Repo}/issues/{key.IssueNumber}",
            LinkedPr = $"https://github.com/{key.Repo}/pull/{key.PrNumber}",
            Pr = key.PrNumber,
            Reason = reason,
        };
        File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");
    }

    private static string BuildReason(string ruling, string claimStatus, bool resumed, string? extra)
    {
        var reason = $"ruling: {ruling.Trim()}; claim: {claimStatus}";
        if (resumed)
        {
            reason += "; resumed: true";
        }

        if (!string.IsNullOrWhiteSpace(extra))
        {
            reason += $"; {extra}";
        }

        return reason;
    }

    private static string? ExtractClaimStatusFromReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        const string prefix = "claim: ";
        var index = reason.IndexOf(prefix, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = index + prefix.Length;
        var end = reason.IndexOf(';', start);
        return end < 0 ? reason[start..].Trim() : reason[start..end].Trim();
    }

    private static string? EvaluateCreatedIssueUrlBinding(IssuePublishArtifact? publishArtifact, string repo, int issue)
    {
        if (publishArtifact?.CreatedIssueUrl is not { } rawUrl || string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        if (!TryParseCreatedIssueUrl(rawUrl, out var urlRepo, out var urlIssue))
        {
            return "repo-mismatch";
        }

        if (!AutomationPublishLifecycleRepairCommand.RepositoryEquals(urlRepo, repo) || urlIssue != issue)
        {
            return "repo-mismatch";
        }

        if (publishArtifact.CreatedIssueNumber is { } createdIssueNumber && createdIssueNumber != urlIssue)
        {
            return "repo-mismatch";
        }

        return null;
    }

    private static bool TryParseCreatedIssueUrl(string url, out string repo, out int issueNumber)
    {
        repo = string.Empty;
        issueNumber = 0;
        var match = LinkedIssueUrlPattern.Match(url.Trim());
        if (!match.Success)
        {
            return false;
        }

        repo = match.Groups["repo"].Value;
        return int.TryParse(match.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out issueNumber)
               && issueNumber > 0;
    }

    private static void NoteOpenClosingPrListingLimit(List<string> warnings)
    {
        const string note =
            "open-closing-PR check considers at most the first 200 OPEN pull requests returned by the repository listing.";
        if (!warnings.Contains(note))
        {
            warnings.Add(note);
        }
    }

    private static string RecoveryKeyDistinctText(RecoveryKey key) =>
        $"{key.Repo.ToLowerInvariant()}|{key.PrNumber}";

    private static IssuePublishArtifact? TryReadPublishArtifact(CliContext context, string executionUnit)
    {
        var path = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit, "publish.yaml");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return IssuePublishArtifactYaml.Deserialize(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static RecoveryKey? TryResolveCurrentKey(QueueState queueState, string repo, string executionUnit, int issue)
    {
        var item = queueState.Items.FirstOrDefault(i => MatchesLinkedIssue(i.LinkedIssue, repo, issue));
        if (item is null || !TryParseLinkedPr(item.LinkedPr, out var linkedRepo, out var prNumber))
        {
            return null;
        }

        if (!AutomationPublishLifecycleRepairCommand.RepositoryEquals(linkedRepo!, repo))
        {
            return null;
        }

        return new RecoveryKey(repo, executionUnit, issue, prNumber);
    }

    private static bool TryParseLinkedPr(string? linkedPr, out string? repo, out int prNumber)
    {
        repo = null;
        prNumber = 0;
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        var match = LinkedPrUrlPattern.Match(linkedPr.Trim());
        if (!match.Success)
        {
            return false;
        }

        repo = match.Groups["repo"].Value;
        return int.TryParse(match.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out prNumber)
               && prNumber > 0;
    }

    private static bool MatchesLinkedIssue(LinkedIssue? linkedIssue, string repo, int issueNumber) =>
        linkedIssue is { Number: { } number } linked
        && number == issueNumber
        && string.Equals(linked.Repo, repo, StringComparison.OrdinalIgnoreCase);

    private static string? ParseTitleExecutionUnit(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = LeadingExecutionUnitPattern.Match(title);
        return match.Success ? match.Value : null;
    }

    private static bool UnitsAgree(string queueUnit, string requestedUnit, string? titleUnit) =>
        string.Equals(queueUnit, requestedUnit, StringComparison.Ordinal)
        && titleUnit is not null
        && string.Equals(titleUnit, requestedUnit, StringComparison.Ordinal);

    private static bool IsOpen(string state) =>
        string.IsNullOrEmpty(state) || string.Equals(state, "OPEN", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ReadIssueLabelNames(string repo, int issue) =>
        LabelNames(LookupIssue(repo, issue).Labels);

    private static IReadOnlyList<string> LabelNames(IReadOnlyList<GitHubIssueLabel> labels)
    {
        if (labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        return labels
            .Where(label => !string.IsNullOrEmpty(label.Name))
            .Select(label => label.Name)
            .ToArray();
    }

    private static ClaimOwnershipVerification VerifyClaim(string repoRoot, string executionUnit, string team)
    {
        var scope = $"execution-unit:{executionUnit}";
        if (ClaimVerifierFactory is not null)
        {
            return ClaimVerifierFactory(repoRoot, scope, team, true);
        }

        return ClaimOwnershipVerifier.Verify(repoRoot, scope, team, allowUnheld: true);
    }

    private static GitHubIssueLookupResult LookupIssue(string repo, int issue) =>
        (IssueLookupFactory?.Invoke() ?? new GhCliGitHubIssueLookup()).Lookup(repo, issue);

    private static GitHubPrLookupResult LookupPr(string repo, int prNumber) =>
        (PrLookupFactory?.Invoke() ?? new GhCliGitHubPrLookup()).Lookup(repo, prNumber);

    private static int Refuse(
        TextWriter writer,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        string mode,
        string cause,
        int? linkedPr,
        bool applied,
        IReadOnlyList<string> warnings,
        string summary,
        string? recheckCause = null) =>
        Emit(writer, format, new AutomationPrCreatedStaleRecoveryResult
        {
            Repo = repo,
            Issue = issue,
            ExecutionUnit = executionUnit,
            Team = team,
            Mode = mode,
            Outcome = "refused",
            Cause = cause,
            LinkedPr = linkedPr,
            Applied = applied,
            Warnings = warnings,
            Summary = summary,
            RecheckCause = recheckCause,
        }, 1);

    private static int EmitSuccess(
        TextWriter writer,
        string format,
        string repo,
        int issue,
        string executionUnit,
        string team,
        string mode,
        string outcome,
        int? linkedPr,
        bool applied,
        IReadOnlyList<string> warnings,
        string summary,
        IReadOnlyList<string>? plannedMutations = null) =>
        Emit(writer, format, new AutomationPrCreatedStaleRecoveryResult
        {
            Repo = repo,
            Issue = issue,
            ExecutionUnit = executionUnit,
            Team = team,
            Mode = mode,
            Outcome = outcome,
            LinkedPr = linkedPr,
            Applied = applied,
            PlannedMutations = plannedMutations ?? Array.Empty<string>(),
            Warnings = warnings,
            Summary = summary,
        }, 0);

    private static int Emit(TextWriter writer, string format, AutomationPrCreatedStaleRecoveryResult result, int exitCode)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            writer.WriteLine($"# automation pr-created-stale-recovery ({result.Repo} #{result.Issue})");
            writer.WriteLine();
            writer.WriteLine($"- execution_unit: {result.ExecutionUnit}");
            writer.WriteLine($"- mode: {result.Mode}");
            writer.WriteLine($"- outcome: {result.Outcome}");
            if (!string.IsNullOrWhiteSpace(result.Cause))
            {
                writer.WriteLine($"- cause: {result.Cause}");
            }
            if (result.LinkedPr is not null)
            {
                writer.WriteLine($"- linked_pr: #{result.LinkedPr}");
            }
            writer.WriteLine($"- applied: {result.Applied.ToString().ToLowerInvariant()}");
            writer.WriteLine($"- summary: {result.Summary}");
            if (result.Warnings.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine("## Warnings");
                foreach (var warning in result.Warnings)
                {
                    writer.WriteLine($"- {warning}");
                }
            }
        }

        return exitCode;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int? issue,
        out string? executionUnit,
        out string? team,
        out string? ruling,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        issue = null;
        executionUnit = null;
        team = null;
        ruling = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }
                    repo = args[++index];
                    break;
                case "--issue":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIssue)
                        || parsedIssue <= 0)
                    {
                        error = "--issue requires a positive integer.";
                        return false;
                    }
                    issue = parsedIssue;
                    index++;
                    break;
                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }
                    executionUnit = args[++index];
                    break;
                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a non-empty value.";
                        return false;
                    }
                    team = args[++index];
                    break;
                case "--ruling":
                    if (index + 1 >= args.Length)
                    {
                        error = "--ruling requires a value.";
                        return false;
                    }
                    ruling = args[++index];
                    break;
                case "--write":
                    write = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length)
                    {
                        error = "--format requires a value.";
                        return false;
                    }
                    format = args[++index];
                    if (!string.Equals(format, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = "--format must be json or markdown.";
                        return false;
                    }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }
        if (issue is null or <= 0)
        {
            error = "--issue is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(team))
        {
            error = "--team is required.";
            return false;
        }
        if (ruling is null)
        {
            error = "--ruling is required.";
            return false;
        }

        return true;
    }

    internal readonly record struct RecoveryKey(string Repo, string ExecutionUnit, int IssueNumber, int PrNumber)
    {
        public static bool Matches(RecoveryKey left, RecoveryKey right) =>
            string.Equals(left.Repo, right.Repo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.ExecutionUnit, right.ExecutionUnit, StringComparison.Ordinal)
            && left.IssueNumber == right.IssueNumber
            && left.PrNumber == right.PrNumber;
    }

    internal sealed record OpenStartedEvent(RecoveryKey Key, RunEvent Event);

    private sealed class RecoveryRunLog
    {
        private readonly IReadOnlyList<RunEvent> events;

        public RecoveryRunLog(IReadOnlyList<RunEvent> events) => this.events = events;

        public IReadOnlyList<OpenStartedEvent> FindOpenStartedEvents(string repo, string executionUnit, int issueNumber)
        {
            var openByKey = new Dictionary<string, List<OpenStartedEvent>>(StringComparer.Ordinal);
            foreach (var runEvent in events)
            {
                if (!IsNamedRecoveryEvent(runEvent.Event) || !TryExtractKey(runEvent, out var key))
                {
                    continue;
                }

                if (!string.Equals(key.Repo, repo, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(key.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                    || key.IssueNumber != issueNumber)
                {
                    continue;
                }

                var keyText = KeyText(key);
                if (string.Equals(runEvent.Event, EventStarted, StringComparison.Ordinal))
                {
                    if (!openByKey.TryGetValue(keyText, out var openStarted))
                    {
                        openStarted = new List<OpenStartedEvent>();
                        openByKey[keyText] = openStarted;
                    }

                    openStarted.Add(new OpenStartedEvent(key, runEvent));
                }
                else if (string.Equals(runEvent.Event, EventRecovered, StringComparison.Ordinal)
                         || string.Equals(runEvent.Event, EventAborted, StringComparison.Ordinal)
                         || string.Equals(runEvent.Event, EventSuperseded, StringComparison.Ordinal))
                {
                    if (openByKey.TryGetValue(keyText, out var openStarted) && openStarted.Count > 0)
                    {
                        openStarted.RemoveAt(0);
                    }
                }
            }

            return openByKey.Values.SelectMany(openStarted => openStarted).ToList();
        }

        public bool HasRecoveredForKey(RecoveryKey key) =>
            events.Any(runEvent =>
                string.Equals(runEvent.Event, EventRecovered, StringComparison.Ordinal)
                && KeysMatch(runEvent, key));

        public bool HasAbortedOrSupersededForKey(RecoveryKey key) =>
            events.Any(runEvent =>
                (string.Equals(runEvent.Event, EventAborted, StringComparison.Ordinal)
                 || string.Equals(runEvent.Event, EventSuperseded, StringComparison.Ordinal))
                && KeysMatch(runEvent, key));

        public bool HasStartedForKey(RecoveryKey key) =>
            events.Any(runEvent =>
                string.Equals(runEvent.Event, EventStarted, StringComparison.Ordinal)
                && KeysMatch(runEvent, key));

        public bool HasAnyRecoveryEventForKey(RecoveryKey key) =>
            events.Any(runEvent => IsNamedRecoveryEvent(runEvent.Event) && KeysMatch(runEvent, key));

        private static bool KeysMatch(RunEvent runEvent, RecoveryKey key) =>
            TryExtractKey(runEvent, out var eventKey) && RecoveryKey.Matches(eventKey, key);

        private static bool IsNamedRecoveryEvent(string? eventName) =>
            !string.IsNullOrWhiteSpace(eventName)
            && NamedRecoveryEvents.Contains(eventName, StringComparer.Ordinal);

        private static bool TryExtractKey(RunEvent runEvent, out RecoveryKey key)
        {
            key = default;
            if (string.IsNullOrWhiteSpace(runEvent.Repo)
                || string.IsNullOrWhiteSpace(runEvent.ExecutionUnit))
            {
                return false;
            }

            var issueNumber = 0;
            if (!string.IsNullOrWhiteSpace(runEvent.LinkedIssue))
            {
                var issueMatch = LinkedIssueUrlPattern.Match(runEvent.LinkedIssue.Trim());
                if (issueMatch.Success)
                {
                    int.TryParse(issueMatch.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out issueNumber);
                }
            }

            var prNumber = runEvent.Pr ?? 0;
            if (prNumber <= 0 && !string.IsNullOrWhiteSpace(runEvent.LinkedPr))
            {
                var prMatch = LinkedPrUrlPattern.Match(runEvent.LinkedPr.Trim());
                if (prMatch.Success)
                {
                    int.TryParse(prMatch.Groups["number"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out prNumber);
                }
            }

            if (issueNumber <= 0 || prNumber <= 0)
            {
                return false;
            }

            key = new RecoveryKey(runEvent.Repo, runEvent.ExecutionUnit, issueNumber, prNumber);
            return true;
        }

        private static string KeyText(RecoveryKey key) =>
            // Repositories compare case-insensitively (contract section 2), so a
            // closer recorded with different repo casing still closes its started.
            $"{key.Repo.ToLowerInvariant()}|{key.ExecutionUnit}|{key.IssueNumber}|{key.PrNumber}";
    }

    private enum LabelRecoveryOutcomeKind
    {
        Proceed,
        Refuse,
        AlreadyRecovered,
        ClosedThenLabelRemoved,
        AuditOnly,
    }

    private readonly record struct LabelRecoveryOutcome(LabelRecoveryOutcomeKind Kind, string? Cause, string? Summary)
    {
        public static LabelRecoveryOutcome Proceed() => new(LabelRecoveryOutcomeKind.Proceed, null, null);
        public static LabelRecoveryOutcome Refuse(string cause, string summary) => new(LabelRecoveryOutcomeKind.Refuse, cause, summary);
        public static LabelRecoveryOutcome Success(string outcome, string summary) =>
            outcome switch
            {
                "already-recovered" => new(LabelRecoveryOutcomeKind.AlreadyRecovered, null, summary),
                "closed-then-label-removed" => new(LabelRecoveryOutcomeKind.ClosedThenLabelRemoved, null, summary),
                _ => new(LabelRecoveryOutcomeKind.AuditOnly, null, summary),
            };
        public static LabelRecoveryOutcome AuditOnly(string outcome, string summary) =>
            new(LabelRecoveryOutcomeKind.AuditOnly, null, summary);
    }
}
