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

        var (exitCode, result) = Run(
            "session-layer", "topology", "validate",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("valid").GetBoolean());
        Assert.DoesNotContain(
            result.GetProperty("findings").EnumerateArray(),
            item => item.GetProperty("cause").GetString() == "reader-path-divergence");
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
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
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
