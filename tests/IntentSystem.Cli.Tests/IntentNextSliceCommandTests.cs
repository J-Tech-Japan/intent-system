using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

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
    public void Execute_GivenNoCandidate_RecommendsNoActionableItem()
    {
        using var workspace = new IntentNextSliceWorkspace();

        using var writer = new StringWriter();
        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run"],
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
    public void Execute_GivenMarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new IntentNextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = IntentNextSliceCommand.Execute(
            workspace.Context,
            ["--dry-run", "--format", "markdown"],
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
            ["--dry-run", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // SKS packet was excluded, so no candidate and no-actionable-item.
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
            ["--dry-run", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // Packet excluded because target_repo doesn't match.
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
