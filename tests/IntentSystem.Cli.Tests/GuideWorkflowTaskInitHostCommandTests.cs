using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G335: tests for <c>intent-cli guide workflow task init-host</c>.
/// The command surface must explain the three host roles (design /
/// review-runtime / child-implementation), state where
/// <c>.intent-cli/</c> is required, optional, or forbidden, emit a
/// scaffold plan, and refuse to scaffold a child-implementation role
/// over an existing <c>.intent-cli/</c> cwd unless <c>--force-host</c>
/// is set.
/// </summary>
public sealed class GuideWorkflowTaskInitHostCommandTests : IDisposable
{
    private readonly string emptyCwd = Directory.CreateTempSubdirectory("init-host-tests-empty-").FullName;
    private readonly string hostCwd = Directory.CreateTempSubdirectory("init-host-tests-host-").FullName;

    public GuideWorkflowTaskInitHostCommandTests()
    {
        // Seed the "host" cwd with a `.intent-cli/` directory so the
        // guard fires for child-implementation requests.
        Directory.CreateDirectory(Path.Combine(hostCwd, ".intent-cli"));
    }

    public void Dispose()
    {
        if (Directory.Exists(emptyCwd))
        {
            Directory.Delete(emptyCwd, recursive: true);
        }
        if (Directory.Exists(hostCwd))
        {
            Directory.Delete(hostCwd, recursive: true);
        }
    }

