using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunFixCommand
{
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
            writer.WriteLine(
                $"Execution unit '{executionUnit}' must be fixing before run fix.");
            return 1;
        }

        if (queueItem.LinkedIssue is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must have a linked issue before run fix.");
            return 1;
        }

        var packetPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.Yaml);
        if (!File.Exists(packetPath))
        {
            writer.WriteLine($"Projection packet artifact was not found at {packetPath}");
            return 1;
        }

        var reviewContextPath = ResolveArtifactPath(context.RepoRoot, queueItem.PacketPaths.ReviewContext);
        if (!File.Exists(reviewContextPath))
        {
            writer.WriteLine($"Review context artifact was not found at {reviewContextPath}");
            return 1;
        }

        var reviewCommentArtifactRef = ReviewCommentArtifactPathResolver.Resolve(executionUnit);
        var reviewCommentArtifactPath = ResolveArtifactPath(context.RepoRoot, reviewCommentArtifactRef);
        if (!File.Exists(reviewCommentArtifactPath))
        {
            writer.WriteLine($"Review comment artifact was not found at {reviewCommentArtifactPath}");
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
                reviewCommentArtifactRef,
                reviewCommentArtifact,
                worktreePath,
                childRepoPath,
                latestLinkedPr);
            var markdown = RunFixRenderer.RenderMarkdown(request);
            var artifactPath = RunFixArtifactWriter.Write(markdown, executionUnit, context.RepoRoot, overwrite: true);
            RunFixRenderer.WriteSummary(writer, request, artifactPath);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
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
            ReviewRequestRef = reviewCommentArtifact.ReviewRequestRef,
            ReviewCommentBodyPath = reviewCommentArtifact.BodyPath,
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
}
