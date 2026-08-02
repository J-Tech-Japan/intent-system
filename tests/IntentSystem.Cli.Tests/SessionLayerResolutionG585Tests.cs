using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G585: omitting --team must never make a team-scoped session-layer record
/// disappear silently from either the record surface or a routing consumer.
/// </summary>
public sealed class SessionLayerResolutionG585Tests
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string OtherTeam = "other-team";
    private const string Repo = "J-Tech-Japan/intent-system";

    [Theory]
    [InlineData("no-team", null, true, SessionLayerMode.Agmsg, "default", true)]
    [InlineData("wrong-team", OtherTeam, true, SessionLayerMode.Agmsg, "default", false)]
    [InlineData("correct-team", Team, true, SessionLayerMode.HerdrOnly, "recorded", false)]
    [InlineData("no-record", null, false, SessionLayerMode.Agmsg, "default", false)]
    public void ShowAndOrchestratorGuide_AgreeAcrossArgumentMatrix_G585(
        string scenario,
        string? requestedTeam,
        bool createTeamRecord,
        string expectedMode,
        string expectedSource,
        bool expectTeamOmissionDisclosure)
    {
        using var workspace = new ResolutionWorkspace();
        if (createTeamRecord)
        {
            workspace.SetTeamMode(Team, SessionLayerMode.HerdrOnly);
        }

        var show = workspace.RunShow(requestedTeam, "json");
        var guide = workspace.RunGuide(requestedTeam, "json");
        Assert.Equal(0, show.ExitCode);
        Assert.Equal(0, guide.ExitCode);

        var showJson = JsonDocument.Parse(show.Output).RootElement;
        var guideJson = JsonDocument.Parse(guide.Output).RootElement;
        var routed = guideJson.GetProperty("session_layer");

        Assert.Equal(expectedMode, showJson.GetProperty("mode").GetString());
        Assert.Equal(expectedMode, routed.GetProperty("mode").GetString());
        Assert.Equal(expectedSource, showJson.GetProperty("source").GetString());
        Assert.Equal(expectedSource, routed.GetProperty("source").GetString());

        var showDiscloses = showJson.TryGetProperty("team_omission", out var showDisclosure);
        var guideDiscloses = routed.TryGetProperty("team_omission", out var guideDisclosure);
        Assert.Equal(expectTeamOmissionDisclosure, showDiscloses);
        Assert.Equal(expectTeamOmissionDisclosure, guideDiscloses);

        if (!expectTeamOmissionDisclosure)
        {
            return;
        }

        AssertDisclosure(
            showDisclosure,
            expectedMode,
            $"intent-cli session-layer show --domain {Domain} --team {Team}");
        AssertDisclosure(
            guideDisclosure,
            expectedMode,
            $"intent-cli guide orchestrator-thread --domain {Domain} --target-repo {Repo} --agent codex --team {Team}");
        Assert.Equal("no-team", scenario);
    }

    [Fact]
    public void NoTeamDisclosure_IsProminentAndActionableInEachMarkdownSurface_G585()
    {
        using var workspace = new ResolutionWorkspace();
        workspace.SetTeamMode(Team, SessionLayerMode.HerdrOnly);

        var show = workspace.RunShow(team: null, format: "markdown");
        var guide = workspace.RunGuide(team: null, format: "markdown");

        Assert.Equal(0, show.ExitCode);
        Assert.Equal(0, guide.ExitCode);
        Assert.Contains("**TEAM NOT SUPPLIED — ROUTING DISCLOSURE:**", show.Output, StringComparison.Ordinal);
        Assert.Contains("**TEAM NOT SUPPLIED — ROUTING DISCLOSURE:**", guide.Output, StringComparison.Ordinal);
        Assert.Contains("`--team` was not supplied", show.Output, StringComparison.Ordinal);
        Assert.Contains("`--team` was not supplied", guide.Output, StringComparison.Ordinal);
        Assert.Contains(SessionLayerMode.Describe(SessionLayerMode.Agmsg), show.Output, StringComparison.Ordinal);
        Assert.Contains(SessionLayerMode.Describe(SessionLayerMode.Agmsg), guide.Output, StringComparison.Ordinal);
        Assert.Contains(
            $"`intent-cli session-layer show --domain {Domain} --team {Team}`",
            show.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"`intent-cli guide orchestrator-thread --domain {Domain} --target-repo {Repo} --agent codex --team {Team}`",
            guide.Output,
            StringComparison.Ordinal);
    }

    private static void AssertDisclosure(JsonElement disclosure, string expectedMode, string expectedCommand)
    {
        Assert.Contains("`--team` was not supplied", disclosure.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(expectedMode, disclosure.GetProperty("mode_in_force").GetString());

        var correction = Assert.Single(disclosure.GetProperty("corrective_commands").EnumerateArray());
        Assert.Equal(Team, correction.GetProperty("team").GetString());
        Assert.Equal(expectedCommand, correction.GetProperty("command").GetString());
    }

    private sealed class ResolutionWorkspace : IDisposable
    {
        public ResolutionWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-layer-g585-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        private string RootPath { get; }

        private CliContext Context { get; }

        public void SetTeamMode(string team, string mode)
        {
            var result = Run([
                "session-layer", "set", "--domain", Domain, "--team", team,
                "--mode", mode, "--write", "--format", "json",
            ]);
            Assert.Equal(0, result.ExitCode);
        }

        public CommandResult RunShow(string? team, string format)
        {
            var args = new List<string> { "session-layer", "show", "--domain", Domain };
            if (team is not null)
            {
                args.AddRange(["--team", team]);
            }
            args.AddRange(["--format", format]);
            return Run(args.ToArray());
        }

        public CommandResult RunGuide(string? team, string format)
        {
            var args = new List<string>
            {
                "guide", "orchestrator-thread", "--domain", Domain,
                "--target-repo", Repo, "--agent", "codex",
            };
            if (team is not null)
            {
                args.AddRange(["--team", team]);
            }
            args.AddRange(["--format", format]);
            return Run(args.ToArray());
        }

        private CommandResult Run(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return new CommandResult(exitCode, writer.ToString());
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
