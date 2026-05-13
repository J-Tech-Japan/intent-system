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
}
