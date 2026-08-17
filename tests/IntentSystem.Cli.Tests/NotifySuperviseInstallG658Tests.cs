using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G658/G712: scheduler setup authors a per-team, session-scoped artifact.
/// These tests inspect all three authoring formats without invoking a process
/// manager; the repo-local fixture is intentionally retained for inspection.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySuperviseInstallG658Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Label = "intent-cli.supervise.intent-cli.intent-cli-dev";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g712-notify-install-g658-" + Guid.NewGuid().ToString("N"));

    public NotifySuperviseInstallG658Tests()
    {
        Directory.CreateDirectory(root);
        NotifyCommand.ProcessRunnerFactory = () =>
            throw new InvalidOperationException("supervise install must not construct a process runner");
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = request => new NotifySuperviseFirstCycleResult
        {
            Verified = true,
            Status = "first-cycle-verified",
            CycleId = "g658-first-cycle",
            Writer = new NotifySupervisionWriterIdentity
            {
                Pid = 6580,
                ProcessStartTime = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
                Host = "g658-fixture",
            },
            ObservedAt = new DateTimeOffset(2026, 8, 15, 12, 0, 1, TimeSpan.Zero),
        };
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
        NotifySuperviseInstallCommand.Delay = Thread.Sleep;
        NotifySuperviseInstallCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
    }

    [Theory]
    [InlineData("macos", ".plist", "launchctl bootstrap", "launchctl bootout", null, "<key>KeepAlive</key>")]
    [InlineData("windows", ".xml", "schtasks /Create", "schtasks /Delete", null, "<RestartOnFailure>")]
    [InlineData("linux", ".service", "systemctl --user link", "systemctl --user stop", null, "Restart=always")]
    public void ExplicitPlatform_EmitsPerTeamArtifactAndOperatorCommands_WithoutExecution(
        string platform,
        string extension,
        string registrationPrefix,
        string unregistrationPrefix,
        string? expectedUnverified,
        string persistenceMarker)
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--platform", platform,
                "--routing-root", root, "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("supervise-install", result.GetProperty("operation").GetString());
        Assert.Equal(platform, result.GetProperty("platform").GetString());
        Assert.Equal(Label, result.GetProperty("label").GetString());
        Assert.True(result.GetProperty("artifact_written").GetBoolean());
        Assert.Equal("first-cycle-verified", result.GetProperty("first_cycle_status").GetString());
        Assert.False(result.GetProperty("manages_process").GetBoolean());
        Assert.StartsWith(registrationPrefix, result.GetProperty("registration_command").GetString(), StringComparison.Ordinal);
        Assert.StartsWith(unregistrationPrefix, result.GetProperty("unregistration_command").GetString(), StringComparison.Ordinal);

        var invocation = result.GetProperty("supervise_invocation").GetString()!;
        AssertFullInvocation(invocation);
        var artifactPath = result.GetProperty("artifact_path").GetString()!;
        Assert.EndsWith(Label + extension, artifactPath, StringComparison.Ordinal);
        var artifact = File.ReadAllText(artifactPath);
        Assert.Contains(Label, artifact, StringComparison.Ordinal);
        Assert.Contains(persistenceMarker, artifact, StringComparison.Ordinal);
        Assert.Contains("current GUI session only", result.GetProperty("lifetime").GetString(), StringComparison.Ordinal);
        Assert.Contains("reconcile --write", result.GetProperty("reconcile_command").GetString(), StringComparison.Ordinal);
        if (platform == "macos")
        {
            Assert.DoesNotContain("RunAtLoad", artifact, StringComparison.Ordinal);
            Assert.Contains("gui/$(id -u)", result.GetProperty("registration_command").GetString(), StringComparison.Ordinal);
        }
        if (platform == "windows")
        {
            Assert.DoesNotContain("LogonTrigger", artifact, StringComparison.Ordinal);
            Assert.DoesNotContain("StartWhenAvailable>true", artifact, StringComparison.Ordinal);
        }
        if (platform == "linux")
        {
            Assert.DoesNotContain("WantedBy=default.target", artifact, StringComparison.Ordinal);
            Assert.Contains("systemctl --user start", result.GetProperty("registration_command").GetString(), StringComparison.Ordinal);
        }
        foreach (var value in new[] { "notify", "supervise", Domain, Team, "J-Tech-Japan/intent-system", "orchestration", "300", "120" })
        {
            Assert.Contains(value, artifact, StringComparison.Ordinal);
        }

        if (expectedUnverified is not null)
        {
            Assert.Equal(expectedUnverified, result.GetProperty("verification_status").GetString());
            Assert.Contains(expectedUnverified, artifact, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DefaultPlatform_UsesCurrentPlatform_AndDryRunWritesNothing()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--dry-run", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal(result.GetProperty("current_platform").GetString(), result.GetProperty("platform").GetString());
        Assert.False(result.GetProperty("cross_authored").GetBoolean());
        Assert.False(result.GetProperty("artifact_written").GetBoolean());
        Assert.False(File.Exists(result.GetProperty("artifact_path").GetString()));
    }

    [Fact]
    public void SuperviseHelp_ReachesInstallAuthoringSurface()
    {
        using var superviseWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), ["--help"], superviseWriter));
        Assert.Contains("notify supervise install", superviseWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("--platform macos|windows|linux", superviseWriter.ToString(), StringComparison.Ordinal);

        using var installWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), ["install", "--help"], installWriter));
        Assert.Contains("--output <path>", installWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownOutput_PreservesEmitOnlyAndOperatorOwnedContract()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--platform", "linux",
                "--dry-run", "--format", "markdown",
            ],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains(Label, output, StringComparison.Ordinal);
        Assert.Contains("preview-unverified", output, StringComparison.Ordinal);
        Assert.Contains("registration command (operator action)", output, StringComparison.Ordinal);
        Assert.Contains("unregistration command (operator action)", output, StringComparison.Ordinal);
        Assert.Contains("install lifecycle command executed by intent-cli: false", output, StringComparison.Ordinal);
        Assert.Contains("current GUI session only", output, StringComparison.Ordinal);
        AssertFullInvocation(output);
    }

    [Fact]
    public void RenderedHostGuidesAndCatalog_RouteSetupThroughInstallAndRecordAge()
    {
        using var initWriter = new StringWriter();
        Assert.Equal(0, GuideWorkflowTaskInitHostCommand.Execute(CreateContext(), ["--format", "json"], initWriter));
        using var initDocument = JsonDocument.Parse(initWriter.ToString());
        var hostSetups = initDocument.RootElement.GetProperty("roles").EnumerateArray()
            .Where(role => role.TryGetProperty("supervision_setup", out _))
            .Select(role => role.GetProperty("supervision_setup").GetString()!)
            .ToArray();
        Assert.NotEmpty(hostSetups);
        foreach (var setup in hostSetups)
        {
            Assert.Contains("notify supervise install", setup, StringComparison.Ordinal);
            Assert.Contains("cycles.jsonl", setup, StringComparison.Ordinal);
            Assert.Contains("Process-name grep is an anti-pattern", setup, StringComparison.Ordinal);
            Assert.Contains("169796s", setup, StringComparison.Ordinal);
            Assert.Contains("reconcile --write", setup, StringComparison.Ordinal);
            Assert.Contains("GUI-session lifetime", setup, StringComparison.Ordinal);
        }

        using var commandsWriter = new StringWriter();
        Assert.Equal(0, GuideCommandsListCommand.Execute(CreateContext(), ["--format", "json"], commandsWriter));
        using var commandsDocument = JsonDocument.Parse(commandsWriter.ToString());
        var notify = commandsDocument.RootElement.GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("name").GetString() == "notify");
        var purpose = notify.GetProperty("purpose").GetString()!;
        Assert.Contains("notify supervise install", purpose, StringComparison.Ordinal);
        Assert.Contains("reconcile|uninstall", purpose, StringComparison.Ordinal);

        using var nextWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            CreateContext(),
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            nextWriter));
        using var nextDocument = JsonDocument.Parse(nextWriter.ToString());
        var setupAction = nextDocument.RootElement.GetProperty("decision_set").EnumerateArray()
            .Single(action => action.GetProperty("action").GetString() == "supervision-setup");
        var prompt = setupAction.GetProperty("suggested_prompt").GetString()!;
        Assert.Contains("notify supervise install", prompt, StringComparison.Ordinal);
        Assert.Contains("--bound <seconds>", prompt, StringComparison.Ordinal);
        Assert.Contains("--interval <seconds>", prompt, StringComparison.Ordinal);
        Assert.Contains("cycles.jsonl", prompt, StringComparison.Ordinal);
        Assert.Contains("process-name grep", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseReferences_CarryTheSameInstallAndLivenessContract()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));
        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("notify supervise install", document, StringComparison.Ordinal);
            Assert.Contains("--platform macos|windows|linux", document, StringComparison.Ordinal);
            Assert.Contains("intent-cli.supervise.<domain>.<team>", document, StringComparison.Ordinal);
            Assert.Contains("emitted-but-unverified", document, StringComparison.Ordinal);
            Assert.Contains("cycles.jsonl", document, StringComparison.Ordinal);
            Assert.Contains("process-name grep", document, StringComparison.Ordinal);
            Assert.Contains("169796", document, StringComparison.Ordinal);
            Assert.Contains("G658", document, StringComparison.Ordinal);
        }
    }

    private static void AssertFullInvocation(string text)
    {
        Assert.Contains("notify", text, StringComparison.Ordinal);
        Assert.Contains("supervise", text, StringComparison.Ordinal);
        Assert.Contains("--domain", text, StringComparison.Ordinal);
        Assert.Contains("--team", text, StringComparison.Ordinal);
        Assert.Contains("--repo", text, StringComparison.Ordinal);
        Assert.Contains("--owner-role", text, StringComparison.Ordinal);
        Assert.Contains("--bound", text, StringComparison.Ordinal);
        Assert.Contains("--interval", text, StringComparison.Ordinal);
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };
}
