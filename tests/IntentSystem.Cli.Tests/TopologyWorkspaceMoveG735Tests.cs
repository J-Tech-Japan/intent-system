using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G735: the topology move must accept a team whose roles share one recorded
/// pane while still refusing genuinely ambiguous mappings, and the record-side
/// refusal must lead to a move command that actually works.
/// Fixtures are intentionally left for the host environment; these tests never
/// delete a temporary path.
/// </summary>
public sealed class TopologyWorkspaceMoveG735Tests
{
    private const string Domain = "remote-herdr";
    private const string Team = "remote-herdr";
    private static readonly JsonSerializerOptions FixtureJsonOptions = new() { WriteIndented = true };

    [Fact]
    public void Move_AcceptsSeveralRolesSharingOneOldPane_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        // The reporter's shape: orchestrator and orchestration both live at wS:p1.
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex", "inline");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude", "file-backed");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var beforeBytes = File.ReadAllBytes(topologyPath);

        var preview = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--dry-run", "--format", "json",
        ]);

        Assert.Equal("dry-run", preview.GetProperty("mode").GetString());
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.True(preview.GetProperty("changed").GetBoolean());
        Assert.Equal("wS", preview.GetProperty("previous_workspace_id").GetString());
        Assert.Equal("w44", preview.GetProperty("workspace_id").GetString());
        Assert.Equal(beforeBytes, File.ReadAllBytes(topologyPath));

        var previewRoles = preview.GetProperty("after").GetProperty("roles");
        Assert.Equal("w44:p1", previewRoles.GetProperty("orchestration").GetProperty("pane_id").GetString());
        Assert.Equal("w44:p1", previewRoles.GetProperty("orchestrator").GetProperty("pane_id").GetString());
        Assert.Equal("w44:p2", previewRoles.GetProperty("implementation").GetProperty("pane_id").GetString());

        var write = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--current-digest", preview.GetProperty("current_digest").GetString()!, "--write", "--format", "json",
        ]);

        Assert.Equal("write", write.GetProperty("mode").GetString());
        Assert.True(write.GetProperty("applied").GetBoolean());
        Assert.False(write.GetProperty("conflict").GetBoolean());

        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        Assert.Equal("w44", moved["workspace_id"]!.GetValue<string>());
        var movedRoles = moved["roles"]!.AsObject();
        Assert.Equal("w44:p1", movedRoles["orchestration"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", movedRoles["orchestrator"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44", movedRoles["orchestration"]!["workspace_id"]!.GetValue<string>());
        Assert.Equal("w44", movedRoles["orchestrator"]!["workspace_id"]!.GetValue<string>());
        Assert.Equal("/orchestration", movedRoles["orchestration"]!["cwd"]!.GetValue<string>());
        Assert.Equal("/orchestrator", movedRoles["orchestrator"]!["cwd"]!.GetValue<string>());
        Assert.Equal("w44:p2", movedRoles["implementation"]!["pane_id"]!.GetValue<string>());

        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validation.GetProperty("valid").GetBoolean());

        var resolved = NotifyRoleTopologyStore.Resolve(context.RepoRoot, Domain, Team);
        Assert.True(resolved.Resolved, resolved.Summary);
        Assert.Equal("w44:p1", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolved.Topology!, "orchestrator").Target);
        Assert.Equal("w44:p1", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolved.Topology!, "orchestration").Target);
    }

    [Fact]
    public void Move_RefusesTwoDifferentOldPanesMappingToOneNewPane_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var refused = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p1",
            "--dry-run", "--format", "json",
        ], expectedExitCode: 1);

        Assert.True(refused.GetProperty("conflict").GetBoolean());
        var summary = refused.GetProperty("summary").GetString();
        Assert.Contains("two different recorded old panes", summary, StringComparison.Ordinal);
        Assert.Contains("ambiguous", summary, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));
    }

    [Fact]
    public void Move_RecordRedirectLeadsToACommandThatWorksForASharedPaneTeam_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var mismatch = RecordHerdrResult(context, "review", "w44:p3", "/review", "claude", "inline");
        Assert.Equal(1, mismatch.ExitCode);
        var summary = mismatch.Result.GetProperty("summary").GetString();
        Assert.Contains("session-layer topology move", summary, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));

        // Following the record-side redirect must reach a working command.
        var moved = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--write", "--format", "json",
        ]);
        Assert.True(moved.GetProperty("applied").GetBoolean());
        Assert.False(moved.GetProperty("conflict").GetBoolean());

        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validation.GetProperty("valid").GetBoolean());
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
        var result = RecordHerdrResult(context, role, pane, cwd, kind, deliveryMethod);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Result.GetProperty("conflict").GetBoolean());
    }

    private static (int ExitCode, JsonElement Result) RecordHerdrResult(
        CliContext context,
        string role,
        string pane,
        string cwd,
        string kind,
        string deliveryMethod)
    {
        return RunWithResult(
            context,
            [
                "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", role,
                "--resident", "herdr", "--workspace-id", pane[..pane.IndexOf(':')], "--pane-id", pane,
                "--cwd", cwd, "--kind", kind, "--delivery-method", deliveryMethod, "--write", "--format", "json",
            ]);
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
