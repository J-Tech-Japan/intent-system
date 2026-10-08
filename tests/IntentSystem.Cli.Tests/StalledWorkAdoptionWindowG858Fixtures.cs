using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed partial class StalledWorkAdoptionWindowG858Tests
{
    private static JsonDocument Report(
        CliContext context,
        string[]? extraArgs = null,
        DateTimeOffset? now = null,
        int staleMinutes = 0)
    {
        AutomationStalledWorkCommand.UtcNowFactory = () => now ?? Now;
        using var writer = new StringWriter();
        var args = new List<string>
        {
            "--domain", Domain, "--repo", Repo, "--stale-minutes", staleMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--format", "json",
        };
        if (extraArgs is not null)
        {
            args.AddRange(extraArgs);
        }
        var exitCode = AutomationStalledWorkCommand.Execute(context, args.ToArray(), writer);
        Assert.Equal(0, exitCode);
        return JsonDocument.Parse(writer.ToString());
    }

    private static void SetMode(CliContext context, string mode, DateTimeOffset at, string? team = null)
    {
        TeamModeCommand.UtcNowFactory = () => at;
        var args = new List<string> { "--domain", Domain };
        if (team is not null)
        {
            args.AddRange(["--team", team]);
        }
        args.AddRange(["--mode", mode, "--write", "--format", "json"]);
        using var writer = new StringWriter();
        Assert.Equal(0, TeamModeCommand.ExecuteSet(context, args.ToArray(), writer));
    }

    private static RunEvent IssueEvent(
        string unit,
        DateTimeOffset at,
        string? linkedIssue,
        string? reason,
        string? repo = null) => new()
    {
        Ts = at,
        ExecutionUnit = unit,
        Event = "issue-created",
        By = "fixture",
        LinkedIssue = linkedIssue,
        Reason = reason,
        Repo = repo,
    };

    private static void PublishFlow(AdoptionWorkspace workspace, string unit, int issueNumber, DateTimeOffset at)
    {
        IssuePublishFlowCommand.CreatorFactory = () => new StubCreator(
            $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}");
        IssuePublishFlowCommand.UtcNowFactory = () => at;
        using var writer = new StringWriter();
        var exitCode = IssuePublishFlowCommand.Execute(
            workspace.Context,
            [unit, "--repo", Repo, "--write", "--format", "json"],
            writer);
        Assert.True(exitCode == 0, writer.ToString());
    }

    private static void PublishMarker(AdoptionWorkspace workspace, string unit, DateTimeOffset at)
    {
        IssuePublishCommand.TimestampFactory = () => at;
        using var writer = new StringWriter();
        var exitCode = IssuePublishCommand.Execute(workspace.Context, [unit], writer);
        Assert.True(exitCode == 0, writer.ToString());
    }

    private static void WriteCloseout(AdoptionWorkspace workspace, string unit, int pr, DateTimeOffset at)
    {
        workspace.AttachPrsAndSetReviewState([unit], [pr]);
        CloseoutPrCommand.UtcNowFactory = () => at;
        using var writer = new StringWriter();
        Assert.Equal(0, CloseoutPrCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--pr", pr.ToString(System.Globalization.CultureInfo.InvariantCulture),
             "--pr-merged", "true", "--write", "--format", "json"],
            writer));
    }

    [Fact]
    public void V04_ActualIssueCreateWriterEmitsRepositoryBoundUrlOnlyStart()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);
        const string unit = "G858-url-only";
        workspace.WriteDebtPacket(unit);
        workspace.WriteDraftPublishArtifact(unit);
        IssueCreateCommand.PublisherFactory = () => new FakePublisher(412);
        IssueCreateCommand.GitCommandRunnerFactory = () => new FakeGitRunner();
        IssueCreateCommand.TimestampFactory = () => cutoff;

        using var writer = new StringWriter();
        var exitCode = IssueCreateCommand.Execute(workspace.Context, [unit], writer);
        Assert.True(exitCode == 0, writer.ToString());
        var emitted = Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunLogPath)));
        Assert.Equal("issue-created", emitted.Event);
        Assert.Equal(unit, emitted.ExecutionUnit);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/412", emitted.LinkedIssue);
        Assert.Null(emitted.Reason);
        Assert.Null(emitted.Repo);

        PublishMarker(workspace, unit, cutoff.AddDays(1));
        var published = Assert.Single(RunLogSerializer.DeserializeAll(File.ReadAllText(workspace.RunLogPath)),
            item => item.Event == "issue-published");
        Assert.Equal(unit, published.ExecutionUnit);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/412", published.LinkedIssue);
        Assert.Equal(cutoff.AddDays(1), published.Ts);

        workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2412);
        using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(3));
        Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Equal(1, result.RootElement.GetProperty("debt_window").GetProperty("decision_counts")
            .GetProperty("included_units").GetInt32());
    }

    [Fact]
    public void V05_ActualClaimWriterUsesEarliestAcquisitionNotReleaseOrReacquisitionTime()
    {
        using (var workspace = new AdoptionWorkspace())
        using (var claims = new ClaimRepositories())
        {
            const string unit = "G858-release-reacquire";
            workspace.WriteDebtPacket(unit);
            var design = ClaimRequestFor(unit, "designer", Team);
            Assert.Equal("acquired", ClaimCommand.RunTransaction(claims.FirstClone, design).Status);
            var release = ClaimCommand.RunTransaction(claims.FirstClone, design with
            {
                Operation = ClaimOperation.Release,
                Reason = "design handoff completed",
            });
            Assert.Equal("released", release.Status);
            Assert.NotNull(release.HistoryPath);
            var implementation = ClaimRequestFor(unit, "implementation", Team);
            Assert.Equal("acquired", ClaimCommand.RunTransaction(claims.FirstClone, implementation).Status);

            var inspection = claims.CloneForInspection();
            var activePath = Path.Combine(inspection, ClaimCommand.ClaimPath(implementation.Scope));
            var historyPath = Path.Combine(inspection, release.HistoryPath!);
            using var activeJson = JsonDocument.Parse(File.ReadAllText(activePath));
            using var historyJson = JsonDocument.Parse(File.ReadAllText(historyPath));
            var firstAcquiredAt = historyJson.RootElement.GetProperty("displaced_claimed_at").GetDateTimeOffset();
            var releaseRecordedAt = historyJson.RootElement.GetProperty("recorded_at").GetDateTimeOffset();
            var reacquiredAt = activeJson.RootElement.GetProperty("claimed_at").GetDateTimeOffset();
            var cutoff = Midpoint(firstAcquiredAt, releaseRecordedAt);
            Assert.True(firstAcquiredAt < cutoff && cutoff < releaseRecordedAt);
            Assert.True(reacquiredAt > releaseRecordedAt);
            Assert.Equal(Team, historyJson.RootElement.GetProperty("displaced_team").GetString());

            workspace.CopyClaimEvidence(unit, activePath, historyPath);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2610);
            var activeBytes = File.ReadAllBytes(workspace.ClaimPath(unit));
            var historyBytes = File.ReadAllBytes(workspace.ClaimHistoryPath(unit, Path.GetFileName(historyPath)));
            SetMode(workspace.Context, TeamMode.SoloConductor, cutoff, Team);

            using var result = Report(workspace.Context, ["--team", Team], now: reacquiredAt.AddDays(1));
            Assert.Equal(activeBytes, File.ReadAllBytes(workspace.ClaimPath(unit)));
            Assert.Equal(historyBytes, File.ReadAllBytes(workspace.ClaimHistoryPath(unit, Path.GetFileName(historyPath))));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("debt_window_evidence_kind").GetString() == "claim-history"
                && item.GetProperty("debt_window_evidence_at").GetDateTimeOffset() == firstAcquiredAt);
        }

        using (var workspace = new AdoptionWorkspace())
        using (var claims = new ClaimRepositories())
        {
            const string unit = "G858-displaced-start";
            workspace.WriteDebtPacket(unit);
            var displacedAcquire = ClaimRequestFor(unit, "alice", Team);
            Assert.Equal("acquired", ClaimCommand.RunTransaction(claims.FirstClone, displacedAcquire).Status);
            var takeover = ClaimCommand.RunTransaction(claims.SecondClone, displacedAcquire with
            {
                Operation = ClaimOperation.Takeover,
                Actor = "bob",
                Team = "intent-cli-reviewers",
                Reason = "operator reassigned the unit",
                DisplacedHolder = "alice",
            });
            Assert.Equal("taken-over", takeover.Status);
            Assert.NotNull(takeover.HistoryPath);

            var inspection = claims.CloneForInspection();
            var historyPath = Path.Combine(inspection, takeover.HistoryPath!);
            using var historyJson = JsonDocument.Parse(File.ReadAllText(historyPath));
            var displacedClaimedAt = historyJson.RootElement.GetProperty("displaced_claimed_at").GetDateTimeOffset();
            var takeoverRecordedAt = historyJson.RootElement.GetProperty("recorded_at").GetDateTimeOffset();
            var cutoff = Midpoint(displacedClaimedAt, takeoverRecordedAt);
            Assert.True(displacedClaimedAt < cutoff && cutoff < takeoverRecordedAt);
            Assert.Equal(Team, historyJson.RootElement.GetProperty("displaced_team").GetString());

            // Retain only the displaced acquisition record so the test pins
            // that it, not the later takeover's recorded_at, establishes the
            // unit's start and team provenance.
            workspace.CopyClaimEvidence(unit, null, historyPath);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2611);
            SetMode(workspace.Context, TeamMode.SoloConductor, cutoff, Team);
            using var result = Report(workspace.Context, ["--team", Team], now: takeoverRecordedAt.AddDays(1));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("debt_window_evidence_kind").GetString() == "claim-history"
                && item.GetProperty("debt_window_evidence_at").GetDateTimeOffset() == displacedClaimedAt);
        }
    }

    private static DateTimeOffset Midpoint(DateTimeOffset first, DateTimeOffset second) =>
        first.AddTicks((second - first).Ticks / 2);

    private static ClaimRequest ClaimRequestFor(string unit, string actor, string team) => new(
        ClaimOperation.Acquire,
        $"execution-unit:{unit}",
        actor,
        team,
        null,
        null,
        true,
        "json",
        ClaimCommand.DefaultMaxAttempts);

    private static void RecordWriteback(CliContext context, string unit, DateTimeOffset at)
    {
        AutomationKnowledgeWriteBackRecordCommand.UtcNowFactory = () => at;
        using var writer = new StringWriter();
        var exitCode = AutomationKnowledgeWriteBackRecordCommand.Execute(context,
            ["--execution-unit", unit, "--commit", new string('a', 40), "--role", "design", "--write", "--format", "json"], writer);
        Assert.True(exitCode == 0, writer.ToString());
    }

    [Fact]
    public void V08_ExactDomainWideUniqueFallbackAndAmbiguousScopesAreExplicit()
    {
        var cutoff = new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);
        using (var exact = new AdoptionWorkspace())
        {
            exact.WriteDebtPacket("G858-exact-team");
            exact.WriteClaim("G858-exact-team", Team, cutoff.AddDays(-1));
            exact.WriteCloseoutOnly("G858-exact-team", cutoff.AddDays(1), 2801);
            SetMode(exact.Context, TeamMode.SoloConductor, cutoff, Team);
            using var report = Report(exact.Context, ["--team", Team], now: cutoff.AddDays(2));
            Assert.Equal("team-specific", report.RootElement.GetProperty("debt_window").GetProperty("resolved_scope").GetString());
            Assert.Equal(Team, report.RootElement.GetProperty("debt_window").GetProperty("resolved_team").GetString());
            Assert.Contains(report.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-exact-team"
                && item.GetProperty("reason").GetString() == "debt-window-historical");
        }

        using (var fallback = new AdoptionWorkspace())
        {
            fallback.WriteDebtPacket("G858-unique-team");
            fallback.WriteClaim("G858-unique-team", Team, cutoff.AddDays(-1));
            fallback.WriteCloseoutOnly("G858-unique-team", cutoff.AddDays(1), 2802);
            SetMode(fallback.Context, TeamMode.SoloConductor, cutoff, Team);
            using var report = Report(fallback.Context, now: cutoff.AddDays(2));
            Assert.Equal("unique-team-fallback", report.RootElement.GetProperty("debt_window").GetProperty("resolved_scope").GetString());
            Assert.Equal(Team, report.RootElement.GetProperty("debt_window").GetProperty("resolved_team").GetString());
        }

        using (var domainWide = new AdoptionWorkspace())
        {
            domainWide.WriteDebtPacket("G858-domain-wide");
            domainWide.WriteClaim("G858-domain-wide", "foreign-team", cutoff.AddDays(-1));
            domainWide.WriteCloseoutOnly("G858-domain-wide", cutoff.AddDays(1), 2803);
            SetMode(domainWide.Context, TeamMode.SoloConductor, cutoff);
            using var report = Report(domainWide.Context, now: cutoff.AddDays(2));
            Assert.Equal("domain-wide", report.RootElement.GetProperty("debt_window").GetProperty("resolved_scope").GetString());
            Assert.Null(report.RootElement.GetProperty("debt_window").GetProperty("resolved_team").GetString());
            Assert.Contains(report.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-domain-wide"
                && item.GetProperty("reason").GetString() == "debt-window-historical");
        }

        using (var ambiguous = new AdoptionWorkspace())
        {
            SetMode(ambiguous.Context, TeamMode.SoloConductor, cutoff, Team);
            SetMode(ambiguous.Context, TeamMode.SoloConductor, cutoff, "intent-cli-reviewers");
            AutomationStalledWorkCommand.UtcNowFactory = () => cutoff.AddDays(2);
            using var writer = new StringWriter();
            var exitCode = AutomationStalledWorkCommand.Execute(ambiguous.Context,
                ["--domain", Domain, "--repo", Repo, "--stale-minutes", "0", "--format", "json"], writer);
            Assert.Equal(1, exitCode);
            Assert.Contains("team-mode-ambiguous", writer.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void V09_TeamProvenanceKeepsForeignMissingAndConflictingRowsVisible()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        var units = new[] { "G858-team-match", "G858-team-foreign", "G858-team-missing", "G858-team-conflict" };
        foreach (var unit in units)
        {
            workspace.WriteDebtPacket(unit);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2810 + Array.IndexOf(units, unit));
        }
        workspace.WriteClaim("G858-team-match", Team, cutoff.AddDays(-2));
        workspace.WriteClaim("G858-team-foreign", "foreign-team", cutoff.AddDays(-2));
        workspace.AppendEvent(IssueEvent("G858-team-missing", cutoff.AddDays(-2), "J-Tech-Japan/intent-system#312",
            "https://github.com/J-Tech-Japan/intent-system/issues/312"));
        workspace.WriteClaim("G858-team-conflict", Team, cutoff.AddDays(-3));
        workspace.WriteClaimHistory("G858-team-conflict", Team, "foreign-team", cutoff.AddDays(-3), cutoff.AddDays(-1));
        SetMode(workspace.Context, TeamMode.SoloConductor, cutoff, Team);

        using var teamScoped = Report(workspace.Context, ["--team", Team], now: cutoff.AddDays(2));
        Assert.Contains(teamScoped.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-team-match"
            && item.GetProperty("reason").GetString() == "debt-window-historical");
        Assert.Contains(teamScoped.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-team-foreign"
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(teamScoped.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-team-foreign"
            && item.GetProperty("reason").GetString() == "debt-window-foreign-team");
        Assert.Contains(teamScoped.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-team-missing"
            && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        Assert.Contains(teamScoped.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-team-conflict"
            && item.GetProperty("reason").GetString() == "debt-window-start-unknown");

        using var explicitWindow = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
        Assert.Contains(explicitWindow.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-team-foreign"
            && item.GetProperty("reason").GetString() == "debt-window-historical");
        Assert.Equal("explicit-since", explicitWindow.RootElement.GetProperty("debt_window").GetProperty("policy").GetString());
    }

    [Fact]
    public void V11_MalformedEvidenceIsUnknownWhileMissingOptionalSourceDoesNotBlockValidStarts()
    {
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        using (var workspace = new AdoptionWorkspace())
        {
            workspace.WriteDebtPacket("G858-optional-source", includeSourceUnit: false);
            workspace.WriteDebtPacket("G858-malformed-claim-team");
            workspace.WriteDebtPacket("G858-future-start");
            workspace.AppendEvent(IssueEvent("G858-optional-source", cutoff.AddDays(-1), "J-Tech-Japan/intent-system#321",
                "https://github.com/J-Tech-Japan/intent-system/issues/321"));
            workspace.AppendEvent(IssueEvent("G858-malformed-claim-team", cutoff.AddDays(-1), "J-Tech-Japan/intent-system#322",
                "https://github.com/J-Tech-Japan/intent-system/issues/322"));
            workspace.AppendEvent(IssueEvent("G858-future-start", cutoff.AddDays(2), "J-Tech-Japan/intent-system#323",
                "https://github.com/J-Tech-Japan/intent-system/issues/323"));
            workspace.WriteClaimWithMalformedTeam("G858-malformed-claim-team", cutoff.AddDays(-2));
            foreach (var unit in new[] { "G858-optional-source", "G858-malformed-claim-team", "G858-future-start" })
            {
                workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2820 + Array.IndexOf(new[] { "G858-optional-source", "G858-malformed-claim-team", "G858-future-start" }, unit));
            }
            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(1));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-optional-source"
                && item.GetProperty("reason").GetString() == "debt-window-historical");
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-malformed-claim-team"
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-future-start"
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }

        using (var workspace = new AdoptionWorkspace())
        {
            workspace.WriteDebtPacket("G858-claims-root-file");
            workspace.AppendEvent(IssueEvent("G858-claims-root-file", cutoff.AddDays(-1), "J-Tech-Japan/intent-system#324",
                "https://github.com/J-Tech-Japan/intent-system/issues/324"));
            workspace.WriteCloseoutOnly("G858-claims-root-file", cutoff.AddDays(1), 2824);
            workspace.WriteClaimsRootAsFile();
            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-claims-root-file"
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }

        if (!OperatingSystem.IsWindows())
        {
            using var workspace = new AdoptionWorkspace();
            workspace.WriteDebtPacket("G858-broken-history-link");
            workspace.WriteClaim("G858-broken-history-link", Team, cutoff.AddDays(-2));
            workspace.WriteCloseoutOnly("G858-broken-history-link", cutoff.AddDays(1), 2825);
            workspace.WriteBrokenHistoryBucket("G858-broken-history-link");
            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == "G858-broken-history-link"
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }
    }

    [Fact]
    public void V11_NullUnitAndMalformedClaimEvidenceRetainCloseoutDebt()
    {
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        using (var workspace = new AdoptionWorkspace())
        {
            const string unit = "G858-null-run-unit";
            workspace.WriteDebtPacket(unit);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2830);
            workspace.AppendRawRunLogLine($"{{\"ts\":\"{cutoff:O}\",\"execution_unit\":null,\"event\":\"issue-created\",\"by\":\"fixture\"}}");

            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
            Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetString()!.Contains("supported event with an unsafe or missing execution-unit", StringComparison.Ordinal));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }

        using (var workspace = new AdoptionWorkspace())
        {
            const string unit = "G858-malformed-claim-json";
            workspace.WriteDebtPacket(unit);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2831);
            workspace.WriteMalformedClaimJson(unit);

            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
            Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetString()!.Contains("claim index is incomplete", StringComparison.Ordinal));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
        }

        using (var workspace = new AdoptionWorkspace())
        {
            const string unit = "G858-invalid-claim-timestamp";
            workspace.WriteDebtPacket(unit);
            workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2832);
            workspace.WriteClaimWithRawTimestamp(unit, Team, "not-a-timestamp");

            using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("reason").GetString() == "debt-window-start-unknown"
                && item.GetProperty("detail").GetString()!.Contains("claim-start-timestamp-invalid", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void V11_RunIndexFailureIsVisibleWithoutCandidatesAndMalformedRunLogCannotAllClear()
    {
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        using (var empty = new AdoptionWorkspace())
        {
            empty.MakeRunLogDirectory();
            using var result = Report(empty.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(1));
            Assert.Equal(0, result.RootElement.GetProperty("items").GetArrayLength());
            Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetString()!.Contains("run-log index is incomplete", StringComparison.Ordinal)
                && warning.GetString()!.Contains(empty.RunLogPath, StringComparison.Ordinal));
        }

        using (var malformed = new AdoptionWorkspace())
        {
            const string unit = "G858-malformed-run-line";
            malformed.WriteDebtPacket(unit);
            malformed.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2833);
            malformed.AppendRawRunLogLine("{ this line is not JSON }");
            using var result = Report(malformed.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2));
            Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
                warning.GetString()!.Contains("run-log index is incomplete", StringComparison.Ordinal));
            Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
                item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonKnowledgeMetadataUnreadable);
        }
    }

    [Fact]
    public async Task V11_FifoClaimEvidenceIsRefusedWithoutBlockingAndLeavesDebtVisible()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        const string unit = "G858-fifo-claim";
        workspace.WriteDebtPacket(unit);
        workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2834);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ClaimPath(unit))!);
        Assert.Equal(0, MkFifo(workspace.ClaimPath(unit), 0x180));
        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(workspace.ClaimPath(unit)));
        Assert.False(CrossRuntimeReviewFileMode.TryReadRegularFileBytes(workspace.ClaimPath(unit), out _, out _, out _));

        var report = Task.Run(() => Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2)));
        var completed = await Task.WhenAny(report, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(report, completed);
        using var result = await report;
        Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains(workspace.ClaimPath(unit), StringComparison.Ordinal));
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("reason").GetString() == "debt-window-start-unknown");
    }

    [Fact]
    public async Task V11_FifoLegacyRunLogIsRefusedByBacklogReadersWithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);
        const string unit = "G858-fifo-run-log";
        workspace.WriteDebtPacket(unit);
        workspace.WriteBlockedQueue(unit);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.RunLogPath)!);
        Assert.Equal(0, MkFifo(workspace.RunLogPath, 0x180));
        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(workspace.RunLogPath));

        var report = Task.Run(() => Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(2)));
        var completed = await Task.WhenAny(report, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(report, completed);
        using var result = await report;
        Assert.Contains(result.RootElement.GetProperty("warnings").EnumerateArray(), warning =>
            warning.GetString()!.Contains("run-log index is incomplete", StringComparison.Ordinal)
            && warning.GetString()!.Contains(workspace.RunLogPath, StringComparison.Ordinal));
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBlockedParked
            && item.GetProperty("reason").GetString() == AutomationStalledWorkCommand.ReasonActivityDataUnusable);
    }

    [Fact]
    public void V11_FutureSoloTransitionIsUnavailableAndPreAugustDebtStaysVisible()
    {
        using var workspace = new AdoptionWorkspace();
        const string unit = "G858-future-solo-transition";
        var preAugustCloseout = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var reportNow = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        workspace.WriteDebtPacket(unit);
        workspace.WriteCloseoutOnly(unit, preAugustCloseout, 2835);
        SetMode(workspace.Context, TeamMode.SoloConductor, reportNow.AddDays(1));

        using var unavailable = Report(workspace.Context, now: reportNow);
        Assert.Equal("solo-adoption-transition-in-future",
            unavailable.RootElement.GetProperty("debt_window").GetProperty("unavailable_reason").GetString());
        Assert.True(unavailable.RootElement.GetProperty("stalled").GetBoolean());
        Assert.Contains(unavailable.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.Contains(unavailable.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("reason").GetString() == "debt-window-unavailable");

        using var deliberateLaneCutoffs = Report(workspace.Context,
            ["--knowledge-writeback-since", AutomationStalledWorkCommand.KnowledgeWriteBackActivationUtc.ToString("O"),
             "--guide-reachability-since", AutomationStalledWorkCommand.GuideReachabilityActivationUtc.ToString("O")],
            now: reportNow);
        Assert.DoesNotContain(deliberateLaneCutoffs.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == unit
            && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                or AutomationStalledWorkCommand.KindGuideReachabilityPending);
        Assert.Equal("solo-adoption-transition-in-future",
            deliberateLaneCutoffs.RootElement.GetProperty("debt_window").GetProperty("unavailable_reason").GetString());
    }

    [Fact]
    public void V11_InvalidRecordedModeStillReturnsTheExistingCommandFailure()
    {
        using var workspace = new AdoptionWorkspace();
        var path = TeamModeStore.ResolvePath(workspace.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schema_version\":\"1\",\"entries\":[{\"domain\":\"intent-cli\",\"team\":\"intent-cli-dev\",\"mode\":\"solo-conductor\",\"updated_at\":\"2026-08-13T00:00:00Z\",\"transitions\":[]}]}");

        using var writer = new StringWriter();
        var exitCode = AutomationStalledWorkCommand.Execute(workspace.Context,
            ["--domain", Domain, "--repo", Repo, "--format", "json"], writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("team mode state", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void V14_ExplicitWindowLeavesEveryUnrelatedLivePopulationUnchanged()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = Now.AddDays(-1);
        const string ciUnit = "G858-v14-ci";
        const string reviewUnit = "G858-v14-review";
        const string repairUnit = "G858-v14-repair";
        const string claimUnit = "G858-v14-claim";
        foreach (var unit in new[] { ciUnit, reviewUnit, repairUnit, claimUnit, "G858-v14-old-debt" })
        {
            workspace.WriteDebtPacket(unit);
        }
        workspace.WriteCloseoutOnly("G858-v14-old-debt", new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), 2840);
        workspace.WriteCloseoutOnly("G858-v14-old-debt", new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), 2841);
        workspace.WriteClaim(claimUnit, Team, Now.AddDays(-10));
        workspace.AppendEvent(new RunEvent
        {
            Ts = Now.AddMinutes(-46),
            ExecutionUnit = "G858-v14-last-activity",
            Event = "run-started",
            By = "fixture",
        });

        var attentionOpened = Now.AddDays(-2);
        OperatorAttentionStore.Write(workspace.Root, OperatorAttentionStore.BuildUpdated(null,
        [
            new OperatorAttentionRecord
            {
                RecordId = "G858-v14-operator-attention",
                Domain = Domain,
                Team = Team,
                Owner = "operator",
                BlockingReference = "issue:1876",
                ActionNeeded = "Review the independent live finding.",
                EstablishingEvidence = "V14 pre-adoption live-lane fixture",
                Status = "open",
                OpenedAt = attentionOpened,
                ResolutionEvidence = null,
                SupersedesRecordId = null,
                Transitions =
                [
                    new OperatorAttentionTransition
                    {
                        FromStatus = null,
                        ToStatus = "open",
                        TransitionedAt = attentionOpened,
                        Evidence = "V14 pre-adoption live-lane fixture",
                    },
                ],
            },
        ], Now));
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.Root, new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G858-v14-delegation",
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            RecipientIdentity = "implementation-seat",
            ExpectedArtifact = "https://github.com/J-Tech-Japan/intent-system/issues/1876",
            ExpectedArtifacts = ["https://github.com/J-Tech-Japan/intent-system/issues/1876"],
            Objective = "Keep the unrelated delegation lane visible.",
            Inputs = ["https://github.com/J-Tech-Japan/intent-system/issues/1876"],
            ResultNonce = "g858-v14-delegation",
            DispatchedAt = Now.AddDays(-1),
        }).Written);

        var issues = new[]
        {
            WindowIssue(2842, ciUnit, Now.AddDays(-3)),
            WindowIssue(2843, reviewUnit, Now.AddDays(-3)),
            WindowIssue(2844, repairUnit, Now.AddDays(-3)),
        };
        var prs = new[]
        {
            WindowPr(2845, ciUnit, 2842, Now.AddHours(-2),
                head: new string('c', 40), statuses: [new GitHubAutomationStatusCheckCandidate { TypeName = "CheckRun", Status = "IN_PROGRESS" }]),
            WindowPr(2846, reviewUnit, 2843, Now.AddHours(-2)),
            WindowPr(2847, repairUnit, 2844, Now.AddHours(-2), "intent-pr-request-update"),
        };
        AutomationStalledWorkCommand.CandidateListerFactory = () => new WindowFixtureLister(issues, prs);
        SetMode(workspace.Context, TeamMode.Delivery, Now.AddDays(-20), Team);

        using var baseline = Report(workspace.Context, ["--team", Team], now: Now, staleMinutes: 0);
        using var activeWindow = Report(workspace.Context,
            ["--team", Team, "--since", cutoff.ToString("O")], now: Now, staleMinutes: 0);

        var expectedKinds = new[]
        {
            AutomationStalledWorkCommand.KindCiPending,
            AutomationStalledWorkCommand.KindClaimStale,
            AutomationStalledWorkCommand.KindOperatorAttentionPending,
            AutomationStalledWorkCommand.KindPrCreatedNotReviewing,
            AutomationStalledWorkCommand.KindRepairPending,
            AutomationStalledWorkCommand.KindPendingDelegationOpen,
        };
        foreach (var kind in expectedKinds)
        {
            Assert.Contains(baseline.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("kind").GetString() == kind);
            Assert.Contains(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("kind").GetString() == kind);
        }

        Assert.Equal(NonDebtPopulation(baseline.RootElement), NonDebtPopulation(activeWindow.RootElement));
        Assert.Equal("explicit-since", activeWindow.RootElement.GetProperty("debt_window").GetProperty("policy").GetString());
        Assert.Contains(baseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-v14-old-debt"
            && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
        Assert.DoesNotContain(activeWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == "G858-v14-old-debt"
            && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                or AutomationStalledWorkCommand.KindGuideReachabilityPending);

        using var backlog = new AdoptionWorkspace();
        const string backlogUnit = "G858-v14-backlog";
        backlog.WriteDebtPacket(backlogUnit);
        backlog.WriteQueue([backlogUnit]);
        backlog.AppendEvent(new RunEvent
        {
            Ts = Now.AddMinutes(-46),
            ExecutionUnit = "G858-v14-backlog-last-activity",
            Event = "run-started",
            By = "fixture",
        });
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyLister();
        SetMode(backlog.Context, TeamMode.Delivery, Now.AddDays(-20), Team);
        using var backlogBaseline = Report(backlog.Context, ["--team", Team], now: Now);
        using var backlogWindow = Report(backlog.Context, ["--team", Team, "--since", cutoff.ToString("O")], now: Now);
        Assert.Contains(backlogBaseline.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle
            && item.GetProperty("execution_unit").GetString() == backlogUnit);
        Assert.Contains(backlogWindow.RootElement.GetProperty("items").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindBacklogReadyIdle
            && item.GetProperty("execution_unit").GetString() == backlogUnit);
        Assert.Equal(NonDebtPopulation(backlogBaseline.RootElement), NonDebtPopulation(backlogWindow.RootElement));
    }

    [Fact]
    public void V14_NoWindowKeepsAugustDebtFloorsForDefaultDeliveryAndAuthoringOnlyModes()
    {
        foreach (var mode in new string?[] { null, TeamMode.Delivery, TeamMode.AuthoringOnly })
        {
            using var workspace = new AdoptionWorkspace();
            const string beforeBothFloors = "G858-v14-before-floors";
            const string afterKnowledgeBeforeGuide = "G858-v14-knowledge-only-floor";
            workspace.WriteDebtPacket(beforeBothFloors);
            workspace.WriteDebtPacket(afterKnowledgeBeforeGuide);
            workspace.WriteCloseoutOnly(beforeBothFloors,
                AutomationStalledWorkCommand.KnowledgeWriteBackActivationUtc.AddTicks(-1), 2848);
            workspace.WriteCloseoutOnly(afterKnowledgeBeforeGuide,
                AutomationStalledWorkCommand.KnowledgeWriteBackActivationUtc.AddDays(4), 2849);
            if (mode is not null)
            {
                SetMode(workspace.Context, mode, Now.AddDays(-10), Team);
            }

            string[]? arguments = mode is null ? null : ["--team", Team];
            using var result = Report(workspace.Context, arguments, now: Now);
            Assert.False(result.RootElement.TryGetProperty("debt_window", out _));
            Assert.DoesNotContain(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == beforeBothFloors
                && item.GetProperty("kind").GetString() is AutomationStalledWorkCommand.KindKnowledgeWritebackPending
                    or AutomationStalledWorkCommand.KindGuideReachabilityPending);
            Assert.Contains(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == afterKnowledgeBeforeGuide
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindKnowledgeWritebackPending);
            Assert.DoesNotContain(result.RootElement.GetProperty("items").EnumerateArray(), item =>
                item.GetProperty("execution_unit").GetString() == afterKnowledgeBeforeGuide
                && item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindGuideReachabilityPending);
        }
    }

    private static GitHubAutomationIssueCandidate WindowIssue(int number, string unit, DateTimeOffset createdAt) => new()
    {
        Number = number,
        Title = $"{unit}: G858 V14 preserved issue",
        Url = $"https://github.com/{Repo}/issues/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        Labels = [new GitHubAutomationLabel { Name = "intent-pr-created" }],
    };

    private static GitHubAutomationPrCandidate WindowPr(
        int number,
        string unit,
        int issueNumber,
        DateTimeOffset createdAt,
        string? label = null,
        string head = "",
        IReadOnlyList<GitHubAutomationStatusCheckCandidate>? statuses = null) => new()
    {
        Number = number,
        Title = $"{unit}: G858 V14 preserved PR",
        Url = $"https://github.com/{Repo}/pull/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        HeadRefOid = head,
        StatusCheckRollup = statuses ?? [],
        Labels = label is null ? [] : [new GitHubAutomationLabel { Name = label }],
        ClosingIssuesReferences =
        [
            new GitHubPrClosingIssueReference
            {
                Number = issueNumber,
                Repository = new GitHubPrClosingIssueRepository
                {
                    Name = "intent-system",
                    Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                },
            },
        ],
    };

    private static string[] NonDebtPopulation(JsonElement report) => report.GetProperty("items").EnumerateArray()
        .Where(item => item.GetProperty("kind").GetString() is not AutomationStalledWorkCommand.KindKnowledgeWritebackPending
            and not AutomationStalledWorkCommand.KindKnowledgeWritebackRecordedUncommitted
            and not AutomationStalledWorkCommand.KindGuideReachabilityPending)
        .Select(item => string.Join("|",
            item.GetProperty("kind").GetString(),
            item.GetProperty("execution_unit").GetString(),
            item.TryGetProperty("issue", out var issue) && issue.ValueKind == JsonValueKind.Object ? issue.GetProperty("number").GetInt32() : 0,
            item.TryGetProperty("pr", out var pr) && pr.ValueKind == JsonValueKind.Object ? pr.GetProperty("number").GetInt32() : 0))
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private sealed class WindowFixtureLister(
        IReadOnlyList<GitHubAutomationIssueCandidate> issues,
        IReadOnlyList<GitHubAutomationPrCandidate> prs) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => prs;
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string pathname, int mode);

    [Fact]
    public void V13_OnlyLegacyRunLogContributesEvidenceWhenScopedRuntimeLogAlsoExists()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);
        const string unit = "G858-legacy-only";
        workspace.WriteDebtPacket(unit);
        workspace.WriteQueue([unit]);
        workspace.AppendEvent(IssueEvent(unit, cutoff.AddTicks(-1), "J-Tech-Japan/intent-system#331",
            "https://github.com/J-Tech-Japan/intent-system/issues/331"));
        workspace.WriteCloseoutOnly(unit, cutoff.AddDays(1), 2831);
        var scopedPath = workspace.WriteScopedRunLog(new RunEvent
        {
            Ts = cutoff.AddDays(1),
            ExecutionUnit = unit,
            Event = "issue-created",
            By = "foreign scoped fixture",
            LinkedIssue = "J-Tech-Japan/intent-system#331",
            Reason = "https://github.com/J-Tech-Japan/intent-system/issues/331",
        });
        var scopedBytes = File.ReadAllBytes(scopedPath);
        using var result = Report(workspace.Context, ["--since", cutoff.ToString("O")], now: cutoff.AddDays(3));
        var exclusions = result.RootElement.GetProperty("excluded").EnumerateArray()
            .Where(item => item.GetProperty("execution_unit").GetString() == unit
                && item.GetProperty("reason").GetString() == "debt-window-historical")
            .ToArray();
        Assert.Equal(2, exclusions.Length);
        Assert.All(exclusions, exclusion =>
        {
            Assert.Equal("issue-created", exclusion.GetProperty("debt_window_evidence_kind").GetString());
            Assert.Equal(cutoff.AddTicks(-1), exclusion.GetProperty("debt_window_evidence_at").GetDateTimeOffset());
            Assert.Equal(workspace.RunLogPath, exclusion.GetProperty("debt_window_evidence_path").GetString());
        });
        Assert.EndsWith(".intent-cli/runs.jsonl", result.RootElement.GetProperty("debt_window")
            .GetProperty("legacy_run_log_path").GetString(), StringComparison.Ordinal);
        Assert.Equal(scopedBytes, File.ReadAllBytes(scopedPath));
    }
}

