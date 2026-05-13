namespace IntentSystem.Cli.Commands;

/// <summary>
/// G343: pure analyzer that classifies the working-tree content of a
/// <c>.intent-cli/issues/&lt;execution-unit&gt;/publish.yaml</c> file as
/// canonical (safe for host-loop auto-commit) or non-canonical
/// (requires operator review / hard stop).
///
/// Canonical means: the file deserializes successfully via
/// <see cref="IssuePublishArtifactYaml"/>, and the
/// <c>execution_unit</c> field matches the directory segment in the
/// path. Any other condition — unparseable YAML, missing required
/// fields, mismatched execution-unit, unknown extra fields — is
/// classified as non-canonical / unsafe so the host loop refuses to
/// auto-commit operator-edited or corrupt metadata.
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
}

/// <summary>
/// G343: result emitted by <see cref="PublishYamlCanonicalAnalyzer"/>.
/// </summary>
internal sealed record PublishYamlCanonicalResult
{
    public required string Classification { get; init; }
    public required string Summary { get; init; }
}
