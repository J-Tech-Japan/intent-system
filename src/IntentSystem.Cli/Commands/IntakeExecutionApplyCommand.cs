namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionApplyCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake execution apply command requires a domain.");
            return 1;
        }

        var domain = args[0];
        var executionDraftPath = Path.Combine(
            context.RepoRoot,
            IntakeExecutionArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(executionDraftPath))
        {
            writer.WriteLine($"Intake execution artifact was not found at {executionDraftPath}");
            return 1;
        }

        try
        {
            var request = IntakeExecutionArtifactMarkdown.Deserialize(File.ReadAllText(executionDraftPath));
            if (!string.Equals(request.Domain, domain, StringComparison.Ordinal))
            {
                writer.WriteLine(
                    $"Intake execution artifact domain '{request.Domain}' does not match requested domain '{domain}'.");
                return 1;
            }

            var result = ApplyDraft(context.RepoRoot, request);
            IntakeExecutionApplyRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IntakeExecutionApplyResult ApplyDraft(string repoRoot, IntakeExecutionRequest request)
    {
        var changedFilePaths = new List<string>();
        var appliedUnitCount = 0;

        foreach (var candidateGroup in request.ProposedExecutionUnits
                     .GroupBy(candidate => candidate.SourceFilePath, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var absolutePath = Path.Combine(repoRoot, candidateGroup.Key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                throw new InvalidOperationException($"Execution source file was not found at {absolutePath}");
            }

            var existingContent = File.ReadAllText(absolutePath);
            var updatedContent = existingContent;
            var fileChanged = false;

            foreach (var candidate in candidateGroup.OrderBy(item => item.ExecutionUnitId, StringComparer.Ordinal))
            {
                var candidateResult = ApplyExecutionUnit(updatedContent, candidate);
                updatedContent = candidateResult.UpdatedContent;
                if (candidateResult.Applied)
                {
                    fileChanged = true;
                    appliedUnitCount++;
                }
            }

            if (fileChanged && !string.Equals(existingContent, updatedContent, StringComparison.Ordinal))
            {
                File.WriteAllText(absolutePath, updatedContent);
                changedFilePaths.Add(candidateGroup.Key);
            }
        }

        return new IntakeExecutionApplyResult
        {
            Domain = request.Domain,
            ChangedFilePaths = changedFilePaths,
            AppliedUnitCount = appliedUnitCount,
            PreservedDependencyRefs = request.ProposedExecutionUnits
                .SelectMany(candidate => candidate.Dependencies)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static ApplyCandidateResult ApplyExecutionUnit(string existingContent, IntakeExecutionUnitCandidate candidate)
    {
        var normalizedExisting = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var linesToAppend = BuildCandidateLines(candidate)
            .Where(line => !ContainsAppliedLine(normalizedExisting, line))
            .ToArray();

        if (linesToAppend.Length == 0)
        {
            var unchangedContent = string.IsNullOrWhiteSpace(normalizedExisting)
                ? string.Empty
                : normalizedExisting + Environment.NewLine;
            return new ApplyCandidateResult(unchangedContent, false);
        }

        var appendedBlock = string.Join(Environment.NewLine, linesToAppend.Select(line => $"- {line}"));
        var updatedContent = string.IsNullOrWhiteSpace(normalizedExisting)
            ? appendedBlock + Environment.NewLine
            : normalizedExisting + Environment.NewLine + Environment.NewLine + appendedBlock + Environment.NewLine;

        return new ApplyCandidateResult(updatedContent, true);
    }

    private static IReadOnlyList<string> BuildCandidateLines(IntakeExecutionUnitCandidate candidate)
    {
        var lines = new List<string>
        {
            $"execution_unit: {candidate.ExecutionUnitId}",
            $"target_part: {candidate.TargetPart}"
        };

        lines.AddRange(candidate.Dependencies
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => $"dependencies: {value}"));
        lines.AddRange(candidate.ReadinessNotes.Select(value => $"readiness_notes: {value}"));
        lines.AddRange(candidate.VerificationHints.Select(value => $"verification_hints: {value}"));

        return lines
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsAppliedLine(string content, string line)
    {
        return content.Split('\n', StringSplitOptions.None)
            .Select(existingLine => existingLine.Trim())
            .Any(existingLine =>
                string.Equals(existingLine, line, StringComparison.Ordinal)
                || string.Equals(existingLine, $"- {line}", StringComparison.Ordinal));
    }

    private readonly record struct ApplyCandidateResult(string UpdatedContent, bool Applied);
}
