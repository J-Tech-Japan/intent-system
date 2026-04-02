using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Tests;

public sealed class PacketGeneratorTests
{
    [Fact]
    public void Generate_GivenCompleteRowAndContext_ProducesImplementationMarkdown()
    {
        var result = PacketGenerator.Generate(CreateRow(), CreateContext());

        Assert.Contains("# [A2] Packet Generator", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Goal", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## In Scope", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Out Of Scope", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Target", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Dependencies", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Acceptance Criteria", result.ImplementationMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Verification", result.ImplementationMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_GivenCompleteRowAndContext_ProducesReviewContextMarkdown()
    {
        var result = PacketGenerator.Generate(CreateRow(), CreateContext());

        Assert.Contains("# Review Context", result.ReviewContextMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Parent Intent Root", result.ReviewContextMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Acceptance Criteria", result.ReviewContextMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Deterministic Review Checks", result.ReviewContextMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_GivenCompleteRowAndContext_ProducesPacketYaml()
    {
        var result = PacketGenerator.Generate(CreateRow(), CreateContext());

        Assert.Contains("implementation_issue_packet:", result.PacketYaml, StringComparison.Ordinal);
        Assert.Contains("review_context_packet:", result.PacketYaml, StringComparison.Ordinal);
        Assert.Contains("issue_title:", result.PacketYaml, StringComparison.Ordinal);
        Assert.Contains("issue_kind: feature", result.PacketYaml, StringComparison.Ordinal);
        Assert.Contains("source_execution_unit: A2", result.PacketYaml, StringComparison.Ordinal);
        Assert.Contains("deterministic_review_checks:", result.PacketYaml, StringComparison.Ordinal);
        Assert.Contains("clarification_return_path:", result.PacketYaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_GivenCompleteRowAndContext_ResolvesDeterministicPaths()
    {
        var result = PacketGenerator.Generate(CreateRow(), CreateContext());

        Assert.Equal(".intent-cli/issues/a2/implementation.md", result.Paths.Implementation);
        Assert.Equal(".intent-cli/issues/a2/review-context.md", result.Paths.ReviewContext);
        Assert.Equal(".intent-cli/issues/a2/packet.yaml", result.Paths.Yaml);
    }

    [Fact]
    public void Generate_GivenSameInputs_ProducesIdenticalOutput()
    {
        var row = CreateRow();
        var context = CreateContext();

        var first = PacketGenerator.Generate(row, context);
        var second = PacketGenerator.Generate(row, context);

        Assert.Equal(first.ImplementationMarkdown, second.ImplementationMarkdown);
        Assert.Equal(first.ReviewContextMarkdown, second.ReviewContextMarkdown);
        Assert.Equal(first.PacketYaml, second.PacketYaml);
        Assert.Equal(first.Paths, second.Paths);
    }

    [Fact]
    public void Generate_GivenNullRow_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PacketGenerator.Generate(null!, CreateContext()));
    }

    [Fact]
    public void Generate_GivenNullContext_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PacketGenerator.Generate(CreateRow(), null!));
    }

    private static SubSliceRow CreateRow()
    {
        return new SubSliceRow
        {
            SourceExecutionUnit = "A2",
            Goal = "Create packet generator that renders Markdown and YAML artifacts.",
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "projection generator",
            DependsOnSubslices = ["A1"],
            RelatedIntents = ["intents/intent-cli/intent-tree/00-map.md"],
            SourceConcepts =
            [
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/02-packet-generator-contract.md",
                "notes/domain-language.md"
            ],
            SuccessSignal = "packet generator produces deterministic Markdown and YAML from projection input",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash"
        };
    }

    private static ProjectionContext CreateContext()
    {
        return new ProjectionContext
        {
            IssueTitle = "[A2] Packet Generator",
            IssueKind = IssueKind.Feature,
            ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            AcceptanceCriteria =
            [
                "projection input generates implementation.md and review-context.md",
                "projection input generates packet.yaml",
                "artifact path is deterministic from execution_unit",
                "same input produces same packet",
                "generator does not modify source data"
            ],
            DeterministicReviewChecks =
            [
                "packet generator does not carry queue policy",
                "Markdown and YAML outputs can drop current projection schema",
                "artifact path is stable per execution unit"
            ],
            VerificationEvidence =
            [
                "contract-reviewed",
                "tests-passing",
                "acceptance-criteria-checked"
            ],
            TechnicalBaseline =
            [
                "C# / .NET",
                ".NET 10.0.100+ baseline"
            ],
            ProjectLocalGuide = ["AGENTS.md", "CLAUDE.md"],
            IntentBaseline =
            [
                "A1 is complete and projection schema field contract is fixed",
                "generator does not update source of truth"
            ],
            AdditionalInScope = ["Markdown artifact generation", "YAML artifact generation"],
            OutOfScope = ["projection schema redefinition", "queue-state update logic", "workflow engine"]
        };
    }
}
