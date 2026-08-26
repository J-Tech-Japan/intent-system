using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G735: a pane is the unit that travels during a workspace rebuild. Multiple
/// logical roles may share that pane, while distinct old panes must not
/// converge on one new pane.
/// </summary>
public sealed class TopologyWorkspaceMoveG735Tests
{
    private const string Domain = "remote-herdr";
    private const string Team = "remote-herdr";

    private readonly ITestOutputHelper output;

    public TopologyWorkspaceMoveG735Tests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Move_AllowsRolesSharingOneOldPane_AndValidationSucceeds_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex");
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "codex");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude");
        RecordHerdr(context, "review", "wS:p3", "/review", "claude");

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        output.WriteLine($"input topology: {Describe(before)}");

        var move = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--pane-map", "wS:p3=w44:p3", "--write", "--format", "json",
        ]);
        output.WriteLine($"shared-pane move output: {move.GetRawText()}");

        Assert.True(move.GetProperty("applied").GetBoolean());
        Assert.False(move.GetProperty("conflict").GetBoolean());
        Assert.Equal("w44", move.GetProperty("workspace_id").GetString());

        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        Assert.Equal("w44", moved["workspace_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", moved["roles"]!["orchestrator"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p1", moved["roles"]!["orchestration"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p2", moved["roles"]!["implementation"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w44:p3", moved["roles"]!["review"]!["pane_id"]!.GetValue<string>());
        foreach (var role in new[] { "orchestrator", "orchestration", "implementation", "review" })
        {
            Assert.Equal("w44", moved["roles"]![role]!["workspace_id"]!.GetValue<string>());
        }

        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        output.WriteLine($"shared-pane topology validate output: {validation.GetRawText()}");
        Assert.True(validation.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Move_RefusesDistinctOldPanesConvergingOnOneNewPane_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude");

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);
        var refusal = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p1",
            "--write", "--format", "json",
        ], expectedExitCode: 1);
        output.WriteLine($"distinct-old-pane convergence input: wS:p1=w44:p1, wS:p2=w44:p1");
        output.WriteLine($"distinct-old-pane refusal output: {refusal.GetRawText()}");

        Assert.True(refusal.GetProperty("conflict").GetBoolean());
        Assert.Contains("distinct recorded old panes", refusal.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("ambiguous workspace move", refusal.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));
    }

    [Fact]
    public void Move_DistinctOldPanesToDistinctNewPanesRemainSupported_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude");

        var move = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--write", "--format", "json",
        ]);
        output.WriteLine($"ordinary distinct-pane move output: {move.GetRawText()}");

        Assert.True(move.GetProperty("applied").GetBoolean());
        Assert.False(move.GetProperty("conflict").GetBoolean());
        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validation.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Record_MismatchPointsToWorkingWholeTeamMove_AndMatchesDisclosedHandEdit_G735()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestrator", "wS:p1", "/orchestrator", "codex");
        RecordHerdr(context, "orchestration", "wS:p1", "/orchestration", "codex");
        RecordHerdr(context, "implementation", "wS:p2", "/implementation", "claude");
        RecordHerdr(context, "review", "wS:p3", "/review", "claude");

        var mismatch = RecordHerdrResult(context, "interview", "w44:p4", "/interview", "codex");
        output.WriteLine($"record mismatch output: {mismatch.Result.GetRawText()}");
        Assert.Equal(1, mismatch.ExitCode);
        Assert.Contains("Refusing per-role repair", mismatch.Result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("topology move", mismatch.Result.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var move = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w44", "--pane-map", "wS:p1=w44:p1", "--pane-map", "wS:p2=w44:p2",
            "--pane-map", "wS:p3=w44:p3", "--write", "--format", "json",
        ]);
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        output.WriteLine($"fixed command output: {move.GetRawText()}");
        output.WriteLine($"reporter hand-edit compatibility: {Describe(moved)}; redo_required=false");

        var expectedHandEdit = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orchestrator"] = "w44:p1",
            ["orchestration"] = "w44:p1",
            ["implementation"] = "w44:p2",
            ["review"] = "w44:p3",
        };
        Assert.Equal("w44", moved["workspace_id"]!.GetValue<string>());
        foreach (var (role, pane) in expectedHandEdit)
        {
            Assert.Equal("w44", moved["roles"]![role]!["workspace_id"]!.GetValue<string>());
            Assert.Equal(pane, moved["roles"]![role]!["pane_id"]!.GetValue<string>());
        }

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
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
            writer));
    }

    private static void RecordHerdr(CliContext context, string role, string pane, string cwd, string kind)
    {
        var result = RecordHerdrResult(context, role, pane, cwd, kind);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.Result.GetProperty("conflict").GetBoolean());
    }

    private static (int ExitCode, JsonElement Result) RecordHerdrResult(
        CliContext context,
        string role,
        string pane,
        string cwd,
        string kind)
    {
        return RunWithResult(context, [
            "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", role,
            "--resident", "herdr", "--workspace-id", pane[..pane.IndexOf(':')], "--pane-id", pane,
            "--cwd", cwd, "--kind", kind, "--delivery-method", "inline", "--write", "--format", "json",
        ]);
    }

    private static JsonElement Run(
        CliContext context,
        IReadOnlyList<string> args,
        int expectedExitCode = 0)
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

    private static string Describe(JsonObject topology)
    {
        var roles = topology["roles"]!.AsObject()
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{entry.Key}={entry.Value!["workspace_id"]}/{entry.Value!["pane_id"]}");
        return $"workspace_id={topology["workspace_id"]}; roles={string.Join(", ", roles)}";
    }
}
