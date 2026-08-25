using System.Diagnostics;
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
    public void GeneratedReportRunsFromDeniedSeatAndHostReconciliationIsIdempotent_G719()
    {
        RequireUnixNonRoot();
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var (delegateExit, delegateResult) = workspace.Run(workspace.DelegateArgs());
        Assert.Equal(0, delegateExit);
        var generatedCommand = delegateResult.GetProperty("report_command").GetString()!;
        Assert.Contains(
            $"--routing-root '{workspace.HostRoot}' --report-root .",
            generatedCommand,
            StringComparison.Ordinal);

        workspace.PrepareDeniedHostProofPaths();
        var exactCommand = workspace.MaterializeReportCommand(
            generatedCommand,
            "https://example.test/pr/1560",
            "generated-sandbox-report-verified");
        Assert.Contains("--report-root .", exactCommand, StringComparison.Ordinal);

        var hostBefore = workspace.HostSnapshot();
        var originalHostRootMode = workspace.HostRootMode();
        workspace.MakeHostReadOnly();
        try
        {
            var deniedWrites = workspace.RunHostWriteProbe();
            Assert.Equal(0, deniedWrites.ExitCode);
            Assert.Contains("denied:root", deniedWrites.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("denied:queue", deniedWrites.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("denied:runs", deniedWrites.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("denied:packet", deniedWrites.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("all-host-write-probes-denied", deniedWrites.StandardOutput, StringComparison.Ordinal);

            var reportProcess = workspace.RunExactCommand(exactCommand);
            Assert.True(reportProcess.ExitCode == 0, reportProcess.StandardOutput + reportProcess.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(reportProcess.StandardError), reportProcess.StandardError);
            using var reportDocument = JsonDocument.Parse(reportProcess.StandardOutput);
            var reportResult = reportDocument.RootElement;
            Assert.True(reportResult.GetProperty("delivered").GetBoolean());
            Assert.Equal("sender-local-role-work-root", reportResult.GetProperty("report_storage_mode").GetString());
            Assert.Equal("deferred-to-orchestration", reportResult.GetProperty("host_state_sync").GetString());
            Assert.Contains("no host-root write was required", reportResult.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Contains(
                Path.Combine(".intent-cli", "notify", Domain, Team, "report-outbox.jsonl"),
                reportResult.GetProperty("outbox_entry_path").GetString(),
                StringComparison.Ordinal);
            Assert.Contains("notify reconcile", reportResult.GetProperty("reconciliation_command").GetString(), StringComparison.Ordinal);
            Assert.Equal(hostBefore, workspace.HostSnapshot());

            var outbox = NotifyReportOutboxStore.Find(
                workspace.SeatRoot,
                Domain,
                Team,
                "G719-report",
                "g719-report-nonce");
            Assert.True(outbox.Resolved);
            Assert.Equal("delivered", outbox.Entry!.DeliveryState);
            var pending = NotifyPendingDelegationStore.Find(workspace.HostRoot, Domain, Team, "G719-report");
            Assert.True(pending.Resolved);
            Assert.True(pending.Record!.IsOpen);
            Assert.False(File.Exists(ContinuationChainStore.ResolvePath(workspace.HostRoot, Domain, Team)));
        }
        finally
        {
            workspace.MakeHostWritable();
            Assert.Equal(originalHostRootMode, workspace.HostRootMode());
        }

        var reconcileArgs = workspace.ReconcileArgs("G719-report");
        var dryReconcileArgs = reconcileArgs
            .Select(argument => string.Equals(argument, "--write", StringComparison.Ordinal) ? "--dry-run" : argument)
            .ToArray();
        var dryPending = File.ReadAllText(NotifyPendingDelegationStore.ResolvePath(workspace.HostRoot, Domain, Team));
        var (dryExit, dryResult) = workspace.RunHost(dryReconcileArgs);
        Assert.Equal(0, dryExit);
        Assert.False(dryResult.GetProperty("reconciled").GetBoolean());
        Assert.True(dryResult.GetProperty("would_reconcile").GetBoolean());
        Assert.False(dryResult.GetProperty("already_converged").GetBoolean());
        Assert.Equal(dryPending, File.ReadAllText(NotifyPendingDelegationStore.ResolvePath(workspace.HostRoot, Domain, Team)));
        Assert.False(File.Exists(ContinuationChainStore.ResolvePath(workspace.HostRoot, Domain, Team)));

        var beforePending = File.ReadAllText(NotifyPendingDelegationStore.ResolvePath(workspace.HostRoot, Domain, Team));
        var (reconcileExit, reconcileResult) = workspace.RunHost(reconcileArgs);
        Assert.Equal(0, reconcileExit);
        Assert.True(reconcileResult.GetProperty("reconciled").GetBoolean());
        Assert.True(reconcileResult.GetProperty("pending_reconciled").GetBoolean());
        Assert.True(reconcileResult.GetProperty("continuation_reconciled").GetBoolean());
        Assert.False(reconcileResult.GetProperty("pending_already_converged").GetBoolean());
        Assert.False(reconcileResult.GetProperty("continuation_already_converged").GetBoolean());

        var pendingPath = NotifyPendingDelegationStore.ResolvePath(workspace.HostRoot, Domain, Team);
        var chainPath = ContinuationChainStore.ResolvePath(workspace.HostRoot, Domain, Team);
        var afterPending = File.ReadAllText(pendingPath);
        var afterChain = File.ReadAllText(chainPath);
        Assert.NotEqual(beforePending, afterPending);
        Assert.Equal(2, File.ReadAllLines(pendingPath).Length);
        Assert.Single(File.ReadAllLines(chainPath));
        var reconciledPending = NotifyPendingDelegationStore.Find(workspace.HostRoot, Domain, Team, "G719-report");
        Assert.True(reconciledPending.Resolved);
        Assert.True(reconciledPending.Record!.ReportArrived);

        var (replayExit, replayResult) = workspace.RunHost(reconcileArgs);
        Assert.Equal(0, replayExit);
        Assert.True(replayResult.GetProperty("already_converged").GetBoolean());
        Assert.True(replayResult.GetProperty("pending_already_converged").GetBoolean());
        Assert.True(replayResult.GetProperty("continuation_already_converged").GetBoolean());
        Assert.Equal(afterPending, File.ReadAllText(pendingPath));
        Assert.Equal(afterChain, File.ReadAllText(chainPath));
    }

    [Fact]
    public void ExternalReaderRetainsSenderLocalHandoffWhenHostRootIsDenied_G719()
    {
        RequireUnixNonRoot();
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        workspace.WriteTopology(externalOrchestration: true);
        var (delegateExit, delegateResult) = workspace.Run(workspace.DelegateArgs("G719-external", "g719-external-nonce"));
        Assert.Equal(0, delegateExit);
        var readerPath = workspace.ExternalReaderPath;
        var readerBefore = File.ReadAllText(readerPath);
        var generatedCommand = delegateResult.GetProperty("report_command").GetString()!;
        var exactCommand = workspace.MaterializeReportCommand(
            generatedCommand,
            "https://example.test/pr/1560-external",
            "external-reader-routing-retained");

        workspace.MakeHostReadOnly();
        try
        {
            var reportProcess = workspace.RunExactCommand(exactCommand);
            Assert.True(reportProcess.ExitCode == 1, reportProcess.StandardOutput + reportProcess.StandardError);
            Assert.True(string.IsNullOrWhiteSpace(reportProcess.StandardError), reportProcess.StandardError);
            using var document = JsonDocument.Parse(reportProcess.StandardOutput);
            var result = document.RootElement;
            Assert.Equal("report-routing-root-write-required", result.GetProperty("cause").GetString());
            Assert.Contains("sender-local report handoff is retained", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Equal(readerBefore, File.ReadAllText(readerPath));
            var outbox = NotifyReportOutboxStore.Find(
                workspace.SeatRoot,
                Domain,
                Team,
                "G719-external",
                "g719-external-nonce");
            Assert.True(outbox.Resolved);
            Assert.Equal("undelivered", outbox.Entry!.DeliveryState);
            Assert.Equal("report-routing-root-write-required", outbox.Entry.DeliveryError);
            var pending = NotifyPendingDelegationStore.Find(workspace.HostRoot, Domain, Team, "G719-external");
            Assert.True(pending.Resolved);
            Assert.True(pending.Record!.IsOpen);
        }
        finally
        {
            workspace.MakeHostWritable();
        }
    }

    [Fact]
    public void CollectRecoversGenuinelyUndeliveredSenderLocalReportByObservedHostWrite_G731()
    {
        RequireUnixNonRoot();
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        workspace.WriteTopology(
            externalOrchestration: true,
            externalReader: NotifyEventWriter.RelativePathFor(Domain, Team));
        var (delegateExit, delegateResult) = workspace.Run(
            workspace.DelegateArgs("G731-recovery", "g731-recovery-nonce"));
        Assert.Equal(0, delegateExit);
        var exactCommand = workspace.MaterializeReportCommand(
            delegateResult.GetProperty("report_command").GetString()!,
            "https://example.test/pr/1586",
            "recover-stuck-sender-local-report");

        workspace.MakeHostReadOnly();
        try
        {
            var reportProcess = workspace.RunExactCommand(exactCommand);
            Assert.Equal(1, reportProcess.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(reportProcess.StandardError), reportProcess.StandardError);
            using var reportDocument = JsonDocument.Parse(reportProcess.StandardOutput);
            var reportResult = reportDocument.RootElement;
            Assert.Equal("report-routing-root-write-required", reportResult.GetProperty("cause").GetString());
            Assert.Contains(
                "The attempted host-root write failed",
                reportResult.GetProperty("summary").GetString(),
                StringComparison.Ordinal);

            var (reconcileExit, reconcileResult) = workspace.RunHost(
                workspace.ReconcileArgs("G731-recovery"));
            Assert.Equal(1, reconcileExit);
            Assert.Equal("sender-local-report-not-delivered", reconcileResult.GetProperty("cause").GetString());
            var recoveryCommand = reconcileResult.GetProperty("recovery_command").GetString()!;
            Assert.Contains("intent-cli notify collect", recoveryCommand, StringComparison.Ordinal);
            Assert.Contains($"--routing-root {workspace.HostRoot}", recoveryCommand, StringComparison.Ordinal);
            Assert.Contains($"--report-root {workspace.SeatRoot}", recoveryCommand, StringComparison.Ordinal);
            Assert.Contains(recoveryCommand, reconcileResult.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Empty(File.ReadAllLines(workspace.ExternalReaderPath));
        }
        finally
        {
            workspace.MakeHostWritable();
        }

        var beforeLines = File.ReadAllLines(workspace.ExternalReaderPath).Length;
        var collectArgs = new[]
        {
            "notify", "collect", "--domain", Domain, "--team", Team, "--task-id", "G731-recovery",
            "--routing-root", workspace.HostRoot, "--report-root", workspace.SeatRoot,
            "--write", "--format", "json",
        };
        var (collectExit, collectResult) = workspace.RunHost(collectArgs);
        Assert.Equal(0, collectExit);
        Assert.True(collectResult.GetProperty("delivered").GetBoolean());
        Assert.True(collectResult.GetProperty("event_appended").GetBoolean());
        Assert.Contains("host routing event was appended", collectResult.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(beforeLines + 1, File.ReadAllLines(workspace.ExternalReaderPath).Length);

        using (var eventDocument = JsonDocument.Parse(File.ReadAllLines(workspace.ExternalReaderPath).Single()))
        {
            Assert.Equal("completion", eventDocument.RootElement.GetProperty("kind").GetString());
            Assert.Equal("G731-recovery", eventDocument.RootElement.GetProperty("unit").GetString());
        }

        var outbox = NotifyReportOutboxStore.Find(
            workspace.SeatRoot,
            Domain,
            Team,
            "G731-recovery",
            "g731-recovery-nonce");
        Assert.True(outbox.Resolved);
        Assert.Equal("delivered", outbox.Entry!.DeliveryState);

        var (finalReconcileExit, finalReconcile) = workspace.RunHost(
            workspace.ReconcileArgs("G731-recovery"));
        Assert.Equal(0, finalReconcileExit);
        Assert.True(finalReconcile.GetProperty("reconciled").GetBoolean());
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
            PrepareSeatExecutables();
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                HostContext,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        public string HostRoot { get; }
        public string SeatRoot { get; }
        private string? externalReaderPath;
        public string ExternalReaderPath => externalReaderPath ?? Path.Combine(
            HostRoot,
            ".intent-cli",
            "notify",
            Domain,
            Team,
            "external-reader.jsonl");
        private string QueueStatePath => Path.Combine(HostRoot, ".intent-cli", "queue-state.json");
        private string RunsPath => Path.Combine(HostRoot, ".takt", "runs", "G719.jsonl");
        private string PacketPath => Path.Combine(HostRoot, "intents", Domain, "packets", "G719.yaml");
        private string IntentCliShimPath => Path.Combine(SeatRoot, "bin", "intent-cli");
        private string HerdrShimPath => Path.Combine(SeatRoot, "bin", "herdr");
        private UnixFileMode? hostRootModeBeforeReadOnly;
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

        public (int ExitCode, JsonElement Result) RunHost(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, HostContext, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public string[] DelegateArgs(string taskId = "G719-report", string resultNonce = "g719-report-nonce") =>
        [
            "notify", "delegate", "--domain", Domain, "--team", Team,
            "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
            "--task-id", taskId, "--objective", "Verify sender-local reporting",
            "--input", "issue #1560", "--expected-artifact", "draft PR", "--result-nonce", resultNonce,
            "--write", "--format", "json",
        ];

        public string[] ReconcileArgs(string taskId) =>
        [
            "notify", "reconcile", "--domain", Domain, "--team", Team, "--task-id", taskId,
            "--routing-root", HostRoot, "--report-root", SeatRoot, "--write", "--format", "json",
        ];

        public string MaterializeReportCommand(string generatedCommand, string artifact, string summary) =>
            generatedCommand
                .Replace("--status <completed|blocked|question>", "--status completed", StringComparison.Ordinal)
                .Replace("--artifact <artifact>", $"--artifact {artifact}", StringComparison.Ordinal)
                .Replace("--summary <one-line-summary>", $"--summary {summary}", StringComparison.Ordinal);

        public ShellResult RunExactCommand(string command) => RunSeatShell(command);

        public ShellResult RunHostWriteProbe()
        {
            var probes = new[]
            {
                (Name: "root", Path: Path.Combine(HostRoot, "g719-root-level-probe")),
                (Name: "queue", Path: QueueStatePath),
                (Name: "runs", Path: RunsPath),
                (Name: "packet", Path: PacketPath),
            };
            var command = string.Join(
                Environment.NewLine,
                probes.Select(probe =>
                    $"if printf x > {QuoteForShell(probe.Path)}; then printf 'writable:{probe.Name}\\n'; rm -f {QuoteForShell(probe.Path)}; exit 1; else printf 'denied:{probe.Name}\\n'; fi;"))
                + " printf 'all-host-write-probes-denied\\n';";
            return RunSeatShell(command);
        }

        public void PrepareDeniedHostProofPaths()
        {
            foreach (var path in new[] { QueueStatePath, RunsPath, PacketPath })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, $"g719-proof:{Path.GetFileName(path)}\n");
            }
        }

        public void MakeHostReadOnly()
        {
            if (OperatingSystem.IsWindows()) return;
            hostRootModeBeforeReadOnly ??= File.GetUnixFileMode(HostRoot);
            foreach (var file in Directory.EnumerateFiles(HostRoot, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            foreach (var directory in Directory.EnumerateDirectories(HostRoot, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length)
                .ThenBy(path => path, StringComparer.Ordinal))
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            File.SetUnixFileMode(
                HostRoot,
                File.GetUnixFileMode(HostRoot)
                & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        }

        public void MakeHostWritable()
        {
            if (OperatingSystem.IsWindows()) return;
            foreach (var directory in Directory.EnumerateDirectories(HostRoot, "*", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .ThenBy(path => path, StringComparer.Ordinal))
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }

            foreach (var file in Directory.EnumerateFiles(HostRoot, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(
                    file,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
            }

            if (hostRootModeBeforeReadOnly is { } originalMode)
            {
                File.SetUnixFileMode(HostRoot, originalMode);
                hostRootModeBeforeReadOnly = null;
            }
        }

        public UnixFileMode HostRootMode()
        {
            if (OperatingSystem.IsWindows()) return default;
            return File.GetUnixFileMode(HostRoot);
        }

        public void WriteTopology(bool externalOrchestration = false, string? externalReader = null)
        {
            var path = NotifyRoleTopologyStore.ResolvePath(HostRoot, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var reader = externalReader
                ?? $".intent-cli/notify/{Domain}/{Team}/external-reader.jsonl";
            externalReaderPath = Path.IsPathRooted(reader)
                ? Path.GetFullPath(reader)
                : Path.GetFullPath(Path.Combine(HostRoot, reader));
            object orchestration = externalOrchestration
                ? new { resident = NotifyRecordedRole.ExternalResident, reader }
                : new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG719", pane_id = "wG719:p1" };
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG719",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = orchestration,
                    ["implementation"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG719", pane_id = "wG719:p2" },
                },
            }));

            if (externalOrchestration)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ExternalReaderPath)!);
                if (!File.Exists(ExternalReaderPath)) File.WriteAllText(ExternalReaderPath, string.Empty);
            }
        }

        private void PrepareSeatExecutables()
        {
            Directory.CreateDirectory(Path.Combine(SeatRoot, "bin"));
            var cliAssembly = typeof(NotifyCommand).Assembly.Location;
            File.WriteAllText(
                IntentCliShimPath,
                $"#!/bin/sh\nexec dotnet {QuoteForShell(cliAssembly)} \"$@\"\n");
            File.WriteAllText(
                HerdrShimPath,
                "#!/bin/sh\nif [ \"$1\" = \"agent\" ] && [ \"$2\" = \"list\" ]; then\n"
                + $"  cat {QuoteForShell(Path.Combine(SeatRoot, "herdr-agents.json"))}\n"
                + "  exit 0\nfi\nexit 0\n");
            File.WriteAllText(Path.Combine(SeatRoot, "herdr-agents.json"), HerdrAgents());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    IntentCliShimPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.SetUnixFileMode(
                    HerdrShimPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }

        private ShellResult RunSeatShell(string command)
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = SeatRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(command);
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            startInfo.Environment["PATH"] = Path.Combine(SeatRoot, "bin") + Path.PathSeparator + currentPath;
            startInfo.Environment[NotifyTransportPaths.HerdrExecutableEnvironmentVariable] = HerdrShimPath;
            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ShellResult(process.ExitCode, standardOutput, standardError);
        }

        private static string QuoteForShell(string value) =>
            $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

        public string HostSnapshot()
        {
            return string.Join(
                "\n",
                Directory.GetFiles(HostRoot, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => Path.GetRelativePath(HostRoot, path) + "\0" + File.ReadAllText(path)));
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
        public void Dispose()
        {
            if (Directory.Exists(HostRoot)) Directory.Delete(HostRoot, recursive: true);
            if (Directory.Exists(SeatRoot)) Directory.Delete(SeatRoot, recursive: true);
        }
    }

    private static void RequireUnixNonRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip("G719 OS-denied fixture requires Unix file permissions.");
        }

        var info = new ProcessStartInfo("id")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("-u");
        using var process = Process.Start(info)!;
        var userId = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.Equals(userId, "0", StringComparison.Ordinal))
        {
            throw Xunit.Sdk.SkipException.ForSkip("G719 OS-denied fixture cannot prove denial while running as root.");
        }
    }

    private sealed record ShellResult(int ExitCode, string StandardOutput, string StandardError);
}
