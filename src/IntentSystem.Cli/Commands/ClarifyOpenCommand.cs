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
            // G561: read only the facts a clarification record needs. The full
            // projection contract is NOT applied here — a packet freshly
            // scaffolded by `packet draft` has no review_context_packet section
            // and has not filled in most implementation fields, and refusing to
            // record a blocking question because the packet is still a draft
            // defeats the point of asking early. The identity check below is
            // kept absolute; only the incidental fields became optional.
            var packet = ClarifyPacketFacts.Read(File.ReadAllText(packetPath));

            // A packet that DOES carry the review-context section is still
            // validated exactly as strictly as before, in the same order and
            // with the same messages — an existing artifact loses no guard and
            // sees no changed diagnostic.
            if (packet.HasReviewContextSection)
            {
                if (!string.Equals(
                        packet.ReviewContextSourceExecutionUnit,
                        queueItem.ExecutionUnit,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Review context packet execution unit '{packet.ReviewContextSourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
                }

                if (!string.Equals(
                        packet.ReviewContextClarificationReturnPath,
                        queueItem.ClarificationReturnPath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Review context packet clarification return path '{packet.ReviewContextClarificationReturnPath}' must match queue item clarification return path '{queueItem.ClarificationReturnPath}'.");
                }
            }

            // The strict serializer used to assert that the two sections named
            // the SAME unit; checking each of them against the queue item is
            // equivalent for a complete packet and is the only available guard
            // for a scaffold, which has one section.
            if (!string.Equals(packet.SourceExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Projection packet execution unit '{packet.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            var reviewContext = ReadReviewContext(File.ReadAllText(reviewContextPath), queueItem.ExecutionUnit);
            if (!string.Equals(reviewContext.SourceExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review context execution unit '{reviewContext.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            var timestamp = TimestampFactory();
            var reason = BuildReason(packet);
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

    /// <summary>
    /// G561: reads review-context.md for the two things a clarification needs —
    /// the execution-unit identity and the first deterministic check (used only
    /// to derive a question when the caller did not supply one).
    ///
    /// The canonical parser is still what reads it, so its execution-unit
    /// semantics are unchanged, including the G373 rule that a PRESENT but
    /// malformed <c># Execution Unit</c> section throws rather than silently
    /// falling back. The one accommodation is the required
    /// <c>## Deterministic Review Checks</c> section, which `packet draft`'s
    /// scaffold does not yet contain: an empty one is supplied so the rest can
    /// be read. An absent list of checks is a draft that has not been filled in,
    /// not a malformed artifact — and it costs only the derived question text,
    /// which <c>--question</c> overrides anyway.
    /// </summary>
    private static Review.Models.ReviewContextSnapshot ReadReviewContext(string markdown, string fallbackExecutionUnit)
    {
        const string requiredHeading = "Deterministic Review Checks";

        // Detected with the canonical parser's OWN heading rule (a line
        // starting with "# ", heading text after stripping '#'/' '), so this
        // never disagrees with what the parser will find.
        var hasChecksSection = markdown
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Any(line => line.StartsWith("# ", StringComparison.Ordinal)
                && string.Equals(line.TrimStart('#', ' '), requiredHeading, StringComparison.Ordinal));

        var readable = hasChecksSection
            ? markdown
            : markdown + Environment.NewLine + Environment.NewLine + "# " + requiredHeading + Environment.NewLine;

        return ReviewContextMarkdownParser.Parse(readable, fallbackExecutionUnit);
    }

    private static ClarificationItem BuildClarification(
        QueueItem queueItem,
        ClarifyPacketFacts packet,
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
                ? BuildQuestionText(packet, reviewContext)
                : inputs.Question!,
            Reason = AppendRecommendation(reason, inputs),
            // The review-context section stays the source when the packet has
            // one, so a complete packet produces the same record it always did;
            // a scaffold falls back to the implementation section's own list.
            AffectedIntents = packet.ReviewContextIntentReferences ?? packet.IntentReferences,
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

    /// <summary>
    /// G561: the derived texts degrade field by field rather than refusing. A
    /// scaffold has an execution unit and usually a title, and that is enough to
    /// name what is blocked; the placeholders below make the gap visible in the
    /// artifact instead of asserting detail the packet does not contain.
    /// Supplying <c>--question</c> bypasses this entirely.
    /// </summary>
    private static string BuildQuestionText(
        ClarifyPacketFacts packet,
        Review.Models.ReviewContextSnapshot reviewContext)
    {
        var subject = Describe(packet.TargetPart, packet.SourceExecutionUnit);
        var firstCheck = reviewContext.DeterministicReviewChecks.FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstCheck)
            ? $"Clarify blocker for {subject}: {Describe(packet.Goal, "(goal not yet recorded in the packet)")}"
            : $"Clarify blocker for {subject}: {firstCheck}";
    }

    private static string BuildReason(ClarifyPacketFacts packet)
    {
        var title = Describe(packet.IssueTitle, packet.SourceExecutionUnit);
        return $"Clarification requested for {title}: {Describe(packet.Goal, "(goal not yet recorded in the packet)")}";
    }

    private static string Describe(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;

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
