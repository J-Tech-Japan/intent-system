using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

internal static class RunCommand
{
    private sealed class RunDeterministicGapException(string executionUnit, string message)
        : InvalidOperationException(message)
    {
        public string ExecutionUnit { get; } = executionUnit;
    }

    private const int IterationBudget = 128;
    private const string NoActionableWorkStopReason = "no-actionable-work";
    private const string ClarificationRequiredStopReason = "clarification-required";
    private const string ParentPlanningRequiredStopReason = "parent-planning-required";
    private const string ReviewDecisionRequiredStopReason = "review-decision-required";
    private const string WorkerMonitoringStopReason = "worker-monitoring";
    private const string ParallelWorkDetectedStopReason = "parallel-work-detected";
    private const string IterationBudgetExhaustedStopReason = "iteration-budget-exhausted";
    private const string DeterministicContractGapStopReason = "deterministic-contract-gap";

    public static Func<CliContext, string, QueueDispatchCommandResult> QueueDispatchExecutor { get; set; } =
        QueueDispatchCommand.ExecuteCore;

    public static Func<CliContext, string, RunStartResult> RunStartExecutor { get; set; } =
        RunStartCommand.ExecuteCore;

    public static Func<CliContext, string, RunImplementResult> RunImplementExecutor { get; set; } =
        RunImplementCommand.ExecuteCore;

    public static Func<CliContext, string, RunFixResult> RunFixExecutor { get; set; } =
        RunFixCommand.ExecuteCore;

    public static Func<CliContext, string, RunSuperviseResult> RunSuperviseExecutor { get; set; } =
        RunSuperviseCommand.ExecuteCore;

    public static Func<CliContext, string, ReviewRunResult> ReviewRunExecutor { get; set; } =
        ReviewRunCommand.ExecuteCore;

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
                        ParallelWorkDetectedStopReason,
                        actions,
                        detail: $"Multiple in-progress items detected: {string.Join(", ", inProgressItems.Select(item => item.ExecutionUnit))}.");
                }

                if (inProgressItems.Count == 1)
                {
                    var inProgressItem = inProgressItems[0];
                    if (inProgressItem.State == QueueItemState.Active)
                    {
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
                            continue;
                        }

                        return CreateStopResult(
                            WorkerMonitoringStopReason,
                            actions,
                            inProgressItem.ExecutionUnit,
                            DescribeSupervisionResult(superviseResult));
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

                        if (!ArtifactExists(context, RunFixArtifactPathResolver.Resolve(inProgressItem.ExecutionUnit)))
                        {
                            ExecuteAction(
                                context,
                                actions,
                                "run fix",
                                inProgressItem.ExecutionUnit,
                                () => RunFixExecutor(context, inProgressItem.ExecutionUnit));
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
                            continue;
                        }

                        return CreateStopResult(
                            WorkerMonitoringStopReason,
                            actions,
                            inProgressItem.ExecutionUnit,
                            DescribeSupervisionResult(superviseResult));
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

                    return CreateStopResult(
                        ReviewDecisionRequiredStopReason,
                        actions,
                        inProgressItem.ExecutionUnit,
                        "Review outcome requires an explicit accept/comment decision.");
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
                    return CreateStopResult(
                        ParentPlanningRequiredStopReason,
                        actions,
                        blockedItem.ExecutionUnit,
                        $"Blocked item '{blockedItem.ExecutionUnit}' requires parent-side planning.");
                }

                return CreateStopResult(NoActionableWorkStopReason, actions);
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
            IterationBudgetExhaustedStopReason,
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

    private static RunCommandResult CreateStopResult(
        string stopReason,
        IReadOnlyList<RunCommandAction> actions,
        string? executionUnit = null,
        string? detail = null)
    {
        return new RunCommandResult
        {
            StopReason = stopReason,
            Actions = actions.ToArray(),
            ExecutionUnit = executionUnit,
            Detail = detail
        };
    }
}
