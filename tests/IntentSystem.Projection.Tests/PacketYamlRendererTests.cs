using IntentSystem.Projection.Models;
using IntentSystem.Projection.Rendering;

namespace IntentSystem.Projection.Tests;

public sealed class PacketYamlRendererTests
{
    [Fact]
    public void Render_GivenPacket_IncludesAllTopLevelKeys()
    {
        var yaml = PacketYamlRenderer.Render(CreatePacket());

        Assert.Contains("issue_title:", yaml, StringComparison.Ordinal);
        Assert.Contains("issue_kind:", yaml, StringComparison.Ordinal);
        Assert.Contains("source_execution_unit:", yaml, StringComparison.Ordinal);
        Assert.Contains("goal:", yaml, StringComparison.Ordinal);
        Assert.Contains("in_scope:", yaml, StringComparison.Ordinal);
        Assert.Contains("out_of_scope:", yaml, StringComparison.Ordinal);
        Assert.Contains("target_repo:", yaml, StringComparison.Ordinal);
        Assert.Contains("target_path:", yaml, StringComparison.Ordinal);
        Assert.Contains("target_part:", yaml, StringComparison.Ordinal);
        Assert.Contains("dependencies:", yaml, StringComparison.Ordinal);
        Assert.Contains("technical_baseline:", yaml, StringComparison.Ordinal);
        Assert.Contains("acceptance_criteria:", yaml, StringComparison.Ordinal);
        Assert.Contains("verification_evidence:", yaml, StringComparison.Ordinal);
        Assert.Contains("review_mode:", yaml, StringComparison.Ordinal);
        Assert.Contains("completion_action:", yaml, StringComparison.Ordinal);
        Assert.Contains("landing_policy:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_FormatsIssueKindAsKebabCase()
    {
        var yaml = PacketYamlRenderer.Render(CreatePacket());

        Assert.Contains("issue_kind: feature", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenBoundaryFixKind_FormatsAsKebabCase()
    {
        var packet = CreatePacket() with { IssueKind = IssueKind.BoundaryFix };
        var yaml = PacketYamlRenderer.Render(packet);

        Assert.Contains("issue_kind: boundary-fix", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenEmptyList_RendersEmptyArraySyntax()
    {
        var packet = CreatePacket() with { RulesAndSpecs = [] };
        var yaml = PacketYamlRenderer.Render(packet);

        Assert.Contains("rules_and_specs: []", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenListItems_RendersAsBulletedItems()
    {
        var yaml = PacketYamlRenderer.Render(CreatePacket());

        Assert.Contains("  - A1", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenValueWithSpecialChars_QuotesTheValue()
    {
        var packet = CreatePacket() with { Goal = "Fix the projection: schema contract" };
        var yaml = PacketYamlRenderer.Render(packet);

        Assert.Contains("goal: \"Fix the projection: schema contract\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenSamePacket_ProducesIdenticalOutput()
    {
        var packet = CreatePacket();

        var first = PacketYamlRenderer.Render(packet);
        var second = PacketYamlRenderer.Render(packet);

        Assert.Equal(first, second);
    }

    private static ImplementationIssuePacket CreatePacket()
    {
        return new ImplementationIssuePacket
        {
            IssueTitle = "[A2] Packet Generator",
            IssueKind = IssueKind.Feature,
            SourceExecutionUnit = "A2",
            Goal = "Create packet generator for Markdown and YAML artifacts.",
            InScope = ["projection generator", "Markdown artifact generation"],
            OutOfScope = ["queue-state update logic"],
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "projection generator",
            Dependencies = ["A1"],
            TechnicalBaseline = ["C# / .NET"],
            ProjectLocalGuide = ["AGENTS.md"],
            IntentBaseline = ["A1 is complete"],
            IntentReferences = ["intents/intent-cli/intent-tree/00-map.md"],
            RulesAndSpecs = ["intents/rules/issue-projection-format.md"],
            AcceptanceCriteria = ["generates implementation.md", "generates packet.yaml"],
            VerificationEvidence = ["contract-reviewed", "tests-passing", "acceptance-criteria-checked"],
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash"
        };
    }
}
