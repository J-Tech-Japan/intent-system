using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideCloseoutRunCommandTests
{
    [Fact]
    public void Execute_NoArgs_ReturnsMarkdownWithPromptAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide closeout — run", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomainAndRepo_EmitsValuesInMarkdownAndPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("target repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomain_EmitsDomainInFirstCallsAndPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
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
        Assert.Contains("intent-cli", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<DOMAIN>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NoDomain_EmitsDomainPlaceholderInPromptAndFirstCalls()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.Contains("automation summary --domain <DOMAIN>", StringComparison.Ordinal));
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("<DOMAIN>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("run", root.GetProperty("kind").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal(4, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.True(root.TryGetProperty("label_ownership", out _));
        Assert.True(root.TryGetProperty("worktree_friendly", out _));
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide closeout run", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideCloseoutRunRoutedThroughCommandRouter_Works()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "closeout", "run", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("run", document.RootElement.GetProperty("kind").GetString());
    }

    // ── focused-closeout-pr-skill-replacement-tests ───────────

    [Fact]
    public void Execute_Json_PromptForbidsIntentCloseoutSkill()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-closeout", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not use the `intent-closeout` skill file", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversPreflightAndMergeStateCheck()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("automation host-review-preflight", prompt, StringComparison.Ordinal);
        Assert.Contains("MERGED", prompt, StringComparison.Ordinal);
        Assert.Contains("not-yet-merged", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversDryRunBeforeWrite()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("closeout pr --pr", prompt, StringComparison.Ordinal);
        Assert.Contains("--dry-run", prompt, StringComparison.Ordinal);
        Assert.Contains("--write", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversLinkedIssueClose()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("gh issue close", prompt, StringComparison.Ordinal);
        Assert.Contains("Closed by PR", prompt, StringComparison.Ordinal);
        Assert.Contains("linked issue", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_PromptCoversSubmodulePointerSync()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("submodule", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mergeCommit", prompt, StringComparison.Ordinal);
        Assert.Contains("reset --hard", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversParentCommitPush()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("git commit", prompt, StringComparison.Ordinal);
        Assert.Contains("git push", prompt, StringComparison.Ordinal);
        Assert.Contains("queue-state.json", prompt, StringComparison.Ordinal);
        Assert.Contains("runs.jsonl", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversContinuationClassification()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("continuation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next-slice-ready", prompt, StringComparison.Ordinal);
        Assert.Contains("no-actionable-item", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptForbidsMerging()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not merge the PR", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ContainsStagesAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCloseoutRunCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Stage 1", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 2", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 3", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 4", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 5", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 6", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intent-closeout skill file", output, StringComparison.Ordinal);
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
