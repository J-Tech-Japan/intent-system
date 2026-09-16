using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 AC6: topology show and guide bootstrap Orca Run handover exposure.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunShowHandoverTests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("handover");

    public OrcaRunShowHandoverTests()
    {
    }

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void TopologyShow_AddsOrcaRunOnlyOnBoundRole_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);

        var (jsonExit, json) = workspace.RunJson(
            "session-layer", "topology", "show",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(0, jsonExit);
        foreach (var role in json.GetProperty("roles").EnumerateArray())
        {
            if (role.GetProperty("role").GetString() == "steward")
            {
                Assert.Equal(OrcaRunTestSupport.RunId, role.GetProperty("orca_run").GetProperty("run_id").GetString());
                Assert.Equal("recorded", role.GetProperty("orca_run").GetProperty("health").GetString());
            }
            else
            {
                Assert.False(role.TryGetProperty("orca_run", out _));
            }
        }

        var markdown = workspace.RunRaw(
            "session-layer", "topology", "show",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "markdown");
        Assert.Equal(0, markdown.ExitCode);
        Assert.Contains($"orca_run={OrcaRunTestSupport.RunId}/orca-push (recorded);", markdown.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_OrcaPushFiveSeat_CarriesExactLiterals_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);

        var markdown = RunBootstrapMarkdown();
        var expected =
            $"Orca Run mailbox (recorded, not verified against Orca): {OrcaRunTestSupport.RunId} on seat 'steward' (orca-push). {OrcaRunTestSupport.ReceiveInstructionOrcaPush}";
        Assert.Contains(expected, markdown, StringComparison.Ordinal);

        var json = RunBootstrapJson();
        var binding = json.GetProperty("orca_run_binding");
        Assert.Equal("steward", binding.GetProperty("role").GetString());
        Assert.Equal("recorded", binding.GetProperty("health").GetString());
        Assert.Equal(OrcaRunTestSupport.RunId, binding.GetProperty("run_id").GetString());
        Assert.Equal("orca-push", binding.GetProperty("receive_policy").GetString());
        Assert.Equal(OrcaRunTestSupport.ReceiveInstructionOrcaPush, binding.GetProperty("receive_instruction").GetString());
    }

    [Fact]
    public void Bootstrap_InboxPullSolo_CarriesExactLiterals_G837()
    {
        workspace.InstallSoloFixture();
        workspace.RunRecordOrcaRun("design", "absent", OrcaRunTestSupport.RunId, "inbox-pull", frontend: "claude-app", write: true);

        var markdown = RunBootstrapMarkdown();
        var expected =
            $"Orca Run mailbox (recorded, not verified against Orca): {OrcaRunTestSupport.RunId} on seat 'design' (inbox-pull). {OrcaRunTestSupport.ReceiveInstructionInboxPull}";
        Assert.Contains(expected, markdown, StringComparison.Ordinal);

        var json = RunBootstrapJson();
        var binding = json.GetProperty("orca_run_binding");
        Assert.Equal(OrcaRunTestSupport.ReceiveInstructionInboxPull, binding.GetProperty("receive_instruction").GetString());
    }

    [Fact]
    public void Bootstrap_MalformedBinding_RendersNotUsableLine_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("steward", "bad-id", "orca-push");

        var markdown = RunBootstrapMarkdown();
        Assert.Contains(
            $"Recorded Orca Run binding for seat 'steward' is not usable (orca-run-id-malformed); run intent-cli session-layer topology validate --domain {OrcaRunTestSupport.Domain} --team {workspace.Team} --format json.",
            markdown,
            StringComparison.Ordinal);
        var json = RunBootstrapJson();
        Assert.False(json.GetProperty("orca_run_binding").TryGetProperty("run_id", out _));
    }

    [Fact]
    public void Bootstrap_TopologyUnresolved_RendersOrcaRunsLine_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("steward", OrcaRunTestSupport.RunId, "orca-push");
        workspace.MakeTopologyWorkspaceIdAmbiguous();

        var markdown = RunBootstrapMarkdown();
        Assert.Contains(
            $"Recorded Orca Run binding for seat 'steward' is not usable (topology-unresolved); the team topology does not resolve, so the shape is undecided. Run intent-cli session-layer topology orca-runs --domain {OrcaRunTestSupport.Domain} --format json for the cause, then repair the topology record.",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_NoBinding_IsByteIdenticalForRecordedShapes_G837()
    {
        AssertNoBindingByteIdentical(() => workspace.InstallFourSeatDeliveryFixture());
        using (var five = new OrcaRunTestSupport.OrcaRunWorkspace("handover-five"))
        {
            five.InstallFiveSeatDeliveryFixture();
            AssertNoBindingByteIdentical(() => { }, five);
        }

        using var solo = new OrcaRunTestSupport.OrcaRunWorkspace("handover-solo");
        solo.InstallSoloFixture();
        AssertNoBindingByteIdentical(() => { }, solo);
    }

    private void AssertNoBindingByteIdentical(Action setup, OrcaRunTestSupport.OrcaRunWorkspace? target = null)
    {
        var ws = target ?? workspace;
        using var baseline = new OrcaRunTestSupport.OrcaRunWorkspace("baseline");
        baseline.SetTeamMode(TeamMode.Delivery);
        if (ReferenceEquals(ws, workspace) && File.Exists(ws.TopologyPath))
        {
            // four-seat baseline already has topology from fixture install
        }

        setup();
        var baseMarkdown = CaptureBootstrap(ws, "markdown", withBinding: false);
        var baseJson = CaptureBootstrap(ws, "json", withBinding: false);
        var showMarkdown = ws.RunRaw(
            "session-layer", "topology", "show", "--domain", OrcaRunTestSupport.Domain, "--team", ws.Team, "--format", "markdown");
        var showJson = ws.RunRaw(
            "session-layer", "topology", "show", "--domain", OrcaRunTestSupport.Domain, "--team", ws.Team, "--format", "json");
        Assert.DoesNotContain("orca_run_binding", baseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Orca Run mailbox", baseMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("orca_run", showJson.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("orca_run=", showMarkdown.Output, StringComparison.Ordinal);
    }

    private string RunBootstrapMarkdown() => CaptureBootstrap(workspace, "markdown", withBinding: true);

    private JsonElement RunBootstrapJson() =>
        OrcaRunTestSupport.Parse(CaptureBootstrap(workspace, "json", withBinding: true));

    private static string CaptureBootstrap(OrcaRunTestSupport.OrcaRunWorkspace ws, string format, bool withBinding)
    {
        var (exitCode, output) = ws.RunRaw(
            "guide", "bootstrap",
            "--domain", OrcaRunTestSupport.Domain,
            "--team", ws.Team,
            "--target-repo", "J-Tech-Japan/intent-system",
            "--routing-root", ws.Root,
            "--format", format);
        Assert.Equal(0, exitCode);
        if (!withBinding)
        {
            Assert.DoesNotContain("orca_run_binding", output, StringComparison.Ordinal);
        }

        return output;
    }
}
