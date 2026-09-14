using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G828: supervision is opt-in. Only an explicit
/// <c>[supervision] opt_in_teams</c> declaration makes guidance recommend it
/// and bootstrap require it; leftover supervision files and cycles never do.
/// </summary>
public sealed class G828SupervisionOptInTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("g828-opt-in-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── config ─────────────────────────────────────────────────────────

    [Fact]
    public void Config_ParsesOptInTeams_AndAbsentKeyMeansNobody()
    {
        var declared = CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [supervision]
            opt_in_teams = ["intent-cli/intent-cli-dev", "sekiban-dcb-ts/sekiban-dcb-ts-orch", "intent-cli/intent-cli-dev"]
            """);
        Assert.Equal(["intent-cli/intent-cli-dev", "sekiban-dcb-ts/sekiban-dcb-ts-orch"], declared.Supervision.OptInTeams);
        Assert.True(declared.Supervision.IsOptedIn("intent-cli", "intent-cli-dev"));

        var absent = CliConfigLoader.Load("""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [supervision]
            artifact_root = ".intent-cli/supervision"
            """);
        Assert.Empty(absent.Supervision.OptInTeams);
        Assert.False(absent.Supervision.IsOptedIn("intent-cli", "intent-cli-dev"));
    }

    [Theory]
    [InlineData("intent-cli-dev")]
    [InlineData("intent-cli/")]
    [InlineData("/intent-cli-dev")]
    [InlineData("intent-cli/intent-cli-dev/extra")]
    [InlineData(" intent-cli/intent-cli-dev")]
    [InlineData("intent-cli/intent cli")]
    [InlineData("../intent-cli-dev")]
    [InlineData("")]
    [InlineData("  ")]
    public void Config_RejectsMalformedEntries_NamingKeyAndEntry(string entry)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load($"""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [supervision]
            opt_in_teams = ["{entry}"]
            """));
        Assert.Contains("supervision.opt_in_teams", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{entry}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("opt_in_teams = [1]", "entry '1'")]
    [InlineData("opt_in_teams = \"intent-cli/intent-cli-dev\"", "must be an array")]
    public void Config_RejectsNonStringShapes_NamingTheKey(string line, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CliConfigLoader.Load($"""
            default_domain = "intent-cli"
            artifact_root = ".intent-cli"

            [supervision]
            {line}
            """));
        Assert.Contains("supervision.opt_in_teams", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("installed-supervisor")]
    [InlineData("cycle")]
    public void GuideNext_NotOptedIn_OtherLeftoverSignalsDoNotOptIn(string signal)
    {
        var context = CreateContext(optInTeams: []);
        if (signal == "cycle")
        {
            var cyclePath = NotifySupervisionStore.ResolveCyclePath(SupervisionRoot(), Domain, Team);
            var now = DateTimeOffset.UtcNow;
            Assert.True(NotifySupervisionStore.RecordCycle(cyclePath, new NotifySupervisionCycle { CycleId = "g828-leftover", StartedAt = now, CompletedAt = now, IntervalSeconds = 300 }, write: true).Applied);
        }
        else
        {
            var record = NotifySupervisionStore.RecordInstalledSupervisor(
                SupervisionRoot(),
                new NotifySupervisionInstalledSupervisor
                {
                    Domain = Domain,
                    Team = Team,
                    Label = "intent-cli.supervise.intent-cli.intent-cli-dev",
                    ArtifactPath = Path.Combine(root, "artifact.plist"),
                    Writer = NotifySupervisionWriterIdentity.Current(),
                    StartupBoundSeconds = 120,
                    RecordedAt = DateTimeOffset.UtcNow,
                },
                write: true);
            Assert.Null(record.Error);
        }

        WriteFullRoster();
        var next = RunGuideNext(context);
        Assert.True(!next.GetProperty("supervision").TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null, error.ToString());
        Assert.False(next.GetProperty("supervision").GetProperty("opted_in").GetBoolean());
        Assert.False(next.GetProperty("supervision").GetProperty("setup_recommended").GetBoolean());

        var bootstrap = RunBootstrap(context);
        Assert.False(bootstrap.GetProperty("state").GetProperty("supervision_opted_in").GetBoolean());
        Assert.True(bootstrap.GetProperty("state").GetProperty("complete").GetBoolean());
        Assert.DoesNotContain("plus one supervision process", bootstrap.GetProperty("team_formula").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideNext_NotOptedIn_MarkdownCarriesNoUnconditionalSupervisionInstruction()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            CreateContext(optInTeams: []),
            ["--domain", Domain, "--team", Team, "--target-repo", "example/repo", "--format", "markdown"],
            writer));
        var text = writer.ToString();
        Assert.DoesNotContain("when no cycle is recorded", text, StringComparison.Ordinal);
        Assert.DoesNotContain("when its check is missing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("without a completed cycle/handoff", text, StringComparison.Ordinal);
        Assert.DoesNotContain("no recorded cycle is a setup gap, while", text, StringComparison.Ordinal);
        Assert.Contains("opt_in_teams", text, StringComparison.Ordinal);
    }

    [Fact]
    public void IsOptedIn_IsExactDomainAndTeamMatch()
    {
        var config = new SupervisionConfig { OptInTeams = ["other-domain/intent-cli-dev"] };
        Assert.False(config.IsOptedIn("intent-cli", "intent-cli-dev"));
        Assert.True(config.IsOptedIn("other-domain", "intent-cli-dev"));
        Assert.False(config.IsOptedIn("other-domain", "INTENT-CLI-DEV"));
    }

    // ── guide next ─────────────────────────────────────────────────────

    [Fact]
    public void GuideNext_NotOptedIn_StaysSilentEvenWithLeftoverSupervisionFiles()
    {
        // Leftover tracked state from a supervisor that ran once and stopped.
        var bound = NotifySupervisionStore.RecordBound(
            SupervisionRoot(),
            new NotifySupervisionBound { Domain = Domain, Team = Team, BoundSeconds = 900, RecordedAt = DateTimeOffset.UtcNow },
            write: true);
        Assert.Null(bound.Error);
        Assert.True(File.Exists(NotifySupervisionStore.ResolveBoundPath(SupervisionRoot(), Domain, Team)));

        var result = RunGuideNext(CreateContext(optInTeams: ["other-domain/intent-cli-dev"]));
        var supervision = result.GetProperty("supervision");

        // The leftover state must resolve, so silence comes from opt-in, not a read error.
        Assert.True(!supervision.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null, error.ToString());
        Assert.False(supervision.GetProperty("cycle_recorded").GetBoolean());
        Assert.False(supervision.GetProperty("setup_recommended").GetBoolean());
        Assert.False(supervision.GetProperty("opted_in").GetBoolean());
        Assert.True(!supervision.TryGetProperty("opt_in_source", out var source) || source.ValueKind == JsonValueKind.Null);
        Assert.DoesNotContain(
            result.GetProperty("decision_set").EnumerateArray(),
            action => action.GetProperty("action").GetString() == GuideNextCommand.ActionSupervisionSetup);
    }

    [Fact]
    public void GuideNext_OptedIn_RecommendsSetupFirst_WithSource()
    {
        var result = RunGuideNext(CreateContext(optInTeams: [$"{Domain}/{Team}"]));
        var supervision = result.GetProperty("supervision");

        Assert.True(supervision.GetProperty("setup_recommended").GetBoolean());
        Assert.True(supervision.GetProperty("opted_in").GetBoolean());
        Assert.Equal("config:supervision.opt_in_teams", supervision.GetProperty("opt_in_source").GetString());
        Assert.Equal(GuideNextCommand.ActionSupervisionSetup, result.GetProperty("decision_set")[0].GetProperty("action").GetString());
    }

    [Fact]
    public void GuideNext_NotOptedIn_MarkdownNamesTheOptInKey()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            CreateContext(optInTeams: []),
            ["--domain", Domain, "--team", Team, "--target-repo", "example/repo", "--format", "markdown"],
            writer));
        var text = writer.ToString();
        Assert.Contains("not opted in", text, StringComparison.Ordinal);
        Assert.Contains("opt_in_teams", text, StringComparison.Ordinal);
        Assert.DoesNotContain("supervision setup recommendation: **supervision-setup**", text, StringComparison.Ordinal);
    }

    // ── guide bootstrap ────────────────────────────────────────────────

    [Fact]
    public void Bootstrap_NotOptedIn_CompleteRosterIsComplete_AndStepFourEmitsNothing()
    {
        WriteFullRoster();

        var guide = RunBootstrap(CreateContext(optInTeams: []));
        var state = guide.GetProperty("state");

        Assert.Equal("complete-join-and-delegate", state.GetProperty("name").GetString());
        Assert.True(state.GetProperty("complete").GetBoolean());
        Assert.False(state.GetProperty("supervision_opted_in").GetBoolean());
        Assert.DoesNotContain(
            state.GetProperty("missing_facts").EnumerateArray(),
            fact => fact.GetString()!.Contains("supervision", StringComparison.OrdinalIgnoreCase));

        var stepFour = guide.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("id").GetString() == "emit-supervision-install");
        Assert.Empty(stepFour.GetProperty("emitted_commands").EnumerateArray());
        Assert.Contains("opt_in_teams", stepFour.GetProperty("instruction").GetString()!, StringComparison.Ordinal);

        var next = RunGuideNext(CreateContext(optInTeams: []));
        Assert.True(next.GetProperty("bootstrap").GetProperty("complete").GetBoolean());
        Assert.False(next.GetProperty("bootstrap").GetProperty("resume_recommended").GetBoolean());
        Assert.DoesNotContain(
            next.GetProperty("decision_set").EnumerateArray(),
            action => action.GetProperty("action").GetString() is "bootstrap-resume" or "supervision-setup");
    }

    [Fact]
    public void Bootstrap_OptedIn_StillRequiresACycle()
    {
        WriteFullRoster();

        var guide = RunBootstrap(CreateContext(optInTeams: [$"{Domain}/{Team}"]));
        var state = guide.GetProperty("state");

        Assert.Equal("topology-recorded-supervision-and-handoff-missing", state.GetProperty("name").GetString());
        Assert.False(state.GetProperty("complete").GetBoolean());
        Assert.True(state.GetProperty("supervision_opted_in").GetBoolean());
        var stepFour = guide.GetProperty("steps").EnumerateArray()
            .Single(step => step.GetProperty("id").GetString() == "emit-supervision-install");
        Assert.Contains(
            stepFour.GetProperty("emitted_commands").EnumerateArray(),
            command => command.GetString()!.Contains("notify supervise install", StringComparison.Ordinal));
    }

    [Fact]
    public void InitHostAndSupervisionSetupGuides_SaySupervisionIsOptIn()
    {
        Assert.StartsWith(SupervisionGuideText.OptInRule, SupervisionGuideText.InitHostSetup, StringComparison.Ordinal);

        using var writer = new StringWriter();
        Assert.Equal(0, GuideWorkflowTaskSupervisionSetupCommand.Execute(CreateContext(optInTeams: []), ["--format", "json"], writer));
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("opt_in_teams", document.RootElement.GetProperty("summary").GetString()!, StringComparison.Ordinal);
    }

    // ── fixtures ───────────────────────────────────────────────────────

    private string SupervisionRoot() => Path.Combine(root, ".intent-cli", "supervision");

    private CliContext CreateContext(IReadOnlyList<string> optInTeams) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision", OptInTeams = optInTeams },
        },
    };

    private static JsonElement RunGuideNext(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--target-repo", "example/repo", "--format", "json"],
            writer));
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }

    private JsonElement RunBootstrap(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideBootstrapCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--routing-root", root, "--format", "json"],
            writer));
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }

    private void WriteFullRoster()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        object Role(string pane, string cwd) => new { resident = "herdr", workspace_id = "w1", pane_id = pane, cwd, kind = "codex" };
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            schema_version = "1",
            domain = Domain,
            team = Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["design"] = Role("w1:p0", root),
                ["orchestration"] = Role("w1:p1", root),
                ["implementation"] = Role("w1:p2", "/work"),
                ["review"] = Role("w1:p3", "/review"),
            },
        }));
    }
}
