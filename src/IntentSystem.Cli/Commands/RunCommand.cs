using System.Text.Json;
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

                        var fixRunStatus = TryReadDirectRunStatus(
                            context,
                            inProgressItem.ExecutionUnit,
                            "fix");
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
                $"Supervisor blocked '{executionUnit}' after non-retryable failure.");
        }

        return CreateStopResult(
            NoActionableItemStopReason,
            actions,
            executionUnit,
            DescribeSupervisionResult(superviseResult));
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

    private static RunReviewDecision ResolveReviewDecision(CliContext context, string executionUnit)
    {
        var resultArtifact = TryReadDirectRunResultArtifact(context, executionUnit, "review");
        if (resultArtifact is null)
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Waiting,
                Detail = $"Review request exists for '{executionUnit}' but no direct run result is available yet."
            };
        }

        var runStatus = resultArtifact.RunStatus;
        if (string.Equals(runStatus, "failed", StringComparison.Ordinal))
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Failure,
                Detail = $"Review direct run failed for '{executionUnit}'."
            };
        }

        if (string.Equals(runStatus, "accepted", StringComparison.Ordinal)
            || string.Equals(runStatus, "approved", StringComparison.Ordinal)
            || string.Equals(runStatus, "succeeded", StringComparison.Ordinal))
        {
            return new RunReviewDecision
            {
                Kind = RunReviewDecisionKind.Accept,
                Detail = $"Review decision for '{executionUnit}' resolved to accept."
            };
        }

        if (string.Equals(runStatus, "comment", StringComparison.Ordinal)
            || string.Equals(runStatus, "commented", StringComparison.Ordinal)
            || string.Equals(runStatus, "fix-requested", StringComparison.Ordinal)
            || string.Equals(runStatus, "changes-requested", StringComparison.Ordinal))
        {
            var providerEvents = TryReadDirectRunProviderEvents(context, executionUnit);
            if (TryResolveReviewCommentBodyPath(context, executionUnit, providerEvents, out var commentBodyPath))
            {
                return new RunReviewDecision
                {
                    Kind = RunReviewDecisionKind.Comment,
                    CommentBodyPath = commentBodyPath,
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

    private static bool TryResolveReviewCommentBodyPath(
        CliContext context,
        string executionUnit,
        IReadOnlyList<DirectRunProviderEvent> providerEvents,
        out string commentBodyPath)
    {
        commentBodyPath = string.Empty;

        for (var index = providerEvents.Count - 1; index >= 0; index--)
        {
            if (!TryResolveCommentBody(providerEvents[index].Payload, out var bodyOrPath, out var isPath))
            {
                continue;
            }

            if (isPath)
            {
                commentBodyPath = bodyOrPath;
                return true;
            }

            var relativePath = $".intent-cli/reviews/{executionUnit}.comment.md";
            var absolutePath = Path.GetFullPath(Path.Combine(
                context.RepoRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var directoryPath = Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException("Review comment body path did not contain a directory.");
            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(absolutePath, bodyOrPath);
            commentBodyPath = relativePath;
            return true;
        }

        return false;
    }

    private static bool TryResolveCommentBody(JsonElement payload, out string bodyOrPath, out bool isPath)
    {
        bodyOrPath = string.Empty;
        isPath = false;

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadString(payload, "body_path", out var bodyPath))
        {
            bodyOrPath = bodyPath;
            isPath = true;
            return true;
        }

        if (TryReadString(payload, "comment_body", out var commentBody)
            || TryReadString(payload, "body", out commentBody)
            || TryReadString(payload, "markdown", out commentBody))
        {
            bodyOrPath = commentBody;
            return true;
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
