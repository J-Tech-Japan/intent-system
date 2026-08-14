using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G695: the completion-signal chain is durable, queryable, idempotent, and
/// cannot stop after canonical classification without a terminal continuation
/// or named blocker link.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class ContinuationChainG695Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private readonly string root = Directory.CreateTempSubdirectory("continuation-chain-g695-").FullName;

    public ContinuationChainG695Tests()
    {
        AutomationHostLoopWakeCommand.NextActionDelegate = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    public void Dispose()
    {
        AutomationHostLoopWakeCommand.NextActionDelegate = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
        ContinuationChainStore.WriteOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Chain_ExposesExactNextMissingLink_AndCompletesWithNamedBlocker()
    {
        var report = ContinuationChainStore.RecordReportReceived(
            root,
            Domain,
            Team,
            "G695-signal",
            "nonce-1",
            "completed",
            "draft-pr",
            "draft PR is ready",
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));

        Assert.True(report.Applied);
        Assert.Equal(
            ContinuationChainStore.OrchestrationWakeAttempted,
            report.Record!.NextMissingLink);

        var wake = ContinuationChainStore.RecordLink(
            root,
            Domain,
            Team,
            report.Record.CompletionSignalId,
            "G695-signal",
            report.Record.ChainId,
            ContinuationChainStore.OrchestrationWakeAttempted,
            "test-wake",
            ["wake-attempted"],
            timestamp: new DateTimeOffset(2026, 8, 14, 12, 0, 1, TimeSpan.Zero));
        Assert.True(wake.Applied);
        Assert.Equal(ContinuationChainStore.WakeDeliveredOrObserved, wake.Record!.NextMissingLink);

        var classified = ContinuationChainStore.RecordLink(
            root,
            Domain,
            Team,
            report.Record.CompletionSignalId,
            "G695-signal",
            report.Record.ChainId,
            ContinuationChainStore.WakeDeliveredOrObserved,
            "test-wake",
            ["wake-observed"],
            timestamp: new DateTimeOffset(2026, 8, 14, 12, 0, 2, TimeSpan.Zero));
        Assert.True(classified.Applied);

        var blocker = ContinuationChainStore.RecordLink(
            root,
            Domain,
            Team,
            report.Record.CompletionSignalId,
            "G695-signal",
            report.Record.ChainId,
            ContinuationChainStore.NamedBlockerRecorded,
            "test-wake",
            ["classification:approved-pr-merge-closeout"],
            classification: "approved-pr-merge-closeout",
            blocker: "merge remains judgement-gated",
            timestamp: new DateTimeOffset(2026, 8, 14, 12, 0, 3, TimeSpan.Zero));

        Assert.True(blocker.Applied);
        Assert.Null(blocker.Record!.NextMissingLink);
        Assert.True(blocker.Record.Complete);
        Assert.Contains(
            blocker.Record.Links,
            link => link.Name == ContinuationChainStore.NamedBlockerRecorded
                && link.Blocker == "merge remains judgement-gated");
    }

    [Fact]
    public void ReportLink_IsIdempotentForTheSameCompletionSignal()
    {
        var first = ContinuationChainStore.RecordReportReceived(
            root,
            Domain,
            Team,
            "G695-idempotent",
            "nonce-2",
            "completed",
            "artifact",
            "summary");
        var second = ContinuationChainStore.RecordReportReceived(
            root,
            Domain,
            Team,
            "G695-idempotent",
            "nonce-2",
            "completed",
            "artifact",
            "summary");

        Assert.True(first.Applied);
        Assert.False(second.Applied);
        Assert.True(second.AlreadyConverged);
        Assert.Single(second.Record!.Links);

        var read = ContinuationChainStore.Read(root, Domain, Team, taskId: "G695-idempotent");
        var record = Assert.Single(read.Records);
        Assert.Equal(ContinuationChainStore.ReportReceived, record.Links[0].Name);
        Assert.Equal(ContinuationChainStore.OrchestrationWakeAttempted, record.NextMissingLink);
    }

    [Fact]
    public void QuerySurface_NamesAnEmptyDurableStoreWithoutMutatingIt()
    {
        using var writer = new StringWriter();
        var exit = AutomationContinuationChainCommand.Execute(
            CreateContext(),
            ["--domain", Domain, "--team", Team, "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("resolved").GetBoolean());
        Assert.Empty(document.RootElement.GetProperty("records").EnumerateArray());
        Assert.Contains("No continuation-chain record", document.RootElement.GetProperty("summary").GetString());
        Assert.False(File.Exists(ContinuationChainStore.ResolvePath(root, Domain, Team)));
    }

    [Fact]
    public void SupervisorNamesTheOwedContinuationForEachIncidentShape()
    {
        var supervisor = new NotifyMeasuredSupervisor(
            CreateContext(),
            root,
            Domain,
            Team,
            Repo,
            ownerRole: "orchestration",
            intervalSeconds: 300,
            declaredBoundSeconds: null,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: false,
            format: "json",
            new NoopRunner(),
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: "unused",
            stalledWorkAnalyzer: () => new AutomationStalledWorkResult
            {
                Domain = Domain,
                Repo = Repo,
                StaleMinutesThreshold = 45,
                BacklogIdleMinutesThreshold = 45,
                OpenPendingDelegations = 0,
                Stalled = true,
                Items =
                [
                    new StalledWorkItem
                    {
                        Kind = AutomationStalledWorkCommand.KindApprovedNotMerged,
                        ExecutionUnit = "G695-direct",
                        Pr = new StalledWorkRef { Number = 1503, Url = "https://github.com/example/pull/1503" },
                        AgeMinutes = 10,
                        IsInformational = false,
                        RecommendedAction = "merge then closeout",
                        ContinuationEvidence = ["lane:direct", "exact-head:abc123", "checks:all-green"],
                    },
                    new StalledWorkItem
                    {
                        Kind = AutomationStalledWorkCommand.KindKnowledgeWritebackPending,
                        ExecutionUnit = "G695-knowledge",
                        AgeMinutes = 10,
                        IsInformational = false,
                        RecommendedAction = "dispatch knowledge write-back",
                        DeclaredWriteBackTargets = ["docs/en/12-agent-message-orchestration.md"],
                        ContinuationEvidence = ["execution-unit:G695-knowledge", "closeout-recorded", "declared-targets:docs/en/12-agent-message-orchestration.md"],
                    },
                    new StalledWorkItem
                    {
                        Kind = AutomationStalledWorkCommand.KindBacklogReadyIdle,
                        ExecutionUnit = "G695-next-slice",
                        AgeMinutes = 10,
                        IsInformational = false,
                        RecommendedAction = "publish next slice",
                        ContinuationEvidence = ["candidate:G695-next-slice", "wip:empty", "publish-gate:issue-cut-ready", "idle-minutes:10"],
                    },
                ],
                Excluded = [],
                Warnings = [],
                OperatorAttentionStatus = null,
                OperatorAttentionError = null,
            });

        var pass = supervisor.RunOnce();

        Assert.Contains(pass.Findings, finding =>
            finding.Kind == "approved-direct-lane-merge-closeout-owed"
            && finding.OwedTransition == "merge-then-closeout"
            && finding.Evidence!.Contains("exact-head:abc123"));
        Assert.Contains(pass.Findings, finding =>
            finding.Kind == "merged-pr-knowledge-writeback-dispatch-owed"
            && finding.OwedTransition == "knowledge-writeback-dispatch"
            && finding.Evidence!.Contains("closeout-recorded"));
        Assert.Contains(pass.Findings, finding =>
            finding.Kind == "actionable-queue-next-slice-publication-owed"
            && finding.OwedTransition == "publish-next-slice"
            && finding.Evidence!.Contains("publish-gate:issue-cut-ready"));
    }

    [Fact]
    public void ApprovedDirectWake_DoesNotStopAfterClassification_G695()
    {
        var report = ContinuationChainStore.RecordReportReceived(
            root,
            Domain,
            Team,
            "G695-regression",
            "nonce-3",
            "completed",
            "approved-pr",
            "review completed");
        StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = (_, _, writer) =>
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                repo = Repo,
                classification = HostLoopNextActionAnalyzer.ClassificationApprovedPrMergeCloseout,
                mutation_allowed = true,
                recommended_command = "intent-cli closeout pr --pr 1503 --write",
                evidence = new[] { "pr:#1503", "lane:direct", "exact-head:abc123", "checks:all-green" },
                summary = "approved exact-head green direct lane",
            }));
            return 0;
        };

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(
            CreateContext(),
            [
                "--repo", Repo,
                "--domain", Domain,
                "--team", Team,
                "--completion-signal-id", report.Record!.CompletionSignalId,
                "--write",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var chain = document.RootElement.GetProperty("continuation_chain");
        var links = chain.GetProperty("links").EnumerateArray()
            .Select(link => link.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains(ContinuationChainStore.CanonicalStateClassified, links);
        Assert.Contains(ContinuationChainStore.NamedBlockerRecorded, links);
        Assert.DoesNotContain(ContinuationChainStore.RequiredContinuationStarted, links);
        Assert.True(chain.GetProperty("complete").GetBoolean());
    }

    private void StubSurfaceAvailable()
    {
        var installed = Path.Combine(root, "installed-cli");
        File.WriteAllText(installed, "stub");
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => installed;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) =>
            new InstalledCliProbeResult(
                0,
                "automation summary host-review-preflight issue-publish pr-transition review-start request-update approved",
                string.Empty);
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
            },
        },
    };

    private sealed class NoopRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            new(0, string.Empty, string.Empty);
    }
}
