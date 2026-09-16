using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC4: alias roster binding seat selection without key merging.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunBindingAliasTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("alias");

    public OrcaRunBindingAliasTests() => OrcaRunTestSupport.ClearFakeLogs();

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void FiveSeat_StewardBinds_ArchitectRefused_G837()
    {
        workspace.InstallAliasFiveSeatFixture();

        var success = workspace.RunRecordOrcaRun(
            "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        Assert.Equal("steward", success.GetProperty("role").GetString());
        Assert.Equal("steward", success.GetProperty("canonical_role").GetString());

        var refusal = workspace.RunRecordOrcaRunExpectFailure(
            "architect", "absent", OrcaRunTestSupport.RunId, "inbox-pull", write: true);
        Assert.Equal("binding-seat-not-allowed", refusal.GetProperty("cause").GetString());
        Assert.Contains("five-seat", refusal.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("steward", refusal.GetProperty("summary").GetString(), StringComparison.Ordinal);

        using var topology = System.Text.Json.JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath));
        Assert.True(topology.RootElement.GetProperty("roles").TryGetProperty("design", out _));
        Assert.True(topology.RootElement.GetProperty("roles").TryGetProperty("architect", out _));
        Assert.True(topology.RootElement.GetProperty("roles").TryGetProperty("steward", out _));
    }

    [Fact]
    public void FourSeat_DesignBinds_ArchitectDuplicateRefused_G837()
    {
        workspace.InstallAliasFourSeatFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", write: true);

        var refusal = workspace.RunRecordOrcaRunExpectFailure(
            "architect", "absent", OrcaRunTestSupport.RunId, "inbox-pull", write: true);
        Assert.Equal("binding-duplicate", refusal.GetProperty("cause").GetString());
        Assert.Contains("design", refusal.GetProperty("summary").GetString(), StringComparison.Ordinal);
    }
}
