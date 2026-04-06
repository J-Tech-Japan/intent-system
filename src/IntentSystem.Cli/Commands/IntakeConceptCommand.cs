using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeConceptCommand
{
    public static Func<TextReader> InputReaderFactory { get; set; } = () => Console.In;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, writer, out var domain, out var conceptFilePath))
        {
            return 1;
        }

        string conceptText;
        try
        {
            conceptText = ReadConceptInput(context.RepoRoot, conceptFilePath, writer);
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        var packet = new ConceptIntakePacket
        {
            DomainSlug = domain,
            ConceptSource = conceptFilePath is null ? "interactive" : "from-file",
            ConceptText = conceptText,
            UpstreamPaths = [],
            InitialGoal = DeriveInitialGoal(conceptText),
            Constraints = [],
            KnownUnknowns = []
        };

        var yaml = IntakeConceptArtifactYaml.Serialize(packet);
        var artifactPath = IntakeConceptArtifactWriter.Write(yaml, domain, context.RepoRoot);
        IntakeConceptRenderer.WriteSummary(writer, packet, artifactPath);
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        TextWriter writer,
        out string domain,
        out string? conceptFilePath)
    {
        domain = string.Empty;
        conceptFilePath = null;

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
            conceptFilePath = args[2];
            return true;
        }

        writer.WriteLine("Intake concept command requires a domain and optionally '--from-file <path>'.");
        return false;
    }

    private static string ReadConceptInput(string repoRoot, string? conceptFilePath, TextWriter writer)
    {
        if (conceptFilePath is not null)
        {
            var resolvedPath = ResolvePath(repoRoot, conceptFilePath);
            if (!File.Exists(resolvedPath))
            {
                throw new InvalidOperationException($"Intake concept file was not found at {resolvedPath}");
            }

            var fileConcept = File.ReadAllText(resolvedPath);
            if (string.IsNullOrWhiteSpace(fileConcept))
            {
                throw new InvalidOperationException("Intake concept file must not be empty.");
            }

            return fileConcept.TrimEnd();
        }

        writer.Write("Concept input: ");
        var concept = InputReaderFactory().ReadToEnd();
        if (string.IsNullOrWhiteSpace(concept))
        {
            throw new InvalidOperationException("Concept input must not be empty.");
        }

        return concept.TrimEnd('\r', '\n');
    }

    private static string ResolvePath(string repoRoot, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DeriveInitialGoal(string conceptText)
    {
        var firstLine = conceptText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        return string.IsNullOrWhiteSpace(firstLine)
            ? conceptText
            : firstLine.Trim();
    }
}
