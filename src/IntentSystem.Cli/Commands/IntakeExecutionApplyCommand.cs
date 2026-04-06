namespace IntentSystem.Cli.Commands;

internal static class IntakeExecutionApplyCommand
{
    private const string PostMvpSubSlicesPath = "intents/intent-cli/execution/05-post-mvp-sub-slices.md";
    private const string ReadinessAndVerificationPath = "intents/intent-cli/execution/03-readiness-and-verification.md";
    private const string TableHeader =
        "| subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |";

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

        var postMvpSubSlicesAbsolutePath = Path.Combine(repoRoot, PostMvpSubSlicesPath.Replace('/', Path.DirectorySeparatorChar));
        var readinessAbsolutePath = Path.Combine(repoRoot, ReadinessAndVerificationPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(postMvpSubSlicesAbsolutePath))
        {
            throw new InvalidOperationException($"Execution source file was not found at {postMvpSubSlicesAbsolutePath}");
        }

        if (!File.Exists(readinessAbsolutePath))
        {
            throw new InvalidOperationException($"Execution source file was not found at {readinessAbsolutePath}");
        }

        var existingSubSlicesContent = File.ReadAllText(postMvpSubSlicesAbsolutePath);
        var updatedSubSlicesContent = ApplySubsliceRows(existingSubSlicesContent, request);
        if (!string.Equals(existingSubSlicesContent, updatedSubSlicesContent, StringComparison.Ordinal))
        {
            File.WriteAllText(postMvpSubSlicesAbsolutePath, updatedSubSlicesContent);
            changedFilePaths.Add(PostMvpSubSlicesPath);
        }

        var existingReadinessContent = File.ReadAllText(readinessAbsolutePath);
        var updatedReadinessContent = ApplyReadinessSection(existingReadinessContent, request);
        if (!string.Equals(existingReadinessContent, updatedReadinessContent, StringComparison.Ordinal))
        {
            File.WriteAllText(readinessAbsolutePath, updatedReadinessContent);
            changedFilePaths.Add(ReadinessAndVerificationPath);
        }

        return new IntakeExecutionApplyResult
        {
            Domain = request.Domain,
            ChangedFilePaths = changedFilePaths,
            AppliedUnitCount = changedFilePaths.Count == 0 ? 0 : request.ProposedExecutionUnits.Count,
            PreservedDependencyRefs = request.ProposedExecutionUnits
                .SelectMany(candidate => candidate.Dependencies)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static string ApplySubsliceRows(string existingContent, IntakeExecutionRequest request)
    {
        var normalized = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None).ToList();
        var headerIndex = lines.FindIndex(line => string.Equals(line, TableHeader, StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Count)
        {
            throw new InvalidOperationException("Execution sub-slices file did not contain the expected table header.");
        }

        var dataStart = headerIndex + 2;
        var dataEnd = dataStart;
        while (dataEnd < lines.Count && lines[dataEnd].StartsWith("|", StringComparison.Ordinal))
        {
            dataEnd++;
        }

        var existingRows = lines.GetRange(dataStart, dataEnd - dataStart)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var replacementRows = request.ProposedExecutionUnits
            .OrderBy(candidate => candidate.ExecutionUnitId, StringComparer.Ordinal)
            .Select(CreateSubsliceRow)
            .ToArray();

        foreach (var candidate in request.ProposedExecutionUnits)
        {
            existingRows.RemoveAll(row => GetFirstCell(row).Equals(candidate.ExecutionUnitId, StringComparison.Ordinal));
        }

        existingRows.AddRange(replacementRows);
        existingRows = existingRows
            .OrderBy(row => GetFirstCell(row), StringComparer.Ordinal)
            .ToList();

        lines.RemoveRange(dataStart, dataEnd - dataStart);
        lines.InsertRange(dataStart, existingRows);

        return string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
    }

    private static string ApplyReadinessSection(string existingContent, IntakeExecutionRequest request)
    {
        var normalized = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var sectionTitle = $"## Intake Execution Candidates: {request.Domain}";
        var sectionBody = BuildReadinessSection(request);
        var startIndex = normalized.IndexOf(sectionTitle, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return normalized + Environment.NewLine + Environment.NewLine + sectionBody + Environment.NewLine;
        }

        var nextSectionIndex = normalized.IndexOf(Environment.NewLine + "## ", startIndex + sectionTitle.Length, StringComparison.Ordinal);
        var updated = nextSectionIndex < 0
            ? normalized[..startIndex] + sectionBody
            : normalized[..startIndex] + sectionBody + normalized[nextSectionIndex..];

        return updated.TrimEnd() + Environment.NewLine;
    }

    private static string BuildReadinessSection(IntakeExecutionRequest request)
    {
        var lines = new List<string>
        {
            $"## Intake Execution Candidates: {request.Domain}",
            string.Empty
        };

        foreach (var candidate in request.ProposedExecutionUnits.OrderBy(candidate => candidate.ExecutionUnitId, StringComparer.Ordinal))
        {
            lines.Add($"### `{candidate.ExecutionUnitId}`");
            lines.Add(string.Empty);
            lines.Add($"source_file_path: {candidate.SourceFilePath}");
            lines.Add($"target_part: {candidate.TargetPart}");
            AppendList(lines, "dependencies", candidate.Dependencies);
            AppendList(lines, "readiness_notes", candidate.ReadinessNotes);
            AppendList(lines, "verification_hints", candidate.VerificationHints);
            lines.Add(string.Empty);
        }

        if (request.ProposedExecutionUnits.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateSubsliceRow(IntakeExecutionUnitCandidate candidate)
    {
        var heading = candidate.ReadinessNotes
            .FirstOrDefault(note => note.StartsWith("Current heading: ", StringComparison.Ordinal));
        var normalizedHeading = heading is null
            ? "updated source"
            : heading["Current heading: ".Length..].TrimStart('#', ' ');
        var dependencies = candidate.Dependencies.Count == 0
            ? "-"
            : string.Join(", ", candidate.Dependencies);

        return string.Join(
            " | ",
            [
                "| " + candidate.ExecutionUnitId,
                "G",
                $"reflect updated source '{normalizedHeading}' into issue-ready execution unit",
                dependencies,
                "submodules/intent-system",
                ".",
                candidate.TargetPart,
                "candidate |"
            ]);
    }

    private static string GetFirstCell(string row)
    {
        var trimmed = row.Trim();
        if (!trimmed.StartsWith("|", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var cells = trimmed.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return cells.Length == 0 ? string.Empty : cells[0];
    }

    private static void AppendList(List<string> lines, string label, IReadOnlyList<string> values)
    {
        lines.Add($"{label}:");
        if (values.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        lines.AddRange(values.Select(value => $"- {value}"));
    }
}
