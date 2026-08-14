using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G697: the topology workspace move is an explicit, dry-run-first, CAS-guarded
/// operation and its installed recipe is reachable from every role-facing guide.
/// Fixtures are intentionally left for the host environment; these tests never
/// delete a temporary path.
/// </summary>
public sealed class TopologyWorkspaceMoveG697Tests
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private static readonly JsonSerializerOptions FixtureJsonOptions = new() { WriteIndented = true };

    [Fact]
    public void Move_DryRunThenWrite_PreservesFieldsAndResolvesNewPanes_G697()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "w2X:p1", "/orchestration", "codex", "inline");
        RecordHerdr(context, "implementation", "w2X:p2", "/implementation", "claude", "file-backed");
        RecordExternal(context, "review", ".intent-cli/events/intent-cli-dev.jsonl", "claude-app");

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var topology = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        topology["team_custom_marker"] = "keep-team-field";
        topology["roles"]!["orchestration"]!["role_custom_marker"] = "keep-role-field";
        File.WriteAllText(topologyPath, topology.ToJsonString(FixtureJsonOptions) + Environment.NewLine);
        var beforeBytes = File.ReadAllBytes(topologyPath);

        var preview = Run(
            context,
            [
                "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
                "--workspace-id", "w33", "--pane-map", "w2X:p1=w33:p1", "--pane-map", "w2X:p2=w33:p2",
                "--dry-run", "--format", "json",
            ]);

        Assert.Equal("dry-run", preview.GetProperty("mode").GetString());
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.True(preview.GetProperty("changed").GetBoolean());
        Assert.Equal("w2X", preview.GetProperty("previous_workspace_id").GetString());
        Assert.Equal("w33", preview.GetProperty("workspace_id").GetString());
        Assert.Equal("w2X", preview.GetProperty("before").GetProperty("workspace_id").GetString());
        Assert.Equal("w33", preview.GetProperty("after").GetProperty("workspace_id").GetString());
        Assert.Equal(beforeBytes, File.ReadAllBytes(topologyPath));

        var previewOrchestration = preview.GetProperty("after").GetProperty("roles").GetProperty("orchestration");
        Assert.Equal("w33:p1", previewOrchestration.GetProperty("pane_id").GetString());
        Assert.Equal("w33", previewOrchestration.GetProperty("workspace_id").GetString());
        Assert.Equal("/orchestration", previewOrchestration.GetProperty("cwd").GetString());
        Assert.Equal("codex", previewOrchestration.GetProperty("kind").GetString());
        Assert.Equal("inline", previewOrchestration.GetProperty("delivery_method").GetString());
        Assert.Equal("keep-role-field", previewOrchestration.GetProperty("role_custom_marker").GetString());
        Assert.Equal("keep-team-field", preview.GetProperty("after").GetProperty("team_custom_marker").GetString());
        Assert.Equal(".intent-cli/events/intent-cli-dev.jsonl", preview.GetProperty("after")
            .GetProperty("roles").GetProperty("review").GetProperty("reader").GetString());

        var write = Run(
            context,
            [
                "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
                "--workspace-id", "w33", "--pane-map", "w2X:p1=w33:p1", "--pane-map", "w2X:p2=w33:p2",
                "--current-digest", preview.GetProperty("current_digest").GetString()!, "--write", "--format", "json",
            ]);

        Assert.Equal("write", write.GetProperty("mode").GetString());
        Assert.True(write.GetProperty("applied").GetBoolean());
        Assert.False(write.GetProperty("conflict").GetBoolean());
        Assert.False(beforeBytes.SequenceEqual(File.ReadAllBytes(topologyPath)));

        var moved = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        Assert.Equal("w33", moved["workspace_id"]!.GetValue<string>());
        Assert.Equal("keep-team-field", moved["team_custom_marker"]!.GetValue<string>());
        var movedRoles = moved["roles"]!.AsObject();
        Assert.Equal("w33:p1", movedRoles["orchestration"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w33", movedRoles["orchestration"]!["workspace_id"]!.GetValue<string>());
        Assert.Equal("/orchestration", movedRoles["orchestration"]!["cwd"]!.GetValue<string>());
        Assert.Equal("codex", movedRoles["orchestration"]!["kind"]!.GetValue<string>());
        Assert.Equal("inline", movedRoles["orchestration"]!["delivery_method"]!.GetValue<string>());
        Assert.Equal("keep-role-field", movedRoles["orchestration"]!["role_custom_marker"]!.GetValue<string>());
        Assert.Equal("w33:p2", movedRoles["implementation"]!["pane_id"]!.GetValue<string>());
        Assert.Equal("w33", movedRoles["implementation"]!["workspace_id"]!.GetValue<string>());
        Assert.Equal("/implementation", movedRoles["implementation"]!["cwd"]!.GetValue<string>());
        Assert.Equal("claude", movedRoles["implementation"]!["kind"]!.GetValue<string>());
        Assert.Equal("file-backed", movedRoles["implementation"]!["delivery_method"]!.GetValue<string>());
        Assert.Equal("external", movedRoles["review"]!["resident"]!.GetValue<string>());
        Assert.Equal("claude-app", movedRoles["review"]!["frontend"]!.GetValue<string>());

        var validation = Run(context, [
            "session-layer", "topology", "validate", "--domain", Domain, "--team", Team, "--format", "json",
        ]);
        Assert.True(validation.GetProperty("valid").GetBoolean());

        var resolved = NotifyRoleTopologyStore.Resolve(context.RepoRoot, Domain, Team);
        Assert.True(resolved.Resolved, resolved.Summary);
        Assert.Equal("w33:p1", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolved.Topology!, "orchestration").Target);
        Assert.Equal("w33:p2", NotifyRoleTopologyStore.ResolveDeliveryTarget(
            context.RepoRoot, resolved.Topology!, "implementation").Target);

        var passive = SessionLayerPreflight.Analyze(context.RepoRoot, Domain, Team, "review");
        Assert.Equal(SessionLayerPreflight.Ready, passive.Verdict);
        Assert.Single(passive.Scopes);

        var notify = Run(
            context,
            [
                "notify", "delegate", "--domain", Domain, "--team", Team, "--from", "orchestration",
                "--to", "review", "--report-to", "orchestration", "--task-id", "G697-preflight",
                "--objective", "Verify moved topology", "--input", "issue #1507",
                "--expected-artifact", "preflight result", "--result-nonce", "g697-preflight-nonce",
                "--dry-run", "--format", "json",
            ]);
        Assert.False(notify.GetProperty("delivered").GetBoolean());
        Assert.Equal(SessionLayerPreflight.Ready,
            notify.GetProperty("session_layer_preflight").GetProperty("verdict").GetString());
    }

    [Fact]
    public void Move_StaleDigestRefusesReplacementAndLeavesRecordUnchanged_G697()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "w2X:p1", "/orchestration", "codex", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);

        var preview = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w33", "--pane-map", "w2X:p1=w33:p1", "--dry-run", "--format", "json",
        ]);
        var staleDigest = preview.GetProperty("current_digest").GetString()!;

        var changedByAnotherWriter = JsonNode.Parse(File.ReadAllText(topologyPath))!.AsObject();
        changedByAnotherWriter["concurrent_change"] = "must survive refusal";
        File.WriteAllText(topologyPath, changedByAnotherWriter.ToJsonString(FixtureJsonOptions) + Environment.NewLine);
        var unchangedAfterRefusal = File.ReadAllText(topologyPath);

        var result = Run(
            context,
            [
                "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
                "--workspace-id", "w33", "--pane-map", "w2X:p1=w33:p1", "--current-digest", staleDigest,
                "--write", "--format", "json",
            ],
            expectedExitCode: 1);

        Assert.True(result.GetProperty("conflict").GetBoolean());
        Assert.Contains("lost its CAS", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(unchangedAfterRefusal, File.ReadAllText(topologyPath));
        Assert.Equal("w2X", JsonNode.Parse(File.ReadAllText(topologyPath))!["workspace_id"]!.GetValue<string>());
    }

    [Fact]
    public void Move_RefusesPartialMapsAndRecordMismatchNamesTheSanctionedMove_G697()
    {
        var context = CreateFixture();
        SetHerdrOnly(context);
        RecordHerdr(context, "orchestration", "w2X:p1", "/orchestration", "codex", "inline");
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(context.RepoRoot, Domain, Team);
        var before = File.ReadAllText(topologyPath);

        var mismatch = RecordHerdrResult(context, "implementation", "w33:p2", "/implementation", "claude", "inline");
        Assert.Equal(1, mismatch.ExitCode);
        Assert.Contains("session-layer topology move", mismatch.Result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));

        var partial = Run(context, [
            "session-layer", "topology", "move", "--domain", Domain, "--team", Team,
            "--workspace-id", "w33", "--dry-run", "--format", "json",
        ], expectedExitCode: 1);
        Assert.True(partial.GetProperty("conflict").GetBoolean());
        Assert.Contains("No --pane-map", partial.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(topologyPath));
    }

    [Fact]
    public void InstalledRecipe_IsReachableFromDirectAndRoleFacingGuides_G697()
    {
        var context = CreateFixture();

        using var directWriter = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["guide", "topology-workspace-move", "--domain", Domain, "--team", Team, "--format", "json"],
            context,
            directWriter));
        using var direct = JsonDocument.Parse(directWriter.ToString());
        AssertMoveGuide(direct.RootElement);
        Assert.Contains("topology move", direct.RootElement.GetProperty("commands").GetProperty("preview").GetString(), StringComparison.Ordinal);

        using var reviewWriter = new StringWriter();
        Assert.Equal(1, GuideReviewCommand.Execute(
            context,
            ["--pr", "1507", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            reviewWriter));
        using var review = JsonDocument.Parse(reviewWriter.ToString());
        AssertMoveGuide(review.RootElement.GetProperty("topology_workspace_move"));

        using var nextWriter = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            context,
            ["--role", "review", "--format", "json"],
            nextWriter));
        using var next = JsonDocument.Parse(nextWriter.ToString());
        AssertMoveGuide(next.RootElement.GetProperty("topology_workspace_move"));

        using var orchestratorWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            context,
            ["--format", "json"],
            orchestratorWriter));
        using var orchestrator = JsonDocument.Parse(orchestratorWriter.ToString());
        AssertMoveGuide(orchestrator.RootElement.GetProperty("topology_workspace_move"));

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideTopologyWorkspaceMoveCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--format", "markdown"],
            markdownWriter));
        Assert.Contains("## Canonical workflow", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("--dry-run", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("--write", markdownWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstalledRecipe_RunsFromBareDirectoryWithoutIntentCliConfig_G697()
    {
        // G697: exercise the actual CLI entry point from a directory that is
        // deliberately outside any repository and has no `.intent-cli/`.
        // This is a negative metadata-dependency guard: the guide must not
        // look up, create, or require host configuration just to render the
        // installed recipe. The bare fixture is intentionally retained; no
        // test cleanup may delete a path under the system temporary directory.
        var bareDirectory = Path.Combine(Path.GetTempPath(), $"intent-g697-bare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(bareDirectory);
        var configDirectory = Path.Combine(bareDirectory, ".intent-cli");
        Assert.False(Directory.Exists(configDirectory));

        var cliAssembly = typeof(Program).Assembly.Location;
        Assert.False(string.IsNullOrWhiteSpace(cliAssembly));

        var startInfo = new ProcessStartInfo(Environment.ProcessPath ?? "dotnet")
        {
            WorkingDirectory = bareDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(cliAssembly);
        startInfo.ArgumentList.Add("guide");
        startInfo.ArgumentList.Add("topology-workspace-move");
        startInfo.ArgumentList.Add("--domain");
        startInfo.ArgumentList.Add(Domain);
        startInfo.ArgumentList.Add("--team");
        startInfo.ArgumentList.Add(Team);
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("json");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the built intent-cli process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("\"guide_surface\": \"guide topology-workspace-move\"", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("config.toml", stdout + stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("missing host state", stdout + stderr, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(configDirectory));
    }

    private static void AssertMoveGuide(JsonElement root)
    {
        Assert.Equal("guide topology-workspace-move", root.GetProperty("guide_surface").GetString());
        Assert.True(root.GetProperty("read_only").GetBoolean());
        Assert.True(root.GetProperty("dry_run_first").GetBoolean());
        var routes = root.GetProperty("routes").EnumerateArray().ToArray();
        Assert.Contains(routes, route => route.GetProperty("role").GetString() == "operator");
        Assert.Contains(routes, route => route.GetProperty("role").GetString() == "orchestrator");
        var commands = root.GetProperty("commands");
        Assert.Contains("topology move", commands.GetProperty("preview").GetString(), StringComparison.Ordinal);
        Assert.Contains("topology move", commands.GetProperty("apply").GetString(), StringComparison.Ordinal);
        Assert.Contains("notify delegate", commands.GetProperty("notify_preflight").GetString(), StringComparison.Ordinal);
        Assert.Contains("CAS", root.GetProperty("cas_contract").GetString(), StringComparison.Ordinal);
    }

    private static CliContext CreateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"intent-g697-{Guid.NewGuid():N}");
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

    private static void RecordExternal(CliContext context, string role, string reader, string frontend)
    {
        var result = RunWithResult(
            context,
            [
                "session-layer", "topology", "record", "--domain", Domain, "--team", Team, "--role", role,
                "--resident", "external", "--reader", reader, "--frontend", frontend, "--write", "--format", "json",
            ]);
        Assert.Equal(0, result.ExitCode);
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
