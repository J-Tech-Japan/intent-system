using System.Diagnostics;
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
    public void V05_ActualClaimWriterFieldsSupplyActiveAndDisplacedStartEvidence()
    {
        using var workspace = new AdoptionWorkspace();
        var cutoff = DateTimeOffset.UtcNow.AddDays(1);
        var reportNow = cutoff.AddDays(1);
        const string activeUnit = "G858-active-claim";
        const string displacedUnit = "G858-displaced-claim";
        workspace.WriteDebtPacket(activeUnit);
        workspace.WriteDebtPacket(displacedUnit);

        using var claims = new ClaimRepositories();
        var activeRequest = ClaimRequestFor(activeUnit, "alice", Team);
        Assert.Equal("acquired", ClaimCommand.RunTransaction(claims.FirstClone, activeRequest).Status);
        var displacedAcquire = ClaimRequestFor(displacedUnit, "alice", Team);
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
        var activeClaimPath = Path.Combine(inspection, ClaimCommand.ClaimPath(activeRequest.Scope));
        var historyPath = Path.Combine(inspection, takeover.HistoryPath!);
        using var activeJson = JsonDocument.Parse(File.ReadAllText(activeClaimPath));
        using var historyJson = JsonDocument.Parse(File.ReadAllText(historyPath));
        Assert.True(activeJson.RootElement.TryGetProperty("claimed_at", out _));
        Assert.True(historyJson.RootElement.TryGetProperty("displaced_claimed_at", out _));
        Assert.True(historyJson.RootElement.TryGetProperty("displaced_team", out _));
        Assert.True(historyJson.RootElement.TryGetProperty("recorded_at", out _));

        workspace.CopyClaimEvidence(activeUnit, activeClaimPath, null);
        workspace.CopyClaimEvidence(displacedUnit, null, historyPath);
        workspace.WriteCloseoutOnly(activeUnit, cutoff.AddDays(1), 2610);
        workspace.WriteCloseoutOnly(displacedUnit, cutoff.AddDays(1), 2611);
        var activeBytes = File.ReadAllBytes(workspace.ClaimPath(activeUnit));
        var historyBytes = File.ReadAllBytes(workspace.ClaimHistoryPath(displacedUnit, Path.GetFileName(historyPath)));
        SetMode(workspace.Context, TeamMode.SoloConductor, cutoff, Team);

        using var result = Report(workspace.Context, ["--team", Team], now: reportNow);
        Assert.Equal(activeBytes, File.ReadAllBytes(workspace.ClaimPath(activeUnit)));
        Assert.Equal(historyBytes, File.ReadAllBytes(workspace.ClaimHistoryPath(displacedUnit, Path.GetFileName(historyPath))));
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == activeUnit
            && item.GetProperty("debt_window_evidence_kind").GetString() == "active-claim");
        Assert.Contains(result.RootElement.GetProperty("excluded").EnumerateArray(), item =>
            item.GetProperty("execution_unit").GetString() == displacedUnit
            && item.GetProperty("debt_window_evidence_kind").GetString() == "claim-history");
    }

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
