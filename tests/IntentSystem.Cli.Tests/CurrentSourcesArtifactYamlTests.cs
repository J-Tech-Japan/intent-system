using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class CurrentSourcesArtifactYamlTests
{
    [Fact]
    public void SerializeAndDeserialize_GivenCurrentSourcesArtifact_RoundTripsDeterministically()
    {
        var artifact = new CurrentSourcesArtifact
        {
            DomainSlug = "auth",
            SourceRoot = "src/IntentSystem.Cli",
            SelectedAltitudes = ["means", "execution"],
            SelectedIssueScope = "114,115",
            SelectedPrScope = "113",
            SelectedPaths = ["src/IntentSystem.Cli/Program.cs", "README.md"],
            SourceRefs = ["code:src/IntentSystem.Cli/Program.cs", "issue:114 https://github.com/org/repo/issues/114 Title"],
            SamplingNotes = ["code scope sampled 1 files under 'src/IntentSystem.Cli'."],
            Gaps = ["PR 113 has sparse signal."]
        };

        var yaml = CurrentSourcesArtifactYaml.Serialize(artifact);
        var parsed = CurrentSourcesArtifactYaml.Deserialize(yaml);

        Assert.Equal(artifact.DomainSlug, parsed.DomainSlug);
        Assert.Equal(artifact.SourceRoot, parsed.SourceRoot);
        Assert.Equal(artifact.SelectedAltitudes, parsed.SelectedAltitudes);
        Assert.Equal(artifact.SelectedIssueScope, parsed.SelectedIssueScope);
        Assert.Equal(artifact.SelectedPrScope, parsed.SelectedPrScope);
        Assert.Equal(artifact.SelectedPaths, parsed.SelectedPaths);
        Assert.Equal(artifact.SourceRefs, parsed.SourceRefs);
        Assert.Equal(artifact.SamplingNotes, parsed.SamplingNotes);
        Assert.Equal(artifact.Gaps, parsed.Gaps);
        Assert.Contains("selected_issue_scope: \"114,115\"", yaml, StringComparison.Ordinal);
        Assert.Contains("source_refs:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => CurrentSourcesArtifactYaml.Deserialize(
                """
                domain_slug: auth
                source_root: "src/IntentSystem.Cli"
                selected_altitudes:
                  - "means"
                selected_issue_scope: "none"
                selected_pr_scope: "none"
                selected_paths: []
                source_refs: []
                sampling_notes: []
                """));

        Assert.Contains("gaps", exception.Message, StringComparison.Ordinal);
    }
}
