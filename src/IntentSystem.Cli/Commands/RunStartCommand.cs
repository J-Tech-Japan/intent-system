using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunStartCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<IGitCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitCommandRunner();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run start command requires an execution unit.");
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

        if (queueItem.LinkedIssue is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must have a linked issue before run start.");
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

            var worktreePath = ResolveWorktreePath(context, executionUnit);
            if (Directory.Exists(worktreePath))
            {
                throw new InvalidOperationException($"Worktree path already exists at {worktreePath}");
            }

            var branchName = ResolveBranchName(executionUnit, queueItem.LinkedIssue);
            var gitRunner = GitCommandRunnerFactory();
            CreateWorktree(childRepoPath, worktreePath, branchName, gitRunner);

            var timestamp = TimestampFactory();
            var transition = QueueManager.Activate(queueState, executionUnit, TransitionActor, timestamp);
            PersistStart(context, transition);

            writer.WriteLine($"Run started for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static string ResolveBranchName(string executionUnit, LinkedIssue linkedIssue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(linkedIssue);

        return $"issue-{linkedIssue.Number}-{executionUnit.ToLowerInvariant()}";
    }

    internal static string ResolveWorktreePath(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return Path.GetFullPath(Path.Combine(context.ResolveWorktreeRootPath(), executionUnit));
    }

    private static string ResolveChildRepoPath(string repoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(repoRoot, childRepoRef));
    }

    private static void CreateWorktree(
        string childRepoPath,
        string worktreePath,
        string branchName,
        IGitCommandRunner gitRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childRepoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        ArgumentNullException.ThrowIfNull(gitRunner);

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)
            ?? throw new InvalidOperationException("Worktree path did not contain a directory."));

        RunGit(gitRunner, childRepoPath, ["fetch", "origin", "main"], "git fetch origin main failed.");
        RunGit(
            gitRunner,
            childRepoPath,
            ["worktree", "add", "-b", branchName, worktreePath, "origin/main"],
            "git worktree add failed.");
    }

    private static void PersistStart(CliContext context, QueueTransitionResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(result.UpdatedState));

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }

    private static void RunGit(
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
    }
}
