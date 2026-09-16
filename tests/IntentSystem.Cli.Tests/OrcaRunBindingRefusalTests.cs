using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC3: record-orca-run refusal order, fixes, and path side effects.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunBindingRefusalTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("refusal");

    public OrcaRunBindingRefusalTests() => OrcaRunTestSupport.ClearFakeLogs();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void Parse_MissingConfirm_PrintsUsageNotTeamModeGate_G837()
    {
        workspace.SetTeamMode(TeamMode.AuthoringOnly);
        var args = OrcaRunTestSupport.RecordOrcaRunArgs(workspace, "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true)
            .Where(arg => arg != "--confirm-record-orca-run")
            .ToArray();
        var (exitCode, output) = workspace.RunRaw(args);
        Assert.Equal(1, exitCode);
        Assert.Contains("record-orca-run", output, StringComparison.Ordinal);
        Assert.DoesNotContain("not-applicable-team-mode", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OrcaRunIdMalformed_NewField_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var before = workspace.TopologyBytes();
        var result = workspace.RunRecordOrcaRunExpectFailure("steward", "absent", "run_legacy_local", "orca-push");
        Assert.Equal("orca-run-id-malformed", result.GetProperty("cause").GetString());
        Assert.Equal("new", result.GetProperty("field").GetString());
        Assert.Equal(before, workspace.TopologyBytes());
    }

    [Fact]
    public void OrcaRunIdMalformed_CurrentField_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        var result = workspace.RunRecordOrcaRunExpectFailure("steward", "run_legacy_local", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("orca-run-id-malformed", result.GetProperty("cause").GetString());
        Assert.Equal("current", result.GetProperty("field").GetString());
    }

    [Fact]
    public void ReceivePolicyMissing_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure("steward", "absent", OrcaRunTestSupport.RunId);
        Assert.Equal("receive-policy-missing", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void ReceivePolicyNotAccepted_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", OrcaRunTestSupport.RunId, "absent", "orca-push", write: true);
        Assert.Equal("receive-policy-not-accepted", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void ReceivePolicyInvalid_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "bad-policy");
        Assert.Equal("receive-policy-invalid", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void RecordLockBusy_TeamModeLock_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var beforePaths = workspace.RelativePathsUnderIntentCli().ToHashSet(StringComparer.Ordinal);
        using var held = OrcaRunTeamModeLock.TryAcquire(workspace.Root, out _);
        Assert.NotNull(held);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("record-lock-busy", result.GetProperty("cause").GetString());
        Assert.Equal("team-mode-lock", result.GetProperty("field").GetString());
        workspace.AssertOnlyNewPaths(
        [
            ".intent-cli/locks/.gitignore",
            ".intent-cli/locks/team-mode.lock",
        ], beforePaths);
    }

    [Fact]
    public void TeamShapeUnreadable_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        GuardedFileRead.ReadAllTextFactory = path =>
            string.Equals(path, workspace.TeamModePath, StringComparison.Ordinal)
                ? throw new IOException("denied")
                : File.ReadAllText(path);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("team-shape-unreadable", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void TeamShapeUnrecorded_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        File.Delete(workspace.TeamModePath);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("team-shape-unrecorded", result.GetProperty("cause").GetString());
        Assert.Contains("intent-cli team-mode set", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TeamShapeNotBindable_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.SetTeamMode(TeamMode.AuthoringOnly);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("team-shape-not-bindable", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void TeamShapeSoloEntryNotTeamScoped_G837()
    {
        using var soloScope = new OrcaRunTestSupport.OrcaRunWorkspace("solo-scope", "solo-scope");
        soloScope.WriteDomainWideTeamModeFile(TeamMode.SoloConductor);
        var result = soloScope.RunRecordOrcaRunExpectFailure(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app");
        Assert.Equal("team-shape-solo-entry-not-team-scoped", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void TopologyUnresolved_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.MakeTopologyWorkspaceIdAmbiguous();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("topology-unresolved", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void TeamShapeUnrecognized_G837()
    {
        workspace.SetTeamMode(TeamMode.Delivery);
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
                ["design"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
            },
        });
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull");
        Assert.Equal("team-shape-unrecognized", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void RoleNotRecorded_G837()
    {
        workspace.InstallFourSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("role-not-recorded", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void BindingSeatNotAllowed_DesignOnFiveSeat_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull");
        Assert.Equal("binding-seat-not-allowed", result.GetProperty("cause").GetString());
        Assert.Contains("steward", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BindingSeatNotAllowed_OrchestrationOnFourSeat_G837()
    {
        workspace.InstallFourSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "orchestration", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("binding-seat-not-allowed", result.GetProperty("cause").GetString());
        Assert.Contains("architect", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BindingSeatNotAllowed_SoloReview_G837()
    {
        workspace.InstallSoloFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "review", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app");
        Assert.Equal("binding-seat-not-allowed", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void FrontendNotAccepted_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", frontend: "claude-app");
        Assert.Equal("frontend-not-accepted", result.GetProperty("cause").GetString());
        Assert.Contains("update-field", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendMissing_Solo_G837()
    {
        workspace.InstallSoloFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull");
        Assert.Equal("frontend-missing", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void ReceivePolicyHerdrSeat_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteRawTopology(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p1"),
                ["implementation"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p2"),
                ["review"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p3"),
                ["design"] = OrcaRunTestSupport.OrcaRunWorkspace.ExternalRoleFor(workspace.Team, "claude-app"),
                ["steward"] = OrcaRunTestSupport.OrcaRunWorkspace.HerdrRole("w1:p4"),
            },
        });
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("receive-policy-herdr-seat", result.GetProperty("cause").GetString());
        Assert.Contains("intent-cli session-layer topology update-residence", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.Contains("--current-resident herdr", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.Contains("--new-resident external", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.Contains("--frontend orca", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.Contains("--confirm-update-residence", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.Contains("--dry-run", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
        Assert.Contains("--format json", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReceivePolicySeatKindUnrecorded_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var text = File.ReadAllText(workspace.TopologyPath);
        text = text.Replace("\"frontend\": \"orca\"", "\"frontend\": \"unknown\"");
        File.WriteAllText(workspace.TopologyPath, text);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push");
        Assert.Equal("receive-policy-seat-kind-unrecorded", result.GetProperty("cause").GetString());
        Assert.Contains("intent-cli session-layer topology update-field --field frontend", result.GetProperty("fix").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReceivePolicySeatMismatch_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "inbox-pull");
        Assert.Equal("receive-policy-seat-mismatch", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void BindingDuplicate_G837()
    {
        workspace.InstallAliasFourSeatFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", write: true);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "architect", "absent", OrcaRunTestSupport.RunId, "inbox-pull", write: true);
        Assert.Equal("binding-duplicate", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void BindingLocationConflict_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteRawTopology(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["design"] = new
                {
                    resident = "external",
                    reader = ".intent-cli/events/r.jsonl",
                    frontend = "claude-app",
                    orca_run = new { run_id = OrcaRunTestSupport.RunId, receive_policy = "inbox-pull" },
                },
            },
        });
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        Assert.Equal("binding-location-conflict", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void BindingIdentityMismatch_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = "other",
            team = "other-team",
            orca_run = new { role = "design", run_id = OrcaRunTestSupport.RunId, receive_policy = "inbox-pull", frontend = "claude-app" },
        });
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        Assert.Equal("binding-identity-mismatch", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void CurrentMismatch_IncludingMalformed_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        var mismatch = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("current-mismatch", mismatch.GetProperty("cause").GetString());

        var text = File.ReadAllText(workspace.TopologyPath);
        text = text.Replace(OrcaRunTestSupport.RunId, "not-a-run-id");
        File.WriteAllText(workspace.TopologyPath, text);
        var malformed = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "malformed", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("current-mismatch", malformed.GetProperty("cause").GetString());
    }

    [Fact]
    public void RecordCasLost_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        OrcaRunRecordCommand.AfterTopologyLockHook = () => File.AppendAllText(workspace.TopologyPath, "\n");
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("record-cas-lost", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void RecordWriteFailed_G837()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        workspace.InstallFiveSeatDeliveryFixture();
        OrcaRunRecordCommand.AfterTopologyLockHook = () =>
            throw new IOException("write denied");
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("record-write-failed", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void Precedence_TeamModeLockBeforeUnrecordedMode_G837()
    {
        using var held = OrcaRunTeamModeLock.TryAcquire(workspace.Root, out _);
        Assert.NotNull(held);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("record-lock-busy", result.GetProperty("cause").GetString());
        Assert.Equal("team-mode-lock", result.GetProperty("field").GetString());
    }

    [Fact]
    public void Precedence_TopologyLockBeforeUnrecordedMode_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        File.Delete(workspace.TeamModePath);
        var topologyPath = workspace.TopologyPath;
        using var topologyLock = SessionLayerTopologyWriter.AcquireCasLockPublic(topologyPath);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("team-shape-unrecorded", result.GetProperty("cause").GetString());
    }

    [Fact]
    public void Precedence_TopologyLockBusyOnValidFiveSeat_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var beforePaths = workspace.RelativePathsUnderIntentCli().ToHashSet(StringComparer.Ordinal);
        using var topologyLock = SessionLayerTopologyWriter.AcquireCasLockPublic(workspace.TopologyPath);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("record-lock-busy", result.GetProperty("cause").GetString());
        Assert.Equal("topology-lock", result.GetProperty("field").GetString());
        workspace.AssertOnlyNewPaths(
        [
            ".intent-cli/locks/.gitignore",
            ".intent-cli/locks/team-mode.lock",
            ".intent-cli/topology/.gitignore",
            $".intent-cli/topology/{OrcaRunTestSupport.Domain}/{workspace.Team}.json.lock",
        ], beforePaths);
    }

    [Fact]
    public void Precedence_SoloSucceedsWithTopologyLockHeld_G837()
    {
        using var solo = new OrcaRunTestSupport.OrcaRunWorkspace("solo-lock", "solo-lock");
        solo.InstallSoloFixture();
        solo.WriteRawTopology(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = solo.Team,
            workspace_id = "w1",
            roles = new Dictionary<string, object>
            {
                ["design"] = solo.ExternalRole("claude-app"),
            },
        });
        using var topologyLock = SessionLayerTopologyWriter.AcquireCasLockPublic(solo.TopologyPath);
        solo.RunRecordOrcaRun(
            "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        Assert.True(File.Exists(solo.SoloBindingPath));
    }

    [Fact]
    public void RecordLockBusy_TopologyLock_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        using var topologyLock = SessionLayerTopologyWriter.AcquireCasLockPublic(workspace.TopologyPath);
        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("record-lock-busy", result.GetProperty("cause").GetString());
        Assert.Equal("topology-lock", result.GetProperty("field").GetString());
    }
}
