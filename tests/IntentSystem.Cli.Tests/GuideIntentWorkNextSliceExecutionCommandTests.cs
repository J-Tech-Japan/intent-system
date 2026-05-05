using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// focused-guide-next-slice-execution-prompt-tests
/// </summary>
public sealed class GuideIntentWorkNextSliceExecutionCommandTests
{
    // ── markdown output ───────────────────────────────────────────────────

    [Fact]
    public void Execute_Markdown_EmitsPasteReadyPromptAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide intent-work next-slice-execution", output, StringComparison.Ordinal);
        Assert.Contains("domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("target repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent search --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intent-next-slice skill file", output, StringComparison.Ordinal);
        Assert.Contains("## Clarification format", output, StringComparison.Ordinal);
        Assert.Contains("background, question, options, pros/cons, and recommendation", output, StringComparison.Ordinal);
        Assert.Contains("## Issue publish boundary", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
        Assert.Contains("Execute the next slice for domain `intent-cli`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_PromptIncludesPacketDraftAndPublishFlow()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli issue publish-flow", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation issue-publish", output, StringComparison.Ordinal);
    }

    // ── json output ───────────────────────────────────────────────────────

    [Fact]
    public void Execute_Json_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("next-slice-execution", root.GetProperty("kind").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal(7, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 4);
        Assert.Contains("Execute the next slice for domain", root.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
        Assert.Contains("background, question, options, pros/cons", root.GetProperty("clarification_format").GetString()!, StringComparison.Ordinal);
        Assert.Contains("worktree", root.GetProperty("worktree_friendly").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_FirstCallsContainStatusSearchAndNextSliceDryRun()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent status --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent search --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent next-slice --dry-run --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("guide model", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("guide onboarding", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("guide commands list", StringComparison.Ordinal));
        Assert.Equal(7, firstCalls.Length);
    }

    // ── prompt content ────────────────────────────────────────────────────

    [Fact]
    public void Execute_Json_PromptEnforcesAtMostOnePublicationAndAllowsMultiplePreloads()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("at most one GitHub issue per wake", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Multiple future packets may be preloaded", prompt, StringComparison.OrdinalIgnoreCase);
        var boundary = document.RootElement.GetProperty("issue_publish_boundary").GetString()!;
        Assert.Contains("at most one GitHub issue per wake", boundary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_PromptRequiresClarificationStructureWithFiveParts()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Background", prompt, StringComparison.Ordinal);
        Assert.Contains("Question", prompt, StringComparison.Ordinal);
        Assert.Contains("Options", prompt, StringComparison.Ordinal);
        Assert.Contains("Pros", prompt, StringComparison.Ordinal);
        Assert.Contains("Recommendation", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptForbidsLocalSkillFilesAndCopiedPrompts()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-next-slice", prompt, StringComparison.Ordinal);
        Assert.Contains("local skill files", prompt, StringComparison.Ordinal);
        Assert.Contains("copied prompt files", prompt, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptBansAdvancedRuntimeAndAiProviders()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask `intent-cli` to launch Claude/Codex", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptIncludesNarrowParentStateCommitAndPushBoundary()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("git add", prompt, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/queue-state.json", prompt, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/runs.jsonl", prompt, StringComparison.Ordinal);
        Assert.Contains("git commit", prompt, StringComparison.Ordinal);
        Assert.Contains("git push", prompt, StringComparison.Ordinal);
        Assert.Contains("durable", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_PromptForbidsGitAddAll()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // Ensure git add -A does not appear as a command (followed by &&, space+&&, or end-of-line)
        Assert.DoesNotContain("git add -A &&", prompt, StringComparison.Ordinal);
        // Ensure the prompt explicitly forbids git add -A
        Assert.Contains("Do not use `git add -A`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_ForbiddenSourcesIncludeIntentNextSliceSkill()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var forbiddenSources = document.RootElement.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(forbiddenSources, s => s!.Contains("intent-next-slice", StringComparison.Ordinal));
        Assert.Contains(forbiddenSources, s => s!.Contains("local skill files", StringComparison.Ordinal));
        Assert.Contains(forbiddenSources, s => s!.Contains("copied prompt files", StringComparison.Ordinal));
        Assert.Contains(forbiddenSources, s => s!.Contains("intents/rules/**", StringComparison.Ordinal));
    }

    // ── error cases ───────────────────────────────────────────────────────

    [Fact]
    public void Execute_MissingDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingTargetRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--target-repo is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--unknown-flag", "value"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide intent-work next-slice-execution", writer.ToString(), StringComparison.Ordinal);
    }

    // ── routing via CommandRouter ─────────────────────────────────────────

    [Fact]
    public void Dispatch_GuideIntentWorkNextSliceExecutionRoutedThroughCommandRouter_Works()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "intent-work", "next-slice-execution",
             "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("next-slice-execution", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("intent-cli", document.RootElement.GetProperty("domain").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", document.RootElement.GetProperty("target_repo").GetString());
    }

    // ── clarification format ──────────────────────────────────────────────

    [Fact]
    public void Execute_Json_ClarificationFormatContainsAllRequiredParts()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var format = document.RootElement.GetProperty("clarification_format").GetString()!;
        Assert.Contains("background", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("question", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("options", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pros", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recommendation", format, StringComparison.OrdinalIgnoreCase);
    }

    // ── worktree friendliness ─────────────────────────────────────────────

    [Fact]
    public void Execute_Json_WorktreeFriendlyMentionsWorktreeAndNoHardCodedPaths()
    {
        using var writer = new StringWriter();
        GuideIntentWorkNextSliceExecutionCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var worktreeFriendly = document.RootElement.GetProperty("worktree_friendly").GetString()!;
        Assert.Contains("worktree", worktreeFriendly, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no", worktreeFriendly, StringComparison.OrdinalIgnoreCase);
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
