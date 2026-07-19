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
    public void Deserialize_GivenOptionalIntentMaintenanceMetadata_StillValidates()
    {
        // G461 regression: appending the optional packet-time intent-maintenance
        // metadata (intent_placement / knowledge_updates / closeout_learning) must
        // NOT break a packet that already satisfies the required contract. Legacy
        // packets omit it entirely; new packets carry it. Both must deserialize.
        var packet = ProjectionPacketSerializer.Deserialize(
            """
            implementation_issue_packet:
              issue_title: "G461 Packet-time intent maintenance"
              issue_kind: "feature"
              source_execution_unit: "G461"
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
              review_mode: "deterministic-review"
              completion_action: "open-pr"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G461"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references: []
              rules_and_specs: []
              acceptance_criteria: []
              deterministic_review_checks: []
              clarification_return_path: "path.md"

            intent_placement:
              primary_intent: "intents/intent-cli/intent-tree/00-map.md"
              supporting_intents: []
              new_intent_needed: false
              placement_rationale: ""
            knowledge_updates:
              intent_tree:
                required: false
                target_paths: []
                summary: ""
              adr:
                required: false
                target_paths: []
                decision_title: ""
              diagram:
                required: false
                target_paths: []
                diagram_type: none
              docs:
                required: false
                target_paths: []
                summary: ""
            closeout_learning:
              expected: ""
              write_back_required: false
              write_back_targets: []
            """);

        // The optional metadata is ignored by the required-field contract; the
        // packet still deserializes to the same shape as a packet without it.
        Assert.Equal("G461", packet.ImplementationIssuePacket.SourceExecutionUnit);
        Assert.Equal("G461", packet.ReviewContextPacket.SourceExecutionUnit);
    }

    [Fact]
    public void Deserialize_GivenTwoSpaceListItemIndentation_ParsesSuccessfully()
    {
        // G534 field finding: a hand-authored packet using the common
        // "list item at the SAME column as its parent key" YAML
        // convention (2-space, not this renderer's own 4-space nested
        // convention) previously threw "field line is missing ':''" on
        // every list item — quoted or unquoted. Both forms are pinned
        // here, mixed within the same packet, matching the real
        // previously-failing shape.
        var packet = ProjectionPacketSerializer.Deserialize(
            """
            implementation_issue_packet:
              issue_title: "G2 Projection Generate Command"
              issue_kind: "feature"
              source_execution_unit: "G2"
              goal: "goal"
              in_scope:
              - "quoted scope item"
              out_of_scope:
              - unquoted scope item
              target_repo: "repo"
              target_path: "."
              target_part: "part"
              dependencies:
              - "G1"
              technical_baseline: []
              project_local_guide: []
              intent_baseline: []
              intent_references:
              - intents/foo/bar.md
              - "intents/baz/qux.md"
              rules_and_specs: []
              acceptance_criteria:
              - unquoted criterion
              verification_evidence: []
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G2"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references:
              - intents/foo/bar.md
              - "intents/baz/qux.md"
              rules_and_specs: []
              acceptance_criteria:
              - unquoted criterion
              deterministic_review_checks: []
              clarification_return_path: "path.md"
            """);

        Assert.Equal("G2", packet.ImplementationIssuePacket.SourceExecutionUnit);
        Assert.Equal(["quoted scope item"], packet.ImplementationIssuePacket.InScope);
        Assert.Equal(["unquoted scope item"], packet.ImplementationIssuePacket.OutOfScope);
        Assert.Equal(["G1"], packet.ImplementationIssuePacket.Dependencies);
        Assert.Equal(["intents/foo/bar.md", "intents/baz/qux.md"], packet.ImplementationIssuePacket.IntentReferences);
        Assert.Equal(["unquoted criterion"], packet.ImplementationIssuePacket.AcceptanceCriteria);
    }

    [Fact]
    public void Deserialize_GivenMixedTwoAndFourSpaceListIndentationAcrossFields_BothParseIdentically()
    {
        // Different fields in the SAME file may use either convention —
        // the parser must not require file-wide consistency.
        var packet = ProjectionPacketSerializer.Deserialize(
            """
            implementation_issue_packet:
              issue_title: "G2 Projection Generate Command"
              issue_kind: "feature"
              source_execution_unit: "G2"
              goal: "goal"
              in_scope:
                - "four-space item"
              out_of_scope:
              - "two-space item"
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
              review_mode: "deterministic-review"
              completion_action: "wait-for-deterministic-review"
              landing_policy: "merge-after-review"

            review_context_packet:
              source_execution_unit: "G2"
              parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
              intent_references: []
              rules_and_specs: []
              acceptance_criteria: []
              deterministic_review_checks: []
              clarification_return_path: "path.md"
            """);

        Assert.Equal(["four-space item"], packet.ImplementationIssuePacket.InScope);
        Assert.Equal(["two-space item"], packet.ImplementationIssuePacket.OutOfScope);
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
