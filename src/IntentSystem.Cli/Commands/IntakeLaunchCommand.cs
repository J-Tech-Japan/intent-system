using IntentSystem.Supervisor.Serialization;

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

            var existingUnits = queueState.Items
                .Select(item => item.ExecutionUnit)
                .ToHashSet(StringComparer.Ordinal);
            var skippedUnits = executionUnits
                .Where(existingUnits.Contains)
                .ToArray();
            var launchUnits = executionUnits
                .Where(unit => !existingUnits.Contains(unit))
                .ToArray();

            ValidateArtifacts(context, launchUnits);

            var result = LaunchUnits(context, domain, launchUnits, skippedUnits);
            IntakeLaunchRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void ValidateArtifacts(CliContext context, IReadOnlyList<string> executionUnits)
    {
        foreach (var executionUnit in executionUnits)
        {
            var packetPath = QueueEnqueueCommand.ResolvePacketPath(context, executionUnit);
            if (!File.Exists(packetPath))
            {
                throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
            }

            var packetRef = QueueEnqueueCommand.ResolvePacketPaths(context.RepoRoot, executionUnit).Yaml;
            var githubBodyPath = QueueDispatchCommand.ResolveGitHubBodyPath(context.RepoRoot, packetRef);
            if (!File.Exists(githubBodyPath))
            {
                throw new InvalidOperationException($"GitHub issue body artifact was not found at {githubBodyPath}");
            }
        }
    }

    private static IntakeLaunchResult LaunchUnits(
        CliContext context,
        string domain,
        IReadOnlyList<string> launchUnits,
        IReadOnlyList<string> skippedUnits)
    {
        var launchedExecutionUnits = new List<string>(launchUnits.Count);
        var createdIssueRefs = new List<string>(launchUnits.Count);
        var worktreePaths = new List<string>(launchUnits.Count);

        foreach (var executionUnit in launchUnits)
        {
            ExecuteStep(
                (ctx, stepWriter) => QueueEnqueueCommand.Execute(ctx, [executionUnit], stepWriter),
                context,
                executionUnit,
                "queue enqueue");
            ExecuteStep(
                (ctx, stepWriter) => QueueDispatchCommand.Execute(ctx, [executionUnit], stepWriter),
                context,
                executionUnit,
                "queue dispatch");
            ExecuteStep(
                (ctx, stepWriter) => RunStartCommand.Execute(ctx, [executionUnit], stepWriter),
                context,
                executionUnit,
                "run start");

            var queueItem = LoadQueueItem(context, executionUnit);
            if (queueItem.LinkedIssue is null)
            {
                throw new InvalidOperationException(
                    $"Execution unit '{executionUnit}' must have a linked issue after intake launch.");
            }

            launchedExecutionUnits.Add(executionUnit);
            createdIssueRefs.Add(queueItem.LinkedIssue.Url);
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

    private static IntentSystem.Supervisor.Models.QueueItem LoadQueueItem(CliContext context, string executionUnit)
    {
        var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(context.GetQueueStatePath()));
        return queueState.Items.FirstOrDefault(item =>
                   string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
               ?? throw new InvalidOperationException(
                   $"Execution unit '{executionUnit}' was not found in queue state after intake launch.");
    }
}
