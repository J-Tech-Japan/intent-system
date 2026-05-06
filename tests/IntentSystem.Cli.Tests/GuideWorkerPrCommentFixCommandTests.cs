using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideWorkerPrCommentFixCommandTests
{
    [Fact]
    public void Execute_NoArgs_ReturnsMarkdownWithPromptAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide worker — pr-comment-fix", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithRepo_EmitsRepoInMarkdownPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("`J-Tech-Japan/intent-system`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomain_EmitsDomainInFirstCallsAndPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        var firstCalls = root.GetProperty("first_calls").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.Contains("automation summary --domain intent-cli --format json", StringComparison.Ordinal));
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("automation summary --domain intent-cli --format json", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<DOMAIN>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoDomain_EmitsDomainPlaceholderInFirstCallsAndPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.Contains("automation summary --domain <DOMAIN>", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Json_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("pr-comment-fix", root.GetProperty("kind").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("repo").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal(4, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("outcome_classification").EnumerateObject().Count() >= 5);
        Assert.True(root.TryGetProperty("label_ownership", out _));
        Assert.True(root.TryGetProperty("worktree_friendly", out _));
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide worker pr-comment-fix", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideWorkerPrCommentFixRoutedThroughCommandRouter_Works()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "worker", "pr-comment-fix", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("pr-comment-fix", document.RootElement.GetProperty("kind").GetString());
    }

    // ── focused-guide-worker-pr-comment-fix-prompt-tests ───────────

    [Fact]
    public void Execute_Json_PromptForbidsGhFixPrCommentSkill()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("gh-fix-pr-comment", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not use the `gh-fix-pr-comment` skill file", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversCommentTriage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Comment triage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-actionable-comments", prompt, StringComparison.Ordinal);
        Assert.Contains("clarification-required", prompt, StringComparison.Ordinal);
        Assert.Contains("already-resolved", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCheckoutsExistingBranchNotCreatesNew()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("gh pr checkout", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not create a new branch", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversWorkerClaimCompleteHandoff()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli worker claim --kind pr", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker complete --kind pr", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker result-summary --kind pr-comment-fix", prompt, StringComparison.Ordinal);
        Assert.Contains("repair-pushed", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptContainsOutcomeClassificationWithAllRequiredOutcomes()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var outcomes = document.RootElement.GetProperty("outcome_classification")
            .EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("repair-pushed", outcomes, StringComparer.Ordinal);
        Assert.Contains("no-actionable-comments", outcomes, StringComparer.Ordinal);
        Assert.Contains("already-resolved", outcomes, StringComparer.Ordinal);
        Assert.Contains("clarification-required", outcomes, StringComparer.Ordinal);
        Assert.Contains("failed", outcomes, StringComparer.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptEnforcesNarrowScopeRepair()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("narrow", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not add unrequested refactors", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ContainsForbiddenSourcesAndWorktreetFriendly()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("gh-fix-pr-comment skill file", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("## Worktree-friendly assumption", output, StringComparison.Ordinal);
    }

    // ── focused-guide-worker-pr-comment-fix-host-bookkeeping-tests ───────────

    [Fact]
    public void Execute_Json_PromptForbidsParentHostBookkeepingEdits()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("queue-state.json", prompt, StringComparison.Ordinal);
        Assert.Contains("linked_issue", prompt, StringComparison.Ordinal);
        Assert.Contains("linked_pr", prompt, StringComparison.Ordinal);
        Assert.Contains("host-owned durable bookkeeping", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_PromptForbidsAutomationIssuePublish()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("automation issue-publish", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `intent-cli automation issue-publish`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptTreatsSelectedPRURLAsAuthoritativeWorkInput()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("authoritative work input", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker next-action", prompt, StringComparison.Ordinal);
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
