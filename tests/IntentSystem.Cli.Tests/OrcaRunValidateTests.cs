using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC7: binding-health table across validate, team-mode validate, and orca-runs.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunValidateTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("validate");

    public OrcaRunValidateTests() => OrcaRunTestSupport.ClearFakeLogs();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void TopologyValidate_AbsentBinding_IsInformational_G837()
    {
        workspace.InstallFourSeatDeliveryFixture();
        var (exitCode, result) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("valid").GetBoolean());
        var finding = Assert.Single(
            result.GetProperty("findings").EnumerateArray(),
            item => item.GetProperty("cause").GetString() == "orca-run-binding-absent");
        Assert.Equal("orca-run-binding-absent", finding.GetProperty("cause").GetString());
        Assert.Contains("intent-cli session-layer topology record-orca-run", finding.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TopologyValidate_UnrecordedMode_IsByteIdenticalToBase_G837()
    {
        workspace.InstallFourSeatDeliveryFixture();
        File.Delete(workspace.TeamModePath);
        var baseline = CaptureTopologyValidate(workspace);
        var (exitCode, result) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.Equal(baseline, result.ToString());
    }

    [Fact]
    public void TopologyValidate_StructuralError_SuppressesAbsentFinding_G837()
    {
        workspace.WriteRawTopology(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            workspace_id = "w1",
            host_state = new { role = "design", envelope = OrcaRunTestSupport.HostStateEnvelope },
            roles = new Dictionary<string, object>(),
        });
        workspace.SetTeamMode(TeamMode.Delivery);
        var (exitCode, result) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain(
            result.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("cause").GetString() == "orca-run-binding-absent");
    }

    [Fact]
    public void TopologyValidate_MalformedRunId_EmitsTvFinding_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("steward", "bad-id", "orca-push");
        var (exitCode, result) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains(
            result.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("cause").GetString() == "orca-run-id-malformed");
    }

    [Fact]
    public void Row9_MissingWorkspaceId_OrcaRunsTopologyUnresolved_ValidateKeepsStoreFindings_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("steward", OrcaRunTestSupport.RunId, "orca-push");
        workspace.MakeTopologyWorkspaceIdAmbiguous();

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("topology-unresolved", runs.GetProperty("bindings")[0].GetProperty("health").GetString());

        var (validateExit, validate) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        Assert.DoesNotContain(
            validate.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("cause").GetString() == "topology-unresolved");
        Assert.DoesNotContain(
            validate.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("cause").GetString() == "orca-run-binding-absent");
    }

    [Fact]
    public void Row9_WakeCommandOnHerdrNonBoundSeat_ValidateByteIdentical_OrcaRunsTopologyUnresolved_G837()
    {
        workspace.WriteRawTopology(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            workspace_id = "w1",
            host_state = new { role = "design", envelope = OrcaRunTestSupport.HostStateEnvelope },
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
                ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
                ["review"] = new
                {
                    resident = "herdr",
                    workspace_id = "w1",
                    pane_id = "w1:p3",
                    cwd = "/host",
                    delivery_method = "inline",
                    wake_command = "wake-sentinel --task t",
                },
                ["design"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
                ["steward"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "orca"),
            },
        });
        workspace.SetTeamMode(TeamMode.Delivery);
        workspace.WriteTopologyOrcaRun("steward", OrcaRunTestSupport.RunId, "orca-push");
        var baseline = workspace.CaptureTopologyValidateOutput();

        var (validateExit, validate) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(0, validateExit);
        Assert.Equal(baseline, validate.ToString());

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("topology-unresolved", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
    }

    [Fact]
    public void Row9PlusMalformedId_TopologyValidateStillEmitsRow3_G837()
    {
        workspace.WriteRawTopology(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            workspace_id = "w1",
            host_state = new { role = "design", envelope = OrcaRunTestSupport.HostStateEnvelope },
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
                ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
                ["review"] = new
                {
                    resident = "herdr",
                    workspace_id = "w1",
                    pane_id = "w1:p3",
                    cwd = "/host",
                    delivery_method = "inline",
                    wake_command = "wake-sentinel --task t",
                },
                ["design"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
                ["steward"] = new
                {
                    resident = "external",
                    reader = ".intent-cli/events/r.jsonl",
                    frontend = "orca",
                    orca_run = new { run_id = "bad-id", receive_policy = "orca-push" },
                },
            },
        });
        workspace.SetTeamMode(TeamMode.Delivery);

        var (validateExit, validate) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        Assert.Contains(
            validate.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("cause").GetString() == "orca-run-id-malformed");

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("orca-run-id-malformed", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
    }

    [Fact]
    public void TeamModeValidate_SoloEntryNotTeamScoped_G837()
    {
        workspace.WriteDomainWideTeamModeFile(TeamMode.SoloConductor);
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new { role = "design", run_id = OrcaRunTestSupport.RunId, receive_policy = "inbox-pull", frontend = "claude-app" },
        });
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-shape-solo-entry-not-team-scoped", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamModeValidate_StrandedSoloOnDelivery_ReportsBindingLocationNotAllowed_G837()
    {
        workspace.InstallFourSeatDeliveryFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new { role = "design", run_id = OrcaRunTestSupport.RunId, receive_policy = "inbox-pull", frontend = "claude-app" },
        });
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("binding-location-not-allowed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthOrder_MalformedIdPrecedesDisallowedSeat_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("design", "bad-id", "inbox-pull");
        var (exitCode, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, exitCode);
        var row = runs.GetProperty("bindings").EnumerateArray().First(binding => binding.GetProperty("role").GetString() == "design");
        Assert.Equal("orca-run-id-malformed", row.GetProperty("health").GetString());
        Assert.Contains("orca-run-id-malformed", row.GetProperty("findings").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("binding-seat-not-allowed", row.GetProperty("findings").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public void NotHealth_TopologyFileUnparseable_IsUnreadableRecordOnly_G837()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.TopologyPath)!);
        File.WriteAllText(workspace.TopologyPath, "{ not json");
        var (exitCode, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.Empty(runs.GetProperty("bindings").EnumerateArray());
        Assert.Equal("topology-file-unparseable", runs.GetProperty("unreadable_records")[0].GetProperty("cause").GetString());
    }

    private static string CaptureTopologyValidate(OrcaRunTestSupport.OrcaRunWorkspace workspace)
    {
        using var clone = new OrcaRunTestSupport.OrcaRunWorkspace("validate-base", workspace.Team);
        clone.InstallFourSeatDeliveryFixture();
        File.Delete(clone.TeamModePath);
        var (_, result) = clone.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", clone.Team, "--format", "json");
        return result.ToString();
    }
}
