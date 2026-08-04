using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerMigrationG602Tests : IDisposable
{
    private readonly Workspace workspace = new();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void RealSwitch_EmitsOrderedManualPlanWithoutChangingNamedArtifacts_AndNoOpEmitsNone_G602()
    {
        workspace.Record(SessionLayerMode.Agmsg);
        workspace.WriteHooks("""
            { "hooks": { "session-start": [{ "command": "agmsg watch.sh" }], "session-end": [{ "command": "agmsg leave.sh" }] } }
            """);
        var hooksBefore = File.ReadAllBytes(workspace.HooksPath);

        var switchResult = workspace.Set(SessionLayerMode.HerdrOnly);

        Assert.Equal(0, switchResult.ExitCode);
        Assert.True(switchResult.Result.GetProperty("applied").GetBoolean());
        var plan = switchResult.Result.GetProperty("migration_plan").EnumerateArray().ToArray();
        Assert.Equal(
            ["other-mode-session-hooks", "other-mode-inbox-watchers-monitors", "g601-visibility-marker"],
            plan.Select(item => item.GetProperty("artifact").GetString()));
        Assert.All(plan, item => Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("action").GetString())));
        Assert.Contains("marker generate --domain intent-cli --team alpha", plan[2].GetProperty("action").GetString(), StringComparison.Ordinal);
        Assert.Equal(hooksBefore, File.ReadAllBytes(workspace.HooksPath));

        var noOp = workspace.Set(SessionLayerMode.HerdrOnly);

        Assert.Equal(0, noOp.ExitCode);
        Assert.True(noOp.Result.GetProperty("already_recorded").GetBoolean());
        Assert.False(noOp.Result.TryGetProperty("migration_plan", out _));
    }

    [Fact]
    public void HerdrOnlyAgmsgHooks_AreAdvisoryResidueAndDoNotChangeTheRecordedMode_G602()
    {
        workspace.Record(SessionLayerMode.HerdrOnly);
        workspace.WriteTopology();
        workspace.WriteAndGenerateMarker(SessionLayerMode.HerdrOnly);
        workspace.WriteHooks("""
            { "hooks": { "session-start": [{ "command": "agmsg watch.sh" }], "session-end": [{ "command": "agmsg leave.sh" }] } }
            """);
        var recordBefore = File.ReadAllBytes(workspace.RecordPath);

        var preflight = SessionLayerPreflight.Analyze(workspace.RootPath, Workspace.Domain, Workspace.Team);

        Assert.Equal(SessionLayerPreflight.Ready, preflight.Verdict);
        var finding = Assert.Single(Assert.Single(preflight.Scopes).Findings,
            item => item.Cause == SessionLayerMigration.ResidueCause);
        Assert.Equal(SessionLayerMigration.ProjectHooksPath, finding.Role);
        Assert.Contains("'agmsg'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("exactly ONE session-layer mode", finding.Message, StringComparison.Ordinal);
        Assert.Contains("remove or disable", finding.Message, StringComparison.Ordinal);
        Assert.Contains("never infers, changes, or overrides", finding.Message, StringComparison.Ordinal);
        Assert.Equal(recordBefore, File.ReadAllBytes(workspace.RecordPath));
        Assert.Equal(SessionLayerMode.HerdrOnly,
            SessionLayerModeStore.Resolve(workspace.RootPath, Workspace.Domain, Workspace.Team).Mode);
    }

    [Fact]
    public void AgmsgHerdrHooks_AreDetectedWhenTheReverseResidueIsDeclared_G602()
    {
        workspace.Record(SessionLayerMode.Agmsg);
        workspace.WriteAndGenerateMarker(SessionLayerMode.Agmsg);
        workspace.WriteHooks("""
            { "hooks": { "SessionStart": [{ "command": "herdr agent start" }] } }
            """);

        var preflight = SessionLayerPreflight.Analyze(workspace.RootPath, Workspace.Domain, Workspace.Team);

        Assert.Equal(SessionLayerPreflight.Ready, preflight.Verdict);
        var finding = Assert.Single(Assert.Single(preflight.Scopes).Findings,
            item => item.Cause == SessionLayerMigration.ResidueCause);
        Assert.Contains("'herdr-only'", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Assert.Single(preflight.Scopes).Findings, item => item.Cause == "marker-drift");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Guidance_ExplainsManualMigrationAndAdvisoryResidue_G602(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md"));

        Assert.Contains("manual migration", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G601", content, StringComparison.Ordinal);
        Assert.Contains("other-mode-residue", content, StringComparison.Ordinal);
        Assert.Contains("never", content, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "alpha";

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-layer-migration-g602-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        public string RecordPath => SessionLayerModeStore.ResolvePath(RootPath);
        public string HooksPath => Path.Combine(RootPath, SessionLayerMigration.ProjectHooksPath);

        public void Record(string mode)
        {
            var result = Set(mode);
            Assert.Equal(0, result.ExitCode);
        }

        public (int ExitCode, JsonElement Result) Set(string mode)
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(Context,
                ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"], writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public void WriteHooks(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
            File.WriteAllText(HooksPath, content);
        }

        public void WriteAndGenerateMarker(string mode)
        {
            var path = Path.Combine(RootPath, "AGENTS.md");
            File.WriteAllText(path,
                $"<!-- intent-cli:session-layer-marker:start domain=\"{Domain}\" team=\"{Team}\" -->\n"
                + "<!-- intent-cli:session-layer-marker:end -->\n");
            using var writer = new StringWriter();
            var exitCode = SessionLayerMarkerCommand.Execute(Context,
                ["generate", "--domain", Domain, "--team", Team, "--file", path, "--write", "--format", "json"], writer);
            Assert.True(exitCode == 0, writer.ToString());
            Assert.Contains($"mode=\"{mode}\"", File.ReadAllText(path), StringComparison.Ordinal);
        }

        public void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                team = Team,
                workspace_id = "w",
                roles = new
                {
                    implementation = new { resident = "herdr", workspace_id = "w", pane_id = "w:p2" },
                    orchestration = new { resident = "herdr", workspace_id = "w", pane_id = "w:p1" },
                },
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
