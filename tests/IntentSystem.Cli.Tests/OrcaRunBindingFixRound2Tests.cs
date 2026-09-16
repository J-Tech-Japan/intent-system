using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 fix round 2: null orca_run, stale topology ignore repair, JSON contract pins.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class OrcaRunBindingFixRound2Tests : IDisposable
{
    private readonly OrcaRunTestSupport.OrcaRunWorkspace workspace = new("fix-round-2");

    public void Dispose() => workspace.Dispose();

    [Fact]
    public void NullOrcaRun_ReadTopologyCurrent_IsMalformed_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var root = JsonNode.Parse(File.ReadAllText(workspace.TopologyPath))!.AsObject();
        root["roles"]!["steward"]!.AsObject()["orca_run"] = null;
        File.WriteAllText(workspace.TopologyPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        var result = workspace.RunRecordOrcaRunExpectFailure(
            "steward", "absent", "absent", write: true);
        Assert.Equal("current-mismatch", result.GetProperty("cause").GetString());
        Assert.Contains("Recorded current is 'malformed'", result.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var cleared = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", "malformed", "absent", write: true));
        Assert.Equal(0, cleared.ExitCode);
        Assert.True(cleared.Result.GetProperty("applied").GetBoolean());
        using var topology = JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath));
        Assert.False(topology.RootElement.GetProperty("roles").GetProperty("steward").TryGetProperty("orca_run", out _));
    }

    [Fact]
    public void NullOrcaRun_TopologyValidate_OrcaRuns_Show_Agree_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var root = JsonNode.Parse(File.ReadAllText(workspace.TopologyPath))!.AsObject();
        root["roles"]!["steward"]!.AsObject()["orca_run"] = null;
        File.WriteAllText(workspace.TopologyPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        var (validateExit, validate) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        var finding = validate.GetProperty("findings").EnumerateArray()
            .Single(item => item.GetProperty("cause").GetString() == "orca-run-binding-malformed");
        Assert.Equal(
            "Role 'steward' orca_run is absent, null, not an object, or has a field of the wrong type.",
            finding.GetProperty("message").GetString());

        var (runsExit, runs) = workspace.RunJson(
            "session-layer", "topology", "orca-runs", "--domain", OrcaRunTestSupport.Domain, "--format", "json");
        Assert.Equal(0, runsExit);
        var row = runs.GetProperty("bindings").EnumerateArray().Single();
        Assert.Equal("orca-run-binding-malformed", row.GetProperty("health").GetString());

        var (_, show) = workspace.RunJson(
            "session-layer", "topology", "show",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        var steward = show.GetProperty("roles").EnumerateArray()
            .Single(role => role.GetProperty("role").GetString() == "steward");
        Assert.Equal(
            "orca-run-binding-malformed",
            steward.GetProperty("orca_run").GetProperty("health").GetString());
    }

    [Fact]
    public void TopologyRecord_RepairsStaleGitignore_G837()
    {
        workspace.SetTeamMode(TeamMode.Delivery);
        var ignorePath = Path.Combine(workspace.Root, ".intent-cli/topology/.gitignore");
        Directory.CreateDirectory(Path.GetDirectoryName(ignorePath)!);
        File.WriteAllText(ignorePath, "*.json\n");

        workspace.RecordHerdr("orchestration", "w1:p1");
        Assert.Equal("*\n", File.ReadAllText(ignorePath));
    }

    [Fact]
    public void TopologyValidate_MalformedRunId_MessageLiteral_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.WriteTopologyOrcaRun("steward", "bad-id", "orca-push");
        var (exitCode, result) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, exitCode);
        var finding = result.GetProperty("findings").EnumerateArray()
            .Single(item => item.GetProperty("cause").GetString() == "orca-run-id-malformed");
        Assert.Equal(
            "Role 'steward' orca_run run_id 'bad-id' does not match ^run_[0-9a-f]{12}$.",
            finding.GetProperty("message").GetString());
        Assert.NotEqual("orca-run-id-malformed", finding.GetProperty("message").GetString());
    }

    [Fact]
    public void TopologyValidate_MalformedObjectField_MessageLiteral_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var root = JsonNode.Parse(File.ReadAllText(workspace.TopologyPath))!.AsObject();
        root["roles"]!["steward"]!.AsObject()["orca_run"] = new JsonObject
        {
            ["run_id"] = null,
            ["receive_policy"] = "orca-push",
        };
        File.WriteAllText(workspace.TopologyPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        var (exitCode, result) = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, exitCode);
        var finding = result.GetProperty("findings").EnumerateArray()
            .Single(item => item.GetProperty("cause").GetString() == "orca-run-binding-malformed");
        Assert.Equal(
            "Role 'steward' orca_run is absent, null, not an object, or has a field of the wrong type.",
            finding.GetProperty("message").GetString());
    }

    [Fact]
    public void Bootstrap_OrcaRunBinding_JsonHasOnlyContractKeys_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        workspace.RunRecordOrcaRun("steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true);
        var (_, json) = workspace.RunJson(
            "guide", "bootstrap",
            "--domain", OrcaRunTestSupport.Domain,
            "--team", workspace.Team,
            "--target-repo", "J-Tech-Japan/intent-system",
            "--routing-root", workspace.Root,
            "--format", "json");
        var binding = json.GetProperty("orca_run_binding");
        Assert.Equal(
            new[] { "binding_location", "health", "receive_instruction", "receive_policy", "role", "run_id" },
            binding.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void RecordOrcaRun_JsonIncludesAllKeysOnSuccessAndRefusal_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var (successExit, success) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: true));
        Assert.Equal(0, successExit);
        AssertResultKeySet(success, conflict: false);

        var (refusalExit, refusal) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", "absent", OrcaRunTestSupport.RunId, "orca-push", write: false));
        Assert.Equal(1, refusalExit);
        Assert.Equal("current-mismatch", refusal.GetProperty("cause").GetString());
        AssertResultKeySet(refusal, conflict: true);

        var (earlyExit, early) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "absent", OrcaRunTestSupport.RunId, "orca-push", write: false));
        Assert.Equal(1, earlyExit);
        Assert.Equal("orca-run-id-malformed", early.GetProperty("cause").GetString());
        AssertResultKeySet(early, conflict: true);
    }

    [Fact]
    public void Solo_NewAbsentWhenNoFile_AlreadyRecorded_G837()
    {
        workspace.InstallSoloFixture();
        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "design", "absent", "absent", write: true));
        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("already_recorded").GetBoolean());
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.False(result.GetProperty("changed").GetBoolean());
        Assert.False(File.Exists(workspace.SoloBindingPath));
    }

    [Fact]
    public void FiveSeat_NewAbsentWhenNoBinding_AlreadyRecorded_G837()
    {
        workspace.InstallFiveSeatDeliveryFixture();
        var (exitCode, result) = workspace.RunJson(OrcaRunTestSupport.RecordOrcaRunArgs(
            workspace, "steward", "absent", "absent", write: true));
        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("already_recorded").GetBoolean());
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.False(result.GetProperty("changed").GetBoolean());
    }


    [Theory]
    [InlineData("null-run-id")]
    [InlineData("absent-orca-run")]
    public void Solo_MalformedBinding_MessageLiteral_G837(string shape)
    {
        workspace.InstallSoloFixture();
        object payload = shape == "null-run-id"
            ? new
            {
                schema_version = "1",
                domain = OrcaRunTestSupport.Domain,
                team = workspace.Team,
                orca_run = new { role = "design", run_id = (string?)null, receive_policy = "inbox-pull", frontend = "claude-app" },
            }
            : new
            {
                schema_version = "1",
                domain = OrcaRunTestSupport.Domain,
                team = workspace.Team,
            };
        workspace.WriteSoloBinding(payload);

        var (validateExit, validate) = workspace.RunJson(
            "team-mode", "validate", "--domain", OrcaRunTestSupport.Domain, "--team", workspace.Team, "--format", "json");
        Assert.Equal(1, validateExit);
        var finding = Assert.Single(
            validate.GetProperty("findings").EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty),
            text => text.Contains("orca-run-binding-malformed", StringComparison.Ordinal));
        Assert.Equal(
            "orca-run-binding-malformed: solo binding file orca_run is absent, null, not an object, or has a field of the wrong type.",
            finding);
    }

    private static void AssertResultKeySet(JsonElement result, bool conflict)
    {
        var expected = new[]
        {
            "already_recorded", "applied", "binding_location", "canonical_role", "cause", "changed",
            "conflict", "current", "domain", "field", "fix", "frontend", "mode", "new", "operation",
            "receive_policy", "record_path", "role", "summary", "team", "team_mode", "team_shape",
        };
        Assert.Equal(expected, result.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.Equal(conflict, result.GetProperty("conflict").GetBoolean());
        if (conflict)
        {
            Assert.True(result.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.String);
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, result.GetProperty("cause").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("field").ValueKind);
            Assert.Equal(JsonValueKind.Null, result.GetProperty("fix").ValueKind);
        }
    }
}
