using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G592: the delivery mapping consumed fail-closed by notify has a canonical
/// writer, an aggregate validator, a no-send projection, and proactive health.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyG592Tests
{
    [Fact]
    public void Validate_FieldIncident_PaneAliasAndMissingResident_AreBothNamedInOneAnswer_G592()
    {
        using var workspace = new TopologyWorkspace();
        workspace.WriteRawTopology(
            """
            {
              "team": "intent-cli-dev",
              "workspace_id": "w1",
              "roles": {
                "implementation": {
                  "workspace_id": "w1",
                  "pane": "w1:p2"
                }
              }
            }
            """);

        var (exitCode, result) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--format", "json");

        Assert.Equal(1, exitCode);
        Assert.False(result.GetProperty("valid").GetBoolean());
        var findings = result.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding =>
            Assert.Equal("implementation", finding.GetProperty("role").GetString()));
        Assert.Contains(findings, finding => finding.GetProperty("field").GetString() == "resident");
        Assert.Contains(findings, finding => finding.GetProperty("field").GetString() == "pane_id");
        Assert.Contains("pane", findings.Single(finding =>
            finding.GetProperty("field").GetString() == "pane_id").GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReturnsEveryResidencePaneReaderAndWorkspaceProblem_G592()
    {
        using var workspace = new TopologyWorkspace();
        workspace.WriteRawTopology(
            """
            {
              "team": "intent-cli-dev",
              "workspace_id": "team-w",
              "roles": {
                "unsupported": { "resident": "somewhere" },
                "review": { "resident": "herdr", "workspace_id": "foreign-w" },
                "design": { "resident": "external", "reader": "../../outside.jsonl" }
              }
            }
            """);

        var (exitCode, result) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--format", "json");

        Assert.Equal(1, exitCode);
        var findings = result.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Contains(findings, finding => Is(finding, "unsupported", "resident"));
        Assert.Contains(findings, finding => Is(finding, "review", "pane_id"));
        Assert.Contains(findings, finding => Is(finding, "review", "workspace_id"));
        Assert.Contains(findings, finding => Is(finding, "design", "reader"));
        Assert.Equal(4, findings.Length);
    }

    [Fact]
    public void Validate_AbsentAndUnreadableFiles_ReturnMachineInvalidAnswers_G592()
    {
        using var workspace = new TopologyWorkspace();

        var (missingExit, missing) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--format", "json");
        Assert.Equal(1, missingExit);
        Assert.False(missing.GetProperty("valid").GetBoolean());
        Assert.Equal("topology-missing", missing.GetProperty("findings")[0].GetProperty("cause").GetString());

        workspace.WriteRawTopology("{ not json");
        var (unreadableExit, unreadable) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--format", "json");
        Assert.Equal(1, unreadableExit);
        Assert.False(unreadable.GetProperty("valid").GetBoolean());
        Assert.Equal("topology-unreadable", unreadable.GetProperty("findings")[0].GetProperty("cause").GetString());
    }

    [Fact]
    public void Record_WritesBothResidenceForms_IsIdempotent_AndRefusesConflict_G592()
    {
        using var workspace = new TopologyWorkspace();

        var (herdrExit, herdr) = workspace.Run(HerdrRecord("w1:p1"));
        Assert.Equal(0, herdrExit);
        Assert.True(herdr.GetProperty("applied").GetBoolean());
        var afterFirstWrite = File.ReadAllText(workspace.TopologyPath);

        var (repeatExit, repeat) = workspace.Run(HerdrRecord("w1:p1"));
        Assert.Equal(0, repeatExit);
        Assert.True(repeat.GetProperty("already_recorded").GetBoolean());
        Assert.False(repeat.GetProperty("applied").GetBoolean());
        Assert.Equal(afterFirstWrite, File.ReadAllText(workspace.TopologyPath));

        var (externalExit, external) = workspace.Run(
            "session-layer", "topology", "record",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--role", "design",
            "--resident", "external",
            "--reader", $".intent-cli/events/{TopologyWorkspace.Team}.jsonl",
            "--frontend", "claude-app",
            "--write", "--format", "json");
        Assert.Equal(0, externalExit);
        Assert.True(external.GetProperty("applied").GetBoolean());

        using (var recorded = JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath)))
        {
            Assert.Equal("intent-cli", recorded.RootElement.GetProperty("domain").GetString());
            var team = recorded.RootElement;
            Assert.Equal("w1", team.GetProperty("workspace_id").GetString());
            var roles = team.GetProperty("roles");
            var orchestration = roles.GetProperty("orchestration");
            Assert.Equal("herdr", orchestration.GetProperty("resident").GetString());
            Assert.Equal("w1:p1", orchestration.GetProperty("pane_id").GetString());
            Assert.Equal("/host", orchestration.GetProperty("cwd").GetString());
            Assert.Equal("codex", orchestration.GetProperty("kind").GetString());
            var design = roles.GetProperty("design");
            Assert.Equal("external", design.GetProperty("resident").GetString());
            Assert.Equal($".intent-cli/events/{TopologyWorkspace.Team}.jsonl",
                design.GetProperty("reader").GetString());
            Assert.Equal("claude-app", design.GetProperty("frontend").GetString());
        }

        var beforeConflict = File.ReadAllText(workspace.TopologyPath);
        var (conflictExit, conflict) = workspace.Run(HerdrRecord("w1:p9"));
        Assert.Equal(1, conflictExit);
        Assert.True(conflict.GetProperty("conflict").GetBoolean());
        Assert.False(conflict.GetProperty("applied").GetBoolean());
        Assert.Equal(beforeConflict, File.ReadAllText(workspace.TopologyPath));

        var (validateExit, validation) = workspace.Run(
            "session-layer", "topology", "validate",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--format", "json");
        Assert.Equal(0, validateExit);
        Assert.True(validation.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Show_UsesNotifyDeliveryTargetResolution_WithoutSending_G592()
    {
        using var workspace = new TopologyWorkspace();
        Assert.Equal(0, workspace.Run(HerdrRecord("w1:p1")).ExitCode);
        Assert.Equal(0, workspace.Run(
            "session-layer", "topology", "record",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--role", "design",
            "--resident", "external",
            "--reader", $".intent-cli/events/{TopologyWorkspace.Team}.jsonl",
            "--write", "--format", "json").ExitCode);

        var topology = NotifyRoleTopologyStore.Resolve(
            workspace.RootPath, "intent-cli", TopologyWorkspace.Team).Topology!;
        var paneTarget = NotifyRoleTopologyStore.ResolveDeliveryTarget(
            workspace.RootPath,
            topology,
            "orchestration");
        var readerTarget = NotifyRoleTopologyStore.ResolveDeliveryTarget(
            workspace.RootPath,
            topology,
            "design");

        var (showExit, show) = workspace.Run(
            "session-layer", "topology", "show",
            "--domain", "intent-cli",
            "--team", TopologyWorkspace.Team,
            "--format", "json");

        Assert.Equal(0, showExit);
        Assert.True(show.GetProperty("valid").GetBoolean());
        var roles = show.GetProperty("roles").EnumerateArray().ToArray();
        var orchestration = roles.Single(role => role.GetProperty("role").GetString() == "orchestration");
        var design = roles.Single(role => role.GetProperty("role").GetString() == "design");
        Assert.Equal(paneTarget.TargetKind, orchestration.GetProperty("delivery_target_kind").GetString());
        Assert.Equal(paneTarget.Target, orchestration.GetProperty("delivery_target").GetString());
        Assert.Equal(readerTarget.TargetKind, design.GetProperty("delivery_target_kind").GetString());
        Assert.Equal(readerTarget.Target, design.GetProperty("delivery_target").GetString());
        Assert.False(File.Exists(workspace.EventPath));
    }

    [Fact]
    public void AutomationDoctor_ReportsStaleTopologyBeforeNotify_G592()
    {
        using var workspace = new TopologyWorkspace();
        workspace.WriteRawTopology(
            """
            {
              "team": "intent-cli-dev",
              "workspace_id": "w1",
              "roles": { "implementation": { "pane": "w1:p2" } }
            }
            """);
        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null;

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationDoctorCommand.Execute(workspace.Context, ["--format", "json"], writer);
            var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;

            Assert.Equal(1, exitCode);
            Assert.Equal("invalid", result.TopologyHealth.Status);
            var findings = Assert.Single(result.TopologyHealth.Teams).Findings;
            Assert.Contains(findings, finding => finding.Role == "implementation" && finding.Field == "resident");
            Assert.Contains(findings, finding => finding.Role == "implementation" && finding.Field == "pane_id");
            Assert.Contains("session-layer topology validate", result.TopologyHealth.Summary, StringComparison.Ordinal);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
        }
    }

    [Fact]
    public void NotifyTopologyRefusal_NamesCanonicalTopologyRemedy_G592()
    {
        using var workspace = new TopologyWorkspace();
        var resolution = NotifyRoleTopologyStore.Resolve(workspace.RootPath, TopologyWorkspace.Team);

        Assert.False(resolution.Resolved);
        Assert.Contains("session-layer topology validate", resolution.Summary, StringComparison.Ordinal);
        Assert.Contains("session-layer topology record", resolution.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void AgentMessageGuidance_DocumentsCanonicalFailClosedTopologySurface_G592(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md"));

        Assert.Contains("session-layer topology record", content, StringComparison.Ordinal);
        Assert.Contains("session-layer topology validate", content, StringComparison.Ordinal);
        Assert.Contains("session-layer topology show", content, StringComparison.Ordinal);
        Assert.Contains("valid: true|false", content, StringComparison.Ordinal);
        Assert.Contains("idempotent", content, StringComparison.Ordinal);
        Assert.Contains("notify", content, StringComparison.Ordinal);
        Assert.Contains("automation doctor", content, StringComparison.Ordinal);
        Assert.Contains("herdr query", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback", content, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Is(JsonElement finding, string role, string field) =>
        string.Equals(finding.GetProperty("role").GetString(), role, StringComparison.Ordinal)
        && string.Equals(finding.GetProperty("field").GetString(), field, StringComparison.Ordinal);

    private static string[] HerdrRecord(string paneId) =>
    [
        "session-layer", "topology", "record",
        "--domain", "intent-cli",
        "--team", TopologyWorkspace.Team,
        "--role", "orchestration",
        "--resident", "herdr",
        "--workspace-id", "w1",
        "--pane-id", paneId,
        "--cwd", "/host",
        "--kind", "codex",
        "--write", "--format", "json",
    ];

    private sealed class TopologyWorkspace : IDisposable
    {
        public const string Team = "intent-cli-dev";

        public TopologyWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-layer-topology-g592-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        public string TopologyPath => NotifyRoleTopologyStore.ResolvePath(RootPath, Context.Config.Project.Domain, Team);
        public string EventPath => Path.Combine(RootPath, ".intent-cli", "events", "intent-cli", $"{Team}.jsonl");

        public void WriteRawTopology(string content)
        {
            if (content.StartsWith("{", StringComparison.Ordinal) && !content.Contains("{ not json", StringComparison.Ordinal))
            {
                content = content.Insert(content.IndexOf('{') + 1, "\n  \"domain\": \"intent-cli\",");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(TopologyPath)!);
            File.WriteAllText(TopologyPath, content);
        }

        public (int ExitCode, JsonElement Result) Run(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
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
