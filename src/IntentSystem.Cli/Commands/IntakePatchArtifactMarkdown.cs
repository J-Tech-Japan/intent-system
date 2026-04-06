namespace IntentSystem.Cli.Commands;

internal static class IntakePatchArtifactMarkdown
{
    private static readonly string[] RequiredTopLevelSections =
    [
        "target_file_paths",
        "source_concept_refs"
    ];

    private static readonly string[] RequiredFileSections =
    [
        "foldin_anchors",
        "source_concept_refs",
        "proposed_edits",
        "rationale"
    ];

    public static IntakePatchRequest Deserialize(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var lines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);

        var sawTitle = false;
        var sawFileByFileSection = false;
        string? domain = null;
        string? currentTopLevelSection = null;
        var expectingDomain = false;
        var topLevelSections = RequiredTopLevelSections.ToDictionary(section => section, _ => new List<string>(), StringComparer.Ordinal);
        var seenTopLevelSections = new HashSet<string>(StringComparer.Ordinal);
        var fileDrafts = new List<IntakePatchFileDraft>();
        PatchFileDraftBuilder? currentFileDraft = null;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (string.Equals(line, "# Intake Patch Draft", StringComparison.Ordinal))
            {
                sawTitle = true;
                continue;
            }

            if (string.Equals(line, "## Domain", StringComparison.Ordinal))
            {
                expectingDomain = true;
                currentTopLevelSection = null;
                continue;
            }

            if (expectingDomain)
            {
                if (line.StartsWith('`') && line.EndsWith('`') && line.Length >= 2)
                {
                    domain = line[1..^1];
                    expectingDomain = false;
                    continue;
                }

                throw new InvalidOperationException("Intake patch artifact must contain a backticked domain line.");
            }

            if (string.Equals(line, "## File-By-File Patch Candidates", StringComparison.Ordinal))
            {
                sawFileByFileSection = true;
                currentTopLevelSection = null;
                continue;
            }

            if (!sawFileByFileSection && line.EndsWith(":", StringComparison.Ordinal))
            {
                var sectionName = line[..^1];
                if (topLevelSections.ContainsKey(sectionName))
                {
                    currentTopLevelSection = sectionName;
                    seenTopLevelSections.Add(sectionName);
                    continue;
                }
            }

