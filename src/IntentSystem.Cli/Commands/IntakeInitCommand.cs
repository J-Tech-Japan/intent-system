using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal static class IntakeInitCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, writer, out var request))
        {
            return 1;
        }

        try
        {
            var result = ExecuteCore(context.RepoRoot, request);
            IntakeInitRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntakeInitResult ExecuteCore(string repoRoot, IntakeInitRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(request);

        var resolvedWorkRepoPath = ResolvePath(repoRoot, request.WorkRepoPath);
        if (!Directory.Exists(resolvedWorkRepoPath))
        {
            throw new InvalidOperationException($"Work repo path was not found at {resolvedWorkRepoPath}");
        }

        var conceptText = request.Text is not null
            ? NormalizeInlineText(request.Text)
            : ReadConceptFile(repoRoot, request.FromFilePath!);
        var generatedPaths = new List<string>();
        var skippedPaths = new List<string>();

        TryWriteFile(
            repoRoot,
            $"{CliRuntimeContracts.IntentCliDirectoryName}/{CliRuntimeContracts.ConfigFileName}",
            RenderConfigToml(request.Domain, request.WorkRepoPath),
            generatedPaths,
            skippedPaths);
        TryWriteFile(
            repoRoot,
            $"intents/{request.Domain}/README.md",
            RenderDomainReadme(request.Domain, request.WorkRepoPath),
            generatedPaths,
            skippedPaths);
        TryWriteFile(
            repoRoot,
            $"intents/{request.Domain}/clarifications/open.md",
            RenderOpenClarifications(),
            generatedPaths,
            skippedPaths);
        TryWriteFile(
            repoRoot,
            $"intents/{request.Domain}/intent-tree/00-map.md",
            RenderIntentMap(request.Domain, request.WorkRepoPath),
            generatedPaths,
            skippedPaths);

        var packet = new ConceptIntakePacket
        {
            DomainSlug = request.Domain,
            ConceptSource = request.Text is null ? "from-file" : "text",
            ConceptText = conceptText,
            UpstreamPaths = [],
            InitialGoal = DeriveInitialGoal(conceptText),
            Constraints = [],
            KnownUnknowns = []
        };

        TryWriteFile(
            repoRoot,
            IntakeConceptArtifactPathResolver.Resolve(request.Domain),
            IntakeConceptArtifactYaml.Serialize(packet),
            generatedPaths,
            skippedPaths);

        var interviewResult = IntakeInterviewCommand.ExecuteCore(repoRoot, [request.Domain]);
        generatedPaths.AddRange(interviewResult.GeneratedArtifactPaths);
        skippedPaths.AddRange(interviewResult.ExistingArtifactPaths);

        return new IntakeInitResult
        {
            Domain = request.Domain,
            WorkRepoPath = resolvedWorkRepoPath,
            InterviewWasSkipped = interviewResult.WasSkipped,
            CreatedQuestionIds = interviewResult.CreatedQuestionIds,
            GeneratedPaths = generatedPaths,
            SkippedPaths = skippedPaths
        };
    }

    private static bool TryParseArguments(string[] args, TextWriter writer, out IntakeInitRequest request)
    {
        request = default!;

        if (args.Length < 5 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine(
                "Intake init command requires '<domain> --work-repo-path <path>' and exactly one of '--text <value>' or '--from-file <path>'.");
            return false;
        }

        var domain = args[0].Trim();
        string? text = null;
        string? fromFilePath = null;
        string? workRepoPath = null;

        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                writer.WriteLine($"Missing value for '{args[index]}'.");
                return false;
            }

            switch (args[index])
            {
                case "--text":
                    text = args[index + 1];
                    break;
                case "--from-file":
                    fromFilePath = args[index + 1];
                    break;
                case "--work-repo-path":
                    workRepoPath = args[index + 1];
                    break;
                default:
                    writer.WriteLine($"Unknown intake init option '{args[index]}'.");
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(workRepoPath))
        {
            writer.WriteLine("Intake init command requires '--work-repo-path <path>'.");
            return false;
        }

        if ((text is null && fromFilePath is null) || (text is not null && fromFilePath is not null))
        {
            writer.WriteLine(
                "Intake init command requires exactly one of '--text <value>' or '--from-file <path>'.");
            return false;
        }

        request = new IntakeInitRequest
        {
            Domain = domain,
            Text = text,
            FromFilePath = fromFilePath,
            WorkRepoPath = workRepoPath.Trim()
        };
        return true;
    }

    private static string ReadConceptFile(string repoRoot, string conceptFilePath)
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

    private static string NormalizeInlineText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Intake init text input must not be empty.");
        }

        return text.TrimEnd('\r', '\n');
    }

    private static string ResolvePath(string repoRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool TryWriteFile(
        string repoRoot,
        string relativePath,
        string contents,
        ICollection<string> generatedPaths,
        ICollection<string> skippedPaths)
    {
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolutePath))
        {
            skippedPaths.Add(relativePath);
            return false;
        }

        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException($"Path '{absolutePath}' did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, contents);
        generatedPaths.Add(relativePath);
        return true;
    }

    private static string RenderConfigToml(string domain, string workRepoPath)
    {
        return $$"""
        [project]
        domain = "{{EscapeTomlString(domain)}}"
        artifact_root = ".intent-cli"
        worktree_root = ".intent-cli/worktrees"
        work_repo_path = "{{EscapeTomlString(workRepoPath)}}"
        """;
    }

    private static string RenderDomainReadme(string domain, string workRepoPath)
    {
        return $$"""
        # {{domain}}

        Work repo path: `{{workRepoPath}}`

        Bootstrapped by `intent-cli intake init`.
        """;
    }

    private static string RenderOpenClarifications()
    {
        return """
        # Open Clarifications

        - none
        """;
    }

    private static string RenderIntentMap(string domain, string workRepoPath)
    {
        return $$"""
        # Intent Map

        - Domain: `{{domain}}`
        - Work repo path: `{{workRepoPath}}`
        - Initial map: pending
        """;
    }

    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
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

    internal sealed record IntakeInitRequest
    {
        public required string Domain { get; init; }

        public string? Text { get; init; }

        public string? FromFilePath { get; init; }

        public required string WorkRepoPath { get; init; }
    }
}
