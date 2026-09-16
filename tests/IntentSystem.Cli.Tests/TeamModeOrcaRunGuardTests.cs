using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC8: team-mode lock, Orca Run binding guard, and interleaving hooks.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class TeamModeOrcaRunGuardTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("guard");

    public TeamModeOrcaRunGuardTests() => OrcaRunTestSupport.ClearFakeLogs();

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
        TeamModeOrcaRunGuard.BeforeWriteHook = () =>
        {
            File.WriteAllText(workspace.TeamModePath, File.ReadAllText(workspace.TeamModePath) + "\n");
        };
        var (exitCode, output) = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
        Assert.Equal(1, exitCode);
        Assert.Contains("team-mode-write-refused: team-mode-cas-lost:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InterleavingHook_RecordOrcaRunBusyThenSetApplies_G837()
    {
        workspace.InstallSoloFixture();
        TeamModeOrcaRunGuard.BeforeWriteHook = () =>
        {
            var refusal = workspace.RunRecordOrcaRunExpectFailure(
                "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);
            Assert.Equal("record-lock-busy", refusal.GetProperty("cause").GetString());
            Assert.Equal("team-mode-lock", refusal.GetProperty("field").GetString());
        };
        var (exitCode, _) = workspace.RunJson(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--write", "--format", "json");
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

        var dry = workspace.RunRaw(
            "team-mode", "set", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--mode", TeamMode.Delivery, "--format", "json");
        Assert.Equal(0, dry.ExitCode);
        Assert.Contains(OrcaRunTestSupport.GuardUnreadableSummaryPrefix, dry.Output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/topology/intent-cli/unreadable.json", dry.Output, StringComparison.Ordinal);
        Assert.Contains(unparseablePath, dry.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void SetWithoutBinding_IsByteIdenticalToBase_G837()
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
