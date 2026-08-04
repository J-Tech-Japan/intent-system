using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class SessionLayerTopologyG604Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private readonly string root = Directory.CreateTempSubdirectory("session-layer-topology-g604-").FullName;

    [Fact]
    public void Record_TwoTeamsUseIndependentMachineLocalIgnoredFiles_G604()
    {
        RunGit("init", "-q");

        Assert.True(Record("intent-cli-dev", "orchestration", "w1:p1").Applied);
        Assert.True(Record("intent-cli-review", "review", "w2:p1").Applied);

        var first = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev");
        var second = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-review");
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.NotEqual(File.ReadAllText(first), File.ReadAllText(second));
        Assert.True(NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-dev").Resolved);
        Assert.True(NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-review").Resolved);
        Assert.True(File.Exists(NotifyRoleTopologyStore.ResolveLocalIgnorePath(root)));
        Assert.False(File.Exists(Path.Combine(root, ".gitignore")));
        Assert.Equal(string.Empty, RunGit("status", "--porcelain").Trim());
    }

    [Fact]
    public void Resolve_CopiedNewRecordAndDualLocationConflictFailClosed_G604()
    {
        Assert.True(Record("intent-cli-dev", "orchestration", "w1:p1").Applied);
        var source = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev");
        var copied = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-review");
        Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
        File.Copy(source, copied);

        var copiedResolution = NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-review");
        Assert.False(copiedResolution.Resolved);
        Assert.Equal("topology-identity-mismatch", copiedResolution.Cause);
        Assert.Contains("intent-cli-dev", copiedResolution.Summary, StringComparison.Ordinal);
        Assert.Contains("intent-cli-review", copiedResolution.Summary, StringComparison.Ordinal);

        File.Delete(copied);
        File.WriteAllText(NotifyRoleTopologyStore.ResolvePath(root),
            """
            { "team": "intent-cli-dev", "workspace_id": "other", "roles": {
                "orchestration": { "resident": "herdr", "workspace_id": "other", "pane_id": "other:p1" }
            }}
            """);
        var conflict = NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-dev");
        Assert.False(conflict.Resolved);
        Assert.Equal("topology-location-conflict", conflict.Cause);
        Assert.Contains(NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev"), conflict.Summary, StringComparison.Ordinal);
        Assert.Contains(NotifyRoleTopologyStore.ResolvePath(root), conflict.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacyOnlyWarnsAndModeOnlyPreflightIsConfigurationIncomplete_G604()
    {
        var legacyPath = NotifyRoleTopologyStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath,
            """
            { "team": "intent-cli-dev", "workspace_id": "w1", "roles": {
                "orchestration": { "resident": "herdr", "workspace_id": "w1", "pane_id": "w1:p1" }
            }}
            """);
        var compatibility = NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-dev");
        Assert.True(compatibility.Resolved);
        Assert.Contains(compatibility.Warnings, warning => warning.Contains("topology record", StringComparison.Ordinal));

        File.Delete(legacyPath);
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
        };
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(context,
            ["--domain", Domain, "--team", "intent-cli-dev", "--mode", "herdr-only", "--write", "--format", "json"], writer));
        var preflight = SessionLayerPreflight.Analyze(root, Domain, "intent-cli-dev");
        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, preflight.Verdict);
        var missing = Assert.Single(preflight.Scopes.Single().Findings, finding => finding.Cause == "topology-missing");
        Assert.Contains("topology record", missing.Message, StringComparison.Ordinal);
    }

    private SessionLayerTopologyRecordResult Record(string team, string role, string paneId) =>
        SessionLayerTopologyWriter.Record(root, new SessionLayerTopologyRecordRequest
        {
            Domain = Domain,
            Team = team,
            Role = role,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = paneId[..paneId.IndexOf(':', StringComparison.Ordinal)],
            PaneId = paneId,
            Cwd = "/machine-local",
            Kind = "codex",
            Write = true,
            Format = "json",
        });

    private string RunGit(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = string.Join(' ', arguments),
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
