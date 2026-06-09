using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G338: tests for <c>intent-cli guide workflow task review-next-slice-loop</c>.
/// The surface forwards to <c>guide prompt-matrix --mode host-loop</c>
/// after pinning the mode, validates flag shape locally so unknown-arg
/// errors surface the task's own usage line, and renders the
/// paste-ready host review / next-slice-loop prompt with the current
/// host-sync preflight + label/automation rules.
/// </summary>
public sealed class GuideWorkflowTaskReviewNextSliceLoopCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_PinsHostLoopMode()
    {
        // Acceptance criterion: "guide workflow task
        // review-next-slice-loop generates host-loop instructions
        // from cwd + domain + target repo + agent + frequency."
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "20m" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("host-loop", output, StringComparison.Ordinal);
        Assert.Contains("example/repo", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("20m", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_HasHostLoopMode()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "20m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("host-loop", root.GetProperty("mode").GetString());
        Assert.Equal("loop", root.GetProperty("kind").GetString());
        Assert.Equal("host", root.GetProperty("target").GetString());
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.Equal("20m", root.GetProperty("frequency").GetString());
    }

    [Fact]
    public void Execute_GeneratedPrompt_CarriesHostSyncPreflightAndLifecycleRules()
    {
        // Acceptance criterion: "Generated prompts include current
        // label/claim/complete rules without requiring operator
        // memory." For the host loop, the canonical rules are the
        // host-sync preflight gate, automation summary call,
        // packet/issue lifecycle handoff, and label transitions.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "20m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString();
        Assert.NotNull(prompt);
        // Host-sync preflight gate must appear.
        Assert.Contains("host-sync-preflight", prompt!, StringComparison.Ordinal);
        // Automation summary anchor for the domain must appear.
        Assert.Contains("automation summary", prompt!, StringComparison.Ordinal);
        // The forbidden raw-gh label mutation must be called out.
        Assert.Contains("automation", prompt!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GeneratedPrompt_IsSkillFree_AndDefersOnPendingCi()
    {
        // G472: the review/next-slice loop guidance must route closeout through
        // installed intent-cli surfaces only — no historical local closeout
        // skill dependency — and must treat CI-pending as a defer condition.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "20m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString();
        Assert.NotNull(prompt);

        // No routine dependency on the historical local closeout skill: the
        // obsolete name may only appear in a sentence that forbids it.
        foreach (var line in prompt!.Split('\n'))
        {
            if (line.Contains("intent-review-closeout", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("Do NOT invoke a local closeout skill", line, StringComparison.Ordinal);
            }
        }

        // Closeout names the installed surfaces.
        Assert.Contains("Closeout is skill-free", prompt!, StringComparison.Ordinal);
        Assert.Contains("closeout pr", prompt!, StringComparison.Ordinal);

        // CI-pending defers without a request-update finding or a stale lease.
        Assert.Contains("CI-pending is a defer condition", prompt!, StringComparison.Ordinal);
        Assert.Contains("review-release", prompt!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsTaskUsage()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--help" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide workflow task review-next-slice-loop", output, StringComparison.Ordinal);
        Assert.Contains("--domain", output, StringComparison.Ordinal);
        Assert.Contains("--target-repo", output, StringComparison.Ordinal);
        Assert.Contains("--frequency", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ModeFlag_Rejected_NamesTaskUsage()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--mode", "child-loop" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--mode is not accepted", output, StringComparison.Ordinal);
        Assert.Contains("guide workflow task review-next-slice-loop", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_SurfacesTaskUsage_NotPromptMatrixUsage()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskReviewNextSliceLoopCommand.Execute(
            CreateContext(),
            new[] { "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("Unknown argument '--bogus'", output, StringComparison.Ordinal);
        Assert.Contains("guide workflow task review-next-slice-loop", output, StringComparison.Ordinal);
        Assert.DoesNotContain("guide prompt-matrix [", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_DispatchesReviewNextSliceLoop()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "review-next-slice-loop", "--domain", "intent-cli", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "20m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("host-loop", document.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void GuideHelpCommand_ReviewNextSliceLoopPhase_PointsToTaskReviewNextSliceLoop()
    {
        var pointer = GuideHelpCommand.WorkflowGuides
            .First(p => string.Equals(p.Phase, "review-next-slice-loop", StringComparison.Ordinal));
        Assert.Contains("guide workflow task review-next-slice-loop", pointer.Command, StringComparison.Ordinal);
        var seeAlso = string.Join(" ", pointer.SeeAlso ?? Array.Empty<string>());
        Assert.Contains("prompt-matrix", seeAlso, StringComparison.Ordinal);
        Assert.Contains("host-loop", seeAlso, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_TopLevelHelp_NamesReviewNextSliceLoopPhase()
    {
        var hostLoopLines = CommandRouter.WorkflowGuidePointersHelp
            .Where(line => line.StartsWith("review-next-slice-loop —", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(hostLoopLines);
        Assert.Contains("guide workflow task review-next-slice-loop", hostLoopLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void LoopForwarder_AllowedFlags_DoesNotIncludeMode()
    {
        // The forwarder allow-list must NOT include --mode because
        // each task wrapper pins the mode itself; if --mode crept
        // back into the allow-list the wrapper's --mode rejection
        // would silently lose effect.
        Assert.DoesNotContain("--mode", GuideWorkflowTaskLoopForwarder.AllowedFlags);
    }

    private static CliContext CreateContext()
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
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
