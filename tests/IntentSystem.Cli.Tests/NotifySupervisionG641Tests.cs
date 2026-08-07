using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// Constructed G641 evidence: an undelivered escalation is surfaced and
/// measured, a restart gap breaks the declared bound, and a missing recorded
/// seat is reported without inventing a transport.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG641Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g641-").FullName;
    private readonly DateTimeOffset firstNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG641Tests()
    {
        now = firstNow;
        NotifyCommand.UtcNowFactory = () => now;
        NotifySupervisor.Delay = _ => { };
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EscalationIsRecordedWithUnknownStart_ThenRestartGapBreaksBound_G641()
    {
        var scripts = Path.Combine(root, "agmsg");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(scripts, "send.sh"), "fixture");
        var runner = new FakeRunner();
        var context = CreateContext();
        NotifyEventWriter.Append(
            ResolveEventPath(),
            new NotifyDesignEvent
            {
                Timestamp = firstNow.AddMinutes(-10),
                Team = Team,
                Kind = "escalation",
                Unit = "G641-escalation",
                Summary = "approval is durable but nobody was woken",
                Artifact = "approval",
            });

        var supervisor = CreateSupervisor(context, scripts, runner, write: true, boundSeconds: 300);
        var first = supervisor.RunOnce();
        var escalation = Assert.Single(first.Findings, finding => finding.Kind == "undelivered-escalation");
        Assert.Null(escalation.DetectableAt);
        Assert.True(first.Bound!.Recorded);
        Assert.Null(first.Bound.BoundMet);
        Assert.Contains(first.RecoveryRecords, record =>
            record.Kind == "undelivered-escalation" && record.DetectableAtUnknown && record.WakeDelivered);
        Assert.Contains(runner.Calls, call => call.Arguments.Count >= 3 && Path.GetFileName(call.Arguments[0]) == "send.sh");

        File.Delete(ResolveEventPath());
        now = firstNow.AddSeconds(301);
        var second = supervisor.RunOnce();

        Assert.False(second.Bound!.BoundMet);
        Assert.True(second.Liveness!.AbsentSinceLastCycle);
        Assert.Equal(301, second.Liveness.GapSeconds);
        Assert.Contains(second.Findings, finding => finding.Kind == "supervisor-not-running");
        Assert.Contains(second.RecoveryRecords, record =>
            record.Kind == "undelivered-escalation" && record.ClearedAt is not null && record.DurationSeconds is null);
    }

    [Fact]
    public void RestartGapReportsSelfAbsenceWithoutClaimingUndeclaredBound_G641()
    {
        var context = CreateContext();
        var supervisor = CreateSupervisor(context, "unused-agmsg", new FakeRunner(), write: true, boundSeconds: null);

        var first = supervisor.RunOnce();
        Assert.True(first.Silent);
        Assert.False(first.Bound!.Recorded);
        Assert.Null(first.Bound.BoundSeconds);

        now = firstNow.AddHours(3);
        var second = supervisor.RunOnce();

        Assert.False(second.Silent);
        Assert.False(second.Bound!.Recorded);
        Assert.Null(second.Bound.BoundSeconds);
        Assert.Null(second.Bound.BoundMet);
        Assert.True(second.Liveness!.AbsentSinceLastCycle);
        Assert.Equal(300, second.Liveness.AbsenceThresholdSeconds);
        Assert.Equal("configured-interval", second.Liveness.AbsenceThresholdKind);
        Assert.DoesNotContain("within the declared", second.Liveness.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("exceeding the declared", second.Liveness.Summary, StringComparison.Ordinal);
        Assert.Contains(second.Findings, finding => finding.Kind == "supervisor-not-running");
    }

    [Fact]
    public void DeliveredEscalationClearsAndReturnsToSilence_G641()
    {
        var scripts = Path.Combine(root, "agmsg");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(scripts, "send.sh"), "fixture");
        var runner = new FakeRunner();
        var context = CreateContext();
        NotifyEventWriter.Append(
            ResolveEventPath(),
            new NotifyDesignEvent
            {
                Timestamp = firstNow.AddMinutes(-10),
                Team = Team,
                Kind = "escalation",
                Unit = "G641-acknowledged-escalation",
                Summary = "approval is durable but nobody was woken",
                Artifact = "approval",
            });

        var supervisor = CreateSupervisor(context, scripts, runner, write: true, boundSeconds: 300);
        var first = supervisor.RunOnce();
        Assert.Contains(first.Findings, finding =>
            finding.Kind == "undelivered-escalation" && finding.WakeDelivered);

        now = firstNow.AddSeconds(1);
        var second = supervisor.RunOnce();

        Assert.True(second.Silent);
        Assert.DoesNotContain(second.Findings, finding => finding.Kind == "undelivered-escalation");
        Assert.Contains(second.RecoveryRecords, record =>
            record.Kind == "undelivered-escalation" && record.WakeDelivered && record.ClearedAt is not null);

        now = firstNow.AddSeconds(2);
        var third = supervisor.RunOnce();
        Assert.True(third.Silent);
        Assert.DoesNotContain(third.Findings, finding => finding.Kind == "undelivered-escalation");
    }

    [Fact]
    public void HealthyRecordedSeatsRemainSilentAndAbsentSeatsAreConstructedFromTopology_G641()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology();
        var runner = new FakeRunner { AgentsJson = "{\"result\":{\"agents\":[]}}" };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: false, boundSeconds: null);

        var pass = supervisor.RunOnce();

        Assert.Contains(pass.Findings, finding => finding.Kind == "seat-absent");
        Assert.All(pass.Findings.Where(finding => finding.Kind == "seat-absent"), finding =>
            Assert.Equal("recorded-topology", finding.Source));
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(["agent", "list"]));
    }

    [Fact]
    public void EnglishAndJapaneseGuidanceNameTheMeasuredPreviewContract_G641()
    {
        var rootPath = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(rootPath, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(rootPath, "docs", "ja", "12-agent-message-orchestration.md"));
        var englishLedger = File.ReadAllText(Path.Combine(rootPath, "docs", "en", "1.0-compatibility-ledger.md"));
        var japaneseLedger = File.ReadAllText(Path.Combine(rootPath, "docs", "ja", "1.0-compatibility-ledger.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("G641", document, StringComparison.Ordinal);
            Assert.Contains("--bound", document, StringComparison.Ordinal);
            Assert.Contains("undelivered-escalation", document, StringComparison.Ordinal);
            Assert.Contains("detectable_at", document, StringComparison.Ordinal);
            Assert.Contains("absent_since_last_cycle", document, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", document, StringComparison.Ordinal);
        }

        Assert.Contains("measured supervision records", englishLedger, StringComparison.Ordinal);
        Assert.Contains("measured supervision records", japaneseLedger, StringComparison.Ordinal);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        string scripts,
        FakeRunner runner,
        bool write,
        int? boundSeconds) => new(
        context,
        root,
        Domain,
        Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 300,
        declaredBoundSeconds: boundSeconds,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write,
        format: "json",
        runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: scripts);

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private string ResolveEventPath()
    {
        NotifyEventWriter.TryResolvePath(root, Team, out var path, out var error);
        Assert.True(string.IsNullOrEmpty(error), error);
        return path;
    }

    private void RecordMode(CliContext context, string mode)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
            writer));
    }

    private void WriteTopology()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG641",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG641", pane_id = "wG641:p1" },
                ["implementation"] = new { resident = "herdr", workspace_id = "wG641", pane_id = "wG641:p2" },
            },
        }));
    }

    private sealed class FakeRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public string AgentsJson { get; init; } = "{\"result\":{\"agents\":[]}}";

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, string.Empty);
            }

            if (fileName == "bash" && arguments.Count > 1 && Path.GetFileName(arguments[0]) == "team.sh")
            {
                return new NotifyProcessResult(0, "orchestration (codex)\n", string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
