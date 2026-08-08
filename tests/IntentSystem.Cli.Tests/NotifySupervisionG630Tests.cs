using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG630Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Role = "implementation";
    private const string DelegatingRole = "orchestration";
    private const string WorkspaceId = "wG630";
    private const string PaneId = "wG630:p2";
    private const string Cwd = "/tmp/g630-role";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g630-").FullName;

    public NotifySupervisionG630Tests()
    {
        NotifySupervisor.NonceFactory = () => "g630-ready";
        NotifySupervisor.Delay = _ => { };
    }

    public void Dispose()
    {
        NotifySupervisor.NonceFactory = () => Guid.NewGuid().ToString("N");
        NotifySupervisor.Delay = Thread.Sleep;
        NotifyPendingDelegationStore.WriteOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LiveRecipient_IsSilentAcrossSeveralWakes_G630()
    {
        var runner = new FakeRunner((_, arguments, _) =>
            Is(arguments, "agent", "list")
                ? Success(Roster(RunningAgent()))
                : throw new InvalidOperationException($"unexpected command: {string.Join(' ', arguments)}"));
        WriteDispatch();
        var supervisor = CreateSupervisor(runner, write: true);

        for (var wake = 0; wake < 3; wake++)
        {
            var pass = supervisor.RunOnce();
            Assert.True(pass.Silent);
            Assert.Empty(pass.Actions);
            Assert.Null(pass.Error);
        }

        Assert.Equal(3, runner.Calls.Count(call => Is(call.Arguments, "agent", "list")));
    }

    [Fact]
    public void SettledRecord_IsExcludedWithoutAProbe_G630()
    {
        var record = WriteDispatch();
        var report = NotifyPendingDelegationStore.WriteReport(
            root,
            record,
            "completed",
            "draft PR",
            "settled",
            DateTimeOffset.UtcNow);
        Assert.True(report.Written, report.Error);

        var runner = new FakeRunner((_, arguments, _) =>
            throw new InvalidOperationException($"settled records must not probe: {string.Join(' ', arguments)}"));
        var pass = CreateSupervisor(runner, write: true).RunOnce();

        Assert.True(pass.Silent);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void LostRecipient_RecoversInOrder_AndNotifiesTheDelegatingRole_G630()
    {
        var runner = RecoveryRunner(response: "READY g630-ready\n");
        WriteDispatch();
        string? notification = null;
        var supervisor = CreateSupervisor(
            runner,
            write: true,
            notifier: (_, payload) =>
            {
                notification = payload;
                return DeliverySuccess("delegating role notified");
            });

        var pass = supervisor.RunOnce();
        var action = Assert.Single(pass.Actions);

        Assert.Equal(0, pass.ExitCode);
        Assert.Equal("recovered-and-reported", action.Outcome);
        Assert.True(action.Recovered);
        Assert.Equal("g630-ready", action.ReadinessNonce);
        Assert.NotNull(notification);
        using var notice = JsonDocument.Parse(notification!);
        Assert.Equal("G630-demo", notice.RootElement.GetProperty("task_id").GetString());
        Assert.True(notice.RootElement.GetProperty("recovered").GetBoolean());
        Assert.True(notice.RootElement.GetProperty("lost").GetBoolean());
        Assert.True(notice.RootElement.GetProperty("must_redispatch").GetBoolean());
        Assert.False(notice.RootElement.GetProperty("auto_redispatched").GetBoolean());
        Assert.Contains("lost", notice.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("must be re-dispatched", notice.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var commands = runner.Calls.Select(call => call.Arguments.ToArray()).ToArray();
        Assert.Equal(["agent", "list"], commands[0]);
        Assert.Equal(["pane", "process-info", "--pane", PaneId], commands[1]);
        Assert.Equal(["agent", "list"], commands[2]);
        Assert.Equal(["pane", "process-info", "--pane", PaneId], commands[3]);
        Assert.Equal(["-TERM", "7001"], commands[4]);
        Assert.Equal(["pane", "process-info", "--pane", PaneId], commands[5]);
        Assert.Equal(["agent", "list"], commands[6]);
        Assert.Equal(["agent", "start", Role, "--kind", "codex", "--pane", PaneId, "--timeout", "10000", "--", "--sandbox"], commands[7]);
        Assert.Equal(["agent", "list"], commands[8]);
        Assert.Equal(["agent", "prompt", PaneId], commands[9].Take(3));
        Assert.Contains("READY g630-ready", commands[9][3], StringComparison.Ordinal);
        Assert.Equal(["agent", "read", PaneId, "--source", "recent-unwrapped", "--lines", "200"], commands[10]);
    }

    [Fact]
    public void ReadinessGate_RejectsAnUnsentPromptEcho_G630()
    {
        var runner = RecoveryRunner(response: "composer (unsent): Register this role READY g630-ready\n");
        WriteDispatch();
        var notificationCount = 0;
        var supervisor = CreateSupervisor(
            runner,
            write: true,
            notifier: (_, _) =>
            {
                notificationCount++;
                return DeliverySuccess("must not be called");
            });

        var pass = supervisor.RunOnce();
        var action = Assert.Single(pass.Actions);

        Assert.Equal("stopped-fail-closed", action.Outcome);
        Assert.Equal("readiness-unproven", action.Cause);
        Assert.False(action.Recovered);
        Assert.Equal(0, notificationCount);
        Assert.Equal(4, runner.Calls.Count(call => Is(call.Arguments, "agent", "read")));
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "prompt") && call.Arguments.Count > 3 && call.Arguments[3].Contains("loss", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProcessOwnershipMismatch_StopsBeforeKillOrReplacement_G630()
    {
        var runner = RecoveryRunner(response: "READY g630-ready\n", oldCwd: "/tmp/other-role");
        WriteDispatch();
        var action = Assert.Single(CreateSupervisor(runner, write: true).RunOnce().Actions);

        Assert.Equal("process-attribution-unverified", action.Cause);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "kill");
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "start"));
    }

    [Fact]
    public void OldProcessStillPresent_StopsBeforeReplacementStart_G630()
    {
        var runner = RecoveryRunner(response: "READY g630-ready\n", oldProcessRemains: true);
        WriteDispatch();
        var action = Assert.Single(CreateSupervisor(runner, write: true).RunOnce().Actions);

        Assert.Equal("old-process-still-present", action.Cause);
        Assert.Contains(runner.Calls, call => call.FileName == "kill");
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "start"));
    }

    [Fact]
    public void AutoRedispatch_IsOffByDefault_AndRunsOnlyAfterReadinessWhenEnabled_G630()
    {
        WriteDispatch();
        var offRunner = RecoveryRunner(response: "READY g630-ready\n");
        var offRedispatches = 0;
        var off = CreateSupervisor(
            offRunner,
            write: true,
            autoRedispatch: false,
            redispatch: _ =>
            {
                offRedispatches++;
                return RedispatchSuccess();
            },
            notifier: (_, _) => DeliverySuccess("reported"));
        var offAction = Assert.Single(off.RunOnce().Actions);
        Assert.Equal("recovered-and-reported", offAction.Outcome);
        Assert.False(offAction.AutoRedispatched);
        Assert.Equal(0, offRedispatches);

        Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        WriteDispatch();
        var onRunner = RecoveryRunner(response: "READY g630-ready\n");
        var order = new List<string>();
        var on = CreateSupervisor(
            onRunner,
            write: true,
            autoRedispatch: true,
            redispatch: _ =>
            {
                order.Add("redispatch");
                return RedispatchSuccess();
            },
            notifier: (_, payload) =>
            {
                order.Add("notify");
                using var notice = JsonDocument.Parse(payload);
                Assert.True(notice.RootElement.GetProperty("auto_redispatched").GetBoolean());
                Assert.False(notice.RootElement.GetProperty("must_redispatch").GetBoolean());
                return DeliverySuccess("reported");
            });

        var onAction = Assert.Single(on.RunOnce().Actions);
        Assert.Equal("recovered-and-redispatched", onAction.Outcome);
        Assert.True(onAction.AutoRedispatched);
        Assert.Equal(["redispatch", "notify"], order);
    }

    [Fact]
    public void DryRunLostPass_IsInformativeButNeverMutatesTheSeat_G630()
    {
        var runner = RecoveryRunner(response: "READY g630-ready\n");
        WriteDispatch();
        var pass = CreateSupervisor(runner, write: false).RunOnce();
        var action = Assert.Single(pass.Actions);

        Assert.Equal("dry-run-would-recover", action.Outcome);
        Assert.DoesNotContain(runner.Calls, call => call.FileName == "kill");
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "start"));
        Assert.DoesNotContain(runner.Calls, call => Is(call.Arguments, "agent", "prompt"));
    }

    private NotifySupervisor CreateSupervisor(
        FakeRunner runner,
        bool write,
        bool autoRedispatch = false,
        Func<NotifyPendingDelegation, NotifySupervisorRedispatchResult>? redispatch = null,
        Func<NotifyPendingDelegation, string, NotifySupervisorDeliveryResult>? notifier = null) => new(
            CreateContext(),
            root,
            Domain,
            Team,
            autoRedispatch,
            write,
            "json",
            runner,
            "fake-herdr",
            root,
            redispatch,
            notifier);

    private NotifyPendingDelegation WriteDispatch()
    {
        var record = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G630-demo",
            DelegatingRole = DelegatingRole,
            RecipientRole = Role,
            ReportToRole = "review",
            RecipientIdentity = $"role={Role};workspace={WorkspaceId};pane={PaneId}",
            ExpectedArtifact = "draft PR",
            ExpectedArtifacts = ["draft PR"],
            Objective = "Recover one lost recipient",
            Inputs = ["issue #1375"],
            ResultNonce = "original-nonce",
            DispatchedAt = DateTimeOffset.UtcNow,
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = WorkspaceId,
            PaneId = PaneId,
            Cwd = Cwd,
            Kind = "codex",
            LaunchArguments = ["--sandbox"],
        };
        var result = NotifyPendingDelegationStore.WriteDispatch(root, record);
        Assert.True(result.Written, result.Error);
        return record;
    }

    private FakeRunner RecoveryRunner(
        string response,
        string oldCwd = Cwd,
        bool oldProcessRemains = false) => new((_, arguments, occurrence) =>
    {
        if (Is(arguments, "agent", "list"))
        {
            var started = occurrence("agent-list") >= 4;
            return Success(Roster(started ? RunningAgent() : StoppedAgent()));
        }

        if (Is(arguments, "pane", "process-info"))
        {
            var processInfoCount = occurrence("process-info");
            return Success(processInfoCount >= 2 && (oldProcessRemains || processInfoCount == 2)
                ? ProcessInfo(new NotifySupervisorProcess(7001, oldCwd, "codex"))
                : ProcessInfo());
        }

        if (arguments.Count == 2 && arguments[0] == "-TERM") return Success();
        if (Is(arguments, "agent", "start")) return Success();
        if (Is(arguments, "agent", "prompt")) return Success();
        if (Is(arguments, "agent", "read")) return Success(response);
        throw new InvalidOperationException($"unexpected command: {string.Join(' ', arguments)}");
    });

    private static bool Is(IReadOnlyList<string> arguments, params string[] expected) =>
        arguments.Take(expected.Length).SequenceEqual(expected);

    private static NotifyProcessResult Success(string output = "") => new(0, output, "");

    private static NotifySupervisorDeliveryResult DeliverySuccess(string summary) => new()
    {
        Resolved = true,
        Delivered = true,
        Summary = summary,
    };

    private static NotifySupervisorRedispatchResult RedispatchSuccess() => new()
    {
        Resolved = true,
        Redispatched = true,
        Summary = "redispatched",
    };

    private static string Roster(params object[] agents) =>
        JsonSerializer.Serialize(new { result = new { agents } });

    private static object RunningAgent() => new
    {
        name = Role,
        workspace_id = WorkspaceId,
        pane_id = PaneId,
        cwd = Cwd,
        agent = "codex",
        agent_session = new { id = "session" },
        agent_status = "working",
        interactive_ready = true,
    };

    private static object StoppedAgent() => new
    {
        name = Role,
        workspace_id = WorkspaceId,
        pane_id = PaneId,
        cwd = Cwd,
        agent = "codex",
        agent_status = "done",
        interactive_ready = false,
    };

    private static string ProcessInfo(params NotifySupervisorProcess[] processes) =>
        JsonSerializer.Serialize(new
        {
            result = new
            {
                process_info = new
                {
                    pane_id = PaneId,
                    foreground_processes = processes.Select(process => new
                    {
                        pid = process.Pid,
                        cwd = process.Cwd,
                        name = process.Name,
                    }),
                },
            },
        });

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

    private sealed class FakeRunner(
        Func<string, IReadOnlyList<string>, Func<string, int>, NotifyProcessResult> handler) : INotifyProcessRunner
    {
        private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);

        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            return handler(fileName, arguments, key =>
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
                return counts[key];
            });
        }
    }
}
