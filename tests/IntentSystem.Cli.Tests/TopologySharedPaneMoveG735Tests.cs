using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G735: the topology move guard was keyed on new-pane repetition, so a team
/// whose roles share one pane (the shape the orchestrator guide documents)
/// could not be rebuilt at all: <c>move</c> refused it as ambiguous while
/// <c>record</c> redirected to <c>move</c>. The guard must key on old-pane
/// identity instead — a shared old pane traveling to one new pane is not
/// ambiguous, while two different old panes collapsing onto one new pane
/// stays refused. The fixtures below construct both shapes; neither exists
/// on the host.
/// </summary>
public sealed class TopologySharedPaneMoveG735Tests
{
    private const string Domain = "remote-herdr";
    private const string Team = "remote-herdr";

    [Fact]
    public void Move_SharedPaneRoles_TravelWithTheirPaneAndTheMoveApplies_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);

        // The failing shape from #1593: orchestration AND orchestrator sit
        // on wS:p1 while implementation sits on wS:p2.
        Assert.Equal(0, Record(context, "orchestration", "wS:p1", "/orchestration", "codex", "inline").ExitCode);
        Assert.Equal(0, Record(context, "orchestrator", "wS:p1", "/orchestrator", "codex", "inline").ExitCode);
        Assert.Equal(0, Record(context, "implementation", "wS:p2", "/implementation", "claude", "file-backed")
            .ExitCode);

        var preview = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--dry-run", "--format", "json",
        ]);
        Assert.Equal("dry-run", preview.Result.GetProperty("mode").GetString());
        Assert.False(preview.Result.GetProperty("conflict").GetBoolean());
        Assert.True(preview.Result.GetProperty("changed").GetBoolean());

        var write = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--write", "--format", "json",
        ]);
        Assert.False(write.Result.GetProperty("conflict").GetBoolean());
        Assert.True(write.Result.GetProperty("applied").GetBoolean());

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var topology = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        var roles = topology["roles"]!.AsObject();
        Assert.Equal("w44:p1", roles["orchestration"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", roles["orchestrator"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p2", roles["implementation"]!["pane_id"]!.GetValue<string>());

        var validated = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validated.Result.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Move_TwoOldPanesMappingToOneNewPane_RemainsRefusedAsAmbiguous_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);

        // Genuinely ambiguous: wS:p1 and wS:p2 are distinct panes whose
        // roles would collapse onto w44:p1 — which pane's roles go where?
        Assert.Equal(0, Record(context, "orchestration", "wS:p1", "/orchestration", "codex", "inline").ExitCode);
        Assert.Equal(0, Record(context, "orchestrator", "wS:p2", "/orchestrator", "codex", "inline").ExitCode);

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var result = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p1",
            "--dry-run", "--format", "json",
        ], 1);

        Assert.True(result.Result.GetProperty("conflict").GetBoolean());
        Assert.Contains(
            "maps more than one recorded role to new pane 'w44:p1'",
            result.Result.GetProperty("summary").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));
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

    private static (int ExitCode, JsonElement Result) Record(
        CliContext context,
        string role,
        string pane,
        string cwd,
        string kind,
        string deliveryMethod)
    {
        var result = Run(context, [
            "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", role,
            "--resident", "herdr", "--workspace-id", pane[..pane.IndexOf(':')], "--pane-id", pane,
            "--cwd", cwd, "--kind", kind, "--delivery-method", deliveryMethod, "--write", "--format", "json",
        ]);
        Assert.Equal(0, result.ExitCode);
        return result;
    }

    private static (int ExitCode, JsonElement Result) Run(
        CliContext context,
        IReadOnlyList<string> args,
        int expectedExitCode = 0)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args.ToArray(), context, writer);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = (exitCode, document.RootElement.Clone());
        if (exitCode != expectedExitCode)
        {
            throw new InvalidOperationException($"expected {expectedExitCode}, got {result}");
        }

        return result;
    }
}
