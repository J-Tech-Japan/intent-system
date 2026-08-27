using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyG731RecoveryTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly SplitWorkspace workspace = new();

    public NotifyG731RecoveryTests()
    {
        NotifyCommand.UtcNowFactory = () => new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
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
    public void HostRecoversExistingUndeliveredEntry_G731()
    {
        RequireUnixNonRoot();
        var runner = new FakeTransportRunner(workspace.HerdrAgents());
        NotifyCommand.ProcessRunnerFactory = () => runner;
        workspace.WriteTopology(externalOrchestration: true);
        
        var (delegateExit, delegateResult) = workspace.Run(workspace.DelegateArgs("G731-recovery", "g731-recovery-nonce"));
        Assert.Equal(0, delegateExit);
        var readerPath = workspace.ExternalReaderPath;
        var generatedCommand = delegateResult.GetProperty("report_command").GetString()!;
        var exactCommand = workspace.MaterializeReportCommand(
            generatedCommand,
            "https://example.test/pr/1585",
            "g731-recovery-test");

        // Step 1: Create an undelivered entry by making the host read-only
        workspace.MakeHostReadOnly();
        try
        {
            var reportProcess = workspace.RunExactCommand(exactCommand);
            Assert.True(reportProcess.ExitCode == 1, reportProcess.StandardOutput + reportProcess.StandardError);
            using var document = JsonDocument.Parse(reportProcess.StandardOutput);
            var result = document.RootElement;
            Assert.Equal("report-routing-root-write-required", result.GetProperty("cause").GetString());
            
            // Show the undelivered entry
            var outbox = NotifyReportOutboxStore.Find(
                workspace.SeatRoot,
                Domain,
                Team,
                "G731-recovery",
                "g731-recovery-nonce");
            Assert.True(outbox.Resolved);
            Assert.Equal("undelivered", outbox.Entry!.DeliveryState);
            Assert.Equal("report-routing-root-write-required", outbox.Entry.DeliveryError);
        }
        finally
        {
            workspace.MakeHostWritable();
        }

        // Step 2: Show the reader file before recovery
        var readerBefore = File.ReadAllText(readerPath);

        // Step 3: Run the host-side recovery
        var recoveryArgs = new[]
        {
            "notify", "collect", "--domain", Domain, "--team", Team, "--task-id", "G731-recovery",
            "--routing-root", workspace.HostRoot, "--report-root", workspace.SeatRoot, "--write", "--format", "json",
        };
        var (recoveryExit, recoveryResult) = workspace.RunHost(recoveryArgs);
        Assert.Equal(0, recoveryExit);

        // Step 4: Show the reader file after recovery
        var readerAfter = File.ReadAllText(readerPath);
        Assert.NotEqual(readerBefore, readerAfter);

        // Step 5: Verify the entry is now delivered
        var deliveredOutbox = NotifyReportOutboxStore.Find(
            workspace.SeatRoot,
            Domain,
            Team,
            "G731-recovery",
            "g731-recovery-nonce");
        Assert.True(deliveredOutbox.Resolved);
        Assert.Equal("delivered", deliveredOutbox.Entry!.DeliveryState);

        // Step 6: Run reconcile and show it names a command
        var (reconcileExit, reconcileResult) = workspace.RunHost(workspace.ReconcileArgs("G731-recovery"));
        Assert.Equal(0, reconcileExit);
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

        public string[] DelegateArgs(string taskId = "G731-recovery", string resultNonce = "g731-recovery-nonce") =>
        [
            "notify", "delegate", "--domain", Domain, "--team", Team,
            "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
            "--task-id", taskId, "--objective", "Test recovery",
            "--input", "issue #1585", "--expected-artifact", "draft PR", "--result-nonce", resultNonce,
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
                Path.Combine(SeatRoot, "bin", "intent-cli"),
                $"#!/bin/sh\nexec dotnet {QuoteForShell(cliAssembly)} \"$@\"\n");
            File.WriteAllText(
                Path.Combine(SeatRoot, "bin", "herdr"),
                "#!/bin/sh\nif [ \"$1\" = \"agent\" ] && [ \"$2\" = \"list\" ]; then\n"
                + $"  cat {QuoteForShell(Path.Combine(SeatRoot, "herdr-agents.json"))}\n"
                + "  exit 0\nfi\nexit 0\n");
            File.WriteAllText(Path.Combine(SeatRoot, "herdr-agents.json"), HerdrAgents());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    Path.Combine(SeatRoot, "bin", "intent-cli"),
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                File.SetUnixFileMode(
                    Path.Combine(SeatRoot, "bin", "herdr"),
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
            startInfo.Environment[NotifyTransportPaths.HerdrExecutableEnvironmentVariable] = Path.Combine(SeatRoot, "bin", "herdr");
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
