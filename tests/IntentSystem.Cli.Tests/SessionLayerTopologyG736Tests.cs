using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G736: host-state authority is an explicit topology declaration. These
/// tests use only test-owned routing roots and emit the record, validation,
/// and rendered discovery outputs that a host operator can verify.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyG736Tests(ITestOutputHelper output) : IDisposable
{
    private const string Domain = "g736-domain";
    private const string WorkspaceId = "wG736";
    private const string Envelope = "non-sandboxed-host-repository-write";

    [Fact]
    public void RecordHostState_EmitsRoleAndEnvelope_AndDeclaredTopologyValid_G736()
    {
        using var workspace = new TopologyWorkspace("declared");
        RecordFourRoleTopology(workspace);

        var (recordExit, recordOutput) = workspace.Run(
            "session-layer", "topology", "record-host-state",
            "--domain", Domain,
            "--team", workspace.Team,
            "--role", "design",
            "--envelope", Envelope,
            "--write", "--format", "json");

        Assert.Equal(0, recordExit);
        using var record = JsonDocument.Parse(recordOutput);
        Assert.Equal("design", record.RootElement.GetProperty("role").GetString());
        Assert.Equal(Envelope, record.RootElement.GetProperty("envelope").GetString());
        Assert.True(record.RootElement.GetProperty("applied").GetBoolean());

        var topologyJson = File.ReadAllText(workspace.TopologyPath);
        output.WriteLine("topology record file:");
        output.WriteLine(topologyJson);
        using (var topology = JsonDocument.Parse(topologyJson))
        {
            var hostState = topology.RootElement.GetProperty("host_state");
            Assert.Equal("design", hostState.GetProperty("role").GetString());
            Assert.Equal(Envelope, hostState.GetProperty("envelope").GetString());
        }

        var (validateExit, validateOutput) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", workspace.Team,
            "--format", "json");

        Assert.Equal(0, validateExit);
        output.WriteLine("declared validate result:");
        output.WriteLine(validateOutput);
        using var validation = JsonDocument.Parse(validateOutput);
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        Assert.Empty(validation.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("design", validation.RootElement.GetProperty("host_state").GetProperty("role").GetString());
        Assert.Equal(Envelope, validation.RootElement.GetProperty("host_state").GetProperty("envelope").GetString());

        var (showExit, showOutput) = workspace.Run(
            "session-layer", "topology", "show",
            "--domain", Domain,
            "--team", workspace.Team,
            "--format", "json");
        Assert.Equal(0, showExit);
        using var show = JsonDocument.Parse(showOutput);
        Assert.Equal("design", show.RootElement.GetProperty("host_state").GetProperty("role").GetString());
        Assert.Equal(Envelope, show.RootElement.GetProperty("host_state").GetProperty("envelope").GetString());
    }

    [Fact]
    public void LegacyFourRoleTopology_RemainsValid_ButReportsMissingCapacityBeforePublish_G736()
    {
        using var workspace = new TopologyWorkspace("legacy");
        workspace.WriteTopology(CreateFourRoleTopology(workspace.Team, designResident: "external", designKind: null));
        var before = File.ReadAllText(workspace.TopologyPath);

        var (validateExit, validateOutput) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", workspace.Team,
            "--format", "json");

        Assert.Equal(0, validateExit);
        output.WriteLine("legacy/all-sandboxed-equivalent validate result:");
        output.WriteLine(validateOutput);
        using var validation = JsonDocument.Parse(validateOutput);
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        var finding = Assert.Single(validation.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal(NotifyRoleTopologyStore.HostStateRoleMissingCause, finding.GetProperty("cause").GetString());
        Assert.True(finding.GetProperty("is_informational").GetBoolean());
        var message = finding.GetProperty("message").GetString()!;
        Assert.Contains("cannot perform required host-state workflow work", message, StringComparison.Ordinal);
        Assert.Contains("declaration alone does not supply a non-sandboxed participant", message, StringComparison.Ordinal);
        Assert.Contains("needs no migration", message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(workspace.TopologyPath));

        var (guideExit, guideJson) = workspace.RunGuide("json");
        Assert.Equal(0, guideExit);
        output.WriteLine("legacy rendered discovery guidance:");
        output.WriteLine(guideJson);
        using var guide = JsonDocument.Parse(guideJson);
        var discovery = guide.RootElement.GetProperty("host_state_discovery");
        Assert.Equal("missing-declaration", discovery.GetProperty("status").GetString());
        Assert.Contains("host-state-role-missing", discovery.GetProperty("route").GetString(), StringComparison.Ordinal);
        Assert.Contains("does not supply a non-sandboxed participant", discovery.GetProperty("route").GetString(), StringComparison.Ordinal);
        var preflightFindings = guide.RootElement
            .GetProperty("session_layer")
            .GetProperty("preflight")
            .GetProperty("scopes")
            .EnumerateArray()
            .SelectMany(scope => scope.GetProperty("findings").EnumerateArray())
            .ToArray();
        Assert.Contains(preflightFindings, finding =>
            finding.GetProperty("cause").GetString() == NotifyRoleTopologyStore.HostStateRoleMissingCause);

        var (markdownExit, markdown) = workspace.RunGuide("markdown");
        Assert.Equal(0, markdownExit);
        Assert.Contains("## Host-state topology discovery (G736)", markdown, StringComparison.Ordinal);
        Assert.Contains("missing-declaration", markdown, StringComparison.Ordinal);
        Assert.Contains("undeclared or ad-hoc", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void AllSandboxedTopology_EmitsMissingHostStateBeforePublish_G736()
    {
        using var workspace = new TopologyWorkspace("all-sandboxed");
        workspace.WriteTopology(CreateFourRoleTopology(workspace.Team, designResident: "herdr", designKind: "codex"));

        var (exitCode, validateOutput) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", workspace.Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        output.WriteLine("all-sandboxed validate result before publish:");
        output.WriteLine(validateOutput);
        using var validation = JsonDocument.Parse(validateOutput);
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        var finding = Assert.Single(validation.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal(NotifyRoleTopologyStore.HostStateRoleMissingCause, finding.GetProperty("cause").GetString());
        Assert.Contains("cannot perform required host-state workflow work", finding.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(validation.RootElement.TryGetProperty("host_state", out _));
    }

    [Theory]
    [InlineData("external", "claude")]
    [InlineData("herdr", "codex")]
    public void ResidentKindAndPlacementAlone_NeverAuthorizeHostState_G736(string designResident, string designKind)
    {
        using var workspace = new TopologyWorkspace($"inference-{designResident}-{designKind}");
        workspace.WriteTopology(CreateFourRoleTopology(workspace.Team, designResident, designKind));

        var (exitCode, outputText) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", workspace.Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        using var outputDocument = JsonDocument.Parse(outputText);
        var findings = outputDocument.RootElement.GetProperty("findings").EnumerateArray().ToArray();
        var finding = Assert.Single(findings);
        Assert.Equal(NotifyRoleTopologyStore.HostStateRoleMissingCause, finding.GetProperty("cause").GetString());
        Assert.False(outputDocument.RootElement.TryGetProperty("host_state", out _));
    }

    [Fact]
    public void UnknownDeclaredRole_IsRejectedWithoutTopologyMutation_G736()
    {
        using var workspace = new TopologyWorkspace("invalid");
        RecordFourRoleTopology(workspace);
        workspace.WriteTopology(workspace.TopologyWithHostState("not-a-role", Envelope));
        var before = File.ReadAllText(workspace.TopologyPath);

        var (exitCode, outputText) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", workspace.Team,
            "--format", "json");

        Assert.Equal(1, exitCode);
        using var validation = JsonDocument.Parse(outputText);
        var finding = Assert.Single(validation.RootElement.GetProperty("findings").EnumerateArray());
        Assert.Equal("host-state-invalid", finding.GetProperty("cause").GetString());
        Assert.Contains("not one uniquely recorded team role", finding.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(workspace.TopologyPath));
    }

    [Fact]
    public void DeclaredDesignHostState_IsDiscoveredAndQualifiedInRenderedGuidance_G736()
    {
        using var workspace = new TopologyWorkspace("guide-declared");
        RecordFourRoleTopology(workspace);
        Assert.Equal(0, workspace.Run(
            "session-layer", "topology", "record-host-state",
            "--domain", Domain, "--team", workspace.Team,
            "--role", "design", "--envelope", Envelope,
            "--write", "--format", "json").ExitCode);

        var (exitCode, guideJson) = workspace.RunGuide("json");
        Assert.Equal(0, exitCode);
        output.WriteLine("declared design rendered discovery guidance:");
        output.WriteLine(guideJson);
        using var guide = JsonDocument.Parse(guideJson);
        var discovery = guide.RootElement.GetProperty("host_state_discovery");
        Assert.Equal("declared", discovery.GetProperty("status").GetString());
        Assert.Equal("architect", discovery.GetProperty("role").GetString());
        Assert.Equal(Envelope, discovery.GetProperty("envelope").GetString());
        Assert.Contains("declared design host-state role is legitimate", discovery.GetProperty("route").GetString(), StringComparison.Ordinal);
        Assert.Contains("undeclared or ad-hoc", discovery.GetProperty("route").GetString(), StringComparison.Ordinal);
        Assert.Contains("design", guide.RootElement.GetProperty("role_boundary").GetProperty("host_state_duty_routing").GetString(), StringComparison.Ordinal);
        Assert.Contains(Envelope, guide.RootElement.GetProperty("role_boundary").GetProperty("host_state_duty_routing").GetString(), StringComparison.Ordinal);
        Assert.Contains("declared design host-state role is legitimate", guide.RootElement.GetProperty("design_handoff").GetProperty("autonomous_publish_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("does NOT ask design to perform routine workflow transitions through an undeclared or ad-hoc request", guide.RootElement.GetProperty("design_handoff").GetProperty("autonomous_publish_rule").GetString(), StringComparison.Ordinal);
    }

    private static void RecordFourRoleTopology(TopologyWorkspace workspace)
    {
        foreach (var (role, resident, pane, kind) in new[]
        {
            ("design", "external", "", ""),
            ("implementation", "herdr", "wG736:p2", "codex"),
            ("orchestration", "herdr", "wG736:p3", "codex"),
            ("review", "herdr", "wG736:p4", "claude"),
        })
        {
            var args = resident == "external"
                ? new[]
                {
                    "session-layer", "topology", "record", "--domain", Domain,
                    "--team", workspace.Team, "--role", role, "--resident", resident,
                    "--reader", $".intent-cli/events/{workspace.Team}.jsonl",
                    "--frontend", "test", "--write", "--format", "json",
                }
                : new[]
                {
                    "session-layer", "topology", "record", "--domain", Domain,
                    "--team", workspace.Team, "--role", role, "--resident", resident,
                    "--workspace-id", WorkspaceId, "--pane-id", pane, "--cwd", "/test-owned",
                    "--kind", kind, "--write", "--format", "json",
                };
            var result = workspace.Run(args);
            Assert.Equal(0, result.ExitCode);
        }

    }

    private static string CreateFourRoleTopology(string team, string designResident, string? designKind)
    {
        var roles = new JsonObject
        {
            ["design"] = designResident == "external"
                ? new JsonObject
                {
                    ["resident"] = designResident,
                    ["reader"] = $".intent-cli/events/{team}.jsonl",
                    ["kind"] = designKind,
                    ["placement"] = "outside-workspace",
                }
                : new JsonObject
                {
                    ["resident"] = designResident,
                    ["workspace_id"] = WorkspaceId,
                    ["pane_id"] = $"{WorkspaceId}:p1",
                    ["cwd"] = "/test-owned",
                    ["kind"] = designKind,
                    ["placement"] = "shared-workspace",
                },
            ["implementation"] = new JsonObject
            {
                ["resident"] = "herdr",
                ["workspace_id"] = WorkspaceId,
                ["pane_id"] = $"{WorkspaceId}:p2",
                ["cwd"] = "/test-owned",
                ["kind"] = "codex",
            },
            ["orchestration"] = new JsonObject
            {
                ["resident"] = "herdr",
                ["workspace_id"] = WorkspaceId,
                ["pane_id"] = $"{WorkspaceId}:p3",
                ["cwd"] = "/test-owned",
                ["kind"] = "codex",
            },
            ["review"] = new JsonObject
            {
                ["resident"] = "herdr",
                ["workspace_id"] = WorkspaceId,
                ["pane_id"] = $"{WorkspaceId}:p4",
                ["cwd"] = "/test-owned",
                ["kind"] = "claude",
            },
        };

        var root = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = team,
            ["workspace_id"] = WorkspaceId,
            ["roles"] = roles,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public void Dispose()
    {
    }

    private sealed class TopologyWorkspace : IDisposable
    {
        public TopologyWorkspace(string suffix)
        {
            RootPath = Directory.CreateTempSubdirectory($"session-layer-topology-g736-{suffix}-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Team = $"g736-{suffix}";
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }
        public string Team { get; }
        public CliContext Context { get; }
        public string TopologyPath => NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);

        public (int ExitCode, string Output) Run(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public (int ExitCode, string Output) RunGuide(string format)
        {
            using var writer = new StringWriter();
            var exitCode = GuideOrchestratorThreadCommand.Execute(
                Context,
                [
                    "--domain", Domain,
                    "--team", Team,
                    "--target-repo", "J-Tech-Japan/intent-system",
                    "--agent", "codex",
                    "--format", format,
                ],
                writer);
            return (exitCode, writer.ToString());
        }

        public void WriteTopology(string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TopologyPath)!);
            File.WriteAllText(TopologyPath, json);
        }

        public string TopologyWithHostState(string role, string envelope) =>
            CreateTopologyWithHostState(role, envelope);

        private string CreateTopologyWithHostState(string role, string envelope)
        {
            var root = JsonNode.Parse(CreateFourRoleTopology(Team, "external", null))!.AsObject();
            root["host_state"] = new JsonObject { ["role"] = role, ["envelope"] = envelope };
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
