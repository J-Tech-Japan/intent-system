using IntentSystem.Supervisor.Serialization;
using QueueItem = IntentSystem.Supervisor.Models.QueueItem;
using QueueItemState = IntentSystem.Supervisor.Models.QueueItemState;

namespace IntentSystem.Cli.Commands;

internal static class IntakeLaunchCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake launch command requires a domain.");
            return 1;
        }

        var domain = args[0].Trim();
        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        try
        {
            var executionUnits = IntakeEnqueueCommand.LoadExecutionUnits(context.RepoRoot, domain);
            if (executionUnits.Count == 0)
            {
                writer.WriteLine($"No generated issue-ready execution units were found for domain '{domain}'.");
                return 1;
            }

            var result = LaunchUnits(context, domain, executionUnits);
            IntakeLaunchRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void ValidatePacketArtifact(CliContext context, string executionUnit)
    {
        var packetPath = QueueEnqueueCommand.ResolvePacketPath(context, executionUnit);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }
    }

    private static void ValidateGitHubBodyArtifact(CliContext context, string executionUnit)
    {
        var packetRef = QueueEnqueueCommand.ResolvePacketPaths(context.RepoRoot, executionUnit).Yaml;
        var githubBodyPath = QueueDispatchCommand.ResolveGitHubBodyPath(context.RepoRoot, packetRef);
        if (!File.Exists(githubBodyPath))
        {
            throw new InvalidOperationException($"GitHub issue body artifact was not found at {githubBodyPath}");
        }
    }

    private static IntakeLaunchResult LaunchUnits(
        CliContext context,
        string domain,
        IReadOnlyList<string> executionUnits)
    {
        var launchedExecutionUnits = new List<string>(executionUnits.Count);
        var createdIssueRefs = new List<string>(executionUnits.Count);
        var worktreePaths = new List<string>(executionUnits.Count);
        var skippedUnits = new List<string>();

        foreach (var executionUnit in executionUnits)
        {
            var queueItem = TryLoadQueueItem(context, executionUnit);
            var shouldEnqueue = queueItem is null;
            var requiresDispatch = shouldEnqueue || queueItem?.LinkedIssue is null;

            if (queueItem is not null && IsAlreadyLaunched(queueItem.State))
            {
                skippedUnits.Add(executionUnit);
                continue;
            }

            ValidatePacketArtifact(context, executionUnit);
            if (requiresDispatch)
            {
                ValidateGitHubBodyArtifact(context, executionUnit);
            }

            if (shouldEnqueue)
            {
                ExecuteStep(
                    (ctx, stepWriter) => QueueEnqueueCommand.Execute(ctx, [executionUnit], stepWriter),
                    context,
                    executionUnit,
                    "queue enqueue");
                queueItem = LoadQueueItem(context, executionUnit);
            }

            queueItem ??= LoadQueueItem(context, executionUnit);
            if (queueItem.State != QueueItemState.Queued)
            {
                throw new InvalidOperationException(
                    $"Execution unit '{executionUnit}' is in state '{FormatState(queueItem.State)}' and cannot be intake-launched.");
            }

            if (queueItem.LinkedIssue is null)
            {
                ExecuteStep(
                    (ctx, stepWriter) => QueueDispatchCommand.Execute(ctx, [executionUnit], stepWriter),
                    context,
                    executionUnit,
                    "queue dispatch");
            }

            ExecuteStep(
                (ctx, stepWriter) => RunStartCommand.Execute(ctx, [executionUnit], stepWriter),
                context,
                executionUnit,
                "run start");

            var launchedQueueItem = LoadQueueItem(context, executionUnit);
            if (launchedQueueItem.LinkedIssue is null)
            {
                throw new InvalidOperationException(
                    $"Execution unit '{executionUnit}' must have a linked issue after intake launch.");
            }

            launchedExecutionUnits.Add(executionUnit);
            if (queueItem.LinkedIssue is null)
            {
                createdIssueRefs.Add(launchedQueueItem.LinkedIssue.Url);
            }

            worktreePaths.Add(RunStartCommand.ResolveWorktreePath(context, executionUnit));
        }

        return new IntakeLaunchResult
        {
            Domain = domain,
            LaunchedExecutionUnits = launchedExecutionUnits,
            CreatedIssueRefs = createdIssueRefs,
            WorktreePaths = worktreePaths,
            SkippedUnits = skippedUnits
        };
    }

    private static void ExecuteStep(
        Func<CliContext, StringWriter, int> step,
        CliContext context,
        string executionUnit,
        string stepName)
    {
        var stepWriter = new StringWriter();
        var exitCode = step(context, stepWriter);

        if (exitCode != 0)
        {
            var detail = stepWriter.ToString().TrimEnd();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Intake launch failed during {stepName} for '{executionUnit}'."
                    : $"Intake launch failed during {stepName} for '{executionUnit}': {detail}");
        }
    }

    private static QueueItem? TryLoadQueueItem(CliContext context, string executionUnit)
    {
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(context.GetQueueStatePath()));
        return queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
    }

    private static QueueItem LoadQueueItem(CliContext context, string executionUnit)
    {
        return TryLoadQueueItem(context, executionUnit)
               ?? throw new InvalidOperationException(
                   $"Execution unit '{executionUnit}' was not found in queue state after intake launch.");
    }

    private static bool IsAlreadyLaunched(QueueItemState state)
    {
        return state is QueueItemState.Active
            or QueueItemState.Review
            or QueueItemState.Fixing
            or QueueItemState.ClarifyBlocked
            or QueueItemState.Completed;
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }
}
