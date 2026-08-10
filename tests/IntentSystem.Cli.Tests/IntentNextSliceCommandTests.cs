using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class IntentNextSliceCommandTests
{
    [Fact]
    public void Execute_GivenClarificationOpen_RecommendsClarificationRequired()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ## Current Open Blockers
            - Need decision on storage strategy
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        Assert.True(root.GetProperty("clarification_open").GetBoolean());
        Assert.True(root.GetProperty("dry_run").GetBoolean());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
    }

    [Fact]
    public void Execute_GivenActiveQueueItem_RecommendsSkipDueToWip()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G241",
                  "title": "intent status",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("skip-next-slice-due-to-wip", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(1, root.GetProperty("wip").GetArrayLength());
        Assert.Equal("G241", root.GetProperty("wip")[0].GetString());
    }

    [Fact]
    public void Execute_GivenNoCandidate_AndRuntimeCreationAllowed_RecommendsNoActionableItem()
    {
        // G328: the pre-G328 "no actionable item" semantic is now
        // gated on the operator passing `--runtime-creation-allowed`
        // (the review-runtime authorization that lets the runtime
        // create packets on its own). Hosts that aren't review
        // runtimes hit the new `design-needed` default — exercised
        // in Execute_GivenNoCandidate_AndRuntimeCreationDisabled_RecommendsDesignNeeded.
        using var workspace = new IntentNextSliceWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--runtime-creation-allowed"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("no-actionable-item", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out var _));
    }

    [Fact]
    public void Execute_GivenCandidateWithMissingSections_RecommendsClarificationAndListsMissing()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            """
            ## Goal
            Add something.

            ## In Scope
            Foo.
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "next slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("G244", candidate.GetProperty("execution_unit").GetString());
        Assert.True(candidate.GetProperty("github_body_present").GetBoolean());
        var missing = candidate.GetProperty("missing_contract_sections");
        Assert.True(missing.GetArrayLength() > 0);
        var missingNames = missing.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", missingNames);
        Assert.Contains("Acceptance Criteria", missingNames);
    }

    [Fact]
    public void Execute_GivenCompleteCandidate_RecommendsIssueCutReady()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "next slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("G244", candidate.GetProperty("execution_unit").GetString());
        Assert.Equal(0, candidate.GetProperty("missing_contract_sections").GetArrayLength());
    }

    [Fact]
    public void Execute_RetiredPacket_IsExcludedAndNextPacketSelected()
    {
        // G474: an absorbed packet directory left under .intent-cli/issues must
        // not be returned as issue-cut-ready; the next non-retired packet is
        // selected instead.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: absorbed\nabsorbed_by: G245\nretired_reason: \"fully absorbed into G245\"\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "absorbed slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G245",
                  "title": "next slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    // ─── G537: priority-aware candidate ordering ────────────────────────────

    [Fact]
    public void Execute_FieldScenario_LaterAuthoredHighPriorityUnit_IsSelectedOverEarlierAuthoredNormalUnit()
    {
        // G537 field incident: G530 (authored first, normal priority) and
        // G532 (authored second, but the orchestrator ruled it should
        // publish FIRST). Setting G532's queue priority to "high" must now
        // make the selector return G532 instead of the authoring-order
        // winner G530.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G530/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G532/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G530",
                  "title": "authored first",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G532",
                  "title": "field-impact fix, ruled to publish first",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G532", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    // ─── G543: legacy/out-of-enum priority values (e.g. "medium") ──────────

    [Fact]
    public void Execute_FieldScenario_MediumPriorityUnit_OutrankedByHighPriorityUnit_G543()
    {
        // G543 field observation, 2026-07-20: the host queue-state has 59
        // items at priority "medium" — a value outside the documented
        // high|normal|low enum. QueuePriorityClassification.Rank ranks any
        // out-of-enum value the same as "normal" (between high and low).
        // This proves "high" still outranks "medium" (authored later, but
        // priority-first ordering still wins).
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G610/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G611/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-20T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G610",
                  "title": "authored first, legacy medium priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "medium"
                },
                {
                  "execution_unit": "G611",
                  "title": "authored second, documented high priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("G611", document.RootElement.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_FieldScenario_MediumPriorityUnit_OutranksLowPriorityUnit_G543()
    {
        // The other half of the position proof: "medium" must still
        // outrank "low", exactly like "normal" would, even though it is
        // authored first (so authoring order alone would have picked it
        // anyway — the assertion below is really pinned by the companion
        // test above, which proves "medium" loses to "high").
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G612/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G613/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-20T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G612",
                  "title": "authored first, documented low priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "low"
                },
                {
                  "execution_unit": "G613",
                  "title": "authored second, legacy medium priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "medium"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("G613", document.RootElement.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_MediumAndNormalPriorityUnits_ShareExactlyTheSameRank_AuthoringOrderTiebreaks_G543()
    {
        // "medium" doesn't just fall SOMEWHERE between high and low — it
        // ranks IDENTICALLY to "normal", proven by the authoring-order
        // tiebreak going to whichever of the two was authored first,
        // regardless of which one is "medium" and which is "normal".
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G614/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G615/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-20T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G614",
                  "title": "authored first, legacy medium priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "medium"
                },
                {
                  "execution_unit": "G615",
                  "title": "authored second, documented normal priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        // "medium", authored FIRST, wins the tiebreak over "normal" —
        // proving the two ranked identically (a real ordering difference
        // between them would have picked "normal" regardless of order).
        Assert.Equal("G614", document.RootElement.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_HighPriorityUnitExcludedByLifecycleGate_NormalPriorityUnitStillSelected()
    {
        // G537: gate precedence over priority. A "high" priority unit that
        // fails a hard eligibility gate (here: G534's lifecycle exclusion —
        // absorbed via lifecycle.yaml) must NEVER be selected ahead of an
        // eligible lower-priority unit; priority only orders candidates
        // that already passed every gate.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G244/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: absorbed\nabsorbed_by: G245\nretired_reason: \"fully absorbed into G245\"\n");
        workspace.WriteFile(".intent-cli/issues/G245/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "absorbed slice, but marked high priority",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                },
                {
                  "execution_unit": "G245",
                  "title": "eligible normal-priority slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_OnlyQueuedUnitHasNonEmptyBlockedBy_FallbackNeverResurrectsIt_G544Repair()
    {
        // G544 review repair: when the primary `queued`-ordered loop finds
        // NO eligible candidate (here: the only queued unit has a
        // non-empty blocked_by), it falls back to re-enumerating every
        // packet directory under .intent-cli/issues/*. That fallback was
        // NOT re-applying the dependency/blocked-by gate the primary loop
        // just used to reject this exact unit -- silently resurrecting a
        // queue-known ineligible unit as issue-cut-ready. The gate must
        // apply identically in both loops, so a unit blocked_by-blocked in
        // queue-state is excluded no matter which loop would otherwise
        // have selected it.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-20T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G600",
                  "title": "blocked_by-blocked slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": ["waiting on operator decision"],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context, ["--dry-run", "--runtime-creation-allowed"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.NotEqual("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out _), "a blocked_by-blocked unit must never be resurrected as a candidate by the all-packet fallback.");
    }

    [Fact]
    public void Execute_HighPriorityUnitWithIncompleteDependency_IsNeverSelectedOverEligibleLowerPriorityUnit()
    {
        // G537 review repair: dependency completeness is an authoritative
        // eligibility gate — a "high" priority queued unit whose
        // dependency has NOT reached `completed` must never be selected
        // ahead of an eligible normal-priority unit with no unmet
        // dependencies. Priority only orders candidates that already pass
        // every gate, dependencies included.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G601/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G600",
                  "title": "high priority but depends on unfinished work",
                  "state": "queued",
                  "dependencies": ["G599"],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                },
                {
                  "execution_unit": "G601",
                  "title": "normal priority, no dependencies, fully eligible",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G601", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_HighPriorityUnitWithNonEmptyBlockedBy_IsNeverSelectedOverEligibleLowerPriorityUnit()
    {
        // G537 review repair: a non-empty `blocked_by` is likewise an
        // authoritative eligibility gate that dominates priority.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G610/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G611/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G610",
                  "title": "high priority but explicitly blocked",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": ["waiting on infra approval"],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                },
                {
                  "execution_unit": "G611",
                  "title": "normal priority, not blocked",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G611", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_HighPriorityUnitWithCompletedDependency_IsSelected()
    {
        // Counterpart: once the dependency reaches `completed`, the
        // high-priority unit becomes eligible and priority correctly wins.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G620/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G621/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G619",
                  "title": "the dependency, already completed",
                  "state": "completed",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G620",
                  "title": "high priority, dependency now complete",
                  "state": "queued",
                  "dependencies": ["G619"],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                },
                {
                  "execution_unit": "G621",
                  "title": "normal priority, authored second",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G620", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_NoPrioritiesSet_SelectionStaysByteIdenticalToAuthoringOrder()
    {
        // G537 required regression: with every item at the enqueue default
        // ("normal"), ordering must be unchanged from pre-G537 behavior —
        // plain authoring (queue-state array) order.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(".intent-cli/issues/G100/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G101/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G102/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G100",
                  "title": "first authored",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G101",
                  "title": "second authored",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G102",
                  "title": "third authored",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G100", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_LifecycleRetiredPacket_NoQueueEntryAtAll_ExcludedAndNextRealCandidateSelected()
    {
        // G534 field finding: the SKS-G812 case — a packet retired via
        // lifecycle.yaml with NO queue-state entry whatsoever (not even a
        // "retired" queue item) was returned as the next-slice candidate
        // instead of the next real one, because the selector's
        // fallback loop only had a packet directory to go on. This is
        // the "even when no queue entry exists" boundary the fix must
        // hold — G244 has no queue-state item at all here.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: retired\nretired_reason: \"retired before queue tracking existed\"\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": []
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_QueueStateRetiredWithNoLifecycleYaml_ExcludedAndNextRealCandidateSelected()
    {
        // G534 review repair: a unit already transitioned to Retired
        // purely in queue-state.json (e.g. backfilled via the new
        // `queue transition --to retired`) previously fell through the
        // state-bucketing switch with NO bucket at all — the fallback
        // (all-directories) loop's only queue-state-derived exclusion was
        // `completed.Contains(...)`, so a queue-Retired-but-no-
        // lifecycle.yaml unit was never excluded and could be
        // re-surfaced. No lifecycle.yaml exists for G244 here — only the
        // queue-state Retired entry is the signal.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "retired via queue transition",
                  "state": "retired",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_QueueRetiredAndLifecycleRetired_Agreement_ExcludedAndNextRealCandidateSelected()
    {
        // G534 review repair: "agreement" case — both signals say retired.
        // Ordinary exclusion, no lifecycle-metadata-diagnostic warning (this
        // is not a contradiction; nothing needs reconciling).
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: retired\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "retired via both signals",
                  "state": "retired",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.DoesNotContain("lifecycle-metadata-diagnostic", warnings);
    }

    [Fact]
    public void Execute_LifecycleActiveContradictsQueueRetired_ExcludedWithDiagnostic()
    {
        // G534 review repair: "active-vs-retired", direction 1 — an explicit
        // `lifecycle: ready` does NOT override a queue-state Retired record.
        // This is a genuine contradiction: still excluded, and surfaced as an
        // actionable diagnostic so it can be reconciled.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: ready\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "contradiction: queue retired, lifecycle ready",
                  "state": "retired",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Contains("lifecycle-metadata-diagnostic", warnings);
        var notes = root.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains(notes, note => note!.Contains("G244", StringComparison.Ordinal)
            && note.Contains("contradict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_LifecycleRetiredContradictsQueueNotRetired_ExcludedWithDiagnostic()
    {
        // G534 review repair (round 2): "active-vs-retired", direction 2 —
        // a packet lifecycle already marked retired excludes the unit even
        // though a PRESENT queue entry does not (yet) agree. Exclusion
        // itself pre-dates G534 (G474/G525) and must keep working
        // unchanged — but this contradiction must now ALSO be diagnosed
        // (previously silent), so a later, unrelated candidate can never
        // hide the inconsistent earlier unit.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: retired\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "lifecycle retired, queue not yet caught up",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Contains("lifecycle-metadata-diagnostic", warnings);
        var notes = root.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains(notes, note => note!.Contains("G244", StringComparison.Ordinal)
            && note.Contains("contradict", StringComparison.OrdinalIgnoreCase)
            && note.Contains("queued", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_LifecycleRetiredContradictsQueueActive_MarkdownFormat_IncludesDiagnostic()
    {
        // G534 review repair (round 2): the same reverse-direction
        // contradiction, verified in the `--format markdown` renderer too
        // — the Markdown output must not silently omit the diagnostic
        // note/warning that the JSON output carries.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: retired\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "lifecycle retired, queue active",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var markdown = writer.ToString();
        Assert.Contains("lifecycle-metadata-diagnostic", markdown, StringComparison.Ordinal);
        Assert.Contains("G244", markdown, StringComparison.Ordinal);
        Assert.Contains("contradict", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_LifecycleActiveContradictsQueueRetired_MarkdownFormat_IncludesDiagnostic()
    {
        // G534 review repair (round 2): direction 1's diagnostic, verified
        // in the `--format markdown` renderer too.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: ready\n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "contradiction: queue retired, lifecycle ready",
                  "state": "retired",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var markdown = writer.ToString();
        Assert.Contains("lifecycle-metadata-diagnostic", markdown, StringComparison.Ordinal);
        Assert.Contains("G244", markdown, StringComparison.Ordinal);
        Assert.Contains("contradict", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_UnknownLifecycleValue_MalformedSksG812NeverSelectedEvenAsOnlyCandidate()
    {
        // G534 review repair: the literal field-finding shape — an
        // unrecognized (e.g. typo'd) lifecycle value must fail closed and
        // never silently become publishable, even when it is the only
        // packet directory available (no other candidate to fall back to).
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G812/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G812/lifecycle.yaml",
            "lifecycle: retird\n");
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": []
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.NotEqual("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out _), "no candidate should be selected");
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Contains("lifecycle-metadata-diagnostic", warnings);
        var notes = root.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).ToArray();
        Assert.Contains(notes, note => note!.Contains("SKS-G812", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_BlankLifecycleValue_ExcludedWithDiagnostic()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G244/lifecycle.yaml",
            "lifecycle: \n");
        workspace.WriteFile(
            ".intent-cli/issues/G245/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": []
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G245", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
        Assert.Contains("lifecycle-metadata-diagnostic", warnings);
    }

    [Fact]
    public void Execute_EndToEnd_EnqueueThenRetiredBackfillThenSelection_RequiresNoManualQueueStateEdits()
    {
        // G534 end-to-end fixture: proves all three field-finding fixes work
        // together through the real command surface, with the only
        // queue-state.json write being the initial empty schema skeleton
        // every repo bootstraps with before any queue command has ever run
        // (not a hand-authored queue entry). Every subsequent queue-state
        // mutation goes through `queue enqueue` / `queue transition` only:
        //   1. `queue enqueue` on a 2-space-list-item packet (SKS-G824 shape,
        //      defect a) enqueues G900 as Queued.
        //   2. `queue transition --to retired` (defect b) backfills G900 to
        //      Retired without ever hand-editing queue-state.json.
        //   3. `queue enqueue` enqueues a second unit, G901, as Queued.
        //   4. `intent next-slice` (defect c, the publish selector) must
        //      skip the queue-Retired G900 and select G901 as the real next
        //      candidate.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-19T00:00:00Z",
              "items": []
            }
            """);

        workspace.WriteFile(
            ".intent-cli/issues/G900/packet.yaml",
            BuildTwoSpaceListPacketYaml("G900", "G900 Retirement Candidate"));
        workspace.WriteFile(
            ".intent-cli/issues/G900/github-body.md",
            BuildCompleteContractBody());

        using (var enqueueFirstWriter = new StringWriter())
        {
            var enqueueFirstExitCode = QueueEnqueueCommand.Execute(
                workspace.Context,
                ["G900"],
                enqueueFirstWriter);
            Assert.Equal(0, enqueueFirstExitCode);
            Assert.Contains(
                "Queue enqueue processed for execution unit 'G900'.",
                enqueueFirstWriter.ToString(),
                StringComparison.Ordinal);
        }

        using (var transitionWriter = new StringWriter())
        {
            var transitionExitCode = QueueTransitionCommand.Execute(
                workspace.Context,
                ["G900", "retired"],
                transitionWriter);
            Assert.Equal(0, transitionExitCode);
            Assert.Contains(
                "Transitioned G900 to retired",
                transitionWriter.ToString(),
                StringComparison.Ordinal);
        }

        workspace.WriteFile(
            ".intent-cli/issues/G901/packet.yaml",
            BuildTwoSpaceListPacketYaml("G901", "G901 Next Real Candidate"));
        workspace.WriteFile(
            ".intent-cli/issues/G901/github-body.md",
            BuildCompleteContractBody());

        using (var enqueueSecondWriter = new StringWriter())
        {
            var enqueueSecondExitCode = QueueEnqueueCommand.Execute(
                workspace.Context,
                ["G901"],
                enqueueSecondWriter);
            Assert.Equal(0, enqueueSecondExitCode);
            Assert.Contains(
                "Queue enqueue processed for execution unit 'G901'.",
                enqueueSecondWriter.ToString(),
                StringComparison.Ordinal);
        }

        using var nextSliceWriter = new StringWriter();
        var nextSliceExitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            nextSliceWriter);

        Assert.Equal(0, nextSliceExitCode);
        using var document = JsonDocument.Parse(nextSliceWriter.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G901", root.GetProperty("candidate").GetProperty("execution_unit").GetString());

        var finalQueueState = QueueStateSerializer.Deserialize(
            File.ReadAllText(workspace.Context.GetQueueStatePath()));
        Assert.Equal(
            QueueItemState.Retired,
            finalQueueState.Items.Single(item => item.ExecutionUnit == "G900").State);
        Assert.Equal(
            QueueItemState.Queued,
            finalQueueState.Items.Single(item => item.ExecutionUnit == "G901").State);
    }

    private static string BuildTwoSpaceListPacketYaml(string executionUnit, string title)
    {
        // G534: the documented (new-schema) packet format, with list items
        // indented at the SAME column as their parent key (the common,
        // previously-rejected convention) — quoted and unquoted scalars.
        return $"""
        implementation_issue_packet:
          issue_title: "{title}"
          issue_kind: "feature"
          source_execution_unit: "{executionUnit}"
          goal: "goal"
          in_scope:
          - "in scope item"
          out_of_scope:
          - out of scope item
          target_repo: "J-Tech-Japan/intent-system"
          target_path: "."
          target_part: "part"
          dependencies: []
          technical_baseline:
          - "C# / .NET"
          project_local_guide:
          - "AGENTS.md"
          intent_baseline:
          - "queue insertion stays thin"
          intent_references:
          - "ICL.P.PRODUCT_GOAL"
          rules_and_specs: []
          acceptance_criteria:
          - acceptance criterion
          verification_evidence:
          - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "{executionUnit}"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
          - "ICL.P.PRODUCT_GOAL"
          rules_and_specs: []
          acceptance_criteria:
          - acceptance criterion
          deterministic_review_checks: []
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    [Fact]
    public void Execute_LegacyHumanRetirementMarker_ExcludedWithRepairWarning()
    {
        // G474: a packet carrying only a stale human marker (STATUS: ABSORBED)
        // is not blindly published; it is excluded from selection and a repair
        // warning recommends converting it to machine-readable metadata.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            "STATUS: ABSORBED - Do NOT seed or publish.\n\n" + BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G244",
                  "title": "absorbed slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.NotEqual("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("legacy-retirement-marker-needs-machine-metadata", warnings);
        var notes = root.GetProperty("notes").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(notes, note => note.Contains("packet retire", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMarkdownFormat_EmitsHumanReadableOutput()
    {
        // G328: pass --runtime-creation-allowed so the recommendation
        // mirrors the pre-G328 markdown rendering for "empty workspace,
        // truly idle". Without the flag the new default outcome is
        // `design-needed`, which is asserted separately.
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--runtime-creation-allowed", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent next-slice dry-run — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("recommended outcome: no-actionable-item", output, StringComparison.Ordinal);
        Assert.Contains("## WIP (in-flight)", output, StringComparison.Ordinal);
        Assert.Contains("## Open clarifications", output, StringComparison.Ordinal);
        Assert.Contains("## Candidate", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingDryRun_ReturnsUsageError()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--dry-run is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'json' or 'markdown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent next-slice", output, StringComparison.Ordinal);
        Assert.Contains("--dry-run", output, StringComparison.Ordinal);
    }

    // --- G328: provenance + design-needed ----------------------------------

    [Fact]
    public void Execute_GivenNoCandidate_AndRuntimeCreationDisabled_RecommendsDesignNeeded()
    {
        // G328 acceptance: when no prepared packet exists and the
        // operator has NOT passed --runtime-creation-allowed, the
        // recommendation is `design-needed` (not `no-actionable-item`).
        // The host loop maps this onto the design-needed classification
        // so it never reports true-idle while a design-side packet
        // draft is the actual next move.
        using var workspace = new IntentNextSliceWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("design-needed", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.GetProperty("runtime_creation_allowed").GetBoolean());
        Assert.False(root.TryGetProperty("candidate", out _));
    }

    [Fact]
    public void Execute_GivenDesignProvenancePacket_CandidateExposesDesignProvenance()
    {
        // G328 acceptance: a packet authored on the design workspace
        // (MyIntentHost) records `created_by_role: design` in
        // packet.yaml. The next-slice candidate JSON surfaces it so
        // review-runtime publish consumers can audit provenance.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G328/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G328/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G328
              target_repo: J-Tech-Japan/intent-system
              clarification_return_path: intents/intent-cli/clarifications/open.md
            provenance:
              created_by_role: design
              created_by_host: MyIntentHost
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var provenance = root.GetProperty("candidate").GetProperty("provenance");
        Assert.Equal("design", provenance.GetProperty("created_by_role").GetString());
        Assert.Equal("MyIntentHost", provenance.GetProperty("created_by_host").GetString());
        Assert.Equal("packet.yaml", provenance.GetProperty("provenance_source").GetString());
    }

    [Fact]
    public void Execute_GivenReviewRuntimeProvenancePacket_CandidateExposesRuntimeProvenance()
    {
        // G328 acceptance: a packet drafted by a review-runtime
        // workspace after a closeout records the runtime role,
        // workspace identity, and source PR. The next-slice
        // candidate JSON surfaces all three.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G329/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G329/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G329
              target_repo: J-Tech-Japan/intent-system
              clarification_return_path: intents/intent-cli/clarifications/open.md
            provenance:
              created_by_role: review-runtime
              created_by_host: review-runtime-intent-system
              source_closeout_pr: 758
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var provenance = root.GetProperty("candidate").GetProperty("provenance");
        Assert.Equal("review-runtime", provenance.GetProperty("created_by_role").GetString());
        Assert.Equal("review-runtime-intent-system",
            provenance.GetProperty("created_by_host").GetString());
        Assert.Equal(758, provenance.GetProperty("source_closeout_pr").GetInt32());
        Assert.Equal("packet.yaml", provenance.GetProperty("provenance_source").GetString());
    }

    [Fact]
    public void Execute_GivenLegacyPacketWithoutProvenance_DefaultsToDesignProvenance()
    {
        // G328 backward-compatibility: pre-G328 packets do not record
        // provenance. They are treated as design-authored so they
        // continue to be publishable; the `provenance_source` field
        // is `default-design` so consumers know the binding is
        // implicit, not explicitly recorded.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G244/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var provenance = root.GetProperty("candidate").GetProperty("provenance");
        Assert.Equal("design", provenance.GetProperty("created_by_role").GetString());
        Assert.Equal("default-design",
            provenance.GetProperty("provenance_source").GetString());
        Assert.False(provenance.TryGetProperty("created_by_host", out _),
            "default-design provenance must not invent a host string.");
    }

    [Fact]
    public void Execute_PreparedDesignPacket_CanBePublishedByReviewRuntime()
    {
        // G328 acceptance: a prepared design-side packet must remain
        // publishable from a review-runtime workspace — the
        // `runtime-creation-allowed` flag controls whether the
        // runtime may CREATE packets, not whether it can publish
        // packets the design workspace already prepared. Verified
        // by asserting `issue-cut-ready` regardless of the flag,
        // with provenance still recorded as `design`.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G330/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G330/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G330
              target_repo: J-Tech-Japan/intent-system
              clarification_return_path: intents/intent-cli/clarifications/open.md
            provenance:
              created_by_role: design
              created_by_host: MyIntentHost
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            // Review-runtime caller does NOT pass --runtime-creation-allowed
            // because they aren't authoring; they're publishing the
            // prepared packet.
            ["--dry-run", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Issue-cut-ready wins because the prepared packet has the
        // contract; design-needed is only emitted when there is NO
        // candidate at all.
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("design",
            root.GetProperty("candidate")
                .GetProperty("provenance")
                .GetProperty("created_by_role").GetString());
    }

    // --- G332: runtime-scoped state preference for WIP gate ----------------

    [Fact]
    public void Execute_G332_TargetRepoWithScopedQueueState_ReadsScopedAndReportsScopedLayout()
    {
        // G332 acceptance: when `--target-repo` is supplied AND the
        // scoped queue-state exists for (domain, target-repo), the
        // WIP gate reads the scoped file rather than the legacy root.
        // Result records `state_layout: scoped`.
        using var workspace = new IntentNextSliceWorkspace();
        // Legacy root has an UNRELATED active item — would falsely
        // trigger WIP if the gate read root.
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": [
                {
                  "execution_unit": "UNRELATED-1",
                  "title": "unrelated",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/SomeOther", "number": 1},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        // Scoped state is empty (no WIP) — so the gate should NOT
        // see WIP and the candidate should publish.
        workspace.WriteFile(
            ".intent-cli/runtime/intent-cli/J-Tech-Japan__intent-system/queue-state.json",
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": []
            }
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G332/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G332/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G332
              target_repo: J-Tech-Japan/intent-system
              clarification_return_path: intents/intent-cli/clarifications/open.md
            """);

        using var writer = new StringWriter();
        var exit = IntentNextSliceCommand.Execute(
            workspace.Context,
            new[]
            {
                "--dry-run",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system"
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("scoped", root.GetProperty("state_layout").GetString());
        // No WIP visible via the scope → candidate is publishable.
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G332_TargetRepoWithLegacyOnly_FallsBackToLegacyAndReportsFallback()
    {
        // G332 transition: when no scoped state exists, the gate falls
        // back to legacy root and surfaces `state_layout: legacy-fallback`.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": []
            }
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G332/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G332/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G332
              target_repo: J-Tech-Japan/intent-system
              clarification_return_path: intents/intent-cli/clarifications/open.md
            """);

        using var writer = new StringWriter();
        var exit = IntentNextSliceCommand.Execute(
            workspace.Context,
            new[]
            {
                "--dry-run",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system"
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("legacy-fallback", root.GetProperty("state_layout").GetString());
    }

    [Fact]
    public void Execute_G332_WithoutTargetRepo_PreservesPreG332RootRead_NoStateLayoutField()
    {
        // G332 invariant: callers that don't pass --target-repo
        // continue with byte-identical pre-G332 root behavior. The
        // result does NOT carry a `state_layout` field (null →
        // omitted by WhenWritingNull).
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": []
            }
            """);

        using var writer = new StringWriter();
        var exit = IntentNextSliceCommand.Execute(
            workspace.Context,
            new[] { "--dry-run", "--runtime-creation-allowed" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.TryGetProperty("state_layout", out _),
            "without --target-repo the result must omit state_layout (pre-G332 behavior).");
    }

    [Fact]
    public void Execute_G332_ScopedActiveItem_BlocksWipEvenIfLegacyRootEmpty()
    {
        // G332 isolation: when scoped state has an Active item, the WIP
        // gate must fire even if the legacy root file is empty.
        // Inverse of the previous test — confirms reads come from
        // scoped state, not legacy.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": []
            }
            """);
        workspace.WriteFile(
            ".intent-cli/runtime/intent-cli/J-Tech-Japan__intent-system/queue-state.json",
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-12T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SCOPED-WIP",
                  "title": "scoped WIP",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": 700},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exit = IntentNextSliceCommand.Execute(
            workspace.Context,
            new[]
            {
                "--dry-run",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system"
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("scoped", root.GetProperty("state_layout").GetString());
        // WIP detected via scoped state.
        Assert.Equal("skip-next-slice-due-to-wip",
            root.GetProperty("recommended_outcome").GetString());
    }

    private static string BuildCompleteContractBody()
    {
        return """
            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification
            x

            ## Related Links
            - x

            ## Base Branch Policy

            Expected PR base branch: `main`
            """;
    }

    // ─── G275 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G275_QueuedPacketNoLinkedIssue_ReturnsIssueCutReady()
    {
        // Queued packet with complete github-body.md, no linked issue → issue-cut-ready
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G275/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G275",
                  "title": "next slice domain filter",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("G275", candidate.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_G275_CrossDomainPacketExcluded_WhenDomainFilterActive()
    {
        // SKS-* packet has clarification_return_path pointing to sks domain.
        // When domain=intent-cli is requested, it must be excluded.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G183/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G183",
                  "title": "sks packet",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sks/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--runtime-creation-allowed"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // G328: with runtime creation allowed, the no-candidate result
        // recommends `no-actionable-item` rather than the new default
        // `design-needed`. This test exercises the cross-domain
        // exclusion regardless of the G328 default flip.
        Assert.Equal("no-actionable-item", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out _));
    }

    [Fact]
    public void Execute_G275_ClarificationFileWithIntentStateClarified_DoesNotBlock()
    {
        // A clarification file with `intent_state: clarified` in front-matter
        // must not block next-slice, even if there are bullet items.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ---
            intent_state: clarified
            ---
            ## Current Open Blockers

            - Some old note that should no longer block
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("clarification_open").GetBoolean(),
            "clarification_open must be false when intent_state is clarified");
        // With no WIP and no candidate, falls to no-actionable-item (not clarification-required).
        Assert.NotEqual("clarification-required", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G275_ClarificationSectionWithNone_DoesNotBlock()
    {
        // A clarification file where "## Current Open Blockers" contains only "None"
        // must not block next-slice.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            None
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("clarification_open").GetBoolean(),
            "clarification_open must be false when 'None' is the only content");
        Assert.NotEqual("clarification-required", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G275_WipPresent_ReturnsSkipDueToWip()
    {
        // When there is a WIP item, skip-next-slice-due-to-wip takes precedence.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G275/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G275",
                  "title": "wip item",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {
                    "repo": "J-Tech-Japan/intent-system",
                    "number": 100,
                    "url": "https://github.com/J-Tech-Japan/intent-system/issues/100"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("skip-next-slice-due-to-wip", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G275_TargetRepoFilter_ExcludesPacketWithDifferentRepo()
    {
        // A packet with target_repo != requested targetRepo must be excluded.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G275/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G275/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G275
              target_repo: J-Tech-Japan/other-repo
              issue_title: something
              issue_kind: feature
              goal: x
              in_scope: []
              out_of_scope: []
              target_path: .
              target_part: x
              dependencies: []
              technical_baseline: []
              project_local_guide: []
              intent_baseline: []
              intent_references: []
              rules_and_specs: []
              acceptance_criteria: []
              verification_evidence: []
              review_mode: full
              completion_action: close
              landing_policy: merge
            review_context_packet:
              source_execution_unit: G275
              parent_intent_root: .
              intent_references: []
              rules_and_specs: []
              acceptance_criteria: []
              deterministic_review_checks: []
              clarification_return_path: intents/intent-cli/clarifications/open.md
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G275",
                  "title": "wrong repo packet",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": ".intent-cli/issues/G275/packet.yaml"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system",
                "--runtime-creation-allowed"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // G328: with runtime creation allowed, the no-candidate result
        // remains `no-actionable-item`. This test exercises target-repo
        // filtering regardless of the G328 default flip.
        Assert.Equal("no-actionable-item", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G275_CrossDomainActiveItem_DoesNotBlockIntentCliIssueCutReady()
    {
        // G275 WIP filter regression: an SKS item in Active state must not block
        // an intent-cli G-series issue-cut-ready candidate when --domain intent-cli
        // is specified.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G280/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G183",
                  "title": "sks wip item",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sks/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {
                    "repo": "J-Tech-Japan/SekibanAsAService",
                    "number": 50,
                    "url": "https://github.com/J-Tech-Japan/SekibanAsAService/issues/50"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G280",
                  "title": "intent-cli queued slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // SKS active item is filtered out of WIP; G280 is the intent-cli candidate.
        Assert.NotEqual("skip-next-slice-due-to-wip", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(0, root.GetProperty("wip").GetArrayLength());
    }

    [Fact]
    public void Execute_G275_CrossDomainReviewItem_DoesNotBlockIntentCliIssueCutReady()
    {
        // G275 WIP filter regression: an SKS item in Review state must not block
        // an intent-cli G-series issue-cut-ready candidate when --domain intent-cli
        // is specified.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G280/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G183",
                  "title": "sks wip item in review",
                  "state": "review",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sks/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {
                    "repo": "J-Tech-Japan/SekibanAsAService",
                    "number": 99,
                    "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/99"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G280",
                  "title": "intent-cli queued slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // SKS review item is filtered out of WIP; not skip-next-slice-due-to-wip.
        Assert.NotEqual("skip-next-slice-due-to-wip", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(0, root.GetProperty("wip").GetArrayLength());
    }

    // ─── G285 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G285_StaleOpenFrontMatterWithBodyNone_RecommendsIssueCutReadyAndWarns()
    {
        // Front-matter still says `intent_state: open` but the body's
        // "Current Open Blockers" section is empty / None. With a complete
        // candidate and empty WIP, this must publish, not Hard Clarification,
        // and surface `stale-clarification-metadata` in `warnings`.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ---
            intent_state: open
            ---
            ## Current Open Blockers

            None

            ## Open Questions

            - None
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G285/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-07T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G285",
                  "title": "stale clarification metadata",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.GetProperty("clarification_open").GetBoolean());
        Assert.True(root.GetProperty("stale_clarification_metadata").GetBoolean());

        var warnings = root.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("stale-clarification-metadata", warnings);
    }

    [Fact]
    public void Execute_G285_StaleOpenFrontMatterButRealOpenQuestion_RecommendsClarificationRequired()
    {
        // Front-matter says `intent_state: open` and the body's "Open Questions"
        // section has a substantive bullet. This is a true Hard Clarification —
        // must NOT surface stale-clarification-metadata, must NOT publish.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ---
            intent_state: open
            ---
            ## Current Open Blockers

            - None

            ## Open Questions

            - Need to confirm storage strategy with the host before cutting.
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G285/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-07T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G285",
                  "title": "real open question",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        Assert.True(root.GetProperty("clarification_open").GetBoolean());
        Assert.False(root.GetProperty("stale_clarification_metadata").GetBoolean());
        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
    }

    [Fact]
    public void Execute_G285_BulletNoneSentinel_DoesNotBlock()
    {
        // A "Current Open Blockers" section whose only bullet is "- None"
        // (the form actually used in some host files) must be treated as
        // cleared, not as a substantive blocker.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            - None
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("clarification_open").GetBoolean(),
            "clarification_open must be false when '- None' is the only bullet");
        Assert.NotEqual("clarification-required", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G285_StaleOpenButCandidateMissingSections_RecommendsClarificationRequired()
    {
        // Candidate has missing required contract sections — that path must
        // still surface clarification-required regardless of stale metadata.
        // The stale warning may still appear so the operator sees the file
        // also needs a metadata repair, but the outcome is not issue-cut-ready.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ---
            intent_state: open
            ---
            ## Current Open Blockers

            None
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G285/github-body.md",
            """
            ## Goal
            x

            ## In Scope
            x
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-07T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G285",
                  "title": "missing contract sections",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        var missing = root.GetProperty("candidate").GetProperty("missing_contract_sections");
        Assert.True(missing.GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_G285_NormalOpenWithoutBodyNoneSignal_DoesNotEmitStaleWarning()
    {
        // Front-matter says `intent_state: open` but the body has no
        // "Current Open Blockers" or "Open Questions" sections at all. We
        // must not synthesise a stale-clarification-metadata warning out of
        // thin air — the body has no explicit no-blocker signal.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ---
            intent_state: open
            ---
            # Some clarification doc

            Random prose.
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("stale_clarification_metadata").GetBoolean());
        Assert.Equal(0, root.GetProperty("warnings").GetArrayLength());
    }

    // ─── G354: packet contract gap classification ──────────────────────────────

    [Fact]
    public void Execute_G354_OnlyMechanicalSectionsMissing_RecommendsPacketGapMechanicalRepairable()
    {
        // G348-like regression: a packet missing only Verification and
        // Related Links must return packet-gap-mechanical-repairable (not
        // clarification-required). The host agent can append the sections
        // from the supplied repair_guidance without operator input.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G348/github-body.md",
            """
            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Base Branch Policy

            Expected PR base branch: `main`
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-13T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G348",
                  "title": "G348 test",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("packet-gap-mechanical-repairable", root.GetProperty("recommended_outcome").GetString());

        var candidate = root.GetProperty("candidate");
        Assert.Equal("G348", candidate.GetProperty("execution_unit").GetString());

        // missing_contract_sections must still list the gaps
        var missing = candidate.GetProperty("missing_contract_sections")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", missing);
        Assert.Contains("Related Links", missing);

        // gap_analysis must be present
        var gapAnalysis = candidate.GetProperty("gap_analysis");
        Assert.Equal("mechanical-repairable", gapAnalysis.GetProperty("overall_classification").GetString());
        Assert.True(gapAnalysis.GetProperty("has_mechanical_gaps").GetBoolean());
        Assert.False(gapAnalysis.GetProperty("has_product_gaps").GetBoolean());

        // repair_guidance must have entries for each mechanical gap
        var repairGuidance = gapAnalysis.GetProperty("repair_guidance");
        Assert.Equal(2, repairGuidance.GetArrayLength());

        // gaps array must classify each section
        var gaps = gapAnalysis.GetProperty("gaps").EnumerateArray().ToArray();
        Assert.Contains(gaps, g => g.GetProperty("section").GetString() == "Verification"
            && g.GetProperty("classification").GetString() == "mechanical-repairable");
        Assert.Contains(gaps, g => g.GetProperty("section").GetString() == "Related Links"
            && g.GetProperty("classification").GetString() == "mechanical-repairable");
    }

    [Fact]
    public void Execute_G354_ApplyingMechanicalRepairMakesPacketIssueCutReady()
    {
        // After appending Verification and Related Links, re-running
        // intent next-slice --dry-run must return issue-cut-ready.
        using var workspace = new IntentNextSliceWorkspace();
        var githubBodyRelPath = ".intent-cli/issues/G348b/github-body.md";
        workspace.WriteFile(
            githubBodyRelPath,
            """
            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Base Branch Policy

            Expected PR base branch: `main`
            """);

        // First pass: confirm mechanical-repairable
        using var firstWriter = new StringWriter();
        IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], firstWriter);
        using var firstDoc = JsonDocument.Parse(firstWriter.ToString());
        Assert.Equal("packet-gap-mechanical-repairable",
            firstDoc.RootElement.GetProperty("recommended_outcome").GetString());

        // Apply the repair: add Verification and Related Links
        workspace.WriteFile(
            githubBodyRelPath,
            """
            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification

            - Run `dotnet test` and confirm all tests pass.

            ## Related Links

            - G354 verification evidence

            ## Base Branch Policy

            Expected PR base branch: `main`
            """);

        // Second pass: must return issue-cut-ready
        using var secondWriter = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            secondWriter);

        Assert.Equal(0, exitCode);
        using var secondDoc = JsonDocument.Parse(secondWriter.ToString());
        Assert.Equal("issue-cut-ready",
            secondDoc.RootElement.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G354_ProductSectionMissing_StillReturnsClarificationRequired()
    {
        // A packet missing Goal (product section) must still return
        // clarification-required, not mechanical-repairable.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G354a/github-body.md",
            """
            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification

            - Run `dotnet test`.

            ## Related Links

            - G354 product-gap evidence
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());

        // gap_analysis must classify Goal as product-clarification-required
        var gapAnalysis = root.GetProperty("candidate").GetProperty("gap_analysis");
        Assert.Equal("product-clarification-required", gapAnalysis.GetProperty("overall_classification").GetString());
        Assert.False(gapAnalysis.GetProperty("has_mechanical_gaps").GetBoolean());
        Assert.True(gapAnalysis.GetProperty("has_product_gaps").GetBoolean());
        var goalGap = gapAnalysis.GetProperty("gaps").EnumerateArray()
            .First(g => g.GetProperty("section").GetString() == "Goal");
        Assert.Equal("product-clarification-required", goalGap.GetProperty("classification").GetString());
        Assert.False(goalGap.TryGetProperty("repair_guidance", out _));
    }

    [Fact]
    public void Execute_G354_MixedGaps_ReturnsClarificationRequired()
    {
        // A packet missing both a product section (Goal) AND a
        // mechanical section (Verification) must return
        // clarification-required (not packet-gap-mechanical-repairable)
        // because the product gap still requires operator input.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G354b/github-body.md",
            """
            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Related Links

            - (none)
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Product gap (Goal) dominates — must remain clarification-required
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());

        var gapAnalysis = root.GetProperty("candidate").GetProperty("gap_analysis");
        Assert.Equal("mixed", gapAnalysis.GetProperty("overall_classification").GetString());
        Assert.True(gapAnalysis.GetProperty("has_mechanical_gaps").GetBoolean());
        Assert.True(gapAnalysis.GetProperty("has_product_gaps").GetBoolean());
    }

    [Fact]
    public void Execute_G354_CompletePacket_HasNoGapAnalysis()
    {
        // A complete packet must NOT expose a gap_analysis field
        // (null → omitted by JsonSerializer).
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G354c/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var candidate = document.RootElement.GetProperty("candidate");
        Assert.Equal("issue-cut-ready", document.RootElement.GetProperty("recommended_outcome").GetString());
        // gap_analysis must be absent (null serialized as omitted)
        Assert.False(candidate.TryGetProperty("gap_analysis", out _));
    }

    // ─── G354 pure unit tests for PacketContractGapAnalyzer ───────────────────

    [Fact]
    public void PacketContractGapAnalyzer_EmptyMissing_ReturnsNone()
    {
        var result = PacketContractGapAnalyzer.Analyze(Array.Empty<string>());

        Assert.Equal(PacketContractGapAnalyzer.ClassificationNone, result.OverallClassification);
        Assert.False(result.HasMechanicalGaps);
        Assert.False(result.HasProductGaps);
        Assert.Empty(result.Gaps);
        Assert.Empty(result.RepairGuidance);
    }

    [Fact]
    public void PacketContractGapAnalyzer_VerificationMissing_IsMechanicalRepairable()
    {
        var result = PacketContractGapAnalyzer.Analyze(new[] { "Verification" });

        Assert.Equal(PacketContractGapAnalyzer.ClassificationMechanicalRepairable, result.OverallClassification);
        Assert.True(result.HasMechanicalGaps);
        Assert.False(result.HasProductGaps);
        Assert.Single(result.Gaps);
        Assert.Equal(PacketContractGapAnalyzer.ClassificationMechanicalRepairable, result.Gaps[0].Classification);
        Assert.NotNull(result.Gaps[0].RepairGuidance);
        Assert.Contains("## Verification", result.Gaps[0].RepairGuidance!, StringComparison.Ordinal);
        Assert.Single(result.RepairGuidance);
    }

    [Fact]
    public void PacketContractGapAnalyzer_RelatedLinksMissing_IsMechanicalRepairable()
    {
        var result = PacketContractGapAnalyzer.Analyze(new[] { "Related Links" });

        Assert.Equal(PacketContractGapAnalyzer.ClassificationMechanicalRepairable, result.OverallClassification);
        Assert.True(result.HasMechanicalGaps);
        Assert.NotNull(result.Gaps[0].RepairGuidance);
        Assert.Contains("## Related Links", result.Gaps[0].RepairGuidance!, StringComparison.Ordinal);
    }

    [Fact]
    public void PacketContractGapAnalyzer_GoalMissing_IsProductClarificationRequired()
    {
        var result = PacketContractGapAnalyzer.Analyze(new[] { "Goal" });

        Assert.Equal(PacketContractGapAnalyzer.ClassificationProductClarificationRequired, result.OverallClassification);
        Assert.False(result.HasMechanicalGaps);
        Assert.True(result.HasProductGaps);
        Assert.Null(result.Gaps[0].RepairGuidance);
        Assert.Empty(result.RepairGuidance);
    }

    [Fact]
    public void PacketContractGapAnalyzer_MixedGaps_ClassifiesMixed()
    {
        var result = PacketContractGapAnalyzer.Analyze(new[] { "Goal", "Verification", "Related Links" });

        Assert.Equal(PacketContractGapAnalyzer.ClassificationMixed, result.OverallClassification);
        Assert.True(result.HasMechanicalGaps);
        Assert.True(result.HasProductGaps);
        // 2 repair guidance entries (Verification + Related Links)
        Assert.Equal(2, result.RepairGuidance.Count);
        Assert.Equal(3, result.Gaps.Count);
    }

    [Fact]
    public void PacketContractGapAnalyzer_WithPacketDirectory_EmbedsFull_PathInGuidance()
    {
        var dir = Path.Combine(Path.GetTempPath(), "G354-test-" + Guid.NewGuid().ToString("N")[..8]);
        var result = PacketContractGapAnalyzer.Analyze(new[] { "Verification" }, packetDirectory: dir);

        var expectedPath = Path.Combine(dir, "github-body.md");
        Assert.Contains(expectedPath, result.Gaps[0].RepairGuidance!, StringComparison.Ordinal);
    }

    // ─── G359 tests ───────────────────────────────────────────────────────────
    // execution_unit_regex from intents/<domain>/automation/bindings.md must
    // filter packet candidates from a shared `.intent-cli/issues` root so a
    // wrong-namespace packet (e.g. SKS-G365) cannot be selected when
    // --domain intent-cli is requested.

    [Fact]
    public void Execute_G359_SharedPacketRoot_DomainBindingsRegex_SelectsMatchingNamespace()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G359/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G365/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("G359", candidate.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_G359_WipPass_AppliesExecutionUnitRegex_IgnoresMisnamedQueueItem()
    {
        // PR #822 review repair: the WIP pass must also enforce
        // `execution_unit_regex` so a misnamed SKS-G… queue item in
        // Active/Review/Fixing state cannot block `--domain intent-cli`
        // with `skip-next-slice-due-to-wip`. Before the fix, the WIP
        // filter used clarification_return_path alone, so a queue item
        // whose path pointed at intent-cli but whose execution_unit
        // was SKS-G… would slip into WIP under the requested lane.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G280/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-16T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G99",
                  "title": "misnamed wip item pointing at intent-cli clarifications",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": {
                    "repo": "J-Tech-Japan/intent-system",
                    "number": 999,
                    "url": "https://github.com/J-Tech-Japan/intent-system/issues/999"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G280",
                  "title": "intent-cli queued slice",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // SKS-G99 misnamed item fails the intent-cli regex, so WIP is
        // empty and G280 is selected — NOT skip-next-slice-due-to-wip.
        Assert.NotEqual("skip-next-slice-due-to-wip", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(0, root.GetProperty("wip").GetArrayLength());
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal("G280", root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_G359_OnlySksPacketAvailable_IntentCliDomain_RecommendsDesignNeeded()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G365/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // No matching candidate AND runtime creation NOT allowed → design-needed (G328).
        Assert.Equal("design-needed", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out _));
    }

    [Fact]
    public void Execute_G359_SekibanDomain_BindingsRegex_SelectsSksPacket()
    {
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteAutomationBindings(
            "sekiban-as-a-service",
            """
            ---
            execution_unit_regex: '^SKS-G[0-9]+$'
            ---
            """);
        // Clarification dir for sekiban domain to silence the missing-file note;
        // not strictly required for the assertion but mirrors realistic state.
        workspace.WriteClarificationOpen("", "sekiban-as-a-service");
        workspace.WriteFile(
            ".intent-cli/issues/G359/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G365/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "sekiban-as-a-service", "--target-repo", "J-Tech-Japan/SekibanAsAService"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        var candidate = root.GetProperty("candidate");
        Assert.Equal("SKS-G365", candidate.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_G359_MismatchedTargetRepoWithinDomain_DoesNotReachIssueCutReady()
    {
        // Packet matches the domain bindings regex but its packet.yaml
        // declares a different target_repo — must NOT reach issue-cut-ready.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G359/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G359/packet.yaml",
            """
            implementation_issue_packet:
              source_execution_unit: G359
              target_repo: J-Tech-Japan/other-repo
              issue_title: G359 wrong repo
              issue_kind: feature
              goal: x
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.NotEqual("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        // With runtime creation NOT allowed, no candidate maps to design-needed.
        Assert.Equal("design-needed", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G439_NoBindingsFile_CompletePacket_ReturnsIssueCutReadyWithWarning()
    {
        // G439: when `--domain` is supplied and bindings.md is MISSING
        // (MissingOrAbsent) but the queued packet is complete, next-slice
        // MUST surface the candidate as issue-cut-ready. Only an explicitly
        // broken regex pattern (InvalidPattern) causes fail-closed behavior.
        // A missing-domain-bindings note is still emitted as a warning so
        // the operator knows to add execution_unit_regex to bindings.md.
        using var workspace = new IntentNextSliceWorkspace();
        // Delete the default permissive bindings the workspace
        // constructor seeded so we exercise the genuine "no
        // bindings.md" path.
        workspace.DeleteAutomationBindings("intent-cli");
        workspace.WriteFile(
            ".intent-cli/issues/G359/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // G439: a complete packet MUST surface as issue-cut-ready even
        // without a bindings.md — diagnostics would agree.
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.True(root.TryGetProperty("candidate", out var candidateEl));
        Assert.Equal(
            "G359",
            candidateEl.GetProperty("execution_unit").GetString());
        // The missing-domain-bindings warning note must still be present.
        var notes = root.GetProperty("notes");
        Assert.Contains(
            notes.EnumerateArray().Select(n => n.GetString() ?? string.Empty),
            n => n.Contains("missing-domain-bindings", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G359_InvalidBindingsRegex_FailsClosedWithDomainNote()
    {
        // PR #824 review repair #3: misconfigured bindings (invalid
        // regex pattern) MUST fail closed rather than silently
        // skipping the cross-domain check. A malformed bindings.md is
        // an operator-fixable error, not a license to publish.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '[unterminated'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G359/github-body.md",
            BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.NotEqual("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out _));
        var notes = root.GetProperty("notes");
        Assert.Contains(
            notes.EnumerateArray().Select(n => n.GetString() ?? string.Empty),
            n => n.Contains("invalid-domain-bindings", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G439_Zero4RacerStyle_QueuedCompletePacket_NoBindings_ReturnsIssueCutReady()
    {
        // Regression for G439: Zero4Racer-style case where Z4R-G286 is
        // queued, packet is complete, but bindings.md is absent from the
        // zero4racer-mobile-revival domain. Previously next-slice returned
        // design-needed; it must now return issue-cut-ready.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.DeleteAutomationBindings("intent-cli");
        // Use a Z4R-prefixed packet name to simulate the Zero4Racer namespace
        workspace.WriteFile(
            ".intent-cli/issues/Z4R-G286/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-29T00:00:00Z",
              "items": [
                {
                  "execution_unit": "Z4R-G286",
                  "title": "zero4racer queued complete packet",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": null,
                  "linked_issue": null,
                  "linked_pr": null
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.True(root.TryGetProperty("candidate", out var candidateEl));
        Assert.Equal(
            "Z4R-G286",
            candidateEl.GetProperty("execution_unit").GetString());
        // Warning note must still be present so operator can add bindings.md
        var notes = root.GetProperty("notes");
        Assert.Contains(
            notes.EnumerateArray().Select(n => n.GetString() ?? string.Empty),
            n => n.Contains("missing-domain-bindings", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_G359_MalformedPacketYaml_FailsClosed_NotIssueCutReady()
    {
        // PR #824 review repair #3: a malformed packet.yaml (e.g.
        // tab indentation, missing colon) MUST fail closed in the
        // next-slice selection lane. The legacy line-scanner silently
        // ignored broken lines so a packet could reach issue-cut-ready
        // with a corrupt body — the strict parser now routes that
        // case through the unsafe lane.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/G359/github-body.md",
            BuildCompleteContractBody());
        // Malformed: tab character in indentation is a YAML syntax error
        // (1.2 §6.1). The strict parser raises FormatException and the
        // command marks the packet ineligible.
        workspace.WriteFile(
            ".intent-cli/issues/G359/packet.yaml",
            "implementation_issue_packet:\n\tsource_execution_unit: G359\n\ttarget_repo: J-Tech-Japan/intent-system\n");

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Malformed packet.yaml is filtered out → no candidate → not
        // issue-cut-ready (runtime-creation-not-allowed → design-needed).
        Assert.NotEqual("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
    }

    [Fact]
    public void Execute_G359_QueuedSksPacket_NotSelectedUnderIntentCliDomain()
    {
        // Queued path (the preferred selection branch) must also honor
        // the bindings regex — SKS-G365 queued but intent-cli requested
        // should fall through to design-needed.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteAutomationBindings(
            "intent-cli",
            """
            ---
            execution_unit_regex: '^G[0-9]+$'
            ---
            """);
        workspace.WriteFile(
            ".intent-cli/issues/SKS-G365/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-15T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G365",
                  "title": "sks queued packet",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("design-needed", root.GetProperty("recommended_outcome").GetString());
        Assert.False(root.TryGetProperty("candidate", out _));
    }

    // ─── G359 pure unit tests for NextSliceDomainBindingsExecutionUnitRegex ───

    [Fact]
    public void NextSliceDomainBindingsExecutionUnitRegex_ExtractsFromFrontmatter()
    {
        var content = """
            ---
            repo: J-Tech-Japan/intent-system
            execution_unit_regex: '^G[0-9]+$'
            ---
            """;

        var pattern = NextSliceDomainBindingsExecutionUnitRegex.ExtractExecutionUnitRegex(content);

        Assert.Equal("^G[0-9]+$", pattern);
    }

    [Fact]
    public void NextSliceDomainBindingsExecutionUnitRegex_ExtractsDoubleQuotedValue()
    {
        var content = """
            execution_unit_regex: "^SKS-G[0-9]+$"
            """;

        var pattern = NextSliceDomainBindingsExecutionUnitRegex.ExtractExecutionUnitRegex(content);

        Assert.Equal("^SKS-G[0-9]+$", pattern);
    }

    [Fact]
    public void NextSliceDomainBindingsExecutionUnitRegex_AbsentField_ReturnsNull()
    {
        var content = """
            ---
            repo: J-Tech-Japan/intent-system
            ---
            """;

        var pattern = NextSliceDomainBindingsExecutionUnitRegex.ExtractExecutionUnitRegex(content);

        Assert.Null(pattern);
    }

    [Fact]
    public void NextSliceDomainBindingsExecutionUnitRegex_EmptyContent_ReturnsNull()
    {
        Assert.Null(NextSliceDomainBindingsExecutionUnitRegex.ExtractExecutionUnitRegex(string.Empty));
    }

    [Fact]
    public void NextSliceDomainBindingsExecutionUnitRegex_ParentRootAuthoritative_OverridesChildBindings()
    {
        // PR #822 review fix: when a parent intent repo root is
        // configured, the PARENT bindings.md is the authoritative
        // source of `execution_unit_regex` — a stale or partial child
        // workspace bindings.md must NOT override it. This locks the
        // parent-aware lookup contract used by the other
        // parent-aware analyzers (AutomationSummaryAnalyzer /
        // NextSliceClassifyAnalyzer).
        var parentRoot = Directory.CreateTempSubdirectory("g359-parent-root-").FullName;
        var childRoot = Directory.CreateTempSubdirectory("g359-child-root-").FullName;
        try
        {
            // Parent has the authoritative regex.
            var parentBindings = Path.Combine(parentRoot, "intents", "intent-cli", "automation");
            Directory.CreateDirectory(parentBindings);
            File.WriteAllText(Path.Combine(parentBindings, "bindings.md"),
                "---\nexecution_unit_regex: '^G[0-9]+$'\n---\n");

            // Child has a STALE / DIFFERENT regex that must NOT win.
            var childBindings = Path.Combine(childRoot, "intents", "intent-cli", "automation");
            Directory.CreateDirectory(childBindings);
            File.WriteAllText(Path.Combine(childBindings, "bindings.md"),
                "---\nexecution_unit_regex: '^SKS-G[0-9]+$'\n---\n");

            var context = new CliContext
            {
                RepoRoot = childRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                        ParentIntentRepoRoot = parentRoot,
                    },
                },
            };

            var regex = NextSliceDomainBindingsExecutionUnitRegex.TryLoad(context, "intent-cli");

            Assert.NotNull(regex);
            // Parent regex matches `G42`; child's `^SKS-G[0-9]+$` would NOT.
            Assert.Matches(regex!, "G42");
            Assert.DoesNotMatch(regex, "SKS-G42");
        }
        finally
        {
            if (Directory.Exists(parentRoot)) Directory.Delete(parentRoot, recursive: true);
            if (Directory.Exists(childRoot)) Directory.Delete(childRoot, recursive: true);
        }
    }

    [Fact]
    public void NextSliceDomainBindingsExecutionUnitRegex_NoParentRoot_FallsBackToChildBindings()
    {
        // PR #822 review fix: without a parent root configured, the
        // child workspace bindings.md remains the source of truth so
        // host-colocated layouts (and the in-memory test fixtures)
        // keep working.
        var childRoot = Directory.CreateTempSubdirectory("g359-child-only-").FullName;
        try
        {
            var childBindings = Path.Combine(childRoot, "intents", "intent-cli", "automation");
            Directory.CreateDirectory(childBindings);
            File.WriteAllText(Path.Combine(childBindings, "bindings.md"),
                "---\nexecution_unit_regex: '^SKS-G[0-9]+$'\n---\n");

            var context = new CliContext
            {
                RepoRoot = childRoot,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                        // No ParentIntentRepoRoot configured.
                    },
                },
            };

            var regex = NextSliceDomainBindingsExecutionUnitRegex.TryLoad(context, "intent-cli");

            Assert.NotNull(regex);
            Assert.Matches(regex!, "SKS-G42");
        }
        finally
        {
            if (Directory.Exists(childRoot)) Directory.Delete(childRoot, recursive: true);
        }
    }

    // ─── G433 regression tests ────────────────────────────────────────────────

    [Fact]
    public void Execute_G433_MissingBaseBranchPolicy_ReturnsClarificationRequired()
    {
        // G433: next-slice must use the same required section list as
        // publish-flow. A packet that is otherwise complete but missing
        // "Base Branch Policy" should return clarification-required, not
        // issue-cut-ready, because publish-flow would reject it.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G433/github-body.md",
            """
            ## Goal
            x

            ## Why This Slice Exists Now
            x

            ## Current Observed State
            x

            ## Accepted Baseline You May Assume
            x

            ## Target Repo / Path / Part
            x

            ## In Scope
            x

            ## Out Of Scope
            x

            ## Acceptance Criteria
            x

            ## Verification
            x

            ## Related Links
            - x
            """);
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G433",
                  "title": "contract validation consistency",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Must not return issue-cut-ready — publish-flow would reject this packet
        Assert.Equal("clarification-required", root.GetProperty("recommended_outcome").GetString());
        var missing = root.GetProperty("candidate").GetProperty("missing_contract_sections");
        var missingNames = missing.EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Base Branch Policy", missingNames);
    }

    [Fact]
    public void Execute_G433_CompleteBodyWithBaseBranchPolicy_ReturnsIssueCutReady()
    {
        // G433 positive case: a packet with all required sections including
        // "Base Branch Policy" must still return issue-cut-ready.
        using var workspace = new IntentNextSliceWorkspace();
        workspace.WriteFile(
            ".intent-cli/issues/G433b/github-body.md",
            BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-05-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G433b",
                  "title": "contract validation consistency positive",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(0, root.GetProperty("candidate").GetProperty("missing_contract_sections").GetArrayLength());
    }

    [Fact]
    public void Execute_TodoScaffoldUsesPublishGateAndIsVisiblyNotReady_G661()
    {
        using var workspace = new IntentNextSliceWorkspace();
        Assert.Equal(0, PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G661", "--target-repo", "J-Tech-Japan/intent-system"],
            TextWriter.Null));
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-08-10T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G661",
                  "title": "TODO short title",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        Assert.Equal(0, IntentNextSliceCommand.Execute(workspace.Context, ["--dry-run"], writer));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.NotEqual("issue-cut-ready", result.RootElement.GetProperty("recommended_outcome").GetString());
        var candidate = result.RootElement.GetProperty("candidate");
        Assert.False(candidate.GetProperty("publish_gate_ready").GetBoolean());
        Assert.Contains("Related Links", candidate.GetProperty("not_ready_reason").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Related Links", candidate.GetProperty("missing_contract_sections").EnumerateArray().Select(item => item.GetString()));
    }

    private sealed class IntentNextSliceWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("intent-next-slice-tests-")
            .FullName;

        public IntentNextSliceWorkspace()
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

            // PR #824 review repair #3: with `--domain` supplied,
            // missing bindings is now an unsafe stop. Seed a permissive
            // default bindings.md so legacy tests that don't write one
            // explicitly continue to behave like the pre-PR-#824 fail-
            // open path. Tests that need a specific regex still call
            // WriteAutomationBindings to overwrite this default.
            WriteAutomationBindings(
                "intent-cli",
                """
                ---
                execution_unit_regex: '.*'
                ---
                """);
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteClarificationOpen(string content, string domain = "intent-cli")
        {
            var path = Path.Combine(rootPath, "intents", domain, "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void DeleteAutomationBindings(string domain)
        {
            var path = Path.Combine(rootPath, "intents", domain, "automation", "bindings.md");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void WriteAutomationBindings(string domain, string content)
        {
            var path = Path.Combine(rootPath, "intents", domain, "automation");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "bindings.md"), content);
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
