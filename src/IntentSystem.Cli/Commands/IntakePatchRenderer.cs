namespace IntentSystem.Cli.Commands;

internal static class IntakePatchRenderer
{
    public static string RenderMarkdown(IntakePatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var lines = new List<string>
        {
            "# Intake Patch Draft",
            string.Empty,
            "## Domain",
            string.Empty,
            $"`{request.Domain}`",
            string.Empty
        };

        AppendList(lines, "target_file_paths", request.TargetFilePaths);
        lines.Add(string.Empty);
        AppendList(lines, "source_concept_refs", request.SourceConceptRefs);
        lines.Add(string.Empty);
        lines.Add("## File-By-File Patch Candidates");
        lines.Add(string.Empty);

        foreach (var fileDraft in request.FileDrafts)
        {
            lines.Add($"### `{fileDraft.TargetFilePath}`");
            lines.Add(string.Empty);
            lines.Add($"current_file_state: {fileDraft.CurrentFileState}");
            AppendList(lines, "foldin_anchors", fileDraft.FoldinAnchors);
            AppendList(lines, "source_concept_refs", fileDraft.SourceConceptRefs);
            AppendList(lines, "proposed_edits", fileDraft.ProposedEdits);
            AppendList(lines, "rationale", fileDraft.Rationale);
            lines.Add("current_file_excerpt:");
            lines.Add("```text");
            lines.Add(fileDraft.CurrentFileExcerpt);
            lines.Add("```");
            lines.Add(string.Empty);
        }

        if (request.FileDrafts.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static void WriteSummary(TextWriter writer, IntakePatchRequest request, string artifactPath)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        writer.WriteLine($"Intake patch draft generated for domain '{request.Domain}'.");
        writer.WriteLine($"Artifact path: {artifactPath}");
        writer.WriteLine($"Target file paths: {request.TargetFilePaths.Count}");
        writer.WriteLine($"File draft sections: {request.FileDrafts.Count}");
        writer.WriteLine($"Source concept refs: {request.SourceConceptRefs.Count}");
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
