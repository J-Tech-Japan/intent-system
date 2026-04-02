using IntentSystem.Projection.Models;
using IntentSystem.Projection.Rendering;

namespace IntentSystem.Projection.Tests;

public sealed class PacketYamlRendererTests
{
    [Fact]
    public void Render_GivenPackets_IncludesBothTopLevelSections()
    {
        var yaml = PacketYamlRenderer.Render(CreateImplementationPacket(), CreateReviewContextPacket());

        Assert.Contains("implementation_issue_packet:", yaml, StringComparison.Ordinal);
        Assert.Contains("review_context_packet:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPackets_IncludesImplementationFields()
    {
        var yaml = PacketYamlRenderer.Render(CreateImplementationPacket(), CreateReviewContextPacket());

        Assert.Contains("  issue_title:", yaml, StringComparison.Ordinal);
        Assert.Contains("  issue_kind:", yaml, StringComparison.Ordinal);
        Assert.Contains("  source_execution_unit:", yaml, StringComparison.Ordinal);
        Assert.Contains("  goal:", yaml, StringComparison.Ordinal);
        Assert.Contains("  in_scope:", yaml, StringComparison.Ordinal);
        Assert.Contains("  out_of_scope:", yaml, StringComparison.Ordinal);
        Assert.Contains("  target_repo:", yaml, StringComparison.Ordinal);
        Assert.Contains("  acceptance_criteria:", yaml, StringComparison.Ordinal);
        Assert.Contains("  verification_evidence:", yaml, StringComparison.Ordinal);
        Assert.Contains("  review_mode:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPackets_IncludesReviewContextFields()
    {
        var yaml = PacketYamlRenderer.Render(CreateImplementationPacket(), CreateReviewContextPacket());

        Assert.Contains("  parent_intent_root:", yaml, StringComparison.Ordinal);
        Assert.Contains("  deterministic_review_checks:", yaml, StringComparison.Ordinal);
        Assert.Contains("  clarification_return_path:", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPackets_FormatsIssueKindAsKebabCase()
    {
        var yaml = PacketYamlRenderer.Render(CreateImplementationPacket(), CreateReviewContextPacket());

        Assert.Contains("issue_kind: feature", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenBoundaryFixKind_FormatsAsKebabCase()
    {
        var implPacket = CreateImplementationPacket() with { IssueKind = IssueKind.BoundaryFix };
        var yaml = PacketYamlRenderer.Render(implPacket, CreateReviewContextPacket());

        Assert.Contains("issue_kind: boundary-fix", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenEmptyList_RendersEmptyArraySyntax()
    {
        var implPacket = CreateImplementationPacket() with { RulesAndSpecs = [] };
        var yaml = PacketYamlRenderer.Render(implPacket, CreateReviewContextPacket());

        Assert.Contains("rules_and_specs: []", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenListItems_RendersAsIndentedBulletedItems()
    {
        var yaml = PacketYamlRenderer.Render(CreateImplementationPacket(), CreateReviewContextPacket());

        Assert.Contains("    - A1", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenValueWithSpecialChars_QuotesTheValue()
    {
        var implPacket = CreateImplementationPacket() with { Goal = "Fix the projection: schema contract" };
        var yaml = PacketYamlRenderer.Render(implPacket, CreateReviewContextPacket());

        Assert.Contains("goal: \"Fix the projection: schema contract\"", yaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenSamePackets_ProducesIdenticalOutput()
    {
        var implPacket = CreateImplementationPacket();
        var reviewPacket = CreateReviewContextPacket();

        var first = PacketYamlRenderer.Render(implPacket, reviewPacket);
        var second = PacketYamlRenderer.Render(implPacket, reviewPacket);

        Assert.Equal(first, second);
    }

    private static ImplementationIssuePacket CreateImplementationPacket()
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

    private static ReviewContextPacket CreateReviewContextPacket()
    {
        return new ReviewContextPacket
        {
            SourceExecutionUnit = "A2",
            ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
            IntentReferences = ["intents/intent-cli/intent-tree/00-map.md"],
            RulesAndSpecs = ["intents/rules/issue-projection-format.md"],
            AcceptanceCriteria = ["generates implementation.md", "generates packet.yaml"],
            DeterministicReviewChecks =
            [
                "packet generator does not carry queue policy",
                "artifact path is stable per execution unit"
            ],
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md"
        };
    }
}
