using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G659 evidence for the opt-in, single-process herdr wait source and its
/// independent interval safety floor.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifyEventSupervisionG659Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g659-").FullName;
    private DateTimeOffset now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    public NotifyEventSupervisionG659Tests()
    {
        NotifyCommand.UtcNowFactory = () => now;
        NotifySupervisionEventMonitor.Delay = (_, cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled(cancellationToken)
                : Task.CompletedTask;
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifySupervisionEventMonitor.Delay = Task.Delay;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("done", "implementation")]
    [InlineData("blocked", "implementation")]
    [InlineData("idle", "implementation")]
    [InlineData("done", "review")]
    [InlineData("blocked", "review")]
    [InlineData("idle", "review")]
    public void EventAndIntervalUseOneTransitionKey_OneWake_AndIntervalSurvivesWaitDeath_G659(
        string settledStatus,
        string subjectRole)
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology(subjectRole);
        var runner = new EventRunner { AgentsJson = AgentsJson("working", 7, now, subjectRole) };
        var supervisor = CreateSupervisor(context, runner);

        var baseline = supervisor.RunOnce();
        Assert.DoesNotContain(baseline.Findings, finding => finding.Kind == "seat-state-transition");

        now = now.AddSeconds(2);
        runner.AgentsJson = AgentsJson(settledStatus, 8, now.AddSeconds(-1), subjectRole);
        var immediate = supervisor.RunOnce("event");

        var transition = Assert.Single(immediate.Findings, finding => finding.Kind == "seat-state-transition");
        Assert.Equal("herdr.agent-wait.event", transition.Source);
        Assert.Equal(subjectRole, transition.SubjectRole);
        Assert.Equal("orchestration", transition.WakeTargetRole);
        Assert.True(transition.WakeDelivered);
        Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        var transitionChain = ContinuationChainStore.Read(root, Domain, Team);
        var chain = Assert.Single(transitionChain.Records);
        Assert.Contains(chain.Links, link => link.Name == ContinuationChainStore.ReportReceived
            && link.Source == "herdr-state-transition");
        Assert.Contains(chain.Links, link => link.Name == ContinuationChainStore.WakeDeliveredOrObserved);
        Assert.Equal(ContinuationChainStore.CanonicalStateClassified, chain.NextMissingLink);

        now = now.AddSeconds(1);
        var deduplicatedInterval = supervisor.RunOnce();
        Assert.DoesNotContain(deduplicatedInterval.Findings, finding => finding.Kind == "seat-state-transition");
        Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.Equal(3, deduplicatedInterval.Liveness!.GapSeconds);

        var cyclePath = NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        var transitions = File.ReadLines(cyclePath)
            .Select(line => JsonDocument.Parse(line))
            .SelectMany(document => document.RootElement.GetProperty("cycle").GetProperty("transitions").EnumerateArray().ToArray())
            .ToArray();
        var recorded = Assert.Single(transitions);
        Assert.Equal(1, recorded.GetProperty("latency_seconds").GetInt64());

        // A dead wait cannot remove the independent interval observation
        // floor: after a fresh working generation, the periodic list detects
        // the next settled transition and wakes through the same route.
        now = now.AddSeconds(1);
        runner.AgentsJson = AgentsJson("working", 9, now, subjectRole);
        supervisor.RunOnce();
        now = now.AddSeconds(1);
        runner.AgentsJson = AgentsJson("blocked", 10, now, subjectRole);
        var intervalFloor = supervisor.RunOnce();
        Assert.Contains(intervalFloor.Findings, finding =>
            finding.Kind == "seat-state-transition" && finding.Source == "herdr.agent-list.interval");
        Assert.Equal(2, runner.Calls.Count(call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"])));
    }

    [Fact]
    public async Task BlockingWaitDeathIsRecordedAndRearmedWithoutTerminalParsing_G659()
    {
        WriteTopology();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var runner = new EventRunner
        {
            AgentsJson = AgentsJson("working", 7, now),
            FailFirstImplementationWait = true,
        };
        var waitEvents = new List<NotifySupervisionWaitEvent>();
        var transitions = new List<string>();
        var monitor = new NotifySupervisionEventMonitor(
            root,
            Domain,
            Team,
            runner,
            "fake-herdr",
            role =>
            {
                transitions.Add(role);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            waitEvents.Add);

        await monitor.RunAsync(cancellation.Token);

        Assert.Equal(["implementation"], transitions);
        var failure = Assert.Single(waitEvents);
        Assert.Equal("wait-died-or-errored", failure.Outcome);
        Assert.True(failure.RearmAttempted);
        Assert.True(runner.ImplementationWaitCalls >= 2);
        Assert.All(runner.AsyncCalls.Where(call => call.Arguments.Contains("wait")), call =>
            Assert.DoesNotContain("--timeout", call.Arguments));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("capture-pane"));

        var context = CreateContext();
        var supervisor = CreateSupervisor(context, runner);
        using var writer = new StringWriter();
        supervisor.RecordWaitEvent(writer, failure);
        Assert.Contains("event-wait-rearmed", writer.ToString(), StringComparison.Ordinal);
        var cyclePath = NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        using var cycle = JsonDocument.Parse(Assert.Single(File.ReadLines(cyclePath)));
        Assert.Equal("event-wait", cycle.RootElement.GetProperty("cycle").GetProperty("trigger").GetString());
        Assert.True(cycle.RootElement.GetProperty("cycle").GetProperty("wait_events")[0]
            .GetProperty("rearm_attempted").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstallArtifactPinsChosenMode_AndAdoptionRequiresReEmission(bool eventMode)
    {
        using var writer = new StringWriter();
        var args = new List<string>
        {
            "install", "--domain", Domain, "--team", Team,
            "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
            "--bound", "300", "--interval", "120", "--platform", "macos",
            "--routing-root", root, "--write", "--format", "json",
        };
        if (eventMode) args.Add("--event-mode");

        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), args.ToArray(), writer));
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(eventMode, document.RootElement.GetProperty("event_mode").GetBoolean());
        var artifact = File.ReadAllText(document.RootElement.GetProperty("artifact_path").GetString()!);
        Assert.Equal(eventMode, artifact.Contains("--event-mode", StringComparison.Ordinal));
    }

    [Fact]
    public void HelpRenderedGuidesAndParityCarryTheSingleProcessAdoptionContract_G659()
    {
        using var help = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), ["--help"], help));
        Assert.Contains("--event-mode", help.ToString(), StringComparison.Ordinal);

        using var invalid = new StringWriter();
        Assert.Equal(1, NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["--domain", Domain, "--team", Team, "--event-mode", "--once"],
            invalid));
        Assert.Contains("continuous", invalid.ToString(), StringComparison.Ordinal);

        using var commandsWriter = new StringWriter();
        Assert.Equal(0, GuideCommandsListCommand.Execute(CreateContext(), ["--format", "json"], commandsWriter));
        using var commands = JsonDocument.Parse(commandsWriter.ToString());
        var notifyPurpose = commands.RootElement.GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("name").GetString() == "notify")
            .GetProperty("purpose").GetString()!;
        Assert.Contains("--event-mode", notifyPurpose, StringComparison.Ordinal);
        Assert.Contains("same supervisor process", notifyPurpose, StringComparison.Ordinal);
        Assert.Contains("re-registration", notifyPurpose, StringComparison.Ordinal);

        using var nextWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            CreateContext(),
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            nextWriter));
        using var next = JsonDocument.Parse(nextWriter.ToString());
        var setup = next.RootElement.GetProperty("decision_set").EnumerateArray()
            .Single(action => action.GetProperty("action").GetString() == "supervision-setup")
            .GetProperty("suggested_prompt").GetString()!;
        Assert.Contains("[--event-mode]", setup, StringComparison.Ordinal);
        Assert.Contains("remains interval-only", setup, StringComparison.Ordinal);

        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var path in new[]
        {
            Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"),
            Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"),
        })
        {
            var document = File.ReadAllText(path);
            foreach (var marker in new[]
            {
                "G659", "--event-mode", "herdr 0.8.0", "event-wait", "rearm_attempted",
                "interval-only", "1 transition", "1 wake", "unverified", "preview",
            })
            {
                Assert.Contains(marker, document, StringComparison.Ordinal);
            }
        }
    }

    private NotifyMeasuredSupervisor CreateSupervisor(CliContext context, EventRunner runner) => new(
        context, root, Domain, Team, repo: null, ownerRole: "orchestration",
        intervalSeconds: 300, declaredBoundSeconds: 300,
        staleMinutes: 45, claimedSilentMinutes: 720, backlogIdleMinutes: 45, repairSilentMinutes: 180,
        autoRedispatch: false, write: true, format: "json", runner,
        herdrExecutable: "fake-herdr", agmsgScriptsDirectory: "unused", eventMode: true);

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

    private void WriteTopology(string subjectRole = "implementation")
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG659",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG659", pane_id = "wG659:p1" },
                [subjectRole] = new { resident = "herdr", workspace_id = "wG659", pane_id = "wG659:p2" },
            },
        }));
    }

    private static string AgentsJson(
        string subjectStatus,
        long sequence,
        DateTimeOffset changedAt,
        string subjectRole = "implementation") =>
        JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new object[]
                {
                    new { name = "orchestration", workspace_id = "wG659", pane_id = "wG659:p1", agent = "codex", agent_session = new { id = "owner" }, agent_status = "working", interactive_ready = true, state_change_seq = 2L, last_state_change_at = changedAt },
                    new { name = subjectRole, workspace_id = "wG659", pane_id = "wG659:p2", agent = "codex", agent_session = new { id = "subject" }, agent_status = subjectStatus, interactive_ready = true, state_change_seq = sequence, last_state_change_at = changedAt },
                },
            },
        });

    private sealed class EventRunner : INotifyProcessRunner
    {
        public string AgentsJson { get; set; } = "";
        public bool FailFirstImplementationWait { get; set; }
        public int ImplementationWaitCalls { get; private set; }
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public List<(string FileName, IReadOnlyList<string> Arguments)> AsyncCalls { get; } = [];

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
            AsyncCalls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, "");
            }
            if (arguments.Take(3).SequenceEqual(["agent", "wait", "wG659:p2"]))
            {
                ImplementationWaitCalls++;
                if (FailFirstImplementationWait && ImplementationWaitCalls == 1)
                {
                    return new NotifyProcessResult(1, "", "fixture wait death");
                }
                return new NotifyProcessResult(0, JsonSerializer.Serialize(new
                {
                    result = new
                    {
                        agent = new { name = "implementation", workspace_id = "wG659", pane_id = "wG659:p2", agent_status = "done", state_change_seq = 8L, last_state_change_at = nowString },
                    },
                }), "");
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new NotifyProcessResult(1, "", "cancelled");
        }

        private const string nowString = "2026-08-10T12:00:01Z";
    }
}
