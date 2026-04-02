using IntentSystem.Projection.Models;
using IntentSystem.Projection.Rendering;

namespace IntentSystem.Projection.Tests;

public sealed class ImplementationMarkdownRendererTests
{
    [Fact]
    public void Render_GivenPacket_StartsWithIssueTitleAsH1()
    {
        var markdown = ImplementationMarkdownRenderer.Render(CreatePacket());

        Assert.StartsWith("# [A2] Packet Generator", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_IncludesKindAndExecutionUnitInHeader()
    {
        var markdown = ImplementationMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("**kind**: `feature`", markdown, StringComparison.Ordinal);
        Assert.Contains("**execution-unit**: `A2`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_IncludesTargetSection()
    {
        var markdown = ImplementationMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("## Target", markdown, StringComparison.Ordinal);
        Assert.Contains("**repo**: `J-Tech-Japan/intent-system`", markdown, StringComparison.Ordinal);
        Assert.Contains("**path**: `.`", markdown, StringComparison.Ordinal);
        Assert.Contains("**part**: `projection generator`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_IncludesVerificationWithBacktickEvidence()
    {
        var markdown = ImplementationMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("`contract-reviewed`", markdown, StringComparison.Ordinal);
        Assert.Contains("`tests-passing`", markdown, StringComparison.Ordinal);
        Assert.Contains("`acceptance-criteria-checked`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenPacket_IncludesWorkflowSection()
    {
        var markdown = ImplementationMarkdownRenderer.Render(CreatePacket());

        Assert.Contains("## Workflow", markdown, StringComparison.Ordinal);
        Assert.Contains("**review-mode**: `manual-review`", markdown, StringComparison.Ordinal);
        Assert.Contains("**completion-action**: `open-pr`", markdown, StringComparison.Ordinal);
        Assert.Contains("**landing-policy**: `squash`", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenEmptyDependencies_ShowsNone()
    {
        var packet = CreatePacket() with { Dependencies = [] };
        var markdown = ImplementationMarkdownRenderer.Render(packet);

        var dependenciesIndex = markdown.IndexOf("## Dependencies", StringComparison.Ordinal);
        var nextSectionIndex = markdown.IndexOf("## Technical Baseline", StringComparison.Ordinal);
        var dependenciesSection = markdown[dependenciesIndex..nextSectionIndex];

        Assert.Contains("(none)", dependenciesSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_GivenSamePacket_ProducesIdenticalOutput()
    {
        var packet = CreatePacket();

        var first = ImplementationMarkdownRenderer.Render(packet);
        var second = ImplementationMarkdownRenderer.Render(packet);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Render_GivenBugfixKind_FormatsBugfixKebabStyle()
    {
        var packet = CreatePacket() with { IssueKind = IssueKind.BoundaryFix };
        var markdown = ImplementationMarkdownRenderer.Render(packet);

        Assert.Contains("**kind**: `boundary-fix`", markdown, StringComparison.Ordinal);
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
            OutOfScope = ["queue-state update logic", "workflow engine"],
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "projection generator",
            Dependencies = ["A1"],
            TechnicalBaseline = ["C# / .NET", ".NET 10.0.100+ baseline"],
            ProjectLocalGuide = ["AGENTS.md", "CLAUDE.md"],
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
