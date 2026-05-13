using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G343: unit coverage for the canonical-content classifier used by
/// the durable-state preflight to decide whether a dirty publish.yaml
/// is safe for host-loop auto-commit.
/// </summary>
public sealed class PublishYamlCanonicalAnalyzerTests
{
    [Fact]
    public void Analyze_CanonicalContent_ReturnsCanonical()
    {
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "SKS-G343",
            PublishStatus = "issue-created",
            PacketPath = "intents/packets/SKS-G343/packet.md",
            IssueBodyPath = "intents/packets/SKS-G343/issue-body.md",
            CreatedIssueNumber = 1234,
            CreatedIssueUrl = "https://github.com/o/r/issues/1234",
            PublishedLabelName = null,
        };

        var yaml = IssuePublishArtifactYaml.Serialize(artifact);
        var result = PublishYamlCanonicalAnalyzer.Analyze(yaml, "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationCanonical, result.Classification);
        Assert.Contains("SKS-G343", result.Summary, StringComparison.Ordinal);
        Assert.Contains("canonical", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_ExecutionUnitMismatch_ReturnsNonCanonical()
    {
        // The publish.yaml declares an execution unit that disagrees
        // with the directory it lives in — classic copy-paste / operator
        // edit signature.
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "SKS-G215",          // body says G215
            PublishStatus = "issue-created",
            PacketPath = "intents/packets/SKS-G215/packet.md",
            IssueBodyPath = "intents/packets/SKS-G215/issue-body.md",
            CreatedIssueNumber = 99,
            CreatedIssueUrl = null,
            PublishedLabelName = null,
        };
        var yaml = IssuePublishArtifactYaml.Serialize(artifact);

        var result = PublishYamlCanonicalAnalyzer.Analyze(yaml, "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationNonCanonical, result.Classification);
        Assert.Contains("SKS-G215", result.Summary, StringComparison.Ordinal);
        Assert.Contains("SKS-G343", result.Summary, StringComparison.Ordinal);
        Assert.Contains(PublishYamlCanonicalAnalyzer.RecoveryCommand, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_UnparseableContent_ReturnsInvalid()
    {
        var result = PublishYamlCanonicalAnalyzer.Analyze(
            "this is not yaml: missing required fields",
            "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationInvalid, result.Classification);
        Assert.Contains(PublishYamlCanonicalAnalyzer.RecoveryCommand, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_EmptyContent_ReturnsInvalid()
    {
        var result = PublishYamlCanonicalAnalyzer.Analyze(string.Empty, "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationInvalid, result.Classification);
        Assert.Contains("empty", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_NullContent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PublishYamlCanonicalAnalyzer.Analyze(null!, "SKS-G343"));
    }

    [Fact]
    public void Analyze_BlankExecutionUnit_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PublishYamlCanonicalAnalyzer.Analyze("execution_unit: x", "  "));
    }

    [Fact]
    public void RecoveryCommand_NamesPublishLifecycleRepair()
    {
        // Pin the surface so the host-loop guidance points at an
        // installed automation command (G343 AC5).
        Assert.Contains("automation publish-lifecycle-repair", PublishYamlCanonicalAnalyzer.RecoveryCommand, StringComparison.Ordinal);
        Assert.Contains("--write", PublishYamlCanonicalAnalyzer.RecoveryCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_UnknownTopLevelKey_ReturnsNonCanonical()
    {
        // G343 (PR #790 repair): reviewer flagged that
        // IssuePublishArtifactYaml.Deserialize silently ignores
        // unknown keys, which let operator-authored content slip
        // through the safe-commit gate. The classifier must reject
        // any top-level key outside the canonical schema.
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "SKS-G343",
            PublishStatus = "issue-created",
            PacketPath = "intents/packets/SKS-G343/packet.md",
            IssueBodyPath = "intents/packets/SKS-G343/issue-body.md",
            CreatedIssueNumber = 1234,
            CreatedIssueUrl = "https://github.com/o/r/issues/1234",
            PublishedLabelName = null,
        };
        var canonical = IssuePublishArtifactYaml.Serialize(artifact);
        var yamlWithExtra = canonical + "operator_note: \"manual override\"" + Environment.NewLine;

        var result = PublishYamlCanonicalAnalyzer.Analyze(yamlWithExtra, "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationNonCanonical, result.Classification);
        Assert.Contains("operator_note", result.Summary, StringComparison.Ordinal);
        Assert.Contains("unknown top-level key", result.Summary, StringComparison.Ordinal);
        Assert.Contains(PublishYamlCanonicalAnalyzer.RecoveryCommand, result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_MultipleUnknownKeys_BothReportedNonCanonical()
    {
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "SKS-G343",
            PublishStatus = "issue-created",
            PacketPath = "intents/packets/SKS-G343/packet.md",
            IssueBodyPath = "intents/packets/SKS-G343/issue-body.md",
            CreatedIssueNumber = 1234,
            CreatedIssueUrl = null,
            PublishedLabelName = null,
        };
        var canonical = IssuePublishArtifactYaml.Serialize(artifact);
        var yamlWithExtras = canonical
            + "extra_field_one: \"a\"" + Environment.NewLine
            + "extra_field_two: 7" + Environment.NewLine;

        var result = PublishYamlCanonicalAnalyzer.Analyze(yamlWithExtras, "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationNonCanonical, result.Classification);
        Assert.Contains("extra_field_one", result.Summary, StringComparison.Ordinal);
        Assert.Contains("extra_field_two", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_AllOptionalLifecycleFieldsPresent_StillCanonical()
    {
        // Regression: the unknown-key check must NOT trip on the
        // optional G307 lifecycle fields that the schema documents.
        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = "SKS-G343",
            PublishStatus = "issue-created",
            PacketPath = "intents/packets/SKS-G343/packet.md",
            IssueBodyPath = "intents/packets/SKS-G343/issue-body.md",
            CreatedIssueNumber = 1234,
            CreatedIssueUrl = "https://github.com/o/r/issues/1234",
            PublishedLabelName = "intent-target",
            LifecycleState = "pr-created",
            LinkedPrNumber = 9999,
            LinkedPrUrl = "https://github.com/o/r/pull/9999",
            ClosedOutAt = null,
        };
        var yaml = IssuePublishArtifactYaml.Serialize(artifact);

        var result = PublishYamlCanonicalAnalyzer.Analyze(yaml, "SKS-G343");

        Assert.Equal(PublishYamlCanonicalAnalyzer.ClassificationCanonical, result.Classification);
    }

    [Fact]
    public void CanonicalKeys_MatchesIssuePublishArtifactYamlContract()
    {
        // Pin the canonical key set so future schema changes must
        // explicitly update both sides; without this, adding a
        // new optional field could silently bypass the safe-commit
        // gate again.
        var expected = new[]
        {
            "execution_unit",
            "publish_status",
            "packet_path",
            "issue_body_path",
            "created_issue_number",
            "created_issue_url",
            "published_label_name",
            "lifecycle_state",
            "linked_pr_number",
            "linked_pr_url",
            "closed_out_at",
        };
        Assert.Equal(
            expected.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            PublishYamlCanonicalAnalyzer.CanonicalKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }
}
