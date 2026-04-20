using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunFixCommand
{
    public static Func<IDirectRunLauncher> DirectRunLauncherFactory { get; set; } = () => new DirectRunLauncher();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run fix command requires an execution unit.");
            return 1;
        }

        try
        {
            var result = ExecuteCore(context, args[0]);
            RunFixRenderer.WriteSummary(writer, result.Request, result.ArtifactPath, result.DirectRun);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static RunFixResult ExecuteCore(CliContext context, string executionUnit)
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

        if (queueItem.State is not QueueItemState.Fixing)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must be fixing before run fix.");
        }

        if (queueItem.LinkedIssue is null)
        {
            throw new InvalidOperationException(
                $"Execution unit '{executionUnit}' must have a linked issue before run fix.");
        }

        var packetPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(packetPath))
        {
            throw new InvalidOperationException($"Projection packet artifact was not found at {packetPath}");
        }

        var reviewContextPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.ReviewContext);
        if (!File.Exists(reviewContextPath))
        {
            throw new InvalidOperationException($"Review context artifact was not found at {reviewContextPath}");
        }

        var reviewCommentArtifactRef = ReviewCommentArtifactPathResolver.Resolve(executionUnit);
        var reviewCommentArtifactPath = ResolveArtifactPath(context.RepoRoot, reviewCommentArtifactRef);
        if (!File.Exists(reviewCommentArtifactPath))
        {
            throw new InvalidOperationException($"Review comment artifact was not found at {reviewCommentArtifactPath}");
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

        PrepareWorktreeRuntimeContext(
            context,
            queueItem,
            packetPath,
            reviewContextPath,
            worktreePath);

        ChildWorkTargetGuard.EnsureTargetAllowed(
            executionUnit,
            context.RepoRoot,
            packet.ImplementationIssuePacket.TargetRepo,
            worktreePath,
            packet.ImplementationIssuePacket.TargetPath,
            packet.ImplementationIssuePacket.TargetPart);

        var reviewContext = ReviewContextMarkdownParser.Parse(File.ReadAllText(reviewContextPath));
        if (!string.Equals(reviewContext.SourceExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Review context execution unit '{reviewContext.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
        }

        var reviewCommentArtifact = ReviewCommentArtifactSerializer.Deserialize(
            File.ReadAllText(reviewCommentArtifactPath));
        if (!string.Equals(reviewCommentArtifact.ExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Review comment artifact execution unit '{reviewCommentArtifact.ExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
        }

        var latestLinkedPr = ResolveLatestLinkedPr(context, executionUnit);
        if (!string.Equals(reviewCommentArtifact.LinkedPr, latestLinkedPr, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Review comment artifact linked PR '{reviewCommentArtifact.LinkedPr}' must match latest linked PR '{latestLinkedPr}'.");
        }

        var request = BuildRequest(
            context,
            queueItem,
            packet,
            reviewContext,
            reviewCommentArtifactPath,
            reviewCommentArtifact,
            worktreePath,
            childRepoPath,
            latestLinkedPr);
        var markdown = RunFixRenderer.RenderMarkdown(request);
        var artifactPath = RunFixArtifactWriter.Write(markdown, executionUnit, context.RepoRoot, overwrite: true);
        var relativeArtifactPath = ToRelativePath(context.RepoRoot, artifactPath);
        var directRun = DirectRunCommandSupport.CreateAndLaunch(
            context,
            DirectRunEntryKind.Fix,
            executionUnit,
            relativeArtifactPath,
            worktreePath,
            DirectRunLauncherFactory(),
            TimestampFactory());

        return new RunFixResult
        {
            Request = request,
            ArtifactPath = relativeArtifactPath,
            DirectRun = directRun
        };
    }

    private static void PrepareWorktreeRuntimeContext(
        CliContext context,
        QueueItem queueItem,
        string packetPath,
        string reviewContextPath,
        string worktreePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(queueItem);
        ArgumentException.ThrowIfNullOrWhiteSpace(packetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewContextPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        SyncCurrentIssueArtifactsToWorktree(queueItem.ExecutionUnit, packetPath, worktreePath);
        SyncCurrentIssueArtifactsToWorktree(queueItem.ExecutionUnit, reviewContextPath, worktreePath);
        RemoveStaleWorktreeRootResultArtifact(context, queueItem.ExecutionUnit, worktreePath);
    }

    private static void SyncCurrentIssueArtifactsToWorktree(
        string executionUnit,
        string sourceArtifactPath,
        string worktreePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceArtifactPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        var sourceIssueDirectory = Path.GetDirectoryName(sourceArtifactPath)
            ?? throw new InvalidOperationException("Issue artifact path did not contain a directory.");
        if (!Directory.Exists(sourceIssueDirectory))
        {
            throw new InvalidOperationException($"Issue artifact directory was not found at {sourceIssueDirectory}");
        }

        var targetIssueDirectory = Path.Combine(worktreePath, ".intent-cli", "issues", executionUnit);
        Directory.CreateDirectory(targetIssueDirectory);

        foreach (var sourceFilePath in Directory.GetFiles(sourceIssueDirectory))
        {
            var targetFilePath = Path.Combine(targetIssueDirectory, Path.GetFileName(sourceFilePath));
            File.Copy(sourceFilePath, targetFilePath, overwrite: true);
        }
    }

    private static void RemoveStaleWorktreeRootResultArtifact(
        CliContext context,
        string executionUnit,
        string worktreePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreePath);

        var worktreeRunResultPath = Path.Combine(
            worktreePath,
            RunRootResultArtifactPathResolver.Resolve(context).Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(worktreeRunResultPath))
        {
            return;
        }

        var artifact = RunRootResultArtifactJson.Deserialize(File.ReadAllText(worktreeRunResultPath));
        if (string.IsNullOrWhiteSpace(artifact.ExecutionUnit)
            || string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            return;
        }

        File.Delete(worktreeRunResultPath);
    }

    private static RunFixRequest BuildRequest(
        CliContext context,
        QueueItem queueItem,
        Projection.Models.ProjectionPacketContract packet,
        Review.Models.ReviewContextSnapshot reviewContext,
        string reviewCommentArtifactRef,
        Review.Models.ReviewCommentArtifact reviewCommentArtifact,
        string worktreePath,
        string childRepoPath,
        string latestLinkedPr)
    {
        return new RunFixRequest
        {
            ExecutionUnit = queueItem.ExecutionUnit,
            State = FormatState(queueItem.State),
            ImplementRole = context.Config.Roles.Implement,
            QueueWorkerRole = queueItem.WorkerRole,
            QueueReviewRole = queueItem.ReviewRole,
            WorktreePath = worktreePath,
            ChildRepoPath = childRepoPath,
            Branch = RunStartCommand.ResolveBranchName(queueItem.ExecutionUnit, queueItem.LinkedIssue!),
            LinkedIssue = queueItem.LinkedIssue!.Url,
            LatestLinkedPr = latestLinkedPr,
            LatestCommentRef = reviewCommentArtifact.CommentRef,
            PacketRef = queueItem.PacketPaths.Yaml,
            ReviewContextRef = queueItem.PacketPaths.ReviewContext,
            ReviewCommentArtifactRef = reviewCommentArtifactRef,
            ReviewRequestRef = ResolveArtifactPath(context.RepoRoot, reviewCommentArtifact.ReviewRequestRef),
            ReviewCommentBodyPath = ResolveArtifactPath(context.RepoRoot, reviewCommentArtifact.BodyPath),
            IssueTitle = packet.ImplementationIssuePacket.IssueTitle,
            Goal = packet.ImplementationIssuePacket.Goal,
            TargetPart = packet.ImplementationIssuePacket.TargetPart,
            TargetRepo = packet.ImplementationIssuePacket.TargetRepo,
            TargetPath = packet.ImplementationIssuePacket.TargetPath,
            InScope = packet.ImplementationIssuePacket.InScope,
            OutOfScope = packet.ImplementationIssuePacket.OutOfScope,
            AcceptanceCriteria = reviewContext.AcceptanceCriteria,
            DeterministicReviewChecks = reviewContext.DeterministicReviewChecks,
            ExpectedEvidence = reviewContext.ExpectedEvidence
        };
    }

    private static string ResolveLatestLinkedPr(CliContext context, string executionUnit)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            throw new InvalidOperationException($"Run log was not found at {runLogPath}");
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        return LatestLinkedPrResolver.Resolve(runEvents, executionUnit);
    }

    private static string ResolveArtifactPath(string repoRoot, string artifactRef)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ResolveChildRepoPath(string repoRoot, string childRepoRef)
    {
        return Path.IsPathRooted(childRepoRef)
            ? Path.GetFullPath(childRepoRef)
            : Path.GetFullPath(Path.Combine(repoRoot, childRepoRef));
    }

    private static string FormatState(QueueItemState state)
    {
        return state switch
        {
            QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }

    private static string ToRelativePath(string repoRoot, string absolutePath)
    {
        return Path.GetRelativePath(repoRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
    }
}
