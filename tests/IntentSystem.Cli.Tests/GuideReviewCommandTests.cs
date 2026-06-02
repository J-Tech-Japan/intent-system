using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideReviewCommandTests
{
    [Fact]
    public void Execute_EmitsDeviceGatedEvidencePolicy_WithApproveWithGapAndHardBlockRules_G445()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var policy = document.RootElement.GetProperty("device_gated_evidence_policy")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.NotEmpty(policy);
        // Distinguishes ordinary device-gap (approve-with-recorded-gap) from hard blockers.
        Assert.Contains(policy, p => p.Contains("device-gap", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(policy, p => p.Contains("Approve-with-recorded-gap", StringComparison.Ordinal)
            && p.Contains("source/log/unit/simulator", StringComparison.Ordinal));
        Assert.Contains(policy, p => p.Contains("HARD-BLOCK", StringComparison.Ordinal)
            && p.Contains("primary deliverable", StringComparison.Ordinal));
        // No-false-claim rule.
        Assert.Contains(policy, p => p.Contains("NEVER claim", StringComparison.Ordinal)
            && p.Contains("NOT collected", StringComparison.Ordinal));
        // Durable follow-up tracking required.
        Assert.Contains(policy, p => p.Contains("follow-up", StringComparison.OrdinalIgnoreCase));
        // Do not re-ask the standing-policy question per packet.
        Assert.Contains(policy, p => p.Contains("Do NOT re-ask", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_IncludesDeviceGatedEvidencePolicySection_G445()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/packet.yaml", "x");

        using var writer = new StringWriter();
        GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "markdown"],
            writer);

        var output = writer.ToString();
        Assert.Contains("## Device-gated evidence policy (G445)", output, StringComparison.Ordinal);
        Assert.Contains("Approve-with-recorded-gap", output, StringComparison.Ordinal);
        Assert.Contains("HARD-BLOCK", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenQueueMatchAndReviewContext_EmitsReadyTrueWithExcerpt()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/review-context.md",
            """
            # G248 Review Context

            Review that this slice keeps review-only behavior and emits deterministic guidance.

            Flag findings if the implementation:

            - launches AI providers from `intent-cli`;
            - mutates GitHub or parent state for a read-only command.
            """);
        workspace.WriteFile(".intent-cli/issues/G248/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("G248", root.GetProperty("execution_unit").GetString());
        Assert.Equal("review", root.GetProperty("queue_item_state").GetString());
        Assert.Equal("guide review", root.GetProperty("queue_item_title").GetString());
        Assert.Contains("Review that this slice", root.GetProperty("review_context_head").GetString()!, StringComparison.Ordinal);
        Assert.True(root.GetProperty("review_checklist").GetArrayLength() >= 5);
        Assert.True(root.GetProperty("review_boundaries").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("validation_suggestions").GetArrayLength() >= 2);
        Assert.Equal(0, root.GetProperty("gaps").GetArrayLength());
    }

    [Fact]
    public void Execute_GivenQueueMatchWithoutReviewContext_EmitsReadyTrueWithoutExcerpt()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.False(root.TryGetProperty("review_context_head", out _));
    }

    [Fact]
    public void Execute_GivenNoMatchingLinkedPr_ReportsQueueGap()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "999"));

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("no queue item found with linked_pr", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenSamePrNumberInDifferentRepo_SkipsOtherRepo()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState("""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G192",
                  "title": "wrong repo",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {"repo": "J-Tech-Japan/intent-system", "number": 490, "url": "https://github.com/J-Tech-Japan/intent-system/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "SKS-G185",
                  "title": "right repo",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 490, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        workspace.WriteFile(".intent-cli/issues/SKS-G185/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--pr", "490", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("SKS-G185", document.RootElement.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_GivenMissingPacketDirectory_ReportsPacketGap()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("packet directory not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMissingQueueState_ReportsQueueStateGap()
    {
        using var workspace = new GuideReviewWorkspace();
        // No queue state written.

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("queue-state file not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_MarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G248", "review", title: "guide review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G248/review-context.md", "# G248 Review Context\nReview head.\n");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide review — J-Tech-Japan/intent-system#598", output, StringComparison.Ordinal);
        Assert.Contains("ready: yes", output, StringComparison.Ordinal);
        Assert.Contains("## Review checklist", output, StringComparison.Ordinal);
        Assert.Contains("## Review boundaries", output, StringComparison.Ordinal);
        Assert.Contains("## Validation suggestions", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingPr_ReturnsUsageError()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--pr", "598"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    // --- G316: intent-and-packet-aware review --------------------------------

    [Fact]
    public void Execute_G316_JsonIncludesPacketPathsAndIntentReferenceAndSufficiencyFields()
    {
        // Acceptance: guide review must surface structured packet_paths
        // (canonical packet files with exists flags),
        // intent_reference_paths (PR-specific paths parsed from packet
        // artifacts — never broad directory pointers),
        // approval_summary_requirements, request_update_requirements,
        // and tests_pass_is_necessary_not_sufficient: true.
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "execution_unit: G316");
        workspace.WriteFile(".intent-cli/issues/G316/implementation.md", "# Implementation");
        // Seed only specs/ to confirm intent_reference_paths is NOT
        // populated by directory existence alone — narrow semantics.
        workspace.WriteFile("intents/intent-cli/specs/00-map.md", "map");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        // tests-pass sufficiency signal
        Assert.True(root.GetProperty("tests_pass_is_necessary_not_sufficient").GetBoolean());

        // packet_paths covers the four canonical files in the documented order
        var packetPaths = root.GetProperty("packet_paths").EnumerateArray().ToArray();
        Assert.Equal(4, packetPaths.Length);
        Assert.Equal("packet.yaml", packetPaths[0].GetProperty("name").GetString());
        Assert.Equal("implementation.md", packetPaths[1].GetProperty("name").GetString());
        Assert.Equal("review-context.md", packetPaths[2].GetProperty("name").GetString());
        Assert.Equal("github-body.md", packetPaths[3].GetProperty("name").GetString());
        Assert.True(packetPaths[0].GetProperty("exists").GetBoolean()); // packet.yaml seeded
        Assert.True(packetPaths[1].GetProperty("exists").GetBoolean()); // implementation.md seeded
        Assert.False(packetPaths[2].GetProperty("exists").GetBoolean()); // review-context.md not seeded
        Assert.False(packetPaths[3].GetProperty("exists").GetBoolean()); // github-body.md not seeded

        // G316 review-fix: when the packet text does NOT mention any
        // `intents/<domain>/...` path, intent_reference_paths is empty.
        // The mere existence of `intents/intent-cli/specs/` on disk is
        // NOT enough to populate the field — that would nudge the
        // reviewer toward full-tree traversal.
        var intentRefs = root.GetProperty("intent_reference_paths").EnumerateArray().ToArray();
        Assert.Empty(intentRefs);

        // approval_summary_requirements references packet contract, AC, OOS,
        // intent reference, tests-pass-paired-with-evidence, and Closes ref.
        var approvalReqs = root.GetProperty("approval_summary_requirements")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.True(approvalReqs.Length >= 5);
        Assert.Contains(approvalReqs, r => r.Contains("packet.yaml", StringComparison.Ordinal));
        Assert.Contains(approvalReqs, r => r.Contains("acceptance criteria", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(approvalReqs, r => r.Contains("out-of-scope", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(approvalReqs, r => r.Contains("intent / spec / rule", StringComparison.Ordinal));
        Assert.Contains(approvalReqs, r => r.Contains("Closes #", StringComparison.Ordinal));
        Assert.Contains(approvalReqs, r => r.Contains("necessary, not sufficient", StringComparison.Ordinal));

        // request_update_requirements force the three-way classification.
        var requestReqs = root.GetProperty("request_update_requirements")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(requestReqs, r => r.Contains("implementation-finding", StringComparison.Ordinal));
        Assert.Contains(requestReqs, r => r.Contains("host-metadata-blocked", StringComparison.Ordinal));
        Assert.Contains(requestReqs, r => r.Contains("intent-ambiguity", StringComparison.Ordinal));
        // Tests-only failure mode is itself an implementation-finding.
        Assert.Contains(requestReqs, r => r.Contains("tests pass but evidence missing", StringComparison.Ordinal)
            || r.Contains("packet/intent conformance", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G316_ChecklistEnforcesPacketAndIntentEvidenceBeyondTests()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var checklist = document.RootElement.GetProperty("review_checklist")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        // Each canonical packet file is named in the checklist.
        Assert.Contains(checklist, item => item.Contains("packet.yaml", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("implementation.md", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("review-context.md", StringComparison.Ordinal));
        // Acceptance Criteria + Out-of-Scope boundaries
        Assert.Contains(checklist, item => item.Contains("Acceptance Criteria", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("Out-of-scope boundaries", StringComparison.Ordinal));
        // Related intent/spec/rule reference
        Assert.Contains(checklist, item => item.Contains("intent / spec / rule", StringComparison.Ordinal)
            || item.Contains("design intent", StringComparison.Ordinal));
        // PR closing reference (G311) — explicitly required
        Assert.Contains(checklist, item => item.Contains("Closes/Fixes/Resolves", StringComparison.Ordinal)
            || item.Contains("G311", StringComparison.Ordinal));
        // Tests-pass-not-sufficient explicit
        Assert.Contains(checklist, item =>
            item.Contains("NECESSARY but NOT SUFFICIENT", StringComparison.Ordinal)
            || item.Contains("necessary but not sufficient", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_G316_MarkdownIncludesNewSections()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Intent reference paths", output, StringComparison.Ordinal);
        Assert.Contains("## Sufficiency of evidence", output, StringComparison.Ordinal);
        Assert.Contains("tests_pass_is_necessary_not_sufficient: yes", output, StringComparison.Ordinal);
        Assert.Contains("## Approval summary requirements", output, StringComparison.Ordinal);
        Assert.Contains("## Request-update requirements", output, StringComparison.Ordinal);
        Assert.Contains("canonical paths:", output, StringComparison.Ordinal);
        // G316 review-fix: when the packet text references no
        // `intents/...` paths, the markdown explicitly says "(none
        // referenced by packet)" rather than listing broad domain
        // directories that nudge the reviewer toward full-tree
        // traversal.
        Assert.Contains("(none referenced by packet)", output, StringComparison.Ordinal);
    }

    // --- G394: durable PR blocker comments + follow-up split -----------------

    [Fact]
    public void Execute_G394_JsonIncludesBlockerProtocolTemplateAndRoutingExamples()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G394", "review", title: "durable blocker comments", linkedPr: "889"));
        workspace.WriteFile(".intent-cli/issues/G394/packet.yaml", "execution_unit: G394");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "889", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        // Chat is not durable workflow state.
        Assert.True(root.GetProperty("chat_is_not_durable_workflow_state").GetBoolean());

        // Protocol: mandatory durable PR comment before request-update/clarification;
        // PR-comment-vs-follow-up split; host-metadata never a PR comment.
        var protocol = root.GetProperty("review_blocker_protocol")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(protocol, r =>
            r.Contains("PR blocker comment", StringComparison.Ordinal)
            && r.Contains("BEFORE", StringComparison.Ordinal));
        Assert.Contains(protocol, r => r.Contains("Chat-only", StringComparison.Ordinal)
            || r.Contains("chat-only", StringComparison.Ordinal));
        Assert.Contains(protocol, r => r.Contains("follow-up issue/packet/signal", StringComparison.Ordinal));
        Assert.Contains(protocol, r => r.Contains("host-metadata", StringComparison.OrdinalIgnoreCase)
            || r.Contains(".intent-cli/**", StringComparison.Ordinal));

        // Blocker comment template covers failed AC, insufficient evidence,
        // required unblock action, false-claim boundaries, and follow-up links.
        var template = root.GetProperty("pr_blocker_comment_template")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(template, t => t.Contains("Failed acceptance criterion", StringComparison.Ordinal));
        Assert.Contains(template, t => t.Contains("insufficient", StringComparison.Ordinal));
        Assert.Contains(template, t => t.Contains("Required unblock action", StringComparison.Ordinal));
        Assert.Contains(template, t => t.Contains("False-claim boundaries", StringComparison.Ordinal));
        Assert.Contains(template, t => t.Contains("Follow-up", StringComparison.Ordinal));

        // Routing examples: the Zero4Racer #406 case → current-PR blocker,
        // durable PR comment, clarification-required, follow-up issue.
        var examples = root.GetProperty("review_blocker_routing_examples").EnumerateArray().ToArray();
        Assert.True(examples.Length >= 4);
        var z4r = examples[0];
        Assert.Contains("Zero4Racer PR #406", z4r.GetProperty("scenario").GetString()!, StringComparison.Ordinal);
        Assert.Equal("CurrentPrAcBlocker", z4r.GetProperty("category").GetString());
        Assert.True(z4r.GetProperty("requires_durable_pr_comment").GetBoolean());
        Assert.True(z4r.GetProperty("requires_follow_up_issue").GetBoolean());
        Assert.False(z4r.GetProperty("must_not_be_pr_comment").GetBoolean());
        Assert.Equal("clarification-required", z4r.GetProperty("recommended_outcome").GetString());

        // A host-metadata example must never be an implementation-PR comment.
        Assert.Contains(examples, e =>
            e.GetProperty("category").GetString() == "HostMetadataBlocker"
            && e.GetProperty("must_not_be_pr_comment").GetBoolean()
            && !e.GetProperty("requires_durable_pr_comment").GetBoolean());
    }

    [Fact]
    public void Execute_G394_MarkdownIncludesBlockerSections()
    {
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G394", "review", title: "durable blocker comments", linkedPr: "889"));
        workspace.WriteFile(".intent-cli/issues/G394/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "889"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Review blocker protocol", output, StringComparison.Ordinal);
        Assert.Contains("chat_is_not_durable_workflow_state: yes", output, StringComparison.Ordinal);
        Assert.Contains("## PR blocker comment template", output, StringComparison.Ordinal);
        Assert.Contains("## Review blocker routing examples", output, StringComparison.Ordinal);
        Assert.Contains("Zero4Racer PR #406", output, StringComparison.Ordinal);
        Assert.Contains("outcome: clarification-required", output, StringComparison.Ordinal);
    }

    // --- G316 review-fix: PR-specific intent_reference_paths -----------------

    [Fact]
    public void Execute_G316_PacketReferencesIntentPaths_SurfacesOnlyThosePaths()
    {
        // Mirrors the G316 issue body's related-links: when packet
        // artifacts cite specific files like
        // `intents/intent-cli/intent-tree/means/07-review-worker-strategy.md`
        // and `intents/intent-cli/specs/05-intent-cli-surface.md`, those
        // exact paths are surfaced (in canonical-file order, deduped),
        // and broad directory pointers are NOT emitted.
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml",
            "execution_unit: G316\nrelated_intents:\n  - intents/intent-cli/intent-tree/means/07-review-worker-strategy.md\n");
        workspace.WriteFile(".intent-cli/issues/G316/review-context.md",
            "Read intents/intent-cli/specs/05-intent-cli-surface.md for the surface contract.\n"
            + "(also referenced again: intents/intent-cli/specs/05-intent-cli-surface.md)\n");
        // Disk fixtures so `exists` reflects truth.
        workspace.WriteFile("intents/intent-cli/intent-tree/means/07-review-worker-strategy.md", "review");
        workspace.WriteFile("intents/intent-cli/specs/05-intent-cli-surface.md", "surface");
        // A `rules/` directory exists on disk but the packet does NOT
        // reference any rules path → it must NOT appear.
        workspace.WriteFile("intents/intent-cli/rules/01-queue-governance.md", "rule");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var intentRefs = document.RootElement.GetProperty("intent_reference_paths")
            .EnumerateArray().ToArray();

        // Exactly the two referenced paths, deduped, packet.yaml first.
        Assert.Equal(2, intentRefs.Length);
        Assert.Equal(
            "intents/intent-cli/intent-tree/means/07-review-worker-strategy.md",
            intentRefs[0].GetProperty("relative_path").GetString());
        Assert.Equal("intent-tree", intentRefs[0].GetProperty("kind").GetString());
        Assert.True(intentRefs[0].GetProperty("exists").GetBoolean());

        Assert.Equal(
            "intents/intent-cli/specs/05-intent-cli-surface.md",
            intentRefs[1].GetProperty("relative_path").GetString());
        Assert.Equal("specs", intentRefs[1].GetProperty("kind").GetString());
        Assert.True(intentRefs[1].GetProperty("exists").GetBoolean());

        // Broad directory pointers are NOT emitted: nothing under
        // `kind: rules` despite rules/ existing on disk.
        Assert.DoesNotContain(intentRefs, e => e.GetProperty("kind").GetString() == "rules");
    }

    [Fact]
    public void Execute_G316_PacketSilent_IntentReferencePathsIsEmpty_NoBroadPointers()
    {
        // Packet exists but does not reference any `intents/...` path.
        // Even when `intents/<domain>/{specs,intent-tree,rules}` all
        // exist on disk, the field stays empty — broad-directory
        // prompting is the bug being fixed.
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml", "execution_unit: G316\n");
        workspace.WriteFile(".intent-cli/issues/G316/review-context.md", "Review only the diff.\n");
        workspace.WriteFile("intents/intent-cli/specs/00-map.md", "x");
        workspace.WriteFile("intents/intent-cli/intent-tree/00-map.md", "x");
        workspace.WriteFile("intents/intent-cli/rules/00-map.md", "x");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var intentRefs = document.RootElement.GetProperty("intent_reference_paths");
        Assert.Equal(0, intentRefs.GetArrayLength());
    }

    [Fact]
    public void Execute_G316_PacketReferencesMissingIntentPath_StillSurfacesItWithExistsFalse()
    {
        // When the packet cites a path the host hasn't materialized,
        // surface the path with exists=false rather than silently
        // dropping it — the reviewer needs to know the intent reference
        // is dangling.
        using var workspace = new GuideReviewWorkspace();
        workspace.WriteQueueState(BuildQueueState("G316", "review", title: "intent-aware review", linkedPr: "598"));
        workspace.WriteFile(".intent-cli/issues/G316/packet.yaml",
            "see intents/intent-cli/specs/99-not-yet-written.md");

        using var writer = new StringWriter();
        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "598", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var intentRefs = document.RootElement.GetProperty("intent_reference_paths")
            .EnumerateArray().ToArray();
        Assert.Single(intentRefs);
        Assert.Equal(
            "intents/intent-cli/specs/99-not-yet-written.md",
            intentRefs[0].GetProperty("relative_path").GetString());
        Assert.False(intentRefs[0].GetProperty("exists").GetBoolean());
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new GuideReviewWorkspace();
        using var writer = new StringWriter();

        var exitCode = GuideReviewCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide review", writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildQueueState(string executionUnit, string state, string title, string? linkedPr)
    {
        var linked = linkedPr is null ? "null" : $"\"{linkedPr}\"";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "{{title}}",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linked}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class GuideReviewWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("guide-review-tests-")
            .FullName;

        public GuideReviewWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteFile(string relativePath, string content)
        {
            var full = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
