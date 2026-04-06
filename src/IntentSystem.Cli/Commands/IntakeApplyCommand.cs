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

            var updatedContent = UpsertManagedBlock(previousContent, request.Domain, fileDraft);
            if (!string.Equals(previousContent, updatedContent, StringComparison.Ordinal))
            {
                File.WriteAllText(absolutePath, updatedContent);
                changedFilePaths.Add(fileDraft.TargetFilePath);
                appliedEditCount += fileDraft.ProposedEdits.Count;
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

    private static string UpsertManagedBlock(string existingContent, string domain, IntakePatchFileDraft fileDraft)
    {
        var normalizedExisting = existingContent.Replace("\r\n", "\n", StringComparison.Ordinal);
        var block = BuildManagedBlock(domain, fileDraft);
        var startMarker = $"<!-- intake-apply:start domain:{domain} path:{fileDraft.TargetFilePath} -->";
        var endMarker = $"<!-- intake-apply:end domain:{domain} path:{fileDraft.TargetFilePath} -->";
        var startIndex = normalizedExisting.IndexOf(startMarker, StringComparison.Ordinal);

        string updated;
        if (startIndex >= 0)
        {
            var endIndex = normalizedExisting.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Existing intake apply block for '{fileDraft.TargetFilePath}' is missing its closing marker.");
            }

            endIndex += endMarker.Length;
            updated = normalizedExisting[..startIndex] + block + normalizedExisting[endIndex..];
        }
        else if (string.IsNullOrWhiteSpace(normalizedExisting))
        {
            updated = block;
        }
        else
        {
            updated = normalizedExisting.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
        }

        return updated.TrimEnd() + Environment.NewLine;
    }

    private static string BuildManagedBlock(string domain, IntakePatchFileDraft fileDraft)
    {
        var lines = new List<string>
        {
            $"<!-- intake-apply:start domain:{domain} path:{fileDraft.TargetFilePath} -->",
            $"## Intake Apply Update ({domain})",
            string.Empty,
            "foldin_anchors:"
        };

        AppendList(lines, fileDraft.FoldinAnchors);
        lines.Add("source_concept_refs:");
        AppendList(lines, fileDraft.SourceConceptRefs);
        lines.Add("proposed_edits:");
        AppendList(lines, fileDraft.ProposedEdits);
        lines.Add("rationale:");
        AppendList(lines, fileDraft.Rationale);
        lines.Add($"<!-- intake-apply:end domain:{domain} path:{fileDraft.TargetFilePath} -->");

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendList(List<string> lines, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            lines.Add("- none");
            return;
        }

        lines.AddRange(values.Select(value => $"- {value}"));
    }
}
