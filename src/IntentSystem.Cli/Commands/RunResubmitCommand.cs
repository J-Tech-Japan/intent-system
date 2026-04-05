using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunResubmitCommand
{
    private const string EventActor = "intent-cli";

    public static Func<IGitCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitCommandRunner();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run resubmit command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = args[0];
        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        if (queueItem.State is not QueueItemState.Fixing)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must be fixing before run resubmit.");
            return 1;
        }

        if (queueItem.LinkedIssue is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must have a linked issue before run resubmit.");
            return 1;
        }

        var packetPath = Path.Combine(
            context.RepoRoot,
            queueItem.PacketPaths.Yaml.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packetPath))
        {
            writer.WriteLine($"Projection packet artifact was not found at {packetPath}");
            return 1;
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            writer.WriteLine($"Run log was not found at {runLogPath}");
            return 1;
        }

        try
        {
            var packet = ProjectionPacketSerializer.Deserialize(File.ReadAllText(packetPath));
            var childRepoRef = packet.ImplementationIssuePacket.TargetRepo;
            if (string.IsNullOrWhiteSpace(childRepoRef))
            {
                throw new InvalidOperationException("Projection packet must contain a target repo.");
            }

            var childRepoPath = ResolveChildRepoPath(context.RepoRoot, childRepoRef);
            if (!Directory.Exists(childRepoPath))
            {
                throw new InvalidOperationException($"Child repo path was not found at {childRepoPath}");
            }

            var worktreePath = RunStartCommand.ResolveWorktreePath(context, executionUnit);
            if (!Directory.Exists(worktreePath))
            {
                throw new InvalidOperationException($"Worktree path was not found at {worktreePath}");
            }

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var linkedPr = LatestLinkedPrResolver.Resolve(runEvents, executionUnit);

            var branchName = RunStartCommand.ResolveBranchName(executionUnit, queueItem.LinkedIssue);
            var gitRunner = GitCommandRunnerFactory();
            EnsureBranchMatches(worktreePath, branchName, gitRunner);
            PushBranch(worktreePath, branchName, gitRunner);

            PersistRunEvent(
                runLogPath,
                new RunEvent
                {
                    Ts = TimestampFactory(),
                    ExecutionUnit = executionUnit,
                    Event = "resubmitted",
                    By = EventActor,
                    LinkedPr = linkedPr
                });

            writer.WriteLine($"Run resubmitted for {executionUnit}.");
            writer.WriteLine($"Branch: {branchName}");
            writer.WriteLine($"Worktree path: {worktreePath}");
            writer.WriteLine($"Latest linked PR: {linkedPr}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ResolveChildRepoPath(string repoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(repoRoot, childRepoRef));
    }

    private static void EnsureBranchMatches(
        string worktreePath,
        string expectedBranchName,
        IGitCommandRunner gitRunner)
    {
        var branchResult = RunGit(
            gitRunner,
            worktreePath,
            ["rev-parse", "--abbrev-ref", "HEAD"],
            "git rev-parse --abbrev-ref HEAD failed.");
        var currentBranch = branchResult.StdOut.Trim();

        if (!string.Equals(currentBranch, expectedBranchName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Current worktree branch '{currentBranch}' must match expected branch '{expectedBranchName}'.");
        }
    }

    private static void PushBranch(
        string worktreePath,
        string branchName,
        IGitCommandRunner gitRunner)
    {
        RunGit(
            gitRunner,
            worktreePath,
            ["push", "-u", "origin", branchName],
            "git push failed.");
    }

    private static GitCommandResult RunGit(
        IGitCommandRunner gitRunner,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        string defaultError)
    {
        var result = gitRunner.Run(workingDirectory, arguments);
        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? defaultError
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        return result;
    }

    private static void PersistRunEvent(string runLogPath, RunEvent runEvent)
    {
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }
}
