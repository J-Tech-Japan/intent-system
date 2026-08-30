using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G735: a pane map that moves several roles sharing one recorded old pane
/// onto one new pane is unambiguous — the roles travel with their pane — and
/// must be accepted. Only two different old panes mapping to one new pane is
/// genuinely ambiguous and keeps its refusal. The record-side whole-team
/// refusal must point at a move that actually performs the rebuild.
/// Fixtures are intentionally left for the host environment; these tests never
/// delete a temporary path.
/// </summary>
public sealed class TopologyWorkspaceMoveSharedPaneG735Tests
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";

    [Fact]
    public void Move_SharedOldPaneRolesTravelTogether_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex", "inline");
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "claude", "inline");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude", "file-backed");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);

        var preview = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44",
            "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--dry-run", "--format", "json",
        ]);
        Assert.Equal("dry-run", preview.GetProperty("mode").GetString());
        Assert.True(preview.GetProperty("changed").GetBoolean());
        Assert.False(preview.GetProperty("conflict").GetBoolean());

        var write = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44",
            "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--current-digest", preview.GetProperty("current_digest").GetString()!,
            "--write", "--format", "json",
        ]);
        Assert.True(write.GetProperty("applied").GetBoolean());
        Assert.False(write.GetProperty("conflict").GetBoolean());

        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        Assert.Equal("w44", moved["workspace_id"]!.GetValue<string>());
        var movedRoles = moved["roles"]!.AsObject();
        Assert.Equal("w44:p1", movedRoles["orchestrator"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", movedRoles["orchestration"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p2", movedRoles["implementation"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("/orchestrator", movedRoles["orchestrator"]!["cwd"]!.GetValue<string>());
        Assert.Equal("codex", movedRoles["orchestrator"]!["kind"]!.GetValue<string>());
        Assert.Equal("claude", movedRoles["orchestration"]!["kind"]!.GetValue<string>());

        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validation.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Move_TwoDifferentOldPanesToOneNewPaneStillRefused_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex", "inline");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var refused = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44",
            "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p1",
            "--dry-run", "--format", "json",
        ], expectedExitCode: 1);

        Assert.True(refused.GetProperty("conflict").GetBoolean());
        var summary = refused.GetProperty("summary").GetString();
        Assert.Contains("wS:p1", summary, StringComparison.Ordinal);
        Assert.Contains("wS:p2", summary, StringComparison.Ordinal);
        Assert.Contains("ambiguous", summary, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));
    }

    [Fact]
    public void Record_WholeTeamRefusalPointsAtMoveThatPerformsSharedPaneRebuild_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex", "inline");
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "claude", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var refusal = RunWithResult(context, [
            "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", "implementation",
            "--resident", "herdr", "--workspace-id", "w44", "--pane-id", "w44:p2",
            "--cwd", "/implementation", "--kind", "claude", "--delivery-method", "inline",
            "--write", "--format", "json",
        ]);
        Assert.Equal(1, refusal.ExitCode);
        var refusalSummary = refusal.Result.GetProperty("summary").GetString();
        Assert.Contains("already records workspace_id", refusalSummary, StringComparison.Ordinal);
        Assert.Contains("topology move", refusalSummary, StringComparison.Ordinal);

        var rebuild = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44",
            "--pane-map", "wS:p1=w44:p1",
            "--write", "--format", "json",
        ]);
        Assert.True(rebuild.GetProperty("applied").GetBoolean());
        Assert.NotEqual(before, File.ReadAllText(topologyPath));

        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        Assert.Equal("w44", moved["workspace_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", moved["roles"]!["orchestrator"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", moved["roles"]!["orchestration"]!["pane_id"]!.GetValue<string>());
    }

    private static CliContext CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"intent-g735-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return new CliContext
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
            },
        };
    }

    private static void SetHerdrOnly(CliContext context)
    {
        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);
    }

    private static void RecordHerdr(
        CliContext context,
        string role,
        string pane,
        string cwd,
        string kind,
        string deliveryMethod)
    {
        var result = RunWithResult(context, [
            "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", role,
            "--resident", "herdr", "--workspace-id", pane[..pane.IndexOf(':')], "--pane-id", pane,
            "--cwd", cwd, "--kind", kind, "--delivery-method", deliveryMethod, "--write", "--format", "json",
        ]);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Result.GetProperty("conflict").GetBoolean());
    }

    private static JsonElement Run(CliContext context, IReadOnlyList<string> args, int expectedExitCode = 0)
    {
        var result = RunWithResult(context, args);
        Assert.Equal(expectedExitCode, result.ExitCode);
        return result.Result;
    }

    private static (int ExitCode, JsonElement Result) RunWithResult(
        CliContext context,
        IReadOnlyList<string> args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args.ToArray(), context, writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
    }
}
