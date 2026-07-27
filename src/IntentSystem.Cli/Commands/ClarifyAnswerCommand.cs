using IntentSystem.Clarify;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ClarifyAnswerCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static Func<TextReader> InputReaderFactory { get; set; } = () => Console.In;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, writer, out var executionUnit, out var answerFilePath))
        {
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        var artifactPath = ResolveClarificationArtifactPath(context.RepoRoot, executionUnit);
        if (!File.Exists(artifactPath))
        {
            writer.WriteLine($"Clarification artifact was not found at {artifactPath}");
            return 1;
        }

        string answer;
        try
        {
            answer = ReadAnswer(context.RepoRoot, answerFilePath, writer);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        var timestamp = TimestampFactory();

        try
        {
            var clarification = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
            ValidateClarification(queueItem.ExecutionUnit, clarification);

            var answeredClarification = clarification with
            {
                Status = ClarificationStatus.Answered,
                Answer = answer,
                AnsweredAt = timestamp
            };

            var result = ClarifyGateway.Apply(answeredClarification, queueState, TransitionActor, timestamp);

            File.WriteAllText(artifactPath, ClarificationSerializer.Serialize(result.AppliedClarification));
            PersistQueueState(context, queueState, result.UpdatedQueueState);
            AppendRunEvents(context, result.Events);

            var resumedItem = result.UpdatedQueueState.Items.Single(item =>
                string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

            writer.WriteLine($"Clarification answered for {executionUnit}.");
            writer.WriteLine($"Artifact status: {FormatStatus(result.AppliedClarification.Status)}");
            writer.WriteLine($"Queue state: {FormatState(resumedItem.State)}");
            writer.WriteLine($"Artifact path: {artifactPath}");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        TextWriter writer,
        out string executionUnit,
        out string? answerFilePath)
    {
        executionUnit = string.Empty;
        answerFilePath = null;

        if (args.Length == 1 && !string.IsNullOrWhiteSpace(args[0]))
        {
            executionUnit = args[0];
            return true;
        }

        if (args.Length == 3
            && !string.IsNullOrWhiteSpace(args[0])
            && string.Equals(args[1], "--from-file", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(args[2]))
        {
            executionUnit = args[0];
            answerFilePath = args[2];
            return true;
        }

        writer.WriteLine("Clarify answer command requires an execution unit and optionally '--from-file <path>'.");
        return false;
    }

    private static string ReadAnswer(string repoRoot, string? answerFilePath, TextWriter writer)
    {
        if (answerFilePath is not null)
        {
            var resolvedPath = ResolveArtifactPath(repoRoot, answerFilePath);
            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException($"Clarification answer file was not found at {resolvedPath}");
            }

            var fileAnswer = File.ReadAllText(resolvedPath);
            if (string.IsNullOrWhiteSpace(fileAnswer))
            {
                throw new InvalidOperationException("Clarification answer file must not be empty.");
            }

            return fileAnswer.TrimEnd();
        }

        writer.Write("Clarification answer: ");
        var answer = InputReaderFactory().ReadLine();
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("Clarification answer must not be empty.");
        }

        return answer;
    }

    private static void ValidateClarification(string executionUnit, ClarificationItem clarification)
    {
        if (!string.Equals(clarification.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Clarification execution unit '{clarification.ExecutionUnit}' must match queue item execution unit '{executionUnit}'.");
        }

        if (clarification.Status != ClarificationStatus.Open)
        {
            throw new InvalidOperationException(
                $"Clarification '{clarification.QuestionId}' must be open before answering, but found '{clarification.Status}'.");
        }
    }

    private static string ResolveClarificationArtifactPath(string repoRoot, string executionUnit)
    {
        return ResolveArtifactPath(repoRoot, $".intent-cli/clarifications/{executionUnit}/request.json");
    }

    private static string ResolveArtifactPath(string repoRoot, string artifactRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRef);

        return Path.IsPathRooted(artifactRef)
            ? Path.GetFullPath(artifactRef)
            : Path.GetFullPath(Path.Combine(repoRoot, artifactRef.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void PersistQueueState(
        CliContext context, Supervisor.Models.QueueState baseState, Supervisor.Models.QueueState queueState)
    {
        var queueStatePath = context.GetQueueStatePath();
        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(queueStatePath, baseState, queueState);
    }

    private static void AppendRunEvents(CliContext context, IReadOnlyList<Supervisor.Models.RunEvent> events)
    {
        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);

        var serializedEvents = string.Join(
            Environment.NewLine,
            events.Select(RunLogSerializer.SerializeLine));

        File.AppendAllText(runLogPath, serializedEvents + Environment.NewLine);
    }

    private static string FormatStatus(ClarificationStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    private static string FormatState(Supervisor.Models.QueueItemState state)
    {
        return state switch
        {
            Supervisor.Models.QueueItemState.Review => "review",
            Supervisor.Models.QueueItemState.Queued => "queued",
            Supervisor.Models.QueueItemState.ClarifyBlocked => "clarify-blocked",
            _ => state.ToString().ToLowerInvariant()
        };
    }
}
