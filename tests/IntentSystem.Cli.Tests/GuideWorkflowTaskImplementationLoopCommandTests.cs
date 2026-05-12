using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G338: tests for <c>intent-cli guide workflow task implementation-loop</c>.
/// The surface forwards to <c>guide prompt-matrix --mode child-loop</c>
/// after pinning the mode, validates flag shape locally so unknown-arg
/// errors surface the task's own usage line, and renders the
/// paste-ready child loop prompt with the current label/claim/complete
/// rules so the operator does not need them from memory.
/// </summary>
public sealed class GuideWorkflowTaskImplementationLoopCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_PinsChildLoopMode()
    {
        // Acceptance criterion: "guide workflow task implementation-loop
        // generates child-loop instructions from cwd + target repo +
        // agent + frequency." The forwarded prompt-matrix call must
        // carry --mode child-loop verbatim.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            new[] { "--target-repo", "example/repo", "--agent", "claude", "--frequency", "5m" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // Markdown render carries the child-loop heading.
        Assert.Contains("child-loop", output, StringComparison.Ordinal);
        // The operator-supplied --frequency is reflected in the
        // generated guidance. The child-loop prompt deliberately
        // resolves the target repo from the cwd at run time (not
        // from --target-repo), so the operator-supplied repo does
        // not substitute into the prompt body — only frequency and
        // agent appear in the rendered text.
        Assert.Contains("5m", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_HasChildLoopMode()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            new[] { "--target-repo", "example/repo", "--agent", "claude", "--frequency", "5m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("child-loop", root.GetProperty("mode").GetString());
        Assert.Equal("loop", root.GetProperty("kind").GetString());
        Assert.Equal("child", root.GetProperty("target").GetString());
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.Equal("5m", root.GetProperty("frequency").GetString());
    }

    [Fact]
    public void Execute_GeneratedPrompt_CarriesCurrentLabelClaimCompleteRules()
    {
        // Acceptance criterion: "Generated prompts include current
        // label/claim/complete rules without requiring operator
        // memory." The wrapper forwards to prompt-matrix, which
        // already embeds the rule text — verify the rule anchors
        // reach the task output so this contract is testable from
        // the wrapper itself.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            new[] { "--target-repo", "example/repo", "--agent", "claude", "--frequency", "5m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString();
        Assert.NotNull(prompt);
        // Label / claim / complete vocabulary.
        Assert.Contains("worker claim", prompt!, StringComparison.Ordinal);
        Assert.Contains("worker complete", prompt!, StringComparison.Ordinal);
        Assert.Contains("worker next-action", prompt!, StringComparison.Ordinal);
        Assert.Contains("worker result-summary", prompt!, StringComparison.Ordinal);
        // G300 / G330 / G333 child-cwd isolation rule must appear.
        Assert.Contains("Child cwd is GitHub-contract-only", prompt!, StringComparison.Ordinal);
        // G311 closing reference gate must appear.
        Assert.Contains("Closes #", prompt!, StringComparison.Ordinal);
        // G314 same-thread scheduler contract must appear.
        Assert.Contains("same-thread", prompt!, StringComparison.Ordinal);
        // Forbidden manual gh label mutation must appear.
        Assert.Contains("automation", prompt!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DoesNotRequireParentHostRootForChildLoopGuide()
    {
        // Acceptance criterion: "Child implementation guide does not
        // require parent host root after G333." The task is read-only
        // text; running it from a fresh tmp cwd (no parent host root,
        // no .intent-cli/) must still produce the child-loop guide.
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "bootstrap",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            context,
            new[] { "--target-repo", "example/repo", "--agent", "claude", "--frequency", "5m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("child-loop", document.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void Execute_HelpFlag_PrintsTaskUsage()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            new[] { "--help" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide workflow task implementation-loop", output, StringComparison.Ordinal);
        Assert.Contains("--target-repo", output, StringComparison.Ordinal);
        Assert.Contains("--frequency", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ModeFlag_Rejected_NamesTaskUsage()
    {
        // The task pins --mode; an operator passing --mode must hit a
        // task-level rejection so they do not blow away the pinned
        // mode.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            new[] { "--mode", "host-loop" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--mode is not accepted", output, StringComparison.Ordinal);
        Assert.Contains("guide workflow task implementation-loop", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_SurfacesTaskUsage_NotPromptMatrixUsage()
    {
        // The wrapper validates flag shape up-front so an unknown
        // argument shows the wrapper's usage line, not the prompt-
        // matrix one. This keeps the discovery surface coherent.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskImplementationLoopCommand.Execute(
            CreateContext(),
            new[] { "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("Unknown argument '--bogus'", output, StringComparison.Ordinal);
        Assert.Contains("guide workflow task implementation-loop", output, StringComparison.Ordinal);
        Assert.DoesNotContain("guide prompt-matrix [", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_DispatchesImplementationLoop()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "implementation-loop", "--target-repo", "example/repo", "--agent", "claude", "--frequency", "5m", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("child-loop", document.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void GuideWorkflowTaskCommand_UnknownTask_NamesG338Tasks()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "bogus-task" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("implementation-loop", output, StringComparison.Ordinal);
        Assert.Contains("review-next-slice-loop", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideHelpCommand_ImplementationLoopPhase_PointsToTaskImplementationLoop()
    {
        var pointer = GuideHelpCommand.WorkflowGuides
            .First(p => string.Equals(p.Phase, "implementation-loop", StringComparison.Ordinal));
        Assert.Contains("guide workflow task implementation-loop", pointer.Command, StringComparison.Ordinal);
        // SeeAlso references the underlying prompt-matrix surface so
        // power users can drill in if they need the lower-level entry.
        var seeAlso = string.Join(" ", pointer.SeeAlso ?? Array.Empty<string>());
        Assert.Contains("prompt-matrix", seeAlso, StringComparison.Ordinal);
        Assert.Contains("child-loop", seeAlso, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_TopLevelHelp_NamesImplementationLoopPhase()
    {
        // The shared WorkflowGuidePointersHelp array must carry the
        // implementation-loop entry so top-level help surfaces it
        // without the user reading per-group help.
        var implementationLoopLines = CommandRouter.WorkflowGuidePointersHelp
            .Where(line => line.StartsWith("implementation-loop —", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(implementationLoopLines);
        Assert.Contains("guide workflow task implementation-loop", implementationLoopLines[0], StringComparison.Ordinal);
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
