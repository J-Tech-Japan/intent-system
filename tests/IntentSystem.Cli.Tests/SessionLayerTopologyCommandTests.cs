using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyCommandTests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "g756-team";
    private const string WorkspaceId = "w-g756";
    // Captured from the parent/pre-change 6ea81ac85e5fc104d5cd954766c916445f751183
    // with the same healthy external-reader fixture. Keep the complete raw
    // payload here so compatibility covers ordering, indentation, escaping,
    // and the trailing newline—not only parsed JSON meaning.
    private static readonly string ParentHealthyValidationJson = string.Join(
        Environment.NewLine,
        [
            "{",
            "  \"valid\": true,",
            "  \"team\": \"g756-team\",",
            "  \"record_path\": \".intent-cli/topology/intent-cli/g756-team.json\",",
            "  \"findings\": [],",
            "  \"role_declarations\": [",
            "    {",
            "      \"role\": \"design\"",
            "    },",
            "    {",
            "      \"role\": \"orchestration\"",
            "    }",
            "  ],",
            "  \"host_state\": {",
            "    \"role\": \"orchestration\",",
            "    \"envelope\": \"test-owned-host-state\"",
            "  },",
            "  \"summary\": \"Recorded delivery topology for team \\u0027g756-team\\u0027 is valid. Model and reasoning effort are operator declarations, not measurements.\"",
            "}"
        ]) + Environment.NewLine;
    private readonly string root = Directory.CreateTempSubdirectory("session-layer-topology-g756-").FullName;

    [Fact]
    public void Validate_LegacyReaderDivergence_IsAdvisoryAndKeepsExitCode_G756()
    {
        const string recordedReader = ".intent-cli/events/g756-team.jsonl";
        WriteTopology(recordedReader);
        Directory.CreateDirectory(Path.GetDirectoryName(ScopedEventPath)!);
        File.WriteAllText(ScopedEventPath, string.Empty);

        var (exitCode, result) = Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("valid").GetBoolean());
        var finding = Assert.Single(result.GetProperty("findings").EnumerateArray(), item =>
            item.GetProperty("cause").GetString() == "reader-path-divergence");
        Assert.True(finding.GetProperty("is_informational").GetBoolean());
        var message = finding.GetProperty("message").GetString()!;
        Assert.Contains(recordedReader, message, StringComparison.Ordinal);
        Assert.Contains(ScopedEventPath, message, StringComparison.Ordinal);
        Assert.Contains("delivered", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_SameScopedReader_EmitsNoReaderFinding_G756()
    {
        var recordedReader = $".intent-cli/events/{Domain}/{Team}.jsonl";
        WriteTopology(recordedReader);
        Directory.CreateDirectory(Path.GetDirectoryName(ScopedEventPath)!);
        File.WriteAllText(ScopedEventPath, string.Empty);
        var before = File.ReadAllBytes(TopologyPath);

        var (exitCode, raw) = RunRaw(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");
        using var document = JsonDocument.Parse(raw);
        var result = document.RootElement.Clone();

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("valid").GetBoolean());
        Assert.DoesNotContain(
            result.GetProperty("findings").EnumerateArray(),
            item => item.GetProperty("cause").GetString() == "reader-path-divergence");
        Assert.Equal(ParentHealthyValidationJson, raw);
        Assert.Equal(before, File.ReadAllBytes(TopologyPath));
    }

    [Fact]
    public void Show_LegacyReader_ReportsRecordedAndEffectivePaths_G756()
    {
        const string recordedReader = ".intent-cli/events/g756-team.jsonl";
        WriteTopology(recordedReader);
        Directory.CreateDirectory(Path.GetDirectoryName(ScopedEventPath)!);
        File.WriteAllText(ScopedEventPath, string.Empty);

        var (exitCode, result) = Run(
            "session-layer", "topology", "show",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("valid").GetBoolean());
        var design = Assert.Single(result.GetProperty("roles").EnumerateArray(), item =>
            item.GetProperty("role").GetString() == "design");
        Assert.Equal(recordedReader, design.GetProperty("reader").GetString());
        Assert.Equal(ScopedEventPath, design.GetProperty("effective_reader").GetString());
        Assert.DoesNotContain(
            result.GetProperty("roles").EnumerateArray(),
            item => item.GetProperty("role").GetString() == "orchestration"
                && item.TryGetProperty("effective_reader", out _));
    }

    [Fact]
    public void Show_CustomReader_RemainsVerbatimAndHasNoFinding_G756()
    {
        const string customReader = ".intent-cli/events/custom-g756.jsonl";
        WriteTopology(customReader);

        var (showExit, show) = Run(
            "session-layer", "topology", "show",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");
        var (validateExit, validation) = Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, showExit);
        Assert.Equal(0, validateExit);
        var design = Assert.Single(show.GetProperty("roles").EnumerateArray(), item =>
            item.GetProperty("role").GetString() == "design");
        Assert.Equal(customReader, design.GetProperty("reader").GetString());
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, customReader.Replace('/', Path.DirectorySeparatorChar))),
            design.GetProperty("effective_reader").GetString());
        Assert.DoesNotContain(
            show.GetProperty("findings").EnumerateArray(),
            item => item.GetProperty("cause").GetString() == "reader-path-divergence");
        Assert.DoesNotContain(
            validation.GetProperty("findings").EnumerateArray(),
            item => item.GetProperty("cause").GetString() == "reader-path-divergence");
    }

    [Fact]
    public void Show_HerdrRole_GainsNoReaderFieldsOrFinding_G756()
    {
        WriteHerdrOnlyTopology();

        var (showExit, show) = Run(
            "session-layer", "topology", "show",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");
        var (validateExit, validation) = Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, showExit);
        Assert.Equal(0, validateExit);
        var orchestration = Assert.Single(show.GetProperty("roles").EnumerateArray());
        Assert.Equal("orchestration", orchestration.GetProperty("role").GetString());
        Assert.False(orchestration.TryGetProperty("reader", out _));
        Assert.False(orchestration.TryGetProperty("effective_reader", out _));
        Assert.Empty(show.GetProperty("findings").EnumerateArray());
        Assert.Empty(validation.GetProperty("findings").EnumerateArray());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationMirrors_DescribeEffectiveReaderAndAdvisoryDivergence_G756(string language)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "05-implementation-loop.md");
        var content = File.ReadAllText(path);

        Assert.Contains("effective_reader", content, StringComparison.Ordinal);
        Assert.Contains("reader-path-divergence", content, StringComparison.Ordinal);
        Assert.Contains("legacy", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("custom", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateResidence_ExternalToHerdr_RemovesExternalOnlyFields_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");

        var (exitCode, result) = Run(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "external",
            "--new-resident", "herdr",
            "--workspace-id", WorkspaceId,
            "--pane-id", $"{WorkspaceId}:p2",
            "--cwd", "/new-design",
            "--kind", "codex",
            "--delivery-method", "inline",
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.Equal("external", result.GetProperty("current_resident").GetString());
        Assert.Equal("herdr", result.GetProperty("new_resident").GetString());
        Assert.True(result.GetProperty("applied").GetBoolean());

        var role = ReadRole("design");
        Assert.Equal("herdr", role.GetProperty("resident").GetString());
        Assert.Equal(WorkspaceId, role.GetProperty("workspace_id").GetString());
        Assert.Equal($"{WorkspaceId}:p2", role.GetProperty("pane_id").GetString());
        Assert.Equal("/new-design", role.GetProperty("cwd").GetString());
        Assert.False(role.TryGetProperty("reader", out _));
        Assert.False(role.TryGetProperty("frontend", out _));
    }

    [Fact]
    public void UpdateResidence_HerdrToExternal_RemovesHerdrOnlyFields_G761()
    {
        WriteHerdrDesignTopology();

        var (exitCode, result) = Run(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "herdr",
            "--new-resident", "external",
            "--reader", ".intent-cli/events/new-design.jsonl",
            "--frontend", "design-web",
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("applied").GetBoolean());

        var role = ReadRole("design");
        Assert.Equal("external", role.GetProperty("resident").GetString());
        Assert.Equal(".intent-cli/events/new-design.jsonl", role.GetProperty("reader").GetString());
        Assert.Equal("design-web", role.GetProperty("frontend").GetString());
        Assert.False(role.TryGetProperty("workspace_id", out _));
        Assert.False(role.TryGetProperty("pane_id", out _));
        Assert.False(role.TryGetProperty("cwd", out _));
        Assert.False(role.TryGetProperty("kind", out _));
        Assert.False(role.TryGetProperty("delivery_method", out _));
    }

    [Fact]
    public void UpdateResidence_DryRun_IsByteIdentical_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");
        var before = File.ReadAllBytes(TopologyPath);

        var (exitCode, result) = Run(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "external",
            "--new-resident", "herdr",
            "--workspace-id", WorkspaceId,
            "--pane-id", $"{WorkspaceId}:p2",
            "--cwd", "/new-design",
            "--confirm-update-residence",
            "--dry-run",
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.Equal("dry-run", result.GetProperty("mode").GetString());
        Assert.False(result.GetProperty("applied").GetBoolean());
        Assert.True(result.GetProperty("changed").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(TopologyPath));
    }

    [Fact]
    public void UpdateResidence_WrongCurrent_RefusesWithoutMutation_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");
        var before = File.ReadAllBytes(TopologyPath);

        var (exitCode, raw) = RunRaw(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "herdr",
            "--new-resident", "external",
            "--reader", ".intent-cli/events/new-design.jsonl",
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(raw);
        Assert.True(document.RootElement.GetProperty("conflict").GetBoolean());
        Assert.Contains("not stated current residence", raw, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(TopologyPath));
    }

    [Fact]
    public void UpdateResidence_MissingConfirmation_RefusesWithoutMutation_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");
        var before = File.ReadAllBytes(TopologyPath);

        var (exitCode, raw) = RunRaw(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "external",
            "--new-resident", "herdr",
            "--workspace-id", WorkspaceId,
            "--pane-id", $"{WorkspaceId}:p2",
            "--cwd", "/new-design",
            "--write",
            "--format", "json");

        Assert.Equal(1, exitCode);
        Assert.Contains("--confirm-update-residence", raw, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(TopologyPath));
    }

    [Fact]
    public void UpdateResidence_EnforcesDestinationRequiredFields_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");
        var beforeExternal = File.ReadAllBytes(TopologyPath);

        var (herdrExit, herdrRaw) = RunRaw(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "external",
            "--new-resident", "herdr",
            "--workspace-id", WorkspaceId,
            "--pane-id", $"{WorkspaceId}:p2",
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(1, herdrExit);
        Assert.Contains("requires --workspace-id, --pane-id, and --cwd", herdrRaw, StringComparison.Ordinal);
        Assert.Equal(beforeExternal, File.ReadAllBytes(TopologyPath));

        WriteHerdrDesignTopology();
        var beforeHerdr = File.ReadAllBytes(TopologyPath);
        var (externalExit, externalRaw) = RunRaw(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "herdr",
            "--new-resident", "external",
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(1, externalExit);
        Assert.Contains("requires --reader", externalRaw, StringComparison.Ordinal);
        Assert.Equal(beforeHerdr, File.ReadAllBytes(TopologyPath));
    }

    [Fact]
    public void UpdateResidence_RejectsDestinationForbiddenFields_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");
        var beforeExternal = File.ReadAllBytes(TopologyPath);
        var (herdrExit, herdrRaw) = RunRaw(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "external",
            "--new-resident", "herdr",
            "--workspace-id", WorkspaceId,
            "--pane-id", $"{WorkspaceId}:p2",
            "--cwd", "/new-design",
            "--reader", ".intent-cli/events/old.jsonl",
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(1, herdrExit);
        Assert.Contains("does not accept --reader or --frontend", herdrRaw, StringComparison.Ordinal);
        Assert.Equal(beforeExternal, File.ReadAllBytes(TopologyPath));

        WriteHerdrDesignTopology();
        var beforeHerdr = File.ReadAllBytes(TopologyPath);
        var (externalExit, externalRaw) = RunRaw(
            "session-layer", "topology", "update-residence",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--current-resident", "herdr",
            "--new-resident", "external",
            "--reader", ".intent-cli/events/new-design.jsonl",
            "--workspace-id", WorkspaceId,
            "--confirm-update-residence",
            "--write",
            "--format", "json");

        Assert.Equal(1, externalExit);
        Assert.Contains("does not accept --workspace-id, --pane-id, --cwd", externalRaw, StringComparison.Ordinal);
        Assert.Equal(beforeHerdr, File.ReadAllBytes(TopologyPath));
    }

    [Theory]
    [InlineData("en", "the human answer from `guide bootstrap`")]
    [InlineData("ja", "`guide bootstrap` の human answer")]
    public void DocumentationMirrors_DescribeHumanControlledResidenceTransition_G761(string language, string wording)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md");
        var content = File.ReadAllText(path);

        Assert.Contains("topology update-residence", content, StringComparison.Ordinal);
        Assert.Contains("--current-resident", content, StringComparison.Ordinal);
        Assert.Contains("--new-resident", content, StringComparison.Ordinal);
        Assert.Contains("--confirm-update-residence", content, StringComparison.Ordinal);
        Assert.Contains(wording, content, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_StillRefusesConflictingShapeWithoutBypass_G761()
    {
        WriteTopology(".intent-cli/events/g756-team.jsonl");
        var before = File.ReadAllBytes(TopologyPath);

        var (exitCode, raw) = RunRaw(
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--resident", "external",
            "--reader", ".intent-cli/events/new-design.jsonl",
            "--write",
            "--format", "json");

        Assert.Equal(1, exitCode);
        Assert.Contains("Refusing to silently repair or replace it", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("--replace", raw, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(TopologyPath));
    }

    private void WriteTopology(string recordedReader)
    {
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = WorkspaceId,
            ["roles"] = new JsonObject
            {
                ["design"] = new JsonObject
                {
                    ["resident"] = "external",
                    ["reader"] = recordedReader,
                    ["frontend"] = "claude-app",
                },
                ["orchestration"] = HerdrRole(),
            },
            ["host_state"] = new JsonObject
            {
                ["role"] = "orchestration",
                ["envelope"] = "test-owned-host-state",
            },
        };
        WriteTopologyJson(topology);
    }

    private void WriteHerdrOnlyTopology()
    {
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = WorkspaceId,
            ["roles"] = new JsonObject { ["orchestration"] = HerdrRole() },
            ["host_state"] = new JsonObject
            {
                ["role"] = "orchestration",
                ["envelope"] = "test-owned-host-state",
            },
        };
        WriteTopologyJson(topology);
    }

    private void WriteHerdrDesignTopology()
    {
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = WorkspaceId,
            ["roles"] = new JsonObject
            {
                ["design"] = new JsonObject
                {
                    ["resident"] = "herdr",
                    ["workspace_id"] = WorkspaceId,
                    ["pane_id"] = $"{WorkspaceId}:p1",
                    ["cwd"] = "/old-design",
                    ["kind"] = "codex",
                    ["delivery_method"] = "inline",
                    ["model"] = "declared-model",
                    ["reasoning_effort"] = "high",
                },
                ["orchestration"] = HerdrRole(),
            },
            ["host_state"] = new JsonObject
            {
                ["role"] = "orchestration",
                ["envelope"] = "test-owned-host-state",
            },
        };
        WriteTopologyJson(topology);
    }

    private JsonElement ReadRole(string role)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TopologyPath));
        return document.RootElement.GetProperty("roles").GetProperty(role).Clone();
    }

    private static JsonObject HerdrRole() => new()
    {
        ["resident"] = "herdr",
        ["workspace_id"] = WorkspaceId,
        ["pane_id"] = $"{WorkspaceId}:p1",
        ["cwd"] = "/test-owned",
        ["kind"] = "codex",
    };

    private void WriteTopologyJson(JsonObject topology)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TopologyPath)!);
        File.WriteAllText(TopologyPath, topology.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private (int ExitCode, JsonElement Result) Run(params string[] args)
    {
        var (exitCode, raw) = RunRaw(args);
        using var document = JsonDocument.Parse(raw);
        return (exitCode, document.RootElement.Clone());
    }

    private (int ExitCode, string Raw) RunRaw(params string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
        return (exitCode, writer.ToString());
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
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

    private string TopologyPath => NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);

    private string ScopedEventPath => Path.Combine(
        root,
        ".intent-cli",
        "events",
        Domain,
        $"{Team}.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
