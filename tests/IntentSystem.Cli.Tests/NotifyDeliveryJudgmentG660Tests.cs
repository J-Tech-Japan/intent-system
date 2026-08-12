using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyDeliveryJudgmentG660Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "g660-team";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g660-").FullName;
    private readonly DateTimeOffset now = new(2026, 8, 10, 18, 0, 0, TimeSpan.Zero);

    public NotifyDeliveryJudgmentG660Tests()
    {
        NotifyCommand.UtcNowFactory = () => now;
        NotifySupervisor.Delay = _ => { };
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ExternalReaderAppendIsTheNamedJudgmentForStatusEscalateAndSupervise_G660()
    {
        WriteExternalDesignTopology();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G660-status",
            RecipientRole = "design",
            RecipientIdentity = $"role=design;reader=.intent-cli/events/{Team}.jsonl",
            ExpectedArtifact = "decision",
            DispatchedAt = now.AddMinutes(-1),
            TransportMode = SessionLayerMode.Agmsg,
            Resident = NotifyRecordedRole.ExternalResident,
            Reader = $".intent-cli/events/{Team}.jsonl",
        }).Written);

        var (statusExit, status) = Run([
            "notify", "status", "--domain", Domain, "--team", Team,
            "--task-id", "G660-status", "--format", "json",
        ]);
        Assert.Equal(0, statusExit);
        Assert.Equal("live", status.GetProperty("verdict").GetString());
        Assert.Equal(
            NotifyRecipientDeliveryJudgment.RecordedReaderAppendBasis,
            status.GetProperty("delivery_basis").GetString());

        NotifyCommand.ProcessRunnerFactory = () => throw new InvalidOperationException(
            "reader delivery and escalation must not start a pane transport");
        var (escalateExit, escalation) = Run(EscalateArgs("G660-reader"));
        Assert.Equal(0, escalateExit);
        Assert.True(escalation.GetProperty("delivered").GetBoolean());
        Assert.True(escalation.GetProperty("event_appended").GetBoolean());
        Assert.Equal(
            NotifyRecipientDeliveryJudgment.RecordedReaderAppendBasis,
            escalation.GetProperty("delivery_basis").GetString());

        var runner = new FakeRunner();
        var pass = CreateSupervisor(runner).RunOnce();
        Assert.DoesNotContain(pass.Findings, finding => finding.Kind == "undelivered-escalation");
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void FailedReaderAppendRemainsARealFindingAndIsNotCleared_G660()
    {
        WriteExternalDesignTopology();
        var eventPath = ResolveEventPath();
        Directory.CreateDirectory(eventPath);

        var (exitCode, escalation) = Run(EscalateArgs("G660-failed-append"));

        Assert.Equal(1, exitCode);
        Assert.False(escalation.GetProperty("delivered").GetBoolean());
        Assert.Equal("event-append-failed", escalation.GetProperty("cause").GetString());
        Assert.Equal(
            NotifyRecipientDeliveryJudgment.RecordedReaderAppendBasis,
            escalation.GetProperty("delivery_basis").GetString());
        Assert.True(File.Exists(NotifyEscalationFailureStore.ResolvePath(root, Domain, Team)));

        var supervisor = CreateSupervisor(new FakeRunner());
        var first = supervisor.RunOnce();
        var finding = Assert.Single(first.Findings, item =>
            item.Kind == "undelivered-escalation"
            && item.Source == "notify-escalate-append-failed");
        Assert.Contains("recorded-reader-append", finding.Summary, StringComparison.Ordinal);

        var second = supervisor.RunOnce();
        Assert.DoesNotContain(second.Findings, item => item.Key == finding.Key);
        var state = NotifySupervisionStore.Read(CreateContext().ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.Contains(finding.Key, state.ActiveStalls.Keys);
        Assert.DoesNotContain(state.StallHistory, item => item.Key == finding.Key && item.ClearedAt is not null);
    }

    [Fact]
    public void ExistingReaderFalsePositiveClearsExactlyOnceOnTheNextCycle_G660()
    {
        WriteExternalDesignTopology();
        var timestamp = now.AddMinutes(-5);
        NotifyEventWriter.Append(ResolveEventPath(), new NotifyDesignEvent
        {
            Timestamp = timestamp,
            Team = Team,
            Kind = "escalation",
            Unit = "G660-existing-false-positive",
            Summary = "already delivered to the recorded reader",
            Artifact = "decision",
        });
        var key = $"escalation:G660-existing-false-positive:{timestamp.UtcTicks}";
        var stallPath = NotifySupervisionStore.ResolveStallPath(
            CreateContext().ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.True(NotifySupervisionStore.OpenStall(stallPath, new NotifySupervisionStallRecord
        {
            Key = key,
            Kind = "undelivered-escalation",
            OwnerRole = "orchestration",
            Source = "notify-escalate",
            Summary = "legacy reader false positive",
            SurfacedAt = now.AddMinutes(-4),
            DetectableAtUnknown = true,
            WakeAttempted = true,
            WakeDelivered = false,
        }, write: true).Applied);

        var supervisor = CreateSupervisor(new FakeRunner());
        var first = supervisor.RunOnce();
        Assert.DoesNotContain(first.Findings, item => item.Key == key);
        Assert.Contains(first.RecoveryRecords, item => item.Key == key && item.ClearedAt == now);

        var second = supervisor.RunOnce();
        Assert.DoesNotContain(second.Findings, item => item.Key == key);
        var clearLines = File.ReadLines(stallPath).Count(line =>
            line.Contains("\"kind\":\"clear\"", StringComparison.Ordinal)
            && line.Contains(key, StringComparison.Ordinal));
        Assert.Equal(1, clearLines);
        Assert.Single(File.ReadLines(ResolveEventPath()));
    }

    [Fact]
    public void PaneResidentThirtyFiveMinuteG641IncidentStillRaisesAndUsesWakeBasis_G660()
    {
        WritePaneDesignTopology();
        RecordMode(SessionLayerMode.HerdrOnly);
        var runner = new FakeRunner { AgentsJson = RunningPaneAgentsJson() };
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var (escalateExit, escalation) = Run(EscalateArgs("G641-thirty-five-minute-approval"));
        Assert.Equal(0, escalateExit);
        Assert.False(escalation.GetProperty("delivered").GetBoolean());
        Assert.Equal(
            NotifyRecipientDeliveryJudgment.RecordedPaneWakeBasis,
            escalation.GetProperty("delivery_basis").GetString());

        // Reconstruct the measured incident age without changing its six-field
        // event shape: the approval was durable while the pane stayed unheard.
        var eventPath = ResolveEventPath();
        using (var document = JsonDocument.Parse(File.ReadAllText(eventPath)))
        {
            var root = document.RootElement;
            File.WriteAllText(eventPath, JsonSerializer.Serialize(new NotifyDesignEvent
            {
                Timestamp = now.AddMinutes(-35),
                Team = root.GetProperty("team").GetString()!,
                Kind = root.GetProperty("kind").GetString()!,
                Unit = root.GetProperty("unit").GetString()!,
                Summary = root.GetProperty("summary").GetString()!,
                Artifact = root.GetProperty("artifact").GetString()!,
            }) + Environment.NewLine);
        }

        var pass = CreateSupervisor(runner).RunOnce();
        var finding = Assert.Single(pass.Findings, item =>
            item.Kind == "undelivered-escalation"
            && item.Key.Contains("G641-thirty-five-minute-approval", StringComparison.Ordinal));
        Assert.Contains("recorded-pane-wake", finding.Summary, StringComparison.Ordinal);
        Assert.True(finding.WakeDelivered);
        Assert.Contains(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG660:p1"]));
    }

    private NotifyMeasuredSupervisor CreateSupervisor(FakeRunner runner) => new(
        CreateContext(),
        root,
        Domain,
        Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 300,
        declaredBoundSeconds: 300,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write: true,
        format: "json",
        runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: "unused-agmsg");

    private (int ExitCode, JsonElement Result) Run(string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private string[] EscalateArgs(string taskId) =>
    [
        "notify", "escalate", "--domain", Domain, "--team", Team,
        "--from", "orchestration", "--task-id", taskId,
        "--artifact", "approval", "--summary", "design decision",
        "--write", "--format", "json",
    ];

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private void WriteExternalDesignTopology() => WriteTopology(new
    {
        resident = "external",
        frontend = "claude-app",
        reader = $".intent-cli/events/{Team}.jsonl",
    });

    private void WritePaneDesignTopology() => WriteTopology(new
    {
        resident = "herdr",
        workspace_id = "wG660",
        pane_id = "wG660:p0",
    }, includeOrchestration: true);

    private void WriteTopology(object design, bool includeOrchestration = false)
    {
        var roles = new Dictionary<string, object> { ["design"] = design };
        if (includeOrchestration)
        {
            roles["orchestration"] = new
            {
                resident = "herdr",
                workspace_id = "wG660",
                pane_id = "wG660:p1",
            };
        }

        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG660",
            roles,
        }));
    }

    private void RecordMode(string mode)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            CreateContext(),
            ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
            writer));
    }

    private string ResolveEventPath()
    {
        Assert.True(NotifyEventWriter.TryResolveWritePath(root, Domain, Team, out var path, out var error), error);
        return path;
    }

    private static string RunningPaneAgentsJson() => JsonSerializer.Serialize(new
    {
        result = new
        {
            agents = new[]
            {
                new
                {
                    name = "design", workspace_id = "wG660", pane_id = "wG660:p0", agent = "codex",
                    agent_session = new { id = "design" }, agent_status = "working", interactive_ready = true,
                },
                new
                {
                    name = "orchestration", workspace_id = "wG660", pane_id = "wG660:p1", agent = "codex",
                    agent_session = new { id = "orchestration" }, agent_status = "working", interactive_ready = true,
                },
            },
        },
    });

    private sealed class FakeRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public string AgentsJson { get; set; } = "{\"result\":{\"agents\":[]}}";

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, string.Empty);
            }

            if (arguments.Take(2).SequenceEqual(["pane", "process-info"]))
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
