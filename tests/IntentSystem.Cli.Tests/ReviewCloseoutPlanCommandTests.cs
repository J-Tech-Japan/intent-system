using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ReviewCloseoutPlanCommandTests : IDisposable
{
    public ReviewCloseoutPlanCommandTests()
    {
        // G329 review fix: default the closing-issues fetcher to an
        // empty result so unit tests never shell out to live `gh`. Tests
        // that exercise the auto-fetch lane override this with a fake
        // returning the issue numbers they want to reconstruct.
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory =
            () => new FakePrClosingIssuesFetcher(Array.Empty<int>());
    }

    public void Dispose()
    {
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory = null;
    }

    private sealed class FakePrClosingIssuesFetcher : IPrClosingIssuesFetcher
    {
        private readonly IReadOnlyList<int> closingIssues;
        public FakePrClosingIssuesFetcher(IReadOnlyList<int> closingIssues)
        {
            this.closingIssues = closingIssues;
        }
        public IReadOnlyList<int> Fetch(string repo, int prNumber) => closingIssues;
    }

    [Fact]
    public void Execute_GivenCompletePacketAndQueueMatch_ReportsReadyTrue()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, "https://github.com/J-Tech-Japan/intent-system/issues/595")));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G247/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("G247", root.GetProperty("execution_unit").GetString());
        Assert.Equal("review", root.GetProperty("queue_item_state").GetString());
        Assert.Equal("submodules/intent-system", root.GetProperty("expected_submodule_path").GetString());
        Assert.Equal(0, root.GetProperty("missing_contract_sections").GetArrayLength());
        Assert.Equal(0, root.GetProperty("gaps").GetArrayLength());
        var linked = root.GetProperty("linked_issue");
        Assert.Equal("J-Tech-Japan/intent-system", linked.GetProperty("repo").GetString());
        Assert.Equal(595, linked.GetProperty("number").GetInt32());
        Assert.True(root.GetProperty("packet_files").GetArrayLength() >= 2);
        Assert.True(root.GetProperty("validation_steps").GetArrayLength() >= 2);
        Assert.True(root.GetProperty("closeout_steps").GetArrayLength() >= 2);
    }

    [Fact]
    public void Execute_GivenMissingContractSections_ReportsGapAndExitsNonZero()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", "## Goal\nx\n");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("ready").GetBoolean());
        Assert.True(root.GetProperty("gaps").GetArrayLength() > 0);
        var missingNames = root.GetProperty("missing_contract_sections").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("Verification", missingNames);
    }

    [Fact]
    public void Execute_GivenNoLinkedIssue_ReportsLinkedIssueGap()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: null));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("linked_issue", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMissingPacketDirectory_ReportsPacketGap()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("packet directory not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenNoMatchingLinkedPr_ReportsQueueGap()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "999", linkedIssue: null));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var gaps = document.RootElement.GetProperty("gaps").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(gaps, gap => gap!.Contains("no queue item found with linked_pr", StringComparison.Ordinal));
    }

    // ─── G287 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G287_NoMatchingLinkedPr_ClassifiesHostMetadataBlocked()
    {
        // PR #670-shaped: no queue item has linked_pr matching the selected PR.
        // This is host metadata drift, not an implementation defect — must NOT
        // become a PR repair comment / request-update.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "999", linkedIssue: null));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "670", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("ready").GetBoolean());
        Assert.Equal("host-metadata-blocked", root.GetProperty("blocker_classification").GetString());
        Assert.Contains("automation reconcile", root.GetProperty("recommended_recovery_command").GetString()!, StringComparison.Ordinal);
        var classifiedGap = root.GetProperty("classified_gaps")[0];
        Assert.Equal("host-metadata", classifiedGap.GetProperty("classification").GetString());
        Assert.Contains("linked_pr", classifiedGap.GetProperty("description").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G313_NoMatchingLinkedPrAndPublishArtifactExists_RecommendsPublishRecoveryFirst()
    {
        // G313: when the missing-linked_pr blocker is on a host that has at
        // least one `.intent-cli/issues/<unit>/publish.yaml`, the recovery
        // command must point at `automation publish-recovery` rather than
        // generic reconcile (publish-recovery's evidence is stronger).
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "999", linkedIssue: null));
        // Publish artifact exists for some execution unit — content does not
        // matter to closeout-plan; existence is the trigger.
        workspace.WriteFile(".intent-cli/issues/G247/publish.yaml", "execution_unit: G247\n");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "670", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-metadata-blocked", root.GetProperty("blocker_classification").GetString());
        var recoveryCommand = root.GetProperty("recommended_recovery_command").GetString()!;
        Assert.Contains("automation publish-recovery", recoveryCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("automation reconcile", recoveryCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G313_NoMatchingLinkedPrAndNoPublishArtifact_RecommendsReconcile()
    {
        // G313: when no publish artifact exists, generic reconcile remains the
        // primary recommendation — publish-recovery has no evidence to work
        // with on a host without artifacts.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "999", linkedIssue: null));
        // No publish.yaml on disk.

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "670", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var recoveryCommand = document.RootElement.GetProperty("recommended_recovery_command").GetString()!;
        Assert.Contains("automation reconcile", recoveryCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G287_MissingContractSections_ClassifiesImplementationReviewFinding()
    {
        // Real implementation finding: contract sections missing on the
        // packet body. The implementer can fix this by amending the PR head /
        // packet content. This is the path that may legitimately become a PR
        // repair comment / request-update.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", "## Goal\nx\n\n## In Scope\nx\n");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("ready").GetBoolean());
        Assert.Equal("implementation-review-finding", root.GetProperty("blocker_classification").GetString());
        // No host-side recovery command for implementation findings — the
        // serializer drops null fields (WhenWritingNull) so the property is
        // absent rather than null-valued.
        Assert.False(
            root.TryGetProperty("recommended_recovery_command", out _),
            "implementation-review-finding must not surface a host-recovery command");
        var classifiedGap = root.GetProperty("classified_gaps")[0];
        Assert.Equal("implementation-review", classifiedGap.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_G287_HostMetadataDominatesImplementationReview_WhenBothPresent()
    {
        // Host metadata drift dominates: even if the packet body also has
        // missing contract sections, the host loop must run reconcile first
        // (and not post a PR comment) because the implementer cannot repair
        // parent host metadata from the PR branch.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        // queue item exists but linked_pr points elsewhere → host-metadata gap
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "999", linkedIssue: null));
        // packet directory exists with incomplete body — would be impl finding
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", "## Goal\nx\n");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "670", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-metadata-blocked", root.GetProperty("blocker_classification").GetString());
    }

    [Fact]
    public void Execute_G287_Ready_BlockerClassificationIsReady()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, "https://github.com/J-Tech-Japan/intent-system/issues/595")));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("ready", root.GetProperty("blocker_classification").GetString());
        Assert.False(root.TryGetProperty("recommended_recovery_command", out _));
    }

    [Fact]
    public void Execute_GivenSamePrNumberInDifferentRepo_SkipsOtherRepo()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
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
                  "linked_issue": {"repo": "J-Tech-Japan/intent-system", "number": 489, "url": "https://github.com/J-Tech-Japan/intent-system/issues/489"},
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
                  "linked_issue": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 489, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/issues/489"},
                  "linked_pr": {"repo": "J-Tech-Japan/SekibanAsAService", "number": 490, "url": "https://github.com/J-Tech-Japan/SekibanAsAService/pull/490"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        workspace.WriteFile(".intent-cli/issues/SKS-G185/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/SKS-G185/packet.yaml", "x");

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/SekibanAsAService", "--pr", "490", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("SKS-G185", document.RootElement.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_MissingPr_ReturnsUsageError()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--pr is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingRepo_ReturnsUsageError()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--pr", "596"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'json' or 'markdown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormat_EmitsHumanReadableOutput()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G247", "review", linkedPr: "596", linkedIssue: ("J-Tech-Japan/intent-system", 595, null)));
        workspace.WriteFile(".intent-cli/issues/G247/github-body.md", BuildCompleteContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system", "--pr", "596"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Review closeout plan — J-Tech-Japan/intent-system#596", output, StringComparison.Ordinal);
        Assert.Contains("expected submodule path: submodules/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("ready: yes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();

        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("review closeout-plan", writer.ToString(), StringComparison.Ordinal);
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
            - x

            ## Out Of Scope
            - x

            ## Acceptance Criteria
            - x

            ## Verification
            x

            ## Related Links
            - x
            """;
    }

    // ─── G329 tests: GitHub linkage reconstruction ──────────────────────────

    [Fact]
    public void Execute_G329_ClosingIssueMatchesSingleQueueItem_RecoversLinkageAndIsReady()
    {
        // G329 acceptance: PR with `Closes #issue` and a matching packet
        // is review-ready even when runtime state lacks `linked_pr`.
        // The closing issue points at exactly one queue item via
        // `linked_issue.number`; the reconstructor recovers the linkage
        // deterministically and the closeout plan reports ready=true
        // with a structured `recovered_linkage` payload.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 759,
                "https://github.com/J-Tech-Japan/intent-system/issues/759")));
        workspace.WriteFile(".intent-cli/issues/G329/github-body.md", BuildContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--closing-issues", "759",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("ready", root.GetProperty("blocker_classification").GetString());
        Assert.Equal("G329", root.GetProperty("execution_unit").GetString());

        var recovered = root.GetProperty("recovered_linkage");
        Assert.Equal("G329", recovered.GetProperty("execution_unit").GetString());
        Assert.Equal(760, recovered.GetProperty("linked_pr_number").GetInt32());
        Assert.Equal("J-Tech-Japan/intent-system", recovered.GetProperty("linked_pr_repo").GetString());
        Assert.Equal(759, recovered.GetProperty("linked_issue_number").GetInt32());
        Assert.Equal("github-closing-reference",
            recovered.GetProperty("recovery_source").GetString());
    }

    [Fact]
    public void Execute_G329_ClosingIssueMatchesMultipleQueueItems_SurfacesAmbiguity()
    {
        // G329 acceptance: ambiguous closing references produce
        // structured unsafe metadata, not guessing. When two queue
        // items both link to the same closing issue number, the
        // planner emits a `linkage-ambiguous` gap and an
        // `ambiguous_linkage` payload listing the candidates — and
        // the aggregate still classifies as host-metadata-blocked so
        // the host loop refuses to mutate.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithTwoItems(
            ("G329", "review", null,
                ("J-Tech-Japan/intent-system", 759,
                    "https://github.com/J-Tech-Japan/intent-system/issues/759")),
            ("G329-PRIME", "review", null,
                ("J-Tech-Japan/intent-system", 759,
                    "https://github.com/J-Tech-Japan/intent-system/issues/759"))));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--closing-issues", "759",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("ready").GetBoolean());
        Assert.Equal("host-metadata-blocked",
            root.GetProperty("blocker_classification").GetString());

        // Structured ambiguity payload listing both candidates.
        var ambiguous = root.GetProperty("ambiguous_linkage");
        var candidates = ambiguous.GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("execution_unit").GetString())
            .ToArray();
        Assert.Contains("G329", candidates);
        Assert.Contains("G329-PRIME", candidates);
        Assert.False(root.TryGetProperty("recovered_linkage", out _),
            "ambiguous recovery must NOT emit a deterministic recovered_linkage payload.");

        var classified = root.GetProperty("classified_gaps").EnumerateArray()
            .Select(g => g.GetProperty("classification").GetString())
            .ToArray();
        Assert.Contains("linkage-ambiguous", classified);
    }

    [Fact]
    public void Execute_G329_NoClosingIssuesSupplied_FallsThroughToHostMetadataBlocked()
    {
        // G329 out-of-scope: do NOT guess without a closing issue.
        // When `--closing-issues` is not passed, the planner keeps the
        // pre-G329 host-metadata-blocked classification (so the host
        // loop still routes to reconcile / publish-recovery).
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 759, null)));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-metadata-blocked",
            root.GetProperty("blocker_classification").GetString());
        Assert.False(root.TryGetProperty("recovered_linkage", out _));
        Assert.False(root.TryGetProperty("ambiguous_linkage", out _));
    }

    [Fact]
    public void Execute_G329_ClosingIssueWithNoQueueMatch_FallsThroughToHostMetadataBlocked()
    {
        // G329: when the closing issue is supplied but no queue item
        // links to it, the planner falls through to the existing
        // host-metadata-blocked recovery path (publish-recovery /
        // reconcile) — the closing-issue facts are not enough to
        // synthesize a queue entry.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("OTHER", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 1, null)));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--closing-issues", "759",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-metadata-blocked",
            root.GetProperty("blocker_classification").GetString());
        Assert.False(root.TryGetProperty("recovered_linkage", out _));
    }

    [Fact]
    public void Execute_G329_QueueStateAlreadyHasLinkedPr_PrefersDirectMatchNoRecovery()
    {
        // G329 invariant: when queue-state already records the linked_pr,
        // the reconstructor is never invoked — direct match wins and
        // recovered_linkage stays null. Confirms the recovery path is a
        // FALLBACK, not a primary lookup.
        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: "760",
            linkedIssue: ("J-Tech-Japan/intent-system", 759, null)));
        workspace.WriteFile(".intent-cli/issues/G329/github-body.md", BuildContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--closing-issues", "759",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.False(root.TryGetProperty("recovered_linkage", out _),
            "recovered_linkage must be null when queue-state already has the link.");
    }

    [Fact]
    public void Execute_G329_AutoFetchesClosingIssuesFromGitHub_WhenFlagOmitted()
    {
        // G329 review fix: the host review path must not have to pipe
        // `--closing-issues` manually. When the operator omits the flag,
        // closeout-plan auto-fetches closing issues via the
        // PrClosingIssuesFetcher seam (production = gh pr view).
        // Deterministic recovery still applies; result records
        // `closing_issues_source: github-auto-fetch`.
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory =
            () => new FakePrClosingIssuesFetcher(new[] { 759 });

        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 759,
                "https://github.com/J-Tech-Japan/intent-system/issues/759")));
        workspace.WriteFile(".intent-cli/issues/G329/github-body.md", BuildContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "760", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("github-auto-fetch",
            root.GetProperty("closing_issues_source").GetString());
        Assert.Equal("G329",
            root.GetProperty("recovered_linkage")
                .GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_G329_AutoFetchEmpty_FallsThroughToHostMetadataBlocked()
    {
        // When auto-fetch returns no closing issues (PR body has none,
        // or gh CLI failed), the planner falls through to existing
        // host-metadata-blocked behavior — no guessing.
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory =
            () => new FakePrClosingIssuesFetcher(Array.Empty<int>());

        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 759, null)));

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[] { "--repo", "J-Tech-Japan/intent-system", "--pr", "760", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-metadata-blocked",
            root.GetProperty("blocker_classification").GetString());
        Assert.False(root.TryGetProperty("closing_issues_source", out _),
            "closing_issues_source must be null when no closing issues were available.");
    }

    [Fact]
    public void Execute_G329_WriteRecoveredLinkage_PersistsToQueueStateAndRunsLog()
    {
        // G329 review fix: with `--write-recovered-linkage` the
        // deterministic recovery is committed to queue-state (linked_pr
        // set on the matched item) and appended to runs.jsonl
        // (`linkage-recovered` event). Host-owned write — the review
        // closeout-plan command is invoked by the host loop, not child
        // workers.
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory =
            () => new FakePrClosingIssuesFetcher(new[] { 759 });

        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 759,
                "https://github.com/J-Tech-Japan/intent-system/issues/759")));
        workspace.WriteFile(".intent-cli/issues/G329/github-body.md", BuildContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--write-recovered-linkage",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("linkage_recovery_applied").GetBoolean());

        // queue-state.json now has linked_pr set on the matched item.
        // (Indented serializer emits `"linked_pr": "https://..."` with a
        // space; parse the JSON instead of substring-matching.)
        var queueAfter = File.ReadAllText(workspace.Context.GetQueueStatePath());
        using var queueDoc = JsonDocument.Parse(queueAfter);
        var matchedAfter = queueDoc.RootElement.GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("execution_unit").GetString() == "G329");
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/760",
            matchedAfter.GetProperty("linked_pr").GetString());

        // runs.jsonl was appended with a `linkage-recovered` event.
        var runsLines = File.ReadAllLines(
            Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runs.jsonl"));
        Assert.Single(runsLines);
        Assert.Contains("\"event\":\"linkage-recovered\"", runsLines[0], StringComparison.Ordinal);
        Assert.Contains("\"execution_unit\":\"G329\"", runsLines[0], StringComparison.Ordinal);
        Assert.Contains("\"pr\":760", runsLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G329_WriteRecoveredLinkage_NeverPersistsAmbiguousMatches()
    {
        // G329 invariant: ambiguous matches MUST NOT be persisted even
        // with --write-recovered-linkage. The structured ambiguity
        // payload is the operator-facing disambiguation contract; the
        // host loop must not "pick the first candidate".
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory =
            () => new FakePrClosingIssuesFetcher(new[] { 759 });

        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueStateWithTwoItems(
            ("G329", "review", null,
                ("J-Tech-Japan/intent-system", 759, null)),
            ("G329-PRIME", "review", null,
                ("J-Tech-Japan/intent-system", 759, null))));
        var queueBefore = File.ReadAllText(workspace.Context.GetQueueStatePath());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--write-recovered-linkage",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.GetProperty("linkage_recovery_applied").GetBoolean());
        Assert.True(root.TryGetProperty("ambiguous_linkage", out _));
        // Queue-state must be byte-identical — no item picked.
        Assert.Equal(queueBefore, File.ReadAllText(workspace.Context.GetQueueStatePath()));
        // runs.jsonl was never created.
        Assert.False(File.Exists(
            Path.Combine(workspace.Context.RepoRoot, ".intent-cli", "runs.jsonl")));
    }

    [Fact]
    public void Execute_G329_ClosingIssuesFlagWins_RecordsOperatorFlagSource()
    {
        // When the operator passes --closing-issues, that wins over the
        // auto-fetch path and the result records `operator-flag` as the
        // source.
        ReviewCloseoutPlanCommand.PrClosingIssuesFetcherFactory =
            () => new FakePrClosingIssuesFetcher(new[] { 999 }); // would mismatch

        using var workspace = new ReviewCloseoutPlanWorkspace();
        workspace.WriteQueueState(BuildQueueState("G329", "review",
            linkedPr: null,
            linkedIssue: ("J-Tech-Japan/intent-system", 759, null)));
        workspace.WriteFile(".intent-cli/issues/G329/github-body.md", BuildContractBody());

        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--closing-issues", "759",
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("ready").GetBoolean());
        Assert.Equal("operator-flag",
            root.GetProperty("closing_issues_source").GetString());
    }

    [Fact]
    public void Execute_G329_ClosingIssuesFlagRejectsNonInteger()
    {
        using var workspace = new ReviewCloseoutPlanWorkspace();
        using var writer = new StringWriter();
        var exitCode = ReviewCloseoutPlanCommand.Execute(
            workspace.Context,
            new[]
            {
                "--repo", "J-Tech-Japan/intent-system",
                "--pr", "760",
                "--closing-issues", "759,not-a-number",
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--closing-issues entry must be a positive integer",
            writer.ToString(), StringComparison.Ordinal);
    }

    private static string BuildContractBody()
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

    private static string BuildQueueStateWithTwoItems(
        (string ExecutionUnit, string State, string? LinkedPr, (string Repo, int Number, string? Url) LinkedIssue) a,
        (string ExecutionUnit, string State, string? LinkedPr, (string Repo, int Number, string? Url) LinkedIssue) b)
    {
        static string Item((string ExecutionUnit, string State, string? LinkedPr, (string Repo, int Number, string? Url) LinkedIssue) i)
        {
            var linkedPrToken = i.LinkedPr is null ? "null" : $"\"{i.LinkedPr}\"";
            var url = i.LinkedIssue.Url is null ? "null" : $"\"{i.LinkedIssue.Url}\"";
            return $$"""
                {
                  "execution_unit": "{{i.ExecutionUnit}}",
                  "title": "title",
                  "state": "{{i.State}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linkedPrToken}},
                  "linked_issue": {
                    "repo": "{{i.LinkedIssue.Repo}}",
                    "number": {{i.LinkedIssue.Number}},
                    "url": {{url}}
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
                """;
        }
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {{Item(a)}},
                {{Item(b)}}
              ]
            }
            """;
    }

    private static string BuildQueueState(string executionUnit, string state, string? linkedPr, (string Repo, int Number, string? Url)? linkedIssue)
    {
        var linkedPrToken = linkedPr is null ? "null" : $"\"{linkedPr}\"";
        var linkedIssueBlock = linkedIssue is null
            ? ""
            : $@",
                  ""linked_issue"": {{
                    ""repo"": ""{linkedIssue.Value.Repo}"",
                    ""number"": {linkedIssue.Value.Number},
                    ""url"": {(linkedIssue.Value.Url is null ? "null" : $"\"{linkedIssue.Value.Url}\"")}
                  }}";
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "title",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_pr": {{linkedPrToken}}{{linkedIssueBlock}},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private sealed class ReviewCloseoutPlanWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("review-closeout-plan-tests-")
            .FullName;

        public ReviewCloseoutPlanWorkspace()
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
