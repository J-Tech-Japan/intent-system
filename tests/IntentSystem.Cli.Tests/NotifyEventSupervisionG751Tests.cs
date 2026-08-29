using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G751: a successful event-mode wait that returns without a qualifying
/// observation is visible to the operator but is not a durable cycle. Genuine
/// observations and the periodic interval safety floor remain durable.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifyEventSupervisionG751Tests : IDisposable
{
    private const string NoObservationOutcome = "wait-returned-without-observation";
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly ITestOutputHelper output;
    private readonly string root = Directory.CreateTempSubdirectory("notify-g751-").FullName;
    private DateTimeOffset now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    public NotifyEventSupervisionG751Tests(ITestOutputHelper output)
    {
        this.output = output;
        NotifyCommand.UtcNowFactory = () => now;
        NotifySupervisor.Delay = Thread.Sleep;
        NotifySupervisionEventMonitor.Delay = Task.Delay;
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.BashExecutableFactory = null;
        NotifySupervisionEventMonitor.Delay = Task.Delay;
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulWaitWithoutObservationIsWarningOnlyAndKeepsIntervalFloor_G751()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        using var cancellation = new CancellationTokenSource();
        var waitEvents = new List<NotifySupervisionWaitEvent>();
        NotifySupervisionEventMonitor.Delay = (_, _) => Task.CompletedTask;
        var runner = new EventRunner
        {
            AgentsJson = AgentsJson("idle", 7, now),
        };
        var monitor = new NotifySupervisionEventMonitor(
            root,
            Domain,
            Team,
            runner,
            "fake-herdr",
            _ => Task.CompletedTask,
            waitEvent =>
            {
                waitEvents.Add(waitEvent);
                cancellation.Cancel();
            });

        await monitor.RunAsync(cancellation.Token);

        var noObservation = Assert.Single(waitEvents);
        Assert.Equal(NoObservationOutcome, noObservation.Outcome);
        Assert.True(noObservation.RearmAttempted);
        Assert.Contains("without a qualifying state transition", noObservation.Detail, StringComparison.Ordinal);

        var supervisor = CreateSupervisor(context, runner);
        using var writer = new StringWriter();
        supervisor.RecordWaitEvent(writer, noObservation);
        var warning = writer.ToString();
        Assert.Contains("event-wait-no-observation", warning, StringComparison.Ordinal);
        Assert.Contains("no durable event-wait cycle was written", warning, StringComparison.Ordinal);
        Assert.Contains("interval safety floor remains active", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("event-wait-rearmed", warning, StringComparison.Ordinal);

        var cyclePath = NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.False(File.Exists(cyclePath));

        var intervalPass = supervisor.RunOnce();
        Assert.Equal(0, intervalPass.ExitCode);
        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.Contains(state.CycleHistory, cycle => cycle.Trigger == "interval");
        Assert.DoesNotContain(state.CycleHistory, cycle => cycle.Trigger == "event-wait");
    }

    [Fact]
    public async Task RepeatedInstantNoObservationReturnsStayObservableWithoutDurableRecords_G751()
    {
        var context = CreateContext();
        WriteTopology();
        using var cancellation = new CancellationTokenSource();
        var waitEvents = new List<NotifySupervisionWaitEvent>();
        NotifySupervisionEventMonitor.Delay = (_, _) => Task.CompletedTask;
        var runner = new EventRunner
        {
            AgentsJson = AgentsJson("idle", 7, now),
        };
        var monitor = new NotifySupervisionEventMonitor(
            root,
            Domain,
            Team,
            runner,
            "fake-herdr",
            _ => Task.CompletedTask,
            waitEvent =>
            {
                waitEvents.Add(waitEvent);
                if (waitEvents.Count == 3)
                {
                    cancellation.Cancel();
                }
            });

        await monitor.RunAsync(cancellation.Token);

        Assert.Equal(3, waitEvents.Count);
        Assert.All(waitEvents, waitEvent =>
        {
            Assert.Equal(NoObservationOutcome, waitEvent.Outcome);
            Assert.True(waitEvent.RearmAttempted);
        });

        var supervisor = CreateSupervisor(context, runner);
        using var writer = new StringWriter();
        foreach (var waitEvent in waitEvents)
        {
            supervisor.RecordWaitEvent(writer, waitEvent);
        }

        var warning = writer.ToString();
        Assert.Equal(3, warning.Split("event-wait-no-observation", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("event-wait-rearmed", warning, StringComparison.Ordinal);
        Assert.False(File.Exists(NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team)));
        output.WriteLine("G751 instant-return sample: returns=3, observable_warnings=3, durable_event_wait_cycles=0");
    }

    [Fact]
    public void GenuineObservationAndIntervalSafetyFloorRemainDurable_G751()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        var runner = new EventRunner
        {
            AgentsJson = AgentsJson("working", 7, now),
        };
        var supervisor = CreateSupervisor(context, runner);

        Assert.Equal(0, supervisor.RunOnce().ExitCode);
        now = now.AddSeconds(2);
        runner.AgentsJson = AgentsJson("done", 8, now.AddSeconds(-1));
        var eventPass = supervisor.RunOnce("event");
        Assert.Contains(eventPass.Findings, finding => finding.Kind == "seat-state-transition");

        now = now.AddSeconds(1);
        Assert.Equal(0, supervisor.RunOnce().ExitCode);

        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.Contains(state.CycleHistory, cycle => cycle.Trigger == "event");
        Assert.Contains(state.CycleHistory, cycle => cycle.Trigger == "interval");
        Assert.DoesNotContain(state.CycleHistory, cycle => cycle.Trigger == "event-wait");
    }

    [Fact]
    public void RunningEventModeSampleReportsRawCadenceAtTheDeclaredFloor_G751()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        var runner = new EventRunner
        {
            AgentsJson = AgentsJson("idle", 7, now),
        };
        var supervisor = CreateSupervisor(context, runner);
        using var cancellation = new CancellationTokenSource();
        using var writer = new StringWriter();
        var sampleDuration = TimeSpan.FromSeconds(2);
        var intervalDelayCalls = 0;
        NotifySupervisionEventMonitor.Delay = (_, _) => Task.Delay(TimeSpan.FromMilliseconds(25));
        NotifySupervisor.Delay = _ =>
        {
            intervalDelayCalls++;
            Thread.Sleep(sampleDuration);
            cancellation.Cancel();
        };

        var started = Stopwatch.GetTimestamp();
        var exitCode = supervisor.RunLoop(writer, cancellation.Token, once: false);
        var elapsed = Stopwatch.GetElapsedTime(started);
        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        var triggerCounts = state.CycleHistory
            .GroupBy(cycle => string.IsNullOrWhiteSpace(cycle.Trigger) ? "interval" : cycle.Trigger)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var eventWaitCount = triggerCounts.GetValueOrDefault("event-wait");
        var intervalCount = triggerCounts.GetValueOrDefault("interval");
        var totalRecords = state.CycleHistory.Count;
        var recordsPerHour = totalRecords / elapsed.TotalHours;
        var triggerMix = string.Join(",", triggerCounts.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value}"));

        output.WriteLine(
            $"G751 running cadence sample: build=child-fixture interval_seconds=300 bound_seconds=900 "
            + $"duration_seconds={elapsed.TotalSeconds:F3} sample_method=running-event-mode-supervisor "
            + $"trigger_mix={triggerMix} raw_records={totalRecords} records_per_hour={recordsPerHour:F2} "
            + $"interval_delay_calls={intervalDelayCalls}");

        Assert.Equal(0, exitCode);
        Assert.True(intervalCount >= 1);
        Assert.Equal(0, eventWaitCount);
    }

    [Fact]
    public void EnglishAndJapaneseOrchestrationDocsDescribeG751EventPersistenceContract_G751()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("G751", document, StringComparison.Ordinal);
            Assert.Contains("event-wait-no-observation", document, StringComparison.Ordinal);
            Assert.Contains("300", document, StringComparison.Ordinal);
            Assert.Contains("900", document, StringComparison.Ordinal);
            Assert.Contains("records-per-hour", document, StringComparison.Ordinal);
            Assert.Contains("interval", document, StringComparison.OrdinalIgnoreCase);
        }
    }

    private NotifyMeasuredSupervisor CreateSupervisor(CliContext context, EventRunner runner) => new(
        context,
        root,
        Domain,
        Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 300,
        declaredBoundSeconds: 900,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write: true,
        format: "json",
        runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: "unused",
        eventMode: true,
        debounceConsecutiveObservations: 1);

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private void RecordMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
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
            workspace_id = "wG751",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG751", pane_id = "wG751:p1" },
                ["implementation"] = new { resident = "herdr", workspace_id = "wG751", pane_id = "wG751:p2" },
            },
        }));
    }

    private static string AgentsJson(string subjectStatus, long sequence, DateTimeOffset changedAt) =>
        JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new object[]
                {
                    new { name = "orchestration", workspace_id = "wG751", pane_id = "wG751:p1", agent = "codex", agent_session = new { id = "owner" }, agent_status = "working", interactive_ready = true, state_change_seq = 2L, last_state_change_at = changedAt },
                    new { name = "implementation", workspace_id = "wG751", pane_id = "wG751:p2", agent = "codex", agent_session = new { id = "subject" }, agent_status = subjectStatus, interactive_ready = true, state_change_seq = sequence, last_state_change_at = changedAt },
                },
            },
        });

    private sealed class EventRunner : INotifyProcessRunner
    {
        public string AgentsJson { get; set; } = "";
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, "");
            }

            if (arguments.Take(3).SequenceEqual(["pane", "process-info", "--pane"]))
            {
                return new NotifyProcessResult(0, "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}", "");
            }

            return new NotifyProcessResult(0, "", "");
        }

        public async Task<NotifyProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, "");
            }

            if (arguments.Take(3).SequenceEqual(["agent", "wait", "wG751:p2"]))
            {
                return new NotifyProcessResult(0, WaitJson("idle", 7), "");
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new NotifyProcessResult(1, "", "cancelled");
        }

        private static string WaitJson(string status, long sequence) => JsonSerializer.Serialize(new
        {
            result = new
            {
                agent = new
                {
                    name = "implementation",
                    workspace_id = "wG751",
                    pane_id = "wG751:p2",
                    agent_status = status,
                    state_change_seq = sequence,
                    last_state_change_at = "2026-08-29T12:00:01Z",
                },
            },
        });
    }
}
