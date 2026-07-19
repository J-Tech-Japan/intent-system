using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
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
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G524", "intent-cli");
        var claimedIssue = BuildIssue(1148, "G524: Something else", FixedNow.AddHours(-26),
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

    [Fact]
    public void Execute_MergedNotClosedOut_FiresWhenQueueItemNotCompleted()
    {
        using var workspace = new StalledWorkWorkspace();
        workspace.WritePacketDomain("G500", "intent-cli");
        workspace.WriteQueueState(BuildQueueStateJson("G500", QueueItemState.Review,
            linkedPr: "https://github.com/J-Tech-Japan/intent-system/pull/1200",
            linkedIssueNumber: 1199));
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
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
        var mergedPr = BuildPr(1300, "SKS-G700: Some other domain's merged change", FixedNow.AddHours(-3), state: "MERGED");
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
        var mergedPr = BuildPr(1200, "G500: Some merged change", FixedNow.AddHours(-3), state: "MERGED");
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

    private static GitHubAutomationIssueCandidate BuildIssue(
        int number, string title, DateTimeOffset createdAt, params string[] labels) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
            CreatedAt = createdAt.ToString("O"),
            State = "OPEN",
            Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
        };

    private static GitHubAutomationPrCandidate BuildPr(
        int number,
        string title,
        DateTimeOffset createdAt,
        string state,
        int? closingIssueNumber = null,
        string[]? extraLabels = null) => new()
        {
            Number = number,
            Title = title,
            Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
            CreatedAt = createdAt.ToString("O"),
            UpdatedAt = createdAt.ToString("O"),
            State = state,
            IsDraft = false,
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

    private static string BuildQueueStateJson(string executionUnit, QueueItemState state, string linkedPr, int linkedIssueNumber)
    {
        var queueState = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = FixedNow,
            Items = new[]
            {
                new QueueItem
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
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = linkedIssueNumber,
                        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{linkedIssueNumber}",
                    },
                    LinkedPr = linkedPr,
                    WorkerRole = "Claude",
                    ReviewRole = "Codex",
                    Priority = "normal",
                },
            },
        };
        return QueueStateSerializer.Serialize(queueState);
    }

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
