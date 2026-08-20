using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyG719Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly SplitWorkspace workspace = new();

    public NotifyG719Tests()
    {
        NotifyCommand.UtcNowFactory = () => new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        NotifyReportOutboxStore.WriteOverride = null;
        workspace.Dispose();
    }

    [Fact]
    public void CanonicalReportUsesSenderLocalOutboxWhenHostRoutingRootIsNotWritable_G719()
    {
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var (delegateExit, delegateResult) = workspace.Run(workspace.DelegateArgs());
        Assert.Equal(0, delegateExit);
        Assert.Contains("--routing-root", delegateResult.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("--report-root .", delegateResult.GetProperty("report_command").GetString(), StringComparison.Ordinal);
        runner.Calls.Clear();
        var hostBefore = workspace.HostSnapshot();

        NotifyReportOutboxStore.WriteOverride = (path, _) =>
            path.StartsWith(workspace.HostRoot, StringComparison.Ordinal)
                ? new NotifyReportOutboxWriteResult(false, path, "fixture denies host routing-root write")
                : new NotifyReportOutboxWriteResult(true, path, null);

        var (oldExit, oldResult) = workspace.Run(workspace.ReportArgs(reportRoot: workspace.HostRoot));
        Assert.Equal(1, oldExit);
        Assert.Equal("report-outbox-write-failed", oldResult.GetProperty("cause").GetString());
        Assert.Empty(runner.Calls);
        Assert.Equal(hostBefore, workspace.HostSnapshot());

        NotifyReportOutboxStore.WriteOverride = null;
        var (repairedExit, repaired) = workspace.Run(workspace.ReportArgs());

        Assert.Equal(0, repairedExit);
        Assert.True(repaired.GetProperty("delivered").GetBoolean());
        Assert.Equal(workspace.SeatRoot, repaired.GetProperty("report_root").GetString());
        Assert.Equal("sender-local-role-work-root", repaired.GetProperty("report_storage_mode").GetString());
        Assert.Equal("deferred-to-orchestration", repaired.GetProperty("host_state_sync").GetString());
        Assert.Contains("no host-root write was required", repaired.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.StartsWith(workspace.SeatRoot, repaired.GetProperty("outbox_entry_path").GetString(), StringComparison.Ordinal);
        Assert.Equal(hostBefore, workspace.HostSnapshot());
        var pending = NotifyPendingDelegationStore.Find(workspace.HostRoot, Domain, Team, "G719-report");
        Assert.True(pending.Resolved);
        Assert.True(pending.Record!.IsOpen);
        Assert.Contains(runner.Calls, call => call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG719:p1"]));
    }

    [Fact]
    public void RegistrationLossNamesMissingAgentSessionAndTheBoundedOperatorAct_G719()
    {
        var record = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G719-registration",
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            ReportToRole = "orchestration",
            RecipientIdentity = "wG719:wG719:p2",
            ExpectedArtifact = "draft PR",
            DispatchedAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG719",
            PaneId = "wG719:p2",
        };
        var runner = new RegistrationDiagnosticRunner();

        var result = NotifyPendingLiveness.Probe(
            workspace.SeatRoot,
            record,
            SessionLayerMode.HerdrOnly,
            runner,
            "fake-herdr",
            workspace.SeatRoot);

        Assert.True(result.Resolved);
        Assert.False(result.Running);
        Assert.Equal(NotifyPendingLivenessResult.RegistrationLostProcessPresent, result.State);
        Assert.True(result.ProcessPresent);
        Assert.False(result.AgentSessionPresent);
        Assert.Contains("agent_session", result.Summary, StringComparison.Ordinal);
        Assert.Contains("one no-op prompt", result.Summary, StringComparison.Ordinal);
        Assert.Contains("establish `agent_session`", result.Summary, StringComparison.Ordinal);
        Assert.Contains("Do not re-register, restart, or kill", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("agent", StringComparer.Ordinal)
            && call.Arguments.Contains("prompt", StringComparer.Ordinal));
    }

    private sealed class FakeTransportRunner(string agentResponse) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, agentResponse, string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG719:p1"])
                || arguments.SequenceEqual(["pane", "process-info", "--pane", "wG719:p2"]))
            {
                return new NotifyProcessResult(0, "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}", string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class RegistrationDiagnosticRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(
                    0,
                    JsonSerializer.Serialize(new
                    {
                        result = new
                        {
                            agents = new[]
                            {
                                new
                                {
                                    name = "implementation",
                                    workspace_id = "wG719",
                                    pane_id = "wG719:p2",
                                    agent = "codex",
                                    agent_session = (object?)null,
                                    agent_status = "idle",
                                    interactive_ready = false,
                                },
                            },
                        },
                    }),
                    string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG719:p2"]))
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[{\"pid\":7192,\"cwd\":\"/private/tmp/g719\",\"name\":\"codex\"}]}}}",
                    string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class SplitWorkspace : IDisposable
    {
        public SplitWorkspace()
        {
            HostRoot = Directory.CreateTempSubdirectory("notify-g719-host-").FullName;
            SeatRoot = Directory.CreateTempSubdirectory("notify-g719-seat-").FullName;
            HostContext = CreateContext(HostRoot);
            SeatContext = CreateContext(SeatRoot);
            WriteTopology();
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                HostContext,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        public string HostRoot { get; }
        public string SeatRoot { get; }
        private CliContext HostContext { get; }
        private CliContext SeatContext { get; }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            var context = args.Contains("--report-root", StringComparer.Ordinal)
                ? SeatContext
                : args[0] == "notify" && args[1] == "report" ? SeatContext : HostContext;
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public string[] DelegateArgs() =>
        [
            "notify", "delegate", "--domain", Domain, "--team", Team,
            "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
            "--task-id", "G719-report", "--objective", "Verify sender-local reporting",
            "--input", "issue #1560", "--expected-artifact", "draft PR", "--result-nonce", "g719-report-nonce",
            "--write", "--format", "json",
        ];

        public string[] ReportArgs(string? reportRoot = null)
        {
            var args = new List<string>
            {
                "notify", "report", "--domain", Domain, "--team", Team,
                "--from", "implementation", "--to", "orchestration", "--task-id", "G719-report",
                "--status", "completed", "--artifact", "https://example.test/pr/1560",
                "--summary", "sender-local report handoff verified", "--routing-root", HostRoot,
            };
            if (reportRoot is not null)
            {
                args.AddRange(["--report-root", reportRoot]);
            }
            args.AddRange(["--write", "--format", "json"]);
            return [.. args];
        }

        public string HerdrAgents() => JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    new
                    {
                        name = "orchestration",
                        workspace_id = "wG719",
                        pane_id = "wG719:p1",
                        agent = "codex",
                        agent_session = new { id = "orchestration" },
                        agent_status = "working",
                        interactive_ready = true,
                    },
                    new
                    {
                        name = "implementation",
                        workspace_id = "wG719",
                        pane_id = "wG719:p2",
                        agent = "codex",
                        agent_session = new { id = "implementation" },
                        agent_status = "working",
                        interactive_ready = true,
                    },
                },
            },
        });

        public string HostSnapshot()
        {
            return string.Join(
                "\n",
                Directory.GetFiles(HostRoot, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => Path.GetRelativePath(HostRoot, path) + "\0" + File.ReadAllText(path)));
        }

        private static CliContext CreateContext(string root) => new()
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

        private void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(HostRoot, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG719",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG719", pane_id = "wG719:p1" },
                    ["implementation"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG719", pane_id = "wG719:p2" },
                },
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(HostRoot)) Directory.Delete(HostRoot, recursive: true);
            if (Directory.Exists(SeatRoot)) Directory.Delete(SeatRoot, recursive: true);
        }
    }
}
