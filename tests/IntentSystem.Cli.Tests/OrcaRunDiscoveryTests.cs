using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC5: read-only orca-runs discovery across mixed host fixtures.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunDiscoveryTests : IDisposable
{
    private readonly string hostRoot = Directory.CreateTempSubdirectory("orca-run-discovery-host-").FullName;

    public OrcaRunDiscoveryTests() => OrcaRunTestSupport.ClearFakeLogs();

    public void Dispose()
    {
        GuardedFileRead.ReadAllTextFactory = null;
        OrcaRunTestSupport.ResetSeams();
        if (Directory.Exists(hostRoot))
        {
            Directory.Delete(hostRoot, recursive: true);
        }
    }

    [Fact]
    public void OrcaRuns_HostFixtureListsBindingsUnreadableAndIgnoresNoise_G837()
    {
        using var five = CreateWorkspace("five", "delivery-five");
        five.InstallFiveSeatDeliveryFixture();
        five.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);

        using var solo = CreateWorkspace("solo", "solo-team");
        solo.InstallSoloFixture();
        solo.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);

        using var malformed = CreateWorkspace("malformed", "malformed-team");
        malformed.InstallFiveSeatDeliveryFixture();
        malformed.WriteTopologyOrcaRun("steward", "bad-id", "orca-push");

        using var unresolved = CreateWorkspace("unresolved", "unresolved-team");
        unresolved.InstallFiveSeatDeliveryFixture();
        unresolved.WriteTopologyOrcaRun("steward", OrcaRunTestSupport.RunId, "orca-push");
        unresolved.MakeTopologyWorkspaceIdAmbiguous();

        using var unparseable = CreateWorkspace("unparseable", "unparseable-team");
        Directory.CreateDirectory(Path.GetDirectoryName(unparseable.TopologyPath)!);
        File.WriteAllText(unparseable.TopologyPath, "{ not json");

        using var unreadable = CreateWorkspace("unreadable", "unreadable-team");
        unreadable.InstallFiveSeatDeliveryFixture();
        unreadable.WriteTopologyOrcaRun("steward", OrcaRunTestSupport.RunId, "orca-push");

        CopyWorkspaceIntoHost(five);
        CopyWorkspaceIntoHost(solo);
        CopyWorkspaceIntoHost(malformed);
        CopyWorkspaceIntoHost(unresolved);
        CopyWorkspaceIntoHost(unparseable);
        CopyWorkspaceIntoHost(unreadable);
        var unreadableHostTopology = NotifyRoleTopologyStore.ResolvePath(hostRoot, OrcaRunTestSupport.Domain, "unreadable-team");
        GuardedFileRead.ReadAllTextFactory = path =>
            string.Equals(path, unreadableHostTopology, StringComparison.Ordinal)
                ? throw new IOException("denied")
                : File.ReadAllText(path);

        var teamsObjectPath = Path.Combine(hostRoot, ".intent-cli/topology", OrcaRunTestSupport.Domain, "teams-object.json");
        Directory.CreateDirectory(Path.GetDirectoryName(teamsObjectPath)!);
        File.WriteAllText(teamsObjectPath,
            """
            {
              "domain": "intent-cli",
              "teams": {
                "teams-object": {
                  "workspace_id": "w1",
                  "roles": {
                    "steward": {
                      "resident": "external",
                      "reader": ".intent-cli/events/r.jsonl",
                      "frontend": "orca",
                      "orca_run": { "run_id": "run_aaaaaaaaaaaa", "receive_policy": "orca-push" }
                    }
                  }
                }
              }
            }
            """);

        var topologyRoot = Path.Combine(hostRoot, ".intent-cli/topology", OrcaRunTestSupport.Domain);
        File.WriteAllText(Path.Combine(topologyRoot, ".gitignore"), "*\n");
        File.WriteAllText(Path.Combine(topologyRoot, "noise.lock"), "held");
        File.WriteAllText(Path.Combine(topologyRoot, ".noise.tmp"), "tmp");
        File.WriteAllText(Path.Combine(hostRoot, ".intent-cli/role-pane-mapping.json"), "{}");

        var context = CreateHostContext();
        var (exitCode, result) = RunHostJson(context, "session-layer", "topology", "orca-runs", "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.Equal(OrcaRunTestSupport.DiscoverySummary, result.GetProperty("summary").GetString());
        Assert.Contains(result.GetProperty("bindings").EnumerateArray(), row => row.GetProperty("health").GetString() == "recorded");
        Assert.Contains(result.GetProperty("bindings").EnumerateArray(), row => row.GetProperty("health").GetString() == "orca-run-id-malformed");
        Assert.Contains(result.GetProperty("bindings").EnumerateArray(), row => row.GetProperty("health").GetString() == "topology-unresolved");
        Assert.Contains(result.GetProperty("unreadable_records").EnumerateArray(), row => row.GetProperty("cause").GetString() == "topology-file-unparseable");
        Assert.DoesNotContain(result.GetProperty("bindings").EnumerateArray(), row => row.GetProperty("team").GetString() == "no-binding-team");
        OrcaRunTestSupport.AssertOrcaLogEmpty();
    }

    [Fact]
    public void OrcaRuns_MixedDomainListsBothTeams_G837()
    {
        using var five = CreateWorkspace("mix-five", "mix-five");
        five.InstallFiveSeatDeliveryFixture();
        five.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);

        using var solo = CreateWorkspace("mix-solo", "mix-solo");
        solo.InstallSoloFixture();
        solo.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);

        CopyWorkspaceIntoHost(five);
        CopyWorkspaceIntoHost(solo);
        var context = CreateHostContext();
        var (exitCode, result) = RunHostJson(
            context, "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, exitCode);
        var teams = result.GetProperty("bindings").EnumerateArray().Select(row => row.GetProperty("team").GetString()).ToArray();
        Assert.Contains("mix-five", teams);
        Assert.Contains("mix-solo", teams);

        five.RunRecordOrcaRun("steward", OrcaRunTestSupport.RunId, OrcaRunTestSupport.RunId, "orca-push", write: true);
        solo.RunRecordOrcaRun("design", OrcaRunTestSupport.RunId, OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
    }

    [Fact]
    public void OrcaRuns_UnreadableTeamMode_ExitsZeroWithUnreadableRecord_G837()
    {
        using var solo = CreateWorkspace("tm-unread", "tm-unread");
        solo.InstallSoloFixture();
        solo.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        CopyWorkspaceIntoHost(solo);
        GuardedFileRead.ReadAllTextFactory = path =>
            string.Equals(path, TeamModeStore.ResolvePath(hostRoot), StringComparison.Ordinal)
                ? throw new UnauthorizedAccessException("denied")
                : File.ReadAllText(path);

        var (exitCode, result) = RunHostJson(CreateHostContext(), "session-layer", "topology", "orca-runs", "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.Equal("team-shape-unreadable", result.GetProperty("bindings")[0].GetProperty("health").GetString());
        Assert.Equal("team-mode-unreadable", result.GetProperty("unreadable_records")[0].GetProperty("cause").GetString());
    }

    private static OrcaRunTestSupport.OrcaRunWorkspace CreateWorkspace(string suffix, string team) =>
        new(suffix, team);

    private void CopyWorkspaceIntoHost(OrcaRunTestSupport.OrcaRunWorkspace workspace)
    {
        CopyDirectory(workspace.Root, hostRoot, skipRelativePaths: [TeamModeStore.RelativePath]);
        MergeTeamMode(workspace.Root);
    }

    private void MergeTeamMode(string sourceRoot)
    {
        var sourcePath = TeamModeStore.ResolvePath(sourceRoot);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var hostPath = TeamModeStore.ResolvePath(hostRoot);
        if (!File.Exists(hostPath))
        {
            File.Copy(sourcePath, hostPath);
            return;
        }

        using var source = JsonDocument.Parse(File.ReadAllText(sourcePath));
        using var host = JsonDocument.Parse(File.ReadAllText(hostPath));
        var entries = host.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetRawText())
            .Concat(source.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.GetRawText()))
            .Distinct()
            .Select(text => JsonDocument.Parse(text).RootElement)
            .ToArray();
        var mergedEntries = entries
            .OrderBy(entry => entry.GetProperty("domain").GetString(), StringComparer.Ordinal)
            .ThenBy(entry => entry.TryGetProperty("team", out var teamElement) && teamElement.ValueKind == JsonValueKind.String
                ? teamElement.GetString()
                : null, StringComparer.Ordinal)
            .Select(entry => JsonSerializer.Deserialize<object>(entry.GetRawText()))
            .ToArray();
        var merged = JsonSerializer.Serialize(new
        {
            schema_version = "1",
            entries = mergedEntries,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(hostPath, merged);
    }

    private static void CopyDirectory(string source, string destination, IReadOnlyList<string>? skipRelativePaths = null)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (skipRelativePaths is not null
                && skipRelativePaths.Any(path => string.Equals(relativePath, path, StringComparison.Ordinal)))
            {
                continue;
            }

            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private CliContext CreateHostContext() => new()
    {
        RepoRoot = hostRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = OrcaRunTestSupport.Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private static (int ExitCode, JsonElement Result) RunHostJson(CliContext context, params string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, context, writer);
        return (exitCode, OrcaRunTestSupport.Parse(writer.ToString()));
    }
}
