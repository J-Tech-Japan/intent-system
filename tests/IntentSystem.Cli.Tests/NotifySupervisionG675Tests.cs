using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG675Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g675-").FullName;

    public NotifySupervisionG675Tests()
    {
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = _ => new NotifySuperviseFirstCycleResult
        {
            Verified = true,
            Status = "first-cycle-verified",
            CycleId = "g675-first-cycle",
            Writer = new NotifySupervisionWriterIdentity
            {
                Pid = 6750,
                ProcessStartTime = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                Host = "g675-fixture",
            },
            ObservedAt = new DateTimeOffset(2026, 8, 15, 12, 0, 1, TimeSpan.Zero),
        };
    }

    public void Dispose()
    {
        NotifyTransportPaths.ExecutableResolverOverride = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.BashExecutableFactory = null;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
        NotifySuperviseInstallCommand.Delay = Thread.Sleep;
        NotifySuperviseInstallCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TransportStartFailure_IsOneCycleFinding_AndNeverRecipientLostPerDelegation()
    {
        var first = Record("G675-a");
        var second = Record("G675-b", "review");
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, first).Written);
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, second).Written);

        var runner = new TracingRunner((_, arguments) =>
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                throw new InvalidOperationException("Notification transport 'missing-herdr' could not start: executable not found");
            }

            throw new InvalidOperationException("recipient liveness must not run after preflight failure");
        });
        var supervisor = CreateSupervisor(runner, write: false, herdrExecutable: "missing-herdr");

        var pass = supervisor.RunOnce();

        var finding = Assert.Single(pass.Findings);
        Assert.Equal("supervision-degraded", finding.Kind);
        Assert.Equal("transport-unavailable", finding.Cause);
        Assert.Contains("herdr", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("executable not found", finding.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(pass.Findings, item => item.Kind == "recipient-lost");
        Assert.DoesNotContain(pass.Actions, item => item.Verdict == "lost");
        Assert.Single(runner.Calls);
        Assert.Equal("missing-herdr", runner.Calls[0].FileName);
        Assert.Equal(["agent", "list"], runner.Calls[0].Arguments);
    }

    [Fact]
    public void StartableTransport_PreservesGenuineCorroboratedAbsence_AndTracesEverySpawn()
    {
        var record = Record("G675-genuine-loss");
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, record).Written);

        var runner = new TracingRunner((fileName, arguments) =>
        {
            Assert.Equal("/opt/herdr/bin/herdr", fileName);
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, "{\"result\":{\"agents\":[]}}", string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", record.PaneId]))
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            throw new InvalidOperationException($"unexpected transport spawn: {string.Join(' ', arguments)}");
        });
        var supervisor = CreateSupervisor(runner, write: false, herdrExecutable: "/opt/herdr/bin/herdr");

        var pass = supervisor.RunOnce();

        Assert.Contains(pass.Findings, item => item.Kind == "recipient-lost");
        Assert.Contains(pass.Actions, item => item.Verdict == "lost");
        Assert.DoesNotContain(pass.Findings, item => item.Kind == "supervision-degraded");
        Assert.Equal(3, runner.Calls.Count(call => call.Arguments.SequenceEqual(["agent", "list"])));
        Assert.Equal(2, runner.Calls.Count(call => call.Arguments.SequenceEqual(["pane", "process-info", "--pane", record.PaneId])));
        Assert.All(runner.Calls, call => Assert.Equal("/opt/herdr/bin/herdr", call.FileName));
    }

    [Fact]
    public void InstallArtifact_UsesAbsoluteEntrypointAndTransport_AndRecordsPath()
    {
        NotifyTransportPaths.ExecutableResolverOverride = executable => executable switch
        {
            "intent-cli" => "/opt/intent-cli/bin/intent-cli",
            "herdr" => "/opt/herdr/bin/herdr",
            _ => null,
        };

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--event-mode",
                "--platform", "macos", "--routing-root", root, "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.True(result.GetProperty("artifact_written").GetBoolean());
        Assert.Empty(result.GetProperty("unresolved_binaries").EnumerateArray());
        Assert.Contains("/opt/intent-cli/bin/intent-cli", result.GetProperty("supervise_invocation").GetString(), StringComparison.Ordinal);
        Assert.Contains("/opt/herdr/bin/herdr", result.GetProperty("supervise_invocation").GetString(), StringComparison.Ordinal);

        var artifact = File.ReadAllText(result.GetProperty("artifact_path").GetString()!);
        Assert.Contains("<string>/opt/intent-cli/bin/intent-cli</string>", artifact, StringComparison.Ordinal);
        Assert.Contains("--herdr-executable", artifact, StringComparison.Ordinal);
        Assert.Contains("/opt/herdr/bin/herdr", artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("/usr/bin/env intent-cli", artifact, StringComparison.Ordinal);
        Assert.Contains("<key>EnvironmentVariables</key>", artifact, StringComparison.Ordinal);
        Assert.Contains("<key>PATH</key>", artifact, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallEmission_NamesUnresolvedTransportAndKeepsRecordedPath()
    {
        NotifyTransportPaths.ExecutableResolverOverride = executable =>
            executable == "intent-cli" ? "/opt/intent-cli/bin/intent-cli" : null;

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--event-mode",
                "--platform", "linux", "--routing-root", root, "--dry-run", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Contains("herdr", result.GetProperty("unresolved_binaries").EnumerateArray().Select(item => item.GetString()));
        Assert.NotNull(result.GetProperty("recorded_path").GetString());
        Assert.Contains("herdr", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        var artifact = result.GetProperty("artifact_path").GetString()!;
        Assert.False(File.Exists(artifact));
    }

    [Theory]
    [InlineData("macos", false, "<string>intent-cli</string>", "/bin/bash")]
    [InlineData("macos", true, "<string>intent-cli</string>", "/opt/herdr/bin/herdr")]
    [InlineData("windows", false, "<Command>intent-cli</Command>", "/bin/bash")]
    [InlineData("windows", true, "<Command>intent-cli</Command>", "/opt/herdr/bin/herdr")]
    [InlineData("linux", false, "ExecStart=\"intent-cli\"", "/bin/bash")]
    [InlineData("linux", true, "ExecStart=\"intent-cli\"", "/opt/herdr/bin/herdr")]
    public void InstallEmission_WhenIntentCliIsNotPathVisible_UsesBareFallbackForEveryPlatformAndEventMode(
        string platform,
        bool eventMode,
        string entrypointMarker,
        string transportPath)
    {
        NotifyTransportPaths.ExecutableResolverOverride = executable => executable switch
        {
            "bash" => "/bin/bash",
            "herdr" => "/opt/herdr/bin/herdr",
            _ => null,
        };

        using var writer = new StringWriter();
        var outputPath = Path.Combine(root, $"{platform}-{eventMode}.artifact");
        var arguments = new List<string>
        {
            "install", "--domain", Domain, "--team", Team,
            "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
            "--bound", "300", "--interval", "120", "--platform", platform,
            "--routing-root", root, "--output", outputPath, "--write", "--format", "json",
        };
        if (eventMode) arguments.Add("--event-mode");

        var exitCode = NotifyCommand.ExecuteSupervise(CreateContext(), arguments.ToArray(), writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.True(result.GetProperty("artifact_written").GetBoolean());
        Assert.Equal(eventMode, result.GetProperty("event_mode").GetBoolean());
        Assert.Contains("intent-cli", result.GetProperty("unresolved_binaries").EnumerateArray().Select(item => item.GetString()));
        var intentCli = result.GetProperty("runtime_binaries").EnumerateArray()
            .Single(binary => binary.GetProperty("name").GetString() == "intent-cli");
        Assert.False(intentCli.GetProperty("resolved").GetBoolean());
        if (intentCli.TryGetProperty("path", out var path))
        {
            Assert.Equal(JsonValueKind.Null, path.ValueKind);
        }
        Assert.StartsWith("intent-cli ", result.GetProperty("supervise_invocation").GetString(), StringComparison.Ordinal);
        Assert.Contains("intent-cli", result.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var artifact = File.ReadAllText(outputPath);
        Assert.Contains(entrypointMarker, artifact, StringComparison.Ordinal);
        Assert.Contains(transportPath, artifact, StringComparison.Ordinal);
        Assert.DoesNotContain("supervise-install-failed", artifact, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "bash")]
    [InlineData(true, "herdr")]
    public void InstallDryRunMarkdown_WhenIntentCliIsNotPathVisible_RecordsFallbackWithoutWriting(bool eventMode, string transportName)
    {
        NotifyTransportPaths.ExecutableResolverOverride = executable => executable switch
        {
            "bash" => "/bin/bash",
            "herdr" => "/opt/herdr/bin/herdr",
            _ => null,
        };

        using var writer = new StringWriter();
        var arguments = new List<string>
        {
            "install", "--domain", Domain, "--team", Team,
            "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
            "--bound", "300", "--interval", "120", "--platform", "linux",
            "--routing-root", root, "--dry-run", "--format", "markdown",
        };
        if (eventMode) arguments.Add("--event-mode");

        var exitCode = NotifyCommand.ExecuteSupervise(CreateContext(), arguments.ToArray(), writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("- command mode: dry-run", output, StringComparison.Ordinal);
        Assert.Contains("(written: false)", output, StringComparison.Ordinal);
        Assert.Contains("- unresolved binaries: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("- supervise invocation: `intent-cli ", output, StringComparison.Ordinal);
        Assert.Contains($"{transportName}=", output, StringComparison.Ordinal);
        Assert.DoesNotContain("supervise-install-failed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OrchestratorGuide_RendersConcreteEventWakeSourceAndG675Verification()
    {
        using var modeWriter = new StringWriter();
        Assert.Equal(
            0,
            SessionLayerCommand.ExecuteSet(
                CreateContext(),
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                modeWriter));

        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                CreateContext(),
                ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "markdown"],
                writer));

        var output = writer.ToString();
        Assert.Contains("notify supervise wake sources", output, StringComparison.Ordinal);
        Assert.Contains("--event-mode", output, StringComparison.Ordinal);
        Assert.Contains("pane.agent_status_changed", output, StringComparison.Ordinal);

        foreach (var language in new[] { "en", "ja" })
        {
            var guidance = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("G675", guidance, StringComparison.Ordinal);
            Assert.Contains("live PID", guidance, StringComparison.Ordinal);
            Assert.Contains("cycles.jsonl", guidance, StringComparison.Ordinal);
            Assert.Contains("supervision-degraded", guidance, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
            Assert.Contains("G675", ledger, StringComparison.Ordinal);
        }
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        INotifyProcessRunner runner,
        bool write,
        string herdrExecutable) =>
        new(
            CreateContext(),
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
            write,
            format: "json",
            runner,
            herdrExecutable,
            agmsgScriptsDirectory: root);

    private NotifyPendingDelegation Record(string taskId, string role = "implementation") => new()
    {
        Domain = Domain,
        Team = Team,
        TaskId = taskId,
        DelegatingRole = "orchestration",
        RecipientRole = role,
        ReportToRole = "orchestration",
        RecipientIdentity = $"role={role}",
        ExpectedArtifact = "artifact",
        ExpectedArtifacts = ["artifact"],
        DispatchedAt = DateTimeOffset.UtcNow,
        TransportMode = SessionLayerMode.HerdrOnly,
        Resident = NotifyRecordedRole.HerdrResident,
        WorkspaceId = "wG675",
        PaneId = $"wG675:{role}",
    };

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private sealed class TracingRunner(
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
