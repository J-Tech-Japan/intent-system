using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed partial class StalledWorkAdoptionWindowG858Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly Func<IGitHubAutomationCandidateLister>? oldCandidateLister = AutomationStalledWorkCommand.CandidateListerFactory;
    private readonly Func<DateTimeOffset>? oldStalledClock = AutomationStalledWorkCommand.UtcNowFactory;
    private readonly Func<IGitRemoteCommandRunner>? oldStalledGit = AutomationStalledWorkCommand.GitCommandRunnerFactory;
    private readonly PacketYamlParseWarningTracker? oldPacketWarningTracker = AutomationStalledWorkCommand.PacketParseWarningTracker;
    private readonly Func<DateTimeOffset>? oldModeClock = TeamModeCommand.UtcNowFactory;
    private readonly Func<IIssueCreator>? oldFlowCreator = IssuePublishFlowCommand.CreatorFactory;
    private readonly Func<DateTimeOffset>? oldFlowClock = IssuePublishFlowCommand.UtcNowFactory;
    private readonly Func<IGitHubExistingIssueChecker>? oldFlowIssueChecker = IssuePublishFlowCommand.ExistingIssueCheckerFactory;
    private readonly Action? oldFlowAfterGate = IssuePublishFlowCommand.AfterGateHook;
    private readonly Action? oldFlowBeforeLookup = IssuePublishFlowCommand.BeforeLookupSnapshotHook;
    private readonly Func<IQueueDispatchPublisher> oldIssuePublishPublisher = IssuePublishCommand.PublisherFactory;
    private readonly Func<IGitRemoteCommandRunner> oldIssuePublishGit = IssuePublishCommand.GitCommandRunnerFactory;
    private readonly Func<DateTimeOffset> oldIssuePublishClock = IssuePublishCommand.TimestampFactory;
    private readonly Func<IQueueDispatchPublisher> oldIssueCreatePublisher = IssueCreateCommand.PublisherFactory;
    private readonly Func<IGitRemoteCommandRunner> oldIssueCreateGit = IssueCreateCommand.GitCommandRunnerFactory;
    private readonly Func<DateTimeOffset> oldIssueCreateClock = IssueCreateCommand.TimestampFactory;
    private readonly Func<DateTimeOffset>? oldCloseoutClock = CloseoutPrCommand.UtcNowFactory;
    private readonly Func<IPrClosingIssuesFetcher>? oldCloseoutFetcher = CloseoutPrCommand.PrClosingIssuesFetcherFactory;
    private readonly Func<DateTimeOffset>? oldWritebackClock = AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory;

    public StalledWorkAdoptionWindowG858Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();
        AutomationStalledWorkCommand.UtcNowFactory = () => Now;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
        TeamModeCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.CreatorFactory = null;
        IssuePublishFlowCommand.UtcNowFactory = null;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = () => new ExistingIssueNone();
        IssuePublishFlowCommand.AfterGateHook = null;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = null;
        IssuePublishCommand.PublisherFactory = () => new FakePublisher();
        IssuePublishCommand.GitCommandRunnerFactory = () => new FakeGitRunner();
        IssueCreateCommand.PublisherFactory = () => new FakePublisher();
        IssueCreateCommand.GitCommandRunnerFactory = () => new FakeGitRunner();
        CloseoutPrCommand.UtcNowFactory = null;
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = () => new EmptyClosingIssuesFetcher();
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = null;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = oldCandidateLister;
        AutomationStalledWorkCommand.UtcNowFactory = oldStalledClock;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = oldStalledGit;
        AutomationStalledWorkCommand.PacketParseWarningTracker = oldPacketWarningTracker;
        TeamModeCommand.UtcNowFactory = oldModeClock;
        IssuePublishFlowCommand.CreatorFactory = oldFlowCreator;
        IssuePublishFlowCommand.UtcNowFactory = oldFlowClock;
        IssuePublishFlowCommand.ExistingIssueCheckerFactory = oldFlowIssueChecker;
        IssuePublishFlowCommand.AfterGateHook = oldFlowAfterGate;
        IssuePublishFlowCommand.BeforeLookupSnapshotHook = oldFlowBeforeLookup;
        IssuePublishCommand.PublisherFactory = oldIssuePublishPublisher;
        IssuePublishCommand.GitCommandRunnerFactory = oldIssuePublishGit;
        IssuePublishCommand.TimestampFactory = oldIssuePublishClock;
        IssueCreateCommand.PublisherFactory = oldIssueCreatePublisher;
        IssueCreateCommand.GitCommandRunnerFactory = oldIssueCreateGit;
        IssueCreateCommand.TimestampFactory = oldIssueCreateClock;
        CloseoutPrCommand.UtcNowFactory = oldCloseoutClock;
        CloseoutPrCommand.PrClosingIssuesFetcherFactory = oldCloseoutFetcher;
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = oldWritebackClock;
    }

    [Fact]
    public void V01_V06_ActualModePublishFlowAndCloseoutWritersUseEarliestStart()
    {
        using var workspace = new AdoptionWorkspace();
        var adoption = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var oldStart = adoption.AddDays(-2);
        var newStart = adoption.AddDays(1);
        const string oldUnit = "G858-old";
        const string newUnit = "G858-new";
        workspace.WriteDebtPacket(oldUnit);
        workspace.WriteDebtPacket(newUnit);
        workspace.WriteQueue([oldUnit, newUnit]);

        PublishFlow(workspace, oldUnit, 1850, oldStart);
        PublishFlow(workspace, newUnit, 1851, newStart);

        var beforeCloseout = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunLogPath));
        var oldCreated = Assert.Single(beforeCloseout, item => item.ExecutionUnit == oldUnit && item.Event == "issue-created");
        Assert.Equal("J-Tech-Japan/intent-system#1850", oldCreated.LinkedIssue);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/1850", oldCreated.Reason);
        workspace.AttachPrsAndSetReviewState([oldUnit, newUnit], [1950, 1951]);
        WriteCloseout(workspace, oldUnit, 1950, adoption.AddDays(3));
        WriteCloseout(workspace, newUnit, 1951, adoption.AddDays(4));
        var closeoutEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunLogPath))
            .Where(item => item.Event == "closeout-recorded")
            .ToArray();
        Assert.Equal(2, closeoutEvents.Length);
        Assert.All(closeoutEvents, item => Assert.Equal(Repo, item.Repo));
        Assert.Contains(closeoutEvents, item => item.ExecutionUnit == oldUnit && item.Pr == 1950 && item.Ts == adoption.AddDays(3));

        // Record solo after generating real lifecycle events. The stored T is
        // the adoption boundary; the order of fixture construction is not.
        SetMode(workspace.Context, TeamMode.SoloConductor, adoption);
        var result = Report(workspace.Context, now: adoption.AddDays(30));

        Assert.Equal(new[] { oldUnit }, result.RootElement.GetProperty("excluded").EnumerateArray()
            .Where(item => item.GetProperty("reason").GetString() == "debt-window-historical")
            .Select(item => item.GetProperty("execution_unit").GetString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(new[] { newUnit }, result.RootElement.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                or AutomationStalledWorkCommand.KindGuideReachabilityPending)
            .Select(item => item.GetProperty("execution_unit").GetString())
            .Distinct(StringComparer.Ordinal));
        Assert.Equal(2, result.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("execution_unit").GetString() == newUnit));
        var oldExclusions = result.RootElement.GetProperty("excluded").EnumerateArray()
            .Where(item => item.GetProperty("execution_unit").GetString() == oldUnit
                && item.GetProperty("reason").GetString() == "debt-window-historical")
            .ToArray();
        Assert.Equal(2, oldExclusions.Length);
        Assert.All(oldExclusions, item =>
        {
            Assert.Equal("issue-created", item.GetProperty("debt_window_evidence_kind").GetString());
            Assert.Equal(oldStart, item.GetProperty("debt_window_evidence_at").GetDateTimeOffset());
            Assert.Equal(adoption, item.GetProperty("debt_window_cutoff").GetDateTimeOffset());
            Assert.Contains("runs.jsonl", item.GetProperty("debt_window_evidence_path").GetString(), StringComparison.Ordinal);
        });
        var counts = result.RootElement.GetProperty("debt_window").GetProperty("decision_counts");
        Assert.Equal(2, counts.GetProperty("candidate_units").GetInt32());
        Assert.Equal(1, counts.GetProperty("historical_units").GetInt32());
        Assert.Equal(1, counts.GetProperty("included_units").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("debt_window")
            .GetProperty("knowledge_writeback_closeout_cutoff").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("debt_window")
            .GetProperty("guide_reachability_closeout_cutoff").ValueKind);
    }

    [Fact]
    public void V02_V03_CloseoutOnlyAndUtcInclusiveBoundariesAreConservative()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var equivalentOffset = cutoff.ToOffset(TimeSpan.FromHours(5));
        workspace.WriteDebtPacket("G858-equal-z");
        workspace.WriteDebtPacket("G858-equal-offset");
        workspace.WriteDebtPacket("G858-before");
        workspace.WriteDebtPacket("G858-closeout-old");
        workspace.WriteDebtPacket("G858-closeout-new");
        workspace.WriteCloseoutOnly("G858-equal-z", cutoff.AddDays(1), 2001);
        workspace.WriteCloseoutOnly("G858-equal-offset", cutoff.AddDays(1), 2002);
        workspace.WriteCloseoutOnly("G858-before", cutoff.AddDays(1), 2003);
        workspace.WriteCloseoutOnly("G858-closeout-old", cutoff.AddDays(-1), 2004, linkedPr: "#2004");
        workspace.WriteCloseoutOnly("G858-closeout-new", cutoff.AddDays(1), 2005);
        workspace.AppendEvent(IssueEvent("G858-equal-z", cutoff, "J-Tech-Japan/intent-system#1",
            "created https://github.com/J-Tech-Japan/intent-system/issues/1"));
        workspace.AppendEvent(IssueEvent("G858-equal-offset", equivalentOffset, "J-Tech-Japan/intent-system#2",
            "created https://github.com/J-Tech-Japan/intent-system/issues/2"));
        workspace.AppendEvent(IssueEvent("G858-before", cutoff.AddTicks(-1), "J-Tech-Japan/intent-system#3",
            "created https://github.com/J-Tech-Japan/intent-system/issues/3"));

        var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(10));
        var historical = result.RootElement.GetProperty("excluded").EnumerateArray()
            .Where(item => item.GetProperty("reason").GetString() == "debt-window-historical")
            .ToArray();
        Assert.True(historical.Length == 4, JsonSerializer.Serialize(result.RootElement.GetProperty("excluded")));
        Assert.All(historical.Where(item => item.GetProperty("execution_unit").GetString() == "G858-before"), item =>
        {
            Assert.Equal("issue-created", item.GetProperty("debt_window_evidence_kind").GetString());
            Assert.Equal(cutoff.AddTicks(-1), item.GetProperty("debt_window_evidence_at").GetDateTimeOffset());
        });
        Assert.All(historical.Where(item => item.GetProperty("execution_unit").GetString() == "G858-closeout-old"), item =>
        {
            Assert.Equal("historical-existence", item.GetProperty("debt_window_evidence_kind").GetString());
            Assert.Equal(cutoff.AddDays(-1), item.GetProperty("debt_window_evidence_at").GetDateTimeOffset());
        });
        Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-closeout-new"
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-closeout-new"
            && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("G858-closeout-new", StringComparison.Ordinal)
            && warning.GetString()!.Contains("debt-window-start-unknown", StringComparison.Ordinal));
        var counts = result.RootElement.GetProperty("debt_window").GetProperty("decision_counts");
        Assert.Equal(5, counts.GetProperty("candidate_units").GetInt32());
        Assert.Equal(2, counts.GetProperty("included_units").GetInt32());
        Assert.Equal(2, counts.GetProperty("historical_units").GetInt32());
        Assert.Equal(1, counts.GetProperty("unknown_units").GetInt32());
    }

    [Fact]
    public void V06_SameModeIsANoopAndReentryKeepsFirstSoloTransition()
    {
        using var workspace = new AdoptionWorkspace();
        var first = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var repeated = first.AddDays(2);
        var delivery = first.AddDays(4);
        var reentry = first.AddDays(8);
        SetMode(workspace.Context, TeamMode.SoloConductor, first);
        var firstBytes = File.ReadAllBytes(TeamModeStore.ResolvePath(workspace.Root));
        SetMode(workspace.Context, TeamMode.SoloConductor, repeated);
        Assert.Equal(firstBytes, File.ReadAllBytes(TeamModeStore.ResolvePath(workspace.Root)));

        SetMode(workspace.Context, TeamMode.Delivery, delivery);
        var deliveryReport = Report(workspace.Context, now: reentry.AddDays(2));
        Assert.False(deliveryReport.RootElement.TryGetProperty("debt_window", out _));
        SetMode(workspace.Context, TeamMode.SoloConductor, reentry);
        var entry = TeamModeStore.TryRead(workspace.Root)!.Entries.Single();
        Assert.Equal(3, entry.Transitions.Count);
        Assert.Equal(first, entry.Transitions.First(item => item.To == TeamMode.SoloConductor).At);
        Assert.Equal(TeamMode.Delivery, entry.Transitions[1].To);
        Assert.Equal(TeamMode.SoloConductor, entry.Transitions[2].To);

        workspace.WriteDebtPacket("G858-reentry");
        workspace.WriteCloseoutOnly("G858-reentry", reentry.AddDays(1), 2010);
        workspace.AppendEvent(IssueEvent("G858-reentry", first.AddDays(-1), "J-Tech-Japan/intent-system#10",
            "https://github.com/J-Tech-Japan/intent-system/issues/10"));
        var soloReport = Report(workspace.Context, now: reentry.AddDays(2));
        Assert.Equal(first, soloReport.RootElement.GetProperty("debt_window").GetProperty("cutoff").GetDateTimeOffset());
        Assert.True(soloReport.RootElement.GetProperty("excluded").EnumerateArray().Any(item =>
            item.GetProperty("execution_unit").GetString() == "G858-reentry"
            && item.GetProperty("reason").GetString() == "debt-window-historical"),
            JsonSerializer.Serialize(soloReport.RootElement));
    }

    [Fact]
    public void V07_ExplicitSinceOverridesModeAndLaneCutoffsRemainIntersections()
    {
        using var workspace = new AdoptionWorkspace();
        var since = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var start = since.AddDays(1);
        var closeout = since.AddDays(2);
        const string unit = "G858-lane-cutoffs";
        workspace.WriteDebtPacket(unit);
        workspace.AppendEvent(IssueEvent(unit, start, "J-Tech-Japan/intent-system#20",
            "https://github.com/J-Tech-Japan/intent-system/issues/20"));
        workspace.WriteCloseoutOnly(unit, closeout, 2020);
        SetMode(workspace.Context, TeamMode.Delivery, since.AddDays(-2));

        var noLaneOverride = Report(workspace.Context, ["--since", since.ToString("O")], now: since.AddDays(15));
        Assert.Equal(2, noLaneOverride.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                    or AutomationStalledWorkCommand.KindGuideReachabilityPending));
        Assert.Equal("explicit-since", noLaneOverride.RootElement.GetProperty("debt_window").GetProperty("policy").GetString());
        Assert.Equal(JsonValueKind.Null, noLaneOverride.RootElement.GetProperty("debt_window")
            .GetProperty("knowledge_writeback_closeout_cutoff").ValueKind);

        var oneLane = Report(workspace.Context,
            ["--since", since.ToString("O"), "--knowledge-writeback-since", closeout.AddTicks(1).ToString("O"),
             "--guide-reachability-since", closeout.AddDays(-1).ToString("O")], now: since.AddDays(15));
        Assert.DoesNotContain(oneLane.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.True(oneLane.RootElement.GetProperty("items").EnumerateArray().Any(item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindGuideReachabilityPending),
            JsonSerializer.Serialize(oneLane.RootElement));
        Assert.Equal(closeout.AddTicks(1), oneLane.RootElement.GetProperty("debt_window")
            .GetProperty("knowledge_writeback_closeout_cutoff").GetDateTimeOffset());

        var bothLanes = Report(workspace.Context,
            ["--since", since.ToString("O"), "--knowledge-writeback-since", closeout.AddTicks(1).ToString("O"),
             "--guide-reachability-since", closeout.AddTicks(1).ToString("O")], now: since.AddDays(15));
        Assert.DoesNotContain(bothLanes.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                or AutomationStalledWorkCommand.KindGuideReachabilityPending);

        var future = since.AddYears(1);
        var futureExplicit = Report(workspace.Context, ["--since", future.ToString("O")], now: since.AddDays(15));
        Assert.Equal(future, futureExplicit.RootElement.GetProperty("debt_window").GetProperty("cutoff").GetDateTimeOffset());
        using var invalidWriter = new StringWriter();
        Assert.Equal(1, AutomationStalledWorkCommand.Execute(workspace.Context,
            ["--domain", Domain, "--repo", Repo, "--since", "tomorrow"], invalidWriter));
        Assert.Contains("--since requires an ISO-8601", invalidWriter.ToString(), StringComparison.Ordinal);

        SetMode(workspace.Context, TeamMode.AuthoringOnly, since.AddDays(3));
        var authoringOverride = Report(workspace.Context, ["--since", since.ToString("O")], now: since.AddDays(15));
        Assert.Equal("explicit-since", authoringOverride.RootElement.GetProperty("debt_window").GetProperty("policy").GetString());
    }

    [Fact]
    public void V10_ContradictoryAndMalformedIdentitiesStayVisibleWithoutHistoricalSuppression()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var old = cutoff.AddDays(-1);
        var closed = cutoff.AddDays(1);
        var badUnits = new[]
        {
            "G858-reason-number-conflict", "G858-run-repo-conflict", "G858-malformed-linked-url",
            "G858-same-number-foreign", "G858-bare-number-only",
            "G858-source-unit-mismatch", "G858-duplicate-repo-conflict", "G858-closeout-pr-conflict",
            "G858-closeout-bare-linked-pr-conflict", "G858-closeout-malformed-linked-pr",
            "G858-closeout-bare-linked-pr-no-repo",
        };
        foreach (var unit in badUnits)
        {
            workspace.WriteDebtPacket(unit, assertedUnit: unit == "G858-source-unit-mismatch" ? "G999" : unit,
                includeRootRepo: unit == "G858-duplicate-repo-conflict");
            workspace.WriteCloseoutOnly(unit, closed, 2050 + Array.IndexOf(badUnits, unit));
        }
        workspace.AppendEvent(IssueEvent("G858-reason-number-conflict", old, "#123",
            "https://github.com/J-Tech-Japan/intent-system/issues/456"));
        workspace.AppendEvent(IssueEvent("G858-run-repo-conflict", old, "J-Tech-Japan/intent-system#124",
            "https://github.com/J-Tech-Japan/intent-system/issues/124", repo: "other-org/other-repo"));
        workspace.AppendEvent(IssueEvent("G858-malformed-linked-url", old,
            "garbage https://github.com/J-Tech-Japan/intent-system/issues/125 trailing",
            "created https://github.com/J-Tech-Japan/intent-system/issues/125"));
        workspace.AppendEvent(IssueEvent("G858-same-number-foreign", old, "other-org/other-repo#126",
            "https://github.com/other-org/other-repo/issues/126", repo: "other-org/other-repo"));
        workspace.AppendEvent(IssueEvent("G858-bare-number-only", old, "#127", null));
        workspace.WriteDebtPacket("G858-metadata-alias", includeMetadataRepo: true);
        workspace.WriteCloseoutOnly("G858-metadata-alias", closed, 2059);
        workspace.AppendEvent(IssueEvent("G858-metadata-alias", old, "J-Tech-Japan/intent-system#128",
            "https://github.com/J-Tech-Japan/intent-system/issues/128"));
        workspace.AppendEvent(IssueEvent("G858-source-unit-mismatch", old, "J-Tech-Japan/intent-system#129",
            "https://github.com/J-Tech-Japan/intent-system/issues/129"));
        workspace.AppendEvent(IssueEvent("G858-duplicate-repo-conflict", old, "J-Tech-Japan/intent-system#130",
            "https://github.com/J-Tech-Japan/intent-system/issues/130"));
        workspace.AppendEvent(new RunEvent
        {
            Ts = old,
            ExecutionUnit = "G858-closeout-pr-conflict",
            Event = "closeout-recorded",
            By = "fixture",
            Repo = Repo,
            Pr = 2058,
            LinkedPr = "https://github.com/J-Tech-Japan/intent-system/pull/2059",
        });
        workspace.AppendEvent(new RunEvent
        {
            Ts = old,
            ExecutionUnit = "G858-closeout-bare-linked-pr-conflict",
            Event = "closeout-recorded",
            By = "fixture",
            Repo = Repo,
            Pr = 2060,
            LinkedPr = "#2061",
        });
        workspace.AppendEvent(new RunEvent
        {
            Ts = old,
            ExecutionUnit = "G858-closeout-malformed-linked-pr",
            Event = "closeout-recorded",
            By = "fixture",
            Repo = Repo,
            Pr = 2062,
            LinkedPr = "extra text https://github.com/J-Tech-Japan/intent-system/pull/2062 trailing text",
        });
        workspace.AppendEvent(new RunEvent
        {
            Ts = old,
            ExecutionUnit = "G858-closeout-bare-linked-pr-no-repo",
            Event = "closeout-recorded",
            By = "fixture",
            Pr = 2063,
            LinkedPr = "#2063",
        });

        const string positiveBareLinkedPrUnit = "G858-closeout-bare-linked-pr-valid";
        workspace.WriteDebtPacket(positiveBareLinkedPrUnit);
        workspace.AppendEvent(new RunEvent
        {
            Ts = old,
            ExecutionUnit = positiveBareLinkedPrUnit,
            Event = "closeout-recorded",
            By = "fixture",
            Repo = Repo,
            Pr = 2064,
            LinkedPr = "#2064",
        });

        var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(5));
        foreach (var unit in badUnits)
        {
            Assert.True(result.RootElement.GetProperty("items").EnumerateArray().Any(item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending),
                $"{unit}: items={JsonSerializer.Serialize(result.RootElement.GetProperty("items"))}; "
                + $"excluded={JsonSerializer.Serialize(result.RootElement.GetProperty("excluded"))}; "
                + $"warnings={JsonSerializer.Serialize(result.RootElement.GetProperty("warnings"))}");
            Assert.DoesNotContain(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("reason").GetString() == "debt-window-historical");
        }
        Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), item =>
            item.GetString()!.Contains("G858-reason-number-conflict", StringComparison.Ordinal));
        Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), item =>
            item.GetString()!.Contains("G858-same-number-foreign", StringComparison.Ordinal));
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-metadata-alias"
            && item.GetProperty("reason").GetString() == "debt-window-historical"
            && item.GetProperty("debt_window_evidence_kind").GetString() == "issue-created");
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == positiveBareLinkedPrUnit
            && item.GetProperty("reason").GetString() == "debt-window-historical"
            && item.GetProperty("debt_window_evidence_kind").GetString() == "historical-existence");
    }

    [Fact]
    public void V12_NoObligationAndSatisfiedRoleStayOutOfDebtWindowDecisionCounts()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        workspace.WriteDebtPacket("G858-not-applicable", knowledgeRequired: false, guideRoute: false);
        workspace.WriteDebtPacket("G858-satisfied", guideRoute: false);
        workspace.WriteDebtPacket("G858-uncommitted-old", guideRoute: false);
        workspace.WriteDebtPacket("G858-uncommitted-new", guideRoute: false);
        workspace.AppendEvent(IssueEvent("G858-not-applicable", cutoff.AddDays(1), "J-Tech-Japan/intent-system#301",
            "https://github.com/J-Tech-Japan/intent-system/issues/301"));
        workspace.AppendEvent(IssueEvent("G858-satisfied", cutoff.AddDays(1), "J-Tech-Japan/intent-system#302",
            "https://github.com/J-Tech-Japan/intent-system/issues/302"));
        workspace.AppendEvent(IssueEvent("G858-uncommitted-old", cutoff.AddTicks(-1), "J-Tech-Japan/intent-system#303",
            "https://github.com/J-Tech-Japan/intent-system/issues/303"));
        workspace.AppendEvent(IssueEvent("G858-uncommitted-new", cutoff.AddDays(1), "J-Tech-Japan/intent-system#304",
            "https://github.com/J-Tech-Japan/intent-system/issues/304"));
        workspace.WriteCloseoutOnly("G858-not-applicable", cutoff.AddDays(2), 2301);
        workspace.WriteCloseoutOnly("G858-satisfied", cutoff.AddDays(2), 2302);
        workspace.WriteCloseoutOnly("G858-uncommitted-old", cutoff.AddDays(2), 2303);
        workspace.WriteCloseoutOnly("G858-uncommitted-new", cutoff.AddDays(2), 2304);
        workspace.InitializeGit();
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = () => cutoff.AddDays(3);
        RecordWriteback(workspace.Context, "G858-satisfied", cutoff.AddDays(3));
        workspace.CommitPath(RoleScopedCloseoutRecordStore.ResolveRoleRelativePath(
            KnowledgeWriteBackRecord.RecordRootRelativePath, "G858-satisfied", "design"));
        RecordWriteback(workspace.Context, "G858-uncommitted-old", cutoff.AddDays(4));
        RecordWriteback(workspace.Context, "G858-uncommitted-new", cutoff.AddDays(4));

        var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(10));
        Assert.DoesNotContain(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() is "G858-not-applicable" or "G858-satisfied"
            && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                or AutomationStalledWorkCommand.KindKnowledgeWritebackRecordedUncommitted
                or AutomationStalledWorkCommand.KindGuideReachabilityPending);
        Assert.DoesNotContain(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() is "G858-not-applicable" or "G858-satisfied"
            && item.GetProperty("reason").GetString()!.StartsWith("debt-window-", StringComparison.Ordinal));
        Assert.DoesNotContain(result.RootElement.GetProperty("warnings").EnumerateArray(), item =>
            item.GetString()!.Contains("debt window retained", StringComparison.Ordinal));
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-uncommitted-old"
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackRecordedUncommitted
            && item.GetProperty("reason").GetString() == "debt-window-historical");
        Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-uncommitted-new"
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackRecordedUncommitted);
        var debtCounts = result.RootElement.GetProperty("debt_window").GetProperty("decision_counts");
        Assert.Equal(2, debtCounts.GetProperty("candidate_units").GetInt32());
        Assert.Equal(1, debtCounts.GetProperty("historical_units").GetInt32());
        Assert.Equal(1, debtCounts.GetProperty("included_units").GetInt32());
    }

    [Fact]
    public void V14_NoWindowKeepsOldReportShapeAndLiveClaimFindingIsNotWindowed()
    {
        using var workspace = new AdoptionWorkspace();
        var oldClaim = Now.AddDays(-20);
        workspace.WriteDebtPacket("G858-live-claim");
        workspace.WriteCloseoutOnly("G858-live-claim", Now.AddDays(-1), 2401);
        workspace.WriteClaim("G858-live-claim", Team, oldClaim);

        foreach (var mode in new string?[] { null, TeamMode.Delivery, TeamMode.AuthoringOnly })
        {
            if (mode is not null)
            {
                SetMode(workspace.Context, mode, Now.AddDays(-10));
            }
            var result = Report(workspace.Context, now: Now, staleMinutes: 0);
            Assert.False(result.RootElement.TryGetProperty("debt_window", out _));
            Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
                && item.GetProperty("execution_unit").GetString() == "G858-live-claim");
        }

        // An active solo window filters only the closeout-debt kinds; the
        // same old claim remains a live finding.
        var adoption = Now.AddDays(-5);
        SetMode(workspace.Context, TeamMode.SoloConductor, adoption);
        AutomationStalledWorkCommand.UtcNowFactory = () => Now;
        var solo = Report(workspace.Context, now: Now, staleMinutes: 0);
        Assert.Contains(solo.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindClaimStale
            && item.GetProperty("execution_unit").GetString() == "G858-live-claim");
        Assert.False(solo.RootElement.GetProperty("debt_window").GetProperty("decision_counts")
            .GetProperty("candidate_units").GetInt32() == 0);
    }

    [Fact]
    public void V15_JsonAndMarkdownMatchAndCountDistinctUnitsAcrossBothDebtLanes()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        foreach (var unit in new[] { "G858-count-included", "G858-count-old", "G858-count-unknown" })
        {
            workspace.WriteDebtPacket(unit);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2500 + Math.Abs(unit.GetHashCode()) % 400);
        }
        workspace.AppendEvent(IssueEvent("G858-count-included", cutoff, "J-Tech-Japan/intent-system#401",
            "https://github.com/J-Tech-Japan/intent-system/issues/401"));
        workspace.AppendEvent(IssueEvent("G858-count-old", cutoff.AddTicks(-1), "J-Tech-Japan/intent-system#402",
            "https://github.com/J-Tech-Japan/intent-system/issues/402"));

        using var json = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(10));
        using var markdownWriter = new StringWriter();
        AutomationStalledWorkCommand.UtcNowFactory = () => cutoff.AddDays(10);
        Assert.Equal(0, AutomationStalledWorkCommand.Execute(workspace.Context,
            ["--domain", Domain, "--repo", Repo, "--stale-minutes", "0", "--since", cutoff.ToString("O")], markdownWriter));
        var markdown = markdownWriter.ToString();
        var summary = json.RootElement.GetProperty("debt_window");
        var counts = summary.GetProperty("decision_counts");
        Assert.Equal(3, counts.GetProperty("candidate_units").GetInt32());
        Assert.Equal(1, counts.GetProperty("included_units").GetInt32());
        Assert.Equal(1, counts.GetProperty("historical_units").GetInt32());
        Assert.Equal(1, counts.GetProperty("unknown_units").GetInt32());
        Assert.True(json.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("execution_unit").GetString() == "G858-count-included"
                && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                    or AutomationStalledWorkCommand.KindGuideReachabilityPending) == 2,
            JsonSerializer.Serialize(json.RootElement.GetProperty("items")));
        Assert.Equal(2, json.RootElement.GetProperty("items").EnumerateArray()
            .Count(item => item.GetProperty("execution_unit").GetString() == "G858-count-unknown"
                && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                    or AutomationStalledWorkCommand.KindGuideReachabilityPending));
        Assert.Contains("policy: explicit-since", markdown, StringComparison.Ordinal);
        Assert.Contains(cutoff.ToString("O"), markdown, StringComparison.Ordinal);
        Assert.Contains("candidates=3, included=1, historical=1, unknown=1", markdown, StringComparison.Ordinal);
        Assert.Contains("G858-count-unknown", markdown, StringComparison.Ordinal);
        Assert.Contains("evidence_kind: issue-created", markdown, StringComparison.Ordinal);
        Assert.True(json.RootElement.GetProperty("stalled").GetBoolean());
    }

    [Fact]
    public void V16_ReportAddsNoDurableWritesOrGitFetches()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = Now.AddDays(-2);
        workspace.WriteDebtPacket("G858-read-only");
        workspace.WriteCloseoutOnly("G858-read-only", Now.AddDays(-1), 2601);
        workspace.AppendEvent(IssueEvent("G858-read-only", Now.AddDays(-3), "J-Tech-Japan/intent-system#501",
            "https://github.com/J-Tech-Japan/intent-system/issues/501"));
        SetMode(workspace.Context, TeamMode.SoloConductor, cutoff);
        workspace.InitializeGit();
        var runner = new RecordingFreshnessRunner();
        AutomationStalledWorkCommand.GitCommandRunnerFactory = () => runner;
        var before = workspace.SnapshotFiles();
        var beforeRefs = workspace.Git("show-ref");

        using var result = Report(workspace.Context, now: Now);

        Assert.Equal(before, workspace.SnapshotFiles());
        Assert.Equal(beforeRefs, workspace.Git("show-ref"));
        Assert.Equal(new[]
        {
            "rev-parse HEAD",
            "ls-remote --symref origin HEAD",
            "cat-file -e aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa^{commit}",
            "merge-base --is-ancestor aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa HEAD",
        }, runner.Calls.ToArray());
        Assert.DoesNotContain(runner.Calls, call => call.Contains("fetch", StringComparison.Ordinal));
    }

    [Fact]
    public void V01_GuideSoloConductorRendersTheDebtWindowRoute()
    {
        using var workspace = new AdoptionWorkspace();
        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideSoloConductorCommand.Execute(workspace.Context, ["--format", "markdown"], markdownWriter));
        Assert.Contains("G858 `automation stalled-work`", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("first recorded solo-conductor transition", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("legacy debt run log", markdownWriter.ToString(), StringComparison.Ordinal);

        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideSoloConductorCommand.Execute(workspace.Context, ["--format", "json"], jsonWriter));
        using var guide = JsonDocument.Parse(jsonWriter.ToString());
        Assert.Contains(guide.RootElement.GetProperty("limits").EnumerateArray(), item =>
            item.GetString()!.Contains("G858 `automation stalled-work`", StringComparison.Ordinal));
    }
}
