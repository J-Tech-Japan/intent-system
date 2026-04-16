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
    private sealed class RunDeterministicGapException(string executionUnit, string message)
        : InvalidOperationException(message)
    {
        public string ExecutionUnit { get; } = executionUnit;
    }

    private const int IterationBudget = 128;
    private const string NoActionableItemStopReason = "no-actionable-item";
    private const string ClarificationRequiredStopReason = "clarification-required";
    private const string ParentIntentUpdateRequiredStopReason = "parent-intent-update-required";
    private const string DeterministicContractGapStopReason = "deterministic-contract-gap";
    private const string NonRetryableFailureStopReason = "non-retryable-failure";

    public static Func<CliContext, string, QueueDispatchCommandResult> QueueDispatchExecutor { get; set; } =
        QueueDispatchCommand.ExecuteCore;

    public static Func<CliContext, string, RunStartResult> RunStartExecutor { get; set; } =
        RunStartCommand.ExecuteCore;

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

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

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
                            ExecuteAction(
                                context,
                                actions,
                                "run submit",
                                inProgressItem.ExecutionUnit,
                                () => RunSubmitExecutor(context, inProgressItem.ExecutionUnit));
                            continue;
                        }

                        if (string.Equals(implementRunStatus, "failed", StringComparison.Ordinal))
                        {
                            return CreateStopResult(
                                NonRetryableFailureStopReason,
                                actions,
                                inProgressItem.ExecutionUnit,
                                $"Implement direct run failed for '{inProgressItem.ExecutionUnit}'.");
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

                        var superviseResult = ExecuteAction(
                            context,
                            actions,
                            "run supervise",
                            inProgressItem.ExecutionUnit,
                            () => RunSuperviseExecutor(context, inProgressItem.ExecutionUnit));

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
                            continue;
                        }

                        var currentFixTargetContractGap = TryResolveCurrentFixTargetContractGap(context, inProgressItem);
                        if (!string.IsNullOrWhiteSpace(currentFixTargetContractGap))
                        {
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
                            continue;
                        }

                        var fixRunStatus = fixResultArtifact?.RunStatus;
                        var currentFixSessionContractGap = TryResolveCurrentFixSessionContractGap(
                            context,
                            inProgressItem.ExecutionUnit,
                            fixRequestArtifact);
                        if (!string.IsNullOrWhiteSpace(currentFixSessionContractGap))
                        {
                            return CreateStopResult(
                                DeterministicContractGapStopReason,
                                actions,
                                inProgressItem.ExecutionUnit,
                                currentFixSessionContractGap);
                        }

                        if (string.Equals(fixRunStatus, "succeeded", StringComparison.Ordinal))
                        {
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

                    if (!ArtifactExists(context, ReviewArtifactPathResolver.Resolve(inProgressItem.ExecutionUnit)))
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
                    if (TryHandlePostFixWorktreeProgressBoundary(context, actions, blockedItem, out var decisionResult))
                    {
                        if (decisionResult is not null)
                        {
                            return decisionResult;
                        }

                        continue;
                    }

                    return CreateStopResult(
                        ParentIntentUpdateRequiredStopReason,
                        actions,
                        blockedItem.ExecutionUnit,
                        $"Blocked item '{blockedItem.ExecutionUnit}' requires parent-side planning.");
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
        if (!TryResolvePostFixWorktreeProgressDecisionSession(context, blockedItem.ExecutionUnit, out var session))
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

    private static bool TryResolvePostFixWorktreeProgressDecisionSession(
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
        return session.WorkerEntry == RunSupervisionWorkerEntry.Fix
            && session.Status == RunSupervisionSessionStatus.Blocked
            && session.RequiresPostFixWorktreeProgressDecision;
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

        RestoreFixingStateAfterPostFixProgressBoundary(context, blockedItem.ExecutionUnit, session);
        CommitPostFixWorktreeProgress(context, blockedItem);
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

        AppendRunEvent(
            context.GetRunLogPath(),
            new RunEvent
            {
                Ts = now,
                ExecutionUnit = executionUnit,
                Event = "post-fix-progress-accepted",
                By = "intent-cli",
                LinkedPr = session.LinkedPr,
                CommentRef = session.CommentRef,
                Reason = "Auto-continued repair from meaningful post-fix worktree progress."
            });
    }

    private static void CommitPostFixWorktreeProgress(CliContext context, QueueItem blockedItem)
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

    private static bool ShouldLaunchFreshFixAttempt(
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

        if (!DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var launchedAt))
        {
            return false;
        }

        return latestFixRequestedAt is not null && latestFixRequestedAt > launchedAt;
    }

    private static bool HasBlockingFixSupervisionSession(
        CliContext context,
        string executionUnit,
        DateTimeOffset? latestFixRequestedAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

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

        var session = RunSupervisionSessionArtifactJson.Deserialize(File.ReadAllText(sessionArtifactPath));
        if (session.WorkerEntry == RunSupervisionWorkerEntry.Fix
            && session.Status == RunSupervisionSessionStatus.Blocked
            && latestFixRequestedAt is not null
            && latestFixRequestedAt > session.UpdatedAt)
        {
            return false;
        }

        return true;
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

        return DirectRunFixOutcomeSupport.TryResolveContractGapDetail(providerEvents, executionUnit, out var detail)
            ? detail
            : null;
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
}
