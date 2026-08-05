using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
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

    [Fact]
    public void TopologyCommands_RequireExplicitNonDefaultDomainForRecordAndValidate_G604()
    {
        var context = CreateContext("sekiban-as-a-service");
        using var recordWriter = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRecord(context,
            ["--domain", Domain, "--team", "intent-cli-dev", "--role", "orchestration", "--resident", "herdr",
                "--workspace-id", "w1", "--pane-id", "w1:p1", "--cwd", "/machine-local", "--write", "--format", "json"],
            recordWriter));

        Assert.True(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev")));
        Assert.False(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, "sekiban-as-a-service", "intent-cli-dev")));

        using var validateWriter = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteValidate(context,
            ["--domain", Domain, "--team", "intent-cli-dev", "--format", "json"], validateWriter));
        using var validation = JsonDocument.Parse(validateWriter.ToString());
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains($"/{Domain}/intent-cli-dev.json", validation.RootElement.GetProperty("record_path").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DualLocationConflict_BlocksValidatePreflightAndAutomationDoctor_G604()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "orchestration", "w1:p1").Applied);
        var legacyPath = NotifyRoleTopologyStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath,
            """
            { "team": "intent-cli-dev", "workspace_id": "other", "roles": {
                "orchestration": { "resident": "herdr", "workspace_id": "other", "pane_id": "other:p1" }
            }}
            """);

        var context = CreateContext(Domain);
        using var modeWriter = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(context,
            ["--domain", Domain, "--team", team, "--mode", "herdr-only", "--write", "--format", "json"], modeWriter));
        using var validateWriter = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteValidate(context,
            ["--domain", Domain, "--team", team, "--format", "json"], validateWriter));
        using (var validate = JsonDocument.Parse(validateWriter.ToString()))
        {
            Assert.Contains(validate.RootElement.GetProperty("findings").EnumerateArray(), finding =>
                finding.GetProperty("cause").GetString() == "topology-location-conflict");
        }

        var preflight = SessionLayerPreflight.Analyze(root, Domain, team);
        Assert.False(preflight.Ready);
        Assert.Contains(preflight.Scopes.Single().Findings, finding => finding.Cause == "topology-location-conflict");

        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null;
        try
        {
            using var doctorWriter = new StringWriter();
            Assert.Equal(1, AutomationDoctorCommand.Execute(context, ["--format", "json"], doctorWriter));
            using var doctor = JsonDocument.Parse(doctorWriter.ToString());
            var findings = doctor.RootElement.GetProperty("topology_health").GetProperty("teams")[0]
                .GetProperty("findings").EnumerateArray();
            Assert.Contains(findings, finding => finding.GetProperty("cause").GetString() == "topology-location-conflict");
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
        }
    }

    [Fact]
    public void UpdateKind_RequiresMatchConfirmationAndOnlyChangesKind_G614()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var missingConfirmation = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "codex", "--new-kind", "copilot", "--write"], missingConfirmation));
        using var mismatch = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "claude", "--new-kind", "copilot", "--confirm-update-kind", "--write"], mismatch));
        Assert.Equal(before, File.ReadAllText(path));

        using var updated = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "codex", "--new-kind", "copilot", "--confirm-update-kind", "--write"], updated));
        using var beforeJson = JsonDocument.Parse(before);
        using var afterJson = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("copilot", afterJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("kind").GetString());
        Assert.Equal(beforeJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("pane_id").GetString(), afterJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("pane_id").GetString());
        Assert.Equal(beforeJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("cwd").GetString(), afterJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("cwd").GetString());
    }

    [Fact]
    public void RetireLegacy_RequiresCurrentRecordConfirmationAndEvidence_G614()
    {
        const string team = "intent-cli-dev";
        var context = CreateContext(Domain);
        using var missing = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--confirm-retire-legacy", "--write"], missing));

        Assert.True(Record(team, "review", "w1:p1").Applied);
        var legacy = NotifyRoleTopologyStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{ \"team\": \"intent-cli-dev\", \"workspace_id\": \"w1\", \"roles\": {} }");
        using var noConfirmation = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--write"], noConfirmation));
        Assert.True(File.Exists(legacy));
        using var retired = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--confirm-retire-legacy", "--write"], retired));
        Assert.False(File.Exists(legacy));
        Assert.Contains("fleet:zero4racer", File.ReadAllText(Path.Combine(root, ".intent-cli", "topology", "legacy-retirements.jsonl")), StringComparison.Ordinal);
    }

    private CliContext CreateContext(string defaultDomain) => new()
    {
        RepoRoot = root,
        Config = new CliConfig { Project = new ProjectConfig { Domain = defaultDomain, ArtifactRoot = ".intent-cli" } },
    };

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
