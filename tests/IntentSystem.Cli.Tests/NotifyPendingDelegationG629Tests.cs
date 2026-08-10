using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyPendingDelegationG629Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly Workspace workspace = new();

    public NotifyPendingDelegationG629Tests()
    {
        NotifyCommand.UtcNowFactory = () => FixedNow;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        NotifyPendingDelegationStore.WriteOverride = null;
        workspace.Dispose();
    }

    [Fact]
    public void DelegateWritesDurableRecordAndStatusReportsLiveFromRunningFlag_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (delegateExit, delegated) = workspace.Run(DelegateArgs());
        Assert.Equal(0, delegateExit);
        var pendingPath = delegated.GetProperty("pending_record_path").GetString();
        Assert.NotNull(pendingPath);
        Assert.True(File.Exists(pendingPath));
        Assert.Contains("G629-demo", File.ReadAllText(pendingPath!), StringComparison.Ordinal);

        var (statusExit, status) = workspace.Run(StatusArgs());
        Assert.Equal(0, statusExit);
        Assert.Equal("live", status.GetProperty("verdict").GetString());
        Assert.True(status.GetProperty("recipient_running").GetBoolean());
        Assert.Equal("herdr.agent_running", status.GetProperty("liveness_source").GetString());
        Assert.Equal(FixedNow, status.GetProperty("dispatched_at").GetDateTimeOffset());
    }

    [Fact]
    public void StatusNamesHerdrActivityEvidenceAndDistinguishesWorkingFromLiveIdle_G652()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(
            implementationRunning: true,
            implementationStatus: "working",
            stateChangeSequence: 7,
            lastStateChangeAt: FixedNow.AddMinutes(1)));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        var supervisionRoot = workspace.Context.ResolveSupervisionArtifactRootPath();
        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(supervisionRoot, Workspace.Domain, Workspace.Team),
            new NotifySupervisionCycle
            {
                CycleId = "G652-baseline",
                StartedAt = FixedNow,
                CompletedAt = FixedNow,
                IntervalSeconds = 300,
                LastObservedStateChangeSequences = new Dictionary<string, long>
                {
                    ["activity:wH:wH:p2"] = 6,
                },
                LastObservedStateChangeTimes = new Dictionary<string, DateTimeOffset>
                {
                    ["activity:wH:wH:p2"] = FixedNow,
                },
            },
            write: true).Applied);

        var (_, working) = workspace.Run(StatusArgs());
        Assert.Equal("working", working.GetProperty("activity_verdict").GetString());
        Assert.Equal("working", working.GetProperty("agent_status").GetString());
        Assert.Equal(7, working.GetProperty("state_change_seq").GetInt64());

        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(supervisionRoot, Workspace.Domain, Workspace.Team),
            new NotifySupervisionCycle
            {
                CycleId = "G652-current-observation",
                StartedAt = FixedNow.AddMinutes(1),
                CompletedAt = FixedNow.AddMinutes(1),
                IntervalSeconds = 300,
                LastObservedStateChangeSequences = new Dictionary<string, long>
                {
                    ["activity:wH:wH:p2"] = 7,
                },
            },
            write: true).Applied);
        runner.AgentResponse = workspace.HerdrAgents(implementationRunning: true, implementationStatus: "working", stateChangeSequence: 7);
        var (_, idle) = workspace.Run(StatusArgs());
        Assert.Equal("live-idle", idle.GetProperty("activity_verdict").GetString());
        Assert.Contains("advancing_since_last_observation=false", idle.GetProperty("activity_inputs").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StatusUsesRunningFlagNotIdleStatusStringAndReportsLost_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: false,
            implementationStatus: "idle",
            includeImplementationSession: false);
        var (statusExit, status) = workspace.Run(StatusArgs());

        Assert.Equal(0, statusExit);
        Assert.Equal("lost", status.GetProperty("verdict").GetString());
        Assert.False(status.GetProperty("recipient_running").GetBoolean());
        Assert.Contains("status strings are ignored", status.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingReportResolvesRecordAndStatusStaysSettledAfterRecipientStops_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        Assert.Equal(0, workspace.Run(DelegateArgs()).ExitCode);

        runner.AgentResponse = workspace.HerdrAgents(
            implementationRunning: false,
            implementationStatus: "idle",
            includeImplementationSession: false);
        var (reportExit, report) = workspace.Run(ReportArgs());
        Assert.Equal(0, reportExit);
        Assert.True(report.GetProperty("delivered").GetBoolean());

        var (statusExit, status) = workspace.Run(StatusArgs());
        Assert.Equal(0, statusExit);
        Assert.Equal("settled", status.GetProperty("verdict").GetString());
        Assert.True(status.GetProperty("report_arrived").GetBoolean());
    }

    [Fact]
    public void UnmatchedReportDeliversWithAdvisoryAndLeavesPendingRecordUnchanged_G640()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var (_, delegated) = workspace.Run(DelegateArgs());
        var pendingPath = delegated.GetProperty("pending_record_path").GetString()!;
        var pendingBefore = File.ReadAllText(pendingPath);
        runner.Calls.Clear();

        var (exitCode, result) = workspace.Run(ReportArgs(taskId: "G629-unknown"));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        var advisory = result.GetProperty("advisory").GetString()!;
        Assert.Contains("G629-unknown", advisory, StringComparison.Ordinal);
        Assert.Contains("No open pending delegation matched", advisory, StringComparison.Ordinal);
        Assert.Contains("G629-demo", advisory, StringComparison.Ordinal);
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("G629-unknown", StringComparison.Ordinal));
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("Delivered notification", summary, StringComparison.Ordinal);
        Assert.Contains(runner.Calls, call => call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p1"]));
        Assert.Equal(pendingBefore, File.ReadAllText(pendingPath));

        var (humanExit, humanOutput) = workspace.RunText(ReportArgs(taskId: "G629-human", format: "markdown"));
        Assert.Equal(0, humanExit);
        Assert.Contains("- advisory:", humanOutput, StringComparison.Ordinal);
        Assert.Contains("G629-human", humanOutput, StringComparison.Ordinal);
        Assert.Contains("No open pending delegation matched", humanOutput, StringComparison.Ordinal);
        Assert.Equal(pendingBefore, File.ReadAllText(pendingPath));
    }

    [Fact]
    public void CorruptPendingStoreRefusesReportWithoutDelivery_G640()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(
            workspace.RootPath,
            Workspace.Domain,
            Workspace.Team);
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        File.WriteAllText(pendingPath, "{ not valid pending json");

        var (exitCode, result) = workspace.Run(ReportArgs(taskId: "G640-corrupt"));

        Assert.Equal(1, exitCode);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("unknown-task-id", result.GetProperty("cause").GetString());
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("G640-corrupt", summary, StringComparison.Ordinal);
        Assert.Contains("could not be read", summary, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void PendingRecordWriteFailureHappensBeforeAnyPaneDelivery_G629()
    {
        var runner = new FakeRunner(() => workspace.HerdrAgents(implementationRunning: true));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyPendingDelegationStore.WriteOverride = (path, _) =>
            new NotifyPendingStoreWriteResult(false, path, "fixture denies pending write");

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal("pending-record-write-failed", result.GetProperty("cause").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Empty(runner.Calls);
    }

    private static string[] DelegateArgs() =>
    [
        "notify", "delegate", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
        "--task-id", "G629-demo", "--objective", "Inspect pending delegation state",
        "--input", "issue #1373", "--expected-artifact", "draft PR URL", "--result-nonce", "g629-nonce",
        "--write", "--format", "json",
    ];

    private static string[] ReportArgs(string taskId = "G629-demo", string format = "json") =>
    [
        "notify", "report", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", "implementation", "--to", "orchestration", "--task-id", taskId,
        "--status", "completed", "--artifact", "https://example.test/pr/1373",
        "--summary", "pending state implemented", "--write", "--format", format,
    ];

    private static string[] StatusArgs() =>
    [
        "notify", "status", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--task-id", "G629-demo", "--format", "json",
    ];

    private sealed class FakeRunner(Func<string> agentResponse) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public string AgentResponse { get; set; } = agentResponse();

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentResponse, string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wH:p2"]))
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g629-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
            WriteTopology();
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public (int ExitCode, string Output) RunText(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public string HerdrAgents(
            bool implementationRunning,
            string implementationStatus = "idle",
            bool includeImplementationSession = true,
            long? stateChangeSequence = null,
            DateTimeOffset? lastStateChangeAt = null)
        {
            object HerdrAgent(string name, string paneId, bool running, string status) => new
            {
                name,
                workspace_id = "wH",
                pane_id = paneId,
                agent = running || includeImplementationSession && name == "implementation" ? "codex" : null,
                agent_session = running || includeImplementationSession && name == "implementation"
                    ? new { id = name }
                    : null,
                agent_status = status,
                interactive_ready = running,
                state_change_seq = name == "implementation" ? stateChangeSequence : null,
                last_state_change_at = name == "implementation" ? lastStateChangeAt?.ToString("O") : null,
            };

            return JsonSerializer.Serialize(new
            {
                result = new
                {
                    agents = new[]
                    {
                        HerdrAgent("orchestration", "wH:p1", true, "idle"),
                        HerdrAgent("implementation", "wH:p2", implementationRunning, implementationStatus),
                    },
                },
            });
        }

        private void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wH",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new { resident = "herdr", workspace_id = "wH", pane_id = "wH:p1" },
                    ["implementation"] = new { resident = "herdr", workspace_id = "wH", pane_id = "wH:p2" },
                },
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