internal sealed class AdoptionWorkspace : IDisposable
{
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public AdoptionWorkspace()
    {
        Root = Directory.CreateTempSubdirectory("g858-adoption-").FullName;
        Context = new CliContext
        {
            RepoRoot = Root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
                Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
            },
        };
        Directory.CreateDirectory(Path.Combine(Root, "J-Tech-Japan", "intent-system"));
        Directory.CreateDirectory(Path.Combine(Root, "submodules", "intent-system"));
    }

    public string Root { get; }
    public CliContext Context { get; }
    public string RunLogPath => Context.GetRunLogPath();
    public string QueueStatePath => Context.GetQueueStatePath();
    public string ClaimPath(string unit) => Path.Combine(Root, ClaimCommand.ClaimPath($"execution-unit:{unit}"));
    public string ClaimHistoryPath(string unit, string filename) => Path.Combine(Root, ".intent-cli", "claims", "history",
        Path.GetFileNameWithoutExtension(ClaimCommand.ClaimPath($"execution-unit:{unit}")), filename);

    public void WriteDebtPacket(
        string unit,
        bool knowledgeRequired = true,
        bool guideRoute = true,
        string? assertedUnit = null,
        bool includeRootRepo = false,
        bool includeMetadataRepo = false,
        bool includeSourceUnit = true)
    {
        var directory = Path.Combine(Root, ".intent-cli", "issues", unit);
        Directory.CreateDirectory(directory);
        var canonicalUnit = assertedUnit ?? unit;
        var rootRepo = includeRootRepo ? "other-org/other-repo" : "J-Tech-Japan/intent-system";
        const string nestedRepo = "J-Tech-Japan/intent-system";
        var metadataRepo = includeMetadataRepo ? "metadata:\n  target_repo: other-org/other-repo\n" : string.Empty;
        var rootSource = includeSourceUnit ? $"source_execution_unit: {canonicalUnit}\n" : string.Empty;
        var nestedSource = includeSourceUnit ? $"  source_execution_unit: {canonicalUnit}\n" : string.Empty;
        var guide = guideRoute
            ? "  no_role_facing_surface: false\n  routes:\n    - guide_surface: docs/en/08-command-reference.md\n      role: builder\n      target_surface: stalled-work adoption window\n"
            : "  no_role_facing_surface: true\n  routes: []\n";
        var text = $"""
            execution_unit: {canonicalUnit}
            title: {unit} adoption test packet
            domain: intent-cli
            target_repo: {rootRepo}
            {rootSource}implementation_issue_packet:
              issue_title: {unit} adoption test packet
              issue_kind: task
            {nestedSource}  domain: intent-cli
              target_repo: {nestedRepo}
            implementation_issue:
              issue_title: {unit} adoption test packet
              target_repo: submodules/intent-system
              target_path: .
              target_part: adoption report
              dependencies: []
            {metadataRepo}knowledge_updates:
              intent_tree:
                required: {knowledgeRequired.ToString().ToLowerInvariant()}
                target_paths: []
                summary: fixture
              adr:
                required: false
                target_paths: []
                summary: fixture
              diagram:
                required: false
                target_paths: []
                summary: fixture
              docs:
                required: false
                target_paths: []
                summary: fixture
            guide_reachability:
            {guide.TrimEnd('\n')}
            """;
        File.WriteAllText(Path.Combine(directory, "packet.yaml"), text);
        File.WriteAllText(Path.Combine(directory, "implementation.md"), $"# {unit}\n\nImplementation fixture.\n");
        File.WriteAllText(Path.Combine(directory, "review-context.md"), $"# Review {unit}\n");
        File.WriteAllText(Path.Combine(directory, "github-body.md"), BuildContractBody(unit));
    }

    public void WriteDraftPublishArtifact(string unit)
    {
        var path = Path.Combine(Root, IssuePublishArtifactPathResolver.Resolve(unit));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, IssuePublishArtifactYaml.Serialize(new IssuePublishArtifact
        {
            ExecutionUnit = unit,
            PublishStatus = "drafted",
            PacketPath = $".intent-cli/issues/{unit}/packet.yaml",
            IssueBodyPath = $".intent-cli/issues/{unit}/github-body.md",
            CreatedIssueNumber = null,
            CreatedIssueUrl = null,
            PublishedLabelName = null,
        }));
    }

    public void WriteQueue(IEnumerable<string> units)
    {
        var items = units.Select(unit => QueueItemFor(unit)).ToArray();
        WriteQueueState(items);
    }

    public void WriteBlockedQueue(string unit)
    {
        var item = QueueItemFor(unit) with
        {
            State = QueueItemState.Blocked,
            BlockedBy = ["fixture blocked state"],
        };
        WriteQueueState([item]);
    }

    public void AttachPrsAndSetReviewState(IReadOnlyList<string> units, IReadOnlyList<int> prs)
    {
        var current = ReadQueueState();
        var updated = current.Items.Select(item =>
        {
            var index = Array.IndexOf(units.ToArray(), item.ExecutionUnit);
            if (index < 0) return item;
            return item with
            {
                State = QueueItemState.Review,
                LinkedPr = $"https://github.com/J-Tech-Japan/intent-system/pull/{prs[index]}",
            };
        }).ToArray();
        WriteQueueState(updated);
    }

    public void WriteCloseoutOnly(string unit, DateTimeOffset at, int pr, string? linkedPr = null) => AppendEvent(new RunEvent
    {
        Ts = at,
        ExecutionUnit = unit,
        Event = "closeout-recorded",
        By = "fixture",
        Repo = "J-Tech-Japan/intent-system",
        Pr = pr,
        LinkedPr = linkedPr,
    });

    public void AppendEvent(RunEvent runEvent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RunLogPath)!);
        File.AppendAllText(RunLogPath, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
    }

    public void AppendRawRunLogLine(string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RunLogPath)!);
        File.AppendAllText(RunLogPath, line + Environment.NewLine);
    }

    public void MakeRunLogDirectory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RunLogPath)!);
        if (File.Exists(RunLogPath)) File.Delete(RunLogPath);
        Directory.CreateDirectory(RunLogPath);
    }

    public void WriteMalformedClaimJson(string unit)
    {
        var path = ClaimPath(unit);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ malformed claim json }");
    }

    public void WriteClaimWithRawTimestamp(string unit, string team, string claimedAt)
    {
        var path = ClaimPath(unit);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = $$"""
            {"schema_version":"1","scope":"execution-unit:{{unit}}","actor":"fixture","team":"{{team}}","claimed_at":"{{claimedAt}}","base_commit":"{{new string('a', 40)}}"}
            """;
        File.WriteAllText(path, json);
    }

    public void WriteClaim(string unit, string team, DateTimeOffset claimedAt)
    {
        var scope = $"execution-unit:{unit}";
        var path = Path.Combine(Root, ClaimCommand.ClaimPath(scope));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new ClaimRecord("1", scope, "fixture", team, claimedAt, new string('a', 40)), SnakeCase));
    }

    public void WriteClaimHistory(
        string unit,
        string takeoverTeam,
        string displacedTeam,
        DateTimeOffset displacedClaimedAt,
        DateTimeOffset recordedAt)
    {
        var scope = $"execution-unit:{unit}";
        var bucket = Path.Combine(Root, ".intent-cli", "claims", "history",
            Path.GetFileNameWithoutExtension(ClaimCommand.ClaimPath(scope)));
        Directory.CreateDirectory(bucket);
        var record = new ClaimHistoryRecord("1", "takeover", scope, "operator", takeoverTeam,
            recordedAt, "operator reassigned", "previous-holder", displacedTeam,
            displacedClaimedAt, new string('b', 40));
        File.WriteAllText(Path.Combine(bucket, $"{recordedAt:yyyyMMddHHmmssfffffff}.json"), JsonSerializer.Serialize(record, SnakeCase));
    }

    public void WriteClaimWithMalformedTeam(string unit, DateTimeOffset claimedAt)
    {
        var scope = $"execution-unit:{unit}";
        var path = Path.Combine(Root, ClaimCommand.ClaimPath(scope));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var record = new
        {
            schema_version = "1",
            scope,
            actor = "fixture",
            team = new[] { "intent-cli-dev" },
            claimed_at = claimedAt,
            base_commit = new string('a', 40),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(record, SnakeCase));
    }

    public void WriteClaimsRootAsFile()
    {
        var cliDirectory = Path.Combine(Root, ".intent-cli");
        Directory.CreateDirectory(cliDirectory);
        File.WriteAllText(Path.Combine(cliDirectory, "claims"), "claims evidence path is the wrong type\n");
    }

    public void WriteBrokenHistoryBucket(string unit)
    {
        var scope = $"execution-unit:{unit}";
        var historyRoot = Path.Combine(Root, ".intent-cli", "claims", "history");
        Directory.CreateDirectory(historyRoot);
        var bucket = Path.Combine(historyRoot, Path.GetFileNameWithoutExtension(ClaimCommand.ClaimPath(scope)));
        Directory.CreateSymbolicLink(bucket, Path.Combine(Root, "missing-history-target"));
    }

    public string WriteScopedRunLog(RunEvent runEvent)
    {
        var path = Path.Combine(Root, ".intent-cli", "runtime", "intent-cli", "J-Tech-Japan__intent-system", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RunLogSerializer.SerializeLine(runEvent) + Environment.NewLine);
        return path;
    }

    public void CopyClaimEvidence(string unit, string? activePath, string? historyPath)
    {
        if (activePath is not null)
        {
            var target = ClaimPath(unit);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(activePath, target, overwrite: true);
        }
        if (historyPath is not null)
        {
            var target = ClaimHistoryPath(unit, Path.GetFileName(historyPath));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(historyPath, target, overwrite: true);
        }
    }

    public void InitializeGit()
    {
        Git("init", "--quiet", "--initial-branch=main");
        Git("config", "user.name", "G858 test");
        Git("config", "user.email", "g858@example.invalid");
        Git("add", ".intent-cli");
        Git("commit", "--quiet", "-m", "fixture baseline");
    }

    public string Git(params string[] arguments) => RunProcess(Root, "git", arguments);

    public void CommitPath(string relativePath)
    {
        Git("add", relativePath);
        Git("commit", "--quiet", "-m", "record committed writeback fixture");
    }

    public IReadOnlyDictionary<string, string> SnapshotFiles() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(Root, path).Split(Path.DirectorySeparatorChar).Contains(".git", StringComparer.Ordinal))
        .ToDictionary(
            path => Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/'),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            StringComparer.Ordinal);

    private QueueState ReadQueueState() => QueueStateSerializer.Deserialize(File.ReadAllText(QueueStatePath));

    private void WriteQueueState(IEnumerable<QueueItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueueStatePath)!);
        File.WriteAllText(QueueStatePath, QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero),
            Items = items.ToArray(),
        }));
    }

    private static QueueItem QueueItemFor(string unit) => new()
    {
        ExecutionUnit = unit,
        Title = $"{unit} fixture",
        State = QueueItemState.Queued,
        Dependencies = [],
        BlockedBy = [],
        ClarificationReturnPath = string.Empty,
        PacketPaths = new PacketPaths
        {
            Implementation = $".intent-cli/issues/{unit}/implementation.md",
            ReviewContext = $".intent-cli/issues/{unit}/review-context.md",
            Yaml = $".intent-cli/issues/{unit}/packet.yaml",
        },
        WorkerRole = "child-impl",
        ReviewRole = "host-review",
        Priority = "normal",
    };

    private static string BuildContractBody(string unit) => $"""
        # {unit} adoption fixture

        ## Goal
        Exercise the adoption-window report with existing closeout duties.

        ## Why This Slice Exists Now
        The test verifies actual writer evidence.

        ## Current Observed State
        A local child packet exists.

        ## Accepted Baseline You May Assume
        Existing closeout obligations are already recorded.

        ## Target Repo / Path / Part
        J-Tech-Japan/intent-system / src / adoption report.

        ## In Scope
        - Read-only start evidence.

        ## Out Of Scope
        - Mutations.

        ## Acceptance Criteria
        - Evidence is reported.

        ## Verification
        Run the focused test.

        ## Related Links
        - https://github.com/J-Tech-Japan/intent-system/issues/1876

        ## Base Branch Policy
        Policy: direct-main. Expected PR base branch: main.
        """;

    private static string Indent(string value, int spaces) => string.Join(
        "\n",
        value.TrimEnd('\n').Split('\n').Select(line => new string(' ', spaces) + line)) + "\n";

    private static string RunProcess(string directory, string fileName, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(fileName) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

internal sealed class ClaimRepositories : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g858-claim-remote-").FullName;

    public ClaimRepositories()
    {
        Bare = Path.Combine(root, "origin.git");
        FirstClone = Path.Combine(root, "first");
        SecondClone = Path.Combine(root, "second");
        var seed = Path.Combine(root, "seed");
        Directory.CreateDirectory(Bare);
        Run(Bare, "git", ["init", "--bare", "--quiet"]);
        Directory.CreateDirectory(seed);
        Run(seed, "git", ["init", "--quiet", "--initial-branch=main"]);
        Run(seed, "git", ["config", "user.name", "G858 seed"]);
        Run(seed, "git", ["config", "user.email", "g858@example.invalid"]);
        File.WriteAllText(Path.Combine(seed, "README.md"), "seed\n");
        Run(seed, "git", ["add", "README.md"]);
        Run(seed, "git", ["commit", "--quiet", "-m", "seed"]);
        Run(seed, "git", ["remote", "add", "origin", Bare]);
        Run(seed, "git", ["push", "--quiet", "-u", "origin", "main"]);
        Run(Bare, "git", ["symbolic-ref", "HEAD", "refs/heads/main"]);
        Run(root, "git", ["clone", "--quiet", Bare, FirstClone]);
        Run(root, "git", ["clone", "--quiet", Bare, SecondClone]);
        foreach (var clone in new[] { FirstClone, SecondClone })
        {
            Run(clone, "git", ["config", "user.name", "G858 worker"]);
            Run(clone, "git", ["config", "user.email", "worker@example.invalid"]);
        }
    }

    public string Bare { get; }
    public string FirstClone { get; }
    public string SecondClone { get; }

    public string CloneForInspection()
    {
        var path = Path.Combine(root, $"inspect-{Guid.NewGuid():N}");
        Run(root, "git", ["clone", "--quiet", Bare, path]);
        return path;
    }

    private static string Run(string directory, string fileName, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(fileName) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}

internal sealed class EmptyLister : IGitHubAutomationCandidateLister
{
    public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => [];
    public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => [];
}

internal sealed class ExistingIssueNone : IGitHubExistingIssueChecker
{
    public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody) => new()
    {
        Classification = GitHubExistingIssueClassification.None,
    };
}

