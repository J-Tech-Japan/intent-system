using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationBaseBranchCheckCommandTests
{
    [Fact]
    public void Execute_DirectMainPolicyWithMainBase_ReturnsOk()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("direct-main"),
            ["--repo", "owner/repo", "--pr", "12", "--actual-base", "main", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("direct-main", root.GetProperty("policy").GetString());
        Assert.Equal("main", root.GetProperty("expected_base").GetString());
        Assert.Equal("main", root.GetProperty("actual_base").GetString());
        Assert.Empty(root.GetProperty("recommended_actions").EnumerateArray());
    }

    [Fact]
    public void Execute_DirectMainPolicyWithMainAiBase_ReturnsMismatch()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("direct-main"),
            ["--repo", "owner/repo", "--pr", "12", "--actual-base", "main-ai", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("mismatch", root.GetProperty("status").GetString());
        Assert.Equal("main", root.GetProperty("expected_base").GetString());
        Assert.Equal("main-ai", root.GetProperty("actual_base").GetString());
        Assert.Contains("requires `main`", root.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, root.GetProperty("recommended_actions").GetArrayLength());
    }

    [Fact]
    public void Execute_MainAiPolicyWithMainAiBase_ReturnsOk()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("main-ai"),
            ["--repo", "owner/repo", "--pr", "42", "--actual-base", "main-ai", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("main-ai", document.RootElement.GetProperty("expected_base").GetString());
    }

    [Fact]
    public void Execute_MainAiPolicyWithMainBase_ReturnsMismatch_AndExitsOne()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("main-ai"),
            ["--repo", "owner/repo", "--pr", "42", "--actual-base", "main", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("mismatch", root.GetProperty("status").GetString());
        Assert.Equal("main-ai", root.GetProperty("policy").GetString());
        Assert.Equal("main-ai", root.GetProperty("expected_base").GetString());
        Assert.Equal("main", root.GetProperty("actual_base").GetString());
    }

    [Fact]
    public void Execute_PolicyOverrideWins_OverConfig()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("direct-main"),
            ["--repo", "owner/repo", "--pr", "5", "--actual-base", "main-ai", "--policy", "main-ai", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("main-ai", document.RootElement.GetProperty("policy").GetString());
    }

    [Fact]
    public void Execute_RejectsUnknownPolicyFlag()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("direct-main"),
            ["--repo", "owner/repo", "--pr", "5", "--actual-base", "main", "--policy", "main-bogus", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must be 'direct-main' or 'main-ai'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsMissingActualBase()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("direct-main"),
            ["--repo", "owner/repo", "--pr", "5", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--actual-base", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MarkdownFormatRendersHumanReadableSummary()
    {
        using var writer = new StringWriter();
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            CreateContext("main-ai"),
            ["--repo", "owner/repo", "--pr", "42", "--actual-base", "main"],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Base branch check — owner/repo PR #42", output, StringComparison.Ordinal);
        Assert.Contains("- Status: **mismatch**", output, StringComparison.Ordinal);
        Assert.Contains("Recommended actions", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ConfiguredImplementationBaseBranch_OverridesPolicyDerivedDefault()
    {
        // G362: when the host config sets an explicit
        // ImplementationBaseBranch (same-repo topology, e.g.
        // `main-implementation`), the base-branch-check MUST use
        // that branch as the expected base — not the policy
        // default. Closes the gap where a PR could be approved
        // against `main` even when the implementation branch is
        // pinned elsewhere.
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                    BaseBranchPolicy = "direct-main",
                    ImplementationBaseBranch = "main-implementation",
                    SameRepoTopology = true,
                },
            },
        };
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            context,
            ["--repo", "owner/repo", "--pr", "99", "--actual-base", "main-implementation", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("main-implementation", root.GetProperty("expected_base").GetString());
    }

    [Fact]
    public void Execute_ConfiguredImplementationBaseBranch_RejectsPolicyDefaultBase()
    {
        // G362: with ImplementationBaseBranch=`main-implementation`,
        // a PR targeting `main` MUST be flagged as a mismatch — the
        // configured branch takes precedence over the policy default.
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                    BaseBranchPolicy = "direct-main",
                    ImplementationBaseBranch = "main-implementation",
                    SameRepoTopology = true,
                },
            },
        };
        var exitCode = AutomationBaseBranchCheckCommand.Execute(
            context,
            ["--repo", "owner/repo", "--pr", "99", "--actual-base", "main", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("mismatch", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("main-implementation", document.RootElement.GetProperty("expected_base").GetString());
    }

    private static CliContext CreateContext(string baseBranchPolicy)
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
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
