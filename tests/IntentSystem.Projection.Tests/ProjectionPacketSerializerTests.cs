using IntentSystem.Projection.Models;
using IntentSystem.Projection.Serialization;

namespace IntentSystem.Projection.Tests;

public sealed class ProjectionPacketSerializerTests
{
    [Fact]
    public void Deserialize_GivenParentStylePacketYaml_ReturnsPacketContract()
    {
        var packet = ProjectionPacketSerializer.Deserialize(
            """
            implementation_issue_packet:
              issue_title: "G2 Projection Generate Command"
              issue_kind: "feature"
              source_execution_unit: "G2"
              goal: "`intent-cli projection generate/regenerate` を working command にする"
              in_scope:
                - "`projection generate` と `projection regenerate` の CLI command 実装"
                - "artifact path と output format が current baseline に従うことの固定"
              out_of_scope:
                - "queue state mutation"
                - "workflow 実行や worker adapter 呼び出し"
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "cli projection command"
              dependencies:
                - "G1"
                - "A2"
              technical_baseline:
                - "C# / .NET"
                - ".NET 10.0.100+ baseline"
              project_local_guide:
                - "submodules/intent-system/AGENTS.md"
              intent_baseline:
                - "source of truth remains in the parent intent repo"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
                - "intents/rules/issue-projection-format.md"
              rules_and_specs:
                - "intents/rules/issue-projection-format.md"
              acceptance_criteria:
                - "`intent-cli projection generate <execution-unit>` が current source data から `.intent-cli/issues/<execution-unit>/implementation.md` を生成できる"
              verification_evidence:
                - "contract-reviewed"
                - "tests-passing"
                - "acceptance-criteria-checked"
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"
            
            review_context_packet:
              source_execution_unit: "G2"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
                - "ICL.P.PRODUCT_GOAL"
                - "intents/rules/issue-projection-format.md"
              rules_and_specs:
                - "intents/rules/issue-projection-format.md"
              acceptance_criteria:
                - "`intent-cli projection generate <execution-unit>` が current source data から `.intent-cli/issues/<execution-unit>/implementation.md` を生成できる"
              deterministic_review_checks:
                - "generated path が `.intent-cli/issues/<execution-unit>/` baseline に従っている"
              clarification_return_path: "intents/intent-cli/clarifications/open.md"
            """);

        Assert.Equal("G2 Projection Generate Command", packet.ImplementationIssuePacket.IssueTitle);
        Assert.Equal(IssueKind.Feature, packet.ImplementationIssuePacket.IssueKind);
        Assert.Equal("G2", packet.ImplementationIssuePacket.SourceExecutionUnit);
        Assert.Equal("submodules/intent-system", packet.ImplementationIssuePacket.TargetRepo);
        Assert.Equal("intents/intent-cli/intent-tree/00-map.md", packet.ReviewContextPacket.ParentIntentRoot);
        Assert.Equal("intents/intent-cli/clarifications/open.md", packet.ReviewContextPacket.ClarificationReturnPath);
    }

    [Fact]
    public void Deserialize_GivenMissingRequiredField_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize(
                """
                implementation_issue_packet:
                  issue_title: "G2 Projection Generate Command"
                
                review_context_packet:
                  source_execution_unit: "G2"
                  parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
                  intent_references: []
                  rules_and_specs: []
                  acceptance_criteria: []
                  deterministic_review_checks: []
                  clarification_return_path: "path.md"
                """));

        Assert.Contains("issue_kind", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_GivenMismatchedExecutionUnits_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionPacketSerializer.Deserialize(
                """
                implementation_issue_packet:
                  issue_title: "G2 Projection Generate Command"
                  issue_kind: "feature"
                  source_execution_unit: "G2"
                  goal: "goal"
                  in_scope: []
                  out_of_scope: []
                  target_repo: "repo"
                  target_path: "."
                  target_part: "part"
                  dependencies: []
                  technical_baseline: []
                  project_local_guide: []
                  intent_baseline: []
                  intent_references: []
                  rules_and_specs: []
                  acceptance_criteria: []
                  verification_evidence: []
                  review_mode: "manual-review"
                  completion_action: "open-pr"
                  landing_policy: "squash"
                
                review_context_packet:
                  source_execution_unit: "G3"
                  parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
                  intent_references: []
                  rules_and_specs: []
                  acceptance_criteria: []
                  deterministic_review_checks: []
                  clarification_return_path: "path.md"
                """));

        Assert.Contains("must match", exception.Message, StringComparison.Ordinal);
    }
}
