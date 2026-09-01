using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyG731Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly ExternalOutboxWorkspace workspace = new();

    public NotifyG731Tests()
    {
        NotifyCommand.UtcNowFactory = () => new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        workspace.Dispose();
    }

    [Fact]
    public void ChildProducesUndeliveredEntryAndHostCollectAppendsTheLine_G731()
    {
        RequireUnixNonRoot();
        NotifyCommand.ProcessRunnerFactory = () => new FakeTransportRunner(workspace.HerdrAgents());

        var (delegateExit, delegateResult) = workspace.Run(workspace.DelegateArgs("G731-stuck", "g731-stuck-nonce"));
        Assert.Equal(0, delegateExit);
        var exactCommand = workspace.MaterializeReportCommand(
            delegateResult.GetProperty("report_command").GetString()!,
            "https://example.test/pr/1585",
            "g731-stuck-entry");

        var eventsBeforeRefusal = File.ReadAllText(workspace.ReaderPath);
        workspace.MakeHostReadOnly();
        JsonElement childResult;
        try
        {
            var reportProcess = workspace.RunSeatExactCommand(exactCommand);
            var jsonStart = reportProcess.StandardOutput.IndexOf('{');
            Assert.True(jsonStart >= 0, reportProcess.StandardOutput + reportProcess.StandardError);
            Assert.True(reportProcess.ExitCode == 1, reportProcess.StandardOutput + reportProcess.StandardError);
            childResult = JsonDocument.Parse(reportProcess.StandardOutput[jsonStart..]).RootElement.Clone();
            Assert.Equal("report-routing-root-write-required", childResult.GetProperty("cause").GetString());
            Assert.Contains("attempted in this execution context and was denied", childResult.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Contains("intent-cli notify collect", childResult.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Equal(eventsBeforeRefusal, File.ReadAllText(workspace.ReaderPath));

            var stuck = NotifyReportOutboxStore.Find(workspace.SeatRoot, Domain, Team, "G731-stuck", "g731-stuck-nonce");
            Assert.True(stuck.Resolved);
            Assert.Equal("undelivered", stuck.Entry!.DeliveryState);
            Assert.Equal("report-routing-root-write-required", stuck.Entry.DeliveryError);
        }
        finally
        {
            workspace.MakeHostWritable();
        }

        var collectArgs = new[]
        {
            "notify", "collect", "--domain", Domain, "--team", Team, "--task-id", "G731-stuck",
            "--routing-root", workspace.HostRoot, "--report-root", workspace.SeatRoot, "--write", "--format", "json",
        };
        Assert.Equal(eventsBeforeRefusal, File.ReadAllText(workspace.ReaderPath));
        var (collectExit, collectResult) = workspace.RunHost(collectArgs);
        Assert.True(collectExit == 0, collectResult.ToString());
        Assert.True(collectResult.GetProperty("delivered").GetBoolean());
        var eventsAfterRecovery = File.ReadAllText(workspace.ReaderPath);
        Assert.Contains("G731-stuck", eventsAfterRecovery, StringComparison.Ordinal);
        Assert.Contains("g731-stuck-entry", eventsAfterRecovery, StringComparison.Ordinal);

        var recovered = NotifyReportOutboxStore.Find(workspace.SeatRoot, Domain, Team, "G731-stuck", "g731-stuck-nonce");
        Assert.Equal("delivered", recovered.Entry!.DeliveryState);

        var (secondExit, secondResult) = workspace.RunHost(collectArgs);
        Assert.Equal(1, secondExit);
        Assert.Equal("already-collected", secondResult.GetProperty("cause").GetString());
        Assert.Equal(eventsAfterRecovery, File.ReadAllText(workspace.ReaderPath));
    }

    [Fact]
    public void ReconcileDeclinesUndeliveredEntryAndNamesTheCollectCommand_G731()
    {
        NotifyCommand.ProcessRunnerFactory = () => new FakeTransportRunner(workspace.HerdrAgents());
        var (delegateExit, _) = workspace.Run(workspace.DelegateArgs("G731-reconcile", "g731-reconcile-nonce"));
        Assert.Equal(0, delegateExit);
        workspace.SeedUndeliveredEntry("G731-reconcile", "g731-reconcile-nonce");

        var (exit, result) = workspace.RunHost(new[]
        {
            "notify", "reconcile", "--domain", Domain, "--team", Team, "--task-id", "G731-reconcile",
            "--routing-root", workspace.HostRoot, "--report-root", workspace.SeatRoot, "--write", "--format", "json",
        });
        Assert.Equal(1, exit);
        Assert.Equal("sender-local-report-not-delivered", result.GetProperty("cause").GetString());
        var expectedCommand = $"intent-cli notify collect --domain {Domain} --team {Team} --task-id G731-reconcile --write --routing-root {workspace.HostRoot}";
        Assert.Contains(expectedCommand, result.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    private sealed class FakeTransportRunner(string agentResponse) : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, agentResponse, string.Empty);
            }

            if (arguments.Count >= 2 && arguments[0] == "pane" && arguments[1] == "process-info")
            {
                return new NotifyProcessResult(0, "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}", string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

    private sealed class ExternalOutboxWorkspace : IDisposable
    {
        private readonly CliContext hostContext;
        private readonly CliContext seatContext;
        private UnixFileMode hostRootModeBeforeReadOnly;
        private bool hostReadOnly;

        public ExternalOutboxWorkspace()
        {
            HostRoot = Directory.CreateTempSubdirectory("notify-g731-host-").FullName;
            SeatRoot = Directory.CreateTempSubdirectory("notify-g731-seat-").FullName;
            hostContext = CreateContext(HostRoot);
            seatContext = CreateContext(SeatRoot);
            WriteTopology();
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                hostContext,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        public string HostRoot { get; }
        public string SeatRoot { get; }
        public string ReaderPath => Path.Combine(HostRoot, ".intent-cli", "events", "intent-cli-dev");

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            var context = args.Contains("--report-root", StringComparer.Ordinal) ? seatContext : hostContext;
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public (int ExitCode, JsonElement Result) RunHost(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, hostContext, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public string[] DelegateArgs(string taskId, string resultNonce) =>
        [
            "notify", "delegate", "--domain", Domain, "--team", Team,
            "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
            "--task-id", taskId, "--objective", "Verify host-side recovery",
            "--input", "issue #1585", "--expected-artifact", "draft PR", "--result-nonce", resultNonce,
            "--write", "--format", "json",
        ];

        public string MaterializeReportCommand(string generatedCommand, string artifact, string summary) =>
            generatedCommand
                .Replace("--status <completed|blocked|question>", "--status completed", StringComparison.Ordinal)
                .Replace("--artifact <artifact>", $"--artifact {artifact}", StringComparison.Ordinal)
                .Replace("--summary <one-line-summary>", $"--summary {summary}", StringComparison.Ordinal);

        public void SeedUndeliveredEntry(string taskId, string resultNonce) =>
            NotifyReportOutboxStore.MarkUndelivered(
                SeatRoot,
                new NotifyReportOutboxEntry
                {
                    Domain = Domain,
                    Team = Team,
                    TaskId = taskId,
                    ResultNonce = resultNonce,
                    FromRole = "implementation",
                    ToRole = "orchestration",
                    Status = "completed",
                    Artifact = "https://example.test/pr/1585",
                    Summary = "g731-reconcile-entry",
                    CreatedAt = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
                    DeliveryState = "pending",
                    DeliveryError = null,
                },
                "report-routing-root-write-required");

        public ShellResult RunSeatExactCommand(string command)
        {
            var cliAssembly = typeof(NotifyCommand).Assembly.Location;
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = SeatRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"exec dotnet '{cliAssembly}' {ArgumentString(command)}");
            using var process = Process.Start(startInfo)!;
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ShellResult(process.ExitCode, standardOutput, standardError);
        }

        public void MakeHostReadOnly()
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                MakeHostReadOnlyCore();
                return;
            }

            throw Xunit.Sdk.SkipException.ForSkip("G731 OS-denied fixture requires Unix file permissions.");
        }

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        [System.Runtime.Versioning.SupportedOSPlatform("osx")]
        private void MakeHostReadOnlyCore()
        {
            if (OperatingSystem.IsWindows()) throw Xunit.Sdk.SkipException.ForSkip("G731 OS-denied fixture requires Unix file permissions.");
            hostRootModeBeforeReadOnly = File.GetUnixFileMode(HostRoot);
            foreach (var file in Directory.EnumerateFiles(HostRoot, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }

            foreach (var directory in Directory.EnumerateDirectories(HostRoot, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            File.SetUnixFileMode(HostRoot, hostRootModeBeforeReadOnly & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
            hostReadOnly = true;
        }

        public void MakeHostWritable()
        {
            if (!hostReadOnly) return;
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) MakeHostWritableCore();
        }

        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        [System.Runtime.Versioning.SupportedOSPlatform("osx")]
        private void MakeHostWritableCore()
        {
            foreach (var directory in Directory.EnumerateDirectories(HostRoot, "*", SearchOption.AllDirectories))
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
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.SetUnixFileMode(HostRoot, hostRootModeBeforeReadOnly);
            hostReadOnly = false;
        }

        private void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(HostRoot, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Directory.CreateDirectory(Path.GetDirectoryName(ReaderPath)!);
            if (!File.Exists(ReaderPath)) File.WriteAllText(ReaderPath, string.Empty);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG731",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new { resident = NotifyRecordedRole.ExternalResident, reader = $".intent-cli/events/{Team}" },
                    ["implementation"] = new { resident = NotifyRecordedRole.HerdrResident, workspace_id = "wG731", pane_id = "wG731:p2" },
                },
            }));
        }

        private static string ArgumentString(string command)
        {
            var tokens = new List<string>();
            var quote = false;
            var token = new System.Text.StringBuilder();

            void Flush()
            {
                if (token.Length == 0 && !quote) return;
                tokens.Add(token.ToString());
                token.Clear();
            }

            foreach (var character in command)
            {
                if (character == '\'')
                {
                    quote = !quote;
                    continue;
                }

                if (!quote && char.IsWhiteSpace(character))
                {
                    Flush();
                    continue;
                }

                token.Append(character);
            }

            Flush();
            if (tokens.Count > 0 && tokens[0] == "intent-cli")
            {
                tokens.RemoveAt(0);
            }

            return string.Join(' ', tokens.Select(arg => $"'{arg.Replace("'", "'\''", StringComparison.Ordinal)}'"));
        }

        public string HerdrAgents() => JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
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
            if (hostReadOnly) MakeHostWritable();
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
