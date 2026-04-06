namespace IntentSystem.Cli.Commands;

internal static class IntakeApplyCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Intake apply command requires a domain.");
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
            var request = IntakePatchArtifactMarkdown.Deserialize(File.ReadAllText(patchPath));
            if (!string.Equals(request.Domain, domain, StringComparison.Ordinal))
            {
                writer.WriteLine(
                    $"Intake patch artifact domain '{request.Domain}' does not match requested domain '{domain}'.");
                return 1;
            }

            var result = ApplyDraft(context.RepoRoot, request);
            IntakeApplyRenderer.WriteSummary(writer, result);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static IntakeApplyResult ApplyDraft(string repoRoot, IntakePatchRequest request)
    {
        var changedFilePaths = new List<string>();
        var appliedEditCount = 0;
        var allowedPaths = request.TargetFilePaths.ToHashSet(StringComparer.Ordinal);

        foreach (var fileDraft in request.FileDrafts.OrderBy(draft => draft.TargetFilePath, StringComparer.Ordinal))
        {
            if (!allowedPaths.Contains(fileDraft.TargetFilePath))
            {
                throw new InvalidOperationException(
                    $"Intake patch artifact file block '{fileDraft.TargetFilePath}' is not listed in target_file_paths.");
            }

            var absolutePath = Path.Combine(repoRoot, fileDraft.TargetFilePath.Replace('/', Path.DirectorySeparatorChar));
            var directoryPath = Path.GetDirectoryName(absolutePath)
                ?? throw new InvalidOperationException($"Target file path '{fileDraft.TargetFilePath}' did not contain a directory.");

            Directory.CreateDirectory(directoryPath);

            var previousContent = File.Exists(absolutePath)
                ? File.ReadAllText(absolutePath)
                : string.Empty;

            EnsureExcerptMatchesCurrentContent(previousContent, fileDraft);

            var updatedContent = ApplyProposedEdits(previousContent, fileDraft);
            if (!string.Equals(previousContent, updatedContent, StringComparison.Ordinal))
            {
                File.WriteAllText(absolutePath, updatedContent);
                changedFilePaths.Add(fileDraft.TargetFilePath);
                appliedEditCount += CountAppliedEdits(previousContent, fileDraft);
            }
        }

        return new IntakeApplyResult
        {
            Domain = request.Domain,
            ChangedFilePaths = changedFilePaths,
            AppliedEditCount = appliedEditCount,
            SourceConceptRefs = request.SourceConceptRefs
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static void EnsureExcerptMatchesCurrentContent(string existingContent, IntakePatchFileDraft fileDraft)
    {
        var normalizedExisting = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var excerpt = fileDraft.CurrentFileExcerpt.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(excerpt)
            || string.Equals(excerpt, "[missing]", StringComparison.Ordinal)
            || string.Equals(excerpt, "[empty]", StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(normalizedExisting)
            && !normalizedExisting.Contains(excerpt, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Current file content for '{fileDraft.TargetFilePath}' no longer matches the patch draft excerpt.");
        }
    }

    private static string ApplyProposedEdits(string existingContent, IntakePatchFileDraft fileDraft)
    {
        var normalizedExisting = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var linesToAppend = fileDraft.ProposedEdits
            .Select(NormalizeAppliedLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .Where(line => !ContainsAppliedLine(normalizedExisting, line))
            .ToArray();

        if (linesToAppend.Length == 0)
        {
            return string.IsNullOrWhiteSpace(normalizedExisting)
                ? string.Empty
                : normalizedExisting + Environment.NewLine;
        }

        var appendedBlock = string.Join(Environment.NewLine, linesToAppend.Select(line => $"- {line}"));
        if (string.IsNullOrWhiteSpace(normalizedExisting))
        {
            return appendedBlock + Environment.NewLine;
        }

        return normalizedExisting + Environment.NewLine + Environment.NewLine + appendedBlock + Environment.NewLine;
    }

    private static int CountAppliedEdits(string existingContent, IntakePatchFileDraft fileDraft)
    {
        var normalizedExisting = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        return fileDraft.ProposedEdits
            .Select(NormalizeAppliedLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .Count(line => !ContainsAppliedLine(normalizedExisting, line));
    }

    private static string NormalizeAppliedLine(string proposedEdit)
    {
        const string updatePrefix = "Apply update candidate: ";
        if (proposedEdit.StartsWith(updatePrefix, StringComparison.Ordinal))
        {
            return proposedEdit[updatePrefix.Length..].Trim();
        }

        return proposedEdit.Trim();
    }

    private static bool ContainsAppliedLine(string content, string line)
    {
        return content.Split('\n', StringSplitOptions.None)
            .Select(existingLine => existingLine.Trim())
            .Any(existingLine =>
                string.Equals(existingLine, line, StringComparison.Ordinal)
                || string.Equals(existingLine, $"- {line}", StringComparison.Ordinal));
    }
}
