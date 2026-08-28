using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG748Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"intent-g748-{Guid.NewGuid():N}");
    private readonly DateTimeOffset firstNow = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG748Tests()
    {
        Directory.CreateDirectory(root);
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
        NotifyCommand.BashExecutableFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        NotifyPendingDelegationStore.WriteOverride = null;
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeliveredDoneDelegationFiresAfterWindow_G748()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        var pending = WriteDeliveredPending("G748-done");
        var runner = new FixtureRunner { AgentStatus = "done", StateChangeSequence = 14 };
        var supervisor = CreateSupervisor(context, runner, delegationWindowSeconds: 300);

        supervisor.RunOnce();
        now = firstNow.AddSeconds(301);
        var pass = supervisor.RunOnce();

        var finding = Assert.Single(
            pass.Findings,
            item => item.Kind == "delegation-delivered-never-executed");
        Assert.Contains("G748-done", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("agent_status=done", string.Join("\n", finding.ConsultedObservations!));
        Assert.Contains("task_id:G748-done", finding.Evidence!, StringComparer.Ordinal);
        Assert.Contains("seat 'role=implementation;workspace=wG748;pane=wG748:p2'", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("300s execution-start window", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("Canonical report absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("expected artifact absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("durable target-entity transition absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("window_seconds:300", finding.Evidence!, StringComparer.Ordinal);
        Assert.Equal(pending.TaskId, NotifyPendingDelegationStore.Find(
            root,
            Domain,
            Team,
            pending.TaskId).Record!.TaskId);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        FixtureRunner runner,
        int delegationWindowSeconds) => new(
        context: context,
        routingRoot: root,
        domain: Domain,
        team: Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 600,
        declaredBoundSeconds: null,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write: true,
        format: "json",
        runner: runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: root,
        delegationExecutionWindowSeconds: delegationWindowSeconds);

    private NotifyPendingDelegation WriteDeliveredPending(string taskId)
    {
        var pending = WriteDispatchedPending(taskId);
        var delivery = NotifyDelegationDeliveryStore.Write(root, pending, firstNow);
        Assert.True(delivery.Written, delivery.Error);
        return pending;
    }

    private NotifyPendingDelegation WriteDispatchedPending(string taskId)
    {
        var pending = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = taskId,
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            ReportToRole = "orchestration",
            RecipientIdentity = "role=implementation;workspace=wG748;pane=wG748:p2",
            ExpectedArtifact = "expected-artifact.txt",
            ExpectedArtifacts = ["expected-artifact.txt"],
            Objective = "execute the delegated task",
            Inputs = ["fixture"],
            ResultNonce = $"{taskId}-nonce",
            DispatchedAt = firstNow.AddSeconds(-1),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG748",
            PaneId = "wG748:p2",
            Cwd = Path.Combine(root, "work", taskId),
            Kind = "implementation",
            LaunchArguments = ["fixture"],
        };
        var write = NotifyPendingDelegationStore.WriteDispatch(root, pending);
        Assert.True(write.Written, write.Error);
        return pending;
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private void RecordHerdrOnlyMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
    }

    private void WriteTopology()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG748",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new { resident = "herdr", workspace_id = "wG748", pane_id = "wG748:p1" },
                    ["implementation"] = new { resident = "herdr", workspace_id = "wG748", pane_id = "wG748:p2" },
                },
            }));
    }

    private static string AgentsJson(string status, long sequence)
    {
        static object Agent(string role, string pane, string status, long sequence) => new
        {
            name = role,
            workspace_id = "wG748",
            pane_id = pane,
            agent = "fixture",
            agent_session = new { id = role },
            agent_status = status,
            agent_running = true,
            interactive_ready = true,
            state_change_seq = sequence,
            last_state_change_at = "2026-08-28T12:00:00.0000000+00:00",
        };

        return JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    Agent("orchestration", "wG748:p1", "working", 1),
                    Agent("implementation", "wG748:p2", status, sequence),
                },
            },
        });
    }

    private sealed class FixtureRunner : INotifyProcessRunner
    {
        public string AgentStatus { get; set; } = "done";
        public long StateChangeSequence { get; set; } = 14;

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(
                    0,
                    AgentsJson(AgentStatus, StateChangeSequence),
                    string.Empty);
            }

            if (arguments.Count >= 2
                && arguments[0] == "pane"
                && arguments[1] == "process-info")
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            if (arguments.Count >= 2
                && arguments[0] == "agent"
                && arguments[1] is "prompt" or "wait")
            {
                return new NotifyProcessResult(0, string.Empty, string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
