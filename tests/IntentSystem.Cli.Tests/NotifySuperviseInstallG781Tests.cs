using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G781: first-cycle proof distinguishes an artifact that never started from a
/// post-install process that wrote no cycle, supports late re-proof without
/// rewriting the artifact, and paces full-store reads.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySuperviseInstallG781Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Label = "intent-cli.supervise.intent-cli.intent-cli-dev";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g781-supervise-install-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public NotifySuperviseInstallG781Tests()
    {
        Directory.CreateDirectory(root);
        NotifyCommand.ProcessRunnerFactory = () =>
            throw new InvalidOperationException("G781 install and verify must not query an OS lifecycle command");
        NotifyCommand.UtcNowFactory = () => now;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
        NotifySuperviseInstallCommand.UtcNowFactory = () => now;
        NotifySuperviseInstallCommand.Delay = delay => now = now.Add(delay);
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory = () => Path.Combine(root, "home");
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.UtcNowFactory = null;
        NotifySuperviseInstallCommand.FirstCycleProbeFactory = null;
        NotifySuperviseInstallCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        NotifySuperviseInstallCommand.Delay = Thread.Sleep;
        NotifySuperviseArtifactInventory.UserProfileDirectoryFactory =
            () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [Fact]
    public void NoProcessTimeout_ReportsKindRegistrationAndNoMissingLogPaths()
    {
        var artifactPath = ArtifactPath("no-process");

        var result = RunInstall(artifactPath, "--write", startupBoundSeconds: 2);

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Payload);
        var payload = document.RootElement;
        Assert.Equal("no-post-install-process", payload.GetProperty("first_cycle_failure_kind").GetString());
        Assert.Equal(2, payload.GetProperty("first_cycle_attempts").GetInt32());
        Assert.Empty(payload.GetProperty("runtime_logs_observed").EnumerateArray());
        var error = payload.GetProperty("error").GetString()!;
        var registrationCommand = payload.GetProperty("registration_command").GetString()!;
        Assert.Contains("no supervisor process started under artifact", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli does not load scheduler jobs", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(registrationCommand, error, StringComparison.Ordinal);
        Assert.Contains("install --verify", error, StringComparison.Ordinal);
        Assert.DoesNotContain(".stdout.log", error, StringComparison.Ordinal);
        Assert.DoesNotContain(".stderr.log", error, StringComparison.Ordinal);
        Assert.True(File.Exists(artifactPath));
    }

    [Fact]
    public void RuntimeLogGrowthTimeout_ReportsRanButNoCycleAndNamesOnlyExistingLogs()
    {
        var artifactPath = ArtifactPath("wrote-no-cycle");
        var stdoutPath = RuntimeLogPath("stdout");
        var delayCount = 0;
        NotifySuperviseInstallCommand.Delay = delay =>
        {
            delayCount++;
            if (delayCount == 1)
            {
                File.AppendAllText(stdoutPath, "supervisor reached runtime\n");
            }

            now = now.Add(delay);
        };

        var result = RunInstall(artifactPath, "--write", startupBoundSeconds: 2);

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Payload);
        var payload = document.RootElement;
        Assert.Equal("post-install-process-wrote-no-cycle", payload.GetProperty("first_cycle_failure_kind").GetString());
        var observedLogs = payload.GetProperty("runtime_logs_observed")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Equal(new[] { stdoutPath }, observedLogs);
        var error = payload.GetProperty("error").GetString()!;
        Assert.Contains("process ran and wrote no cycle", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(stdoutPath, error, StringComparison.Ordinal);
        Assert.DoesNotContain(RuntimeLogPath("stderr"), error, StringComparison.Ordinal);
        Assert.Contains("install --verify", error, StringComparison.Ordinal);
    }

    [Fact]
    public void PostInstallCycleWithoutWriter_ReportsThirdTimeoutKind()
    {
        var artifactPath = ArtifactPath("missing-writer");
        var delayCount = 0;
        NotifySuperviseInstallCommand.Delay = delay =>
        {
            delayCount++;
            if (delayCount == 1)
            {
                var artifactRoot = CreateContext().ResolveSupervisionArtifactRootPath();
                Assert.True(NotifySupervisionStore.RecordCycle(
                    NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team),
                    new NotifySupervisionCycle
                    {
                        CycleId = "g781-missing-writer",
                        StartedAt = now,
                        CompletedAt = now.AddSeconds(1),
                        IntervalSeconds = 300,
                    },
                    write: true).Applied);
            }

            now = now.Add(delay);
        };

        var result = RunInstall(artifactPath, "--write", startupBoundSeconds: 2);

        Assert.Equal(1, result.ExitCode);
        using var document = JsonDocument.Parse(result.Payload);
        var payload = document.RootElement;
        Assert.Equal("post-install-process-missing-writer", payload.GetProperty("first_cycle_failure_kind").GetString());
        Assert.Contains("without the required writer identity", payload.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyAfterLateCycle_PreservesArtifactRecordsEvidenceAndDoesNotInvokeOsLifecycle_G765()
    {
        var artifactPath = ArtifactPath("late-proof");
        var processRunnerInvoked = false;
        NotifyCommand.ProcessRunnerFactory = () =>
        {
            processRunnerInvoked = true;
            throw new InvalidOperationException("G781 verify must not invoke an OS lifecycle command");
        };

        var initial = RunInstall(artifactPath, "--write", startupBoundSeconds: 1);
        Assert.Equal(1, initial.ExitCode);
        var beforeBytes = File.ReadAllText(artifactPath);
        var writtenAt = new DateTimeOffset(File.GetLastWriteTimeUtc(artifactPath), TimeSpan.Zero);

        var artifactRoot = CreateContext().ResolveSupervisionArtifactRootPath();
        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team),
            new NotifySupervisionCycle
            {
                CycleId = "g781-late-cycle",
                StartedAt = writtenAt.AddSeconds(1),
                CompletedAt = writtenAt.AddSeconds(2),
                IntervalSeconds = 300,
                Writer = new NotifySupervisionWriterIdentity
                {
                    Pid = 7810,
                    ProcessStartTime = writtenAt.AddSeconds(1),
                    Host = "g781-fixture",
                },
            },
            write: true).Applied);

        now = writtenAt.AddSeconds(3);
        var verified = RunInstall(artifactPath, "--verify", startupBoundSeconds: 1);

        Assert.Equal(0, verified.ExitCode);
        using var verifiedDocument = JsonDocument.Parse(verified.Payload);
        var verifiedPayload = verifiedDocument.RootElement;
        Assert.Equal("verify", verifiedPayload.GetProperty("command_mode").GetString());
        Assert.False(verifiedPayload.GetProperty("artifact_written").GetBoolean());
        Assert.Equal(writtenAt, verifiedPayload.GetProperty("artifact_written_at").GetDateTimeOffset());
        Assert.Equal("first-cycle-verified", verifiedPayload.GetProperty("verification_status").GetString());
        Assert.Equal(1, verifiedPayload.GetProperty("first_cycle_attempts").GetInt32());
        Assert.True(File.Exists(verifiedPayload.GetProperty("installed_supervisor_path").GetString()));
        Assert.Equal(beforeBytes, File.ReadAllText(artifactPath));
        Assert.Equal(writtenAt, new DateTimeOffset(File.GetLastWriteTimeUtc(artifactPath), TimeSpan.Zero));
        Assert.False(processRunnerInvoked);

        using var livenessWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(
            CreateContext(),
            ["liveness", "--domain", Domain, "--team", Team, "--format", "json"],
            livenessWriter));
        using var livenessDocument = JsonDocument.Parse(livenessWriter.ToString());
        Assert.Equal(
            "installation-artifact-present",
            livenessDocument.RootElement.GetProperty("scheduler_installation_evidence").GetString());
        Assert.Equal("unknown", livenessDocument.RootElement.GetProperty("scheduler_live_state").GetString());
        Assert.Contains(
            "installed first-cycle record",
            livenessDocument.RootElement.GetProperty("scheduler_evidence_detail").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyRejectsCycleCompletedAtTheArtifactWrittenAt()
    {
        var artifactPath = ArtifactPath("equal-timestamp");
        var initial = RunInstall(artifactPath, "--write", startupBoundSeconds: 1);
        Assert.Equal(1, initial.ExitCode);
        var writtenAt = new DateTimeOffset(File.GetLastWriteTimeUtc(artifactPath), TimeSpan.Zero);

        var artifactRoot = CreateContext().ResolveSupervisionArtifactRootPath();
        Assert.True(NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team),
            new NotifySupervisionCycle
            {
                CycleId = "g781-equal-written-at",
                StartedAt = writtenAt.AddSeconds(-1),
                CompletedAt = writtenAt,
                IntervalSeconds = 300,
                Writer = new NotifySupervisionWriterIdentity
                {
                    Pid = 7811,
                    ProcessStartTime = writtenAt.AddSeconds(-1),
                    Host = "g781-fixture",
                },
            },
            write: true).Applied);

        var verified = RunInstall(artifactPath, "--verify", startupBoundSeconds: 1);

        Assert.Equal(1, verified.ExitCode);
        using var document = JsonDocument.Parse(verified.Payload);
        Assert.Equal(
            "no-post-install-process",
            document.RootElement.GetProperty("first_cycle_failure_kind").GetString());
        Assert.False(File.Exists(Path.Combine(
            artifactRoot,
            Domain,
            Team,
            "installed-supervisor.json")));
    }

    [Fact]
    public void VerifyIsExclusiveWithWriteAndDryRun()
    {
        var artifactPath = ArtifactPath("exclusive");

        var writeConflict = RunInstall(artifactPath, "--verify", startupBoundSeconds: 1, "--write");
        var dryRunConflict = RunInstall(artifactPath, "--verify", startupBoundSeconds: 1, "--dry-run");

        Assert.Equal(1, writeConflict.ExitCode);
        Assert.Equal(1, dryRunConflict.ExitCode);
        Assert.Contains("--verify is exclusive", writeConflict.Payload, StringComparison.Ordinal);
        Assert.Contains("--verify is exclusive", dryRunConflict.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbePacesReadsAtOneSecondAndBoundsAttemptsByStartupWindow()
    {
        var delays = new List<TimeSpan>();
        NotifySuperviseInstallCommand.Delay = delay =>
        {
            delays.Add(delay);
            now = now.Add(delay);
        };

        var result = NotifySuperviseFirstCycleProbe.Wait(new NotifySuperviseFirstCycleRequest
        {
            ArtifactRoot = CreateContext().ResolveSupervisionArtifactRootPath(),
            Domain = Domain,
            Team = Team,
            ArtifactPath = ArtifactPath("paced"),
            CyclePath = Path.Combine(root, "no-cycle.jsonl"),
            StartupBoundSeconds = 3,
            StartedAt = now,
        });

        Assert.False(result.Verified);
        Assert.Equal("first-cycle-timeout", result.Status);
        Assert.Equal("no-post-install-process", result.FailureKind);
        Assert.True(result.Attempts <= 3);
        Assert.NotEmpty(delays);
        Assert.All(delays, delay => Assert.True(delay >= TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationNamesVerifyBoundAndTimeoutDistinctions(string language)
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var path in new[]
        {
            Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"),
            Path.Combine(repoRoot, "docs", language, "08-command-reference.md"),
        })
        {
            var document = File.ReadAllText(path);
            Assert.Contains("--verify", document, StringComparison.Ordinal);
            Assert.Contains("120", document, StringComparison.Ordinal);
            Assert.Contains("no-post-install-process", document, StringComparison.Ordinal);
            Assert.Contains("post-install-process-wrote-no-cycle", document, StringComparison.Ordinal);
            Assert.Contains("post-install-process-missing-writer", document, StringComparison.Ordinal);
        }
    }

    private (int ExitCode, string Payload) RunInstall(
        string artifactPath,
        string mode,
        int startupBoundSeconds,
        params string[] additionalModeArguments)
    {
        var arguments = new List<string>
        {
            "install", "--domain", Domain, "--team", Team,
            "--repo", "J-Tech-Japan/intent-system", "--owner-role", "orchestration",
            "--bound", "900", "--interval", "300",
            "--startup-bound", startupBoundSeconds.ToString(),
            "--platform", "macos", "--output", artifactPath,
            "--routing-root", root, mode,
        };
        arguments.AddRange(additionalModeArguments);
        arguments.AddRange(["--format", "json"]);

        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteSupervise(CreateContext(), arguments.ToArray(), writer);
        return (exitCode, writer.ToString());
    }

    private string ArtifactPath(string fixture) => Path.Combine(
        root,
        "install",
        fixture,
        Label + ".plist");

    private string RuntimeLogPath(string stream) => Path.Combine(
        root,
        ".intent-cli",
        "supervision",
        Domain,
        Team,
        "runtime",
        Label + "." + stream + ".log");

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
