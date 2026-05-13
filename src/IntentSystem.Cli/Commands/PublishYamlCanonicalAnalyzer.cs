namespace IntentSystem.Cli.Commands;

/// <summary>
/// G343: pure analyzer that classifies the working-tree content of a
/// <c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c> file as
/// canonical (safe for host-loop auto-commit) or non-canonical
/// (requires operator review / hard stop).
///
/// Canonical means: the file deserializes successfully via
/// <see cref="IssuePublishArtifactYaml"/>, the <c>execution_unit</c>
/// field matches the directory segment in the path, AND every
/// top-level key in the file is one of the documented canonical
/// keys. Any other condition — unparseable YAML, missing required
/// fields, mismatched execution-unit, or unknown / operator-authored
/// extra fields — is classified as non-canonical / unsafe so the host
/// loop refuses to auto-commit operator-edited or corrupt metadata.
///
/// Pure data in / pure data out: caller passes the file content and
/// the directory-derived execution-unit; analyzer returns a verdict
/// without reading files or running git.
/// </summary>
internal static class PublishYamlCanonicalAnalyzer
{
    public const string ClassificationCanonical = "canonical";
    public const string ClassificationNonCanonical = "non-canonical";
    public const string ClassificationInvalid = "invalid";

    /// <summary>
    /// Recovery command the host loop should surface when publish.yaml
    /// content is classified non-canonical or invalid. Embedded in the
    /// unsafe-stop summary so operators see the exact recovery hint
    /// instead of generic <c>unsafe-durable-state</c> noise (G343 AC5).
    /// </summary>
    public const string RecoveryCommand =
        "intent-cli automation publish-lifecycle-repair --repo <owner/repo> --write";

    /// <summary>
    /// G343 (PR #790 repair): top-level keys allowed in a canonical
    /// publish.yaml. Mirrors the required field list in
    /// <see cref="IssuePublishArtifactYaml"/> plus the optional G307
    /// lifecycle fields. Any unknown top-level key downgrades the
    /// classification to <see cref="ClassificationNonCanonical"/>
    /// because <see cref="IssuePublishArtifactYaml.Deserialize"/>
    /// silently ignores unknown keys, which would otherwise let
    /// operator-authored fields slip through as canonical.
    /// </summary>
    public static readonly IReadOnlySet<string> CanonicalKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "execution_unit",
        "publish_status",
        "packet_path",
        "issue_body_path",
        "created_issue_number",
        "created_issue_url",
        "published_label_name",
        // G307 optional lifecycle fields
        "lifecycle_state",
        "linked_pr_number",
        "linked_pr_url",
        "closed_out_at",
    };

    public static PublishYamlCanonicalResult Analyze(
        string yamlContent,
        string expectedExecutionUnit)
    {
        ArgumentNullException.ThrowIfNull(yamlContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedExecutionUnit);

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            return new PublishYamlCanonicalResult
            {
                Classification = ClassificationInvalid,
                Summary = $"publish.yaml for `{expectedExecutionUnit}` is empty; "
                    + $"cannot validate canonical shape. Run `{RecoveryCommand}` "
                    + "to regenerate from deterministic queue/runs/GitHub facts.",
            };
        }

        IssuePublishArtifact artifact;
        try
        {
            artifact = IssuePublishArtifactYaml.Deserialize(yamlContent);
        }
        catch (InvalidOperationException exception)
        {
            return new PublishYamlCanonicalResult
            {
                Classification = ClassificationInvalid,
                Summary = $"publish.yaml for `{expectedExecutionUnit}` does not "
                    + $"parse as a canonical IssuePublishArtifact: {exception.Message} "
                    + $"Run `{RecoveryCommand}` to regenerate.",
            };
        }

        // G343 (PR #790 repair): IssuePublishArtifactYaml.Deserialize
        // silently ignores unknown keys, so a file that parses
        // successfully can still contain operator-authored extra
        // fields (commentary, ad-hoc state, etc.). Refuse to classify
        // as canonical when any top-level key is outside the
        // documented set — reviewer flagged this as the path by
        // which operator content could slip through the safe-commit
        // gate.
        var unknownKeys = EnumerateTopLevelKeys(yamlContent)
            .Where(k => !CanonicalKeys.Contains(k))
            .ToArray();
        if (unknownKeys.Length > 0)
        {
            var keyList = string.Join(", ", unknownKeys.Select(k => "`" + k + "`"));
            return new PublishYamlCanonicalResult
            {
                Classification = ClassificationNonCanonical,
                Summary = $"publish.yaml for `{expectedExecutionUnit}` carries "
                    + $"unknown top-level key(s) {keyList} that are not part of the "
                    + "canonical IssuePublishArtifact schema; operator-authored or "
                    + $"future-version content. Run `{RecoveryCommand}` to regenerate "
                    + "canonical content.",
            };
        }

        // The directory name on disk is the authoritative execution-unit
        // identity. A drift between the directory and the YAML field is
        // strong evidence of an operator-authored edit or a mis-copied
        // file; refuse auto-commit.
        if (!string.Equals(artifact.ExecutionUnit, expectedExecutionUnit, StringComparison.Ordinal))
        {
            return new PublishYamlCanonicalResult
            {
                Classification = ClassificationNonCanonical,
                Summary = $"publish.yaml under `.intent-cli/issues/{expectedExecutionUnit}/` "
                    + $"declares `execution_unit: {artifact.ExecutionUnit}` which does not "
                    + $"match the directory; operator-edited or copy-pasted metadata. "
                    + $"Run `{RecoveryCommand}` to regenerate canonical content.",
            };
        }

        return new PublishYamlCanonicalResult
        {
            Classification = ClassificationCanonical,
            Summary = $"publish.yaml for `{artifact.ExecutionUnit}` "
                + $"(publish_status={artifact.PublishStatus}"
                + (artifact.CreatedIssueNumber is { } n ? $", created_issue=#{n}" : string.Empty)
                + $") parses as a canonical IssuePublishArtifact.",
        };
    }

    /// <summary>
    /// G343 (PR #790 repair): enumerate the top-level keys present in
    /// a publish.yaml string. Format is line-based <c>key: value</c> —
    /// matches the parse loop in <see cref="IssuePublishArtifactYaml"/>.
    /// </summary>
    private static IEnumerable<string> EnumerateTopLevelKeys(string yamlContent)
    {
        using var reader = new StringReader(yamlContent);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }
            // Skip nested/indented lines defensively — canonical
            // publish.yaml is single-level scalar key/value.
            if (char.IsWhiteSpace(line[0]))
            {
                continue;
            }
            yield return line[..separatorIndex].Trim();
        }
    }
}

/// <summary>
/// G343: result emitted by <see cref="PublishYamlCanonicalAnalyzer"/>.
/// </summary>
internal sealed record PublishYamlCanonicalResult
{
    public required string Classification { get; init; }
    public required string Summary { get; init; }
}
