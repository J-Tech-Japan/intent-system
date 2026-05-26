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

    // ── G353: host-artifact-repair-required stop condition ───────────

    [Fact]
    public void Execute_Json_G353_PromptStopsOnHostMetadataCommentPaths()
    {
        // G353 AC: "Child worker guidance refuses to edit .intent-cli/**
        // or intents/** during pr-comment-fix and reports
        // host-artifact-repair-required."
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The prompt must name the forbidden host metadata path patterns.
        Assert.Contains(".intent-cli/**", prompt, StringComparison.Ordinal);
        Assert.Contains("intents/**", prompt, StringComparison.Ordinal);

        // The prompt must surface the stop outcome name.
        Assert.Contains("host-artifact-repair-required", prompt, StringComparison.Ordinal);

        // The prompt must explicitly instruct the worker to stop (not repair) on host paths.
        Assert.Contains("host metadata", prompt, StringComparison.OrdinalIgnoreCase);

        // The outcome_classification map must include the new outcome.
        var outcomes = document.RootElement.GetProperty("outcome_classification")
            .EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("host-artifact-repair-required", outcomes, StringComparer.Ordinal);
    }

    [Fact]
    public void Execute_Json_G353_PromptMentionsPrCommentPreflightAsHostMetadataDetector()
    {
        // G353: the guide prompt should direct the worker to use
        // pr-comment-preflight to detect host-artifact-repair-required
        // automatically, rather than parsing comment bodies manually.
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The prompt should mention pr-comment-preflight as a triage helper.
        Assert.Contains("pr-comment-preflight", prompt, StringComparison.Ordinal);
        // And link the classification field to the stop condition.
        Assert.Contains("classification", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_G353_HardRulesForbidHostMetadataEdits()
    {
        // The "Hard rules" section of the prompt must explicitly prohibit
        // editing .intent-cli/** or intents/** paths during a pr-comment-fix.
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Hard-rules prohibition must reference both path families.
        Assert.Contains("Do not edit `.intent-cli/**`", prompt, StringComparison.Ordinal);
        Assert.Contains("intents/**`", prompt, StringComparison.Ordinal);
        // And name the stop outcome.
        // (The outcome name appears in both the triage section and the hard rules.)
        var occurrences = 0;
        var search = "host-artifact-repair-required";
        var idx = 0;
        while ((idx = prompt.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
        {
            occurrences++;
            idx += search.Length;
        }
        // Must appear at least twice: once in comment-triage, once in hard-rules.
        Assert.True(occurrences >= 2, $"Expected ≥2 occurrences of '{search}' in prompt, found {occurrences}.");
    }

    // ── G408 repeated-stall recovery ─────────────────────────────────────────

    /// <summary>
    /// G408: the pr-comment-fix prompt must contain repeated-stall recovery guidance
    /// that triggers after two or more consecutive wakes on the same PR without progress.
    /// </summary>
    [Fact]
    public void Execute_Json_PromptContainsRepeatedStallRecoverySection()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Repeated-stall recovery", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("two or more consecutive", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli guide model", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide onboarding", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide commands list", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker pr-comment-preflight", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// G408: recovery must identify safe child-loop-owned repairs vs host/operator stops.
    /// </summary>
    [Fact]
    public void Execute_Json_PromptDistinguishesSafeRepairFromOperatorStop()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("child-selector-label-gap", prompt, StringComparison.Ordinal);
        Assert.Contains("operator stop", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: the JSON output must include a structured repeated_stall_recovery field.
    /// </summary>
    [Fact]
    public void Execute_Json_EmitsRepeatedStallRecoveryStructuredField()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("repeated_stall_recovery", out var recovery));
        Assert.False(string.IsNullOrWhiteSpace(recovery.GetProperty("trigger").GetString()));
        Assert.True(recovery.GetProperty("re_read_guidance").GetArrayLength() >= 5);
        Assert.Equal("child-selector-label-gap", recovery.GetProperty("safe_repair_category").GetString());
        Assert.True(recovery.GetProperty("operator_stop_categories").GetArrayLength() >= 3);
        Assert.Equal(1, recovery.GetProperty("max_repairs_per_cycle").GetInt32());
    }

    /// <summary>
    /// G408: the markdown output must include a repeated-stall recovery section.
    /// </summary>
    [Fact]
    public void Execute_Markdown_ContainsRepeatedStallRecoverySection()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerPrCommentFixCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Repeated-stall recovery", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child-selector-label-gap", output, StringComparison.Ordinal);
        Assert.Contains("max repairs per cycle: 1", output, StringComparison.OrdinalIgnoreCase);
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
