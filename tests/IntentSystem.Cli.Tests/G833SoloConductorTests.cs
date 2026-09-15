using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G833: <c>solo-conductor</c> is an explicit third team mode. One conductor
/// seat carries architect, orchestrator, and builder; a fresh independent
/// reviewer subagent carries reviewer. The mode is recorded, never inferred;
/// bootstrap and next recognize it without a seat roster, and every delivery
/// gate keeps its delivery behavior.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class G833SoloConductorTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string Repo = "J-Tech-Japan/intent-system";
    private readonly string root = Directory.CreateTempSubdirectory("g833-solo-").FullName;

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ── team mode ──────────────────────────────────────────────────────

    [Fact]
    public void TeamMode_RecordsSoloConductor_WithTransitions_AndShowValidateAcceptIt()
    {
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);
        Assert.Equal(0, RunSet(TeamMode.Delivery).ExitCode);
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);

        var (showExit, show) = Run(TeamModeCommand.ExecuteShow, ["--domain", Domain, "--team", Team, "--format", "json"]);
        Assert.Equal(0, showExit);
        Assert.Equal(TeamMode.SoloConductor, show.GetProperty("mode").GetString());
        var transitions = show.GetProperty("transitions").EnumerateArray()
            .Select(item => (item.GetProperty("from").GetString(), item.GetProperty("to").GetString()))
            .ToArray();
        Assert.Equal(
            [(TeamMode.Delivery, TeamMode.SoloConductor), (TeamMode.SoloConductor, TeamMode.Delivery), (TeamMode.Delivery, TeamMode.SoloConductor)],
            transitions);

        var (validateExit, validation) = Run(TeamModeCommand.ExecuteValidate, ["--domain", Domain, "--team", Team, "--format", "json"]);
        Assert.Equal(0, validateExit);
        Assert.True(validation.GetProperty("valid").GetBoolean());
        Assert.True(TeamModeStore.Resolve(root, Domain, Team).IsSoloConductor);
    }

    [Theory]
    [InlineData("solo")]
    [InlineData("Solo-Conductor")]
    [InlineData("solo_conductor")]
    public void TeamMode_RefusesNearMissValues_AndErrorNamesAllThreeModes(string mode)
    {
        using var writer = new StringWriter();
        var exitCode = TeamModeCommand.ExecuteSet(
            CreateContext(),
            ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
            writer);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("'delivery', 'authoring-only', or 'solo-conductor'", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(TeamModeStore.ResolvePath(root)));
    }

    [Fact]
    public void CapabilityMatrix_SoloIsNamedWithEveryClassActive()
    {
        var solo = TeamModeCapabilityMatrix.FromResolution(new TeamModeResolution
        {
            Mode = TeamMode.SoloConductor,
            Source = TeamModeSource.Recorded,
        });
        var delivery = TeamModeCapabilityMatrix.FromResolution(new TeamModeResolution
        {
            Mode = TeamMode.Delivery,
            Source = TeamModeSource.Recorded,
        });

        Assert.Equal(TeamMode.SoloConductor, solo.TeamMode);
        Assert.Equal(TeamModeCapabilityClasses.All, solo.ActiveClasses);
        Assert.Empty(solo.NotApplicableClasses);
        Assert.True(solo.IsApplicable(TeamModeCapabilityClasses.Delegation));
        Assert.True(solo.EmittedInJson);
        Assert.False(solo.IsAuthoringOnly);
        Assert.False(delivery.EmittedInJson);
    }

    // ── guide bootstrap ────────────────────────────────────────────────

    [Fact]
    public void Bootstrap_RecordedSolo_RendersSoloShapeWithoutRosterOrSupervision()
    {
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);

        var guide = RunBootstrapJson(withTeam: true);
        Assert.Equal("solo-conductor-team-bootstrap", guide.GetProperty("process").GetString());
        Assert.Equal(TeamMode.SoloConductor, guide.GetProperty("team_mode").GetString());
        Assert.Equal("solo-conductor", guide.GetProperty("flow").GetString());
        Assert.Equal(
            "one conductor seat (architect, orchestrator, builder) plus a fresh independent reviewer subagent per review; no seat roster",
            guide.GetProperty("team_formula").GetString());
        Assert.False(guide.TryGetProperty("model_resolution", out _));

        var state = guide.GetProperty("state");
        Assert.Equal("solo-conductor-complete", state.GetProperty("name").GetString());
        Assert.True(state.GetProperty("complete").GetBoolean());
        Assert.False(state.GetProperty("topology_recorded").GetBoolean());
        Assert.Empty(state.GetProperty("missing_facts").EnumerateArray());

        var steps = guide.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(
            ["accept-solo-conductor-model", "verify-repository-and-claims", "confirm-isolated-reviewer", "run-per-unit-loop"],
            steps.Select(step => step.GetProperty("id").GetString()));
        var stepText = string.Join("\n", steps.Select(step => step.GetProperty("instruction").GetString()));
        Assert.Contains("independent reviewer subagent", stepText, StringComparison.Ordinal);
        Assert.Contains("opt_in_teams", stepText, StringComparison.Ordinal);
        Assert.Contains("intent-cli never starts or manages an agent", stepText, StringComparison.Ordinal);
        Assert.Contains(
            steps[3].GetProperty("emitted_commands").EnumerateArray(),
            command => command.GetString() == "intent-cli guide solo-conductor --format markdown");
        Assert.DoesNotContain("session-layer topology record", guide.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("notify supervise install", guide.ToString(), StringComparison.Ordinal);
        Assert.Contains("guide solo-conductor", guide.GetProperty("final_handoff_statement").GetString()!, StringComparison.Ordinal);

        using var writer = new StringWriter();
        Assert.Equal(0, GuideBootstrapCommand.Execute(CreateContext(), BootstrapArgs(withTeam: true, "markdown"), writer));
        Assert.Contains("Solo-conductor mode:", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Authoring-only mode:", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_IsNeverInferred_FromTopology()
    {
        WriteFullRoster();
        var unrecorded = RunBootstrapJson(withTeam: true);
        Assert.NotEqual("solo-conductor", unrecorded.GetProperty("flow").GetString());
        Assert.NotEqual(TeamMode.SoloConductor, unrecorded.TryGetProperty("team_mode", out var mode) ? mode.GetString() : null);

        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);
        var recordedWithLeftoverRoster = RunBootstrapJson(withTeam: true);
        Assert.Equal("solo-conductor", recordedWithLeftoverRoster.GetProperty("flow").GetString());
        Assert.Equal("solo-conductor-complete", recordedWithLeftoverRoster.GetProperty("state").GetProperty("name").GetString());
    }

    [Fact]
    public void Bootstrap_WithoutTeam_IsUnchangedByASoloRecord()
    {
        var before = RenderBootstrap(withTeam: false);
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);
        var after = RenderBootstrap(withTeam: false);
        Assert.Equal(before, after);
        Assert.DoesNotContain("solo-conductor", after, StringComparison.Ordinal);
    }

    // ── guide next ─────────────────────────────────────────────────────

    [Fact]
    public void Next_RecordedSolo_BootstrapCompleteWithoutRoster_DecisionSetMatchesDelivery()
    {
        var delivery = RunNextJson(CreateContext());
        Assert.False(delivery.TryGetProperty("team_mode", out _));

        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);
        var solo = RunNextJson(CreateContext());

        Assert.Equal(TeamMode.SoloConductor, solo.GetProperty("team_mode").GetString());
        var bootstrap = solo.GetProperty("bootstrap");
        Assert.True(bootstrap.GetProperty("complete").GetBoolean());
        Assert.False(bootstrap.GetProperty("resume_recommended").GetBoolean());
        Assert.Equal(Actions(delivery), Actions(solo));
        Assert.DoesNotContain(GuideNextCommand.ActionBootstrapResume, Actions(solo));

        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(CreateContext(), NextArgs("markdown"), writer));
        Assert.Contains("solo-conductor bootstrap: complete", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("topology recorded: no", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Next_OptedInSolo_StillRecommendsSupervisionSetup_PerG828()
    {
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);

        var result = RunNextJson(CreateContext(optInTeams: [$"{Domain}/{Team}"]));
        Assert.True(result.GetProperty("supervision").GetProperty("setup_recommended").GetBoolean());
        Assert.Equal(GuideNextCommand.ActionSupervisionSetup, result.GetProperty("decision_set")[0].GetProperty("action").GetString());

        var notOptedIn = RunNextJson(CreateContext());
        Assert.DoesNotContain(GuideNextCommand.ActionSupervisionSetup, Actions(notOptedIn));
    }

    // ── delivery gates keep delivery behavior ──────────────────────────

    [Fact]
    public void PublishFlowAuthorization_SoloUsesTheDeliveryPath()
    {
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);

        Assert.True(IssuePublishFlowCommand.TryBuildAuthorization(
            TeamModeStore.Resolve(root, Domain, Team),
            Team,
            actorRole: null,
            operatorAcceptance: null,
            handoffDestination: null,
            out var authorization,
            out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(TeamMode.Delivery, authorization.Mode);
        Assert.False(authorization.IsAuthoringOnly);
    }

    [Fact]
    public void SuperviseAndTopology_AreNotRefusedForSolo()
    {
        Assert.Equal(0, RunSet(TeamMode.SoloConductor).ExitCode);
        WriteFullRoster();

        using var topologyWriter = new StringWriter();
        CommandRouter.Execute(
            ["session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json"],
            CreateContext(),
            topologyWriter);
        Assert.NotEqual(string.Empty, topologyWriter.ToString());
        Assert.DoesNotContain("not-applicable-team-mode", topologyWriter.ToString(), StringComparison.Ordinal);

        using var installWriter = new StringWriter();
        NotifyCommand.ExecuteSupervise(CreateContext(),
            ["install", "--domain", Domain, "--team", Team, "--repo", Repo,
                "--owner-role", "orchestration", "--bound", "300", "--interval", "120", "--routing-root", root, "--format", "json"],
            installWriter);
        Assert.NotEqual(string.Empty, installWriter.ToString());
        Assert.DoesNotContain("not-applicable-team-mode", installWriter.ToString(), StringComparison.Ordinal);
    }

    // ── guide model ────────────────────────────────────────────────────

    [Fact]
    public void GuideModel_NamesThreeShapes_AndSoloIsBoundToTheNormalizer()
    {
        var model = GuideModelCommand.BuildModel().ExecutionOrchestrationModel;
        Assert.Contains("three shapes", model.Summary, StringComparison.Ordinal);
        Assert.Contains("PRIMARY", model.Summary, StringComparison.Ordinal);
        Assert.Contains("four-thread", model.Summary, StringComparison.Ordinal);
        Assert.Contains("solo-conductor", model.Summary, StringComparison.Ordinal);

        var solo = Assert.Single(model.ThreadModels, item => item.Name == "solo-conductor");
        Assert.Equal(["architect", "orchestrator", "builder", "reviewer"], solo.Roles);
        foreach (var role in solo.Roles.Concat(solo.Aliases.Keys))
        {
            Assert.True(LogicalRoleNormalizer.TryNormalize(role, out _, out var error), error);
        }

        Assert.Contains("independent subagent", solo.Summary, StringComparison.Ordinal);
    }

    // ── guide solo-conductor ───────────────────────────────────────────

    [Fact]
    public void Route_RendersEveryContractSection_InMarkdownAndJson_FromABareDirectory()
    {
        var bare = Directory.CreateTempSubdirectory("g833-bare-").FullName;
        try
        {
            using var markdown = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", "markdown"], BareContext(bare), markdown));
            foreach (var heading in new[]
            {
                "## Model", "## Per-unit loop", "## Independence rules (blocking)", "## Pacing", "## Operator questions",
                "## Host discipline", "## Handoff durability", "## Limits", "## No-execution boundary",
            })
            {
                Assert.Contains(heading, markdown.ToString(), StringComparison.Ordinal);
            }

            using var json = new StringWriter();
            Assert.Equal(0, CommandRouter.Execute(["guide", "solo-conductor", "--format", "json"], BareContext(bare), json));
            using var document = JsonDocument.Parse(json.ToString());
            var guide = document.RootElement;
            Assert.Equal(GuideSoloConductorCommand.CommandName, guide.GetProperty("route").GetString());
            Assert.True(guide.GetProperty("metadata_free").GetBoolean());
            Assert.Equal(Enumerable.Range(1, 10), guide.GetProperty("loop").EnumerateArray().Select(step => step.GetProperty("number").GetInt32()));
            Assert.Equal(5, guide.GetProperty("independence_rules").GetArrayLength());
            Assert.All(guide.GetProperty("independence_rules").EnumerateArray(), rule => Assert.StartsWith("Blocking:", rule.GetString(), StringComparison.Ordinal));
            Assert.Empty(Directory.EnumerateFileSystemEntries(bare));

            // The route is in the metadata-free allow-list, so a bare child cwd reaches it.
            var oneshot = typeof(CommandRouter).Assembly.GetType("IntentSystem.Cli.Program")!
                .GetMethod("IsGuideOneshotCommand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Assert.True((bool)oneshot.Invoke(null, [new[] { "guide", "solo-conductor", "--format", "json" }])!);
        }
        finally
        {
            Directory.Delete(bare, recursive: true);
        }
    }

    [Fact]
    public void Route_LabelsEveryCommand_AndEmittedIntentCliCommandsExistInSource()
    {
        var guide = GuideSoloConductorCommand.BuildGuide();
        var commands = guide.Loop.SelectMany(step => step.Commands).ToArray();
        Assert.All(commands, command =>
        {
            Assert.Contains(command.Tool, new[] { GuideSoloConductorCommand.ToolIntentCli, GuideSoloConductorCommand.ToolGh, GuideSoloConductorCommand.ToolGit });
            Assert.StartsWith(command.Tool + " ", command.Command, StringComparison.Ordinal);
        });

        var sourceRoot = Path.Combine(RepoVersionPolicySource.RepoRoot(), "src", "IntentSystem.Cli");
        var source = string.Join("\n", Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("GuideSoloConductorCommand.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        var router = File.ReadAllText(Path.Combine(sourceRoot, "Commands", "CommandRouter.cs"));

        var intentCliCommands = commands.Where(command => command.Tool == GuideSoloConductorCommand.ToolIntentCli).ToArray();
        Assert.True(intentCliCommands.Length >= 15);
        foreach (var command in intentCliCommands)
        {
            var tokens = command.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            foreach (var word in tokens.TakeWhile(token => !token.StartsWith('-') && !token.StartsWith('<')))
            {
                Assert.True(router.Contains($"[\"{word}\"]", StringComparison.Ordinal), $"'{word}' in '{command.Command}' is not a routed command");
            }

            foreach (var flag in tokens.Where(token => token.StartsWith("--", StringComparison.Ordinal)))
            {
                Assert.True(source.Contains($"\"{flag}\"", StringComparison.Ordinal), $"'{flag}' in '{command.Command}' does not exist in CLI source");
            }
        }
    }

    [Fact]
    public void Route_NeverClaimsIntentCliStartsOrManagesAnAgent()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideSoloConductorCommand.Execute(CreateContext(), ["--format", "json"], writer));
        var text = writer.ToString();
        var affirmative = new Regex(@"intent-cli (starts|launches|manages|spawns|runs) (an? |the )?(agent|subagent|reviewer)", RegexOptions.IgnoreCase);
        Assert.DoesNotMatch(affirmative, text);
        Assert.Contains("intent-cli does not start, launch, or manage any agent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteIsListedInCatalogHelpAndDocs_WithForwardCompatibilityNote()
    {
        Assert.Contains(GuideCommandsListCommand.Groups, group => group.Name == "guide solo-conductor");
        using var help = new StringWriter();
        Assert.Equal(0, GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], help));
        Assert.Contains("\"solo-conductor\"", help.ToString(), StringComparison.Ordinal);

        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var orchestration = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("solo-conductor", orchestration, StringComparison.Ordinal);
            Assert.Contains("G833", orchestration, StringComparison.Ordinal);

            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("| `guide solo-conductor` |", ledger, StringComparison.Ordinal);
            Assert.Contains("G833; `preview-through-1.x`", ledger, StringComparison.Ordinal);
            foreach (var document in new[] { orchestration, ledger })
            {
                Assert.Contains("team-mode.json", document, StringComparison.Ordinal);
            }
        }
    }

    // ── fixtures ───────────────────────────────────────────────────────

    private static string[] Actions(JsonElement next) =>
        next.GetProperty("decision_set").EnumerateArray().Select(action => action.GetProperty("action").GetString()!).ToArray();

    private (int ExitCode, JsonElement Result) Run(Func<CliContext, string[], TextWriter, int> command, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = command(CreateContext(), args, writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
    }

    private (int ExitCode, JsonElement Result) RunSet(string mode) =>
        Run(TeamModeCommand.ExecuteSet, ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"]);

    private string[] BootstrapArgs(bool withTeam, string format) => withTeam
        ? ["--domain", Domain, "--team", Team, "--target-repo", Repo, "--routing-root", root, "--format", format]
        : ["--domain", Domain, "--target-repo", Repo, "--routing-root", root, "--format", format];

    private string RenderBootstrap(bool withTeam)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideBootstrapCommand.Execute(CreateContext(), BootstrapArgs(withTeam, "json"), writer));
        return writer.ToString();
    }

    private JsonElement RunBootstrapJson(bool withTeam) =>
        JsonDocument.Parse(RenderBootstrap(withTeam)).RootElement.Clone();

    private static string[] NextArgs(string format) =>
        ["--domain", Domain, "--team", Team, "--target-repo", Repo, "--format", format];

    private static JsonElement RunNextJson(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(context, NextArgs("json"), writer));
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

    private static CliContext BareContext(string bare) => new()
    {
        RepoRoot = bare,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
        },
    };

    private CliContext CreateContext(IReadOnlyList<string>? optInTeams = null) => new()
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
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision", OptInTeams = optInTeams ?? [] },
        },
    };
}
