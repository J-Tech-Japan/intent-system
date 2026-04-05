using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunImplementCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Run implement command requires an execution unit.");
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

        if (queueItem.State is not (QueueItemState.Active or QueueItemState.Fixing))
        {
            writer.WriteLine(
                $"Execution unit '{executionUnit}' must be active or fixing before run implement.");
            return 1;
        }

        if (queueItem.LinkedIssue is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' must have a linked issue before run implement.");
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

            var latestLinkedPr = ResolveLatestLinkedPr(context, executionUnit);
            var request = BuildRequest(
                context,
                queueItem,
                packet,
                reviewContext,
                worktreePath,
                childRepoPath,
                latestLinkedPr);
            var markdown = RunImplementRenderer.RenderMarkdown(request);
            var artifactPath = RunImplementArtifactWriter.Write(markdown, executionUnit, context.RepoRoot, overwrite: true);
            RunImplementRenderer.WriteSummary(writer, request, artifactPath);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static RunImplementRequest BuildRequest(
        CliContext context,
        QueueItem queueItem,
        Projection.Models.ProjectionPacketContract packet,
        Review.Models.ReviewContextSnapshot reviewContext,
        string worktreePath,
        string childRepoPath,
        string? latestLinkedPr)
    {
        return new RunImplementRequest
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
            PacketRef = queueItem.PacketPaths.Yaml,
            ReviewContextRef = queueItem.PacketPaths.ReviewContext,
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

    private static string? ResolveLatestLinkedPr(CliContext context, string executionUnit)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return null;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        return LatestLinkedPrResolver.TryResolve(runEvents, executionUnit);
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
