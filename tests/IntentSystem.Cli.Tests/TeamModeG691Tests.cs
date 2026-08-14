using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G691: team shape is durable and independent from transport. Delivery keeps
/// its exact existing rendering; authoring-only never reads or requires the
/// delivery topology/supervision records.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class TeamModeG691Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("team-mode-g691-").FullName;

    public void Dispose()
    {
        TeamModeCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TeamMode_DefaultsToDelivery_AndRecordsReversibleTransitions()
    {
        var (defaultExit, defaultShow) = Run(TeamModeCommand.ExecuteShow, ["--domain", Domain, "--team", Team, "--format", "json"]);
        Assert.Equal(0, defaultExit);
        Assert.Equal("delivery", defaultShow.GetProperty("mode").GetString());
        Assert.Equal("default", defaultShow.GetProperty("source").GetString());
        Assert.False(File.Exists(TeamModeStore.ResolvePath(root)));

        Assert.Equal(0, RunSet(TeamMode.AuthoringOnly, write: true).ExitCode);
        Assert.Equal(0, RunSet(TeamMode.Delivery, write: true).ExitCode);

        var (_, show) = Run(TeamModeCommand.ExecuteShow, ["--domain", Domain, "--team", Team, "--format", "json"]);
        Assert.Equal(TeamMode.Delivery, show.GetProperty("mode").GetString());
        Assert.Equal("recorded", show.GetProperty("source").GetString());
        var transitions = show.GetProperty("transitions").EnumerateArray().ToArray();
        Assert.Equal(2, transitions.Length);
        Assert.Equal(TeamMode.Delivery, transitions[0].GetProperty("from").GetString());
        Assert.Equal(TeamMode.AuthoringOnly, transitions[0].GetProperty("to").GetString());
        Assert.Equal(TeamMode.AuthoringOnly, transitions[1].GetProperty("from").GetString());
        Assert.Equal(TeamMode.Delivery, transitions[1].GetProperty("to").GetString());
    }

    [Fact]
    public void SetIsIdempotent_AndValidateReadsCommandProducedState()
    {
        var first = RunSet(TeamMode.AuthoringOnly, write: true);
        Assert.Equal(0, first.ExitCode);
        var bytes = File.ReadAllBytes(TeamModeStore.ResolvePath(root));

        var second = RunSet(TeamMode.AuthoringOnly, write: true);
        Assert.Equal(0, second.ExitCode);
        Assert.True(second.Result.GetProperty("already_recorded").GetBoolean());
        Assert.False(second.Result.GetProperty("applied").GetBoolean());
        Assert.Equal(bytes, File.ReadAllBytes(TeamModeStore.ResolvePath(root)));

        var (validateExit, validation) = Run(TeamModeCommand.ExecuteValidate, ["--domain", Domain, "--team", Team, "--format", "json"]);
        Assert.Equal(0, validateExit);
        Assert.True(validation.GetProperty("valid").GetBoolean());
        Assert.Equal(TeamMode.AuthoringOnly, validation.GetProperty("mode").GetString());
    }

    [Fact]
    public void MalformedTeamMode_FailsClosedRatherThanRevertingToDelivery()
    {
        var path = TeamModeStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schema_version\":\"1\",\"entries\":[{\"domain\":\"intent-cli\",\"team\":\"intent-cli-dev\",\"mode\":\"authoring-only\",\"updated_at\":\"2026-08-13T00:00:00Z\",\"transitions\":[]}]}");

        using var writer = new StringWriter();
        var exitCode = TeamModeCommand.ExecuteShow(
            CreateContext(), ["--domain", Domain, "--team", Team, "--format", "json"], writer);
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-unreadable", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeliveryMode_IsByteIdenticalWhetherAbsentOrExplicitlyRecorded()
    {
        var context = CreateContext();
        var beforeBootstrap = Execute(GuideBootstrapCommand.Execute, context,
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--routing-root", root, "--format", "json"]);
        var beforeNext = Execute(GuideNextCommand.Execute, context,
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"]);

        Assert.Equal(0, RunSet(TeamMode.Delivery, write: true).ExitCode);

        var afterBootstrap = Execute(GuideBootstrapCommand.Execute, context,
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--routing-root", root, "--format", "json"]);
        var afterNext = Execute(GuideNextCommand.Execute, context,
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"]);

        Assert.Equal(beforeBootstrap, afterBootstrap);
        Assert.Equal(beforeNext, afterNext);
    }

    [Fact]
    public void AuthoringOnlyBootstrap_UsesOnlyFrontDoorAndPublicationPrerequisites()
    {
        Assert.Equal(0, RunSet(TeamMode.AuthoringOnly, write: true).ExitCode);

        var output = Execute(GuideBootstrapCommand.Execute, CreateContext(),
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--routing-root", root, "--format", "json"]);
        using var document = JsonDocument.Parse(output);
        var guide = document.RootElement;
        Assert.Equal(TeamMode.AuthoringOnly, guide.GetProperty("team_mode").GetString());
        Assert.Equal("authoring-only", guide.GetProperty("flow").GetString());
        Assert.Equal("not-applicable-team-mode", guide.GetProperty("target_session_layer").GetString());
        Assert.False(guide.TryGetProperty("model_resolution", out _));

        var steps = guide.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(new[] { "accept-authoring-front-door", "verify-repository-prerequisite", "author-packet", "publish-issue" },
            steps.Select(step => step.GetProperty("id").GetString()).ToArray());
        var stepText = string.Join("\n", steps.SelectMany(step => new[]
        {
            step.GetProperty("instruction").GetString()!,
            string.Join("\n", step.GetProperty("emitted_commands").EnumerateArray().Select(command => command.GetString())),
        })).ToLowerInvariant();
        foreach (var forbidden in new[] { "herdr", "workspace", "pane", "agent", "model", "supervis", "delegat" })
        {
            Assert.DoesNotContain(forbidden, stepText, StringComparison.Ordinal);
        }
        Assert.Contains("claim", stepText, StringComparison.Ordinal);
        Assert.Contains("publish", stepText, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringOnlyNext_OffersOnlyAuthoringActions_AndDoesNotReadDeliveryState()
    {
        Assert.Equal(0, RunSet(TeamMode.AuthoringOnly, write: true).ExitCode);
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, "not-json");

        var output = Execute(GuideNextCommand.Execute, CreateContext(),
            ["--domain", Domain, "--team", Team, "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"]);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement;
        Assert.Equal(TeamMode.AuthoringOnly, result.GetProperty("team_mode").GetString());
        Assert.True(result.GetProperty("supervision").GetProperty("not_applicable").GetBoolean());
        Assert.Contains("not-applicable-team-mode", result.GetProperty("supervision").GetProperty("error").GetString()!, StringComparison.Ordinal);
        Assert.True(result.GetProperty("bootstrap").GetProperty("not_applicable").GetBoolean());
        Assert.Contains("not-applicable-team-mode", result.GetProperty("bootstrap").GetProperty("error").GetString()!, StringComparison.Ordinal);

        var actions = result.GetProperty("decision_set").EnumerateArray()
            .Select(action => action.GetProperty("action").GetString())
            .ToArray();
        Assert.Equal(new[] { "shape-interview", "packet-authoring", "publish", "improve", "inspect", "idle" }, actions);
        Assert.DoesNotContain(actions, action => action is "supervision-setup" or "bootstrap-resume");
    }

    [Fact]
    public void AuthoringOnlySupervisionAndTopology_ReturnNamedNonzeroOutcomeWithoutWriting()
    {
        Assert.Equal(0, RunSet(TeamMode.AuthoringOnly, write: true).ExitCode);

        NotifyCommand.ProcessRunnerFactory = () => throw new InvalidOperationException("runner must not be constructed");
        using var superviseWriter = new StringWriter();
        var superviseExit = NotifyCommand.ExecuteSupervise(CreateContext(),
            ["--domain", Domain, "--team", Team, "--once", "--routing-root", root, "--format", "json"], superviseWriter);
        Assert.Equal(1, superviseExit);
        Assert.Contains("not-applicable-team-mode", superviseWriter.ToString(), StringComparison.Ordinal);

        using var installWriter = new StringWriter();
        var installExit = NotifyCommand.ExecuteSupervise(CreateContext(),
            ["install", "--domain", Domain, "--team", Team, "--repo", "J-Tech-Japan/intent-system",
                "--owner-role", "orchestration", "--bound", "300", "--interval", "120", "--routing-root", root, "--format", "json"], installWriter);
        Assert.Equal(1, installExit);
        Assert.Contains("not-applicable-team-mode", installWriter.ToString(), StringComparison.Ordinal);

        using var topologyWriter = new StringWriter();
        var topologyExit = CommandRouter.Execute(
            ["session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json"],
            CreateContext(), topologyWriter);
        Assert.Equal(1, topologyExit);
        Assert.Contains("not-applicable-team-mode", topologyWriter.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, Domain, Team)) &&
            File.ReadAllText(NotifyRoleTopologyStore.ResolvePath(root, Domain, Team)) != "not-json");
    }

    [Fact]
    public void RouterAndDocsExposeTeamMode()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(["team-mode", "show", "--domain", Domain, "--team", Team, "--format", "json"], CreateContext(), writer));
        Assert.Contains("\"mode\": \"delivery\"", writer.ToString(), StringComparison.Ordinal);

        Assert.Contains(GuideCommandsListCommand.Groups, group => group.Name == "team-mode");
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var path in new[]
        {
            Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"),
            Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"),
            Path.Combine(repoRoot, "docs", "en", "1.0-compatibility-ledger.md"),
            Path.Combine(repoRoot, "docs", "ja", "1.0-compatibility-ledger.md"),
        })
        {
            var document = File.ReadAllText(path);
            Assert.Contains("team-mode", document, StringComparison.Ordinal);
            Assert.Contains("authoring-only", document, StringComparison.Ordinal);
            Assert.Contains("G691", document, StringComparison.Ordinal);
        }
    }

    private (int ExitCode, JsonElement Result) Run(Func<CliContext, string[], TextWriter, int> command, string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = command(CreateContext(), args, writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
    }

    private (int ExitCode, JsonElement Result) RunSet(string mode, bool write)
    {
        return Run(TeamModeCommand.ExecuteSet,
            ["--domain", Domain, "--team", Team, "--mode", mode, write ? "--write" : "--dry-run", "--format", "json"]);
    }

    private static string Execute(Func<CliContext, string[], TextWriter, int> command, CliContext context, string[] args)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, command(context, args, writer));
        return writer.ToString();
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
