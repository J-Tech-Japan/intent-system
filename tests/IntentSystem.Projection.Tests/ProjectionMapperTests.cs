using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Tests;

public sealed class ProjectionMapperTests
{
    [Fact]
    public void ToImplementationPacket_GivenCompleteRowAndContext_MapsAllRequiredFields()
    {
        var row = CreateRow();
        var context = CreateContext();
        var packet = ProjectionMapper.ToImplementationPacket(row, context);
        Assert.Equal(context.IssueTitle, packet.IssueTitle);
        Assert.Equal(context.IssueKind, packet.IssueKind);
        Assert.Equal(row.SourceExecutionUnit, packet.SourceExecutionUnit);
        Assert.Equal(row.Goal, packet.Goal);
        Assert.Equal(["projection schema", "implementation packet"], packet.InScope);
        Assert.Equal(["rendering implementation", "workflow engine"], packet.OutOfScope);
        Assert.Equal(row.TargetRepo, packet.TargetRepo);
        Assert.Equal(row.TargetPath, packet.TargetPath);
        Assert.Equal(row.TargetPart, packet.TargetPart);
        Assert.Equal(["A0", "B1"], packet.Dependencies);
        Assert.Equal(
            [
                "C# / .NET",
                ".NET 10.0.100+ baseline",
                "dnx or dotnet tool exec",
                "do not switch to Node or TypeScript toolchain",
                "do not commit node_modules or vendor artifacts"
            ],
            packet.TechnicalBaseline);
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], packet.ProjectLocalGuide);
        Assert.Equal(
            [
                "parent Intent repo remains the source of truth",
                "projection schema fixes the contract only",
                "issue body must contain the implementation-critical intent summary"
            ],
            packet.IntentBaseline);
        Assert.Equal(
            [
                "intents/intent-cli/intent-tree/00-map.md",
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/01-projection-schema.md",
                "intents/intent-cli/design/projection-architecture.md",
                "notes/domain-language.md"
            ],
            packet.IntentReferences);
        Assert.Equal(
            [
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/01-projection-schema.md",
                "intents/intent-cli/design/projection-architecture.md"
            ],
            packet.RulesAndSpecs);
        Assert.Equal(
            [
                "sub-slice row maps deterministically to implementation and review packets",
                "implementation packet carries verification evidence",
                "review context packet carries deterministic review checks and clarification return path"
            ],
            packet.AcceptanceCriteria);
        Assert.Equal(
            [
                "contract-reviewed",
                "tests-passing",
                "acceptance-criteria-checked"
            ],
            packet.VerificationEvidence);
        Assert.Equal(row.ReviewMode, packet.ReviewMode);
        Assert.Equal(row.CompletionAction, packet.CompletionAction);
        Assert.Equal(row.LandingPolicy, packet.LandingPolicy);
    }

    [Fact]
    public void ToImplementationPacket_GivenSameInputs_ReturnsTheSameProjection()
    {
        var row = CreateRow();
        var context = CreateContext();
        var first = ProjectionMapper.ToImplementationPacket(row, context);
        var second = ProjectionMapper.ToImplementationPacket(row, context);
        AssertEquivalent(first, second);
    }

    [Fact]
    public void ToImplementationPacket_GivenDependsOnSubslices_UsesThemForDependencies()
    {
        var row = CreateRow() with
        {
            DependsOnSubslices = ["A0", "B1"],
            DependsOn = ["legacy-dependency"]
        };
        var context = CreateContext();
        var packet = ProjectionMapper.ToImplementationPacket(row, context);
        Assert.Equal(["A0", "B1"], packet.Dependencies);
    }

    [Fact]
    public void ToImplementationPacket_GivenNoDependsOnSubslices_FallsBackToDependsOn()
    {
        var row = CreateRow() with
        {
            DependsOnSubslices = [],
            DependsOn = ["issue-7", "issue-8"]
        };
        var context = CreateContext();
        var packet = ProjectionMapper.ToImplementationPacket(row, context);
        Assert.Equal(["issue-7", "issue-8"], packet.Dependencies);
    }

    [Fact]
    public void ToImplementationPacket_GivenNoMatchingRulePaths_LeavesRulesAndSpecsEmpty()
    {
        var row = CreateRow() with
        {
            RelatedIntents = ["intents/intent-cli/intent-tree/00-map.md"],
            SourceConcepts = ["notes/domain-language.md", "intents/intent-cli/concepts/projection.md"]
        };
        var context = CreateContext();
        var packet = ProjectionMapper.ToImplementationPacket(row, context);
        Assert.Equal(
            [
                "intents/intent-cli/intent-tree/00-map.md",
                "notes/domain-language.md",
                "intents/intent-cli/concepts/projection.md"
            ],
            packet.IntentReferences);
        Assert.Empty(packet.RulesAndSpecs);
    }

    [Fact]
    public void ToImplementationPacket_GivenNoAdditionalScope_UsesTargetPartAsTheOnlyInScopeEntry()
    {
        var row = CreateRow();
        var context = CreateContext() with
        {
            AdditionalInScope = []
        };
        var packet = ProjectionMapper.ToImplementationPacket(row, context);
        Assert.Equal([row.TargetPart], packet.InScope);
    }

    [Fact]
    public void ToImplementationPacket_GivenMissingRequiredVerificationEvidence_ThrowsInvalidOperationException()
    {
        var row = CreateRow();
        var context = CreateContext() with
        {
            VerificationEvidence = ["contract-reviewed", "tests-passing"]
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionMapper.ToImplementationPacket(row, context));

        Assert.Contains("acceptance-criteria-checked", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToReviewContextPacket_GivenCompleteRowAndContext_MapsAllRequiredFields()
    {
        var row = CreateRow();
        var context = CreateContext();
        var packet = ProjectionMapper.ToReviewContextPacket(row, context);
        Assert.Equal(row.SourceExecutionUnit, packet.SourceExecutionUnit);
        Assert.Equal(context.ParentIntentRoot, packet.ParentIntentRoot);
        Assert.Equal(
            [
                "intents/intent-cli/intent-tree/00-map.md",
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/01-projection-schema.md",
                "intents/intent-cli/design/projection-architecture.md",
                "notes/domain-language.md"
            ],
            packet.IntentReferences);
        Assert.Equal(
            [
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/01-projection-schema.md",
                "intents/intent-cli/design/projection-architecture.md"
            ],
            packet.RulesAndSpecs);
        Assert.Equal(
            [
                "sub-slice row maps deterministically to implementation and review packets",
                "implementation packet carries verification evidence",
                "review context packet carries deterministic review checks and clarification return path"
            ],
            packet.AcceptanceCriteria);
        Assert.Equal(
            [
                "source of truth remains in the parent intent repository",
                "projected packets do not introduce new business rules",
                "sub-slice mapping can be regenerated"
            ],
            packet.DeterministicReviewChecks);
        Assert.Equal(context.ClarificationReturnPath, packet.ClarificationReturnPath);
    }

    private static SubSliceRow CreateRow()
    {
        return new SubSliceRow
        {
            SourceExecutionUnit = "A1",
            Goal = "Fix the projection schema contract for sub-slice packets.",
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "projection schema",
            DependsOnSubslices = ["A0", "B1"],
            DependsOn = ["legacy-dependency"],
            RelatedIntents = ["intents/intent-cli/intent-tree/00-map.md"],
            SourceConcepts =
            [
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/01-projection-schema.md",
                "intents/intent-cli/design/projection-architecture.md",
                "notes/domain-language.md"
            ],
            SuccessSignal = "sub-slice row maps deterministically to implementation and review packets",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash"
        };
    }

    private static ProjectionContext CreateContext()
    {
        return new ProjectionContext
        {
            IssueTitle = "Issue #5: [A1] Projection Schema",
            IssueKind = IssueKind.Feature,
            ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
            ClarificationReturnPath = "intents/rules/issue-template-and-review-context.md",
            AcceptanceCriteria =
            [
                "sub-slice row maps deterministically to implementation and review packets",
                "implementation packet carries verification evidence",
                "review context packet carries deterministic review checks and clarification return path"
            ],
            DeterministicReviewChecks =
            [
                "source of truth remains in the parent intent repository",
                "projected packets do not introduce new business rules",
                "sub-slice mapping can be regenerated"
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
                ".NET 10.0.100+ baseline",
                "dnx or dotnet tool exec",
                "do not switch to Node or TypeScript toolchain",
                "do not commit node_modules or vendor artifacts"
            ],
            ProjectLocalGuide = ["AGENTS.md", "CLAUDE.md"],
            IntentBaseline =
            [
                "parent Intent repo remains the source of truth",
                "projection schema fixes the contract only",
                "issue body must contain the implementation-critical intent summary"
            ],
            AdditionalInScope = ["implementation packet"],
            OutOfScope = ["rendering implementation", "workflow engine"]
        };
    }

    private static void AssertEquivalent(
        ImplementationIssuePacket expected,
        ImplementationIssuePacket actual)
    {
        Assert.Equal(expected.IssueTitle, actual.IssueTitle);
        Assert.Equal(expected.IssueKind, actual.IssueKind);
        Assert.Equal(expected.SourceExecutionUnit, actual.SourceExecutionUnit);
        Assert.Equal(expected.Goal, actual.Goal);
        Assert.Equal(expected.InScope, actual.InScope);
        Assert.Equal(expected.OutOfScope, actual.OutOfScope);
        Assert.Equal(expected.TargetRepo, actual.TargetRepo);
        Assert.Equal(expected.TargetPath, actual.TargetPath);
        Assert.Equal(expected.TargetPart, actual.TargetPart);
        Assert.Equal(expected.Dependencies, actual.Dependencies);
        Assert.Equal(expected.TechnicalBaseline, actual.TechnicalBaseline);
        Assert.Equal(expected.ProjectLocalGuide, actual.ProjectLocalGuide);
        Assert.Equal(expected.IntentBaseline, actual.IntentBaseline);
        Assert.Equal(expected.IntentReferences, actual.IntentReferences);
        Assert.Equal(expected.RulesAndSpecs, actual.RulesAndSpecs);
        Assert.Equal(expected.AcceptanceCriteria, actual.AcceptanceCriteria);
        Assert.Equal(expected.VerificationEvidence, actual.VerificationEvidence);
        Assert.Equal(expected.ReviewMode, actual.ReviewMode);
        Assert.Equal(expected.CompletionAction, actual.CompletionAction);
        Assert.Equal(expected.LandingPolicy, actual.LandingPolicy);
    }
}
