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

            - (none)
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
    public void Execute_G359_NoBindingsFile_FallsBackToOpenFilter()
    {
        // Pre-G359 hosts (no bindings.md) must continue to behave
        // byte-identically: with no regex configured, every candidate
        // passes the name filter.
        using var workspace = new IntentNextSliceWorkspace();
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
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
        Assert.Equal(
            "G359",
            root.GetProperty("candidate").GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_G359_InvalidBindingsRegex_FallsBackToOpenFilter()
    {
        // Misconfigured bindings (invalid regex pattern) must not
        // silently block ALL candidates; the filter degrades open.
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
        Assert.Equal("issue-cut-ready", root.GetProperty("recommended_outcome").GetString());
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
            Assert.True(regex!.IsMatch("G42"));
            Assert.False(regex.IsMatch("SKS-G42"));
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
            Assert.True(regex!.IsMatch("SKS-G42"));
        }
        finally
        {
            if (Directory.Exists(childRoot)) Directory.Delete(childRoot, recursive: true);
        }
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
