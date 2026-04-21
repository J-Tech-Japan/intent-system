using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunCommand
{
    private sealed record AutoContinueIntakeCandidate(string Domain, string ExecutionUnit);

    private sealed class RunDeterministicGapException(string executionUnit, string message)
        : InvalidOperationException(message)
    {
        public string ExecutionUnit { get; } = executionUnit;
    }

    private sealed record FreshFixContinuationState(
        DateTimeOffset TotalDeadline,
        DateTimeOffset Deadline,
        DateTimeOffset LastObservedActivityAt,
        int RemainingPolls);

    private const int IterationBudget = 128;
    private const string NoActionableItemStopReason = "no-actionable-item";
    private const string ClarificationRequiredStopReason = "clarification-required";
    private const string ParentIntentUpdateRequiredStopReason = "parent-intent-update-required";
    private const string DeterministicContractGapStopReason = "deterministic-contract-gap";
    private const string NonRetryableFailureStopReason = "non-retryable-failure";
    private const string TransitionActor = "intent-cli";

    public static Func<CliContext, string, QueueDispatchCommandResult> QueueDispatchExecutor { get; set; } =
        QueueDispatchCommand.ExecuteCore;

    public static Func<CliContext, string, RunStartResult> RunStartExecutor { get; set; } =
        RunStartCommand.ExecuteCore;

    public static Func<string, string, string, IntakeIssueResult> IntakeIssueExecutor { get; set; } =
        IntakeIssueCommand.ExecuteCore;

    public static Func<CliContext, string, int> QueueEnqueueExecutor { get; set; } =
        ExecuteQueueEnqueue;

    public static Func<CliContext, string, RunImplementResult> RunImplementExecutor { get; set; } =
        RunImplementCommand.ExecuteCore;

    public static Func<CliContext, string, RunFixResult> RunFixExecutor { get; set; } =
        RunFixCommand.ExecuteCore;

    public static Func<CliContext, string, RunSubmitResult> RunSubmitExecutor { get; set; } =
        RunSubmitCommand.ExecuteCore;

    public static Func<CliContext, string, RunResubmitResult> RunResubmitExecutor { get; set; } =
        RunResubmitCommand.ExecuteCore;

    public static Func<CliContext, string, RunRereviewResult> RunRereviewExecutor { get; set; } =
        RunRereviewCommand.ExecuteCore;

    public static Func<CliContext, string, RunSuperviseResult> RunSuperviseExecutor { get; set; } =
        RunSuperviseCommand.ExecuteCore;

    public static Func<CliContext, string, ReviewRunResult> ReviewRunExecutor { get; set; } =
        ReviewRunCommand.ExecuteCore;

    public static Func<CliContext, string, string, ReviewCommentResult> ReviewCommentExecutor { get; set; } =
        ReviewCommentCommand.ExecuteCore;

    public static Func<CliContext, string, ReviewAcceptResult> ReviewAcceptExecutor { get; set; } =
        ReviewAcceptCommand.ExecuteCore;

    public static Func<IGitCommandRunner> GitCommandRunnerFactory { get; set; } =
        () => new GitCommandRunner();

    public static Func<IGitHubCommandRunner> GitHubCommandRunnerFactory { get; set; } =
        () => new GhCommandRunner();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    internal static TimeSpan FreshFixContinuationWindow { get; set; } = TimeSpan.FromSeconds(30);

    internal static TimeSpan FreshFixContinuationActivityWindow { get; set; } = TimeSpan.FromSeconds(45);

    internal static TimeSpan FreshFixContinuationTotalWindow { get; set; } = TimeSpan.FromSeconds(90);

    internal static TimeSpan FreshFixContinuationPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    internal static int FreshFixContinuationMaxPolls { get; set; } = 120;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 0)
        {
            writer.WriteLine("Run command does not take additional arguments.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context);
            result = PersistResultArtifact(context, result);
            RunCommandRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static RunCommandResult ExecuteCore(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var actions = new List<RunCommandAction>();
        var freshImplementContinuationStates = new Dictionary<string, FreshFixContinuationState>(StringComparer.Ordinal);
        var freshFixContinuationStates = new Dictionary<string, FreshFixContinuationState>(StringComparer.Ordinal);

        for (var iteration = 0; iteration < IterationBudget; iteration++)
        {
            try
            {
                var queueState = LoadQueueStateOrThrow(context);
                var inProgressItems = queueState.Items
                    .Where(item => item.State is QueueItemState.Active or QueueItemState.Fixing or QueueItemState.Review)
                    .ToList();

                if (inProgressItems.Count > 1)
                {
                    return CreateStopResult(
                        DeterministicContractGapStopReason,
                        actions,
                        detail: $"Multiple in-progress items detected: {string.Join(", ", inProgressItems.Select(item => item.ExecutionUnit))}.");
                }

                if (inProgressItems.Count == 1)
                {
                    var inProgressItem = inProgressItems[0];
                    if (inProgressItem.State == QueueItemState.Active)
                    {
                        var implementRunStatus = TryReadDirectRunStatus(
                            context,
                            inProgressItem.ExecutionUnit,
                            "implement");
                        if (string.Equals(implementRunStatus, "succeeded", StringComparison.Ordinal))
                        {
                            freshImplementContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            ExecuteAction(
                                context,
                                actions,
                                "run submit",
                                inProgressItem.ExecutionUnit,
                                () => RunSubmitExecutor(context, inProgressItem.ExecutionUnit));
                            continue;
                        }

                        if (!ArtifactExists(context, RunImplementArtifactPathResolver.Resolve(inProgressItem.ExecutionUnit)))
                        {
                            ExecuteAction(
                                context,
                                actions,
                                "run implement",
                                inProgressItem.ExecutionUnit,
                                () => RunImplementExecutor(context, inProgressItem.ExecutionUnit));
                            continue;
                        }

                        var implementRequestArtifact = TryReadDirectRunRequestArtifact(context, inProgressItem.ExecutionUnit);
                        var implementResultArtifact = TryReadDirectRunResultArtifact(
                            context,
                            inProgressItem.ExecutionUnit,
                            "implement");
                        if (ShouldLaunchFreshImplementAttempt(
                                context,
                                inProgressItem.ExecutionUnit,
                                implementRequestArtifact,
                                implementResultArtifact))
                        {
                            ClearActiveQueueBlockedBy(context, inProgressItem.ExecutionUnit);
                            ExecuteAction(
                                context,
                                actions,
                                "run implement",
                                inProgressItem.ExecutionUnit,
                                () => RunImplementExecutor(context, inProgressItem.ExecutionUnit));
                            TrackFreshImplementContinuation(
                                context,
                                inProgressItem.ExecutionUnit,
                                freshImplementContinuationStates);
                            continue;
                        }

                        var superviseResult = ExecuteAction(
                            context,
                            actions,
                            "run supervise",
                            inProgressItem.ExecutionUnit,
                            () => RunSuperviseExecutor(context, inProgressItem.ExecutionUnit));

                        if (ShouldContinueFreshImplementSupervision(
                                context,
                                freshImplementContinuationStates,
                                inProgressItem.ExecutionUnit,
                                superviseResult))
                        {
                            SleepFreshFixContinuationPollInterval();
                            continue;
                        }

                        freshImplementContinuationStates.Remove(inProgressItem.ExecutionUnit);

                        if (superviseResult.Blocked)
                        {
                            if (superviseResult.ReportAsNonRetryableFailure)
                            {
                                if (superviseResult.RequiresPostFixWorktreeProgressDecision)
                                {
                                    continue;
                                }

                                return CreateMonitoringStopResult(actions, inProgressItem.ExecutionUnit, superviseResult);
                            }

                            continue;
                        }

                        return CreateMonitoringStopResult(actions, inProgressItem.ExecutionUnit, superviseResult);
                    }

                    if (inProgressItem.State == QueueItemState.Fixing)
                    {
                        if (!ArtifactExists(context, ReviewCommentArtifactPathResolver.Resolve(inProgressItem.ExecutionUnit)))
                        {
                            return CreateStopResult(
                                DeterministicContractGapStopReason,
                                actions,
                                inProgressItem.ExecutionUnit,
                                $"Fixing item '{inProgressItem.ExecutionUnit}' requires {ReviewCommentArtifactPathResolver.Resolve(inProgressItem.ExecutionUnit)}.");
                        }

                        var hasFixArtifact = ArtifactExists(context, RunFixArtifactPathResolver.Resolve(inProgressItem.ExecutionUnit));
                        if (!hasFixArtifact)
                        {
                            ExecuteAction(
                                context,
                                actions,
                                "run fix",
                                inProgressItem.ExecutionUnit,
                                () => RunFixExecutor(context, inProgressItem.ExecutionUnit));
                            TrackFreshFixContinuation(
                                context,
                                inProgressItem.ExecutionUnit,
                                freshFixContinuationStates);
                            continue;
                        }

                        var currentFixTargetContractGap = TryResolveCurrentFixTargetContractGap(context, inProgressItem);
                        if (!string.IsNullOrWhiteSpace(currentFixTargetContractGap))
                        {
                            freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            return CreateStopResult(
                                DeterministicContractGapStopReason,
                                actions,
                                inProgressItem.ExecutionUnit,
                                currentFixTargetContractGap);
                        }

                        var fixRequestArtifact = TryReadDirectRunRequestArtifact(context, inProgressItem.ExecutionUnit);
                        var fixResultArtifact = TryReadDirectRunResultArtifact(context, inProgressItem.ExecutionUnit, "fix");
                        if (ShouldLaunchFreshFixAttempt(
                                context,
                                inProgressItem.ExecutionUnit,
                                fixRequestArtifact,
                                fixResultArtifact))
                        {
                            ExecuteAction(
                                context,
                                actions,
                                "run fix",
                                inProgressItem.ExecutionUnit,
                                () => RunFixExecutor(context, inProgressItem.ExecutionUnit));
                            TrackFreshFixContinuation(
                                context,
                                inProgressItem.ExecutionUnit,
                                freshFixContinuationStates);
                            continue;
                        }

                        var fixRunStatus = fixResultArtifact?.RunStatus;
                        if (TryReconcileBlockedFixSupervisionState(
                                context,
                                actions,
                                inProgressItem,
                                out var blockedFixResult))
                        {
                            freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            return blockedFixResult;
                        }

                        if (TryReconcileStaleFailedFixResultState(
                                context,
                                actions,
                                inProgressItem,
                                fixRequestArtifact,
                                fixResultArtifact,
                                out var staleFailedFixResult))
                        {
                            freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            return staleFailedFixResult;
                        }

                        var currentFixSessionContractGap = TryResolveCurrentFixSessionContractGap(
                            context,
                            inProgressItem.ExecutionUnit,
                            fixRequestArtifact);
                        if (!string.IsNullOrWhiteSpace(currentFixSessionContractGap))
                        {
                            freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            if (fixResultArtifact is not null
                                && !string.Equals(fixResultArtifact.RunStatus, "failed", StringComparison.Ordinal))
                            {
                                fixResultArtifact = fixResultArtifact with
                                {
                                    RunStatus = "failed"
                                };
                                PersistDirectRunResultArtifact(context, inProgressItem.ExecutionUnit, fixResultArtifact);
                            }

                            return CreateStopResult(
                                DeterministicContractGapStopReason,
                                actions,
                                inProgressItem.ExecutionUnit,
                                currentFixSessionContractGap);
                        }

                        if (string.Equals(fixRunStatus, "succeeded", StringComparison.Ordinal))
                        {
                            freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            ExecuteAction(
                                context,
                                actions,
                                "run resubmit",
                                inProgressItem.ExecutionUnit,
                                () => RunResubmitExecutor(context, inProgressItem.ExecutionUnit));
                            ExecuteAction(
                                context,
                                actions,
                                "run rereview",
                                inProgressItem.ExecutionUnit,
                                () => RunRereviewExecutor(context, inProgressItem.ExecutionUnit));
                            continue;
                        }

                        if (string.Equals(fixRunStatus, "failed", StringComparison.Ordinal))
                        {
                            freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);
                            return CreateStopResult(
                                NonRetryableFailureStopReason,
                                actions,
                                inProgressItem.ExecutionUnit,
                                $"Fix direct run failed for '{inProgressItem.ExecutionUnit}'.");
                        }

                        var superviseResult = ExecuteAction(
                            context,
                            actions,
                            "run supervise",
                            inProgressItem.ExecutionUnit,
                            () => RunSuperviseExecutor(context, inProgressItem.ExecutionUnit));

                        if (ShouldContinueFreshFixSupervision(
                                context,
                                freshFixContinuationStates,
                                inProgressItem.ExecutionUnit,
                                superviseResult))
                        {
                            SleepFreshFixContinuationPollInterval();
                            continue;
                        }

                        freshFixContinuationStates.Remove(inProgressItem.ExecutionUnit);

                        if (superviseResult.Blocked)
                        {
                            if (superviseResult.ReportAsNonRetryableFailure)
                            {
                                if (superviseResult.RequiresPostFixWorktreeProgressDecision)
                                {
                                    continue;
                                }

                                return CreateMonitoringStopResult(actions, inProgressItem.ExecutionUnit, superviseResult);
                            }

                            continue;
                        }

                        return CreateMonitoringStopResult(actions, inProgressItem.ExecutionUnit, superviseResult);
                    }

                    if (ShouldLaunchReviewRun(context, inProgressItem.ExecutionUnit))
                    {
                        ExecuteAction(
                            context,
                            actions,
                            "review run",
                            inProgressItem.ExecutionUnit,
                            () => ReviewRunExecutor(context, inProgressItem.ExecutionUnit));
                    }

                    var reviewDecision = ResolveReviewDecision(context, inProgressItem.ExecutionUnit);
                    if (reviewDecision.Kind == RunReviewDecisionKind.Accept)
                    {
                        ExecuteAction(
                            context,
                            actions,
                            "review accept",
                            inProgressItem.ExecutionUnit,
                            () => ReviewAcceptExecutor(context, inProgressItem.ExecutionUnit));
                        continue;
                    }

                    if (reviewDecision.Kind == RunReviewDecisionKind.Comment)
                    {
                        ExecuteAction(
                            context,
                            actions,
                            "review comment",
                            inProgressItem.ExecutionUnit,
                            () => ReviewCommentExecutor(context, inProgressItem.ExecutionUnit, reviewDecision.CommentBodyPath!));
                        continue;
                    }

                    if (reviewDecision.Kind == RunReviewDecisionKind.Failure)
                    {
                        return CreateStopResult(
                            NonRetryableFailureStopReason,
                            actions,
                            inProgressItem.ExecutionUnit,
                            reviewDecision.Detail);
                    }

                    if (reviewDecision.Kind == RunReviewDecisionKind.ContractGap)
                    {
                        return CreateStopResult(
                            DeterministicContractGapStopReason,
                            actions,
                            inProgressItem.ExecutionUnit,
                            reviewDecision.Detail);
                    }

                    return CreateStopResult(
                        NoActionableItemStopReason,
                        actions,
                        inProgressItem.ExecutionUnit,
                        reviewDecision.Detail);
                }

                var nextQueuedItem = QueueSelection.SelectNext(queueState);
                if (nextQueuedItem is not null)
                {
                    if (nextQueuedItem.LinkedIssue is null)
                    {
                        ExecuteAction(
                            context,
                            actions,
                            "queue dispatch",
                            nextQueuedItem.ExecutionUnit,
                            () => QueueDispatchExecutor(context, nextQueuedItem.ExecutionUnit));
                        continue;
                    }

                    ExecuteAction(
                        context,
                        actions,
                        "run start",
                        nextQueuedItem.ExecutionUnit,
                        () => RunStartExecutor(context, nextQueuedItem.ExecutionUnit));
                    continue;
                }

                var clarifyBlockedItem = queueState.Items.FirstOrDefault(item => item.State == QueueItemState.ClarifyBlocked);
                if (clarifyBlockedItem is not null)
                {
                    return CreateStopResult(
                        ClarificationRequiredStopReason,
                        actions,
                        clarifyBlockedItem.ExecutionUnit,
                        $"Clarification return path: {clarifyBlockedItem.ClarificationReturnPath}");
                }

                var blockedItem = queueState.Items.FirstOrDefault(item => item.State == QueueItemState.Blocked);
                if (blockedItem is not null)
                {
                    if (TryReconcileExternallyCompletedBlockedItem(
                            context,
                            actions,
                            queueState,
                            blockedItem,
                            out var externallyCompletedResult))
                    {
                        return externallyCompletedResult;
                    }

                    if (TryHandlePostFixWorktreeProgressBoundary(context, actions, blockedItem, out var decisionResult))
                    {
                        if (decisionResult is not null)
                        {
                            return decisionResult;
                        }

                        continue;
                    }

                    if (TryAutoContinueBlockedImplementRecoveredSpecBoundary(
                            context,
                            queueState,
                            blockedItem))
                    {
                        continue;
                    }

                    if (TryResolveBlockedImplementSessionFailureResult(context, actions, blockedItem, out var blockedImplementResult))
                    {
                        return blockedImplementResult;
                    }

                    if (TryResolveBlockedFixRetryExhaustionResult(context, actions, blockedItem, out var blockedFixResult))
                    {
                        return blockedFixResult;
                    }

                    return CreateStopResult(
                        ParentIntentUpdateRequiredStopReason,
                        actions,
                        blockedItem.ExecutionUnit,
                        $"Blocked item '{blockedItem.ExecutionUnit}' requires parent-side planning.");
                }

                if (TryResolveAutoContinueIntakeCandidate(context, queueState, out var intakeCandidate))
                {
                    var selectedIntakeCandidate = intakeCandidate
                        ?? throw new InvalidOperationException("Auto-continue intake candidate was not resolved.");

                    ExecuteAction(
                        context,
                        actions,
                        "intake issue",
                        selectedIntakeCandidate.ExecutionUnit,
                        () => IntakeIssueExecutor(
                            context.RepoRoot,
                            selectedIntakeCandidate.Domain,
                            selectedIntakeCandidate.ExecutionUnit));

                    var launchQueueItem = TryLoadQueueItem(context, selectedIntakeCandidate.ExecutionUnit);
                    if (launchQueueItem is null)
                    {
                        ExecuteAction(
                            context,
                            actions,
                            "queue enqueue",
                            selectedIntakeCandidate.ExecutionUnit,
                            () => QueueEnqueueExecutor(context, selectedIntakeCandidate.ExecutionUnit));
                        launchQueueItem = LoadQueueItemOrThrow(context, selectedIntakeCandidate.ExecutionUnit);
                    }

                    if (launchQueueItem.State != QueueItemState.Queued)
                    {
                        throw new RunDeterministicGapException(
                            selectedIntakeCandidate.ExecutionUnit,
                            $"Auto-continue intake target '{selectedIntakeCandidate.ExecutionUnit}' is in state '{FormatQueueItemState(launchQueueItem.State)}'.");
                    }

                    if (launchQueueItem.LinkedIssue is null)
                    {
                        ExecuteAction(
                            context,
                            actions,
                            "queue dispatch",
                            selectedIntakeCandidate.ExecutionUnit,
                            () => QueueDispatchExecutor(context, selectedIntakeCandidate.ExecutionUnit));
                    }

                    ExecuteAction(
                        context,
                        actions,
                        "run start",
                        selectedIntakeCandidate.ExecutionUnit,
                        () => RunStartExecutor(context, selectedIntakeCandidate.ExecutionUnit));
                    continue;
                }

                return CreateStopResult(NoActionableItemStopReason, actions);
            }
            catch (RunDeterministicGapException exception)
            {
                return CreateStopResult(
                    DeterministicContractGapStopReason,
                    actions,
                    exception.ExecutionUnit,
                    exception.Message);
            }
        }

        return CreateStopResult(
            DeterministicContractGapStopReason,
            actions,
            detail: $"Run orchestration exceeded {IterationBudget} iterations.");
    }

    private static bool TryResolveAutoContinueIntakeCandidate(
        CliContext context,
        QueueState queueState,
        out AutoContinueIntakeCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(queueState);

        candidate = null;

        if (queueState.Items.Count != 0 &&
            !queueState.Items.Any(item => item.State == QueueItemState.Completed))
        {
            return false;
        }

        var intakeDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "intake");
        if (!Directory.Exists(intakeDirectory))
        {
            return false;
        }

        foreach (var artifactPath in Directory
                     .EnumerateFiles(intakeDirectory, "*.execution.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            IntakeExecutionRequest request;
            try
            {
                request = IntakeExecutionArtifactMarkdown.Deserialize(File.ReadAllText(artifactPath));
            }
            catch (InvalidOperationException exception)
            {
                throw new RunDeterministicGapException(
                    Path.GetFileName(artifactPath),
                    $"Intake auto-continue could not read '{artifactPath}': {exception.Message}");
            }

            foreach (var unit in request.ProposedExecutionUnits)
            {
                if (!IsAutoContinueLaunchable(queueState, unit.ExecutionUnitId))
                {
                    continue;
                }

                candidate = new AutoContinueIntakeCandidate(request.Domain, unit.ExecutionUnitId);
                return true;
            }
        }

        return false;
    }

    private static bool IsAutoContinueLaunchable(QueueState queueState, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(queueState);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        return queueItem is null || queueItem.State == QueueItemState.Queued;
    }

    private static QueueItem? TryLoadQueueItem(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueState = LoadQueueStateOrThrow(context);
        return queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
    }

    private static QueueItem LoadQueueItemOrThrow(CliContext context, string executionUnit)
    {
        return TryLoadQueueItem(context, executionUnit)
               ?? throw new RunDeterministicGapException(
                   executionUnit,
                   $"Execution unit '{executionUnit}' was not found in queue state.");
    }

    private static int ExecuteQueueEnqueue(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        using var writer = new StringWriter();
        var exitCode = QueueEnqueueCommand.Execute(context, [executionUnit], writer);
        if (exitCode != 0)
        {
            var detail = writer.ToString().Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Queue enqueue failed for '{executionUnit}'."
                    : detail);
        }

        return exitCode;
    }

    private static string FormatQueueItemState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static T ExecuteAction<T>(
        CliContext context,
        List<RunCommandAction> actions,
        string actionName,
        string executionUnit,
        Func<T> executor)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(executor);

        try
        {
            var result = executor();
            actions.Add(new RunCommandAction
            {
                Name = actionName,
                ExecutionUnit = executionUnit
            });
            return result;
        }
        catch (InvalidOperationException exception)
        {
            throw new RunDeterministicGapException(
                executionUnit,
                $"Deterministic contract gap while executing '{actionName}' for '{executionUnit}': {exception.Message}");
        }
    }

    private static QueueState LoadQueueStateOrThrow(CliContext context)
    {
        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        return queueState;
    }

    private static bool ArtifactExists(CliContext context, string artifactRef)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRef);

        var artifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            artifactRef.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(artifactPath);
    }

    private static bool TryHandlePostFixWorktreeProgressBoundary(
        CliContext context,
        List<RunCommandAction> actions,
        QueueItem blockedItem,
        out RunCommandResult? decisionResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(blockedItem);

        decisionResult = null;
        if (!TryResolvePostFixWorktreeProgressDecisionSession(context, blockedItem, out var session))
        {
            return false;
        }

        if (!string.Equals(
                context.Config.Run.PostFixWorktreeProgressPolicy,
                CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy,
                StringComparison.Ordinal))
        {
            decisionResult = CreateStopResult(
                ClarificationRequiredStopReason,
                actions,
                blockedItem.ExecutionUnit,
                CreatePostFixWorktreeProgressClarificationDetail(blockedItem.ExecutionUnit, session));
            return true;
        }

        ExecuteAutoContinuePostFixWorktreeProgress(context, actions, blockedItem, session);
        return true;
    }

    private static bool TryAutoContinueBlockedImplementRecoveredSpecBoundary(
        CliContext context,
        QueueState queueState,
        QueueItem blockedItem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(queueState);
        ArgumentNullException.ThrowIfNull(blockedItem);

        if (!TryReadSupervisionSession(context, blockedItem.ExecutionUnit, out var session)
            || session.WorkerEntry != RunSupervisionWorkerEntry.Implement
            || session.Status != RunSupervisionSessionStatus.Blocked)
        {
            return false;
        }

        if (!HasCurrentBlockedImplementRecoveredSpecBoundary(context, blockedItem.ExecutionUnit))
        {
            return false;
        }

        var timestamp = TimestampFactory();
        var transition = QueueManager.TransitionNonBlocking(
            queueState,
            blockedItem.ExecutionUnit,
            QueueItemState.Active,
            TransitionActor,
            timestamp);
        var updatedState = transition.UpdatedState with
        {
            Items = transition.UpdatedState.Items.Select(item =>
                string.Equals(item.ExecutionUnit, blockedItem.ExecutionUnit, StringComparison.Ordinal)
                    ? item with { BlockedBy = [] }
                    : item).ToArray()
        };
        File.WriteAllText(context.GetQueueStatePath(), QueueStateSerializer.Serialize(updatedState));
        AppendRunEvent(context.GetRunLogPath(), transition.Event);
        return true;
    }

    private static bool TryResolveBlockedFixRetryExhaustionResult(
        CliContext context,
        IReadOnlyList<RunCommandAction> actions,
        QueueItem blockedItem,
        out RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(blockedItem);

        result = null!;
        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            blockedItem.ExecutionUnit);
        var sessionArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            sessionArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sessionArtifactPath))
        {
            return false;
        }

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionArtifactPath));
        if (session.WorkerEntry != RunSupervisionWorkerEntry.Fix
            || session.Status != RunSupervisionSessionStatus.Blocked
            || session.RetryCount < session.RetryBudget
            || string.IsNullOrWhiteSpace(session.LastInterruptionReason))
        {
            return false;
        }

        result = CreateStopResult(
            NonRetryableFailureStopReason,
            actions,
            blockedItem.ExecutionUnit,
            session.LastInterruptionReason);
        return true;
    }

    private static bool TryResolveBlockedImplementSessionFailureResult(
        CliContext context,
        IReadOnlyList<RunCommandAction> actions,
        QueueItem blockedItem,
        out RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(blockedItem);

        result = null!;
        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            blockedItem.ExecutionUnit);
        var sessionArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            sessionArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sessionArtifactPath))
        {
            return false;
        }

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionArtifactPath));
        if (session.WorkerEntry != RunSupervisionWorkerEntry.Implement
            || session.Status != RunSupervisionSessionStatus.Blocked)
        {
            return false;
        }

        var detail = TryResolveCurrentImplementSessionFailureDetail(context, blockedItem.ExecutionUnit);
        if (string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }

        result = CreateStopResult(
            NonRetryableFailureStopReason,
            actions,
            blockedItem.ExecutionUnit,
            detail);
        return true;
    }

    private static bool TryReconcileExternallyCompletedBlockedItem(
        CliContext context,
        IReadOnlyList<RunCommandAction> actions,
        QueueState queueState,
        QueueItem blockedItem,
        out RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(queueState);
        ArgumentNullException.ThrowIfNull(blockedItem);

        result = null!;
        if (!TryResolveBlockedItemExternalCompletion(context, blockedItem, out var linkedIssue, out var linkedPr))
        {
            return false;
        }

        var timestamp = TimestampFactory();
        var transition = QueueManager.TransitionNonBlocking(
            queueState,
            blockedItem.ExecutionUnit,
            QueueItemState.Completed,
            TransitionActor,
            timestamp);
        var reconciledState = transition.UpdatedState with
        {
            Items = transition.UpdatedState.Items.Select(item =>
                string.Equals(item.ExecutionUnit, blockedItem.ExecutionUnit, StringComparison.Ordinal)
                    ? item with { BlockedBy = [] }
                    : item).ToArray()
        };
        File.WriteAllText(context.GetQueueStatePath(), QueueStateSerializer.Serialize(reconciledState));

        AppendRunEvent(
            context.GetRunLogPath(),
            new RunEvent
            {
                Ts = timestamp,
                ExecutionUnit = blockedItem.ExecutionUnit,
                Event = "pr-merged",
                By = TransitionActor,
                LinkedPr = linkedPr
            });
        AppendRunEvent(
            context.GetRunLogPath(),
            new RunEvent
            {
                Ts = timestamp,
                ExecutionUnit = blockedItem.ExecutionUnit,
                Event = "issue-closed",
                By = TransitionActor,
                LinkedIssue = linkedIssue
            });
        AppendRunEvent(context.GetRunLogPath(), transition.Event);

        result = CreateStopResult(
            NoActionableItemStopReason,
            actions,
            blockedItem.ExecutionUnit,
            $"Execution unit '{blockedItem.ExecutionUnit}' was reconciled from externally completed linked child state.");
        return true;
    }

    private static bool TryResolveBlockedItemExternalCompletion(
        CliContext context,
        QueueItem blockedItem,
        out string linkedIssue,
        out string linkedPr)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blockedItem);

        linkedIssue = null!;
        linkedPr = null!;

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return false;
        }

        try
        {
            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            linkedIssue = blockedItem.LinkedIssue?.Url
                ?? LatestLinkedIssueResolver.Resolve(runEvents, blockedItem.ExecutionUnit);
            linkedPr = LatestLinkedPrResolver.TryResolve(runEvents, blockedItem.ExecutionUnit) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(linkedIssue) || string.IsNullOrWhiteSpace(linkedPr))
            {
                linkedIssue = null!;
                linkedPr = null!;
                return false;
            }

            var githubRunner = GitHubCommandRunnerFactory();
            var issue = GitHubIssueRef.Parse(linkedIssue);
            var issueResult = RunGitHub(
                githubRunner,
                [
                    "issue",
                    "view",
                    issue.IssueNumber.ToString(),
                    "--repo",
                    $"{issue.Owner}/{issue.Repo}",
                    "--json",
                    "state"
                ],
                "gh issue view failed.");
            using var issueDocument = JsonDocument.Parse(issueResult.StdOut);
            var issueState = issueDocument.RootElement.GetProperty("state").GetString();
            if (!string.Equals(issueState, "CLOSED", StringComparison.OrdinalIgnoreCase))
            {
                linkedIssue = null!;
                linkedPr = null!;
                return false;
            }

            var pullRequest = GitHubPullRequestRef.Parse(linkedPr);
            var pullRequestResult = RunGitHub(
                githubRunner,
                [
                    "pr",
                    "view",
                    pullRequest.PullNumber.ToString(),
                    "--repo",
                    $"{pullRequest.Owner}/{pullRequest.Repo}",
                    "--json",
                    "state,mergeCommit"
                ],
                "gh pr view failed.");
            using var pullRequestDocument = JsonDocument.Parse(pullRequestResult.StdOut);
            var isMerged = pullRequestDocument.RootElement.TryGetProperty("mergeCommit", out var mergeCommitElement)
                && mergeCommitElement.ValueKind != JsonValueKind.Null;
            if (!isMerged)
            {
                linkedIssue = null!;
                linkedPr = null!;
                return false;
            }

            return true;
        }
        catch (InvalidOperationException)
        {
            linkedIssue = null!;
            linkedPr = null!;
            return false;
        }
    }

    private static GitHubCommandResult RunGitHub(
        IGitHubCommandRunner runner,
        IReadOnlyList<string> arguments,
        string defaultError)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(arguments);

        var result = runner.Run(arguments);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? defaultError
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        return result;
    }

    private static bool TryReconcileBlockedFixSupervisionState(
        CliContext context,
        IReadOnlyList<RunCommandAction> actions,
        QueueItem fixingItem,
        out RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(fixingItem);

        result = null!;
        var latestFixRequestedAt = TryResolveLatestFixRequestedTimestamp(context, fixingItem.ExecutionUnit);
        if (!TryResolveBlockedFixSupervisionSession(
                context,
                fixingItem.ExecutionUnit,
                latestFixRequestedAt,
                out var session))
        {
            return false;
        }

        var interruptionReason = string.IsNullOrWhiteSpace(session.LastInterruptionReason)
            ? $"Supervisor blocked '{fixingItem.ExecutionUnit}' after non-retryable failure."
            : session.LastInterruptionReason;
        PersistBlockedFixQueueState(context, fixingItem, interruptionReason);
        AppendRunEvent(
            context.GetRunLogPath(),
            new RunEvent
            {
                Ts = TimestampFactory(),
                ExecutionUnit = fixingItem.ExecutionUnit,
                Event = "blocked",
                By = "intent-cli",
                LinkedPr = session.LinkedPr,
                CommentRef = session.CommentRef,
                Reason = interruptionReason
            });

        result = CreateStopResult(
            NonRetryableFailureStopReason,
            actions,
            fixingItem.ExecutionUnit,
            interruptionReason);
        return true;
    }

    private static bool TryReconcileStaleFailedFixResultState(
        CliContext context,
        IReadOnlyList<RunCommandAction> actions,
        QueueItem fixingItem,
        DirectRunRequestArtifact? requestArtifact,
        DirectRunResultArtifact? resultArtifact,
        out RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(fixingItem);

        result = null!;
        if (requestArtifact is null
            || resultArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "fix", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.RunStatus, "failed", StringComparison.Ordinal)
            || !MatchesCurrentDirectRunRequestBoundary(requestArtifact, resultArtifact)
            || !DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var launchedAt)
            || !TryReadBlockedFixSupervisionSession(context, fixingItem.ExecutionUnit, out var session)
            || session.UpdatedAt >= launchedAt)
        {
            return false;
        }

        var failureReason = $"Fix direct run failed for '{fixingItem.ExecutionUnit}'.";
        PersistBlockedFixQueueState(context, fixingItem, failureReason);
        AppendRunEvent(
            context.GetRunLogPath(),
            new RunEvent
            {
                Ts = TimestampFactory(),
                ExecutionUnit = fixingItem.ExecutionUnit,
                Event = "blocked",
                By = "intent-cli",
                LinkedPr = session.LinkedPr,
                CommentRef = session.CommentRef,
                Reason = failureReason
            });

        result = CreateStopResult(
            NonRetryableFailureStopReason,
            actions,
            fixingItem.ExecutionUnit,
            failureReason);
        return true;
    }

    private static void PersistBlockedFixQueueState(
        CliContext context,
        QueueItem queueItem,
        string interruptionReason)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(interruptionReason);

        var queueState = LoadQueueStateOrThrow(context);
        var queueStatePath = context.GetQueueStatePath();
        var now = TimestampFactory();
        var updatedState = queueState with
        {
            UpdatedAt = now,
            Items = queueState.Items.Select(item =>
                string.Equals(item.ExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal)
                    ? item with
                    {
                        State = QueueItemState.Blocked,
                        BlockedBy = [interruptionReason]
                    }
                    : item).ToArray()
        };

        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));
    }

    private static bool TryResolvePostFixWorktreeProgressDecisionSession(
        CliContext context,
        QueueItem blockedItem,
        out RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blockedItem);

        session = null!;
        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            blockedItem.ExecutionUnit);
        var sessionArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            sessionArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sessionArtifactPath))
        {
            return false;
        }

        session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionArtifactPath));
        if (session.WorkerEntry != RunSupervisionWorkerEntry.Fix
            || session.Status != RunSupervisionSessionStatus.Blocked)
        {
            return false;
        }

        if (session.RequiresPostFixWorktreeProgressDecision)
        {
            return true;
        }

        if (!TryUpgradeLegacyPostFixWorktreeProgressDecisionSession(
                context,
                blockedItem,
                sessionArtifactPath,
                session,
                out var upgradedSession))
        {
            return false;
        }

        session = upgradedSession;
        return true;
    }

    private static string CreatePostFixWorktreeProgressClarificationDetail(
        string executionUnit,
        RunSupervisionSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(session);

        var reason = string.IsNullOrWhiteSpace(session.LastInterruptionReason)
            ? $"Meaningful fix progress exists in the execution-unit worktree for '{executionUnit}'."
            : session.LastInterruptionReason;
        return $"{reason} Confirm whether to carry this progress forward. " +
               $"To continue automatically on the next root run, set [run] " +
               $"{CliRuntimeContracts.PostFixWorktreeProgressPolicyKey} = " +
               $"\"{CliRuntimeContracts.AutoContinuePostFixWorktreeProgressPolicy}\" and rerun.";
    }

    private static bool TryUpgradeLegacyPostFixWorktreeProgressDecisionSession(
        CliContext context,
        QueueItem blockedItem,
        string sessionArtifactPath,
        RunSupervisionSession session,
        out RunSupervisionSession upgradedSession)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blockedItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionArtifactPath);
        ArgumentNullException.ThrowIfNull(session);

        upgradedSession = null!;
        if (!blockedItem.BlockedBy.Any(reason =>
                reason.Contains("meaningful execution-unit worktree changes", StringComparison.Ordinal)))
        {
            return false;
        }

        var worktreePath = string.IsNullOrWhiteSpace(session.WorktreePath)
            ? RunStartCommand.ResolveWorktreePath(context, blockedItem.ExecutionUnit)
            : session.WorktreePath;
        if (!Directory.Exists(worktreePath))
        {
            return false;
        }

        if (!RunWorktreeProgressSupport.TryResolveMeaningfulWorktreeDiffPaths(
                GitCommandRunnerFactory(),
                worktreePath,
                out var changedPaths))
        {
            return false;
        }

        var interruptionReason = ResolveLegacyPostFixWorktreeProgressReason(blockedItem, changedPaths);
        upgradedSession = session with
        {
            QueueState = "blocked",
            WorktreePath = worktreePath,
            LastInterruptionReason = interruptionReason,
            RequiresPostFixWorktreeProgressDecision = true,
            UpdatedAt = TimestampFactory()
        };

        File.WriteAllText(
            sessionArtifactPath,
            RunSupervisionSessionArtifactJson.Serialize(upgradedSession));
        return true;
    }

    private static string ResolveLegacyPostFixWorktreeProgressReason(
        QueueItem blockedItem,
        IReadOnlyList<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(blockedItem);
        ArgumentNullException.ThrowIfNull(changedPaths);

        var blockedReason = blockedItem.BlockedBy.FirstOrDefault(reason =>
            reason.Contains("meaningful execution-unit worktree changes", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(blockedReason))
        {
            return $"Meaningful fix progress exists in the execution-unit worktree for '{blockedItem.ExecutionUnit}'. " +
                   $"Changed paths: {RunWorktreeProgressSupport.SummarizePaths(changedPaths)}.";
        }

        if (blockedReason.Contains("Changed paths:", StringComparison.Ordinal))
        {
            return blockedReason;
        }

        return $"{blockedReason.TrimEnd()} Changed paths: {RunWorktreeProgressSupport.SummarizePaths(changedPaths)}.";
    }

    private static void ExecuteAutoContinuePostFixWorktreeProgress(
        CliContext context,
        List<RunCommandAction> actions,
        QueueItem blockedItem,
        RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(blockedItem);
        ArgumentNullException.ThrowIfNull(session);

        var carryForwardCommit = CommitPostFixWorktreeProgress(context, blockedItem);
        var rollbackState = CapturePostFixProgressRollbackState(context, blockedItem.ExecutionUnit);

        try
        {
            RestoreFixingStateAfterPostFixProgressBoundary(context, blockedItem.ExecutionUnit, session);
            ExecuteAction(
                context,
                actions,
                "run resubmit",
                blockedItem.ExecutionUnit,
                () => RunResubmitExecutor(context, blockedItem.ExecutionUnit));
            ExecuteAction(
                context,
                actions,
                "run rereview",
                blockedItem.ExecutionUnit,
                () => RunRereviewExecutor(context, blockedItem.ExecutionUnit));
        }
        catch
        {
            RestorePostFixProgressRollbackState(rollbackState);
            try
            {
                RollBackPostFixCarryForwardCommit(carryForwardCommit);
            }
            catch (InvalidOperationException rollbackException)
            {
                throw new RunDeterministicGapException(
                    blockedItem.ExecutionUnit,
                    $"Failed to roll back auto-continue carry-forward for '{blockedItem.ExecutionUnit}': {rollbackException.Message}");
            }

            throw;
        }

        AppendRunEvent(
            context.GetRunLogPath(),
            new RunEvent
            {
                Ts = TimestampFactory(),
                ExecutionUnit = blockedItem.ExecutionUnit,
                Event = "post-fix-progress-accepted",
                By = "intent-cli",
                LinkedPr = session.LinkedPr,
                CommentRef = session.CommentRef,
                Reason = "Auto-continued repair from meaningful post-fix worktree progress."
            });
    }

    private static void RestoreFixingStateAfterPostFixProgressBoundary(
        CliContext context,
        string executionUnit,
        RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(session);

        var queueState = LoadQueueStateOrThrow(context);
        var queueStatePath = context.GetQueueStatePath();
        var now = TimestampFactory();
        var updatedState = queueState with
        {
            UpdatedAt = now,
            Items = queueState.Items.Select(item =>
                string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                    ? item with
                    {
                        State = QueueItemState.Fixing,
                        BlockedBy = []
                    }
                    : item).ToArray()
        };
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));

        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            executionUnit);
        var sessionArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            sessionArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        File.WriteAllText(
            sessionArtifactPath,
            RunSupervisionSessionArtifactJson.Serialize(session with
            {
                RequiresPostFixWorktreeProgressDecision = false,
                UpdatedAt = now
            }));
    }

    private static PostFixCarryForwardCommit CommitPostFixWorktreeProgress(CliContext context, QueueItem blockedItem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(blockedItem);

        if (blockedItem.LinkedIssue is null)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                $"Blocked execution unit '{blockedItem.ExecutionUnit}' must have a linked issue before carry-forward.");
        }

        var worktreePath = RunStartCommand.ResolveWorktreePath(context, blockedItem.ExecutionUnit);
        if (!Directory.Exists(worktreePath))
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                $"Worktree path was not found at {worktreePath}.");
        }

        var gitRunner = GitCommandRunnerFactory();
        if (!RunWorktreeProgressSupport.TryResolveMeaningfulWorktreeDiffPaths(
                gitRunner,
                worktreePath,
                out var changedPaths))
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                $"Meaningful post-fix worktree progress for '{blockedItem.ExecutionUnit}' was no longer present.");
        }

        var expectedBranchName = RunStartCommand.ResolveBranchName(blockedItem.ExecutionUnit, blockedItem.LinkedIssue);
        var branchResult = gitRunner.Run(worktreePath, ["rev-parse", "--abbrev-ref", "HEAD"]);
        if (branchResult.ExitCode != 0)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                string.IsNullOrWhiteSpace(branchResult.StdErr)
                    ? "git rev-parse --abbrev-ref HEAD failed."
                    : branchResult.StdErr.Trim());
        }

        var currentBranch = branchResult.StdOut.Trim();
        if (!string.Equals(currentBranch, expectedBranchName, StringComparison.Ordinal))
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                $"Current worktree branch '{currentBranch}' must match expected branch '{expectedBranchName}'.");
        }

        var headResult = gitRunner.Run(worktreePath, ["rev-parse", "HEAD"]);
        if (headResult.ExitCode != 0)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                string.IsNullOrWhiteSpace(headResult.StdErr)
                    ? "git rev-parse HEAD failed."
                    : headResult.StdErr.Trim());
        }

        var originalHead = headResult.StdOut.Trim();
        if (string.IsNullOrWhiteSpace(originalHead))
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                "git rev-parse HEAD returned an empty commit id.");
        }

        var addArguments = new List<string> { "add", "--" };
        addArguments.AddRange(changedPaths);
        var addResult = gitRunner.Run(worktreePath, addArguments);
        if (addResult.ExitCode != 0)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                string.IsNullOrWhiteSpace(addResult.StdErr)
                    ? "git add failed."
                    : addResult.StdErr.Trim());
        }

        var diffResult = gitRunner.Run(worktreePath, ["diff", "--cached", "--quiet"]);
        if (diffResult.ExitCode == 0)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                $"Carry-forward commit for '{blockedItem.ExecutionUnit}' had no staged changes.");
        }

        if (diffResult.ExitCode != 1)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                string.IsNullOrWhiteSpace(diffResult.StdErr)
                    ? "git diff --cached --quiet failed."
                    : diffResult.StdErr.Trim());
        }

        var commitResult = gitRunner.Run(
            worktreePath,
            ["commit", "-m", $"Carry forward post-fix progress for {blockedItem.ExecutionUnit}"]);
        if (commitResult.ExitCode != 0)
        {
            throw new RunDeterministicGapException(
                blockedItem.ExecutionUnit,
                string.IsNullOrWhiteSpace(commitResult.StdErr)
                    ? "git commit failed."
                    : commitResult.StdErr.Trim());
        }

        return new PostFixCarryForwardCommit(worktreePath, originalHead);
    }

    private static void RollBackPostFixCarryForwardCommit(PostFixCarryForwardCommit carryForwardCommit)
    {
        ArgumentNullException.ThrowIfNull(carryForwardCommit);

        var gitRunner = GitCommandRunnerFactory();
        var resetResult = gitRunner.Run(
            carryForwardCommit.WorktreePath,
            ["reset", "--mixed", carryForwardCommit.PreviousHead]);
        if (resetResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(resetResult.StdErr)
                    ? "git reset --mixed failed."
                    : resetResult.StdErr.Trim());
        }
    }

    private static void AppendRunEvent(string runLogPath, RunEvent runEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runLogPath);
        ArgumentNullException.ThrowIfNull(runEvent);

        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    private static PostFixProgressRollbackState CapturePostFixProgressRollbackState(
        CliContext context,
        string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var runLogPath = context.GetRunLogPath();
        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            executionUnit);
        var sessionArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            sessionArtifactRef.Replace('/', Path.DirectorySeparatorChar)));

        return new PostFixProgressRollbackState(
            queueStatePath,
            File.ReadAllText(queueStatePath),
            sessionArtifactPath,
            File.ReadAllText(sessionArtifactPath),
            runLogPath,
            File.Exists(runLogPath) ? File.ReadAllText(runLogPath) : string.Empty);
    }

    private static void RestorePostFixProgressRollbackState(PostFixProgressRollbackState rollbackState)
    {
        ArgumentNullException.ThrowIfNull(rollbackState);

        File.WriteAllText(rollbackState.QueueStatePath, rollbackState.QueueStateContent);
        File.WriteAllText(rollbackState.SessionArtifactPath, rollbackState.SessionArtifactContent);
        File.WriteAllText(rollbackState.RunLogPath, rollbackState.RunLogContent);
    }

    private static string DescribeSupervisionResult(RunSuperviseResult superviseResult)
    {
        if (superviseResult.RetryScheduled)
        {
            return "Worker heartbeat expired and retry was scheduled.";
        }

        if (superviseResult.AutoResumed)
        {
            return "Worker was auto-resumed and supervision returned to monitoring.";
        }

        return "Worker remains under supervision.";
    }

    private static RunCommandResult CreateMonitoringStopResult(
        IReadOnlyList<RunCommandAction> actions,
        string executionUnit,
        RunSuperviseResult superviseResult)
    {
        if (superviseResult.Blocked)
        {
            return CreateStopResult(
                NonRetryableFailureStopReason,
                actions,
                executionUnit,
                superviseResult.FailureReason
                ?? $"Supervisor blocked '{executionUnit}' after non-retryable failure.");
        }

        return CreateStopResult(
            NoActionableItemStopReason,
            actions,
            executionUnit,
            DescribeSupervisionResult(superviseResult));
    }

    internal static bool ShouldLaunchFreshImplementAttempt(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact? requestArtifact,
        DirectRunResultArtifact? resultArtifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var latestActivatedAt = TryResolveLatestActivatedTimestamp(context, executionUnit);
        if (HasBlockingImplementSupervisionSession(context, executionUnit, latestActivatedAt))
        {
            return false;
        }

        if (requestArtifact is null
            || resultArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "implement", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.RunStatus, "failed", StringComparison.Ordinal))
        {
            return false;
        }

        if (DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var launchedAt)
            && latestActivatedAt is not null
            && latestActivatedAt <= launchedAt)
        {
            return false;
        }

        if (!MatchesCurrentDirectRunRequestBoundary(requestArtifact, resultArtifact))
        {
            return false;
        }

        if (string.Equals(resultArtifact.RunStatus, "running", StringComparison.Ordinal)
            || string.Equals(resultArtifact.RunStatus, "succeeded", StringComparison.Ordinal))
        {
            return false;
        }

        if (latestActivatedAt is null)
        {
            return false;
        }

        var latestActivityAt = TryResolveCurrentImplementSessionLatestActivityAt(context, executionUnit, requestArtifact);
        if (latestActivityAt is not null && latestActivatedAt > latestActivityAt)
        {
            return true;
        }

        if (!DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out launchedAt))
        {
            return false;
        }

        return latestActivatedAt > launchedAt;
    }

    internal static bool ShouldLaunchFreshFixAttempt(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact? requestArtifact,
        DirectRunResultArtifact? resultArtifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var latestFixRequestedAt = TryResolveLatestFixRequestedTimestamp(context, executionUnit);
        if (HasBlockingFixSupervisionSession(context, executionUnit, latestFixRequestedAt))
        {
            return false;
        }

        if (requestArtifact is null || resultArtifact is null)
        {
            return true;
        }

        if (!MatchesCurrentDirectRunRequestBoundary(requestArtifact, resultArtifact))
        {
            return true;
        }

        if (string.Equals(resultArtifact.RunStatus, "running", StringComparison.Ordinal))
        {
            return false;
        }

        if (latestFixRequestedAt is null)
        {
            return false;
        }

        var latestActivityAt = TryResolveCurrentFixSessionLatestActivityAt(context, executionUnit, requestArtifact);
        if (latestActivityAt is not null
            && latestFixRequestedAt > latestActivityAt
            && !TryReadBlockedFixSupervisionSession(context, executionUnit, out _))
        {
            return true;
        }

        if (!DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var launchedAt))
        {
            return false;
        }

        return latestFixRequestedAt > launchedAt;
    }

    private static void TrackFreshFixContinuation(
        CliContext context,
        string executionUnit,
        IDictionary<string, FreshFixContinuationState> continuationStates)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(continuationStates);

        continuationStates.Remove(executionUnit);
        if (FreshFixContinuationWindow <= TimeSpan.Zero
            || FreshFixContinuationTotalWindow <= TimeSpan.Zero
            || FreshFixContinuationMaxPolls <= 0)
        {
            return;
        }

        var now = TimestampFactory();
        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        var launchedAt = requestArtifact is not null
            && string.Equals(requestArtifact.EntryKind, "fix", StringComparison.Ordinal)
            && DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var parsedLaunchedAt)
                ? parsedLaunchedAt
                : now;
        var totalDeadline = launchedAt + FreshFixContinuationTotalWindow;
        var deadline = launchedAt + FreshFixContinuationWindow;
        if (deadline > totalDeadline)
        {
            deadline = totalDeadline;
        }

        if (deadline <= now)
        {
            return;
        }

        continuationStates[executionUnit] = new FreshFixContinuationState(
            totalDeadline,
            deadline,
            launchedAt,
            FreshFixContinuationMaxPolls);
    }

    private static void TrackFreshImplementContinuation(
        CliContext context,
        string executionUnit,
        IDictionary<string, FreshFixContinuationState> continuationStates)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(continuationStates);

        continuationStates.Remove(executionUnit);
        if (FreshFixContinuationWindow <= TimeSpan.Zero
            || FreshFixContinuationTotalWindow <= TimeSpan.Zero
            || FreshFixContinuationMaxPolls <= 0)
        {
            return;
        }

        var now = TimestampFactory();
        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        var launchedAt = requestArtifact is not null
            && string.Equals(requestArtifact.EntryKind, "implement", StringComparison.Ordinal)
            && DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var parsedLaunchedAt)
                ? parsedLaunchedAt
                : now;
        var totalDeadline = launchedAt + FreshFixContinuationTotalWindow;
        var deadline = launchedAt + FreshFixContinuationWindow;
        if (deadline > totalDeadline)
        {
            deadline = totalDeadline;
        }

        if (deadline <= now)
        {
            return;
        }

        continuationStates[executionUnit] = new FreshFixContinuationState(
            totalDeadline,
            deadline,
            launchedAt,
            FreshFixContinuationMaxPolls);
    }

    private static bool ShouldContinueFreshFixSupervision(
        CliContext context,
        IDictionary<string, FreshFixContinuationState> continuationStates,
        string executionUnit,
        RunSuperviseResult superviseResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuationStates);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(superviseResult);

        if (!continuationStates.TryGetValue(executionUnit, out var continuationState))
        {
            return false;
        }

        if (superviseResult.Blocked
            || superviseResult.WorkerEntry != RunSupervisionWorkerEntry.Fix
            || superviseResult.SessionStatus != RunSupervisionSessionStatus.Monitoring
            || superviseResult.RetryScheduled
            || superviseResult.AutoResumed)
        {
            continuationStates.Remove(executionUnit);
            return false;
        }

        continuationState = RefreshFreshFixContinuationActivity(
            continuationStates,
            executionUnit,
            continuationState,
            context);

        if (TimestampFactory() >= continuationState.Deadline || continuationState.RemainingPolls <= 0)
        {
            continuationStates.Remove(executionUnit);
            return false;
        }

        if (continuationState.RemainingPolls == 1)
        {
            continuationStates.Remove(executionUnit);
        }
        else
        {
            continuationStates[executionUnit] = continuationState with
            {
                RemainingPolls = continuationState.RemainingPolls - 1
            };
        }

        return true;
    }

    private static bool ShouldContinueFreshImplementSupervision(
        CliContext context,
        IDictionary<string, FreshFixContinuationState> continuationStates,
        string executionUnit,
        RunSuperviseResult superviseResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(continuationStates);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(superviseResult);

        if (!continuationStates.TryGetValue(executionUnit, out var continuationState))
        {
            return false;
        }

        if (superviseResult.Blocked
            || superviseResult.WorkerEntry != RunSupervisionWorkerEntry.Implement
            || superviseResult.SessionStatus != RunSupervisionSessionStatus.Monitoring
            || superviseResult.RetryScheduled
            || superviseResult.AutoResumed)
        {
            continuationStates.Remove(executionUnit);
            return false;
        }

        continuationState = RefreshFreshImplementContinuationActivity(
            continuationStates,
            executionUnit,
            continuationState,
            context);

        if (TimestampFactory() >= continuationState.Deadline || continuationState.RemainingPolls <= 0)
        {
            continuationStates.Remove(executionUnit);
            return false;
        }

        if (continuationState.RemainingPolls == 1)
        {
            continuationStates.Remove(executionUnit);
        }
        else
        {
            continuationStates[executionUnit] = continuationState with
            {
                RemainingPolls = continuationState.RemainingPolls - 1
            };
        }

        return true;
    }

    private static FreshFixContinuationState RefreshFreshFixContinuationActivity(
        IDictionary<string, FreshFixContinuationState> continuationStates,
        string executionUnit,
        FreshFixContinuationState continuationState,
        CliContext? context)
    {
        if (context is null || FreshFixContinuationActivityWindow <= TimeSpan.Zero)
        {
            return continuationState;
        }

        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        var latestActivityAt = TryResolveCurrentFixSessionLatestActivityAt(context, executionUnit, requestArtifact);
        if (latestActivityAt is null || latestActivityAt <= continuationState.LastObservedActivityAt)
        {
            return continuationState;
        }

        var extendedDeadline = latestActivityAt.Value + FreshFixContinuationActivityWindow;
        if (extendedDeadline > continuationState.TotalDeadline)
        {
            extendedDeadline = continuationState.TotalDeadline;
        }

        continuationState = continuationState with
        {
            Deadline = extendedDeadline > continuationState.Deadline
                ? extendedDeadline
                : continuationState.Deadline,
            LastObservedActivityAt = latestActivityAt.Value
        };
        continuationStates[executionUnit] = continuationState;
        return continuationState;
    }

    private static FreshFixContinuationState RefreshFreshImplementContinuationActivity(
        IDictionary<string, FreshFixContinuationState> continuationStates,
        string executionUnit,
        FreshFixContinuationState continuationState,
        CliContext? context)
    {
        if (context is null || FreshFixContinuationActivityWindow <= TimeSpan.Zero)
        {
            return continuationState;
        }

        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        var latestActivityAt = TryResolveCurrentImplementSessionLatestActivityAt(context, executionUnit, requestArtifact);
        if (latestActivityAt is null || latestActivityAt <= continuationState.LastObservedActivityAt)
        {
            return continuationState;
        }

        var extendedDeadline = latestActivityAt.Value + FreshFixContinuationActivityWindow;
        if (extendedDeadline > continuationState.TotalDeadline)
        {
            extendedDeadline = continuationState.TotalDeadline;
        }

        continuationState = continuationState with
        {
            Deadline = extendedDeadline > continuationState.Deadline
                ? extendedDeadline
                : continuationState.Deadline,
            LastObservedActivityAt = latestActivityAt.Value
        };
        continuationStates[executionUnit] = continuationState;
        return continuationState;
    }

    private static void SleepFreshFixContinuationPollInterval()
    {
        if (FreshFixContinuationPollInterval > TimeSpan.Zero)
        {
            Thread.Sleep(FreshFixContinuationPollInterval);
        }
    }

    private static bool HasBlockingFixSupervisionSession(
        CliContext context,
        string executionUnit,
        DateTimeOffset? latestFixRequestedAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (!TryReadSupervisionSession(context, executionUnit, out var session))
        {
            return false;
        }

        if (session.WorkerEntry != RunSupervisionWorkerEntry.Fix)
        {
            return true;
        }

        if (session.Status == RunSupervisionSessionStatus.Blocked)
        {
            return latestFixRequestedAt is null || latestFixRequestedAt <= session.UpdatedAt;
        }

        if (IsStaleMonitoringFixSupervisionSession(session, latestFixRequestedAt))
        {
            return false;
        }

        return true;
    }

    private static bool HasBlockingImplementSupervisionSession(
        CliContext context,
        string executionUnit,
        DateTimeOffset? latestActivatedAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (!TryReadSupervisionSession(context, executionUnit, out var session))
        {
            return false;
        }

        if (session.WorkerEntry != RunSupervisionWorkerEntry.Implement)
        {
            return true;
        }

        if (session.Status == RunSupervisionSessionStatus.Blocked)
        {
            return latestActivatedAt is null || latestActivatedAt <= session.UpdatedAt;
        }

        return latestActivatedAt is null || latestActivatedAt <= session.UpdatedAt;
    }

    private static bool TryReadSupervisionSession(
        CliContext context,
        string executionUnit,
        out RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        session = null!;
        var sessionArtifactRef = RunSupervisionSessionArtifactPathResolver.Resolve(
            context.Config.Supervision.ArtifactRoot,
            executionUnit);
        var sessionArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            sessionArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(sessionArtifactPath))
        {
            return false;
        }

        session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionArtifactPath));
        return true;
    }

    private static bool TryReadBlockedFixSupervisionSession(
        CliContext context,
        string executionUnit,
        out RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (!TryReadSupervisionSession(context, executionUnit, out session)
            || session.WorkerEntry != RunSupervisionWorkerEntry.Fix
            || session.Status != RunSupervisionSessionStatus.Blocked)
        {
            return false;
        }

        return true;
    }

    private static bool TryResolveBlockedFixSupervisionSession(
        CliContext context,
        string executionUnit,
        DateTimeOffset? latestFixRequestedAt,
        out RunSupervisionSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (!TryReadBlockedFixSupervisionSession(context, executionUnit, out session))
        {
            return false;
        }

        if (latestFixRequestedAt is not null && latestFixRequestedAt > session.UpdatedAt)
        {
            session = null!;
            return false;
        }

        return true;
    }

    private static bool IsStaleMonitoringFixSupervisionSession(
        RunSupervisionSession session,
        DateTimeOffset? latestFixRequestedAt)
    {
        ArgumentNullException.ThrowIfNull(session);

        return session.WorkerEntry == RunSupervisionWorkerEntry.Fix
            && session.Status == RunSupervisionSessionStatus.Monitoring
            && latestFixRequestedAt is not null
            && latestFixRequestedAt > session.UpdatedAt;
    }

    private static string? TryReadDirectRunStatus(
        CliContext context,
        string executionUnit,
        string expectedEntryKind)
    {
        var resultArtifact = TryReadDirectRunResultArtifact(context, executionUnit, expectedEntryKind);
        return resultArtifact?.RunStatus;
    }

    private static DirectRunResultArtifact? TryReadDirectRunResultArtifact(
        CliContext context,
        string executionUnit,
        string expectedEntryKind)
    {
        var resultArtifactRef = ResolveDirectRunResultArtifactRef(context, executionUnit);
        var resultArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            resultArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(resultArtifactPath))
        {
            return null;
        }

        var artifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        return string.Equals(artifact.EntryKind, expectedEntryKind, StringComparison.Ordinal)
            ? artifact
            : null;
    }

    private static string? TryResolveCurrentFixTargetContractGap(CliContext context, QueueItem queueItem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(queueItem);

        var packetPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            queueItem.PacketPaths.Yaml.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(packetPath))
        {
            return $"Projection packet artifact was not found at {packetPath}";
        }

        try
        {
            var packet = ProjectionPacketRuntimeReader.Read(File.ReadAllText(packetPath));
            var childRepoPath = Path.IsPathRooted(packet.TargetRepo)
                ? Path.GetFullPath(packet.TargetRepo)
                : Path.GetFullPath(Path.Combine(context.RepoRoot, packet.TargetRepo));
            if (!Directory.Exists(childRepoPath))
            {
                return $"Child repo path was not found at {childRepoPath}";
            }

            var worktreePath = RunStartCommand.ResolveWorktreePath(context, queueItem.ExecutionUnit);
            if (!Directory.Exists(worktreePath))
            {
                return $"Worktree path was not found at {worktreePath}";
            }

            ChildWorkTargetGuard.EnsureTargetAllowed(
                queueItem.ExecutionUnit,
                context.RepoRoot,
                packet.TargetRepo,
                worktreePath,
                packet.TargetPath,
                packet.TargetPart);

            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static string? TryResolveCurrentFixSessionContractGap(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact? requestArtifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (requestArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "fix", StringComparison.Ordinal))
        {
            return null;
        }

        var providerEvents = TryReadDirectRunProviderEvents(context, executionUnit);
        if (providerEvents.Count == 0)
        {
            return null;
        }

        providerEvents = SelectCurrentSessionEvents(
            providerEvents,
            requestArtifact.ProviderSessionId,
            requestArtifact.LaunchedAt);

        return DirectRunFixOutcomeSupport.TryResolveContractGapDetail(providerEvents, executionUnit, "fix", out var detail)
            ? detail
            : null;
    }

    private static string? TryResolveCurrentImplementSessionFailureDetail(
        CliContext context,
        string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        var resultArtifact = TryReadDirectRunResultArtifact(context, executionUnit, "implement");
        if (requestArtifact is null
            || resultArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "implement", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.EntryKind, "implement", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.RunStatus, "failed", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.SessionId, requestArtifact.ProviderSessionId, StringComparison.Ordinal))
        {
            return null;
        }

        var providerEvents = TryReadDirectRunProviderEvents(context, executionUnit);
        if (providerEvents.Count == 0)
        {
            return null;
        }

        providerEvents = SelectCurrentSessionEvents(
            providerEvents,
            requestArtifact.ProviderSessionId,
            requestArtifact.LaunchedAt);

        if (DirectRunFixOutcomeSupport.TryResolveStartupOnlyFailureDetail(
                providerEvents,
                executionUnit,
                "implement",
                out var startupOnlyDetail))
        {
            return startupOnlyDetail;
        }

        if (DirectRunFixOutcomeSupport.TryResolveContractGapDetail(
                providerEvents,
                executionUnit,
                "implement",
                out var contractGapDetail))
        {
            return contractGapDetail;
        }

        var canonicalBoundaryEvent = DirectRunFixOutcomeSupport.CreateCanonicalContractGapEventIfNeeded(
            providerEvents,
            DateTimeOffset.UtcNow,
            executionUnit,
            "implement",
            requestArtifact.Provider,
            requestArtifact.ProviderSessionId,
            providerSessionAlive: false);
        if (canonicalBoundaryEvent is null)
        {
            return null;
        }

        var providerLogRef = ResolveDirectRunProviderLogRef(context, executionUnit);
        var providerLogPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            providerLogRef.Replace('/', Path.DirectorySeparatorChar)));
        var writer = new DirectRunProviderEventWriter(providerLogPath);
        writer.Append(canonicalBoundaryEvent);
        providerEvents = [.. providerEvents, canonicalBoundaryEvent];

        return DirectRunFixOutcomeSupport.TryResolveContractGapDetail(
            providerEvents,
            executionUnit,
            "implement",
            out var synthesizedContractGapDetail)
            ? synthesizedContractGapDetail
            : null;
    }

    private static bool HasCurrentBlockedImplementRecoveredSpecBoundary(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        var resultArtifact = TryReadDirectRunResultArtifact(context, executionUnit, "implement");
        if (requestArtifact is null
            || resultArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "implement", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.EntryKind, "implement", StringComparison.Ordinal)
            || !string.Equals(resultArtifact.RunStatus, "failed", StringComparison.Ordinal)
            || !MatchesCurrentDirectRunRequestBoundary(requestArtifact, resultArtifact))
        {
            return false;
        }

        var providerEvents = TryReadDirectRunProviderEvents(context, executionUnit);
        if (providerEvents.Count == 0)
        {
            return false;
        }

        providerEvents = SelectCurrentSessionEvents(
            providerEvents,
            requestArtifact.ProviderSessionId,
            requestArtifact.LaunchedAt);

        return DirectRunFixOutcomeSupport.HasRecoveredSpecWithoutProductReadSignal(providerEvents);
    }

    private static DateTimeOffset? TryResolveCurrentFixSessionLatestActivityAt(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact? requestArtifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (requestArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "fix", StringComparison.Ordinal))
        {
            return null;
        }

        var providerEvents = TryReadDirectRunProviderEvents(context, executionUnit);
        if (providerEvents.Count == 0)
        {
            return null;
        }

        providerEvents = SelectCurrentSessionEvents(
            providerEvents,
            requestArtifact.ProviderSessionId,
            requestArtifact.LaunchedAt);

        DateTimeOffset? latestActivityAt = null;
        foreach (var providerEvent in providerEvents)
        {
            if (!DateTimeOffset.TryParse(providerEvent.Timestamp, out var parsedTimestamp))
            {
                continue;
            }

            if (latestActivityAt is null || parsedTimestamp > latestActivityAt.Value)
            {
                latestActivityAt = parsedTimestamp;
            }
        }

        return latestActivityAt;
    }

    private static DateTimeOffset? TryResolveCurrentImplementSessionLatestActivityAt(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact? requestArtifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (requestArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "implement", StringComparison.Ordinal))
        {
            return null;
        }

        var providerEvents = TryReadDirectRunProviderEvents(context, executionUnit);
        if (providerEvents.Count == 0)
        {
            return null;
        }

        providerEvents = SelectCurrentSessionEvents(
            providerEvents,
            requestArtifact.ProviderSessionId,
            requestArtifact.LaunchedAt);

        DateTimeOffset? latestActivityAt = null;
        foreach (var providerEvent in providerEvents)
        {
            if (!DateTimeOffset.TryParse(providerEvent.Timestamp, out var parsedTimestamp))
            {
                continue;
            }

            if (latestActivityAt is null || parsedTimestamp > latestActivityAt.Value)
            {
                latestActivityAt = parsedTimestamp;
            }
        }

        return latestActivityAt;
    }

    private static IReadOnlyList<DirectRunProviderEvent> TryReadDirectRunProviderEvents(CliContext context, string executionUnit)
    {
        var providerLogRef = ResolveDirectRunProviderLogRef(context, executionUnit);
        var providerLogPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            providerLogRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(providerLogPath))
        {
            return [];
        }

        return DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerLogPath));
    }

    private static DirectRunRequestArtifact? TryReadDirectRunRequestArtifact(CliContext context, string executionUnit)
    {
        var requestArtifactRef = ResolveDirectRunRequestArtifactRef(context, executionUnit);
        var requestArtifactPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            requestArtifactRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(requestArtifactPath))
        {
            return null;
        }

        return DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(requestArtifactPath));
    }

    private static string ResolveDirectRunRequestArtifactRef(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.request.json";
    }

    private static string ResolveDirectRunResultArtifactRef(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.result.json";
    }

    private static string ResolveDirectRunProviderLogRef(CliContext context, string executionUnit)
    {
        var root = context.Config.DirectRun.ArtifactRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{executionUnit.Trim()}.provider.jsonl";
    }

    private static DateTimeOffset? TryResolveLatestFixRequestedTimestamp(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return null;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        for (var index = runEvents.Count - 1; index >= 0; index--)
        {
            var runEvent = runEvents[index];
            if (string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                && string.Equals(runEvent.Event, "fix-requested", StringComparison.Ordinal))
            {
                return runEvent.Ts;
            }
        }

        return null;
    }

    private static DateTimeOffset? TryResolveLatestActivatedTimestamp(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return null;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        for (var index = runEvents.Count - 1; index >= 0; index--)
        {
            var runEvent = runEvents[index];
            if (string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                && string.Equals(runEvent.Event, "activated", StringComparison.Ordinal))
            {
                return runEvent.Ts;
            }
        }

        return null;
    }

    private static bool ShouldLaunchReviewRun(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (!ArtifactExists(context, ReviewArtifactPathResolver.Resolve(executionUnit)))
        {
            return true;
        }

        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        if (requestArtifact is null
            || !string.Equals(requestArtifact.EntryKind, "review", StringComparison.Ordinal))
        {
            return true;
        }

        return IsReviewRequestBoundaryStale(context, executionUnit, requestArtifact);
    }

    private static RunReviewDecision ResolveReviewDecision(CliContext context, string executionUnit)
    {
        var requestArtifact = TryReadDirectRunRequestArtifact(context, executionUnit);
        if (requestArtifact is null)
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Waiting,
                Detail = $"Review request exists for '{executionUnit}' but no direct run request boundary is available yet."
            };
        }

        var resultArtifact = TryReadDirectRunResultArtifact(context, executionUnit, "review");
        if (resultArtifact is null)
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Waiting,
                Detail = $"Review request exists for '{executionUnit}' but no direct run result is available yet."
            };
        }

        if (!MatchesCurrentDirectRunRequestBoundary(requestArtifact, resultArtifact))
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Waiting,
                Detail = $"Review direct run result for '{executionUnit}' does not match the current launched request boundary."
            };
        }

        var providerEvents = EnsureCurrentSessionTerminalProviderEvent(
            context,
            executionUnit,
            requestArtifact,
            TryReadDirectRunProviderEvents(context, executionUnit));
        providerEvents = SelectCurrentSessionEvents(providerEvents, requestArtifact.ProviderSessionId, requestArtifact.LaunchedAt);
        var runStatus = ResolveEffectiveRunStatus(resultArtifact.RunStatus, providerEvents);
        if (!string.Equals(runStatus, resultArtifact.RunStatus, StringComparison.Ordinal))
        {
            resultArtifact = resultArtifact with
            {
                RunStatus = runStatus
            };
            PersistDirectRunResultArtifact(context, executionUnit, resultArtifact);
        }

        var capturedOutcomeEvent = DirectRunReviewOutcomeSupport.TryCreateReviewOutcomeEventFromCapturedMessage(
            providerEvents,
            Path.GetFullPath(Path.Combine(
                context.RepoRoot,
                DirectRunCommandSupport.ResolveCapturedMessagePath(
                    context,
                    executionUnit,
                    DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var capturedLaunchedAt)
                        ? capturedLaunchedAt
                        : DateTimeOffset.Parse(
                            requestArtifact.LaunchedAt,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind))
                .Replace('/', Path.DirectorySeparatorChar))),
            DateTimeOffset.UtcNow,
            executionUnit,
            requestArtifact.EntryKind,
            requestArtifact.Provider,
            requestArtifact.ProviderSessionId);
        if (capturedOutcomeEvent is not null)
        {
            var providerLogPath = Path.GetFullPath(Path.Combine(
                context.RepoRoot,
                resultArtifact.RawLogRef.Replace('/', Path.DirectorySeparatorChar)));
            var writer = new DirectRunProviderEventWriter(providerLogPath);
            writer.Append(capturedOutcomeEvent);
            providerEvents = [.. providerEvents, capturedOutcomeEvent];
        }

        string? reviewOutcome = null;
        string? reviewCommentBodyPath = null;
        if (DirectRunReviewOutcomeSupport.TryResolveCanonicalReviewOutcome(
                runStatus,
                resultArtifact.ReviewOutcome,
                providerEvents,
                out var resolvedReviewOutcome))
        {
            reviewOutcome = resolvedReviewOutcome;
            if (DirectRunReviewOutcomeSupport.IsCommentOutcome(reviewOutcome))
            {
                if (!string.IsNullOrWhiteSpace(resultArtifact.ReviewCommentBodyPath))
                {
                    reviewCommentBodyPath = resultArtifact.ReviewCommentBodyPath;
                }
                else if (DirectRunReviewOutcomeSupport.TryResolveReviewCommentBodyPath(
                             context,
                             executionUnit,
                             providerEvents,
                             out var resolvedCommentBodyPath))
                {
                    reviewCommentBodyPath = resolvedCommentBodyPath;
                }
            }

            if (!string.Equals(resultArtifact.ReviewOutcome, reviewOutcome, StringComparison.Ordinal)
                || !string.Equals(resultArtifact.ReviewCommentBodyPath, reviewCommentBodyPath, StringComparison.Ordinal))
            {
                resultArtifact = resultArtifact with
                {
                    ReviewOutcome = reviewOutcome,
                    ReviewCommentBodyPath = reviewCommentBodyPath
                };
                PersistDirectRunResultArtifact(context, executionUnit, resultArtifact);
            }

            var outcomeEvent = DirectRunReviewOutcomeSupport.CreateCanonicalReviewOutcomeEventIfNeeded(
                providerEvents,
                DateTimeOffset.UtcNow,
                executionUnit,
                requestArtifact.EntryKind,
                requestArtifact.Provider,
                requestArtifact.ProviderSessionId,
                reviewOutcome,
                reviewCommentBodyPath);
            if (outcomeEvent is not null)
            {
                var providerLogPath = Path.GetFullPath(Path.Combine(
                    context.RepoRoot,
                    resultArtifact.RawLogRef.Replace('/', Path.DirectorySeparatorChar)));
                var writer = new DirectRunProviderEventWriter(providerLogPath);
                writer.Append(outcomeEvent);
                providerEvents = [.. providerEvents, outcomeEvent];
            }
        }

        var effectiveRunStatus = DirectRunReviewOutcomeSupport.ResolveEffectiveReviewRunStatus(runStatus, reviewOutcome);
        if (!string.Equals(effectiveRunStatus, runStatus, StringComparison.Ordinal))
        {
            runStatus = effectiveRunStatus;
        }

        if (!string.Equals(runStatus, resultArtifact.RunStatus, StringComparison.Ordinal))
        {
            resultArtifact = resultArtifact with
            {
                RunStatus = runStatus
            };
            PersistDirectRunResultArtifact(context, executionUnit, resultArtifact);
        }

        if (string.Equals(runStatus, "failed", StringComparison.Ordinal))
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Failure,
                Detail = $"Review direct run failed for '{executionUnit}'."
            };
        }

        if (string.Equals(runStatus, "accepted", StringComparison.Ordinal)
            || string.Equals(runStatus, "approved", StringComparison.Ordinal))
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Accept,
                Detail = $"Review decision for '{executionUnit}' resolved to accept."
            };
        }

        if (string.Equals(runStatus, "succeeded", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(reviewOutcome))
            {
                if (DirectRunReviewOutcomeSupport.IsAcceptOutcome(reviewOutcome))
                {
                    return new RunReviewDecision
                    {
                        Kind = RunReviewDecisionKind.Accept,
                        Detail = $"Review decision for '{executionUnit}' resolved to accept."
                    };
                }

                if (DirectRunReviewOutcomeSupport.IsCommentOutcome(reviewOutcome))
                {
                    if (!string.IsNullOrWhiteSpace(reviewCommentBodyPath))
                    {
                        return new RunReviewDecision
                        {
                            Kind = RunReviewDecisionKind.Comment,
                            CommentBodyPath = reviewCommentBodyPath,
                            Detail = $"Review decision for '{executionUnit}' resolved to comment."
                        };
                    }

                    return new RunReviewDecision
                    {
                        Kind = RunReviewDecisionKind.ContractGap,
                        Detail = $"Review decision for '{executionUnit}' requested comment but no deterministic comment body was found."
                    };
                }
            }
        }

        if (string.Equals(runStatus, "comment", StringComparison.Ordinal)
            || string.Equals(runStatus, "commented", StringComparison.Ordinal)
            || string.Equals(runStatus, "fix-requested", StringComparison.Ordinal)
            || string.Equals(runStatus, "changes-requested", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(reviewCommentBodyPath))
            {
                return new RunReviewDecision
                {
                    Kind = RunReviewDecisionKind.Comment,
                    CommentBodyPath = reviewCommentBodyPath,
                    Detail = $"Review decision for '{executionUnit}' resolved to comment."
                };
            }

            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.ContractGap,
                Detail = $"Review decision for '{executionUnit}' requested comment but no deterministic comment body was found."
            };
        }

        return new RunReviewDecision
        {
            Kind = RunReviewDecisionKind.Waiting,
            Detail = $"Review direct run for '{executionUnit}' is '{runStatus}'."
        };
    }

    private static DateTimeOffset? TryResolveLatestReviewReentryTimestamp(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return null;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        for (var index = runEvents.Count - 1; index >= 0; index--)
        {
            var runEvent = runEvents[index];
            if (!string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(runEvent.Event, "rereview", StringComparison.Ordinal)
                || string.Equals(runEvent.Event, "review", StringComparison.Ordinal))
            {
                return runEvent.Ts;
            }
        }

        return null;
    }

    private static bool IsReviewRequestBoundaryStale(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact requestArtifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(requestArtifact);

        if (!DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var launchedAt))
        {
            return false;
        }

        var latestReviewReentryAt = TryResolveLatestReviewReentryTimestamp(context, executionUnit);
        return latestReviewReentryAt is not null && latestReviewReentryAt > launchedAt;
    }

    private static IReadOnlyList<DirectRunProviderEvent> EnsureCurrentSessionTerminalProviderEvent(
        CliContext context,
        string executionUnit,
        DirectRunRequestArtifact requestArtifact,
        IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(requestArtifact);
        ArgumentNullException.ThrowIfNull(providerEvents);

        var currentProviderEvents = SelectCurrentSessionEvents(providerEvents, requestArtifact.ProviderSessionId, requestArtifact.LaunchedAt);
        if (!string.Equals(requestArtifact.Provider, "Codex", StringComparison.OrdinalIgnoreCase)
            || currentProviderEvents.Any(HasBackendExitType)
            || !TryParseSessionProcessId(requestArtifact.ProviderSessionId, out var processId)
            || IsProcessAlive(processId))
        {
            return providerEvents;
        }

        var backendExitEvent = DirectRunProviderEventFactory.CreateBackendExitEvent(
            DateTimeOffset.UtcNow,
            executionUnit,
            requestArtifact.EntryKind,
            requestArtifact.Provider,
            requestArtifact.ProviderSessionId,
            exitCode: 0);
        var providerLogRef = ResolveDirectRunProviderLogRef(context, executionUnit);
        var providerLogPath = Path.GetFullPath(Path.Combine(
            context.RepoRoot,
            providerLogRef.Replace('/', Path.DirectorySeparatorChar)));
        var writer = new DirectRunProviderEventWriter(providerLogPath);
        writer.Append(backendExitEvent);

        return [.. providerEvents, backendExitEvent];
    }

    private static string ResolveEffectiveRunStatus(
        string currentRunStatus,
        IReadOnlyList<DirectRunProviderEvent> providerEvents)
    {
        if (!string.Equals(currentRunStatus, "running", StringComparison.Ordinal))
        {
            return currentRunStatus;
        }

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (TryResolveRunStatus(providerEvents[index].Payload, out var runStatus))
            {
                return runStatus;
            }
        }

        return currentRunStatus;
    }

    private static bool TryParseSessionProcessId(string providerSessionId, out int processId)
    {
        processId = default;

        const string prefix = "pid:";
        if (!providerSessionId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(
            providerSessionId[prefix.Length..],
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out processId);
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasBackendExitType(DirectRunProviderEvent providerEvent)
    {
        ArgumentNullException.ThrowIfNull(providerEvent);

        return providerEvent.Kind == "provider-event"
            && providerEvent.Payload.ValueKind == JsonValueKind.Object
            && providerEvent.Payload.TryGetProperty("type", out var typeElement)
            && string.Equals(typeElement.GetString(), "backend-exit", StringComparison.Ordinal);
    }

    private static IReadOnlyList<DirectRunProviderEvent> SelectCurrentSessionEvents(
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        string launchedSessionId,
        string launchedAt)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        if (!DirectRunSessionBoundary.TryParseLaunchedAt(launchedAt, out var parsedLaunchedAt))
        {
            parsedLaunchedAt = default;
        }

        return DirectRunSessionBoundary.SelectEvents(
            providerEvents,
            launchedSessionId,
            parsedLaunchedAt == default ? null : parsedLaunchedAt);
    }

    private static bool MatchesCurrentDirectRunRequestBoundary(
        DirectRunRequestArtifact requestArtifact,
        DirectRunResultArtifact resultArtifact)
    {
        ArgumentNullException.ThrowIfNull(requestArtifact);
        ArgumentNullException.ThrowIfNull(resultArtifact);

        return string.Equals(resultArtifact.EntryKind, requestArtifact.EntryKind, StringComparison.Ordinal)
            && string.Equals(resultArtifact.UpstreamRequestRef, requestArtifact.UpstreamRequestRef, StringComparison.Ordinal)
            && string.Equals(resultArtifact.Provider, requestArtifact.Provider, StringComparison.Ordinal)
            && string.Equals(resultArtifact.Model, requestArtifact.Model, StringComparison.Ordinal)
            && string.Equals(resultArtifact.SessionId, requestArtifact.ProviderSessionId, StringComparison.Ordinal);
    }

    private static void ClearActiveQueueBlockedBy(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueState = LoadQueueStateOrThrow(context);
        var selectedItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        if (selectedItem is null
            || selectedItem.State != QueueItemState.Active
            || selectedItem.BlockedBy.Count == 0)
        {
            return;
        }

        var updatedState = queueState with
        {
            UpdatedAt = TimestampFactory(),
            Items = queueState.Items.Select(item =>
                string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal)
                    ? item with { BlockedBy = [] }
                    : item).ToArray()
        };
        File.WriteAllText(context.GetQueueStatePath(), QueueStateSerializer.Serialize(updatedState));
    }


    private static bool TryResolveRunStatus(JsonElement payload, out string runStatus)
    {
        runStatus = string.Empty;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadString(payload, "run_status", out var payloadRunStatus))
        {
            runStatus = NormalizeRunStatus(payloadRunStatus);
            return true;
        }

        if (TryReadString(payload, "status", out var status))
        {
            runStatus = NormalizeRunStatus(status);
            return true;
        }

        if (TryReadString(payload, "disposition", out var disposition))
        {
            runStatus = NormalizeRunStatus(disposition);
            return true;
        }

        if (TryReadInt32(payload, "exit_code", out var exitCode)
            || TryReadInt32(payload, "exitCode", out exitCode))
        {
            runStatus = exitCode == 0 ? "succeeded" : "failed";
            return true;
        }

        return false;
    }

    private static bool TryReadInt32(JsonElement payload, string propertyName, out int value)
    {
        value = default;

        if (!payload.TryGetProperty(propertyName, out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            return !string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        return false;
    }

    private static bool TryReadString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;

        if (!payload.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string NormalizeRunStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "success" or "completed" => "succeeded",
            "error" => "failed",
            var normalized when !string.IsNullOrWhiteSpace(normalized) => normalized,
            _ => "running"
        };
    }

    private static void PersistDirectRunResultArtifact(
        CliContext context,
        string executionUnit,
        DirectRunResultArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(artifact);

        var resultArtifactPath = Path.Combine(
            context.RepoRoot,
            ResolveDirectRunResultArtifactRef(context, executionUnit).Replace('/', Path.DirectorySeparatorChar));
        var directoryPath = Path.GetDirectoryName(resultArtifactPath)
            ?? throw new InvalidOperationException("Direct run result artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(resultArtifactPath, DirectRunResultArtifactJson.Serialize(artifact));
    }

    private static RunCommandResult CreateStopResult(
        string stopReason,
        IReadOnlyList<RunCommandAction> actions,
        string? executionUnit = null,
        string? detail = null)
    {
        var touchedExecutionUnits = new List<string>();
        foreach (var action in actions)
        {
            if (!touchedExecutionUnits.Contains(action.ExecutionUnit, StringComparer.Ordinal))
            {
                touchedExecutionUnits.Add(action.ExecutionUnit);
            }
        }

        if (!string.IsNullOrWhiteSpace(executionUnit)
            && !touchedExecutionUnits.Contains(executionUnit, StringComparer.Ordinal))
        {
            touchedExecutionUnits.Add(executionUnit);
        }

        var reusedChildCommandRefs = new List<string>();
        foreach (var action in actions)
        {
            if (!reusedChildCommandRefs.Contains(action.Name, StringComparer.Ordinal))
            {
                reusedChildCommandRefs.Add(action.Name);
            }
        }

        return new RunCommandResult
        {
            StopReason = stopReason,
            Actions = actions.ToArray(),
            TouchedExecutionUnits = touchedExecutionUnits,
            ReusedChildCommandRefs = reusedChildCommandRefs,
            ExecutionUnit = executionUnit,
            Detail = detail
        };
    }

    private static RunCommandResult PersistResultArtifact(CliContext context, RunCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        var artifactPath = RunRootResultArtifactPathResolver.Resolve(context);
        var absoluteArtifactPath = Path.IsPathRooted(artifactPath)
            ? artifactPath
            : Path.GetFullPath(Path.Combine(context.RepoRoot, artifactPath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absoluteArtifactPath)
            ?? throw new InvalidOperationException("Run root result artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(
            absoluteArtifactPath,
            RunRootResultArtifactJson.Serialize(
                new RunRootResultArtifact
                {
                    SchemaVersion = "1",
                    StopReason = result.StopReason,
                    TouchedExecutionUnits = result.TouchedExecutionUnits,
                    ReusedChildCommandRefs = result.ReusedChildCommandRefs,
                    ExecutionUnit = result.ExecutionUnit,
                    Detail = result.Detail
                }));

        return result with
        {
            ArtifactPath = artifactPath
        };
    }

    private enum RunReviewDecisionKind
    {
        Waiting,
        Accept,
        Comment,
        Failure,
        ContractGap
    }

    private sealed record RunReviewDecision
    {
        public required RunReviewDecisionKind Kind { get; init; }

        public string? CommentBodyPath { get; init; }

        public required string Detail { get; init; }
    }

    private sealed record PostFixProgressRollbackState(
        string QueueStatePath,
        string QueueStateContent,
        string SessionArtifactPath,
        string SessionArtifactContent,
        string RunLogPath,
        string RunLogContent);

    private sealed record PostFixCarryForwardCommit(
        string WorktreePath,
        string PreviousHead);
}
