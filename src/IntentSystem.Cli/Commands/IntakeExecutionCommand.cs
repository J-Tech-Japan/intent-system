namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionCommand
{
    private const string IntentCliDirectory = "intents/intent-cli";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake execution command requires a domain.");
            return 1;
        }

        var domain = args[0];

        try
        {
            var request = CreateRequest(context.RepoRoot, domain);
            if (request.ProposedExecutionUnits.Count == 0)
            {
                writer.WriteLine($"No updated parent source files were found for domain '{domain}'.");
                return 1;
            }

            var markdown = IntakeExecutionRenderer.RenderMarkdown(request);
            var artifactPath = IntakeExecutionArtifactWriter.Write(markdown, domain, context.RepoRoot);
            IntakeExecutionRenderer.WriteSummary(writer, request, artifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IntakeExecutionRequest CreateRequest(string repoRoot, string domain)
    {
        var intentCliRoot = Path.Combine(repoRoot, IntentCliDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(intentCliRoot))
        {
            return new IntakeExecutionRequest
            {
                Domain = domain,
                ProposedExecutionUnits = []
            };
        }

        var matchingFiles = Directory.EnumerateFiles(intentCliRoot, "*.md", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relativePath => relativePath.Contains(domain, StringComparison.OrdinalIgnoreCase))
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .ToArray();

        var candidates = matchingFiles
            .Select((relativePath, index) => CreateCandidate(repoRoot, domain, relativePath, index + 1))
            .ToArray();

        var conceptIds = candidates
            .Where(candidate => string.Equals(candidate.TargetPart, "concepts", StringComparison.Ordinal))
            .Select(candidate => candidate.ExecutionUnitId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var resolvedCandidates = candidates
            .Select(candidate => candidate with
            {
                Dependencies = string.Equals(candidate.TargetPart, "concepts", StringComparison.Ordinal)
                    ? Array.Empty<string>()
                    : conceptIds
            })
            .ToArray();

        return new IntakeExecutionRequest
        {
            Domain = domain,
            ProposedExecutionUnits = resolvedCandidates
        };
    }

    private static IntakeExecutionUnitCandidate CreateCandidate(
        string repoRoot,
        string domain,
        string relativePath,
        int ordinal)
    {
        var absolutePath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(absolutePath).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = content.Split('\n', StringSplitOptions.None);
        var heading = lines.FirstOrDefault(line => line.StartsWith("#", StringComparison.Ordinal))?.Trim();
        var bulletCount = lines.Count(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal));
        var targetPart = ResolveTargetPart(relativePath);
        var executionUnitId = $"{domain.ToUpperInvariant()}-{ordinal:00}";

        var readinessNotes = new List<string>
        {
            $"Source file path: {relativePath}",
            $"Current heading: {heading ?? "[none]"}",
            $"Detected bullet lines: {bulletCount}"
        };

        var verificationHints = new List<string>
        {
            $"Review parent source file '{relativePath}' for issue-ready scope.",
            $"Confirm target part '{targetPart}' in the execution draft.",
            "dotnet test IntentSystem.sln"
        };

        return new IntakeExecutionUnitCandidate
        {
            ExecutionUnitId = executionUnitId,
            SourceFilePath = relativePath,
            TargetPart = targetPart,
            Dependencies = [],
            ReadinessNotes = readinessNotes,
            VerificationHints = verificationHints
        };
    }

    private static string ResolveTargetPart(string relativePath)
    {
        const string intentCliPrefix = "intents/intent-cli/";
        var trimmed = relativePath.StartsWith(intentCliPrefix, StringComparison.Ordinal)
            ? relativePath[intentCliPrefix.Length..]
            : relativePath;

        if (trimmed.StartsWith("concepts/", StringComparison.Ordinal))
        {
            return "concepts";
        }

        var lastSlashIndex = trimmed.LastIndexOf('/');
        return lastSlashIndex > 0
            ? trimmed[..lastSlashIndex]
            : trimmed;
    }
}
