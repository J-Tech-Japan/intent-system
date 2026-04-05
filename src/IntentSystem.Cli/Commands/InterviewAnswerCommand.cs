using IntentSystem.ConceptIntake.Interview;

namespace IntentSystem.Cli.Commands;

internal static class InterviewAnswerCommand
{
    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static Func<TextReader> InputReaderFactory { get; set; } = () => Console.In;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, writer, out var domain, out var answerFilePath))
        {
            return 1;
        }

        var artifacts = InterviewArtifactYaml.Discover(context.RepoRoot, domain);
        var selectedArtifact = artifacts
            .OrderBy(artifact => artifact.Item.BlockingOrNonblocking == "blocking" ? 0 : 1)
            .ThenBy(artifact => artifact.Item.CreatedAt)
            .ThenBy(artifact => artifact.Item.QuestionId, StringComparer.Ordinal)
            .FirstOrDefault(artifact => artifact.Item.Status == ConceptIntake.Models.InterviewQueueItemStatus.Open);

        if (selectedArtifact is null)
        {
            writer.WriteLine($"No open interview questions found for domain '{domain}'.");
            return 0;
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
            var result = InterviewQueue.ApplyAnswer(
                selectedArtifact.Item,
                answer,
                selectedArtifact.Item.RecommendedUpdates ?? [],
                timestamp);

            File.WriteAllText(
                selectedArtifact.ArtifactPath,
                InterviewArtifactYaml.Serialize(result.AnsweredItem));

            InterviewAnswerRenderer.Write(writer, domain, result);
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
        out string domain,
        out string? answerFilePath)
    {
        domain = string.Empty;
        answerFilePath = null;

        if (args.Length == 1 && !string.IsNullOrWhiteSpace(args[0]))
        {
            domain = args[0];
            return true;
        }

        if (args.Length == 3
            && !string.IsNullOrWhiteSpace(args[0])
            && string.Equals(args[1], "--from-file", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(args[2]))
        {
            domain = args[0];
            answerFilePath = args[2];
            return true;
        }

        writer.WriteLine("Interview answer command requires a domain and optionally '--from-file <path>'.");
        return false;
    }

    private static string ReadAnswer(string repoRoot, string? answerFilePath, TextWriter writer)
    {
        if (answerFilePath is not null)
        {
            var resolvedPath = ResolvePath(repoRoot, answerFilePath);
            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException($"Interview answer file was not found at {resolvedPath}");
            }

            var fileAnswer = File.ReadAllText(resolvedPath);
            if (string.IsNullOrWhiteSpace(fileAnswer))
            {
                throw new InvalidOperationException("Interview answer file must not be empty.");
            }

            return fileAnswer.TrimEnd();
        }

        writer.Write("Interview answer: ");
        var answer = InputReaderFactory().ReadLine();
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("Interview answer must not be empty.");
        }

        return answer;
    }

    private static string ResolvePath(string repoRoot, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
