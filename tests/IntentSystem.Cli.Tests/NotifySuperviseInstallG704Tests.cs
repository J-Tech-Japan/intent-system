using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G704: install validation, observable launchd artifacts, bounded first-cycle
/// proof, installed-writer attribution, and the grammar-only Claude registry.
/// Fixtures are repository-local and intentionally retained; no test cleanup
/// deletes a system-temporary path or registers a real service.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySuperviseInstallG704Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Label = "intent-cli.supervise.intent-cli.intent-cli-dev";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        $"g704-tests-{Guid.NewGuid():N}");
    private readonly DateTimeOffset firstNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;
    private NotifySuperviseFirstCycleResult firstCycle;

    public NotifySuperviseInstallG704Tests()
    {
        Directory.CreateDirectory(root);
        now = firstNow;
        firstCycle = VerifiedFirstCycle();
        NotifyCommand.UtcNowFactory = () => now;
        NotifyCommand.ProcessRunnerFactory = () =>
            throw new InvalidOperationException("G704 install tests must not construct a process runner");
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = _ => firstCycle;
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
        NotifySuperviseInstallCommand.Delay = Thread.Sleep;
        NotifySuperviseInstallCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
    }

    [Fact]
    public void BoundBelowIntervalIsRejectedWithStructuralAbsenceConsequence()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "120", "--interval", "300", "--platform", "macos",
                "--routing-root", root, "--dry-run", "--format", "json",
            ],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var error = document.RootElement.GetProperty("error").GetString()!;
        Assert.Contains("bound-below-interval", error, StringComparison.Ordinal);
        Assert.Contains("structurally judged absent", error, StringComparison.Ordinal);
        Assert.Contains("supervisor-not-running", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeKeepsTheContradictionWarningForLegacyRecords()
    {
        var pass = new NotifyMeasuredSupervisor(
            CreateContext(),
            root,
            Domain,
            Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 300,
            declaredBoundSeconds: 120,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: false,
            format: "json",
            runner: new NoOpRunner(),
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: root,
            repeatBackoffSeconds: 60,
            debounceConsecutiveObservations: 3).RunOnce();

        Assert.Contains(
            pass.Warnings,
            warning => warning.Contains("structurally exceed", StringComparison.Ordinal));
    }

    [Fact]
    public void MacArtifactNamesWorkingDirectoryAndBothRuntimeLogs_AndRecordsFirstWriter()
    {
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "900", "--interval", "300", "--startup-bound", "5",
                "--platform", "macos", "--routing-root", root, "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.Equal("first-cycle-verified", result.GetProperty("first_cycle_status").GetString());
        var runtimeDirectory = result.GetProperty("runtime_directory").GetString()!;
        var stdout = result.GetProperty("stdout_log_path").GetString()!;
        var stderr = result.GetProperty("stderr_log_path").GetString()!;
        Assert.StartsWith(runtimeDirectory, stdout, StringComparison.Ordinal);
        Assert.StartsWith(runtimeDirectory, stderr, StringComparison.Ordinal);
        Assert.Contains("WorkingDirectory", File.ReadAllText(result.GetProperty("artifact_path").GetString()!), StringComparison.Ordinal);
        var artifact = File.ReadAllText(result.GetProperty("artifact_path").GetString()!);
        Assert.Contains($"<string>{root}</string>", artifact, StringComparison.Ordinal);
        Assert.Contains($"<string>{stdout}</string>", artifact, StringComparison.Ordinal);
        Assert.Contains($"<string>{stderr}</string>", artifact, StringComparison.Ordinal);
        Assert.True(File.Exists(result.GetProperty("installed_supervisor_path").GetString()!));
    }

    [Fact]
    public void MissingFirstCycleFailsNamedWithBothLogPathsAndLeavesArtifact()
    {
        firstCycle = new NotifySuperviseFirstCycleResult
        {
            Verified = false,
            Status = "first-cycle-timeout",
            Attempts = 2,
            FailureReason = "fixture managed process never appended cycles.jsonl",
        };

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(
            CreateContext(),
            [
                "install", "--domain", Domain, "--team", Team,
                "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
                "--bound", "900", "--interval", "300", "--startup-bound", "1",
                "--platform", "macos", "--routing-root", root, "--write", "--format", "json",
            ],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("first-cycle-proof-failed", output, StringComparison.Ordinal);
        Assert.Contains(".stdout.log", output, StringComparison.Ordinal);
        Assert.Contains(".stderr.log", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(
            root,
            ".intent-cli",
            "supervision",
            Domain,
            Team,
            "install",
            Label + ".plist")));
    }

    [Fact]
    public void InstalledWriterDifferenceIsDuplicateAndUsesG699ParkWithoutTerminalEvidence()
    {
        var installed = Identity(7040, firstNow.AddHours(-2));
        var current = Identity(7041, firstNow.AddHours(-1));
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        Assert.True(NotifySupervisionStore.RecordInstalledSupervisor(
            artifactRoot,
            new NotifySupervisionInstalledSupervisor
            {
                Domain = Domain,
                Team = Team,
                Label = Label,
                ArtifactPath = Path.Combine(root, "installed.plist"),
                Writer = installed,
                StartupBoundSeconds = 30,
                RecordedAt = firstNow.AddMinutes(-5),
            },
            write: true).Applied);
        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team),
            new NotifySupervisionCycle
            {
                CycleId = "installed-cycle",
                StartedAt = firstNow.AddSeconds(-11),
                CompletedAt = firstNow.AddSeconds(-10),
                IntervalSeconds = 300,
                Writer = installed,
            },
            write: true).Applied);

        var supervisor = new NotifyMeasuredSupervisor(
            context,
            root,
            Domain,
            Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 10,
            declaredBoundSeconds: null,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: true,
            format: "json",
            runner: new NoOpRunner(),
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: root,
            writerIdentity: current,
            writerIsLive: identity => identity.IsSameWriter(installed),
            repeatBackoffSeconds: 60,
            debounceConsecutiveObservations: 3);

        var first = supervisor.RunOnce();
        var finding = Assert.Single(first.Findings, value => value.Kind == "duplicate-supervisor");
        Assert.Contains("installed writer", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("terminal-content evidence", finding.Summary, StringComparison.Ordinal);
        Assert.Empty(first.Actions);

        now = firstNow.AddSeconds(20);
        var parked = supervisor.RunOnce();
        Assert.DoesNotContain(parked.Findings, value => value.Kind == "duplicate-supervisor");
        var record = Assert.Single(parked.RecoveryRecords, value => value.Kind == "duplicate-supervisor");
        Assert.True(record.Parked);
        Assert.Equal(2, record.RepeatCount);
        Assert.Equal(60, record.EmissionCadenceSeconds);
        Assert.Empty(parked.Actions);
    }

    [Fact]
    public void ClaudeRegistryIsGrammarOnlyAndGuideRendersIt()
    {
        var recipe = AgentLaunchRecipeRegistry.Find("claude");
        Assert.NotNull(recipe);
        Assert.Equal("claude --model <id> --add-dir <host-root>", recipe!.Invocation);
        Assert.DoesNotContain("sonnet", JsonSerializer.Serialize(recipe), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opus", JsonSerializer.Serialize(recipe), StringComparison.OrdinalIgnoreCase);

        using var writer = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", "json"],
            writer));
        using var guide = JsonDocument.Parse(writer.ToString());
        var unattended = guide.RootElement
            .GetProperty("terminal_workspace_provisioning")
            .GetProperty("unattended_launch_recipes");
        Assert.Contains(
            unattended.GetProperty("recorded_kinds").EnumerateArray().Select(value => value.GetString()),
            value => value == "claude");
        Assert.Equal(
            "claude --model <id> --add-dir <host-root>",
            unattended.GetProperty("claude_recipe").GetProperty("invocation").GetString());
        Assert.Contains(
            unattended.GetProperty("model_flag_grammars").EnumerateArray(),
            value => value.GetProperty("kind").GetString() == "claude"
                && value.GetProperty("add_dir").GetString() == "--add-dir <host-root>");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationCarriesG704ContractInBothLanguages(string language)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md");
        var text = File.ReadAllText(path);
        foreach (var marker in new[]
        {
            "G704",
            "bound-below-interval",
            "supervisor-not-running",
            "WorkingDirectory",
            "StandardOutPath",
            "StandardErrorPath",
            "first-cycle-proof-failed",
            "duplicate-supervisor",
            "G699",
            "claude --model <id> --add-dir <host-root>",
        })
        {
            Assert.Contains(marker, text, StringComparison.Ordinal);
        }
    }

    private NotifySuperviseFirstCycleResult VerifiedFirstCycle() => new()
    {
        Verified = true,
        Status = "first-cycle-verified",
        CycleId = "g704-first-cycle",
        Writer = Identity(7042, firstNow),
        ObservedAt = firstNow.AddSeconds(1),
    };

    private static NotifySupervisionWriterIdentity Identity(int pid, DateTimeOffset start) => new()
    {
        Pid = pid,
        ProcessStartTime = start,
        Host = "g704-fixture",
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

    private sealed class NoOpRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            new(0, "{\"result\":{\"agents\":[]}}", string.Empty);
    }
}
