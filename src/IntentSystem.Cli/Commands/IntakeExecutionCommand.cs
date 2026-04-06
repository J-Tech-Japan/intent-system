namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length > 0 && string.Equals(args[0], "apply", StringComparison.Ordinal))
        {
            return IntakeExecutionApplyCommand.Execute(context, args[1..], writer);
        }

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake execution command requires a domain.");
            return 1;
        }

        var domain = args[0];
        var patchPath = Path.Combine(
            context.RepoRoot,
            IntakePatchArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(patchPath))
        {
            writer.WriteLine($"Intake patch artifact was not found at {patchPath}");
            return 1;
        }

        try
        {
            var patchRequest = IntakePatchArtifactMarkdown.Deserialize(File.ReadAllText(patchPath));
            if (!string.Equals(patchRequest.Domain, domain, StringComparison.Ordinal))
            {
                writer.WriteLine(
                    $"Intake patch artifact domain '{patchRequest.Domain}' does not match requested domain '{domain}'.");
                return 1;
            }

            var request = CreateRequest(context.RepoRoot, patchRequest);
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

    private static IntakeExecutionRequest CreateRequest(string repoRoot, IntakePatchRequest patchRequest)
    {
        var updatedSourceFiles = patchRequest.TargetFilePaths
            .Distinct(StringComparer.Ordinal)
            .OrderBy(relativePath => relativePath, StringComparer.Ordinal)
            .Where(relativePath => File.Exists(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        var candidates = updatedSourceFiles
            .Select((relativePath, index) => CreateCandidate(repoRoot, patchRequest.Domain, relativePath, index + 1))
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
            Domain = patchRequest.Domain,
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
