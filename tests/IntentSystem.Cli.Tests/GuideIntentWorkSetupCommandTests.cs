using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// focused-guide-intent-work-setup-command-tests
/// </summary>
public sealed class GuideIntentWorkSetupCommandTests
{
    // ── domain-organize ──────────────────────────────────────────────────

    [Fact]
    public void Execute_DomainOrganizeMarkdown_EmitsPasteReadyPromptAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "domain-organize", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide intent-work setup — domain-organize", output, StringComparison.Ordinal);
        Assert.Contains("domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("target repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent search --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("## Clarification format", output, StringComparison.Ordinal);
        Assert.Contains("background, question, options, pros/cons, and recommendation", output, StringComparison.Ordinal);
        Assert.Contains("## Issue publish boundary", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
        Assert.Contains("Organize the current intent state for domain `intent-cli`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DomainOrganizeJson_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "domain-organize", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("domain-organize", root.GetProperty("kind").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal(7, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.Contains("Organize the current intent state", root.GetProperty("prompt").GetString()!, StringComparison.Ordinal);
        Assert.Contains("background, question, options, pros/cons", root.GetProperty("clarification_format").GetString()!, StringComparison.Ordinal);
        Assert.Contains("worktree", root.GetProperty("worktree_friendly").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DomainOrganizePrompt_BansAdvancedRuntimeAndLocalRulesFiles()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "domain-organize", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", prompt, StringComparison.Ordinal);
    }

    // ── next-slice ────────────────────────────────────────────────────────

    [Fact]
    public void Execute_NextSliceMarkdown_NamesIssuePublishBoundaryAndPacketDraft()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide intent-work setup — next-slice", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli issue publish-flow", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation issue-publish", output, StringComparison.Ordinal);
        Assert.Contains("at most one GitHub issue", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_NextSliceJson_FirstCallsContainStatusSearchAndNextSlice()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent status --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent search --domain intent-cli", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.StartsWith("intent-cli intent next-slice --dry-run --domain intent-cli", StringComparison.Ordinal));
        Assert.Equal(7, firstCalls.Length);
    }

    [Fact]
    public void Execute_NextSlicePrompt_EnforcesAtMostOnePublicationAndPreloadsMultiple()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Publish at most one", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multiple future packets may be preloaded", prompt, StringComparison.OrdinalIgnoreCase);
        var boundary = document.RootElement.GetProperty("issue_publish_boundary").GetString()!;
        Assert.Contains("at most one GitHub issue", boundary, StringComparison.OrdinalIgnoreCase);
    }

    // ── packet-preload ────────────────────────────────────────────────────

    [Fact]
    public void Execute_PacketPreloadMarkdown_EmitsPreloadStepsAndPublishBoundary()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "packet-preload", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide intent-work setup — packet-preload", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli packet draft", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation issue-publish", output, StringComparison.Ordinal);
        Assert.Contains("at most one GitHub issue", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_PacketPreloadJson_IssuePublishBoundaryAllowsMultiplePreloads()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "packet-preload", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var boundary = document.RootElement.GetProperty("issue_publish_boundary").GetString()!;
        Assert.Contains("at most one GitHub issue", boundary, StringComparison.OrdinalIgnoreCase);
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("multiple packets are preloaded", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── clarification ─────────────────────────────────────────────────────

    [Fact]
    public void Execute_ClarificationMarkdown_RequiresClarificationStructure()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "clarification", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide intent-work setup — clarification", output, StringComparison.Ordinal);
        Assert.Contains("background, question, options, pros/cons, and recommendation", output, StringComparison.Ordinal);
        Assert.Contains("Do not publish a GitHub issue in this wake", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ClarificationJson_IssuePublishBoundaryForbidsIssueInThisWake()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "clarification", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var boundary = document.RootElement.GetProperty("issue_publish_boundary").GetString()!;
        Assert.Contains("Do not publish", boundary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ClarificationPrompt_RequiresFivePartStructure()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "clarification", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Background", prompt, StringComparison.Ordinal);
        Assert.Contains("Question", prompt, StringComparison.Ordinal);
        Assert.Contains("Options", prompt, StringComparison.Ordinal);
        Assert.Contains("Pros", prompt, StringComparison.Ordinal);
        Assert.Contains("Recommendation", prompt, StringComparison.Ordinal);
    }

    // ── error cases ───────────────────────────────────────────────────────

    [Fact]
    public void Execute_MissingKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "next-slice", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingTargetRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "next-slice", "--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--target-repo is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "unknown-kind", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "next-slice", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide intent-work setup", writer.ToString(), StringComparison.Ordinal);
    }

    // ── routing via CommandRouter ─────────────────────────────────────────

    [Fact]
    public void Dispatch_GuideIntentWorkSetupRoutedThroughCommandRouter_Works()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "intent-work", "setup", "--kind", "next-slice",
             "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("next-slice", document.RootElement.GetProperty("kind").GetString());
    }

    // ── intent-shape (G272) ───────────────────────────────────────────────

    [Fact]
    public void Execute_IntentShapeMarkdown_EmitsPasteReadyPromptWithExplainAndInterview()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide intent-work setup — intent-shape", output, StringComparison.Ordinal);
        Assert.Contains("domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("target repo: J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent search --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run --domain intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent explain", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli interview", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("## Clarification format", output, StringComparison.Ordinal);
        Assert.Contains("background, question, options, pros/cons, and recommendation", output, StringComparison.Ordinal);
        Assert.Contains("## Issue publish boundary", output, StringComparison.Ordinal);
        Assert.Contains("at most one GitHub issue", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_IntentShapeJson_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-shape", root.GetProperty("kind").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());
        Assert.Equal(7, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.Contains("background, question, options, pros/cons", root.GetProperty("clarification_format").GetString()!, StringComparison.Ordinal);
        Assert.Contains("worktree", root.GetProperty("worktree_friendly").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_IntentShapeJson_PromptCoversExplainAndInterviewCommands()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli intent explain", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli interview", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_IntentShapeJson_PromptCoversStatusSearchNextSlicePacketFlow()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent status", prompt, StringComparison.Ordinal);
        Assert.Contains("intent search", prompt, StringComparison.Ordinal);
        Assert.Contains("intent explain", prompt, StringComparison.Ordinal);
        Assert.Contains("intent next-slice", prompt, StringComparison.Ordinal);
        Assert.Contains("interview", prompt, StringComparison.Ordinal);
        Assert.Contains("packet draft", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_IntentShapeJson_PromptEnforcesAtMostOnePublicationAndDurableCommit()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("at most one GitHub issue per wake", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("durable", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_IntentShapeJson_PromptForbidsLocalRulesAndSkillsAndProvider()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intents/rules/**", prompt, StringComparison.Ordinal);
        Assert.Contains("local skill files", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not call `intent-cli run`", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not run `dotnet run`", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_IntentShapeJson_ClarificationFormatRequiresFiveParts()
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "intent-shape", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
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
    public void Execute_HelpFlag_ListsIntentShapeKind()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("intent-shape", writer.ToString(), StringComparison.Ordinal);
    }

    // ── shared contract ───────────────────────────────────────────────────

    [Theory]
    [InlineData("domain-organize")]
    [InlineData("next-slice")]
    [InlineData("packet-preload")]
    [InlineData("clarification")]
    [InlineData("intent-shape")]
    public void Execute_AllKinds_PromptForbidsLocalRulesFilesAndSkillFiles(string kind)
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", kind, "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intents/rules/**", prompt, StringComparison.Ordinal);
        Assert.Contains("local skill files", prompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("domain-organize")]
    [InlineData("next-slice")]
    [InlineData("packet-preload")]
    [InlineData("clarification")]
    [InlineData("intent-shape")]
    public void Execute_AllKinds_FirstCallsIncludeModelOnboardingAndCommandsList(string kind)
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", kind, "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var firstCalls = document.RootElement.GetProperty("first_calls")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(firstCalls, c => c!.Contains("guide model", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("guide onboarding", StringComparison.Ordinal));
        Assert.Contains(firstCalls, c => c!.Contains("guide commands list", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("domain-organize")]
    [InlineData("next-slice")]
    [InlineData("packet-preload")]
    [InlineData("clarification")]
    [InlineData("intent-shape")]
    public void Execute_AllKinds_PromptRequiresClarificationFormat(string kind)
    {
        using var writer = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", kind, "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var format = document.RootElement.GetProperty("clarification_format").GetString()!;
        Assert.Contains("background", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("question", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("options", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pros", format, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recommendation", format, StringComparison.OrdinalIgnoreCase);
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
