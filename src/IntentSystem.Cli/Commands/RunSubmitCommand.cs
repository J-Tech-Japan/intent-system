using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunSubmitCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<IGitCommandRunner> GitCommandRunnerFactory { get; set; } = () => new GitCommandRunner();

    public static Func<IRunSubmitPublisher> PublisherFactory { get; set; } = () => new GhRunSubmitPublisher();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run submit command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0]);
            writer.WriteLine($"Run submitted for {result.ExecutionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static RunSubmitResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            throw new InvalidOperationException($"Execution unit '{executionUnit}' was not found in queue state.");
        }

        if (queueItem.LinkedIssue is null)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must have a linked issue before run submit.");
        }

        var packetPath = Path.Combine(
            context.RepoRoot,
            queueItem.PacketPaths.Yaml.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

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

        var branchName = RunStartCommand.ResolveBranchName(executionUnit, queueItem.LinkedIssue);
        var gitRunner = GitCommandRunnerFactory();
        EnsureBranchMatches(worktreePath, branchName, gitRunner);
        EnsureCarryForwardCommit(executionUnit, worktreePath, branchName, gitRunner);
        PushBranch(worktreePath, branchName, gitRunner);

        var targetRepo = ResolveGitHubTargetRepo(childRepoPath, gitRunner);
        var pullRequestTitle = ResolvePullRequestTitle(queueItem);
        var pullRequestBody = ResolvePullRequestBody(queueItem);
        var linkedPr = PublisherFactory().CreateDraftPullRequest(
            targetRepo,
            branchName,
            pullRequestTitle,
            pullRequestBody);

        var timestamp = TimestampFactory();
        var transition = QueueManager.SubmitForReview(
            queueState,
            executionUnit,
            TransitionActor,
            timestamp,
            linkedPr);
        PersistSubmit(context, transition);

        return new RunSubmitResult
        {
            ExecutionUnit = executionUnit,
            LinkedPr = linkedPr
        };
    }

    internal static string ResolvePullRequestTitle(QueueItem queueItem)
    {
        ArgumentNullException.ThrowIfNull(queueItem);
        return queueItem.Title;
    }

    internal static string ResolvePullRequestBody(QueueItem queueItem)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        if (queueItem.LinkedIssue is null)
        {
            throw new InvalidOperationException(
                $"Execution unit '{queueItem.ExecutionUnit}' must have a linked issue before resolving PR body.");
        }

        return
            $$"""
            ## Summary

            - {{queueItem.Title}}

            ## Linked Issue

            - {{queueItem.LinkedIssue.Url}}
            """;
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

    private static void EnsureCarryForwardCommit(
        string executionUnit,
        string worktreePath,
        string branchName,
        IGitCommandRunner gitRunner)
    {
        if (!RunWorktreeProgressSupport.TryResolveMeaningfulWorktreeDiffPaths(
                gitRunner,
                worktreePath,
                out var changedPaths))
        {
            return;
        }

        var addArguments = new List<string> { "add", "--" };
        addArguments.AddRange(changedPaths);
        var addResult = RunGit(
            gitRunner,
            worktreePath,
            addArguments,
            "git add failed.");

        var diffResult = gitRunner.Run(worktreePath, ["diff", "--cached", "--quiet"]);
        if (diffResult.ExitCode != 0 && diffResult.ExitCode != 1)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(diffResult.StdErr)
                    ? "git diff --cached --quiet failed."
                    : diffResult.StdErr.Trim());
        }

        if (diffResult.ExitCode == 0)
        {
            return;
        }

        var commitResult = RunGit(
            gitRunner,
            worktreePath,
            ["commit", "-m", $"Carry forward succeeded implement progress for {executionUnit}"],
            "git commit failed.");
    }

    private static string ResolveGitHubTargetRepo(string childRepoPath, IGitCommandRunner gitRunner)
    {
        var remoteResult = RunGit(
            gitRunner,
            childRepoPath,
            ["remote", "get-url", "origin"],
            "git remote get-url origin failed.");

        return GitHubRepositoryTargetResolver.ParseRemoteUrl(remoteResult.StdOut.Trim());
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

    private static void PersistSubmit(CliContext context, QueueTransitionResult result)
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
}