            if (!sawFileByFileSection)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal) && currentTopLevelSection is not null)
                {
                    var value = line[2..];
                    if (!string.Equals(value, "none", StringComparison.Ordinal))
                    {
                        topLevelSections[currentTopLevelSection].Add(value);
                    }
                }

                continue;
            }

            if (line.StartsWith("### `", StringComparison.Ordinal) && line.EndsWith('`'))
            {
                if (currentFileDraft is not null)
                {
                    fileDrafts.Add(currentFileDraft.Build());
                }

                currentFileDraft = new PatchFileDraftBuilder(line["### `".Length..^1]);
                continue;
            }

            if (currentFileDraft is null)
            {
                continue;
            }

            if (line.StartsWith("current_file_state: ", StringComparison.Ordinal))
            {
                currentFileDraft.CurrentFileState = line["current_file_state: ".Length..];
                continue;
            }

            if (line == "current_file_excerpt:")
            {
                index = ParseExcerpt(lines, index, currentFileDraft);
                continue;
            }

            if (line.EndsWith(":", StringComparison.Ordinal))
            {
                currentFileDraft.CurrentListSection = line[..^1];
                if (RequiredFileSections.Contains(currentFileDraft.CurrentListSection, StringComparer.Ordinal))
                {
                    currentFileDraft.SeenSections.Add(currentFileDraft.CurrentListSection);
                    continue;
                }
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) && currentFileDraft.CurrentListSection is not null)
            {
                var value = line[2..];
                if (!string.Equals(value, "none", StringComparison.Ordinal))
                {
                    currentFileDraft.AddValue(value);
                }
            }
        }

        if (currentFileDraft is not null)
        {
            fileDrafts.Add(currentFileDraft.Build());
        }

        if (!sawTitle)
        {
            throw new InvalidOperationException("Intake patch artifact must start with '# Intake Patch Draft'.");
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new InvalidOperationException("Intake patch artifact must contain a domain.");
        }

        var missingTopLevelSection = RequiredTopLevelSections.FirstOrDefault(section => !seenTopLevelSections.Contains(section));
        if (missingTopLevelSection is not null)
        {
            throw new InvalidOperationException($"Intake patch artifact must contain section '{missingTopLevelSection}:'.");
        }

        if (!sawFileByFileSection)
        {
            throw new InvalidOperationException("Intake patch artifact must contain '## File-By-File Patch Candidates'.");
        }

        return new IntakePatchRequest
        {
            Domain = domain,
            TargetFilePaths = topLevelSections["target_file_paths"].ToArray(),
            SourceConceptRefs = topLevelSections["source_concept_refs"].ToArray(),
            FileDrafts = fileDrafts
        };
    }

    private static int ParseExcerpt(string[] lines, int currentIndex, PatchFileDraftBuilder builder)
    {
        if (currentIndex + 1 >= lines.Length || !string.Equals(lines[currentIndex + 1], "```text", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Intake patch artifact current_file_excerpt must start with a ```text fence.");
        }

        var excerptLines = new List<string>();
        var index = currentIndex + 2;
        for (; index < lines.Length; index++)
        {
            if (string.Equals(lines[index], "```", StringComparison.Ordinal))
            {
                builder.CurrentFileExcerpt = string.Join(Environment.NewLine, excerptLines).TrimEnd();
                return index;
            }

            excerptLines.Add(lines[index]);
        }

        throw new InvalidOperationException("Intake patch artifact current_file_excerpt fence was not closed.");
    }

    private sealed class PatchFileDraftBuilder
    {
        private readonly List<string> foldinAnchors = [];
        private readonly List<string> sourceConceptRefs = [];
        private readonly List<string> proposedEdits = [];
        private readonly List<string> rationale = [];

        public PatchFileDraftBuilder(string targetFilePath)
        {
            TargetFilePath = targetFilePath;
        }

        public string TargetFilePath { get; }

        public string? CurrentFileState { get; set; }

        public string CurrentFileExcerpt { get; set; } = string.Empty;

        public string? CurrentListSection { get; set; }

        public HashSet<string> SeenSections { get; } = new(StringComparer.Ordinal);

        public void AddValue(string value)
        {
            switch (CurrentListSection)
            {
                case "foldin_anchors":
                    foldinAnchors.Add(value);
                    break;
                case "source_concept_refs":
                    sourceConceptRefs.Add(value);
                    break;
                case "proposed_edits":
                    proposedEdits.Add(value);
                    break;
                case "rationale":
                    rationale.Add(value);
                    break;
            }
        }

        public IntakePatchFileDraft Build()
        {
            if (string.IsNullOrWhiteSpace(CurrentFileState))
            {
                throw new InvalidOperationException($"Intake patch artifact file block '{TargetFilePath}' must contain current_file_state.");
            }

            var missingSection = RequiredFileSections.FirstOrDefault(section => !SeenSections.Contains(section));
            if (missingSection is not null)
            {
                throw new InvalidOperationException(
                    $"Intake patch artifact file block '{TargetFilePath}' must contain section '{missingSection}:'.");
            }

            return new IntakePatchFileDraft
            {
                TargetFilePath = TargetFilePath,
                CurrentFileState = CurrentFileState,
                ProposedEdits = proposedEdits.ToArray(),
                Rationale = rationale.ToArray(),
                SourceConceptRefs = sourceConceptRefs.ToArray(),
                FoldinAnchors = foldinAnchors.ToArray(),
                CurrentFileExcerpt = string.IsNullOrEmpty(CurrentFileExcerpt) ? "[empty]" : CurrentFileExcerpt
            };
        }
    }
}
