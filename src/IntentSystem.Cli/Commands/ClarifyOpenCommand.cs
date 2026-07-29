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

        if (!TryParseArguments(args, out var parsed, out var parseError))
        {
            writer.WriteLine(parseError);
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = parsed.ExecutionUnit;
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

            var clarification = BuildClarification(queueItem, packet, reviewContext, timestamp, reason, parsed);
            var artifactPath = PersistClarification(context.RepoRoot, clarification);
            PersistTransition(context, queueState, transition);

            writer.WriteLine($"Clarification opened for {executionUnit}.");
            writer.WriteLine($"Artifact path: {artifactPath}");
            // G552: echo what was actually PERSISTED (question, and the reason
            // including any labeled recommendation/evidence), not the
            // pre-composition reason — the operator needs to see the durable
            // record, since that is what the detector and design will read.
            writer.WriteLine($"Question: {clarification.QuestionText}");
            writer.WriteLine($"Reason: {clarification.Reason}");
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
        string reason,
        ClarifyOpenInputs inputs)
    {
        return new ClarificationItem
        {
            ClarificationSource = ClarificationSource,
            QuestionId = QuestionId,
            ExecutionUnit = queueItem.ExecutionUnit,
            // G552: an explicitly supplied question is the REAL design-blocking
            // question and always wins over the packet-derived synthesis. The
            // OPEN artifact itself must carry it — an agmsg message may notify,
            // but it can never substitute for the durable record.
            QuestionText = string.IsNullOrWhiteSpace(inputs.Question)
                ? BuildQuestionText(packet.ImplementationIssuePacket, reviewContext)
                : inputs.Question!,
            Reason = AppendRecommendation(reason, inputs),
            AffectedIntents = packet.ReviewContextPacket.IntentReferences,
            AffectedExecutionUnits = [queueItem.ExecutionUnit],
            BlockingOrNonblocking = BlockingValue,
            ClarificationReturnPath = queueItem.ClarificationReturnPath,
            Status = ClarificationStatus.Open,
            CreatedAt = timestamp
        };
    }

    /// <summary>
    /// G552: the explicit inputs that let a design-decision hold record its
    /// REAL question (and, when the asking thread already believes it knows the
    /// answer, its recommendation and the facts behind it) in the OPEN
    /// clarification artifact. All optional — omitting them preserves the
    /// pre-G552 packet-derived behavior byte for byte, so every existing caller
    /// and fixture is unaffected. No clarification schema change: the question
    /// lands in <c>QuestionText</c> and the recommendation/evidence land in the
    /// already-serialized <c>Reason</c> field under explicit labels.
    /// </summary>
    private sealed record ClarifyOpenInputs
    {
        public required string ExecutionUnit { get; init; }

        public string? Question { get; init; }

        public string? RecommendedAnswer { get; init; }

        public string? Evidence { get; init; }
    }

    private const string RecommendedAnswerLabel = "Recommended answer:";

    private const string EvidenceLabel = "Evidence:";

    private const string UsageLine =
        "Usage: intent-cli clarify open <execution-unit> [--question <text>] [--recommended-answer <text>] [--evidence <text>]";

    /// <summary>
    /// G552: composes the durable <c>Reason</c> so the recommendation and its
    /// evidence survive in the OPEN artifact under labels a reader (and a
    /// reviewer) can find. The packet-derived reason always stays first, so the
    /// existing content is never displaced — only extended.
    /// </summary>
    private static string AppendRecommendation(string reason, ClarifyOpenInputs inputs)
    {
        var builder = new System.Text.StringBuilder(reason);

        if (!string.IsNullOrWhiteSpace(inputs.RecommendedAnswer))
        {
            builder.Append(' ').Append(RecommendedAnswerLabel).Append(' ').Append(inputs.RecommendedAnswer!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(inputs.Evidence))
        {
            builder.Append(' ').Append(EvidenceLabel).Append(' ').Append(inputs.Evidence!.Trim());
        }

        return builder.ToString();
    }

    private static bool TryParseArguments(string[] args, out ClarifyOpenInputs parsed, out string error)
    {
        parsed = new ClarifyOpenInputs { ExecutionUnit = string.Empty };
        error = string.Empty;

        string? executionUnit = null;
        string? question = null;
        string? recommendedAnswer = null;
        string? evidence = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--question":
                case "--recommended-answer":
                case "--evidence":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"{argument} requires a value.";
                        return false;
                    }

                    var value = args[index + 1];
                    if (argument == "--question")
                    {
                        question = value;
                    }
                    else if (argument == "--recommended-answer")
                    {
                        recommendedAnswer = value;
                    }
                    else
                    {
                        evidence = value;
                    }

                    index++;
                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown argument '{argument}'. {UsageLine}";
                        return false;
                    }

                    if (executionUnit is not null)
                    {
                        error = $"Clarify open command accepts a single execution unit. {UsageLine}";
                        return false;
                    }

                    executionUnit = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "Clarify open command requires an execution unit.";
            return false;
        }

        parsed = new ClarifyOpenInputs
        {
            ExecutionUnit = executionUnit,
            Question = question,
            RecommendedAnswer = recommendedAnswer,
            Evidence = evidence,
        };
        return true;
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
