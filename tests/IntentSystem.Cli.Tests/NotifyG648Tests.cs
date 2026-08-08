using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyG648Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string WorkspaceId = "wG648";
    private const string PaneId = "wG648:p2";
    private const string Cwd = "/tmp/g648-role";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g648-").FullName;

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatusSerializesTheDistinctLivenessStateAndResendPermission_G648()
    {
        WriteTopology();
        WriteDispatch("G648-status", "implementation");
        var runner = new FakeRunner((_, arguments) =>
            Is(arguments, "agent", "list")
                ? Success("{\"result\":{\"agents\":[]}}")
                : Is(arguments, "pane", "process-info")
                    ? Success(ProcessInfo(new NotifyPaneProcess(6480, Cwd, "codex")))
                    : throw new InvalidOperationException("status must not prompt"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "notify", "status", "--domain", Domain, "--team", Team,
                "--task-id", "G648-status", "--format", "json",
            ],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.GetProperty("liveness_state").GetString());
        Assert.True(result.GetProperty("process_present").GetBoolean());
        Assert.True(result.GetProperty("resend_permitted").GetBoolean());
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.GetProperty("verdict").GetString());
    }

    [Fact]
    public void LivenessNamesRegistrationLossOnlyWhenProcessIsPresent_G648()
    {
        var record = Record("G648-liveness");
        var runner = new FakeRunner((_, arguments) =>
            Is(arguments, "agent", "list")
                ? Success(Roster(StoppedAgent()))
                : Is(arguments, "pane", "process-info")
                    ? Success(ProcessInfo(new NotifyPaneProcess(6481, Cwd, "codex")))
                    : throw new InvalidOperationException("unexpected transport call"));

        var result = NotifyPendingLiveness.Probe(
            root,
            record,
            SessionLayerMode.HerdrOnly,
            runner,
            "fake-herdr",
            root);

        Assert.True(result.Resolved);
        Assert.False(result.Running);
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.State);
        Assert.True(result.ProcessPresent);
        Assert.True(result.ResendPermitted);
        Assert.Contains("re-register", result.Summary, StringComparison.OrdinalIgnoreCase);

        runner = new FakeRunner((_, arguments) =>
            Is(arguments, "agent", "list")
                ? Success(Roster(StoppedAgent()))
                : Is(arguments, "pane", "process-info")
                    ? Success(ProcessInfo())
                    : throw new InvalidOperationException("unexpected transport call"));
        var genuineLoss = NotifyPendingLiveness.Probe(
            root,
            record,
            SessionLayerMode.HerdrOnly,
            runner,
            "fake-herdr",
            root);

        Assert.True(genuineLoss.Resolved);
        Assert.False(genuineLoss.Running);
        Assert.Equal("lost", genuineLoss.State);
        Assert.False(genuineLoss.ProcessPresent);
    }

    [Fact]
    public void DeliveryUsesCorroboratedStateAndPermitsResendWithoutLifecycleAction_G648()
    {
        WriteTopology();
        var runner = new FakeRunner((_, arguments) =>
            Is(arguments, "agent", "list")
                ? Success("{\"result\":{\"agents\":[]}}")
                : Is(arguments, "pane", "process-info")
                    ? Success(ProcessInfo(new NotifyPaneProcess(6482, Cwd, "codex")))
                    : throw new InvalidOperationException("delivery must not prompt a registration-loss pane"));
        var transport = new HerdrNotifyTransport(runner, "fake-herdr");

        var result = transport.Deliver(
            root,
            Domain,
            Team,
            "orchestration",
            "implementation",
            ["orchestration", "implementation"],
            "payload",
            write: true);

        Assert.False(result.Resolved);
        Assert.False(result.Delivered);
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.Cause);
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.ReceiverStateOutcome);
        Assert.True(result.ResendPermitted);
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "prompt"));
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "kill" || Is(call.Arguments, "agent", "start"));
    }

    [Fact]
    public void AbsenceLikePromptTextCannotOverrideProcessCorroboration_G648()
    {
        WriteTopology();
        var runner = new FakeRunner((_, arguments) =>
            Is(arguments, "agent", "list")
                ? Success(Roster(RunningAgent()))
                : Is(arguments, "agent", "prompt")
                    ? new NotifyProcessResult(1, string.Empty, "pane absent: agent_not_found")
                    : Is(arguments, "pane", "process-info")
                        ? Success(ProcessInfo(new NotifyPaneProcess(6483, Cwd, "codex")))
                        : throw new InvalidOperationException("unexpected transport call"));
        var transport = new HerdrNotifyTransport(runner, "fake-herdr");

        var result = transport.Deliver(
            root,
            Domain,
            Team,
            "orchestration",
            "implementation",
            ["orchestration", "implementation"],
            "payload",
            write: true);

        Assert.False(result.Resolved);
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.Cause);
        Assert.True(result.ResendPermitted);
        Assert.DoesNotContain("pane-absent", result.Cause, StringComparison.Ordinal);
        Assert.Contains(runner.Calls, call => Is(call.Arguments, "agent", "prompt"));
        Assert.Contains(runner.Calls, call => Is(call.Arguments, "pane", "process-info"));
    }

    [Fact]
    public void SupervisionEmitsOneRegistrationFindingPerPanePerCycle_G648()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology(twoRolesAtSamePane: true);
        WriteDispatch("G648-pane-a", "implementation");
        WriteDispatch("G648-pane-b", "review");
        var runner = new FakeRunner((_, arguments) =>
            Is(arguments, "agent", "list")
                ? Success("{\"result\":{\"agents\":[]}}")
                : Is(arguments, "pane", "process-info")
                    ? Success(ProcessInfo(new NotifyPaneProcess(6484, Cwd, "codex")))
                    : throw new InvalidOperationException("registration loss must not mutate a pane"));
        var supervisor = new NotifyMeasuredSupervisor(
            context,
            root,
            Domain,
            Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 30,
            declaredBoundSeconds: null,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: false,
            format: "json",
            runner,
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: root);

        var pass = supervisor.RunOnce();
        var findings = pass.Findings
            .Where(finding => finding.Kind == NotifyPendingLivenessResult.RegistrationLostProcessPresent)
            .ToArray();

        Assert.Equal(2, pass.Actions.Count);
        Assert.All(pass.Actions, action => Assert.True(action.ResendPermitted));
        var paneFindings = findings.Where(finding => finding.Key == $"registration:{WorkspaceId}:{PaneId}").ToArray();
        Assert.Single(paneFindings);
        Assert.True(paneFindings[0].ResendPermitted);
        Assert.Contains("re-register", paneFindings[0].Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "kill");
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "start"));
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "prompt"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GuidanceAndLedgerNameTheCorroboratedPreviewState_G648(string language)
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var guidance = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
        var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));

        Assert.Contains("G648", guidance, StringComparison.Ordinal);
        Assert.Contains("registration-lost-process-present", guidance, StringComparison.Ordinal);
        Assert.Contains("resend_permitted", guidance, StringComparison.Ordinal);
        Assert.Contains("kill", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registration-lost-process-present", ledger, StringComparison.Ordinal);
        Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
    }

    private NotifyPendingDelegation Record(string taskId, string role = "implementation") => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = taskId,
        DelegatingRole = "orchestration",
        RecipientRole = role,
        ReportToRole = "orchestration",
        RecipientIdentity = $"role={role};workspace={WorkspaceId};pane={PaneId}",
        ExpectedArtifact = "draft PR",
        ExpectedArtifacts = ["draft PR"],
        Objective = "corroborate herdr registration loss",
        Inputs = ["issue #1399"],
        DispatchedAt = DateTimeOffset.UtcNow,
        TransportMode = SessionLayerMode.HerdrOnly,
        Resident = NotifyRecordedRole.HerdrResident,
        WorkspaceId = WorkspaceId,
        PaneId = PaneId,
        Cwd = Cwd,
        Kind = "codex",
        LaunchArguments = ["--sandbox"],
    };

    private void WriteDispatch(string taskId, string role)
    {
        var result = NotifyPendingDelegationStore.WriteDispatch(root, Record(taskId, role));
        Assert.True(result.Written, result.Error);
    }

    private void RecordMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
            writer));
    }

    private void WriteTopology(bool twoRolesAtSamePane = false)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var reviewPane = twoRolesAtSamePane ? PaneId : "wG648:p3";
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = WorkspaceId,
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = WorkspaceId, pane_id = "wG648:p1" },
                ["implementation"] = new { resident = "herdr", workspace_id = WorkspaceId, pane_id = PaneId },
                ["review"] = new { resident = "herdr", workspace_id = WorkspaceId, pane_id = reviewPane },
            },
        }));
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private static bool Is(IReadOnlyList<string> arguments, params string[] expected) =>
        arguments.Take(expected.Length).SequenceEqual(expected);

    private static NotifyProcessResult Success(string output = "") => new(0, output, string.Empty);

    private static string Roster(params object[] agents) =>
        JsonSerializer.Serialize(new { result = new { agents } });

    private static object RunningAgent() => new
    {
        name = "implementation",
        workspace_id = WorkspaceId,
        pane_id = PaneId,
        agent = "codex",
        agent_session = new { id = "implementation" },
        agent_status = "working",
        interactive_ready = true,
    };

    private static object StoppedAgent() => new
    {
        name = "implementation",
        workspace_id = WorkspaceId,
        pane_id = PaneId,
        agent = "codex",
        agent_status = "unknown",
        interactive_ready = false,
    };

    private static string ProcessInfo(params NotifyPaneProcess[] processes) =>
        JsonSerializer.Serialize(new
        {
            result = new
            {
                process_info = new
                {
                    foreground_processes = processes.Select(process => new
                    {
                        pid = process.Pid,
                        cwd = process.Cwd,
                        name = process.Name,
                    }),
                },
            },
        });

    private sealed class FakeRunner(
        Func<string, IReadOnlyList<string>, NotifyProcessResult> handler) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            return handler(fileName, arguments);
        }
    }
}
