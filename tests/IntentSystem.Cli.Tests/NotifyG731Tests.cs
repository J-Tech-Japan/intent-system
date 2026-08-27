using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyG731Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly SplitWorkspace workspace = new();
    private readonly ITestOutputHelper output;

    public NotifyG731Tests(ITestOutputHelper output)
    {
        this.output = output;
        NotifyCommand.UtcNowFactory = () => new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
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
    public void DeniedSeatRefusalNamesHostCollectAndHostCollectCompletesTheHandoff_G731()
    {
        RequireUnixNonRoot();
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        workspace.WriteTopology(externalOrchestration: true);
        var (delegateExit, delegateResult) = workspace.Run(workspace.DelegateArgs("G731-external", "g731-external-nonce"));
        Assert.Equal(0, delegateExit);
        var readerPath = workspace.ExternalReaderPath;
        var readerBefore = File.ReadAllText(readerPath);
        var generatedCommand = delegateResult.GetProperty("report_command").GetString()!;
        var exactCommand = workspace.MaterializeReportCommand(
            generatedCommand,
            "https://example.test/pr/1731",
            "g731-denied-seat-refusal");

        workspace.MakeHostReadOnly();
        ShellResult reportProcess;
        try
        {
            reportProcess = workspace.RunExactCommand(exactCommand);
        }
        finally
        {
            workspace.MakeHostWritable();
        }

        Assert.True(reportProcess.ExitCode == 1, reportProcess.StandardOutput + reportProcess.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(reportProcess.StandardError), reportProcess.StandardError);
        using var refusalDocument = JsonDocument.Parse(reportProcess.StandardOutput);
        var refusal = refusalDocument.RootElement;
        Assert.Equal("report-routing-root-write-required", refusal.GetProperty("cause").GetString());
        var refusalSummary = refusal.GetProperty("summary").GetString()!;
        output.WriteLine($"[g731] refusal summary: {refusalSummary}");
        Assert.Contains("sender-local report handoff is retained", refusalSummary, StringComparison.Ordinal);
        Assert.Contains(
            $"intent-cli notify collect --domain {Domain} --team {Team} --task-id G731-external --write --routing-root {workspace.HostRoot}",
            refusalSummary,
            StringComparison.Ordinal);
        Assert.Contains("--report-root", refusalSummary, StringComparison.Ordinal);
        Assert.Equal(readerBefore, File.ReadAllText(readerPath));
        var undelivered = NotifyReportOutboxStore.Find(
            workspace.SeatRoot,
            Domain,
            Team,
            "G731-external",
            "g731-external-nonce");
        Assert.True(undelivered.Resolved);
        Assert.Equal("undelivered", undelivered.Entry!.DeliveryState);
        Assert.Equal("report-routing-root-write-required", undelivered.Entry.DeliveryError);

        var (reconcileRefusalExit, reconcileRefusal) = workspace.RunHost(workspace.ReconcileArgs("G731-external"));
        Assert.Equal(1, reconcileRefusalExit);
        Assert.Equal("sender-local-report-not-delivered", reconcileRefusal.GetProperty("cause").GetString());
        var reconcileSummary = reconcileRefusal.GetProperty("summary").GetString()!;
        output.WriteLine($"[g731] reconcile summary: {reconcileSummary}");
        Assert.Contains("intent-cli notify collect", reconcileSummary, StringComparison.Ordinal);
        Assert.Contains($"--routing-root {workspace.HostRoot}", reconcileSummary, StringComparison.Ordinal);
        Assert.Contains("--report-root", reconcileSummary, StringComparison.Ordinal);

        string[] recoveryArgs =
        [
            "notify", "collect", "--domain", Domain, "--team", Team, "--task-id", "G731-external",
            "--routing-root", workspace.HostRoot, "--report-root", workspace.SeatRoot, "--write", "--format", "json",
        ];
        var (recoveryExit, recovery) = workspace.RunHost(recoveryArgs);
        output.WriteLine($"[g731] recovery summary: {recovery.GetProperty("summary").GetString()}");
        Assert.Equal(0, recoveryExit);
        Assert.True(recovery.GetProperty("delivered").GetBoolean());
        var recovered = NotifyReportOutboxStore.Find(
            workspace.SeatRoot,
            Domain,
            Team,
            "G731-external",
            "g731-external-nonce");
        Assert.True(recovered.Resolved);
        Assert.Equal("delivered", recovered.Entry!.DeliveryState);
        Assert.Null(recovered.Entry.DeliveryError);

        var readerAfter = File.ReadAllText(readerPath);
        output.WriteLine($"[g731] reader before lines: {readerBefore.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}; after lines: {readerAfter.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}");
        Assert.NotEqual(readerBefore, readerAfter);
        Assert.Contains("g731-denied-seat-refusal", readerAfter, StringComparison.Ordinal);

        var (reconcileExit, reconcile) = workspace.RunHost(workspace.ReconcileArgs("G731-external"));
        Assert.Equal(0, reconcileExit);
        Assert.True(reconcile.GetProperty("reconciled").GetBoolean());
        Assert.True(reconcile.GetProperty("pending_reconciled").GetBoolean());
        Assert.True(reconcile.GetProperty("continuation_reconciled").GetBoolean());

        var (replayExit, replay) = workspace.RunHost(workspace.ReconcileArgs("G731-external"));
        Assert.Equal(0, replayExit);
        Assert.True(replay.GetProperty("already_converged").GetBoolean());
    }

    [Fact]
    public void DryRunSenderLocalReportAgainstExternalReaderStatesNoWriteAttempted_G731()
    {
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        workspace.WriteTopology(externalOrchestration: true);
        var (delegateExit, _) = workspace.Run(workspace.DelegateArgs("G731-dry-run", "g731-dry-run-nonce"));
        Assert.Equal(0, delegateExit);
        var readerPath = workspace.ExternalReaderPath;
        var readerBefore = File.ReadAllText(readerPath);

        string[] dryRunArgs =
        [
            "notify", "report", "--domain", Domain, "--team", Team,
            "--from", "implementation", "--to", "orchestration", "--task-id", "G731-dry-run",
            "--status", "completed", "--artifact", "https://example.test/pr/1731",
            "--summary", "g731-dry-run-report",
            "--routing-root", workspace.HostRoot, "--report-root", workspace.SeatRoot,
            "--dry-run", "--format", "json",
        ];
        var (dryRunExit, dryRun) = workspace.Run(dryRunArgs);
        Assert.True(dryRunExit == 0, dryRun.ToString());
        var summary = dryRun.GetProperty("summary").GetString()!;
        output.WriteLine($"[g731] dry-run summary: {summary}");
        Assert.Contains("dry-run", summary, StringComparison.Ordinal);
        Assert.Contains("were both not attempted", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("no host-root write was required", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("completed exactly once", summary, StringComparison.Ordinal);
        Assert.Equal(readerBefore, File.ReadAllText(readerPath));
        Assert.False(File.Exists(NotifyReportOutboxStore.ResolvePath(workspace.SeatRoot, Domain, Team)));
    }

    private sealed class FakeTransportRunner(string agentResponse) : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, agentResponse, string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG731:p1"])
                || arguments.SequenceEqual(["pane", "process-info", "--pane", "wG731:p2"]))
            {
                return new NotifyProcessResult(0, "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}", string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class SplitWorkspace : IDisposable
    {
        public SplitWorkspace()
        {
            HostRoot = Directory.CreateTempSubdirectory("notify-g731-host-").FullName;
            SeatRoot = Directory.CreateTempSubdirectory("notify-g731-seat-").FullName;
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
        public string ExternalReaderPath => Path.Combine(
            HostRoot,
            ".intent-cli",
            "notify",
            Domain,
            Team,
            "external-reader.jsonl");
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

        public string[] DelegateArgs(string taskId, string resultNonce) =>
        [
            "notify", "delegate", "--domain", Domain, "--team", Team,
            "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
            "--task-id", taskId, "--objective", "Verify sender-local recovery",
            "--input", "issue #1731", "--expected-artifact", "draft PR", "--result-nonce", resultNonce,
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

        public void WriteTopology(bool externalOrchestration = false)
        {
            var path = NotifyRoleTopologyStore.ResolvePath(HostRoot, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            object orchestration = externalOrchestration
                ? new { resident = NotifyRecordedRole.ExternalResident, reader = ".intent-cli/notify/intent-cli/intent-cli-dev/external-reader.jsonl" }
                : new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG731", pane_id = "wG731:p1" };
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG731",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = orchestration,
                    ["implementation"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG731", pane_id = "wG731:p2" },
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

        public string HerdrAgents() => JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    new
                    {
                        name = "orchestration",
                        workspace_id = "wG731",
                        pane_id = "wG731:p1",
                        agent = "codex",
                        agent_session = new { id = "orchestration" },
                        agent_status = "working",
                        interactive_ready = true,
                    },
                    new
                    {
                        name = "implementation",
                        workspace_id = "wG731",
                        pane_id = "wG731:p2",
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
            throw Xunit.Sdk.SkipException.ForSkip("G731 OS-denied fixture requires Unix file permissions.");
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
            throw Xunit.Sdk.SkipException.ForSkip("G731 OS-denied fixture cannot prove denial while running as root.");
        }
    }

    private sealed record ShellResult(int ExitCode, string StandardOutput, string StandardError);
}
