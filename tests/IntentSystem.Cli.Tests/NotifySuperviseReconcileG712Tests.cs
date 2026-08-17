using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G712: the explicit lifecycle command repairs a deliberately drifted
/// current-GUI-session inventory without touching unrelated jobs. Fixtures are
/// repo-local and intentionally retained; no system launchctl or temporary
/// directory cleanup is performed by these tests.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySuperviseReconcileG712Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Prefix = "intent-cli.supervise.";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g712-reconcile-" + Guid.NewGuid().ToString("N"));
    private readonly string home;

    public NotifySuperviseReconcileG712Tests()
    {
        Directory.CreateDirectory(root);
        home = Path.Combine(root, "home");
        NotifyCommand.ProcessRunnerFactory = null;
        NotifySuperviseReconcileCommand.MacOsDetector = () => true;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory = () => home;
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifySuperviseReconcileCommand.MacOsDetector = OperatingSystem.IsMacOS;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory =
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
    }

    [Fact]
    public void ReconcileDryRun_ReportsThreeLoadedJobsAndOneArtifactWithoutMutation()
    {
        var artifact = CreateArtifact("aic-herdr");
        var runner = new LaunchctlRunner(
            "intent-cli.supervise.aic.aic-herdr",
            "intent-cli.supervise.sekiban-as-a-service.sekiban-as-a-service-orch",
            "intent-cli.supervise.remote-herdr.remote-herdr");
        NotifyCommand.ProcessRunnerFactory = () => runner;

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["reconcile", "--platform", "macos", "--dry-run", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("supervise-reconcile", result.GetProperty("operation").GetString());
        Assert.Equal("dry-run", result.GetProperty("command_mode").GetString());
        Assert.Equal(3, result.GetProperty("loaded_before").GetArrayLength());
        Assert.Equal(3, result.GetProperty("would_unload").GetArrayLength());
        Assert.Single(result.GetProperty("artifacts_before").EnumerateArray());
        Assert.Equal(1, result.GetProperty("would_remove_artifacts").GetArrayLength());
        Assert.Empty(runner.BootedOut);
        Assert.True(File.Exists(artifact));
        Assert.Equal(3, result.GetProperty("loaded_after").GetArrayLength());
    }

    [Fact]
    public void ReconcileWrite_UnloadsThreeLoadedJobsAndRemovesTheDriftedArtifact()
    {
        var artifact = CreateArtifact("aic-herdr");
        var runner = new LaunchctlRunner(
            Prefix + "aic-herdr",
            Prefix + "sekiban-as-a-service.sekiban-as-a-service-orch",
            Prefix + "remote-herdr.remote-herdr",
            "com.example.unrelated");
        NotifyCommand.ProcessRunnerFactory = () => runner;

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["uninstall", "--platform", "macos", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("supervise-uninstall", result.GetProperty("operation").GetString());
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal(3, result.GetProperty("loaded_before").GetArrayLength());
        Assert.Equal(3, result.GetProperty("unloaded").GetArrayLength());
        Assert.Single(result.GetProperty("removed_artifacts").EnumerateArray());
        Assert.Empty(result.GetProperty("loaded_after").EnumerateArray());
        Assert.Empty(result.GetProperty("artifacts_after").EnumerateArray());
        Assert.False(File.Exists(artifact));
        Assert.Contains("com.example.unrelated", runner.RemainingLoaded, StringComparer.Ordinal);
        Assert.Equal(
            new[]
            {
                "intent-cli.supervise.aic-herdr",
                "intent-cli.supervise.remote-herdr.remote-herdr",
                "intent-cli.supervise.sekiban-as-a-service.sekiban-as-a-service-orch",
            },
            runner.BootedOut.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void InstallWrite_RemovesLegacyLaunchAgentAndNamesCurrentSessionLifetime()
    {
        var legacyPath = Path.Combine(
            home,
            "Library",
            "LaunchAgents",
            Prefix + Domain + "." + Team + ".plist");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath, "legacy-login-persistent-plist");
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = _ => new NotifySuperviseFirstCycleResult
        {
            Verified = true,
            Status = "first-cycle-verified",
            CycleId = "g712-first-cycle",
            Writer = new NotifySupervisionWriterIdentity
            {
                Pid = 7120,
                ProcessStartTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero),
                Host = "g712-fixture",
            },
            ObservedAt = new DateTimeOffset(2026, 8, 16, 12, 0, 1, TimeSpan.Zero),
        };
        NotifyCommand.ProcessRunnerFactory = () =>
            throw new InvalidOperationException("install must not invoke lifecycle process commands");

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--platform", "macos",
                "--output", Path.Combine(root, "install", Prefix + Domain + "." + Team + ".plist"),
                "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("current GUI session only; no LaunchAgents login auto-load and no reboot persistence", result.GetProperty("lifetime").GetString());
        Assert.Single(result.GetProperty("legacy_artifacts_removed").EnumerateArray());
        Assert.False(File.Exists(legacyPath));
        var artifact = File.ReadAllText(result.GetProperty("artifact_path").GetString()!);
        Assert.DoesNotContain("RunAtLoad", artifact, StringComparison.Ordinal);
        Assert.Contains("launchctl bootstrap gui/$(id -u)", result.GetProperty("registration_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("launchctl bootout gui/$(id -u)/", result.GetProperty("unregistration_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("reconcile --write", result.GetProperty("reconcile_command").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InstallWrite_RejectsLoginAutoLoadedOutputWithoutProbingOrWriting()
    {
        var forbiddenPath = Path.Combine(
            home,
            "Library",
            "LaunchAgents",
            Prefix + Domain + "." + Team + ".plist");
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = _ =>
            throw new InvalidOperationException("first-cycle probe must not run for a login-auto-loaded path");

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "300", "--interval", "120", "--platform", "macos",
                "--output", forbiddenPath, "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("login-auto-loaded-path", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(forbiddenPath));
    }

    [Fact]
    public void SuperviseHelp_ExposesExplicitReconcileAndUninstallSurface()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), ["--help"], writer));
        var help = writer.ToString();
        Assert.Contains("reconcile|uninstall", help, StringComparison.Ordinal);
        Assert.Contains("--write", help, StringComparison.Ordinal);
    }

    [Fact]
    public void OrchestratorGuideRoute_RendersG712LifecycleFromMetadataFreeContext()
    {
        using var jsonWriter = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                CreateContext(),
                ["--format", "json"],
                jsonWriter));
        using var json = JsonDocument.Parse(jsonWriter.ToString());
        var lifetime = json.RootElement
            .GetProperty("design_workspace_supervision")
            .GetProperty("emission_hygiene")
            .GetProperty("session_lifetime");
        Assert.Contains("GUI-session lifetime", lifetime.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("reconcile --write", lifetime.GetProperty("reconcile_command").GetString(), StringComparison.Ordinal);
        Assert.Contains("~/Library/LaunchAgents", lifetime.GetProperty("artifact_location").GetString(), StringComparison.Ordinal);
        Assert.Contains("loaded_before", lifetime.GetProperty("verification")[1].GetString(), StringComparison.Ordinal);

        using var markdownWriter = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                CreateContext(),
                ["--format", "markdown"],
                markdownWriter));
        var markdown = markdownWriter.ToString();
        Assert.Contains("Session-scoped lifecycle and drift repair (G712)", markdown, StringComparison.Ordinal);
        Assert.Contains("launchctl bootstrap gui/$(id -u)", markdown, StringComparison.Ordinal);
        Assert.Contains("no managed artifact is emitted to `~/Library/LaunchAgents`", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("launchctl load", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("launchctl unload", markdown, StringComparison.Ordinal);
    }

    private string CreateArtifact(string suffix)
    {
        var path = Path.Combine(
            root,
            ".intent-cli",
            "supervision",
            Domain,
            Team,
            "install",
            Prefix + suffix + ".plist");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture artifact");
        return path;
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

    private sealed class LaunchctlRunner(params string[] labels) : INotifyProcessRunner
    {
        private readonly HashSet<string> loaded = labels.ToHashSet(StringComparer.Ordinal);
        public List<string> BootedOut { get; } = [];
        public IReadOnlyCollection<string> RemainingLoaded => loaded;

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (fileName == "id")
            {
                return new NotifyProcessResult(0, "501\n", string.Empty);
            }

            if (fileName != "launchctl")
            {
                throw new InvalidOperationException($"unexpected process: {fileName}");
            }

            if (arguments.Count == 1 && arguments[0] == "list")
            {
                var output = string.Join(
                    Environment.NewLine,
                    loaded.Order(StringComparer.Ordinal).Select((label, index) => $"{7120 + index} 0 {label}"));
                return new NotifyProcessResult(0, output, string.Empty);
            }

            if (arguments.Count == 2 && arguments[0] == "bootout")
            {
                var label = arguments[1].Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
                if (!loaded.Remove(label))
                {
                    return new NotifyProcessResult(113, string.Empty, "Could not find service");
                }

                BootedOut.Add(label);
                return new NotifyProcessResult(0, string.Empty, string.Empty);
            }

            throw new InvalidOperationException($"unexpected launchctl arguments: {string.Join(' ', arguments)}");
        }
    }
}
