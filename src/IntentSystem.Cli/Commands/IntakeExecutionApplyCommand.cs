namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionApplyCommand
{
    private const string CommandMarker = "`intake execution apply <domain>`";

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

        try
        {
            var result = ExecuteCore(context.RepoRoot, domain);
            IntakeExecutionApplyRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static IntakeExecutionApplyResult ExecuteCore(string repoRoot, string domain)
    {
        var executionDraftPath = Path.Combine(
            repoRoot,
            IntakeExecutionArtifactPathResolver.Resolve(domain).Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(executionDraftPath))
        {
            throw new InvalidOperationException($"Intake execution artifact was not found at {executionDraftPath}");
        }

        var request = IntakeExecutionArtifactMarkdown.Deserialize(File.ReadAllText(executionDraftPath));
        if (!string.Equals(request.Domain, domain, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Intake execution artifact domain '{request.Domain}' does not match requested domain '{domain}'.");
        }

        return ApplyDraft(repoRoot, request);
    }

    private static IntakeExecutionApplyResult ApplyDraft(string repoRoot, IntakeExecutionRequest request)
    {
        var targetFilePaths = ResolveExecutionTargetPaths(repoRoot);
        if (targetFilePaths.Count == 0)
        {
            throw new InvalidOperationException(
                "Execution apply target could not be derived from the current execution baseline.");
        }

        var changedFilePaths = new List<string>();
        var appliedUnitCount = 0;

        foreach (var targetFilePath in targetFilePaths)
        {
            var absolutePath = Path.Combine(repoRoot, targetFilePath.Replace('/', Path.DirectorySeparatorChar));
            var existingContent = File.ReadAllText(absolutePath);
            var applyResult = ApplyToExecutionBaseline(existingContent, request);

            if (applyResult.AppliedUnitCount == 0)
            {
                continue;
            }

            if (!string.Equals(existingContent, applyResult.UpdatedContent, StringComparison.Ordinal))
            {
                File.WriteAllText(absolutePath, applyResult.UpdatedContent);
                changedFilePaths.Add(targetFilePath);
                appliedUnitCount += applyResult.AppliedUnitCount;
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

    private static IReadOnlyList<string> ResolveExecutionTargetPaths(string repoRoot)
    {
        var intentsRoot = Path.Combine(repoRoot, "intents");
        if (!Directory.Exists(intentsRoot))
        {
            return [];
        }

        return Directory.GetFiles(intentsRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}execution{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relativePath => File.ReadAllText(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                .Contains(CommandMarker, StringComparison.Ordinal))
            .ToArray();
    }

    private static ApplyBaselineResult ApplyToExecutionBaseline(string existingContent, IntakeExecutionRequest request)
    {
        var normalized = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None).ToList();

        var sectionStart = FindTargetSectionStart(lines);
        if (sectionStart < 0)
        {
            throw new InvalidOperationException(
                "Execution apply target could not be derived from the current execution baseline.");
        }

        var sectionEnd = FindSectionEnd(lines, sectionStart);
        var sectionLines = lines.Skip(sectionStart).Take(sectionEnd - sectionStart).ToList();
        var existingSectionContent = string.Join("\n", sectionLines);

        var appliedUnitCount = 0;
        foreach (var candidate in request.ProposedExecutionUnits.OrderBy(item => item.ExecutionUnitId, StringComparer.Ordinal))
        {
            var candidateLines = BuildCandidateLines(candidate);
            if (candidateLines.All(line => ContainsAppliedLine(existingSectionContent, line)))
            {
                continue;
            }

            if (sectionLines.Count > 0 && !string.IsNullOrWhiteSpace(sectionLines[^1]))
            {
                sectionLines.Add(string.Empty);
            }

            sectionLines.AddRange(candidateLines.Select(line => $"- {line}"));
            existingSectionContent = string.Join("\n", sectionLines);
            appliedUnitCount++;
        }

        if (appliedUnitCount == 0)
        {
            return new ApplyBaselineResult(normalized.TrimEnd() + Environment.NewLine, 0);
        }

        lines.RemoveRange(sectionStart, sectionEnd - sectionStart);
        lines.InsertRange(sectionStart, sectionLines);
        return new ApplyBaselineResult(string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine, appliedUnitCount);
    }

    private static int FindTargetSectionStart(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith("## ", StringComparison.Ordinal))
            {
                continue;
            }

            var sectionEnd = FindSectionEnd(lines, index);
            var sectionContent = string.Join("\n", lines.Skip(index).Take(sectionEnd - index));
            if (sectionContent.Contains(CommandMarker, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindSectionEnd(IReadOnlyList<string> lines, int startIndex)
    {
        for (var index = startIndex + 1; index < lines.Count; index++)
        {
            if (lines[index].StartsWith("## ", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static IReadOnlyList<string> BuildCandidateLines(IntakeExecutionUnitCandidate candidate)
    {
        var lines = new List<string>
        {
            $"execution_unit: {candidate.ExecutionUnitId}",
            $"source_file_path: {candidate.SourceFilePath}",
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

    private readonly record struct ApplyBaselineResult(string UpdatedContent, int AppliedUnitCount);
}
