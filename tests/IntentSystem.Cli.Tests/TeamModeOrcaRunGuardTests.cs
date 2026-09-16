using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC8: team-mode lock, Orca Run binding guard, and interleaving hooks.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class TeamModeOrcaRunGuardTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("guard");

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void SoloBinding_SetDeliveryRefuses_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: orca-run-binding-present:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TopologyBinding_SetSoloConductorRefuses_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.SoloConductor, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: orca-run-binding-present:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterClear_SetApplies_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        workspace.RunRecordOrcaRun("design", OrcaRunTestSupport.RunId, "absent", write: true);
        var (exitCode, _) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void HeldLock_RefusesTeamModeLockBusy_G837()
    {
        workspace.InstallSoloFixture();
        using var held = OrcaRunTeamModeLock.TryAcquire(workspace.Root, out _);
        Assert.NotNull(held);
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: team-mode-lock-busy:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DryRunGuard_RefusesWithoutCreatingLocks_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        var locksPath = Path.Combine(workspace.Root, ".intent-cli/locks");
        if (Directory.Exists(locksPath))
        {
            Directory.Delete(locksPath, recursive: true);
        }

        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: orca-run-binding-present:", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(locksPath));
    }

    [Fact]
    public void WriteGuardRefusal_CreatesLocksDirectory_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        var locksPath = Path.Combine(workspace.Root, ".intent-cli/locks");
        if (Directory.Exists(locksPath))
        {
            Directory.Delete(locksPath, recursive: true);
        }

        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: orca-run-binding-present:", output, StringComparison.Ordinal);
        Assert.True(Directory.Exists(locksPath));
        Assert.True(File.Exists(Path.Combine(locksPath, ".gitignore")));
    }

    [Fact]
    public void HeldLockWithBoundTeam_RefusesLockBusyNotBindingPresent_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
        using var held = OrcaRunTeamModeLock.TryAcquire(workspace.Root, out _);
        Assert.NotNull(held);
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: team-mode-lock-busy:", output, StringComparison.Ordinal);
        Assert.DoesNotContain("orca-run-binding-present:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainWideRepeatDelivery_AppliesWhenBindingHealthStaysRecorded_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        workspace.WriteDomainWideTeamModeFile(TeamMode.Delivery);
        var (exitCode, result) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("changed").GetBoolean());
        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        Assert.Equal("recorded", runs.GetProperty("bindings")[0].GetProperty("health").GetString());
    }

    [Fact]
    public void DomainWideSoloConductorRefuses_TeamScopedDeliveryAllows_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        workspace.WriteDomainWideTeamModeFile(TeamMode.Delivery);
        var refused = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--mode", TeamMode.SoloConductor, "--write", "--format", "json");
        Assert.Equal(1, refused.ExitCode);

        workspace.SetTeamMode(TeamMode.Delivery);
        var allowed = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(0, allowed.ExitCode);
    }

    [Fact]
    public void InterleavingHook_TeamModeCasLost_G837()
    {
        workspace.InstallSoloFixture();
        var hookInvoked = false;
        TeamModeOrcaRunGuard.BeforeWriteHook = () =>
        {
            hookInvoked = true;
            File.WriteAllText(workspace.TeamModePath, File.ReadAllText(workspace.TeamModePath) + "\n");
        };
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.True(hookInvoked);
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: team-mode-cas-lost:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InterleavingHook_RecordOrcaRunBusyThenSetApplies_G837()
    {
        workspace.InstallSoloFixture();
        var hookInvoked = false;
        TeamModeOrcaRunGuard.BeforeWriteHook = () =>
        {
            hookInvoked = true;
            var refusal = workspace.RunRecordOrcaRunExpectFailure(
                "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
            Assert.Equal("record-lock-busy", refusal.GetProperty("cause").GetString());
            Assert.Equal("team-mode-lock", refusal.GetProperty("field").GetString());
        };
        var (exitCode, _) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.True(hookInvoked);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void AfterLockHook_RecordOrcaRunBetweenLockAndGuard_IsBlocked_G837()
    {
        workspace.InstallSoloFixture();
        var hookInvoked = false;
        TeamModeOrcaRunGuard.AfterLockHook = () =>
        {
            hookInvoked = true;
            var refusal = workspace.RunRecordOrcaRunExpectFailure(
                "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
            Assert.Equal("record-lock-busy", refusal.GetProperty("cause").GetString());
            Assert.Equal("team-mode-lock", refusal.GetProperty("field").GetString());
        };
        var (exitCode, _) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.True(hookInvoked);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void UnreadableAffectedTopology_AppendsPinnedSummarySentence_G837()
    {
        workspace.SetTeamMode(TeamMode.Delivery, domainWide: true);
        workspace.InstallFiveSeatDeliveryFixture();
        var unreadablePath = Path.Combine(workspace.Root, ".intent-cli/topology", OrcaRunTestSupport.Domain, "unreadable.json");
        Directory.CreateDirectory(Path.GetDirectoryName(unreadablePath)!);
        File.WriteAllText(unreadablePath, "{ not json");
        var unparseablePath = NotifyRoleTopologyStore.RelativePathFor(OrcaRunTestSupport.Domain, workspace.Team);
        GuardedFileRead.ReadAllTextFactory = path =>
            string.Equals(path, workspace.TopologyPath, StringComparison.Ordinal)
                ? throw new IOException("denied")
                : File.ReadAllText(path);

        var (dryExit, dry) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--format", "json");
        Assert.Equal(0, dryExit);
        var expectedPaths = new[]
        {
            ".intent-cli/topology/intent-cli/unreadable.json",
            unparseablePath,
        };
        var expectedSentence =
            $"Orca Run binding guard: {expectedPaths.Length} affected topology record(s) could not be read or parsed and were not checked: {string.Join(", ", expectedPaths.OrderBy(path => path, StringComparer.Ordinal))}.";
        Assert.EndsWith(expectedSentence, dry.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var (writeExit, write) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(0, writeExit);
        Assert.EndsWith(expectedSentence, write.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedTopologyBinding_GuardRefusesModeChange_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var root = JsonNode.Parse(File.ReadAllText(workspace.TopologyPath))!.AsObject();
        root["roles"]!["steward"]!.AsObject()["orca_run"] = new JsonObject();
        File.WriteAllText(workspace.TopologyPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.SoloConductor, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: orca-run-binding-present:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedSoloBinding_GuardRefusesModeChange_G837()
    {
        workspace.InstallSoloFixture();
        workspace.WriteSoloBinding(new
        {
            schema_version = "1",
            domain = OrcaRunTestSupport.Domain,
            team = workspace.Team,
            orca_run = new { role = "design", receive_policy = "inbox-pull", frontend = "claude-app" },
        });
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: orca-run-binding-present:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SetWithoutBinding_MatchesMainHeadDryRun_G837()
    {
        const string sharedTeam = "g837-guard-shared";
        using var baseline = new OrcaRunTestSupport.OrcaRunWorkspace("guard-base", sharedTeam);
        baseline.SetTeamMode(TeamMode.Delivery);
        var baseDry = baseline.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", sharedTeam, "--mode", TeamMode.Delivery, "--format", "json");
        using var current = new OrcaRunTestSupport.OrcaRunWorkspace("guard-current", sharedTeam);
        current.SetTeamMode(TeamMode.Delivery);
        var dry = current.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", sharedTeam, "--mode", TeamMode.Delivery, "--format", "json");
        Assert.Equal(baseDry.Output, dry.Output);
    }
}
