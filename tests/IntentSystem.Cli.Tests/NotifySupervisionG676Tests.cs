using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G676: cycle writer identity is additive and duplicate supervision is
/// detected without granting the supervisor lifecycle authority.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG676Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string SchedulerLabel = "intent-cli.supervise.intent-cli.intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g676-").FullName;
    private readonly DateTimeOffset firstNow = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG676Tests()
    {
        now = firstNow;
        NotifyCommand.UtcNowFactory = () => now;
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NewCycleRecordsAdditiveWriterIdentity_AndLegacyCycleRemainsReadable()
    {
        var current = Identity(7002, firstNow.AddHours(-2));
        var supervisor = CreateSupervisor(current, _ => false);

        var pass = supervisor.RunOnce();

        Assert.Empty(pass.Findings);
        var state = NotifySupervisionStore.Read(
            CreateContext().ResolveSupervisionArtifactRootPath(),
            Domain,
            Team);
        Assert.NotNull(state.LastCycle);
        Assert.Equal(current.Pid, state.LastCycle!.Writer!.Pid);
        Assert.Equal(current.ProcessStartTime, state.LastCycle.Writer.ProcessStartTime);
        Assert.Equal(current.Host, state.LastCycle.Writer.Host);

        var legacyRoot = Directory.CreateTempSubdirectory("notify-g676-legacy-").FullName;
        try
        {
            var legacyPath = NotifySupervisionStore.ResolveCyclePath(legacyRoot, Domain, Team);
            var legacy = NotifySupervisionStore.RecordCycle(
                legacyPath,
                Cycle("legacy", firstNow.AddSeconds(-10), writer: null),
                write: true);

            Assert.True(legacy.Applied, legacy.Error);
            var legacyState = NotifySupervisionStore.Read(legacyRoot, Domain, Team);
            Assert.NotNull(legacyState.LastCycle);
            Assert.Null(legacyState.LastCycle!.Writer);
        }
        finally
        {
            Directory.Delete(legacyRoot, recursive: true);
        }
    }

    [Fact]
    public void RecentCycleFromDifferentLiveWriter_EmitsOneFindingWithRemedyAndCost()
    {
        var other = Identity(7001, firstNow.AddHours(-3));
        var current = Identity(7002, firstNow.AddHours(-2));
        WriteCycle(Cycle("other", firstNow.AddSeconds(-10), other));

        var pass = CreateSupervisor(current, candidate => candidate.IsSameWriter(other)).RunOnce();

        var finding = Assert.Single(pass.Findings, item => item.Kind == "duplicate-supervisor");
        Assert.Equal("supervision-cycle", finding.Source);
        Assert.Equal(0, pass.ExitCode);
        Assert.Contains("current writer pid=7002", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("other live writer pid=7001", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("host='test-host'", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("other cycle age=10s", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("Duplicate-wake cost", finding.Summary, StringComparison.Ordinal);
        Assert.Contains(SchedulerLabel, finding.Summary, StringComparison.Ordinal);
        Assert.Contains("Detection only", finding.Summary, StringComparison.Ordinal);
        Assert.Empty(pass.Actions);
    }

    [Fact]
    public void DuplicateFindingIsExactlyOnePerCycle_AndDoesNotWakeOrManageWriters()
    {
        var other = Identity(7001, firstNow.AddHours(-3));
        var current = Identity(7002, firstNow.AddHours(-2));
        WriteCycle(Cycle("other-1", firstNow.AddSeconds(-10), other));

        var first = CreateSupervisor(current, candidate => candidate.IsSameWriter(other)).RunOnce();

        Assert.Single(first.Findings, item => item.Kind == "duplicate-supervisor");
        Assert.Empty(first.Actions);

        now = firstNow.AddSeconds(20);
        WriteCycle(Cycle("other-2", now.AddSeconds(-10), other));
        var second = CreateSupervisor(current, candidate => candidate.IsSameWriter(other)).RunOnce();

        Assert.Single(second.Findings, item => item.Kind == "duplicate-supervisor");
        Assert.Empty(second.Actions);
        Assert.DoesNotContain(second.Findings, item => item.Kind is "recipient-lost" or "supervisor-not-running");
    }

    [Theory]
    [InlineData("dead")]
    [InlineData("stale")]
    [InlineData("same")]
    [InlineData("legacy")]
    public void DeadStaleSameOrLegacyPriorCycle_ProducesNoDuplicateFinding(string priorCycle)
    {
        var current = Identity(7002, firstNow.AddHours(-2));
        var other = Identity(7001, firstNow.AddHours(-3));
        NotifySupervisionWriterIdentity? writer = priorCycle switch
        {
            "legacy" => null,
            "same" => current,
            _ => other,
        };
        var completedAt = priorCycle == "stale"
            ? firstNow.AddSeconds(-601)
            : firstNow.AddSeconds(-10);
        WriteCycle(Cycle($"prior-{priorCycle}", completedAt, writer));

        var pass = CreateSupervisor(
            current,
            candidate => priorCycle is not ("dead" or "legacy") && candidate.IsSameWriter(other)).RunOnce();

        Assert.DoesNotContain(pass.Findings, item => item.Kind == "duplicate-supervisor");
        Assert.DoesNotContain(pass.Findings, item => item.Kind == "recipient-lost");
        Assert.Empty(pass.Actions);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void RenderedGuidanceAndPreviewLedgerNameCleanupIncidentAndDetectionBoundary(string language)
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var guidance = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
        var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));

        Assert.Contains("G676", guidance, StringComparison.Ordinal);
        Assert.Contains("duplicate-supervisor", guidance, StringComparison.Ordinal);
        Assert.Contains("stale hand-run", guidance, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "four concurrent" : "4 concurrent", guidance, StringComparison.Ordinal);
        Assert.Contains("intent-cli.supervise.<domain>.<team>", guidance, StringComparison.Ordinal);
        Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        Assert.Contains("G676", ledger, StringComparison.Ordinal);
        Assert.Contains("writer.process_start_time", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallGuidanceRendersStaleSupervisorCleanupAndMeasuredIncident()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer));

        using var document = System.Text.Json.JsonDocument.Parse(writer.ToString());
        var setups = document.RootElement.GetProperty("roles").EnumerateArray()
            .Where(role => role.TryGetProperty("supervision_setup", out _))
            .Select(role => role.GetProperty("supervision_setup").GetString()!)
            .ToArray();
        Assert.NotEmpty(setups);
        Assert.All(setups, setup =>
        {
            Assert.Contains("G676", setup, StringComparison.Ordinal);
            Assert.Contains("stale hand-run supervisors", setup, StringComparison.Ordinal);
            Assert.Contains("duplicate-supervisor", setup, StringComparison.Ordinal);
            Assert.Contains("four concurrent loops", setup, StringComparison.Ordinal);
            Assert.Contains("never kills, stops, ranks, elects", setup, StringComparison.Ordinal);
        });
    }

    private void WriteCycle(NotifySupervisionCycle cycle)
    {
        var result = NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(
                CreateContext().ResolveSupervisionArtifactRootPath(),
                Domain,
                Team),
            cycle,
            write: true);
        Assert.True(result.Applied, result.Error);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        NotifySupervisionWriterIdentity identity,
        Func<NotifySupervisionWriterIdentity, bool> writerIsLive) =>
        new(
            CreateContext(),
            root,
            Domain,
            Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 300,
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
            writerIdentity: identity,
            writerIsLive: writerIsLive);

    private static NotifySupervisionCycle Cycle(
        string cycleId,
        DateTimeOffset completedAt,
        NotifySupervisionWriterIdentity? writer) => new()
    {
        CycleId = cycleId,
        StartedAt = completedAt.AddSeconds(-1),
        CompletedAt = completedAt,
        IntervalSeconds = 300,
        Writer = writer,
    };

    private static NotifySupervisionWriterIdentity Identity(int pid, DateTimeOffset processStartTime) => new()
    {
        Pid = pid,
        ProcessStartTime = processStartTime,
        Host = "test-host",
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
            },
            Supervision = new SupervisionConfig
            {
                ArtifactRoot = ".intent-cli/supervision",
            },
        },
    };

    private sealed class NoOpRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            throw new InvalidOperationException($"Unexpected process spawn: {fileName} {string.Join(' ', arguments)}");
    }
}
