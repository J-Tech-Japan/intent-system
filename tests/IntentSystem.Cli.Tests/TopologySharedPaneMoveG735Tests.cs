using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G735: a recorded pane is the unit of the move's pane map. Several roles
/// sharing one recorded pane travel together to that pane's new pane, while
/// two different recorded panes collapsing onto one new pane stays refused as
/// genuinely ambiguous. The per-role record refusal therefore names a route
/// that completes for a shared-pane team.
/// </summary>
public sealed class TopologySharedPaneMoveG735Tests
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";

    [Fact]
    public void Move_TwoRolesSharingOneRecordedPane_TravelTogetherAndValidate_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "w2X:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "implementation", "w2X:p1", "/implementation", "claude", "inline");
        RecordHerdr(context, "review", "w2X:p2", "/review", "claude", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);

        var preview = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "w2X:p1=w44:p1", "--pane-map", "w2X:p2=w44:p2",
            "--dry-run", "--format", "json",
        ]);
        Assert.Equal("dry-run", preview.GetProperty("mode").GetString());
        Assert.True(preview.GetProperty("changed").GetBoolean());
        Assert.False(preview.GetProperty("conflict").GetBoolean());

        var write = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "w2X:p1=w44:p1", "--pane-map", "w2X:p2=w44:p2",
            "--current-digest", preview.GetProperty("current_digest").GetString()!, "--write", "--format", "json",
        ]);
        Assert.Equal("write", write.GetProperty("mode").GetString());
        Assert.True(write.GetProperty("applied").GetBoolean());
        Assert.False(write.GetProperty("conflict").GetBoolean());

        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        Assert.Equal("w44", moved["workspace_id"]!.GetValue<string>());
        var movedRoles = moved["roles"]!.AsObject();
        Assert.Equal("w44:p1", movedRoles["orchestration"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44", movedRoles["orchestration"]!["workspace_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", movedRoles["implementation"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44", movedRoles["implementation"]!["workspace_id"]!.GetValue<string>());
        Assert.Equal("w44:p2", movedRoles["review"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("/orchestration", movedRoles["orchestration"]!["cwd"]!.GetValue<string>());
        Assert.Equal("/implementation", movedRoles["implementation"]!["cwd"]!.GetValue<string>());

        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validation.GetProperty("valid").GetBoolean());

        var resolution = NotifyRoleTopologyStore.Resolve(context.RepoRoot, Domain, Team);
        Assert.True(resolution.Resolved, resolution.Summary);
        Assert.Equal("w44:p1", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolution.Topology!, "orchestration").Target);
        Assert.Equal("w44:p1", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolution.Topology!, "implementation").Target);
        Assert.Equal("w44:p2", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolution.Topology!, "review").Target);
    }

    [Fact]
    public void Move_TwoDifferentRecordedPanesToOneNewPane_StillRefused_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "w2X:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "implementation", "w2X:p2", "/implementation", "claude", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var result = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "w2X:p1=w44:p1", "--pane-map", "w2X:p2=w44:p1",
            "--write", "--format", "json",
        ], expectedExitCode: 1);

        Assert.True(result.GetProperty("conflict").GetBoolean());
        var summary = result.GetProperty("summary").GetString();
        Assert.Contains("two different recorded panes", summary, StringComparison.Ordinal);
        Assert.Contains("w2X:p1", summary, StringComparison.Ordinal);
        Assert.Contains("w2X:p2", summary, StringComparison.Ordinal);
        Assert.Contains("w44:p1", summary, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));
    }

    [Fact]
    public void Record_WorkspaceMismatchOnSharedPaneTeam_NamesMoveThatCompletes_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "w2X:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "implementation", "w2X:p1", "/implementation", "claude", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var refusal = Run(context, [
            "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", "review",
            "--resident", "herdr", "--workspace-id", "w44", "--pane-id", "w44:p3", "--cwd", "/review",
            "--kind", "claude", "--delivery-method", "inline", "--write", "--format", "json",
        ], expectedExitCode: 1);
        Assert.True(refusal.GetProperty("conflict").GetBoolean());
        var refusalSummary = refusal.GetProperty("summary").GetString();
        Assert.Contains("session-layer topology move", refusalSummary, StringComparison.Ordinal);
        Assert.Contains("share one recorded pane travel together", refusalSummary, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));

        var preview = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "w2X:p1=w44:p1", "--dry-run", "--format", "json",
        ]);
        var write = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "w2X:p1=w44:p1",
            "--current-digest", preview.GetProperty("current_digest").GetString()!, "--write", "--format", "json",
        ]);
        Assert.True(write.GetProperty("applied").GetBoolean());
        Assert.False(write.GetProperty("conflict").GetBoolean());
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
        var result = RunWithResult(
            context,
            [
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
