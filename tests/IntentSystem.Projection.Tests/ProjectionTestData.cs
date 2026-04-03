using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Tests;

internal static class ProjectionTestData
{
    public static ProjectionSourceBundle CreateBundle()
    {
        return new ProjectionSourceBundle
        {
            Row = CreateRow(),
            Context = CreateContext()
        };
    }

    public static GeneratedPacket CreatePacket()
    {
        return PacketGenerator.Generate(CreateRow(), CreateContext());
    }

    private static SubSliceRow CreateRow()
    {
        return new SubSliceRow
        {
            SourceExecutionUnit = "G2",
            Goal = "Generate projection packet artifacts from current source data.",
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "cli projection command",
            DependsOnSubslices = ["G1", "A2"],
            DependsOn = [],
            RelatedIntents = ["intents/intent-cli/intent-tree/00-map.md"],
            SourceConcepts =
            [
                "intents/rules/issue-projection-format.md",
                "intents/intent-cli/specs/02-packet-generator-contract.md"
            ],
            SuccessSignal = "projection command can regenerate packet artifacts deterministically",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash"
        };
    }

    private static ProjectionContext CreateContext()
    {
        return new ProjectionContext
        {
            IssueTitle = "[G2] Projection Generate Command",
            IssueKind = IssueKind.Feature,
            ParentIntentRoot = "intents/intent-cli/intent-tree/00-map.md",
            ClarificationReturnPath = ".takt/runs/20260403-122452-issue-31-g2-projection-generat/context/task/order.md",
            AcceptanceCriteria =
            [
                "projection generate writes implementation.md",
                "projection regenerate writes packet.yaml deterministically"
            ],
            DeterministicReviewChecks =
            [
                "artifact path stays under .intent-cli/issues/<execution-unit>/",
                "projection command stays thin"
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
                "G1 and A2 are fixed baselines"
            ],
            AdditionalInScope =
            [
                "artifact path baseline",
                "generator wiring"
            ],
            OutOfScope =
            [
                "queue mutation",
                "workflow execution",
                "GitHub mutation"
            ]
        };
    }
}
