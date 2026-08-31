using System.Text.Json;
using System.Xml.Linq;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G765: declared supervision persistence, explicit reconciliation ownership,
/// and read-only liveness independent of the supervisor process.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySuperviseCommandTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Label = "intent-cli.supervise.intent-cli.intent-cli-dev";
    private const string PersistenceMarker = "intent-cli persistence-intent: persistent";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g765-supervision-" + Guid.NewGuid().ToString("N"));
    private readonly string home;
    private readonly DateTimeOffset now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    public NotifySuperviseCommandTests()
    {
        Directory.CreateDirectory(root);
        home = Path.Combine(root, "home");
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.UtcNowFactory = () => now;
        NotifySuperviseReconcileCommand.MacOsDetector = () => true;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory = () => home;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = _ => VerifiedFirstCycle();
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.UtcNowFactory = null;
        NotifySuperviseReconcileCommand.MacOsDetector = OperatingSystem.IsMacOS;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory =
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
    }

    [Fact]
    public void InstallPersistent_RecordsIntentInArtifactMetadata()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "900", "--interval", "300", "--platform", "macos",
                "--output", Path.Combine(root, "install", Label + ".plist"),
                "--persistence", "persistent", "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("persistent", result.GetProperty("persistence_intent").GetString());
        var artifact = File.ReadAllText(result.GetProperty("artifact_path").GetString()!);
        Assert.Contains(PersistenceMarker, artifact, StringComparison.Ordinal);
        Assert.NotNull(XDocument.Parse(artifact));
    }

    [Fact]
    public void ReconcileWrite_KeepsDeclaredPersistentAndRemovesLegacyArtifacts()
    {
        var persistent = CreateArtifact("persistent", PersistenceMarker);
        var legacy = CreateLegacyArtifact("legacy artifact");
        NotifyCommand.ProcessRunnerFactory = () => new EmptyLaunchctlRunner();

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["reconcile", "--platform", "macos", "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Contains(
            persistent,
            result.GetProperty("kept_persistent_artifacts").EnumerateArray().Select(value => value.GetString()),
            StringComparer.Ordinal);
        Assert.Equal(
            legacy,
            result.GetProperty("removed_artifacts").EnumerateArray().Single().GetString());
        Assert.True(File.Exists(persistent));
        Assert.False(File.Exists(legacy));
        Assert.Contains(
            persistent,
            result.GetProperty("artifacts_after").EnumerateArray().Select(value => value.GetString()),
            StringComparer.Ordinal);
        Assert.Contains("declared persistent", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Liveness_IsReadOnlyAndReportsAbsentSupervisorAgainstOldCycle()
    {
        Assert.True(NotifySupervisionStore.RecordBound(
            NotifySupervisionArtifactRoot(),
            new NotifySupervisionBound
            {
                Domain = Domain,
                Team = Team,
                BoundSeconds = 900,
                RecordedAt = now.AddHours(-3),
            },
            write: true).Applied);
        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(NotifySupervisionArtifactRoot(), Domain, Team),
            new NotifySupervisionCycle
            {
                CycleId = "g765-old-cycle",
                StartedAt = now.AddHours(-3).AddSeconds(-4),
                CompletedAt = now.AddHours(-3),
                IntervalSeconds = 300,
            },
            write: true).Applied);
        NotifyCommand.ProcessRunnerFactory = () =>
            throw new InvalidOperationException("read-only liveness must not invoke an OS process");

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["liveness", "--domain", Domain, "--team", Team, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("supervise-liveness", result.GetProperty("operation").GetString());
        Assert.Equal(900, result.GetProperty("declared_bound_seconds").GetInt32());
        Assert.Equal(10_800, result.GetProperty("elapsed_seconds").GetInt64());
        Assert.True(result.GetProperty("absent_since_last_cycle").GetBoolean());
        Assert.False(result.GetProperty("scheduler_job_loaded").GetBoolean());
        Assert.Equal("read-only", result.GetProperty("command_mode").GetString());
    }

    [Fact]
    public void Liveness_DoesNotRequireSupervisorProcessOrSchedulerLifecycle()
    {
        NotifyCommand.ProcessRunnerFactory = () =>
            throw new InvalidOperationException("liveness must not run launchctl, systemctl, or a supervisor");

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["liveness", "--domain", Domain, "--team", Team, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.False(result.GetProperty("scheduler_job_loaded").GetBoolean());
        Assert.Contains("no supervisor process", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("launchctl", result.GetProperty("commands_executed").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemctl", result.GetProperty("commands_executed").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationNamesDeclaredPersistenceAndIndependentLiveness(string language)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md");
        var document = File.ReadAllText(path);
        Assert.Contains("G765", document, StringComparison.Ordinal);
        Assert.Contains("--persistence persistent", document, StringComparison.Ordinal);
        Assert.Contains("notify supervise liveness", document, StringComparison.Ordinal);
        Assert.Contains("launchctl", document, StringComparison.Ordinal);
        if (language == "en")
        {
            Assert.Contains("does not execute", document, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("実行しません", document, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DesignGuidePointsToReadOnlySupervisionLiveness()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(CreateContext(), ["--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var residual = document.RootElement.GetProperty("monitoring").GetProperty("residual_design_check").GetString();
        Assert.Contains("notify supervise liveness", residual, StringComparison.Ordinal);
        Assert.Contains("read-only", residual, StringComparison.OrdinalIgnoreCase);
    }

    private string NotifySupervisionArtifactRoot() => Path.Combine(root, ".intent-cli", "supervision");

    private string CreateArtifact(string suffix, string content)
    {
        var path = Path.Combine(
            NotifySupervisionArtifactRoot(),
            Domain,
            Team,
            "install",
            suffix,
            Label + ".plist");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateLegacyArtifact(string content)
    {
        var path = Path.Combine(home, "Library", "LaunchAgents", Label + ".plist");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private NotifySuperviseFirstCycleResult VerifiedFirstCycle() => new()
    {
        Verified = true,
        Status = "first-cycle-verified",
        CycleId = "g765-first-cycle",
        Writer = new NotifySupervisionWriterIdentity
        {
            Pid = 7650,
            ProcessStartTime = now,
            Host = "g765-fixture",
        },
        ObservedAt = now.AddSeconds(1),
    };

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

    private sealed class EmptyLaunchctlRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            if (fileName == "id")
            {
                return new NotifyProcessResult(0, "501\n", string.Empty);
            }

            if (fileName == "launchctl" && arguments.Count == 1 && arguments[0] == "list")
            {
                return new NotifyProcessResult(0, string.Empty, string.Empty);
            }

            throw new InvalidOperationException($"unexpected process: {fileName} {string.Join(' ', arguments)}");
        }
    }
}