internal sealed class StubCreator(string url) : IIssueCreator
{
    public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath) => new(url);
}

internal sealed class FakePublisher(int issueNumber = 1) : IQueueDispatchPublisher
{
    public LinkedIssue CreateIssue(string targetRepo, string title, string body) => new()
    {
        Repo = targetRepo,
        Number = issueNumber,
        Url = $"https://github.com/{targetRepo}/issues/{issueNumber}",
    };

    public void AddLabel(string targetRepo, int issueNumber, string labelName) { }
}

internal sealed class FakeGitRunner : IGitRemoteCommandRunner
{
    public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments) => new()
    {
        ExitCode = 0,
        StdOut = "git@github.com:J-Tech-Japan/intent-system.git\n",
        StdErr = string.Empty,
    };
}

internal sealed class EmptyClosingIssuesFetcher : IPrClosingIssuesFetcher
{
    public IReadOnlyList<int> Fetch(string repo, int prNumber) => [];
}

internal sealed class RecordingFreshnessRunner : IGitRemoteCommandRunner
{
    public List<string> Calls { get; } = [];

    public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var call = string.Join(' ', arguments);
        Calls.Add(call);
        var result = call switch
        {
            "rev-parse HEAD" => new GitRemoteCommandResult { ExitCode = 0, StdOut = new string('b', 40) + "\n", StdErr = string.Empty },
            "ls-remote --symref origin HEAD" => new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = $"ref: refs/heads/main\tHEAD\n{new string('a', 40)}\tHEAD\n",
                StdErr = string.Empty,
            },
            _ => new GitRemoteCommandResult { ExitCode = 1, StdOut = string.Empty, StdErr = $"Unexpected call: {call}" },
        };
        if (call is "cat-file -e aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa^{commit}"
            or "merge-base --is-ancestor aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa HEAD")
        {
            result = new GitRemoteCommandResult { ExitCode = 0, StdOut = string.Empty, StdErr = string.Empty };
        }
        return result;
    }
}