    [Fact]
    public void Execute_NoFilter_ListsAllThreeRolesAndInvariants_ExitZero()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // All three role IDs must appear.
        Assert.Contains("design", output, StringComparison.Ordinal);
        Assert.Contains("review-runtime", output, StringComparison.Ordinal);
        Assert.Contains("child-worker", output, StringComparison.Ordinal);
        // The acceptance criteria explicitly require each role to
        // state where `.intent-cli/` is required / optional / forbidden.
        Assert.Contains("REQUIRED", output, StringComparison.Ordinal);
        Assert.Contains("FORBIDDEN", output, StringComparison.Ordinal);
        // Scaffold files must include the three files named in the
        // acceptance criteria.
        Assert.Contains("AGENTS.md", output, StringComparison.Ordinal);
        Assert.Contains("CLAUDE.md", output, StringComparison.Ordinal);
        Assert.Contains("host-binding.toml", output, StringComparison.Ordinal);
        // Invariants must include the metadata-mutation prohibition.
        Assert.Contains("Prefer intent-cli-backed metadata mutation", output, StringComparison.Ordinal);
        // Child isolation invariant.
        Assert.Contains("Parent host queue-state", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_ReturnsStableShape()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("usage", out _));
        Assert.True(root.TryGetProperty("cwd_has_dot_intent_cli", out var cwdProp));
        Assert.False(cwdProp.GetBoolean());
        Assert.True(root.TryGetProperty("force_host", out var forceProp));
        Assert.False(forceProp.GetBoolean());
        Assert.True(root.TryGetProperty("roles", out var rolesProp));
        Assert.Equal(JsonValueKind.Array, rolesProp.ValueKind);
        Assert.Equal(3, rolesProp.GetArrayLength());
        Assert.True(root.TryGetProperty("invariants", out var invProp));
        Assert.True(invProp.GetArrayLength() >= 5);
        // Each role must have the required fields.
        foreach (var role in rolesProp.EnumerateArray())
        {
            Assert.True(role.TryGetProperty("role", out _));
            Assert.True(role.TryGetProperty("summary", out _));
            Assert.True(role.TryGetProperty("dot_intent_cli_placement", out _));
            Assert.True(role.TryGetProperty("scaffold_files", out _));
            Assert.True(role.TryGetProperty("init_commands", out _));
            Assert.True(role.TryGetProperty("first_loop", out _));
        }
    }

    [Theory]
    [InlineData("design", "design")]
    [InlineData("review-runtime", "review-runtime")]
    [InlineData("child-worker", "child-worker")]
    // G335 alias: external users learn "child-implementation" from
    // the issue title; accept both spellings on input.
    [InlineData("child-implementation", "child-worker")]
    public void Execute_RoleFilter_ReturnsOnlyThatRole(string requested, string normalized)
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "--role", requested, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var roles = document.RootElement.GetProperty("roles");
        Assert.Equal(1, roles.GetArrayLength());
        Assert.Equal(normalized, roles[0].GetProperty("role").GetString());
        Assert.Equal(normalized, document.RootElement.GetProperty("focus_role").GetString());
    }

    [Fact]
    public void Execute_ChildImplementationRoleOverExistingDotIntentCli_RefusesAndExitsOne()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(hostCwd),
            new[] { "--role", "child-implementation", "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("cwd_has_dot_intent_cli").GetBoolean());
        Assert.False(document.RootElement.GetProperty("force_host").GetBoolean());
        Assert.True(document.RootElement.TryGetProperty("refusals", out var refusals));
        Assert.True(refusals.GetArrayLength() >= 1);
        var combined = string.Join(" ", refusals.EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("already contains", combined, StringComparison.Ordinal);
        Assert.Contains("--force-host", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementationRoleWithForceHost_OverridesGuardExitsZero()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(hostCwd),
            new[] { "--role", "child-implementation", "--force-host", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("cwd_has_dot_intent_cli").GetBoolean());
        Assert.True(document.RootElement.GetProperty("force_host").GetBoolean());
        // No refusals when --force-host is set.
        if (document.RootElement.TryGetProperty("refusals", out var refusals))
        {
            // Null is OK (omitted via WhenWritingNull); else must be empty.
            Assert.True(refusals.ValueKind == JsonValueKind.Null || refusals.GetArrayLength() == 0);
        }
    }

    [Fact]
    public void Execute_DesignRoleWithExistingDotIntentCli_NoRefusal()
    {
        // Design and review-runtime SHOULD live in a cwd with
        // `.intent-cli/`; only child-implementation triggers the
        // guard.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(hostCwd),
            new[] { "--role", "design", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("cwd_has_dot_intent_cli").GetBoolean());
        if (document.RootElement.TryGetProperty("refusals", out var refusals))
        {
            Assert.True(refusals.ValueKind == JsonValueKind.Null || refusals.GetArrayLength() == 0);
        }
    }

    [Fact]
    public void Execute_UnknownRole_ExitsOneWithError()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "--role", "nope" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("did not resolve", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOneWithError()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "--unknown" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    // ---- dispatcher (`guide workflow task`) tests --------------------------

    [Fact]
    public void GuideWorkflowTaskCommand_NoTask_ExitsOneWithUsage()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(emptyCwd),
            Array.Empty<string>(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Supported: init-host", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_UnknownTask_ExitsOneWithSupportedList()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "deploy-host" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("Unknown 'guide workflow task' name 'deploy-host'", output, StringComparison.Ordinal);
        Assert.Contains("Supported: init-host", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_InitHostDispatch_ReturnsInitHostGuidance()
    {
        // The dispatcher must thread `init-host` (plus any options)
        // into GuideWorkflowTaskInitHostCommand without losing args.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "init-host", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        // The init-host payload is identifiable by its usage line.
        Assert.Contains("guide workflow task init-host", document.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
    }

    // ---- guide workflow parent dispatcher -----------------------------------

    [Fact]
    public void GuideWorkflowCommand_DispatchesTaskSubcommand()
    {
        // `guide workflow task ...` must reach the task dispatcher
        // through the existing GuideWorkflowCommand entry; the help
        // line also advertises the new subcommand.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "task", "init-host", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide workflow task init-host", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowCommand_HelpMentionsTaskSubcommand()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(emptyCwd),
            new[] { "--help" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("suggest", output, StringComparison.Ordinal);
        Assert.Contains("task", output, StringComparison.Ordinal);
        Assert.Contains("init-host", output, StringComparison.Ordinal);
    }

    // ---- guide help integration --------------------------------------------

    [Fact]
    public void GuideHelpCommand_WorkflowGuides_InitPhasePointsToTaskInitHost()
    {
        // G335: the `init` workflow guide pointer must now route an
        // external user to `task init-host` first, so they see the
        // role explanation BEFORE running `intent init --write`.
        var initPointer = GuideHelpCommand.WorkflowGuides
            .FirstOrDefault(p => string.Equals(p.Phase, "init", StringComparison.Ordinal));
        Assert.NotNull(initPointer);
        Assert.Contains("guide workflow task init-host", initPointer!.Command, StringComparison.Ordinal);
        Assert.Contains("intent init", string.Join(" ", initPointer.SeeAlso ?? Array.Empty<string>()), StringComparison.Ordinal);
    }

    // ----- G348: same-repo topology guidance -----

    [Fact]
    public void Execute_G348_TopologySameRepo_MarkdownIncludesSameRepoSection()
    {
        // G348 AC: `guide workflow task init-host --topology same-repo` must
        // describe same-repo topology with branch strategy and forbidden paths.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            ["--topology", "same-repo"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Same-Repo Topology", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/**", output, StringComparison.Ordinal);
        Assert.Contains("intents/**", output, StringComparison.Ordinal);
        Assert.Contains("FORBIDDEN", output, StringComparison.Ordinal);
        Assert.Contains("main-ai", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G348_TopologySameRepo_JsonIncludesSameRepoTopologyNode()
    {
        // G348 AC: JSON shape must include same_repo_topology with
        // branch_strategy, forbidden_paths_for_implementation, host_constraints.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            ["--topology", "same-repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("same-repo", root.GetProperty("topology").GetString());
        Assert.True(root.TryGetProperty("same_repo_topology", out var topology));
        Assert.True(topology.TryGetProperty("summary", out _));
        Assert.True(topology.TryGetProperty("branch_strategy", out var branchStrategy));
        Assert.True(branchStrategy.GetArrayLength() >= 1);
        Assert.True(topology.TryGetProperty("forbidden_paths_for_implementation", out var forbidden));
        Assert.True(forbidden.GetArrayLength() >= 2);
        Assert.True(topology.TryGetProperty("host_constraints", out var hostConstraints));
        Assert.True(hostConstraints.GetArrayLength() >= 1);
        Assert.True(topology.TryGetProperty("warning", out _));
    }

    [Fact]
    public void Execute_G348_TopologySameRepoNoPolicy_EmitsPolicyWarning()
    {
        // G348 AC: when same-repo topology is selected but no base-branch
        // policy is configured (default direct-main), a warning must appear.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            ["--topology", "same-repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("same_repo_policy_warning", out var warningProp));
        Assert.NotNull(warningProp.GetString());
        Assert.False(string.IsNullOrWhiteSpace(warningProp.GetString()));
        Assert.Contains("main-ai", warningProp.GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G348_TopologySameRepoWithMainAiPolicy_NoPolicyWarning()
    {
        // G348 AC: when same-repo topology + main-ai policy is configured,
        // the same_repo_policy_warning must be null (no warning needed).
        using var writer = new StringWriter();
        var context = CreateContextWithPolicy(emptyCwd, "main-ai");

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            context,
            ["--topology", "same-repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // When policy is main-ai, same_repo_policy_warning should be absent or null.
        if (root.TryGetProperty("same_repo_policy_warning", out var warningProp))
        {
            Assert.True(warningProp.ValueKind == JsonValueKind.Null,
                $"Expected same_repo_policy_warning to be null but got: {warningProp.GetString()}");
        }
    }

    [Fact]
    public void Execute_G348_NoTopology_SameRepoNodeAbsent()
    {
        // G348: when --topology is not passed, same_repo_topology must be absent.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        // topology and same_repo_topology should be absent (WhenWritingNull).
        if (root.TryGetProperty("topology", out var topologyProp))
        {
            Assert.Equal(JsonValueKind.Null, topologyProp.ValueKind);
        }
        if (root.TryGetProperty("same_repo_topology", out var sameRepoProp))
        {
            Assert.Equal(JsonValueKind.Null, sameRepoProp.ValueKind);
        }
    }

    [Fact]
    public void Execute_G348_UnknownTopology_ExitsOneWithError()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskInitHostCommand.Execute(
            CreateContext(emptyCwd),
            ["--topology", "cross-repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be 'same-repo'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G348_SameRepoTopologyGuidance_ForbiddenPathsNamesBothHostPaths()
    {
        // G348 AC: same-repo guidance must explicitly name both host-metadata
        // path groups as forbidden for implementation agents.
        var forbidden = GuideWorkflowTaskInitHostCommand.SameRepoTopology.ForbiddenPathsForImplementation;
        var combined = string.Join(" ", forbidden);
        Assert.Contains(".intent-cli/**", combined, StringComparison.Ordinal);
        Assert.Contains("intents/**", combined, StringComparison.Ordinal);
        Assert.Contains("FORBIDDEN", combined, StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }

    private static CliContext CreateContextWithPolicy(string repoRoot, string baseBranchPolicy)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                    BaseBranchPolicy = baseBranchPolicy
                }
            }
        };
    }
}
