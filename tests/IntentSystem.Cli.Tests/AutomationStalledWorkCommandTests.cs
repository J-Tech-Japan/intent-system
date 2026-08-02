using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class AutomationStalledWorkCommandTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);

    public AutomationStalledWorkCommandTests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_EmptyPipeline_ReturnsStalledFalseAndNoItems()
    {
        using var workspace = new StalledWorkWorkspace();
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
    }

    [Fact]
    public void Execute_PublishedNotDelegated_FiresWhenPacketConfirmsRequestedDomain()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G523", "intent-cli");
        var issue = BuildIssue(1147, "G523: Add automation stalled-work surface", FixedNow.AddHours(-26),
            "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stalled").GetBoolean());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedNotDelegated, item.GetProperty("kind").GetString());
        Assert.Equal("G523", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1147, item.GetProperty("issue").GetProperty("number").GetInt32());
        Assert.Equal(1560, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("worker claim", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("--number 1147", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
    }

    [Fact]
    public void Execute_PublishedNotDelegated_ExcludesIssueAlreadyClaimed()
    {
        // G533: kept RECENT (well under the claimed-but-silent default
        // 720-minute threshold) so this fixture stays narrowly scoped to
        // its original purpose — published-not-delegated's own exclusion
        // logic for an already-claimed issue — without incidentally also
        // tripping the new claimed-but-silent kind (that scenario is
        // covered by its own dedicated fixtures below).
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G524", "intent-cli");
        var claimedIssue = BuildIssue(1148, "G524: Something else", FixedNow.AddMinutes(-30),
            "intent-target", "intent-issue-in-progress");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [claimedIssue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_PublishedNotDelegated_ExcludesIssueWithLabelDriftButAlreadyClosedByOpenPr()
    {
        // PR #1148 review repair (finding 2): the completion label can drift
        // out of sync with reality. This issue has NEITHER
        // intent-issue-in-progress NOR intent-pr-created, but an OPEN PR in
        // the same repo already closes it — it must NOT be recommended for
        // `worker claim` (it is already implemented).
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G527", "intent-cli");
        var driftedIssue = BuildIssue(1160, "G527: Label drifted out of sync", FixedNow.AddHours(-26),
            "intent-target");
        var closingPr = BuildPr(1161, "G527: Label drifted out of sync", FixedNow.AddHours(-20),
            state: "OPEN", closingIssueNumber: 1160);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(
            issues: [driftedIssue], prs: [closingPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        // Only the pr-created-not-reviewing scan could still find this PR,
        // but it requires intent-pr-created on the issue (absent here), so
        // no item of ANY kind should reference G527's issue as
        // published-not-delegated.
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.DoesNotContain(items, item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindPublishedNotDelegated
            && item.GetProperty("execution_unit").GetString() == "G527");
    }

    [Fact]
    public void Execute_PrCreatedNotReviewing_FiresWhenIssueCarriesPrCreatedAndPrLacksReviewStart()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1144, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-1.5),
            state: "OPEN", closingIssueNumber: 1143);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindPrCreatedNotReviewing, item.GetProperty("kind").GetString());
        Assert.Equal("G521", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1143, item.GetProperty("issue").GetProperty("number").GetInt32());
        Assert.Equal(1144, item.GetProperty("pr").GetProperty("number").GetInt32());
        Assert.Equal(90, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("--transition review-start", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_CiPendingAndTerminalGreen_ForSamePr_ProduceDistinctDedupeReadyEvidence()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G589", "intent-cli");
        var issue = BuildIssue(1281, "G589: CI wait must be survivable without a timer",
            FixedNow.AddDays(-2), "intent-pr-created");
        const string headSha = "589abc123def4567890abc123def4567890abc12";
        var pendingPr = BuildPr(
            1282,
            issue.Title,
            FixedNow.AddMinutes(-90),
            state: "OPEN",
            closingIssueNumber: issue.Number,
            headRefOid: headSha,
            statusCheckRollup:
            [
                CheckRun("COMPLETED", "SUCCESS"),
                CheckRun("IN_PROGRESS"),
            ]);

        var pending = RunJson(workspace, issue, pendingPr);
        Assert.False(pending.RootElement.GetProperty("stalled").GetBoolean());
        var pendingItem = Assert.Single(pending.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindCiPending, pendingItem.GetProperty("kind").GetString());
        Assert.True(pendingItem.GetProperty("is_informational").GetBoolean());
        Assert.Equal(headSha, pendingItem.GetProperty("pr_head_sha").GetString());
        Assert.Equal("pending", pendingItem.GetProperty("ci_outcome").GetString());
        AssertCiBreakdown(pendingItem, passed: 1, failed: 0, skipped: 0, pending: 1, total: 2);
        Assert.Equal($"ci-pending:pr-1282:{headSha}", pendingItem.GetProperty("dedupe_key").GetString());
        Assert.DoesNotContain("pr-transition", pendingItem.GetProperty("recommended_action").GetString(),
            StringComparison.Ordinal);

        var greenPr = pendingPr with
        {
            StatusCheckRollup =
            [
                CheckRun("COMPLETED", "SUCCESS"),
                CheckRun("COMPLETED", "SKIPPED"),
            ],
        };
        var green = RunJson(workspace, issue, greenPr);
        Assert.True(green.RootElement.GetProperty("stalled").GetBoolean());
        var greenItem = Assert.Single(green.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindCiAllGreenNotTransitioned,
            greenItem.GetProperty("kind").GetString());
        Assert.False(greenItem.GetProperty("is_informational").GetBoolean());
        Assert.Equal("all-green", greenItem.GetProperty("ci_outcome").GetString());
        AssertCiBreakdown(greenItem, passed: 1, failed: 0, skipped: 1, pending: 0, total: 2);
        Assert.Equal($"ci-all-green-not-transitioned:pr-1282:{headSha}",
            greenItem.GetProperty("dedupe_key").GetString());
        Assert.Contains("--transition review-start", greenItem.GetProperty("recommended_action").GetString(),
            StringComparison.Ordinal);
        Assert.NotEqual(pendingItem.GetProperty("kind").GetString(), greenItem.GetProperty("kind").GetString());
    }

    [Fact]
    public void Execute_CiTerminalFailure_IsActionableAndCarriesOutcomeBreakdown()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G589", "intent-cli");
        var issue = BuildIssue(1281, "G589: CI wait must be survivable without a timer",
            FixedNow.AddDays(-2), "intent-pr-created");
        const string headSha = "589fff123def4567890abc123def4567890abc12";
        var failedPr = BuildPr(
            1282,
            issue.Title,
            FixedNow.AddMinutes(-90),
            state: "OPEN",
            closingIssueNumber: issue.Number,
            headRefOid: headSha,
            statusCheckRollup:
            [
                StatusContext("SUCCESS"),
                CheckRun("COMPLETED", "SKIPPED"),
                StatusContext("FAILURE"),
            ]);

        var result = RunJson(workspace, issue, failedPr);
        var item = Assert.Single(result.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindCiFailedNotTransitioned,
            item.GetProperty("kind").GetString());
        Assert.False(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal("failed", item.GetProperty("ci_outcome").GetString());
        AssertCiBreakdown(item, passed: 1, failed: 1, skipped: 1, pending: 0, total: 3);
        Assert.Equal($"ci-failed-not-transitioned:pr-1282:{headSha}",
            item.GetProperty("dedupe_key").GetString());
        var action = item.GetProperty("recommended_action").GetString();
        Assert.Contains("repair", action, StringComparison.Ordinal);
        Assert.Contains("escalation", action, StringComparison.Ordinal);
        Assert.DoesNotContain("review-start", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_CiClassification_IsStrictlyReadOnlyAcrossAllOutcomes()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G589", "intent-cli");
        workspace.WriteFile("sentinel.txt", "workflow state must remain untouched\n");
        var issue = BuildIssue(1281, "G589: CI wait must be survivable without a timer",
            FixedNow.AddDays(-2), "intent-pr-created");
        const string headSha = "589ddd123def4567890abc123def4567890abc12";
        var before = SnapshotFiles(workspace.RootPath);

        foreach (var checks in new IReadOnlyList<GitHubAutomationStatusCheckCandidate>[]
                 {
                     [CheckRun("IN_PROGRESS")],
                     [CheckRun("COMPLETED", "SUCCESS")],
                     [CheckRun("COMPLETED", "FAILURE")],
                 })
        {
            var pr = BuildPr(1282, issue.Title, FixedNow.AddMinutes(-90), "OPEN", issue.Number,
                headRefOid: headSha, statusCheckRollup: checks);
            _ = RunJson(workspace, issue, pr);
        }

        var after = SnapshotFiles(workspace.RootPath);
        Assert.Equal(before, after);
    }

    [Fact]
    public void LiveLister_RequestsExactHeadAndStatusRollup()
    {
        Assert.Contains("headRefOid", GhCliGitHubAutomationCandidateLister.PrListJsonFields,
            StringComparison.Ordinal);
        Assert.Contains("statusCheckRollup", GhCliGitHubAutomationCandidateLister.PrListJsonFields,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrCreatedNotReviewing_ExcludesPrAlreadyReviewing()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1144, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-1.5),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-reviewing"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    // ── G533: repair-pending / rereview-pending / claimed-but-silent ────

    [Fact]
    public void Execute_RepairPending_FiresForPrWithRequestUpdateLabel_ReproducesPr1750Finding()
    {
        // Field finding #2: an OPEN PR with intent-target + intent-pr-
        // request-update was reported pr-created-not-reviewing with a
        // review-start recommendation — semantically wrong mid-repair.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1750, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-5),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-request-update"],
            updatedAt: FixedNow.AddHours(-2));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairPending, item.GetProperty("kind").GetString());
        Assert.NotEqual(AutomationStalledWorkCommand.KindPrCreatedNotReviewing, item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal(1750, item.GetProperty("pr").GetProperty("number").GetInt32());
        // Age is since the repair state was entered (PR's own updatedAt),
        // not since PR creation.
        Assert.Equal(120, item.GetProperty("age_minutes").GetInt32());
        Assert.DoesNotContain("--transition", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RepairPending_FiresForPrWithUpdateInProgressLabel()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        // G546: updatedAt is deliberately INSIDE --repair-silent-minutes so
        // this fixture keeps pinning the G533 informational semantics; the
        // promotion past the threshold is covered by its own fixtures below.
        var pr = BuildPr(1144, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-5),
            state: "OPEN", closingIssueNumber: 1143,
            extraLabels: ["intent-pr-request-update", "intent-pr-update-in-progress"],
            updatedAt: FixedNow.AddMinutes(-30));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairPending, item.GetProperty("kind").GetString());
    }

    [Fact]
    public void Execute_RereviewPending_FiresForPrWithRereviewReadyLabel()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1144, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-5),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-rereview-ready"],
            updatedAt: FixedNow.AddMinutes(-45));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRereviewPending, item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal(45, item.GetProperty("age_minutes").GetInt32());
        Assert.DoesNotContain("--transition", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    // ── G546: repair-stalled ────────────────────────────────────────────

    [Fact]
    public void Execute_RepairStalled_ReproducesG545FourDayDraftSilence_G546()
    {
        // Field regression, 2026-07-23 → 07-27: the G545 repair was claimed
        // (intent-pr-update-in-progress) and the implement session died. The
        // PR was a DRAFT, so CollectPrCreatedNotReviewing skipped it outright
        // and stalled-work reported `stalled=false, items=[]` for four days —
        // the exact gap the orchestrator reported. It must now surface.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G545", "intent-cli");
        var issue = BuildIssue(1192, "G545: Exempt queue-blocked units from claimed-but-silent",
            FixedNow.AddDays(-5), "intent-pr-created");
        var pr = BuildPr(1193, "G545: Exempt queue-blocked units from claimed-but-silent",
            FixedNow.AddDays(-4), state: "OPEN", closingIssueNumber: 1192,
            extraLabels: ["intent-pr-request-update", "intent-pr-update-in-progress"],
            updatedAt: FixedNow.AddDays(-4), isDraft: true);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stalled").GetBoolean());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairStalled, item.GetProperty("kind").GetString());
        Assert.False(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal(1193, item.GetProperty("pr").GetProperty("number").GetInt32());
        Assert.Equal(4 * 24 * 60, item.GetProperty("age_minutes").GetInt32());

        var action = item.GetProperty("recommended_action").GetString()!;
        Assert.Contains("status check", action, StringComparison.Ordinal);
        Assert.Contains("`implement`", action, StringComparison.Ordinal);
        Assert.Contains("intent-pr-update-in-progress", action, StringComparison.Ordinal);
        // Never a transition, and never a reassignment, from silence alone.
        Assert.DoesNotContain("--transition", action, StringComparison.Ordinal);
        Assert.DoesNotContain("worker claim", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RepairStalled_FiresForNonDraftRequestUpdate_NamingImplementThread_G546()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1750, "G521: Document agmsg Codex monitor", FixedNow.AddHours(-9),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-request-update"],
            updatedAt: FixedNow.AddHours(-6));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairStalled, item.GetProperty("kind").GetString());
        Assert.False(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal(360, item.GetProperty("age_minutes").GetInt32());
        var action = item.GetProperty("recommended_action").GetString()!;
        Assert.Contains("`implement`", action, StringComparison.Ordinal);
        Assert.Contains("intent-pr-request-update", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RepairStalled_FiresForRereviewReady_NamingReviewDispatchThread_G546()
    {
        // The responsible thread differs by state: a pushed repair awaiting
        // re-review is the REVIEW side going quiet, not the implementer.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G538", "intent-cli");
        var issue = BuildIssue(1180, "G538: Something awaiting rereview", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1181, "G538: Something awaiting rereview", FixedNow.AddDays(-1),
            state: "OPEN", closingIssueNumber: 1180, extraLabels: ["intent-pr-rereview-ready"],
            updatedAt: FixedNow.AddHours(-4));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairStalled, item.GetProperty("kind").GetString());
        var action = item.GetProperty("recommended_action").GetString()!;
        Assert.Contains("`review-dispatch`", action, StringComparison.Ordinal);
        Assert.Contains("intent-pr-rereview-ready", action, StringComparison.Ordinal);
        Assert.DoesNotContain("`implement`", action, StringComparison.Ordinal);
    }

    [Theory]
    // Each of the three observable activity classes named by the contract —
    // a push to the head branch, a PR comment, and a label change — bumps the
    // PR's updatedAt, which is the single field this detector reads. An
    // actively-progressing repair must never be flagged.
    [InlineData(5, "recent head commit")]
    [InlineData(30, "recent PR comment")]
    [InlineData(179, "recent label change")]
    public void Execute_RepairStalled_DoesNotFireForActivelyProgressingRepair_G546(int minutesSinceActivity, string activity)
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1750, $"G521: Document agmsg Codex monitor ({activity})", FixedNow.AddDays(-3),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-update-in-progress"],
            updatedAt: FixedNow.AddMinutes(-minutesSinceActivity));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        // Still the G533 informational kind, byte-for-byte as before.
        Assert.Equal(AutomationStalledWorkCommand.KindRepairPending, item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal(minutesSinceActivity, item.GetProperty("age_minutes").GetInt32());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindRepairStalled);
    }

    [Fact]
    public void Execute_RepairStalled_ActiveDraftRepairInsideThreshold_StaysInvisibleExactlyAsToday_G546()
    {
        // A draft repair PR inside the threshold must produce NO item at all
        // — the draft path deliberately invents no informational kind, so
        // today's output stays byte-compatible.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G545", "intent-cli");
        var issue = BuildIssue(1192, "G545: Exempt queue-blocked units", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1193, "G545: Exempt queue-blocked units", FixedNow.AddDays(-1),
            state: "OPEN", closingIssueNumber: 1192, extraLabels: ["intent-pr-update-in-progress"],
            updatedAt: FixedNow.AddMinutes(-20), isDraft: true);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Empty(doc.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Execute_RepairStalled_ThresholdOverrideChangesPromotion_G546()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G538", "intent-cli");
        var issue = BuildIssue(1180, "G538: Something awaiting rereview", FixedNow.AddDays(-2), "intent-pr-created");
        // The measured G538 shape: 105 minutes — under the 180m default, so
        // it stays informational unless an operator lowers the threshold.
        var pr = BuildPr(1181, "G538: Something awaiting rereview", FixedNow.AddDays(-1),
            state: "OPEN", closingIssueNumber: 1180, extraLabels: ["intent-pr-rereview-ready"],
            updatedAt: FixedNow.AddMinutes(-105));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var defaultWriter = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            defaultWriter);

        using var defaultDoc = JsonDocument.Parse(defaultWriter.ToString());
        Assert.Equal(
            AutomationStalledWorkCommand.KindRereviewPending,
            Assert.Single(defaultDoc.RootElement.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());

        using var overrideWriter = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system",
             "--repair-silent-minutes", "90", "--format", "json"],
            overrideWriter);

        using var overrideDoc = JsonDocument.Parse(overrideWriter.ToString());
        var promoted = Assert.Single(overrideDoc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairStalled, promoted.GetProperty("kind").GetString());
        Assert.Equal(105, promoted.GetProperty("age_minutes").GetInt32());
    }

    [Fact]
    public void Execute_RepairStalled_RejectsNegativeThresholdOverride_G546()
    {
        using var workspace = new StalledWorkWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--repair-silent-minutes", "-1"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repair-silent-minutes requires a non-negative integer", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RepairStalled_UnusableActivityTimestamp_IsNeverPromoted_G546()
    {
        // Silence cannot be established from a malformed updatedAt, so the
        // PR keeps its informational treatment rather than being flagged on
        // unusable evidence — the same fail-closed rule claimed-but-silent
        // already follows.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G521", "intent-cli");
        var issue = BuildIssue(1143, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-2), "intent-pr-created");
        var pr = BuildPr(1750, "G521: Document agmsg Codex monitor", FixedNow.AddDays(-3),
            state: "OPEN", closingIssueNumber: 1143, extraLabels: ["intent-pr-update-in-progress"]) with
        {
            UpdatedAt = "not-a-timestamp",
        };
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindRepairPending, item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
    }

    [Fact]
    public void Execute_ClaimedButSilent_FiresPastDefaultThreshold()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        // 25h silent claim, matching field finding #3 exactly.
        var issue = BuildIssue(1200, "G540: Something claimed and gone quiet", FixedNow.AddHours(-25),
            updatedAt: FixedNow.AddHours(-25), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindClaimedButSilent, item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal("G540", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1200, item.GetProperty("issue").GetProperty("number").GetInt32());
        Assert.Equal(1500, item.GetProperty("age_minutes").GetInt32());
        Assert.DoesNotContain("--transition", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("status check", item.GetProperty("recommended_action").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ClaimedButSilent_FreshClaimUnderThreshold_ReportsNothing()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Something freshly claimed", FixedNow.AddMinutes(-10),
            updatedAt: FixedNow.AddMinutes(-10), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_ClaimedButSilent_ThresholdOverride_FiresEarlierThanDefault()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        // 90 minutes silent — well under the 720-minute default, but past a
        // 60-minute override.
        var issue = BuildIssue(1200, "G540: Something claimed 90 minutes ago", FixedNow.AddMinutes(-90),
            updatedAt: FixedNow.AddMinutes(-90), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var defaultWriter = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            defaultWriter);
        using var defaultDoc = JsonDocument.Parse(defaultWriter.ToString());
        Assert.Equal(0, defaultDoc.RootElement.GetProperty("items").GetArrayLength());

        using var overrideWriter = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--claimed-silent-minutes", "60", "--format", "json"],
            overrideWriter);
        using var overrideDoc = JsonDocument.Parse(overrideWriter.ToString());
        var item = Assert.Single(overrideDoc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindClaimedButSilent, item.GetProperty("kind").GetString());
    }

    [Fact]
    public void Execute_ClaimedButSilent_ExcludedOnceIssueHasPrCreated_PrLifecycleTakesOver()
    {
        // Even a very long silence must NOT fire claimed-but-silent once a
        // PR exists for the issue — that lifecycle is covered by the
        // pr-created-not-reviewing / repair-pending / rereview-pending
        // kinds instead (detecting a stale repair-state PR itself is an
        // explicit out-of-scope follow-up).
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Already has a PR", FixedNow.AddDays(-10),
            updatedAt: FixedNow.AddDays(-10), labels: ["intent-target", "intent-issue-in-progress", "intent-pr-created"]);
        var pr = BuildPr(1201, "G540: Already has a PR", FixedNow.AddDays(-9),
            state: "OPEN", closingIssueNumber: 1200, extraLabels: ["intent-pr-reviewing"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
    }

    [Fact]
    public void Execute_ClaimedButSilent_LinkedOpenPrActivityCountsAsObservableActivity()
    {
        // A linked open PR's own updatedAt counts as activity even before
        // intent-pr-created is applied (e.g. a freshly-opened draft) — the
        // issue itself looks silent, but the linked PR was touched recently.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Draft PR opened quietly", FixedNow.AddHours(-25),
            updatedAt: FixedNow.AddHours(-25), labels: ["intent-target", "intent-issue-in-progress"]);
        var pr = BuildPr(1201, "G540: Draft PR opened quietly", FixedNow.AddHours(-25),
            state: "OPEN", closingIssueNumber: 1200, updatedAt: FixedNow.AddMinutes(-10));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
    }

    // ── G533 review repair: fail closed on unusable activity data ────

    [Fact]
    public void Execute_ClaimedButSilent_MissingUpdatedAt_NeverFallsBackToOldCreatedAt_ExcludedNotFired()
    {
        // The core defect: an old createdAt must NEVER be substituted for
        // a missing updatedAt — that would manufacture a silence interval
        // that begins long before the claim was ever made. Excluded, not
        // reported as a finding.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = new GitHubAutomationIssueCandidate
        {
            Number = 1200,
            Title = "G540: Old issue, missing updatedAt",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1200",
            CreatedAt = FixedNow.AddDays(-100).ToString("O"),
            UpdatedAt = string.Empty,
            State = "OPEN",
            Labels = [new GitHubAutomationLabel { Name = "intent-target" }, new GitHubAutomationLabel { Name = "intent-issue-in-progress" }],
        };
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(
            doc.RootElement.GetProperty("excluded").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
        Assert.Equal(AutomationStalledWorkCommand.ReasonActivityDataUnusable, excludedItem.GetProperty("reason").GetString());
        Assert.Contains("missing", excludedItem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1200", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ClaimedButSilent_MalformedUpdatedAt_NeverFallsBackToOldCreatedAt_ExcludedNotFired()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = new GitHubAutomationIssueCandidate
        {
            Number = 1200,
            Title = "G540: Old issue, malformed updatedAt",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1200",
            CreatedAt = FixedNow.AddDays(-100).ToString("O"),
            UpdatedAt = "not-a-real-timestamp",
            State = "OPEN",
            Labels = [new GitHubAutomationLabel { Name = "intent-target" }, new GitHubAutomationLabel { Name = "intent-issue-in-progress" }],
        };
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(
            doc.RootElement.GetProperty("excluded").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
        Assert.Equal(AutomationStalledWorkCommand.ReasonActivityDataUnusable, excludedItem.GetProperty("reason").GetString());
        Assert.Contains("malformed", excludedItem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ClaimedButSilent_LinkedPrMissingUpdatedAt_ConservativelyExcluded()
    {
        // Same fail-closed treatment for a linked PR's own activity
        // timestamp — never risk under-counting real (unverifiable) PR
        // activity as silence.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Linked PR has bad updatedAt", FixedNow.AddDays(-100),
            updatedAt: FixedNow.AddDays(-100), labels: ["intent-target", "intent-issue-in-progress"]);
        var pr = new GitHubAutomationPrCandidate
        {
            Number = 1201,
            Title = "G540: Linked PR has bad updatedAt",
            Url = "https://github.com/J-Tech-Japan/intent-system/pull/1201",
            CreatedAt = FixedNow.AddDays(-99).ToString("O"),
            UpdatedAt = string.Empty,
            State = "OPEN",
            IsDraft = false,
            ClosingIssuesReferences =
            [
                new GitHubPrClosingIssueReference
                {
                    Number = 1200,
                    Repository = new GitHubPrClosingIssueRepository
                    {
                        Name = "intent-system",
                        Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                    },
                },
            ],
        };
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(
            doc.RootElement.GetProperty("excluded").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
        Assert.Equal(AutomationStalledWorkCommand.ReasonActivityDataUnusable, excludedItem.GetProperty("reason").GetString());
        Assert.Contains("1201", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ClaimedButSilent_OldCreatedAtButRecentUpdatedAt_PostClaimActivityResetsThreshold()
    {
        // Proves createdAt is NEVER consulted for this kind: an issue open
        // for 100 days, but its updatedAt (the ONLY signal this kind uses)
        // is recent — must not fire.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Old issue, recently active", FixedNow.AddDays(-100),
            updatedAt: FixedNow.AddMinutes(-10), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("excluded").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
    }

    [Fact]
    public void Execute_ClaimedButSilent_FutureUpdatedAt_ClampedToNow_NeverFires()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Clock-skewed future updatedAt", FixedNow.AddDays(-100),
            updatedAt: FixedNow.AddHours(2), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    // ─── G545: queue-blocked exemption + blocked-label-drift ───────────────

    [Fact]
    public void Execute_ClaimedButSilent_QueueBlockedUnit_NoBlockedLabelYet_ReportsBlockedLabelDriftNotSilent()
    {
        // G545 field finding (sekiban-as-a-service, 2026-07-21): SKS-G818 is
        // state=blocked in queue-state (blocked_by SKS-G837), but the
        // GitHub issue still only carries intent-issue-in-progress -- no
        // reconcile label yet. Must never fire claimed-but-silent; must
        // instead surface the transitional blocked-label-drift kind naming
        // the reconcile command.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G818", "sekiban-as-a-service");
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-21T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G818",
                  "title": "SKS-G818 title",
                  "state": "blocked",
                  "dependencies": [],
                  "blocked_by": ["SKS-G837"],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        var issue = BuildIssue(818, "SKS-G818: Queue-blocked but still marked in-progress on GitHub", FixedNow.AddHours(-25),
            updatedAt: FixedNow.AddHours(-25), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "sekiban-as-a-service", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindBlockedLabelDrift, item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
        Assert.Equal("SKS-G818", item.GetProperty("execution_unit").GetString());
        Assert.Equal(818, item.GetProperty("issue").GetProperty("number").GetInt32());
        var recommendedAction = item.GetProperty("recommended_action").GetString();
        Assert.Contains("automation issue-block", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("--issue 818", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("SKS-G837", recommendedAction, StringComparison.Ordinal);
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            other => other.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimedButSilent);
    }

    [Fact]
    public void Execute_ClaimedButSilent_QueueBlockedUnit_AlreadyHasBlockedLabel_FullyExempt_NoItemAtAll()
    {
        // Once GitHub has been reconciled (intent-issue-blocked applied),
        // labels and queue-state agree -- neither claimed-but-silent nor
        // blocked-label-drift should fire.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G818", "sekiban-as-a-service");
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-21T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G818",
                  "title": "SKS-G818 title",
                  "state": "blocked",
                  "dependencies": [],
                  "blocked_by": ["SKS-G837"],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        var issue = BuildIssue(818, "SKS-G818: Reconciled", FixedNow.AddHours(-25),
            updatedAt: FixedNow.AddHours(-25), labels: ["intent-target", "intent-issue-in-progress", "intent-issue-blocked"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "sekiban-as-a-service", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_ClaimedButSilent_QueueStatePresentButNotBlocked_StillFiresClaimedButSilent_NoWeakening()
    {
        // No-weakening: a claimed, non-blocked, silent-past-threshold unit
        // must still fire claimed-but-silent even when queue-state.json
        // exists and is readable (proving the G545 exemption is scoped
        // strictly to state=blocked, not "queue-state exists at all").
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        workspace.WriteQueueState(BuildReadyQueueStateJson("G540"));
        var issue = BuildIssue(1200, "G540: Something claimed and gone quiet", FixedNow.AddHours(-25),
            updatedAt: FixedNow.AddHours(-25), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindClaimedButSilent, item.GetProperty("kind").GetString());
        Assert.Equal("G540", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_MergedPr_NeverTreatedAsPrCreatedNotReviewingOrClaimedButSilent()
    {
        // False-positive fixture: a MERGED PR (fetched via the separate
        // merged-PR lister call, never appearing in openPrs) must never be
        // treated as "not reviewing", and its source issue (already
        // carrying intent-pr-created) must never trip claimed-but-silent.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G540", "intent-cli");
        var issue = BuildIssue(1200, "G540: Already merged", FixedNow.AddDays(-10),
            updatedAt: FixedNow.AddDays(-1), labels: ["intent-target", "intent-issue-in-progress", "intent-pr-created"]);
        // Defensive: even if a MERGED-state PR were mistakenly present in
        // the openPrs fake list (never a real `gh pr list --state open`
        // result), it must still be filtered out by the existing IsOpen
        // check rather than misreported.
        var mergedLookingOpenPr = BuildPr(1201, "G540: Already merged", FixedNow.AddDays(-9),
            state: "MERGED", closingIssueNumber: 1200, extraLabels: ["intent-pr-request-update"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [mergedLookingOpenPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_FiresWhenQueueItemNotCompleted()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson("G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199));
        // G532 review repair: the merged PR must itself GitHub-report the
        // queue item's linked_issue among its closing references — a bare
        // linked_pr number match alone is no longer sufficient.
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindMergedNotClosedOut, item.GetProperty("kind").GetString());
        Assert.Equal("G500", item.GetProperty("execution_unit").GetString());
        Assert.Equal(1200, item.GetProperty("pr").GetProperty("number").GetInt32());
        Assert.Equal(180, item.GetProperty("age_minutes").GetInt32());
        Assert.Contains("closeout pr", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("--pr 1200", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MergedNotClosedOut_ExcludesCompletedQueueItem()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson("G500", QueueItemState.Completed,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_MissingQueueStateSurfacesWarning_DoesNotFail()
    {
        using var workspace = new StalledWorkWorkspace();
        // No queue-state.json written at all.
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("warnings").GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_StaleMinutesFilter_ExcludesItemsYoungerThanThreshold()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G525", "intent-cli");
        workspace.WritePacketDomain("G526", "intent-cli");
        var youngIssue = BuildIssue(1150, "G525: A brand new issue", FixedNow.AddMinutes(-10), "intent-target");
        var oldIssue = BuildIssue(1151, "G526: A stale issue", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [youngIssue, oldIssue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "60", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G526", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_PacketDeclaresContradictingDomain_ExcludesAsStructuredResult()
    {
        // PR #1148 review repair (finding 1): a candidate whose
        // packet-declared domain contradicts the requested --domain must be
        // fail-closed — excluded from items[], surfaced in excluded[].
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G512", "sekiban-as-a-service");
        var otherDomainIssue = BuildIssue(9999, "SKS-G512: Not ours", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [otherDomainIssue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("SKS-G512", excludedItem.GetProperty("execution_unit").GetString());
        Assert.Equal("domain-contradiction", excludedItem.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_MisleadingTitlePrefixMatchingOurConvention_StillExcludedByPacketDomain()
    {
        // PR #1148 review repair (finding 1): a title prefix that LOOKS like
        // it belongs to our domain (same "G<n>" convention) must not leak in
        // just because the prefix matches our naming pattern — the packet's
        // own declared domain is authoritative, and here it disagrees.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G9001", "sekiban-as-a-service");
        var misleadingIssue = BuildIssue(9001, "G9001: Looks like ours but isn't", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [misleadingIssue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("domain-contradiction", excludedItem.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_NoPacketYamlAtAll_UncorroboratedCandidate_StillFailsClosedAsUnderivable()
    {
        // G532 review repair: an explicit --domain SCOPES the scan; it does
        // not by itself establish that an otherwise-unidentified candidate
        // (no packet.yaml anywhere corroborates it) is a member of that
        // domain. This is distinct from the case G532 actually fixed —
        // Execute_PacketDeclaresDomainOnlyNested_NotDomainUnderivable_ConfirmsRequestedDomain
        // below, where a REAL, corroborating packet exists but is merely
        // silent on domain. Only a corroborated-but-silent candidate is
        // rescued by an explicit --domain; a fully uncorroborated one
        // remains fail-closed, exactly like the original PR #1148 policy.
        using var workspace = new StalledWorkWorkspace();
        // No packet.yaml written for this execution unit at all.
        var issue = BuildIssue(9999, "SKS-G512: From a different domain naming convention", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        // The leading-token guess is still shown for human readability, but
        // was never trusted for the domain decision.
        Assert.Equal("SKS-G512", excludedItem.GetProperty("execution_unit").GetString());
        Assert.Equal("domain-underivable", excludedItem.GetProperty("reason").GetString());
        Assert.Contains("could not be corroborated", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            "Re-invoke with: intent-cli automation stalled-work --domain <name> --repo J-Tech-Japan/intent-system --format json",
            excludedItem.GetProperty("detail").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoPacketYamlAtAll_Markdown_PinsUnderivableReasonAndReinvocation()
    {
        // Same uncorroborated fixture as above, rendered as markdown.
        using var workspace = new StalledWorkWorkspace();
        var issue = BuildIssue(9999, "SKS-G512: From a different domain naming convention", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("SKS-G512", output, StringComparison.Ordinal);
        Assert.Contains("domain-underivable", output, StringComparison.Ordinal);
        Assert.Contains(
            "Re-invoke with: intent-cli automation stalled-work --domain <name> --repo J-Tech-Japan/intent-system --format json",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MergedNotClosedOut_PacketContradictsDomain_ExcludedNotItem()
    {
        // Cross-domain leakage check for the merged-not-closed-out category
        // specifically: a legacy/shared queue-state may list an item that
        // belongs to a different domain than requested.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G700", "sekiban-as-a-service");
        workspace.WriteQueueState(BuildQueueStateJson("SKS-G700", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1300",
            linkedIssueNumber: 1299));
        var mergedPr = BuildPr(1300, "SKS-G700: Some other domain's merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1299);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("SKS-G700", excludedItem.GetProperty("execution_unit").GetString());
        Assert.Equal("domain-contradiction", excludedItem.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_LinkedIssueRepoDiffersFromMergedPrRepo_FailsClosedNotCorroborated()
    {
        // G532 review repair: a bare `linked_pr: "1300"` number match alone
        // is not sufficient corroboration on a shared/multi-repo
        // queue-state — here the queue item's OWN declared linked_issue
        // names a DIFFERENT repo than the one being scanned, so it must not
        // corroborate this merged PR even though the PR number matches and
        // the merged PR's own closing reference happens to cite the same
        // issue NUMBER (for the scanned repo).
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson(
            "G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199,
            linkedIssueRepo: "SomeOtherOrg/unrelated-repo"));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("G500", excludedItem.GetProperty("execution_unit").GetString());
        Assert.Equal("domain-underivable", excludedItem.GetProperty("reason").GetString());
        Assert.Contains("bare number only", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MergedNotClosedOut_LinkedIssueNumberNotAmongPrClosingReferences_FailsClosedNotCorroborated()
    {
        // The queue item declares a linked_issue for the correct repo, but
        // the merged PR's OWN GitHub-reported closing references cite a
        // DIFFERENT issue number — no genuine correspondence, so it must
        // not corroborate.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson(
            "G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199));
        // Merged PR #1200's own closing reference cites issue #4321, not
        // #1199 — a genuine mismatch, not a coincidental number collision.
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 4321);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("domain-underivable", excludedItem.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_QueueItemHasNoLinkedIssueAtAll_FailsClosedNotCorroborated()
    {
        // A queue item with no linked_issue at all cannot be cross-checked
        // against the merged PR's own closing references — fail closed
        // rather than trusting the bare linked_pr number match alone.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson(
            "G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: null));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("domain-underivable", excludedItem.GetProperty("reason").GetString());
        Assert.Contains("(none)", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MergedNotClosedOut_TwoActiveQueueItemsSameValidRepoIssueDifferentUnits_ReportedAsAmbiguous()
    {
        // G532 review repair: two active queue items both linked to the
        // same merged PR — and both declaring the SAME valid repo+issue
        // that genuinely appears in the PR's own closing references — must
        // never be resolved by picking whichever is first in JSON order.
        // Two different execution units both claiming the same issue is a
        // data-integrity problem, not something --domain or corroboration
        // logic should silently pick a winner for.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WritePacketDomain("G501", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson(
            BuildQueueItem("G500", QueueItemState.Review,
                linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200", linkedIssueNumber: 1199),
            BuildQueueItem("G501", QueueItemState.Review,
                linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200", linkedIssueNumber: 1199)));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous, excludedItem.GetProperty("reason").GetString());
        var detail = excludedItem.GetProperty("detail").GetString();
        Assert.Contains("G500", detail, StringComparison.Ordinal);
        Assert.Contains("G501", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MergedNotClosedOut_TwoActiveQueueItemsOneValidOneInvalidLinkage_StillAmbiguousRegardlessOfOrder()
    {
        // Mixed valid/invalid linkage: one active item's linked_issue
        // genuinely corroborates the merged PR, the other's does not. The
        // mere presence of two ACTIVE matches is itself the ambiguity —
        // it must not be resolved by silently preferring whichever item
        // happens to validate, in either JSON order.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WritePacketDomain("G502", "intent-cli");
        // G500 -> #1199 (matches the merged PR's own closing reference);
        // G502 -> #9999 (does not).
        workspace.WriteQueueState(BuildQueueStateJson(
            BuildQueueItem("G502", QueueItemState.Review,
                linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200", linkedIssueNumber: 9999),
            BuildQueueItem("G500", QueueItemState.Review,
                linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200", linkedIssueNumber: 1199)));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous, excludedItem.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_MergedNotClosedOut_OneActiveOneCompletedQueueItemForSamePr_UsesTheSoleActiveItem()
    {
        // A completed duplicate alongside one genuinely active item is NOT
        // ambiguous — only ACTIVE (non-completed) items compete for
        // authority; a stale/completed leftover must not block detection
        // of the one real stall.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WritePacketDomain("G503", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson(
            BuildQueueItem("G503", QueueItemState.Completed,
                linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200", linkedIssueNumber: 1199),
            BuildQueueItem("G500", QueueItemState.Review,
                linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200", linkedIssueNumber: 1199)));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(mergedPrs: [mergedPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G500", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_TitleWithSubSliceSuffixNotImmediatelyFollowedByColon_ResolvesLeadingIdTokenOnly()
    {
        // G532 regression fixture: field finding #1 (design@sekiban-as-a-
        // service-orch, 2026-07-15, PR #1748). The prior "everything before
        // the first colon" rule parsed this title as unit "SKS-G815 G812
        // sub-slice 1" — packet lookup then failed and the candidate was
        // wrongly excluded. The correct unit is the LEADING ID token alone.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G815", "sekiban-as-a-service");
        var issue = BuildIssue(1748, "SKS-G815 G812 sub-slice 1: Something narrow", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "sekiban-as-a-service", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("SKS-G815", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_PacketDeclaresDomainOnlyNested_NotDomainUnderivable_ConfirmsRequestedDomain()
    {
        // G532 regression fixture: field finding #4 (design@sekiban-as-a-
        // service-orch, 2026-07-18, SKS-G823 / issue #1757). Domain
        // derivation must read the nested
        // implementation_issue_packet.domain field — the top-level domain:
        // alias is not the only source.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteNestedPacket("SKS-G823", "sekiban-as-a-service");
        var issue = BuildIssue(1757, "SKS-G823: Nested domain only", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "sekiban-as-a-service", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("SKS-G823", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_PacketDeclaresBothNestedAndTopLevelDomain_NestedWinsOverTopLevelAlias()
    {
        // The nested field is first-class; a disagreeing top-level alias
        // must not shadow it merely by appearing earlier in the file.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketWithDisagreeingTopLevelAndNestedDomain(
            "G960", topLevelDomain: "wrong-domain", nestedDomain: "intent-cli");
        var issue = BuildIssue(1960, "G960: Nested domain takes priority", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G960", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_TitleWithNoLeadingIdToken_FallsBackToSourceExecutionUnitMatchBeforeExclusion()
    {
        // G532 acceptance criterion: "a title with no leading ID token
        // falls back to source_execution_unit matching before exclusion."
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteNestedPacket("G900", "intent-cli");
        var issue = BuildIssue(1900, "Freeform title mentioning G900 mid-sentence", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G900", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_TitleWithNoLeadingIdTokenAndNoMatchingPacketAnywhere_ReportedAsUnderivableNotSilentlyDropped()
    {
        // The worst case (no leading token, no packet anywhere corroborates
        // it) must be REPORTED — in excluded[] with a structured reason and
        // diagnostics — rather than silently disappearing. It must NOT be
        // silently included either: an explicit --domain scopes the scan,
        // it does not identify an otherwise-unidentified candidate as
        // belonging to it.
        using var workspace = new StalledWorkWorkspace();
        var issue = BuildIssue(1901, "Completely freeform title with no identifiable unit", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(string.Empty, excludedItem.GetProperty("execution_unit").GetString());
        Assert.Equal("domain-underivable", excludedItem.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_PacketDeclaresContradictingDomain_DetailNamesDerivationAttempted()
    {
        // G532 acceptance criterion: every excluded item's diagnostics must
        // name not just the reason but the derivation attempted.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G950", "sekiban-as-a-service");
        var issue = BuildIssue(1950, "G950: Not ours", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Contains("Derivation attempted", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("implementation_issue_packet.domain", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("top-level `domain:` alias", excludedItem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PacketExistsButDeclaresNoDomainField_CorroboratedAndRescuedByExplicitDomain()
    {
        // G532's core fix, precisely scoped: a REAL, corroborating packet
        // (folder exists, packet.yaml is readable, its own
        // source_execution_unit matches) that simply never declares a
        // domain — neither nested nor top-level — is rescued by an
        // explicit --domain, unlike a fully uncorroborated candidate (see
        // Execute_NoPacketYamlAtAll_UncorroboratedCandidate_StillFailsClosedAsUnderivable).
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketWithNoDomainField("G970");
        var issue = BuildIssue(1970, "G970: Packet exists but is silent on domain", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("G970", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_TitleWithLetterImmediatelyAfterLeadingIdDigits_NeverTruncatesToAShorterUnit()
    {
        // G532 review repair: the leading-token regex needs a right
        // boundary. Without one, "G12abc" would wrongly parse as "G12" —
        // here a packet for "G12" exists (and would confirm the requested
        // domain if wrongly matched), but the title's real token "G12abc"
        // does not correspond to it, and no packet declares
        // source_execution_unit "G12abc" either — so this must stay
        // uncorroborated and excluded, never silently included via the
        // truncated "G12" guess.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G12", "intent-cli");
        var issue = BuildIssue(1912, "G12abc: Looks like G12 but is not", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal("domain-underivable", excludedItem.GetProperty("reason").GetString());
        // Never the truncated guess — the boundary rejects it entirely, so
        // there is no leading-token guess left to show either.
        Assert.Equal(string.Empty, excludedItem.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_TitleMatchesTwoDistinctDeclaredExecutionUnits_ReportedAsAmbiguousNotGuessed()
    {
        // G532 review repair: when a freeform title (no leading ID token)
        // contains tokens matching TWO different packets' declared
        // source_execution_unit, the execution unit is genuinely ambiguous
        // — it must never be resolved by picking the longest match or the
        // first sorted directory.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteNestedPacket("G12", "intent-cli");
        workspace.WriteNestedPacket("G34", "intent-cli");
        var issue = BuildIssue(1934, "Combine G12 and G34 into one follow-up", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous, excludedItem.GetProperty("reason").GetString());
        Assert.Equal(string.Empty, excludedItem.GetProperty("execution_unit").GetString());
        var detail = excludedItem.GetProperty("detail").GetString();
        Assert.Contains("ambiguous", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G12", detail, StringComparison.Ordinal);
        Assert.Contains("G34", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TwoPacketFilesDeclareIdenticalSourceExecutionUnit_ReportedAsAmbiguousNotCollapsed()
    {
        // G532 review repair: two DISTINCT packet files that happen to
        // declare the identical source_execution_unit value (a duplicate
        // declaration — here with contradictory domains) must never be
        // collapsed into one corroborated match by string equality. Exactly
        // one matching packet FILE is required, not one distinct value.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteNestedPacketAtFolder("G50-copy-a", "G50", "intent-cli");
        workspace.WriteNestedPacketAtFolder("G50-copy-b", "G50", "sekiban-as-a-service");
        var issue = BuildIssue(1950, "Freeform title mentioning G50 mid-sentence", FixedNow.AddHours(-26), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excludedItem = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.ReasonExecutionUnitAmbiguous, excludedItem.GetProperty("reason").GetString());
        Assert.Equal(string.Empty, excludedItem.GetProperty("execution_unit").GetString());
        var detail = excludedItem.GetProperty("detail").GetString();
        Assert.Contains("G50-copy-a", detail, StringComparison.Ordinal);
        Assert.Contains("G50-copy-b", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RequiresDomainFlag()
    {
        using var workspace = new StalledWorkWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RequiresRepoFlag()
    {
        using var workspace = new StalledWorkWorkspace();
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesGitHubQueueStateOrRunsLog()
    {
        // Read-only guarantee: even with a full one-of-each fixture, the
        // command must never touch queue-state.json / runs.jsonl / any
        // GitHub write path (the fake lister has no write methods at all,
        // so this test additionally proves the command never needs one).
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WritePacketDomain("G523", "intent-cli");
        workspace.WritePacketDomain("G521", "intent-cli");
        var queueStateJson = BuildQueueStateJson("G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199);
        workspace.WriteQueueState(queueStateJson);
        var publishedIssue = BuildIssue(1147, "G523: Ours", FixedNow.AddHours(-26), "intent-target");
        var prCreatedIssue = BuildIssue(1143, "G521: Document agmsg", FixedNow.AddDays(-2), "intent-pr-created");
        var reviewPr = BuildPr(1144, "G521: Document agmsg", FixedNow.AddHours(-1.5), state: "OPEN", closingIssueNumber: 1143);
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED", closingIssueNumber: 1199);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(
            issues: [publishedIssue, prCreatedIssue],
            prs: [reviewPr],
            mergedPrs: [mergedPr]);

        var runsPath = Path.Combine(workspace.RootPath, ".intent-cli", "runs.jsonl");
        var queueStatePath = workspace.Context.GetQueueStatePath();
        var queueStateBefore = File.ReadAllText(queueStatePath);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(3, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.False(File.Exists(runsPath), "stalled-work must never append a runs.jsonl event");
        Assert.Equal(queueStateBefore, File.ReadAllText(queueStatePath));
    }

    // ─── G552: design-decision-pending ──────────────────────────────────────

    [Fact]
    public void Execute_DesignDecisionPending_FiresForOpenClarification_WithAgeUnitAndQuestion()
    {
        // G552 field incident (2026-07-28 16:11 -> 07-29 01:29): a nine-hour
        // hold on a one-line wording ruling reported stalled=false throughout
        // because the block lived only in agmsg messages. Recorded as a
        // clarification artifact, the same hold is visible.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G551", "intent-cli");
        workspace.WriteClarification(
            "G551",
            "Does the release note say eleven or twelve slices?",
            FixedNow.AddMinutes(-540));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stalled").GetBoolean());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindDesignDecisionPending, item.GetProperty("kind").GetString());
        Assert.Equal("G551", item.GetProperty("execution_unit").GetString());
        Assert.Equal(540, item.GetProperty("age_minutes").GetInt32());
        Assert.False(item.GetProperty("is_informational").GetBoolean());

        var recommendedAction = item.GetProperty("recommended_action").GetString()!;
        // Names the clarification to answer (design) and the escalation path
        // (operator) — and never auto-answers.
        Assert.Contains("Does the release note say eleven or twelve slices?", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("clarify answer", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("--execution-unit G551", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("escalate", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("Never auto-answer", recommendedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DesignDecisionPending_ClearsOnceTheClarificationIsAnswered()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G551", "intent-cli");
        workspace.WriteClarification(
            "G551",
            "Does the release note say eleven or twelve slices?",
            FixedNow.AddMinutes(-540),
            ClarificationStatus.Answered);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        // Answering clears it entirely — not into excluded[], which would be
        // its own kind of noise.
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
    }

    [Fact]
    public void Execute_DesignDecisionPending_NoClarificationSurface_ProducesNothing()
    {
        // The no-false-positive case: absence of a clarification is never a
        // stall signal on its own.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G551", "intent-cli");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
    }

    [Fact]
    public void Execute_DesignDecisionPending_UnreadableArtifact_IsExcludedNeverAssumedAnswered()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/clarifications/G551/request.json", "{ not valid json }");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());

        var excluded = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindDesignDecisionPending, excluded.GetProperty("kind").GetString());
        Assert.Equal(AutomationStalledWorkCommand.ReasonClarificationUnreadable, excluded.GetProperty("reason").GetString());
        var detail = excluded.GetProperty("detail").GetString()!;
        Assert.Contains("request.json", detail, StringComparison.Ordinal);
        Assert.Contains("not evidence of an unblocked pipeline", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DesignDecisionPending_OtherDomainClarification_IsExcludedNotAttributed()
    {
        // Domain isolation: a clarification whose packet declares a different
        // domain never leaks into this domain's report.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G900", "sekiban-as-a-service");
        workspace.WriteClarification("SKS-G900", "Which aggregate owns this invariant?", FixedNow.AddMinutes(-300));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excluded = Assert.Single(doc.RootElement.GetProperty("excluded").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindDesignDecisionPending, excluded.GetProperty("kind").GetString());
        Assert.Equal("SKS-G900", excluded.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_DesignDecisionPending_ReportsEveryOpenClarification_Independently()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G551", "intent-cli");
        workspace.WritePacketDomain("G552", "intent-cli");
        workspace.WriteClarification("G551", "Eleven or twelve slices?", FixedNow.AddMinutes(-540));
        workspace.WriteClarification("G552", "Does bounded authority cover wording?", FixedNow.AddMinutes(-60));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var items = doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => (Unit: i.GetProperty("execution_unit").GetString()!, Age: i.GetProperty("age_minutes").GetInt32()))
            .ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, i => i.Unit == "G551" && i.Age == 540);
        Assert.Contains(items, i => i.Unit == "G552" && i.Age == 60);
    }

    // ─── G552 repair: end-to-end through the REAL canonical commands ────────

    [Fact]
    public void EndToEnd_ClarifyOpen_CarriesTheRealQuestionAndRecommendation_ThroughDetectorHeartbeatAndAnswer_G552()
    {
        // G552 repair acceptance: the canonical flow must be executable end to
        // end through the REAL commands — direct JSON serialization is not
        // sufficient proof. Drives: clarify open (with the actual design
        // question, its recommended answer, and the verifying evidence) ->
        // stalled-work detects the EXACT persisted content -> heartbeat carries
        // it -> clarify answer clears it.
        using var workspace = new StalledWorkWorkspace();
        // NOTE: WritePacketDomain must NOT be called here — it rewrites
        // packet.yaml with a bare `domain:` line, which would destroy the full
        // projection packet the real `clarify open` needs. The packet written
        // below declares its own domain instead.
        workspace.WriteClarifiablePacket("G552");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        const string Question = "Does the v0.6.0 release note say eleven or twelve slices?";
        const string RecommendedAnswer = "Eleven — G547 was terminally retired and re-cut as G551.";
        const string Evidence = "Merged PR list #1181-#1204 enumerates eleven slices; G547 has no merged PR.";

        var originalTimestamp = ClarifyOpenCommand.TimestampFactory;
        try
        {
            ClarifyOpenCommand.TimestampFactory = () => FixedNow.AddMinutes(-540);

            // 1. Open the hold through the real command, carrying real content.
            using (var openWriter = new StringWriter())
            {
                var openExit = ClarifyOpenCommand.Execute(
                    workspace.Context,
                    ["G552", "--question", Question, "--recommended-answer", RecommendedAnswer, "--evidence", Evidence],
                    openWriter);

                Assert.True(openExit == 0, openWriter.ToString());
                // The command echoes what it PERSISTED, not a pre-composition
                // value — the operator must see the durable record.
                Assert.Contains(Question, openWriter.ToString(), StringComparison.Ordinal);
                Assert.Contains(RecommendedAnswer, openWriter.ToString(), StringComparison.Ordinal);
            }

            // The OPEN artifact itself carries the content — no agmsg
            // substitution, no packet synthesis.
            var artifact = ClarificationSerializer.Deserialize(
                File.ReadAllText(Path.Combine(workspace.RootPath, ".intent-cli", "clarifications", "G552", "request.json")));
            Assert.Equal(ClarificationStatus.Open, artifact.Status);
            Assert.Equal(Question, artifact.QuestionText);
            Assert.Contains($"Recommended answer: {RecommendedAnswer}", artifact.Reason, StringComparison.Ordinal);
            Assert.Contains($"Evidence: {Evidence}", artifact.Reason, StringComparison.Ordinal);

            // 2. The detector reports the EXACT persisted question.
            using (var stalledWriter = new StringWriter())
            {
                var exitCode = AutomationStalledWorkCommand.Execute(
                    workspace.Context,
                    ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
                    stalledWriter);

                Assert.Equal(0, exitCode);
                using var doc = JsonDocument.Parse(stalledWriter.ToString());
                Assert.True(doc.RootElement.GetProperty("stalled").GetBoolean());
                var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(AutomationStalledWorkCommand.KindDesignDecisionPending, item.GetProperty("kind").GetString());
                Assert.Equal("G552", item.GetProperty("execution_unit").GetString());
                Assert.Equal(540, item.GetProperty("age_minutes").GetInt32());
                Assert.Contains(Question, item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
            }

            // 3. The heartbeat carries it.
            using (var heartbeatWriter = new StringWriter())
            {
                var exitCode = AutomationHeartbeatCommand.Execute(
                    workspace.Context,
                    ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
                    heartbeatWriter);

                Assert.Equal(0, exitCode);
                using var doc = JsonDocument.Parse(heartbeatWriter.ToString());
                Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
                var messageBody = doc.RootElement.GetProperty("message_body").GetString()!;
                Assert.Contains(AutomationStalledWorkCommand.KindDesignDecisionPending, messageBody, StringComparison.Ordinal);
                Assert.Contains("G552", messageBody, StringComparison.Ordinal);
            }

            // 4. Answering through the real command clears the item.
            var answerPath = Path.Combine(workspace.RootPath, "answer.txt");
            File.WriteAllText(answerPath, "Eleven. G547 is retired, not shipped.");
            using (var answerWriter = new StringWriter())
            {
                var answerExit = ClarifyAnswerCommand.Execute(
                    workspace.Context,
                    ["G552", "--from-file", answerPath],
                    answerWriter);

                Assert.True(answerExit == 0, answerWriter.ToString());
            }

            using (var clearedWriter = new StringWriter())
            {
                var exitCode = AutomationStalledWorkCommand.Execute(
                    workspace.Context,
                    ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
                    clearedWriter);

                Assert.Equal(0, exitCode);
                using var doc = JsonDocument.Parse(clearedWriter.ToString());
                Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
                Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
                Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
            }
        }
        finally
        {
            ClarifyOpenCommand.TimestampFactory = originalTimestamp;
        }
    }

    [Fact]
    public void ClarifyOpen_WithoutExplicitInputs_KeepsThePreG552DerivedBehavior()
    {
        // The new inputs are optional: omitting them must leave the
        // packet-derived question and the plain reason exactly as before, so no
        // existing caller changes behavior.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteClarifiablePacket("G552");

        var originalTimestamp = ClarifyOpenCommand.TimestampFactory;
        try
        {
            ClarifyOpenCommand.TimestampFactory = () => FixedNow.AddMinutes(-30);
            using var openWriter = new StringWriter();
            var openExit = ClarifyOpenCommand.Execute(workspace.Context, ["G552"], openWriter);
            Assert.True(openExit == 0, openWriter.ToString());
        }
        finally
        {
            ClarifyOpenCommand.TimestampFactory = originalTimestamp;
        }

        var artifact = ClarificationSerializer.Deserialize(
            File.ReadAllText(Path.Combine(workspace.RootPath, ".intent-cli", "clarifications", "G552", "request.json")));
        // Derived from the packet's first deterministic review check.
        Assert.Contains("Clarify blocker for", artifact.QuestionText, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommended answer:", artifact.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Evidence:", artifact.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ClarificationStatus.Answered)]
    [InlineData(ClarificationStatus.Applied)]
    [InlineData(ClarificationStatus.Cancelled)]
    public void DesignDecisionPending_ClearsOnEveryTerminalStatus_G552(ClarificationStatus terminalStatus)
    {
        // G552 repair acceptance: every terminal status clears the item, not
        // just `answered`. `clarify answer` produces Answered/Applied through
        // the canonical gateway (covered end to end above); Cancelled has no
        // command of its own today, so it is proven here against the same
        // canonical model path the detector reads.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G552", "intent-cli");
        workspace.WriteClarification(
            "G552",
            "Eleven or twelve slices?",
            FixedNow.AddMinutes(-540),
            terminalStatus);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, doc.RootElement.GetProperty("excluded").GetArrayLength());
    }

    [Fact]
    public void BoundedAuthorityResolution_IsRecordedUnderRecentlyResolved_AndStaysForPostHocAmendment_G552()
    {
        // G552 repair acceptance: the bounded-authority evidence log has a
        // concrete durable sink — `clarify record --from-file` — and the entry
        // must REMAIN readable afterwards so design can amend post hoc.
        using var workspace = new StalledWorkWorkspace();
        var returnPath = Path.Combine(workspace.RootPath, "intents", "intent-cli", "clarifications", "open.md");
        Directory.CreateDirectory(Path.GetDirectoryName(returnPath)!);
        File.WriteAllText(returnPath, "# Clarifications\n\n## Open\n\n(none)\n\n## Recently Resolved\n\n");

        var decisionPath = Path.Combine(workspace.RootPath, "authority-decision.md");
        File.WriteAllText(decisionPath, """
            ## Question
            G552 release-note slice count: eleven or twelve?

            ## Decision
            Eleven.

            ## Rationale
            Merged PR list #1181-#1204 enumerates eleven slices; G547 has no merged PR. Reviewer and orchestrator agreed.
            """);

        using var writer = new StringWriter();
        var exitCode = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--from-file", decisionPath],
            writer);

        Assert.True(exitCode == 0, writer.ToString());

        // The entry lands under `## Recently Resolved` with the exact content
        // of all three sections.
        var recorded = File.ReadAllText(returnPath);
        var recentlyResolvedIndex = recorded.IndexOf("## Recently Resolved", StringComparison.Ordinal);
        Assert.True(recentlyResolvedIndex >= 0);
        var recentlyResolved = recorded[recentlyResolvedIndex..];
        Assert.Contains("G552 release-note slice count: eleven or twelve?", recentlyResolved, StringComparison.Ordinal);
        Assert.Contains("Eleven.", recentlyResolved, StringComparison.Ordinal);
        Assert.Contains("Merged PR list #1181-#1204 enumerates eleven slices", recentlyResolved, StringComparison.Ordinal);
        Assert.Contains("Reviewer and orchestrator agreed", recentlyResolved, StringComparison.Ordinal);

        // Post-hoc amendment: the entry is still there on a later read, and a
        // second recorded decision never displaces the first — design can read
        // both and amend from the evidence.
        var amendmentPath = Path.Combine(workspace.RootPath, "amendment.md");
        File.WriteAllText(amendmentPath, """
            ## Question
            G552 release-note slice count: eleven or twelve?

            ## Decision
            Confirmed eleven (design amendment review).

            ## Rationale
            Design reviewed the recorded evidence and confirmed the granted-authority resolution.
            """);

        using var amendWriter = new StringWriter();
        var amendExit = ClarifyRecordCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--from-file", amendmentPath],
            amendWriter);

        Assert.True(amendExit == 0, amendWriter.ToString());

        var afterAmendment = File.ReadAllText(returnPath);
        Assert.Contains("Confirmed eleven (design amendment review).", afterAmendment, StringComparison.Ordinal);
        // The original resolution and its verifying facts survive — an
        // amendment adds to the trail rather than erasing what it amends.
        Assert.Contains("Merged PR list #1181-#1204 enumerates eleven slices", afterAmendment, StringComparison.Ordinal);
    }

    // ─── G544: backlog-ready-idle ───────────────────────────────────────────

    [Fact]
    public void Execute_BacklogReadyIdle_FiresPastThreshold_EmptyWipPublishablePacket()
    {
        // G544 field incident (2026-07-20, immediately after the G539
        // closeout): WIP empty, a ready packet unpublished, stalled-work
        // reported healthy anyway. Runs.jsonl's last activity is 46
        // minutes old (past the default 45-minute threshold) -> fires.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-46)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stalled").GetBoolean());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindBacklogReadyIdle, item.GetProperty("kind").GetString());
        Assert.Equal("G600", item.GetProperty("execution_unit").GetString());
        Assert.Equal(46, item.GetProperty("age_minutes").GetInt32());
        Assert.False(item.GetProperty("is_informational").GetBoolean());
        var recommendedAction = item.GetProperty("recommended_action").GetString();
        Assert.Contains("issue publish-flow", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("G600", recommendedAction, StringComparison.Ordinal);
        Assert.Contains("--write", recommendedAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_InsideThreshold()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-10)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithOpenPrHavingNoClosingIssueReference()
    {
        // An open PR with no closing-issue reference for this repo cannot
        // have its domain corroborated by anything -- conservatively
        // blocks, exactly like an uncorroborated open intent-target issue.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        var openPr = BuildPr(1500, "G601: unrelated in-flight work", FixedNow.AddHours(-1), state: "OPEN");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(prs: [openPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithOpenPrClosingSameDomainIssue_G544Repair()
    {
        // G544 review repair: a PR's domain is resolved through its CLOSING
        // ISSUE (never the PR's own title) -- an open PR whose closing
        // issue is confirmed to belong to THIS domain genuinely blocks.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        workspace.WritePacketDomain("G601", "intent-cli");
        var sourceIssue = BuildIssue(1501, "G601: in-flight for this domain", FixedNow.AddHours(-2), "intent-target", "intent-pr-created");
        var openPr = BuildPr(1600, "G601: PR for in-flight work", FixedNow.AddHours(-1), state: "OPEN", closingIssueNumber: 1501);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [sourceIssue], prs: [openPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_Fires_WhenOpenPrClosesIssueConfirmedForDifferentDomain_G544Repair()
    {
        // G544 review repair: an open PR whose closing issue is CONCLUSIVELY
        // confirmed to belong to a DIFFERENT domain must not suppress this
        // domain's detection.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        workspace.WritePacketDomain("SKS-G700", "sekiban-as-a-service");
        var otherDomainIssue = BuildIssue(1502, "SKS-G700: in-flight for another domain", FixedNow.AddHours(-2), "intent-target", "intent-pr-created");
        var otherDomainPr = BuildPr(1601, "SKS-G700: PR for another domain", FixedNow.AddHours(-1), state: "OPEN", closingIssueNumber: 1502);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [otherDomainIssue], prs: [otherDomainPr]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal("G600", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithUnmetDependency_G544Repair()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G600",
                  "title": "G600 title",
                  "state": "queued",
                  "dependencies": ["G599"],
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
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G598", FixedNow.AddMinutes(-100)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithNonEmptyBlockedBy_G544Repair()
    {
        // G544 review repair regression: before the shared canonical
        // selector's fallback loop was fixed, a queue-known blocked_by-
        // blocked unit could still be resurrected as issue-cut-ready and
        // wrongly fire backlog-ready-idle.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G600",
                  "title": "G600 title",
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
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G598", FixedNow.AddMinutes(-100)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithOpenClarification_G544Repair()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        workspace.WriteFile(
            "intents/intent-cli/clarifications/open.md",
            "## Open Questions\n\n- [ ] Genuinely open question blocking publish.\n");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_Fires_WithLaterEligibleUnit_WhenEarlierUnitIsBlocked_G544Repair()
    {
        // Canonical selector order is preserved: the earlier-authored unit
        // is blocked_by-blocked, so the LATER, eligible unit is the one
        // backlog-ready-idle names -- never the blocked one, and never a
        // refusal to fire just because SOME unit is blocked.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(".intent-cli/issues/G601/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "G600",
                  "title": "authored first, blocked_by-blocked",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": ["waiting on operator decision"],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                },
                {
                  "execution_unit": "G601",
                  "title": "authored second, eligible",
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
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G598", FixedNow.AddMinutes(-100)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal("G601", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithOpenIntentTargetIssueForThisDomain()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        workspace.WritePacketDomain("G601", "intent-cli");
        var claimedIssue = BuildIssue(1501, "G601: in-flight for this domain", FixedNow.AddHours(-1), "intent-target", "intent-issue-in-progress");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [claimedIssue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_Fires_WhenOpenIntentTargetIssueConfirmedForDifferentDomain()
    {
        // A confirmed OTHER-domain open intent-target issue must not block
        // THIS domain's backlog-ready-idle check.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        workspace.WritePacketDomain("SKS-G700", "sekiban-as-a-service");
        var otherDomainIssue = BuildIssue(1502, "SKS-G700: in-flight for another domain", FixedNow.AddHours(-1), "intent-target");
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [otherDomainIssue]);

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal("G600", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithOnlyBlockedCandidate()
    {
        // The only authored packet is lifecycle-retired (absorbed into
        // another unit) -- one of the gates the issue explicitly names
        // ("dependencies, open clarifications, or lifecycle-retired") as
        // never counting a candidate as ready. next-slice's own
        // IsExcludedByLifecycle gate excludes it identically regardless of
        // which internal path selects candidates, so no candidate survives
        // and the outcome is never issue-cut-ready.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteFile(
            ".intent-cli/issues/G600/lifecycle.yaml",
            "lifecycle: absorbed\nabsorbed_by: G601\nretired_reason: \"fully absorbed into G601\"\n");
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_BacklogReadyIdle_DoesNotFire_WithEmptyBacklog()
    {
        // No queue-state at all, no open issues/PRs -- genuinely idle, not
        // "ready but idle": next-slice reports no candidate at all.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-100)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void Execute_BacklogReadyIdle_ExcludedWhenNoRunsLogExists_NeverGuessesAnAge()
    {
        // No runs.jsonl at all -- no last-activity baseline can be
        // established. Fails closed into excluded[], never a guessed age.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
        var excluded = Assert.Single(
            doc.RootElement.GetProperty("excluded").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal(AutomationStalledWorkCommand.ReasonActivityDataUnusable, excluded.GetProperty("reason").GetString());
    }

    [Fact]
    public void Execute_BacklogReadyIdle_UsesMostRecentRunsLogRow_NotFirstOrLast()
    {
        // Multiple runs.jsonl rows -- the MAXIMUM ts is the activity
        // baseline, regardless of file order.
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(
            ".intent-cli/runs.jsonl",
            BuildRunsLogLine("G598", FixedNow.AddMinutes(-500))
            + BuildRunsLogLine("G599", FixedNow.AddMinutes(-10)) // most recent -- inside threshold
            + BuildRunsLogLine("G597", FixedNow.AddMinutes(-300)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        // The most recent row (-10m) is inside the 45m threshold, so it
        // must not fire even though two OTHER rows are far older.
        Assert.False(doc.RootElement.GetProperty("stalled").GetBoolean());
    }

    [Fact]
    public void Execute_BacklogReadyIdleThreshold_OverriddenViaFlag()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-20)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--backlog-idle-minutes", "15", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(15, doc.RootElement.GetProperty("backlog_idle_minutes_threshold").GetInt32());
        var item = Assert.Single(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
        Assert.Equal("G600", item.GetProperty("execution_unit").GetString());
    }

    [Fact]
    public void Execute_BlockedParked_ReproducesReleaseGateFieldShape_AndHeartbeatCarriesInformationalNote_G574()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G570/github-body.md", BuildCompleteContractBody());
        workspace.WritePacketDomain("G570", "intent-cli");
        workspace.WriteQueueState(BuildPrePublishQueueStateJson(
            "G570", "blocked", ["v0.7.1-release-gate — publish only after release"]));
        workspace.WriteFile(
            ".intent-cli/runs.jsonl",
            BuildRunsLogLine("G569", FixedNow.AddMinutes(-200))
            + BuildRunsLogLine("G570", FixedNow.AddMinutes(-45), "blocked", "v0.7.1-release-gate — publish only after release"));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var stalledWriter = new StringWriter();
        var stalledExit = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            stalledWriter);

        Assert.Equal(0, stalledExit);
        using var stalledDoc = JsonDocument.Parse(stalledWriter.ToString());
        var item = Assert.Single(stalledDoc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindBlockedParked, item.GetProperty("kind").GetString());
        Assert.Equal("G570", item.GetProperty("execution_unit").GetString());
        Assert.Equal(45, item.GetProperty("age_minutes").GetInt32());
        Assert.True(item.GetProperty("is_informational").GetBoolean());
        Assert.Contains("v0.7.1-release-gate", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("publish-flow", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            stalledDoc.RootElement.GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);

        using var heartbeatWriter = new StringWriter();
        var heartbeatExit = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            heartbeatWriter);

        Assert.Equal(0, heartbeatExit);
        using var heartbeatDoc = JsonDocument.Parse(heartbeatWriter.ToString());
        var messageBody = heartbeatDoc.RootElement.GetProperty("message_body").GetString()!;
        Assert.Contains("0 pending transition(s), 1 informational note(s)", messageBody, StringComparison.Ordinal);
        Assert.Contains("blocked-parked", messageBody, StringComparison.Ordinal);
        Assert.Contains("FYI:", messageBody, StringComparison.Ordinal);
        Assert.Contains("v0.7.1-release-gate", messageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("publish-flow", messageBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("blocked", false, "blocked")]
    [InlineData("queued", true, "queued")]
    public void Execute_HalfConvergedBlockedFields_ReportActionableStateDrift_NeverPublish_G574(
        string state,
        bool hasBlockedBy,
        string transitionEvent)
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G570/github-body.md", BuildCompleteContractBody());
        workspace.WritePacketDomain("G570", "intent-cli");
        workspace.WriteQueueState(BuildPrePublishQueueStateJson(
            "G570", state, hasBlockedBy ? ["v0.7.1-release-gate"] : []));
        workspace.WriteFile(
            ".intent-cli/runs.jsonl",
            BuildRunsLogLine("G570", FixedNow.AddMinutes(-60), transitionEvent,
                hasBlockedBy ? "v0.7.1-release-gate" : null));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindStateDrift, item.GetProperty("kind").GetString());
        Assert.False(item.GetProperty("is_informational").GetBoolean());
        Assert.Contains("automation issue-block", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.Contains("--clear --pre-publish", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("publish-flow", item.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            doc.RootElement.GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle);
    }

    [Fact]
    public void Execute_PrePublishUnblock_RestartsBacklogIdleAgeAtCanonicalTransition_G574()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G570/github-body.md", BuildCompleteContractBody());
        workspace.WritePacketDomain("G570", "intent-cli");
        workspace.WriteQueueState(BuildPrePublishQueueStateJson(
            "G570", "blocked", ["v0.7.1-release-gate"]));
        workspace.WriteFile(
            ".intent-cli/runs.jsonl",
            BuildRunsLogLine("G570", FixedNow.AddMinutes(-120), "blocked", "v0.7.1-release-gate"));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();
        AutomationIssueBlockCommand.UtcNowFactory = () => FixedNow.AddMinutes(-10);

        try
        {
            using var clearWriter = new StringWriter();
            var clearExit = AutomationIssueBlockCommand.Execute(
                workspace.Context,
                ["G570", "--clear", "--pre-publish", "--write", "--format", "json"],
                clearWriter);
            Assert.True(clearExit == 0, clearWriter.ToString());

            using var writer = new StringWriter();
            var exitCode = AutomationStalledWorkCommand.Execute(
                workspace.Context,
                ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "0", "--backlog-idle-minutes", "0", "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            using var doc = JsonDocument.Parse(writer.ToString());
            var item = Assert.Single(doc.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(AutomationStalledWorkCommand.KindBacklogReadyIdle, item.GetProperty("kind").GetString());
            Assert.Equal(10, item.GetProperty("age_minutes").GetInt32());
            Assert.DoesNotContain(
                doc.RootElement.GetProperty("items").EnumerateArray(),
                candidate => candidate.GetProperty("kind").GetString() is
                    AutomationStalledWorkCommand.KindBlockedParked or AutomationStalledWorkCommand.KindStateDrift);
        }
        finally
        {
            AutomationIssueBlockCommand.UtcNowFactory = null;
        }
    }

    [Fact]
    public void Execute_GenuinelyQueuedUnit_RetainsExactJsonBytes_G574()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WritePacketDomain("G600", "intent-cli");
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-46)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            """
            {
              "domain": "intent-cli",
              "repo": "J-Tech-Japan/intent-system",
              "stale_minutes_threshold": 0,
              "backlog_idle_minutes_threshold": 45,
              "stalled": true,
              "items": [
                {
                  "kind": "backlog-ready-idle",
                  "execution_unit": "G600",
                  "issue": null,
                  "pr": null,
                  "age_minutes": 46,
                  "is_informational": false,
                  "recommended_action": "intent-cli issue publish-flow G600 --repo J-Tech-Japan/intent-system --write --format json",
                  "declared_write_back_targets": null
                }
              ],
              "excluded": [],
              "warnings": []
            }

            """,
            writer.ToString());
    }

    [Fact]
    public void Execute_Heartbeat_SurfacesBacklogReadyIdleInMessageBody()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WriteFile(".intent-cli/issues/G600/github-body.md", BuildCompleteContractBody());
        workspace.WriteQueueState(BuildReadyQueueStateJson("G600"));
        workspace.WriteFile(".intent-cli/runs.jsonl", BuildRunsLogLine("G599", FixedNow.AddMinutes(-46)));
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister();

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
        var messageBody = doc.RootElement.GetProperty("message_body").GetString();
        Assert.Contains(AutomationStalledWorkCommand.KindBacklogReadyIdle, messageBody, StringComparison.Ordinal);
        Assert.Contains("G600", messageBody, StringComparison.Ordinal);
        Assert.Contains("issue publish-flow", messageBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Heartbeat_SurfacesBlockedLabelDriftInMessageBody_G545()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("SKS-G818", "sekiban-as-a-service");
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-07-21T00:00:00Z",
              "items": [
                {
                  "execution_unit": "SKS-G818",
                  "title": "SKS-G818 title",
                  "state": "blocked",
                  "dependencies": [],
                  "blocked_by": ["SKS-G837"],
                  "clarification_return_path": "intents/sekiban-as-a-service/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """);
        var issue = BuildIssue(818, "SKS-G818: Queue-blocked but still marked in-progress on GitHub", FixedNow.AddHours(-25),
            updatedAt: FixedNow.AddHours(-25), labels: ["intent-target", "intent-issue-in-progress"]);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue]);

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "sekiban-as-a-service", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
        var messageBody = doc.RootElement.GetProperty("message_body").GetString();
        Assert.Contains(AutomationStalledWorkCommand.KindBlockedLabelDrift, messageBody, StringComparison.Ordinal);
        Assert.Contains("SKS-G818", messageBody, StringComparison.Ordinal);
        Assert.Contains("FYI:", messageBody, StringComparison.Ordinal);
        Assert.DoesNotContain(AutomationStalledWorkCommand.KindClaimedButSilent, messageBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Heartbeat_SurfacesRepairStalledAsActionableInMessageBody_G546()
    {
        // The four-day G545 shape again, this time through the heartbeat the
        // orchestrator actually reads: it must be counted as a PENDING
        // TRANSITION line (actionable), not an "FYI" note, and must name the
        // kind so a reader can route it.
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G545", "intent-cli");
        var issue = BuildIssue(1192, "G545: Exempt queue-blocked units from claimed-but-silent",
            FixedNow.AddDays(-5), "intent-pr-created");
        var pr = BuildPr(1193, "G545: Exempt queue-blocked units from claimed-but-silent",
            FixedNow.AddDays(-4), state: "OPEN", closingIssueNumber: 1192,
            extraLabels: ["intent-pr-update-in-progress"],
            updatedAt: FixedNow.AddDays(-4), isDraft: true);
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);

        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
        var messageBody = doc.RootElement.GetProperty("message_body").GetString()!;
        Assert.Contains(AutomationStalledWorkCommand.KindRepairStalled, messageBody, StringComparison.Ordinal);
        Assert.Contains("G545", messageBody, StringComparison.Ordinal);
        Assert.Contains("pr #1193", messageBody, StringComparison.Ordinal);
        Assert.Contains("1 pending transition(s)", messageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("FYI:", messageBody, StringComparison.Ordinal);
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

    private static string BuildReadyQueueStateJson(string executionUnit) => $$"""
        {
          "schema_version": "1",
          "updated_at": "2026-07-01T00:00:00Z",
          "items": [
            {
              "execution_unit": "{{executionUnit}}",
              "title": "{{executionUnit}} title",
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
        """;

    private static string BuildRunsLogLine(
        string executionUnit,
        DateTimeOffset ts,
        string eventName = "pr-merged-closeout",
        string? reason = null) =>
        RunLogSerializer.SerializeLine(new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = eventName,
            By = "intent-cli closeout pr",
            Reason = reason,
        }) + "\n";

    private static string BuildPrePublishQueueStateJson(
        string executionUnit,
        string state,
        IReadOnlyList<string> blockedBy)
    {
        var blockedByJson = JsonSerializer.Serialize(blockedBy);
        return $$"""
            {
              "schema_version": "1",
              "updated_at": "2026-07-01T00:00:00Z",
              "items": [
                {
                  "execution_unit": "{{executionUnit}}",
                  "title": "{{executionUnit}} title",
                  "state": "{{state}}",
                  "dependencies": [],
                  "blocked_by": {{blockedByJson}},
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                  "linked_issue": null,
                  "linked_pr": null,
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "normal"
                }
              ]
            }
            """;
    }

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number, string title, DateTimeOffset createdAt, params string[] labels) =>
        BuildIssue(number, title, createdAt, updatedAt: null, labels);

    /// <summary>G533: overload accepting an independent <c>updatedAt</c> for claimed-but-silent fixtures — defaults to <paramref name="createdAt"/> when omitted, matching the pre-G533 fixture shape.</summary>
    private static GitHubAutomationIssueCandidate BuildIssue(
        int number, string title, DateTimeOffset createdAt, DateTimeOffset? updatedAt, params string[] labels) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = createdAt.ToString("O"),
            UpdatedAt = (updatedAt ?? createdAt).ToString("O"),
            State = "OPEN",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
        };

    private static JsonDocument RunJson(
        StalledWorkWorkspace workspace,
        GitHubAutomationIssueCandidate issue,
        GitHubAutomationPrCandidate pr)
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister(issues: [issue], prs: [pr]);
        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--stale-minutes", "0",
                "--repair-silent-minutes", "0", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);
        return JsonDocument.Parse(writer.ToString());
    }

    private static GitHubAutomationStatusCheckCandidate CheckRun(string status, string conclusion = "") => new()
    {
        TypeName = "CheckRun",
        Status = status,
        Conclusion = conclusion,
    };

    private static GitHubAutomationStatusCheckCandidate StatusContext(string state) => new()
    {
        TypeName = "StatusContext",
        State = state,
    };

    private static void AssertCiBreakdown(
        JsonElement item,
        int passed,
        int failed,
        int skipped,
        int pending,
        int total)
    {
        var breakdown = item.GetProperty("ci_breakdown");
        Assert.Equal(passed, breakdown.GetProperty("passed").GetInt32());
        Assert.Equal(failed, breakdown.GetProperty("failed").GetInt32());
        Assert.Equal(skipped, breakdown.GetProperty("skipped").GetInt32());
        Assert.Equal(pending, breakdown.GetProperty("pending").GetInt32());
        Assert.Equal(total, breakdown.GetProperty("total").GetInt32());
    }

    private static IReadOnlyDictionary<string, string> SnapshotFiles(string rootPath) =>
        Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(rootPath, path),
                path => Convert.ToHexString(File.ReadAllBytes(path)),
                StringComparer.Ordinal);

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        DateTimeOffset createdAt,
        string state,
        int? closingIssueNumber = null,
        string[]? extraLabels = null,
        DateTimeOffset? updatedAt = null,
        bool isDraft = false,
        string headRefOid = "",
        IReadOnlyList<GitHubAutomationStatusCheckCandidate>? statusCheckRollup = null) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            CreatedAt = createdAt.ToString("O"),
            UpdatedAt = (updatedAt ?? createdAt).ToString("O"),
            State = state,
            IsDraft = isDraft,
            HeadRefOid = headRefOid,
            StatusCheckRollup = statusCheckRollup ?? Array.Empty<GitHubAutomationStatusCheckCandidate>(),
            Labels = (extraLabels ?? Array.Empty<string>()).Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
            ClosingIssuesReferences = closingIssueNumber is int n
                ? new[]
                {
                    new GitHubPrClosingIssueReference
                    {
                        Number = n,
                        Repository = new GitHubPrClosingIssueRepository
                        {
                            Name = "intent-system",
                            Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                        },
                    },
                }
                : Array.Empty<GitHubPrClosingIssueReference>(),
        };

    private static string BuildQueueStateJson(
        string executionUnit,
        QueueItemState state,
        string linkedPr,
        int? linkedIssueNumber,
        string linkedIssueRepo = "J-Tech-Japan/intent-system") =>
        BuildQueueStateJson(BuildQueueItem(executionUnit, state, linkedPr, linkedIssueNumber, linkedIssueRepo));

    /// <summary>
    /// G532 review repair: overload accepting multiple queue items, so a
    /// fixture can pin the "two+ active queue items reference the same
    /// merged PR" ambiguity — the prior FirstOrDefault selection picked
    /// whichever item happened to be first in JSON order.
    /// </summary>
    private static string BuildQueueStateJson(params QueueItem[] items)
    {
        var queueState = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = FixedNow,
            Items = items,
        };
        return QueueStateSerializer.Serialize(queueState);
    }

    private static QueueItem BuildQueueItem(
        string executionUnit,
        QueueItemState state,
        string linkedPr,
        int? linkedIssueNumber,
        string linkedIssueRepo = "J-Tech-Japan/intent-system") => new()
        {
            ExecutionUnit = executionUnit,
            Title = $"{executionUnit} title",
            State = state,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
            },
            // G532 review repair: nullable, so a fixture can pin "no
            // linked_issue at all" — the queue-linkage corroboration check
            // must fail closed for that case.
            LinkedIssue = linkedIssueNumber is int number
                ? new LinkedIssue
                {
                    Repo = linkedIssueRepo,
                    Number = number,
                    Url = $"https://github.com/{linkedIssueRepo}/issues/{number}",
                }
                : null,
            LinkedPr = linkedPr,
            WorkerRole = "Claude",
            ReviewRole = "Codex",
            Priority = "normal",
        };

    private sealed class FakeLister : IGitHubAutomationCandidateLister
    {
        private readonly IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> prs;
        private readonly IReadOnlyList<GitHubAutomationPrCandidate> mergedPrs;

        public FakeLister(
            IReadOnlyList<GitHubAutomationIssueCandidate>? issues = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? prs = null,
            IReadOnlyList<GitHubAutomationPrCandidate>? mergedPrs = null)
        {
            this.issues = issues ?? Array.Empty<GitHubAutomationIssueCandidate>();
            this.prs = prs ?? Array.Empty<GitHubAutomationPrCandidate>();
            this.mergedPrs = mergedPrs ?? Array.Empty<GitHubAutomationPrCandidate>();
        }

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => mergedPrs;
    }

    private sealed class StalledWorkWorkspace : IDisposable
    {
        public StalledWorkWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("stalled-work-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public void WriteQueueState(string json) => File.WriteAllText(Context.GetQueueStatePath(), json);

        /// <summary>G544: writes an arbitrary file (e.g. a github-body.md contract, or runs.jsonl) relative to the workspace root, creating parent directories as needed.</summary>
        public void WriteFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        /// <summary>
        /// G522/G523: write a minimal packet.yaml declaring `domain:` for a
        /// candidate execution unit, so <c>automation stalled-work</c> can
        /// confirm that candidate's domain from its own packet metadata.
        /// </summary>
        public void WritePacketDomain(string executionUnit, string domain)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "packet.yaml"), $"domain: {domain}\n");
        }

        /// <summary>
        /// G552 repair: writes everything the REAL <c>clarify open</c> needs —
        /// a queue item, its packet.yaml, and its review-context.md — so the
        /// canonical flow can be driven end to end instead of hand-serializing
        /// an artifact.
        /// </summary>
        public void WriteClarifiablePacket(string executionUnit)
        {
            WriteQueueState($$"""
                {
                  "schema_version": "1",
                  "updated_at": "2026-07-14T11:00:00+00:00",
                  "items": [
                    {
                      "execution_unit": "{{executionUnit}}",
                      "title": "[{{executionUnit}}] Design-decision hold",
                      "state": "review",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                      "packet_paths": {
                        "implementation": ".intent-cli/issues/{{executionUnit}}/implementation.md",
                        "review_context": ".intent-cli/issues/{{executionUnit}}/review-context.md",
                        "yaml": ".intent-cli/issues/{{executionUnit}}/packet.yaml"
                      },
                      "worker_role": "coder",
                      "review_role": "reviewer",
                      "priority": "normal"
                    }
                  ]
                }
                """);

            WriteFile($".intent-cli/issues/{executionUnit}/packet.yaml", $$"""
                implementation_issue_packet:
                  issue_title: "[{{executionUnit}}] Design-decision hold"
                  issue_kind: "feature"
                  source_execution_unit: "{{executionUnit}}"
                  goal: "Record a design-decision hold."
                  domain: "intent-cli"
                  in_scope:
                    - "clarification-backed holds"
                  out_of_scope:
                    - "schema changes"
                  target_repo: "J-Tech-Japan/intent-system"
                  target_path: "."
                  target_part: "clarify open"
                  dependencies: []
                  technical_baseline:
                    - "C# / .NET"
                  project_local_guide:
                    - "AGENTS.md"
                  intent_baseline:
                    - "design absence must not invisibly block"
                  intent_references:
                    - "ICL.P.PRODUCT_GOAL"
                  rules_and_specs:
                    - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
                  acceptance_criteria:
                    - "clarification artifact generated"
                  verification_evidence:
                    - "dotnet test IntentSystem.sln"
                  review_mode: "deterministic-review"
                  completion_action: "wait-for-deterministic-review"
                  landing_policy: "merge-after-review"

                review_context_packet:
                  source_execution_unit: "{{executionUnit}}"
                  parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
                  intent_references:
                    - "ICL.P.PRODUCT_GOAL"
                  rules_and_specs:
                    - "intents/intent-cli/specs/06-interview-and-clarification-artifact-contract.md"
                  acceptance_criteria:
                    - "clarification artifact generated"
                  deterministic_review_checks:
                    - "clarify open stays entry-only"
                  clarification_return_path: "intents/intent-cli/clarifications/open.md"
                """);

            WriteFile($".intent-cli/issues/{executionUnit}/review-context.md", $$"""
                # Execution Unit

                `{{executionUnit}}`

                # Acceptance Criteria

                - clarification artifact generated

                # Deterministic Review Checks

                - clarify open stays entry-only
                """);
        }

        /// <summary>
        /// G552: writes a clarification artifact exactly where the canonical
        /// clarify surface puts one
        /// (<c>.intent-cli/clarifications/&lt;execution-unit&gt;/request.json</c>),
        /// so <c>design-decision-pending</c> reads a real artifact shape
        /// rather than a test-only stand-in.
        /// </summary>
        public void WriteClarification(
            string executionUnit,
            string questionText,
            DateTimeOffset createdAt,
            ClarificationStatus status = ClarificationStatus.Open,
            string questionId = "request")
        {
            var item = new ClarificationItem
            {
                ClarificationSource = "execution",
                QuestionId = questionId,
                ExecutionUnit = executionUnit,
                QuestionText = questionText,
                Reason = "blocked on a design decision",
                AffectedIntents = [],
                AffectedExecutionUnits = [executionUnit],
                BlockingOrNonblocking = "blocking",
                ClarificationReturnPath = $".intent-cli/clarifications/{executionUnit}/",
                Status = status,
                CreatedAt = createdAt,
                Answer = status == ClarificationStatus.Open ? null : "answered",
                AnsweredAt = status == ClarificationStatus.Open ? null : createdAt.AddMinutes(1),
            };

            WriteFile(
                $".intent-cli/clarifications/{executionUnit}/request.json",
                ClarificationSerializer.Serialize(item));
        }

        /// <summary>
        /// G532: writes a packet.yaml declaring `domain:` and
        /// `source_execution_unit:` ONLY under the nested
        /// `implementation_issue_packet:` section — no top-level alias —
        /// so a test can pin the nested-first-class derivation contract
        /// (SKS-G823 regression) and the source_execution_unit fallback
        /// match independently of the top-level alias path.
        /// </summary>
        public void WriteNestedPacket(string executionUnit, string domain)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "packet.yaml"),
                $"implementation_issue_packet:\n  source_execution_unit: {executionUnit}\n  domain: {domain}\n");
        }

        /// <summary>
        /// G532 review repair: writes a packet.yaml at an arbitrary FOLDER
        /// name declaring an arbitrary source_execution_unit — used to pin
        /// that two distinct packet FILES declaring the identical unit
        /// value are still ambiguous (never collapsed by string equality),
        /// e.g. a duplicate declaration across two folders.
        /// </summary>
        public void WriteNestedPacketAtFolder(string folderName, string declaredExecutionUnit, string domain)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", folderName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "packet.yaml"),
                $"implementation_issue_packet:\n  source_execution_unit: {declaredExecutionUnit}\n  domain: {domain}\n");
        }

        /// <summary>
        /// G532: writes a packet.yaml whose top-level `domain:` disagrees
        /// with its nested `implementation_issue_packet.domain` — used to
        /// pin that the nested field takes priority over the top-level
        /// alias, not just "whichever appears first in the file".
        /// </summary>
        public void WritePacketWithDisagreeingTopLevelAndNestedDomain(
            string executionUnit, string topLevelDomain, string nestedDomain)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "packet.yaml"),
                $"domain: {topLevelDomain}\nimplementation_issue_packet:\n  source_execution_unit: {executionUnit}\n  domain: {nestedDomain}\n");
        }

        /// <summary>
        /// G532 review repair: writes a packet.yaml that identifies the
        /// execution unit (so it IS corroborated — the folder exists and
        /// the packet is readable) but declares NO domain field anywhere,
        /// neither nested nor top-level. This is the one case an explicit
        /// --domain is still allowed to rescue: packet/queue linkage
        /// identifies the candidate, it is simply silent on domain.
        /// </summary>
        public void WritePacketWithNoDomainField(string executionUnit)
        {
            var dir = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "packet.yaml"),
                $"implementation_issue_packet:\n  source_execution_unit: {executionUnit}\n  target_repo: J-Tech-Japan/intent-system\n");
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
