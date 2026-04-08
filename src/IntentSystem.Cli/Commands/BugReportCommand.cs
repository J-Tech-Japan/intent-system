namespace IntentSystem.Cli.Commands;

internal static class BugReportCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugReportRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugReportCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        var parsed = ParseArgs(args);
        var problemStatementPath = ResolveInputPath(context.RepoRoot, parsed.ProblemStatementPath);
        if (!File.Exists(problemStatementPath))
        {
            throw new InvalidOperationException($"Prepared problem statement file was not found at {problemStatementPath}");
        }

        var problemStatement = File.ReadAllText(problemStatementPath).TrimEnd();
        if (string.IsNullOrWhiteSpace(problemStatement))
        {
            throw new InvalidOperationException("Prepared problem statement file must not be empty.");
        }

        var artifact = new BugReportArtifact
        {
            DomainSlug = parsed.Domain,
            BugId = parsed.BugId,
            Title = parsed.Title,
            ReportSource = "from-file",
            ProblemStatement = problemStatement,
            OriginalInstructionRefs = parsed.OriginalInstructionRefs,
            LinkedExecutionUnits = parsed.LinkedExecutionUnits,
            LinkedIssueRefs = parsed.LinkedIssueRefs,
            LinkedPrRefs = parsed.LinkedPrRefs,
            LinkedReviewRefs = parsed.LinkedReviewRefs
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugReportCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath
        };
    }

    private static ParsedArgs ParseArgs(string[] args)
    {
        if (args.Length < 6 || string.IsNullOrWhiteSpace(args[0]) || string.IsNullOrWhiteSpace(args[1]))
        {
            throw new InvalidOperationException(
                "Bug report command requires '<domain> <bug-id> --title <text> --from-file <path>'.");
        }

        var domain = args[0].Trim();
        var bugId = args[1].Trim();
        string? title = null;
        string? problemStatementPath = null;
        var originalInstructionRefs = Array.Empty<string>();
        var linkedExecutionUnits = Array.Empty<string>();
        var linkedIssueRefs = Array.Empty<string>();
        var linkedPrRefs = Array.Empty<string>();
        var linkedReviewRefs = Array.Empty<string>();

        for (var index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new InvalidOperationException($"Flag '{args[index]}' requires a value.");
            }

            var flag = args[index];
            var value = args[index + 1];

            switch (flag)
            {
                case "--title":
                    title = value.Trim();
                    break;
                case "--from-file":
                    problemStatementPath = value.Trim();
                    break;
                case "--instruction-refs":
                    originalInstructionRefs = ParseCsvList(value);
                    break;
                case "--execution-units":
                    linkedExecutionUnits = ParseCsvList(value);
                    break;
                case "--issues":
                    linkedIssueRefs = ParseCsvList(value);
                    break;
                case "--prs":
                    linkedPrRefs = ParseCsvList(value);
                    break;
                case "--reviews":
                    linkedReviewRefs = ParseCsvList(value);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Bug report command supports '--title <text>', '--from-file <path>', '--instruction-refs <csv>', '--execution-units <csv>', '--issues <csv>', '--prs <csv>', and '--reviews <csv>'.");
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Bug report command requires '--title <text>'.");
        }

        if (string.IsNullOrWhiteSpace(problemStatementPath))
        {
            throw new InvalidOperationException("Bug report command requires '--from-file <path>'.");
        }

        return new ParsedArgs
        {
            Domain = domain,
            BugId = bugId,
            Title = title,
            ProblemStatementPath = problemStatementPath,
            OriginalInstructionRefs = originalInstructionRefs,
            LinkedExecutionUnits = linkedExecutionUnits,
            LinkedIssueRefs = linkedIssueRefs,
            LinkedPrRefs = linkedPrRefs,
            LinkedReviewRefs = linkedReviewRefs
        };
    }

    private static string[] ParseCsvList(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveInputPath(string repoRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repoRoot, path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string WriteArtifact(string repoRoot, BugReportArtifact artifact)
    {
        var relativePath = BugReportArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug report artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugReportArtifactYaml.Serialize(artifact));

        return relativePath;
    }

    private sealed record ParsedArgs
    {
        public required string Domain { get; init; }

        public required string BugId { get; init; }

        public required string Title { get; init; }

        public required string ProblemStatementPath { get; init; }

        public required string[] OriginalInstructionRefs { get; init; }

        public required string[] LinkedExecutionUnits { get; init; }

        public required string[] LinkedIssueRefs { get; init; }

        public required string[] LinkedPrRefs { get; init; }

        public required string[] LinkedReviewRefs { get; init; }
    }
}
