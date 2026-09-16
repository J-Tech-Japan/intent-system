using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC2: solo-conductor record-orca-run and sidecar validation.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunBindingSoloTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("solo");

    public OrcaRunBindingSoloTests() => OrcaRunTestSupport.ClearFakeLogs();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void Solo_WriteCreatesOnlySidecarAndGitignore_G837()
    {
        workspace.InstallSoloFixture();
        var teamModeBefore = workspace.TeamModeBytes();

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace,
            "design",
            "absent",
            OrcaRunTestSupport.RunId,
            "inbox-pull",
            frontend: "claude-app",
            write: true));

        Assert.Equal(0, exitCode);
        Assert.Equal("orca-run-file", result.GetProperty("binding_location").GetString());
        Assert.True(File.Exists(workspace.SoloBindingPath));
        Assert.True(File.Exists(OrcaRunSoloStore.ResolveIgnorePath(workspace.Root)));
        Assert.Equal(teamModeBefore, workspace.TeamModeBytes());
        Assert.False(File.Exists(workspace.TopologyPath));
        OrcaRunTestSupport.AssertOrcaLogEmpty();
    }

    [Fact]
    public void Solo_NewAbsentDeletesFile_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        Assert.True(File.Exists(workspace.SoloBindingPath));

        var (exitCode, _) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "design", OrcaRunTestSupport.RunId, "absent", write: true));
        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(workspace.SoloBindingPath));
    }

    [Fact]
    public void Solo_HealthyPath_TeamModeValidateAndOrcaRunsRecorded_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        var baseValidate = CaptureTeamModeValidate();

        var (validateExit, validate) = workspace.RunJson(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(0, validateExit);
        Assert.True(validate.GetProperty("valid").GetBoolean());
        Assert.Equal(baseValidate, validate.ToString());

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        var row = runs.GetProperty("bindings").EnumerateArray().Single();
        Assert.Equal("recorded", row.GetProperty("health").GetString());
    }

    [Fact]
    public void Solo_AbsentOrcaRun_IsMalformedNotRecorded_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
        });

        AssertSoloMalformedSurfaces();
    }

    [Fact]
    public void Solo_NullOrcaRun_IsMalformedNotRecorded_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = (string?)null,
        });

        AssertSoloMalformedSurfaces();
    }

    [Fact]
    public void Solo_UnreadableSidecar_ReportsUnparseable_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new { role = "design", run_id = OrcaRunTestSupport.RunId, receive_policy = "inbox-pull", frontend = "claude-app" },
        });
        GuardedFileRead.ReadAllTextFactory = path =>
            string.Equals(path, workspace.SoloBindingPath, StringComparison.Ordinal)
                ? throw new UnauthorizedAccessException("permission denied")
                : File.ReadAllText(path);

        var (validateExit, validate) = workspace.RunJson(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        Assert.Contains("orca-run-file-unparseable", validate.ToString(), StringComparison.Ordinal);

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Empty(runs.GetProperty("bindings").EnumerateArray());
        var unreadable = Assert.Single(runs.GetProperty("unreadable_records").EnumerateArray());
        Assert.Equal("orca-run-file-unparseable", unreadable.GetProperty("cause").GetString());
    }

    [Fact]
    public void Solo_BrokenJsonSidecar_ReportsUnparseable_G837()
    {
        workspace.InstallSoloFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.SoloBindingPath)!);
        File.WriteAllText(workspace.SoloBindingPath, "{ not json");

        var (validateExit, validate) = workspace.RunJson(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        Assert.Contains("orca-run-file-unparseable", validate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Solo_UnsupportedSchemaVersion_ReportsSchemaUnsupported_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "2",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new { role = "design", run_id = OrcaRunTestSupport.RunId, receive_policy = "inbox-pull", frontend = "claude-app" },
        });

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("orca-run-file-schema-unsupported", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
        Assert.Empty(runs.GetProperty("unreadable_records").EnumerateArray());
    }

    [Fact]
    public void Solo_TeamOmittedValidate_IsByteIdenticalToBase_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        var baseValidate = CaptureTeamModeValidate(domainOnly: true);

        var (exitCode, validate) = workspace.RunJson(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.Equal(baseValidate, validate.ToString());
    }

    [Fact]
    public void Solo_UnreadableTeamMode_OrcaRunsAndValidateReportUnreadable_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        GuardedFileRead.ReadAllTextFactory = path =>
            string.Equals(path, workspace.TeamModePath, StringComparison.Ordinal)
                ? throw new IOException("io denied")
                : File.ReadAllText(path);

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("team-shape-unreadable", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
        Assert.Equal("team-mode-unreadable", runs.GetProperty("unreadable_records")[0].GetProperty("cause").GetString());

        var (validateExit, output) = workspace.RunRaw(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        Assert.Contains("could not be read", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Solo_MalformedBinding_KeepsTopologyAndBootstrapWorking_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new { role = "design", run_id = "bad", receive_policy = "inbox-pull", frontend = "claude-app" },
        });

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("orca-run-id-malformed", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
        var bootstrap = RunBootstrapMarkdown(workspace);
        Assert.Contains("is not usable (orca-run-id-malformed)", bootstrap, StringComparison.Ordinal);
    }

    private void AssertSoloMalformedSurfaces()
    {
        var (validateExit, validate) = workspace.RunJson(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        Assert.Contains("orca-run-binding-malformed", validate.ToString(), StringComparison.Ordinal);

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("orca-run-binding-malformed", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
        Assert.Null(runs.GetProperty("bindings")[0].GetProperty("run_id").GetString());

        var bootstrap = RunBootstrapMarkdown(workspace);
        Assert.Contains("is not usable (orca-run-binding-malformed)", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("Orca Run mailbox (recorded, not verified against Orca)", bootstrap, StringComparison.Ordinal);
    }

    private static string RunBootstrapMarkdown(OrcaRunTestSupport.OrcaRunWorkspace workspace)
    {
        var (exitCode, output) = workspace.RunRaw(
            "guide", "bootstrap",
            "--domain", OrcaRunTestSupport.Domain,
            "--team", workspace.Team,
            "--target-repo", "J-Tech-Japan/intent-system",
            "--routing-root", workspace.Root,
            "--format", "markdown");
        Assert.Equal(0, exitCode);
        return output;
    }

    private string CaptureTeamModeValidate(bool domainOnly = false)
    {
        using var empty = new OrcaRunTestSupport.OrcaRunWorkspace("solo-base", workspace.Team);
        empty.InstallSoloFixture();
        var args = domainOnly
            ? new[] { "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--format", "json" }
            : new[] { "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", empty.Team, "--format", "json" };
        var (_, result) = empty.RunJson(args);
        return result.ToString();
    }
}
