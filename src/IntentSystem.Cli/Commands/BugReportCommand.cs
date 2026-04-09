using System.Security.Cryptography;
using System.Text;

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
        var (problemStatement, reportSource) = ResolveProblemStatement(context.RepoRoot, parsed);
        var bugId = string.IsNullOrWhiteSpace(parsed.BugId)
            ? GenerateBugId(parsed.Domain, parsed.Title, problemStatement)
            : parsed.BugId;

        var suspectedFailureLocus = string.IsNullOrWhiteSpace(parsed.SuspectedFailureLocus)
            ? DeriveSuspectedFailureLocus(parsed.Title, problemStatement)
            : parsed.SuspectedFailureLocus;

        var artifact = new BugReportArtifact
        {
            DomainSlug = parsed.Domain,
            BugId = bugId,
            Title = parsed.Title,
            ReportSource = reportSource,
            ProblemStatement = problemStatement,
            SuspectedFailureLocus = suspectedFailureLocus,
            OriginalInstructionRefs = parsed.OriginalInstructionRefs,
            AffectedIntentRefs = parsed.AffectedIntentRefs,
            AffectedRuleSpecRefs = parsed.AffectedRuleSpecRefs,
            ClarificationCandidates = parsed.ClarificationCandidates,
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
        if (args.Length < 3 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException(
                "Bug report command requires '<domain> [<bug-id>] --title <text> [--text <text> | --from-file <path>]'.");
        }

        var domain = args[0].Trim();
        string? bugId = null;
        var flagStartIndex = 1;
        if (args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(args[1]))
            {
                throw new InvalidOperationException(
                    "Bug report command requires '<domain> [<bug-id>] --title <text> [--text <text> | --from-file <path>]'.");
            }

            bugId = args[1].Trim();
            flagStartIndex = 2;
        }

        string? title = null;
        string? problemStatementPath = null;
        string? problemStatementText = null;
        string? suspectedFailureLocus = null;
        var originalInstructionRefs = Array.Empty<string>();
        var affectedIntentRefs = Array.Empty<string>();
        var affectedRuleSpecRefs = Array.Empty<string>();
        var clarificationCandidates = Array.Empty<string>();
        var linkedExecutionUnits = Array.Empty<string>();
        var linkedIssueRefs = Array.Empty<string>();
        var linkedPrRefs = Array.Empty<string>();
        var linkedReviewRefs = Array.Empty<string>();

        for (var index = flagStartIndex; index < args.Length; index += 2)
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
                case "--text":
                    problemStatementText = value;
                    break;
                case "--suspected-failure-locus":
                    suspectedFailureLocus = value.Trim();
                    break;
                case "--instruction-refs":
                    originalInstructionRefs = ParseCsvList(value);
                    break;
                case "--affected-intent-refs":
                    affectedIntentRefs = ParseCsvList(value);
                    break;
                case "--affected-rule-spec-refs":
                    affectedRuleSpecRefs = ParseCsvList(value);
                    break;
                case "--clarification-candidates":
                    clarificationCandidates = ParseCsvList(value);
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
                        "Bug report command supports '--title <text>', '--text <text>', '--from-file <path>', '--suspected-failure-locus <text>', '--instruction-refs <csv>', '--affected-intent-refs <csv>', '--affected-rule-spec-refs <csv>', '--clarification-candidates <csv>', '--execution-units <csv>', '--issues <csv>', '--prs <csv>', and '--reviews <csv>'.");
            }
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Bug report command requires '--title <text>'.");
        }

        var hasProblemStatementPath = !string.IsNullOrWhiteSpace(problemStatementPath);
        var hasProblemStatementText = !string.IsNullOrWhiteSpace(problemStatementText);
        if (hasProblemStatementPath == hasProblemStatementText)
        {
            throw new InvalidOperationException(
                "Bug report command requires exactly one of '--text <text>' or '--from-file <path>'.");
        }

        return new ParsedArgs
        {
            Domain = domain,
            BugId = bugId,
            Title = title,
            ProblemStatementPath = problemStatementPath,
            ProblemStatementText = problemStatementText,
            SuspectedFailureLocus = suspectedFailureLocus,
            OriginalInstructionRefs = originalInstructionRefs,
            AffectedIntentRefs = affectedIntentRefs,
            AffectedRuleSpecRefs = affectedRuleSpecRefs,
            ClarificationCandidates = clarificationCandidates,
            LinkedExecutionUnits = linkedExecutionUnits,
            LinkedIssueRefs = linkedIssueRefs,
            LinkedPrRefs = linkedPrRefs,
            LinkedReviewRefs = linkedReviewRefs
        };
    }

    private static (string ProblemStatement, string ReportSource) ResolveProblemStatement(string repoRoot, ParsedArgs parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.ProblemStatementText))
        {
            var problemStatementText = parsed.ProblemStatementText.TrimEnd();
            if (string.IsNullOrWhiteSpace(problemStatementText))
            {
                throw new InvalidOperationException("Inline bug report text must not be empty.");
            }

            return (problemStatementText, "inline-text");
        }

        var problemStatementPath = ResolveInputPath(
            repoRoot,
            parsed.ProblemStatementPath
            ?? throw new InvalidOperationException("Bug report command requires '--from-file <path>'."));
        if (!File.Exists(problemStatementPath))
        {
            throw new InvalidOperationException($"Prepared problem statement file was not found at {problemStatementPath}");
        }

        var problemStatement = File.ReadAllText(problemStatementPath).TrimEnd();
        if (string.IsNullOrWhiteSpace(problemStatement))
        {
            throw new InvalidOperationException("Prepared problem statement file must not be empty.");
        }

        return (problemStatement, "from-file");
    }

    private static string[] ParseCsvList(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string DeriveSuspectedFailureLocus(string title, string problemStatement)
    {
        foreach (var line in problemStatement.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return title;
    }

    private static string GenerateBugId(string domain, string title, string problemStatement)
    {
        var normalizedInput = string.Join(
            "\n",
            domain.Trim().ToLowerInvariant(),
            title.Trim(),
            problemStatement.Trim());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedInput));
        return $"BUG-{Convert.ToHexString(hash)[..12]}";
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

        public string? BugId { get; init; }

        public required string Title { get; init; }

        public string? ProblemStatementPath { get; init; }

        public string? ProblemStatementText { get; init; }

        public string? SuspectedFailureLocus { get; init; }

        public required string[] OriginalInstructionRefs { get; init; }

        public required string[] AffectedIntentRefs { get; init; }

        public required string[] AffectedRuleSpecRefs { get; init; }

        public required string[] ClarificationCandidates { get; init; }

        public required string[] LinkedExecutionUnits { get; init; }

        public required string[] LinkedIssueRefs { get; init; }

        public required string[] LinkedPrRefs { get; init; }

        public required string[] LinkedReviewRefs { get; init; }
    }
}
