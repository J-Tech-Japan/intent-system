using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G707: a supervision conclusion must be corroborated by non-terminal
/// observations from the same cycle.  Fixtures are unique and intentionally
/// retained; these tests never delete a system-temporary path.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG707Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"intent-g707-{Guid.NewGuid():N}");
    private readonly DateTimeOffset firstNow = new(2026, 8, 14, 14, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG707Tests()
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
        NotifySupervisor.Delay = Thread.Sleep;
    }

    [Fact]
    public void SameCycleRegistrationContradictionEmitsOneSelfVerifyingConflictWithoutRecoveryWords_G707()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        var runner = new FakeRunner
        {
            AgentsJson = AgentsWithImplementation(
                status: "idle",
                stateChangeSequence: 12,
                interactiveReady: false,
                includeImplementationSession: false),
            ForegroundProcessCount = 1,
        };

        var pass = CreateSupervisor(context, runner, write: false).RunOnce();

        Assert.DoesNotContain(pass.Findings, finding => finding.Kind == "registration-lost-process-present");
        var conflict = Assert.Single(pass.Findings, finding => finding.Kind == "observation-conflict");
        Assert.Equal("observation-conflict:wG707:wG707:p2", conflict.Key);
        Assert.Equal("supervision-cycle.corroboration", conflict.Source);
        Assert.NotNull(conflict.RegistrationDefinition);
        Assert.NotNull(conflict.RegistrationLookup);
        Assert.NotNull(conflict.RegistrationResult);
        Assert.NotNull(conflict.ConsultedObservations);
        Assert.Contains("agent_status='idle'", conflict.Summary, StringComparison.Ordinal);
        Assert.Contains("Verification first", conflict.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("re-register", conflict.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("restart", conflict.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kill", conflict.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("restart"));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("kill"));
    }

    [Fact]
    public void GenuineAbsentSeatRemainsEligibleWithoutSameCycleContradiction_G707()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        var runner = new FakeRunner
        {
            AgentsJson = "{\"result\":{\"agents\":[]}}",
            ForegroundProcessCount = 0,
        };

        var pass = CreateSupervisor(context, runner, write: false).RunOnce();

        var absent = Assert.Single(pass.Findings, finding =>
            finding.Kind == "seat-absent" && finding.SubjectRole == "implementation");
        Assert.DoesNotContain(pass.Findings, finding => finding.Kind == "observation-conflict");
        Assert.Equal("registration-missing; foreground-processes-absent", absent.RegistrationResult);
        Assert.NotNull(absent.RegistrationDefinition);
        Assert.NotNull(absent.RegistrationLookup);
        Assert.Contains(absent.ConsultedObservations!, item => item.Contains("foreground_processes=0", StringComparison.Ordinal));
    }

    [Fact]
    public void ConflictRecurrenceUsesG699BackoffAndParkInsteadOfDroppingEvidence_G707()
    {
        var context = CreateContext();
        RecordMode(context);
        WriteTopology();
        var runner = new FakeRunner
        {
            AgentsJson = AgentsWithImplementation(
                status: "idle",
                stateChangeSequence: 12,
                interactiveReady: false,
                includeImplementationSession: false),
            ForegroundProcessCount = 1,
        };
        var supervisor = CreateSupervisor(
            context,
            runner,
            write: true,
            repeatBackoffSeconds: 30);

        var first = supervisor.RunOnce();
        Assert.Single(first.Findings, finding => finding.Kind == "observation-conflict");

        now = firstNow.AddSeconds(10);
        var parked = supervisor.RunOnce();
        Assert.DoesNotContain(parked.Findings, finding => finding.Kind == "observation-conflict");
        Assert.Contains(parked.RecoveryRecords, record =>
            record.Kind == "observation-conflict"
            && record.Parked
            && record.RepeatCount == 2
            && record.EmissionCadenceSeconds == 30);

        now = firstNow.AddSeconds(30);
        var due = supervisor.RunOnce();
        Assert.Single(due.Findings, finding => finding.Kind == "observation-conflict");
        Assert.Contains(due.RecoveryRecords, record =>
            record.Kind == "observation-conflict"
            && record.RepeatCount == 3
            && record.Parked);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        FakeRunner runner,
        bool write,
        int repeatBackoffSeconds = 60) => new(
        context,
        root,
        Domain,
        Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: 300,
        declaredBoundSeconds: null,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write,
        format: "json",
        runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: "unused-agmsg",
        repeatBackoffSeconds: repeatBackoffSeconds);

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private static string AgentsWithImplementation(
        string status,
        long stateChangeSequence,
        bool interactiveReady,
        bool includeImplementationSession)
    {
        var implementationSession = includeImplementationSession
            ? new { id = "implementation" }
            : null;
        return JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new object[]
                {
                    new
                    {
                        name = "orchestration",
                        workspace_id = "wG707",
                        pane_id = "wG707:p1",
                        agent = "fixture",
                        agent_session = new { id = "orchestration" },
                        agent_status = "working",
                        interactive_ready = true,
                        state_change_seq = 1L,
                    },
                    new
                    {
                        name = "implementation",
                        workspace_id = "wG707",
                        pane_id = "wG707:p2",
                        agent = "fixture",
                        agent_session = implementationSession,
                        agent_status = status,
                        interactive_ready = interactiveReady,
                        state_change_seq = stateChangeSequence,
                    },
                },
            },
        });
    }

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
            workspace_id = "wG707",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG707", pane_id = "wG707:p1" },
                ["implementation"] = new { resident = "herdr", workspace_id = "wG707", pane_id = "wG707:p2" },
            },
        }));
    }

    private sealed class FakeRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public string AgentsJson { get; set; } = "{\"result\":{\"agents\":[]}}";
        public int ForegroundProcessCount { get; set; }

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, string.Empty);
            }

            if (arguments.Count == 4
                && arguments[0] == "pane"
                && arguments[1] == "process-info")
            {
                var processes = Enumerable.Range(0, ForegroundProcessCount)
                    .Select(index => new { pid = index + 1 })
                    .ToArray();
                return new NotifyProcessResult(
                    0,
                    JsonSerializer.Serialize(new { result = new { process_info = new { foreground_processes = processes } } }),
                    string.Empty);
            }

            if (fileName == "bash")
            {
                return new NotifyProcessResult(0, "orchestration (codex)\n", string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
