using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ClarifyOpenCommand
{
    private const string TransitionActor = "intent-cli";
    private const string ClarificationSource = "execution";
    private const string QuestionId = "request";
    private const string BlockingValue = "blocking";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Clarify open command requires an execution unit.");
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
            if (!string.Equals(
                    packet.ReviewContextPacket.SourceExecutionUnit,
                    queueItem.ExecutionUnit,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review context packet execution unit '{packet.ReviewContextPacket.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            if (!string.Equals(
                    packet.ReviewContextPacket.ClarificationReturnPath,
                    queueItem.ClarificationReturnPath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review context packet clarification return path '{packet.ReviewContextPacket.ClarificationReturnPath}' must match queue item clarification return path '{queueItem.ClarificationReturnPath}'.");
            }

            var reviewContext = ReviewContextMarkdownParser.Parse(File.ReadAllText(reviewContextPath));
            if (!string.Equals(reviewContext.SourceExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review context execution unit '{reviewContext.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            var timestamp = TimestampFactory();
            var reason = BuildReason(packet.ImplementationIssuePacket);
            var transition = QueueManager.TransitionBlocking(
                queueState,
                executionUnit,
                QueueItemState.ClarifyBlocked,
                reason,
                TransitionActor,
                timestamp);

            var clarification = BuildClarification(queueItem, packet, reviewContext, timestamp, reason);
            var artifactPath = PersistClarification(context.RepoRoot, clarification);
            PersistTransition(context, queueState, transition);

            writer.WriteLine($"Clarification opened for {executionUnit}.");
            writer.WriteLine($"Artifact path: {artifactPath}");
            writer.WriteLine($"Reason: {reason}");
            writer.WriteLine($"Clarification return path: {clarification.ClarificationReturnPath}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static ClarificationItem BuildClarification(
        QueueItem queueItem,
        Projection.Models.ProjectionPacketContract packet,
        Review.Models.ReviewContextSnapshot reviewContext,
        DateTimeOffset timestamp,
        string reason)
    {
        return new ClarificationItem
        {
            ClarificationSource = ClarificationSource,
            QuestionId = QuestionId,
            ExecutionUnit = queueItem.ExecutionUnit,
            QuestionText = BuildQuestionText(packet.ImplementationIssuePacket, reviewContext),
            Reason = reason,
            AffectedIntents = packet.ReviewContextPacket.IntentReferences,
            AffectedExecutionUnits = [queueItem.ExecutionUnit],
            BlockingOrNonblocking = BlockingValue,
            ClarificationReturnPath = queueItem.ClarificationReturnPath,
            Status = ClarificationStatus.Open,
            CreatedAt = timestamp
        };
    }

    private static string BuildQuestionText(
        Projection.Models.ImplementationIssuePacket issuePacket,
        Review.Models.ReviewContextSnapshot reviewContext)
    {
        var firstCheck = reviewContext.DeterministicReviewChecks.FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstCheck)
            ? $"Clarify blocker for {issuePacket.TargetPart}: {issuePacket.Goal}"
            : $"Clarify blocker for {issuePacket.TargetPart}: {firstCheck}";
    }

    private static string BuildReason(Projection.Models.ImplementationIssuePacket issuePacket)
    {
        return $"Clarification requested for {issuePacket.IssueTitle}: {issuePacket.Goal}";
    }

    private static string PersistClarification(string repoRoot, ClarificationItem clarification)
    {
        var artifactRelativePath = ResolveClarificationRequestPath(clarification.ExecutionUnit);
        var artifactPath = ResolveArtifactPath(repoRoot, artifactRelativePath);
        var directoryPath = Path.GetDirectoryName(artifactPath)
            ?? throw new InvalidOperationException("Clarification artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(artifactPath, ClarificationSerializer.Serialize(clarification));
        return artifactPath;
    }

    private static void PersistTransition(CliContext context, QueueState baseState, QueueTransitionResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(queueStatePath, baseState, result.UpdatedState);

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }

    private static string ResolveClarificationRequestPath(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        return $".intent-cli/clarifications/{executionUnit}/request.json";
    }

    private static string ResolveArtifactPath(string repoRoot, string artifactRef)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }
}
