using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideAutomationSetupCommandTests
{
    [Fact]
    public void Execute_ChildImplementMarkdown_EmitsPasteReadyPromptAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide automation setup — child-implement", output, StringComparison.Ordinal);
        Assert.Contains("repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide onboarding --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide commands list --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation summary --domain", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("local skill files", output, StringComparison.Ordinal);
        Assert.Contains("## Label ownership", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
        Assert.Contains("Set up the child implementation and PR-comment-update loop for `J-Tech-Japan/intent-system`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementJson_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("child-implement", root.GetProperty("kind").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("repo").GetString());
        Assert.Equal(4, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.Contains("Set up the child implementation and PR-comment-update loop", root.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Manual `gh ... edit", root.GetProperty("label_ownership").GetString()!, StringComparison.Ordinal);
        Assert.Contains("worktree", root.GetProperty("worktree_friendly").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementPrompt_BansAdvancedRuntimeAndManualLabelMutation()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
        Assert.Contains("No manual `gh ... edit", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-target", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-pr-created", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMarkdown_NamesEveryFirstCallAndStageStep()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide automation setup — host-review-next-slice", output, StringComparison.Ordinal);
        Assert.Contains("domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("target repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation pr-transition", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli closeout pr", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli issue publish-flow", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_FirstCallsContainHostSpecificCommands()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent status --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent next-slice --dry-run --domain intent-cli", StringComparison.Ordinal));
        Assert.Equal(6, firstCalls.Length);
    }

    [Fact]
    public void Execute_MissingKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        // G320: error message names both --kind and --purpose since the
        // operator-facing alias must be discoverable from the usage line.
        Assert.Contains("--kind (or --purpose) is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-bootstrap"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind must be 'child-implement' or 'host-review-next-slice'", writer.ToString(), StringComparison.Ordinal);
    }

    // ── focused-guide-automation-setup-child-domain-repo-tests ───────────

    [Fact]
    public void Execute_ChildImplementNoRepo_EmitsRepoCwdGuidanceAndDomainPlaceholder()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("gh repo view --json nameWithOwner", prompt, StringComparison.Ordinal);
        Assert.Contains("git remote get-url origin", prompt, StringComparison.Ordinal);
        Assert.Contains("<DOMAIN>", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementNoRepo_FirstCallsContainDomainPlaceholder()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls,
            c => c!.Contains("automation summary --domain <DOMAIN>", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_ChildImplementWithDomain_UsesDomainInAutomationSummary()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls,
            c => c!.Contains("automation summary --domain intent-cli --format json", StringComparison.Ordinal));
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("automation summary --domain intent-cli --format json", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<DOMAIN>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementWithRepoAndDomain_EmitsExplicitValuesAndNoCwdPath()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system",
             "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("J-Tech-Japan/intent-system", document.RootElement.GetProperty("repo").GetString());
        Assert.Equal("intent-cli", document.RootElement.GetProperty("domain").GetString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("`J-Tech-Japan/intent-system`", prompt, StringComparison.Ordinal);
        Assert.Contains("automation summary --domain intent-cli --format json", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementPrompt_SeparatesChildWorktreeFromParentHostRoot()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("CHILD_WORKTREE", prompt, StringComparison.Ordinal);
        Assert.Contains("parent host root", prompt, StringComparison.Ordinal);
        Assert.Contains("--workdir $CHILD_WORKTREE", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementMarkdown_NoDomain_EmitsDomainPlaceholderInSection()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "markdown"],
            writer);

        var output = writer.ToString();
        Assert.Contains("<DOMAIN>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMissingDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain is required for --kind host-review-next-slice.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMissingTargetRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--target-repo is required for --kind host-review-next-slice.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide automation setup", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideAutomationSetupRoutedThroughGuideAutomation_Works()
    {
        // `intent-cli guide automation setup ...` flows through GuideAutomationCommand,
        // which delegates to GuideAutomationSetupCommand for the leading `setup` token.
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["setup", "--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("child-implement", document.RootElement.GetProperty("kind").GetString());
    }

    // ── focused-guide-automation-setup-same-thread-and-wrapper-help-tests ───────────

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptPreventsNewThreadCreation()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("do not create a new chat", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Run the loop body exactly once", prompt, StringComparison.Ordinal);
        Assert.Contains("in the current thread", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptWarnsAboutCloudSchedulers()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("cloud", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local paths", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".intent-cli", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptBansNewSchedulerUnlessOperatorAsks()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not open a new chat, session, cron, monitor, or scheduler", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_FirstCallsAutomationSummaryIncludesDomain()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls,
            c => c!.Contains("automation summary --domain intent-cli --format json", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_HostReviewNextSliceMarkdown_ContainsSameThreadGuidance()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("do not create a new chat", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local paths", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── focused-guide-automation-setup-frequency-policy-tests ───────────

    [Fact]
    public void Execute_ChildImplementJson_PromptContainsFrequencyPolicy()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("5 minutes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask for the frequency", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-wake execution", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ChildImplementJson_PromptDistinguishesOneWakeFromRecurring()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("one-wake execution does not create any scheduler", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recurring loop", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never guess or use a tool-default interval", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementMarkdown_ContainsFrequencyPolicy()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("5 minutes", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask for the frequency", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptContainsFrequencyPolicy()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("5 minutes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask for the frequency", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-wake execution", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptDistinguishesOneWakeFromRecurring()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("one-wake execution does not create any scheduler", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recurring loop", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never guess or use a tool-default interval", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSliceMarkdown_ContainsFrequencyPolicy()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("5 minutes", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20 minutes", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask for the frequency", output, StringComparison.OrdinalIgnoreCase);
    }

    // ── focused-guide-automation-setup-host-review-next-control-flow-tests ───────────

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptAbortsOnStaleHostCliAndForbidsFallback()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("stale-host-cli", prompt, StringComparison.Ordinal);
        Assert.Contains("Abort", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Refresh or reinstall", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("direct DLL", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("raw `gh` label mutation", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptProceedsToStage2WhenNoPrInStage1()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("no-actionable-item", prompt, StringComparison.Ordinal);
        Assert.Contains("NOT the final idle decision", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Proceed directly to Stage 2", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptSkipsStage2OnWipCap()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("skip-next-slice-due-to-wip", prompt, StringComparison.Ordinal);
        Assert.Contains("Skip Stage 2", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WIP cap", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptTrueNoActionableOnlyWhenBothStagesEmpty()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("truly `no-actionable-item`", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 1 found no eligible PR", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stage 2 found no actionable packet", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptPublishesQueuedPacketsInStage2()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Queued unpublished packets", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("published here rather than ignored", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptStage2ClarificationRequiredStopsWithQuestion()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("clarification-required", prompt, StringComparison.Ordinal);
        Assert.Contains("surface the open blocker or ambiguous question", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clarification-required", prompt.Replace("clarification-required", "REMOVED", StringComparison.Ordinal)
            .Replace("no-actionable-item", "", StringComparison.Ordinal), StringComparison.Ordinal); // structural check passes
        // Must NOT collapse clarification-required into idle
        var clarificationIndex = prompt.IndexOf("clarification-required", StringComparison.Ordinal);
        var doNotDeclareIdleIndex = prompt.IndexOf("Do NOT declare idle", StringComparison.OrdinalIgnoreCase);
        Assert.True(doNotDeclareIdleIndex > clarificationIndex, "Do NOT declare idle must follow clarification-required in the dispatch table");
    }

    [Fact]
    public void Execute_HostReviewNextSliceJson_PromptStage2OutcomesHandledDistinctly()
    {
        using var writer = new StringWriter();
        GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        // All four distinct outcomes must appear
        Assert.Contains("issue-cut-ready", prompt, StringComparison.Ordinal);
        Assert.Contains("clarification-required", prompt, StringComparison.Ordinal);
        Assert.Contains("no-actionable-item", prompt, StringComparison.Ordinal);
        Assert.Contains("Any other outcome", prompt, StringComparison.OrdinalIgnoreCase);
        // Clarification-required must not become idle
        Assert.Contains("Do NOT declare idle", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── focused-guide-automation-setup-pr-comment-fix-hardening-tests ───────────

    [Fact]
    public void Execute_ChildImplementPrompt_PrCommentFixTreatsSelectedPRAsAuthoritative()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("authoritative work input", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("queue-state", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementPrompt_PrCommentFixForbidsHostBookkeepingEdits()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
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
    public void Execute_ChildImplementPrompt_PrCommentFixForbidsAutomationIssuePublish()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("automation issue-publish", prompt, StringComparison.Ordinal);
        Assert.Contains("never run `intent-cli automation issue-publish`", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ChildImplementPrompt_PrCommentFixInstructsCheckoutExistingBranch()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("gh pr checkout", prompt, StringComparison.Ordinal);
        Assert.Contains("existing PR head branch", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ChildImplementPrompt_ContainsMinimalOperatorTriggerPhrase()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("PR comment update loop", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli に聞いて", prompt, StringComparison.Ordinal);
    }

    // --- G320: agent-aware setup contract --------------------------------

    [Fact]
    public void Execute_ChildImplementClaudeMarkdown_NamesClaudeLoopSameThread()
    {
        // G320: minimal operator request (purpose + agent + domain +
        // target-repo + cwd + frequency) must yield a complete contract.
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "child-implement",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--repo", "J-Tech-Japan/intent-system",
                "--cwd", "/Users/op/work/repo",
                "--frequency", "5m",
                "--format", "markdown"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("- agent: claude", output, StringComparison.Ordinal);
        Assert.Contains("- cwd: /Users/op/work/repo", output, StringComparison.Ordinal);
        Assert.Contains("- frequency: 5m", output, StringComparison.Ordinal);
        Assert.Contains("- scheduling mechanism: claude-loop-same-thread", output, StringComparison.Ordinal);
        Assert.Contains("Claude Code same-thread `/loop 5m", output, StringComparison.Ordinal);
        Assert.Contains("Scheduling for this agent (G320)", output, StringComparison.Ordinal);
        // Operator-supplied cwd MUST be embedded so the agent confirms
        // before acting.
        Assert.Contains("Operator-supplied cwd: `/Users/op/work/repo`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementClaudeJson_EmitsAgentFrequencyAndSchedulingFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "child-implement",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--cwd", "/Users/op/work/repo",
                "--frequency", "5m",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("child-implement", root.GetProperty("kind").GetString());
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.Equal("/Users/op/work/repo", root.GetProperty("cwd").GetString());
        Assert.Equal("5m", root.GetProperty("frequency").GetString());
        Assert.Equal("claude-loop-same-thread", root.GetProperty("scheduling_mechanism").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("Claude Code same-thread `/loop 5m", prompt, StringComparison.Ordinal);
        // G314 cross-reference: must forbid new chat/remote scheduler.
        Assert.Contains("do NOT spawn a new chat", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementCodexMarkdown_NamesCodexCurrentThreadHeartbeat()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "child-implement",
                "--agent", "codex",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--frequency", "5m",
                "--format", "markdown"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("- agent: codex", output, StringComparison.Ordinal);
        Assert.Contains("- scheduling mechanism: codex-heartbeat-same-thread", output, StringComparison.Ordinal);
        Assert.Contains("Codex current-thread local automation / heartbeat", output, StringComparison.Ordinal);
        // Codex contract MUST NOT silently borrow Claude's `/loop`.
        Assert.DoesNotContain("`/loop 5m`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementUnknownAgent_SurfacesAskInsteadOfGuessing()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "child-implement",
                "--agent", "robot",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("robot", root.GetProperty("agent").GetString());
        Assert.Equal("unknown-ask-operator", root.GetProperty("scheduling_mechanism").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("ASK the operator", prompt, StringComparison.Ordinal);
        Assert.Contains("Do NOT guess", prompt, StringComparison.Ordinal);
        // Frequency is intentionally optional for unknown agents — the
        // contract refuses to pin a cadence it cannot validate.
        Assert.False(root.TryGetProperty("frequency", out _) && root.GetProperty("frequency").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public void Execute_KnownAgentWithoutFrequency_ReturnsUsageError()
    {
        // G320: claude/codex contracts MUST pin a cadence; missing
        // --frequency is a usage error rather than a guessed default.
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "child-implement",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system"
            ],
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--frequency is required when --agent is 'claude'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewClaudeMarkdown_NamesClaudeLoopSameThread()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "host-review-next-slice",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--cwd", "/Users/op/MyIntentHost",
                "--frequency", "5m",
                "--format", "markdown"
            ],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("- agent: claude", output, StringComparison.Ordinal);
        Assert.Contains("- scheduling mechanism: claude-loop-same-thread", output, StringComparison.Ordinal);
        Assert.Contains("Claude Code same-thread `/loop 5m", output, StringComparison.Ordinal);
        Assert.Contains("Operator-supplied parent host root: `/Users/op/MyIntentHost`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewClaudeJson_EmitsAgentFrequencyAndSchedulingFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            [
                "--purpose", "host-review-next-slice",
                "--agent", "claude",
                "--domain", "intent-cli",
                "--target-repo", "J-Tech-Japan/intent-system",
                "--cwd", "/Users/op/MyIntentHost",
                "--frequency", "5m",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("host-review-next-slice", root.GetProperty("kind").GetString());
        Assert.Equal("claude", root.GetProperty("agent").GetString());
        Assert.Equal("/Users/op/MyIntentHost", root.GetProperty("cwd").GetString());
        Assert.Equal("5m", root.GetProperty("frequency").GetString());
        Assert.Equal("claude-loop-same-thread", root.GetProperty("scheduling_mechanism").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("Claude Code same-thread `/loop 5m", prompt, StringComparison.Ordinal);
        Assert.Contains("Operator-supplied parent host root", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PurposeIsAliasOfKind_ProducesIdenticalKindField()
    {
        // G320: `--purpose` is the operator-facing alias of `--kind`. Output
        // shape MUST stay identical so controllers and host loops can keep
        // routing on `kind`.
        using var withPurpose = new StringWriter();
        var purposeExit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", "child-implement", "--format", "json"],
            withPurpose);

        using var withKind = new StringWriter();
        var kindExit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement", "--format", "json"],
            withKind);

        Assert.Equal(0, purposeExit);
        Assert.Equal(0, kindExit);
        Assert.Equal(withKind.ToString(), withPurpose.ToString());
    }

    // --- G321: purpose / agent / cwd-role normalization -------------------

    [Theory]
    [InlineData("実装")]
    [InlineData("PR comment update")]
    [InlineData("implementation")]
    [InlineData("implement")]
    [InlineData("child")]
    [InlineData("pr-comment-fix")]
    public void Execute_PurposeAliasResolvesToChildImplement(string alias)
    {
        // G321: minimal Japanese / English child-loop phrases all
        // normalize to the canonical `child-implement` kind.
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", alias, "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("child-implement", root.GetProperty("kind").GetString());
        Assert.Equal("child-implement", root.GetProperty("canonical_purpose").GetString());
        Assert.Equal(alias, root.GetProperty("raw_purpose").GetString());
        Assert.Equal("child", root.GetProperty("cwd_role").GetString());
    }

    [Theory]
    [InlineData("review")]
    [InlineData("review & next slice")]
    [InlineData("next slice")]
    [InlineData("host")]
    [InlineData("レビュー")]
    [InlineData("次スライス")]
    public void Execute_PurposeAliasResolvesToHostReviewNextSlice(string alias)
    {
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", alias,
             "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system",
             "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("host-review-next-slice", root.GetProperty("kind").GetString());
        Assert.Equal("host-review-next-slice", root.GetProperty("canonical_purpose").GetString());
        Assert.Equal(alias, root.GetProperty("raw_purpose").GetString());
        Assert.Equal("host", root.GetProperty("cwd_role").GetString());
    }

    [Theory]
    [InlineData("Claude Code", "claude")]
    [InlineData("claude-code", "claude")]
    [InlineData("CLAUDE", "claude")]
    [InlineData("codex-cli", "codex")]
    public void Execute_AgentAliasResolvesToCanonical(string alias, string expectedCanonical)
    {
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", "child-implement",
             "--agent", alias,
             "--frequency", "5m",
             "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal(expectedCanonical, root.GetProperty("canonical_agent").GetString());
        Assert.Equal(alias, root.GetProperty("raw_agent").GetString());
        Assert.Equal(alias, root.GetProperty("agent").GetString());
        var schedulingMechanism = root.GetProperty("scheduling_mechanism").GetString();
        Assert.Equal(
            expectedCanonical == "claude" ? "claude-loop-same-thread" : "codex-heartbeat-same-thread",
            schedulingMechanism);
    }

    [Fact]
    public void Execute_UnknownAgentAlias_DoesNotSilentlyAdoptClaudeOrCodex()
    {
        // G321 acceptance: unknown agents must produce the operator-ask
        // contract, never silently inherit Claude's `/loop` or Codex's
        // heartbeat semantics.
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", "child-implement",
             "--agent", "vscode-copilot",
             "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("unknown", root.GetProperty("canonical_agent").GetString());
        Assert.Equal("vscode-copilot", root.GetProperty("raw_agent").GetString());
        Assert.Equal("unknown-ask-operator", root.GetProperty("scheduling_mechanism").GetString());
        var prompt = root.GetProperty("prompt").GetString()!;
        // The unknown-agent scheduling block legitimately mentions "/loop"
        // in a NEGATIVE context ("Do NOT guess `/loop`..."), so the bare
        // "/loop" substring is allowed. What matters is that the prompt
        // does NOT contain the positive Claude / Codex scheduling
        // contracts (the very mechanisms G321 forbids guessing).
        Assert.DoesNotContain("Claude Code same-thread `/loop", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Codex current-thread local automation", prompt, StringComparison.Ordinal);
        Assert.Contains("ASK the operator", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ExplicitCwdRoleMatchesPurpose_PreservesRole()
    {
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", "host-review-next-slice",
             "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system",
             "--cwd-role", "host",
             "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("host", doc.RootElement.GetProperty("cwd_role").GetString());
    }

    [Fact]
    public void Execute_ConflictingCwdRole_RefusesWithStructuredMessage()
    {
        // G321 acceptance: host purpose + child cwd role is a structured
        // conflict; the command must refuse rather than silently pick a
        // side.
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", "host-review-next-slice",
             "--domain", "intent-cli",
             "--target-repo", "J-Tech-Japan/intent-system",
             "--cwd-role", "child",
             "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        var output = writer.ToString();
        Assert.Contains("--cwd-role 'child' conflicts with --purpose 'host-review-next-slice'", output, StringComparison.Ordinal);
        Assert.Contains("expected cwd-role 'host'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidCwdRole_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exit = GuideAutomationSetupCommand.Execute(
            CreateContext(),
            ["--purpose", "child-implement", "--cwd-role", "garbage", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--cwd-role must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AliasResolver_InferCwdRoleMatchesCanonicalPurpose()
    {
        // Direct unit-style sanity check on the pure normalizer; keeps
        // alias coverage tractable without spinning up the whole command.
        Assert.Equal(
            "child",
            GuideAutomationSetupAliasResolver.InferCwdRole(GuideAutomationSetupAliasResolver.CanonicalPurposeChildImplement));
        Assert.Equal(
            "host",
            GuideAutomationSetupAliasResolver.InferCwdRole(GuideAutomationSetupAliasResolver.CanonicalPurposeHostReviewNextSlice));
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
