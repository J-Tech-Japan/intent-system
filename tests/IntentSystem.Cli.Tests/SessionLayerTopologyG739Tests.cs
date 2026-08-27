using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G739: topology roles may carry optional operator-declared model identity
/// and reasoning effort without turning those declarations into measurements
/// or changing the existing topology transition semantics.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyG739Tests
{
    private const string Domain = "intent-cli";
    private const string WorkspaceId = "wG739";

    [Fact]
    public void Record_PersistsFreeFormDeclarations_AndExactRepeatIsIdempotent_G739()
    {
        using var workspace = new TopologyWorkspace("idempotence");
        var first = workspace.RunJson(Record(
            workspace,
            "implementation",
            "wG739:p1",
            "/implementation",
            "vendor/model-experimental",
            "thinking-ultra"));

        Assert.True(first.GetProperty("applied").GetBoolean());
        using (var topology = JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath)))
        {
            var role = topology.RootElement.GetProperty("roles").GetProperty("implementation");
            Assert.Equal("vendor/model-experimental", role.GetProperty("model").GetString());
            Assert.Equal("thinking-ultra", role.GetProperty("reasoning_effort").GetString());
        }

        var afterFirstWrite = File.ReadAllBytes(workspace.TopologyPath);
        var repeat = workspace.RunJson(Record(
            workspace,
            "implementation",
            "wG739:p1",
            "/implementation",
            "vendor/model-experimental",
            "thinking-ultra"));

        Assert.True(repeat.GetProperty("already_recorded").GetBoolean());
        Assert.False(repeat.GetProperty("changed").GetBoolean());
        Assert.Equal(afterFirstWrite, File.ReadAllBytes(workspace.TopologyPath));

        var conflict = workspace.RunJson(
            Record(
                workspace,
                "implementation",
                "wG739:p1",
                "/implementation",
                "vendor/model-other",
                "thinking-ultra"),
            expectedExitCode: 1);
        Assert.True(conflict.GetProperty("conflict").GetBoolean());
        Assert.Contains("conflicting recorded shape", conflict.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(afterFirstWrite, File.ReadAllBytes(workspace.TopologyPath));
    }

    [Fact]
    public void ShowAndValidate_RenderDeclarations_WhileAbsentValuesRemainNonFailing_G739()
    {
        using var workspace = new TopologyWorkspace("render");
        var recorded = workspace.RunJson(Record(
            workspace,
            "implementation",
            "wG739:p1",
            "/implementation",
            "operator/model-alpha",
            "max"));
        Assert.True(recorded.GetProperty("applied").GetBoolean());
        workspace.RunJson(Record(
            workspace,
            "review",
            "wG739:p2",
            "/review"));

        var show = workspace.RunJson(
            "session-layer", "topology", "show",
            "--domain", Domain, "--team", workspace.Team, "--format", "json");
        var shownRoles = show.GetProperty("roles").EnumerateArray().ToArray();
        var implementation = Assert.Single(shownRoles, role => role.GetProperty("role").GetString() == "implementation");
        Assert.Equal("operator/model-alpha", implementation.GetProperty("model").GetString());
        Assert.Equal("max", implementation.GetProperty("reasoning_effort").GetString());
        var review = Assert.Single(shownRoles, role => role.GetProperty("role").GetString() == "review");
        Assert.False(review.TryGetProperty("model", out _));
        Assert.False(review.TryGetProperty("reasoning_effort", out _));
        Assert.Contains(
            SessionLayerTopologyDeclaredValueRules.OperatorDeclarationSummary,
            show.GetProperty("summary").GetString(),
            StringComparison.Ordinal);

        var validation = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", Domain, "--team", workspace.Team, "--format", "json");
        Assert.True(validation.GetProperty("valid").GetBoolean());
        var declarations = validation.GetProperty("role_declarations").EnumerateArray().ToArray();
        var validatedImplementation = Assert.Single(
            declarations,
            declaration => declaration.GetProperty("role").GetString() == "implementation");
        Assert.Equal("operator/model-alpha", validatedImplementation.GetProperty("model").GetString());
        Assert.Equal("max", validatedImplementation.GetProperty("reasoning_effort").GetString());
        Assert.Contains(
            SessionLayerTopologyDeclaredValueRules.OperatorDeclarationSummary,
            validation.GetProperty("summary").GetString(),
            StringComparison.Ordinal);

        var showMarkdown = workspace.RunRaw(
            "session-layer", "topology", "show",
            "--domain", Domain, "--team", workspace.Team, "--format", "markdown");
        Assert.Equal(0, showMarkdown.ExitCode);
        Assert.Contains("model=operator/model-alpha", showMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains("reasoning_effort=max", showMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains("model=absent", showMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains("reasoning_effort=absent", showMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains(
            SessionLayerTopologyDeclaredValueRules.OperatorDeclarationSummary,
            showMarkdown.Output,
            StringComparison.Ordinal);

        var validateMarkdown = workspace.RunRaw(
            "session-layer", "topology", "validate",
            "--domain", Domain, "--team", workspace.Team, "--format", "markdown");
        Assert.Equal(0, validateMarkdown.ExitCode);
        Assert.Contains("model=operator/model-alpha", validateMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains("reasoning_effort=max", validateMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains("model=absent", validateMarkdown.Output, StringComparison.Ordinal);
        Assert.Contains(
            SessionLayerTopologyDeclaredValueRules.OperatorDeclarationSummary,
            validateMarkdown.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Declarations_AreBoundedButNotEnumerated_G739()
    {
        using var workspace = new TopologyWorkspace("bounds");
        var tooLong = new string('x', SessionLayerTopologyDeclaredValueRules.MaxLength + 1);
        var rejected = workspace.RunRaw(
            "session-layer", "topology", "record",
            "--domain", Domain, "--team", workspace.Team, "--role", "implementation",
            "--resident", "herdr", "--workspace-id", WorkspaceId, "--pane-id", "wG739:p1",
            "--cwd", "/implementation", "--model", tooLong, "--dry-run", "--format", "json");

        Assert.Equal(1, rejected.ExitCode);
        Assert.Contains($"at most {SessionLayerTopologyDeclaredValueRules.MaxLength} characters", rejected.Output, StringComparison.Ordinal);

        var accepted = workspace.RunJson(Record(
            workspace,
            "implementation",
            "wG739:p1",
            "/implementation",
            "provider:model/with-any-free-form-name",
            "effort-level-that-is-not-an-enum"));
        Assert.True(accepted.GetProperty("applied").GetBoolean());
    }

    [Fact]
    public void LegacyFixture_RoundTripsByteIdenticallyWhenDeclarationsAreOmitted_G739()
    {
        using var workspace = new TopologyWorkspace("legacy");
        var fixturePath = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "tests",
            "IntentSystem.Cli.Tests",
            "Fixtures",
            "g739-legacy-topology.json");
        var fixtureBytes = File.ReadAllBytes(fixturePath);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.TopologyPath)!);
        File.WriteAllBytes(workspace.TopologyPath, fixtureBytes);
        var before = File.ReadAllBytes(workspace.TopologyPath);

        var request = new SessionLayerTopologyRecordRequest
        {
            Domain = Domain,
            Team = workspace.Team,
            Role = "implementation",
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = WorkspaceId,
            PaneId = "wG739:p1",
            Cwd = "/legacy-implementation",
            Kind = "codex",
            DeliveryMethod = "inline",
            Model = null,
            ReasoningEffort = null,
            Write = true,
            Format = "json",
        };

        var result = SessionLayerTopologyWriter.Record(workspace.RootPath, request);
        Assert.False(result.Conflict);
        Assert.True(result.AlreadyRecorded);
        Assert.False(result.Changed);
        Assert.Equal(before, File.ReadAllBytes(workspace.TopologyPath));

        var validation = workspace.RunJson(
            "session-layer", "topology", "validate",
            "--domain", Domain, "--team", workspace.Team, "--format", "json");
        Assert.True(validation.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void Move_PreservesDeclarationsAndUsesExistingWholeTeamSemantics_G739()
    {
        using var workspace = new TopologyWorkspace("move");
        workspace.RunJson(Record(
            workspace,
            "implementation",
            "wG739:p1",
            "/implementation",
            "operator/model-move",
            "reasoning-move"));

        var moved = workspace.RunJson(
            "session-layer", "topology", "move",
            "--domain", Domain, "--team", workspace.Team,
            "--workspace-id", "wG739-new", "--pane-map", "wG739:p1=wG739-new:p1",
            "--write", "--format", "json");
        Assert.True(moved.GetProperty("applied").GetBoolean());

        using var topology = JsonDocument.Parse(File.ReadAllText(workspace.TopologyPath));
        var role = topology.RootElement.GetProperty("roles").GetProperty("implementation");
        Assert.Equal("operator/model-move", role.GetProperty("model").GetString());
        Assert.Equal("reasoning-move", role.GetProperty("reasoning_effort").GetString());
        Assert.Equal("wG739-new:p1", role.GetProperty("pane_id").GetString());
    }

    [Fact]
    public void DocumentationAndLedger_MirrorG739ContractAndKeepG684Exclusion_G739()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "12-agent-message-orchestration.md"));
        var englishLedger = File.ReadAllText(Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md"));
        var japaneseLedger = File.ReadAllText(Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("G739", document, StringComparison.Ordinal);
            Assert.Contains("--model <text>", document, StringComparison.Ordinal);
            Assert.Contains("--reasoning-effort <text>", document, StringComparison.Ordinal);
            Assert.Contains(SessionLayerTopologyDeclaredValueRules.MaxLength.ToString(), document, StringComparison.Ordinal);
        }

        Assert.Contains("operator declarations, not measurements", english, StringComparison.Ordinal);
        Assert.Contains("operator declaration であり、measurement", japanese, StringComparison.Ordinal);
        Assert.Contains("G684's model/effort-as-wish drift semantics", english, StringComparison.Ordinal);
        Assert.Contains("unchanged", english, StringComparison.Ordinal);
        Assert.Contains("G684 の model / effort を wish とする drift semantics は不変です", japanese, StringComparison.Ordinal);

        foreach (var ledger in new[] { englishLedger, japaneseLedger })
        {
            Assert.Contains("| per-team topology record |", ledger, StringComparison.Ordinal);
            Assert.Contains("`model`", ledger, StringComparison.Ordinal);
            Assert.Contains("`reasoning_effort`", ledger, StringComparison.Ordinal);
            Assert.Contains("stable-at-1.0", ledger, StringComparison.Ordinal);
            Assert.Contains("byte-compatible", ledger, StringComparison.Ordinal);
        }
    }

    private static string[] Record(
        TopologyWorkspace workspace,
        string role,
        string pane,
        string cwd,
        string? model = null,
        string? reasoningEffort = null)
    {
        var args = new List<string>
        {
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", workspace.Team,
            "--role", role,
            "--resident", "herdr",
            "--workspace-id", WorkspaceId,
            "--pane-id", pane,
            "--cwd", cwd,
            "--kind", "codex",
            "--delivery-method", "inline",
        };
        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }
        if (reasoningEffort is not null)
        {
            args.AddRange(["--reasoning-effort", reasoningEffort]);
        }
        args.AddRange(["--write", "--format", "json"]);
        return args.ToArray();
    }

    private sealed class TopologyWorkspace : IDisposable
    {
        public TopologyWorkspace(string suffix)
        {
            RootPath = Directory.CreateTempSubdirectory($"session-layer-topology-g739-{suffix}-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Team = $"g739-{suffix}";
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

        public (int ExitCode, string Output) RunRaw(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public JsonElement RunJson(string[] args, int expectedExitCode = 0)
        {
            var result = RunRaw(args);
            Assert.Equal(expectedExitCode, result.ExitCode);
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.Clone();
        }

        public JsonElement RunJson(params string[] args) => RunJson(args, expectedExitCode: 0);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
