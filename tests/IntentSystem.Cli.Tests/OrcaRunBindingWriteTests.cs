using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC1: four/five-seat record-orca-run write behavior.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunBindingWriteTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("write");

    public OrcaRunBindingWriteTests()
    {
        OrcaRunTestSupport.ClearFakeLogs();
    }

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void FiveSeat_DryRunPlansWithoutLockOrWrite_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var before = workspace.TopologyBytes();
        var beforePaths = workspace.RelativePathsUnderIntentCli().ToHashSet(StringComparer.Ordinal);

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: false));

        Assert.Equal(0, exitCode);
        Assert.Equal("dry-run", result.GetProperty("mode").GetString());
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.True(result.GetProperty("changed").GetBoolean());
        Assert.Equal("five-seat", result.GetProperty("team_shape").GetString());
        Assert.Equal("topology-role", result.GetProperty("binding_location").GetString());
        Assert.Contains(OrcaRunTestSupport.RecordedOnlySuffix, result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, workspace.TopologyBytes());
        workspace.AssertOnlyNewPaths([], beforePaths);
        OrcaRunTestSupport.AssertOrcaLogEmpty();
    }

    [Fact]
    public void FiveSeat_WriteAddsOnlyOrcaRun_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var beforeText = File.ReadAllText(workspace.TopologyPath);

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true));

        Assert.Equal(0, exitCode);
        Assert.Equal("write", result.GetProperty("mode").GetString());
        Assert.True(result.GetProperty("applied").GetBoolean());
        Assert.True(result.GetProperty("changed").GetBoolean());
        Assert.False(result.GetProperty("already_recorded").GetBoolean());
        var afterText = File.ReadAllText(workspace.TopologyPath);
        workspace.AssertTopologyDeepEqualsExceptOrcaRun(beforeText, afterText);
        using var after = JsonDocument.Parse(afterText);
        var orcaRun = after.RootElement.GetProperty("roles").GetProperty("steward").GetProperty("orca_run");
        Assert.Equal(OrcaRunTestSupport.RunId, orcaRun.GetProperty("run_id").GetString());
        Assert.Equal("orca-push", orcaRun.GetProperty("receive_policy").GetString());
        OrcaRunTestSupport.AssertOrcaLogEmpty();
    }

    [Fact]
    public void FiveSeat_RerunIsAlreadyRecorded_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        var before = File.ReadAllBytes(workspace.TopologyPath);

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", OrcaRunTestSupport.RunId, OrcaRunTestSupport.RunId, "orca-push", write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("already_recorded").GetBoolean());
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(workspace.TopologyPath));
    }

    [Fact]
    public void FiveSeat_NewAbsentClearsBinding_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", OrcaRunTestSupport.RunId, "absent", write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("applied").GetBoolean());
        using var topology = JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath));
        Assert.False(topology.RootElement.GetProperty("roles").GetProperty("steward").TryGetProperty("orca_run", out _));
    }

    [Fact]
    public void FiveSeat_PolicyOnlyChangeApplies_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", OrcaRunTestSupport.RunId, OrcaRunTestSupport.RunId, "orca-push", write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("already_recorded").GetBoolean());
    }

    [Fact]
    public void FourSeat_WriteOnDesignSeat_G837()
    {
        workspace.InstallFourSeatDeliveryFixture();
        var beforeText = File.ReadAllText(workspace.TopologyPath);

        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", write: true));

        Assert.Equal(0, exitCode);
        Assert.Equal("four-seat", result.GetProperty("team_shape").GetString());
        Assert.True(result.GetProperty("applied").GetBoolean());
        workspace.AssertTopologyDeepEqualsExceptOrcaRun(beforeText, File.ReadAllText(workspace.TopologyPath));
        using var after = JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath));
        var orcaRun = after.RootElement.GetProperty("roles").GetProperty("design").GetProperty("orca_run");
        Assert.Equal("inbox-pull", orcaRun.GetProperty("receive_policy").GetString());
    }

    [Fact]
    public void HandAuthoredVariant_DeepEqualsExceptOrcaRun_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var beforeText = File.ReadAllText(workspace.TopologyPath);
        var node = JsonNode.Parse(beforeText) as JsonObject;
        node!["hand_authored"] = "marker";
        File.WriteAllText(workspace.TopologyPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        workspace.AssertTopologyDeepEqualsExceptOrcaRun(
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            File.ReadAllText(workspace.TopologyPath));
    }
}
