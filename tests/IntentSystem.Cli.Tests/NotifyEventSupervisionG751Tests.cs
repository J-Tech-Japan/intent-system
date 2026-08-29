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
    public void RunningEventModeSampleSeparatesStartupFromWarmCadenceAtTheDeclaredFloor_G751()
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
        const int declaredIntervalSeconds = 300;
        const int declaredBoundSeconds = 900;
        const int warmIntervalPasses = 12;
        const int preFixRawRecords = 73;
        const int preFixEventWaitRecords = 72;
        const int preFixStartupIntervalRecords = 1;
        const double preFixWallSeconds = 2.065;
        const double preFixRecordsPerHour = 127269.78;
        var intervalDelayCalls = 0;
        DateTimeOffset? warmWindowStart = null;
        NotifySupervisionEventMonitor.Delay = (_, token) =>
            Task.Delay(TimeSpan.FromMilliseconds(1), token);
        NotifySupervisor.Delay = delay =>
        {
            if (intervalDelayCalls == warmIntervalPasses)
            {
                cancellation.Cancel();
                return;
            }

            Assert.Equal(TimeSpan.FromSeconds(declaredIntervalSeconds), delay);
            warmWindowStart ??= now;
            now = now.Add(delay);
            intervalDelayCalls++;
            Thread.Yield();
        };

        var exitCode = supervisor.RunLoop(writer, cancellation.Token, once: false);
        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        var triggerCounts = state.CycleHistory
            .GroupBy(cycle => string.IsNullOrWhiteSpace(cycle.Trigger) ? "interval" : cycle.Trigger)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var eventWaitCount = triggerCounts.GetValueOrDefault("event-wait");
        var intervalCount = triggerCounts.GetValueOrDefault("interval");
        var totalRecords = state.CycleHistory.Count;
        Assert.NotNull(warmWindowStart);
        var warmWindowSeconds = (now - warmWindowStart!.Value).TotalSeconds;
        const int startupIntervalRecords = 1;
        var warmIntervalRecords = intervalCount - startupIntervalRecords;
        var recordsPerHour = warmIntervalRecords / (warmWindowSeconds / 3600d);
        var triggerMix = string.Join(",", triggerCounts.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value}"));

        output.WriteLine(
            $"G751 before measurement: build=b525191a independent_review_measurement "
            + $"interval_seconds={declaredIntervalSeconds} bound_seconds={declaredBoundSeconds} "
            + $"sample_method=running-event-mode-supervisor window=startup-plus-two-second-wall-window "
            + $"raw_records={preFixRawRecords} event-wait_records={preFixEventWaitRecords} "
            + $"startup_interval_records={preFixStartupIntervalRecords} wall_window_seconds={preFixWallSeconds:F3} "
            + $"records-per-hour={preFixRecordsPerHour:F2} steady_state=false");
        output.WriteLine(
            $"G751 after measurement: build=child-fixture interval_seconds={declaredIntervalSeconds} "
            + $"bound_seconds={declaredBoundSeconds} "
            + "sample_method=running-event-mode-supervisor-controlled-elapsed-time "
            + $"startup_first_cycle_records={startupIntervalRecords} warm_window_seconds={warmWindowSeconds:F0} "
            + $"warm_interval_records={warmIntervalRecords} event-wait_records={eventWaitCount} "
            + $"trigger_mix={triggerMix} raw_records={totalRecords} records-per-hour={recordsPerHour:F2} "
            + $"interval_delay_calls={intervalDelayCalls}");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, startupIntervalRecords);
        Assert.Equal(warmIntervalPasses, intervalDelayCalls);
        Assert.Equal(warmIntervalPasses + startupIntervalRecords, intervalCount);
        Assert.Equal(warmIntervalPasses, warmIntervalRecords);
        Assert.Equal(3600d, warmWindowSeconds, precision: 6);
        Assert.Equal(warmIntervalPasses + startupIntervalRecords, totalRecords);
        Assert.Equal(0, eventWaitCount);
        Assert.Equal(12d, recordsPerHour, precision: 6);
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
            Assert.Contains("b525191a", document, StringComparison.Ordinal);
            Assert.Contains("interval_seconds=300", document, StringComparison.Ordinal);
            Assert.Contains("bound_seconds=900", document, StringComparison.Ordinal);
            Assert.Contains("event-wait:72", document, StringComparison.Ordinal);
            Assert.Contains("interval:1", document, StringComparison.Ordinal);
            Assert.Contains("wall_window_seconds=2.065", document, StringComparison.Ordinal);
            Assert.Contains("records-per-hour=127269.78", document, StringComparison.Ordinal);
            Assert.Contains("sample_method=running-event-mode-supervisor-controlled-elapsed-time", document, StringComparison.Ordinal);
            Assert.Contains("startup_first_cycle_records=1", document, StringComparison.Ordinal);
            Assert.Contains("warm_window_seconds=3600", document, StringComparison.Ordinal);
            Assert.Contains("warm_interval_records=12", document, StringComparison.Ordinal);
            Assert.Contains("event-wait_records=0", document, StringComparison.Ordinal);
            Assert.Contains("raw_records=13", document, StringComparison.Ordinal);
            Assert.Contains("records-per-hour=12.00", document, StringComparison.Ordinal);
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
