using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideWorkerIssueToPrCommandTests
{
    [Fact]
    public void Execute_NoArgs_ReturnsMarkdownWithPromptAndContractGate()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide worker — issue-to-pr", output, StringComparison.Ordinal);
        Assert.Contains("## First-call sequence", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide model --format json", output, StringComparison.Ordinal);
        Assert.Contains("## Contract gate sections", output, StringComparison.Ordinal);
        Assert.Contains("Acceptance Criteria", output, StringComparison.Ordinal);
        Assert.Contains("## Prompt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithRepo_EmitsRepoInMarkdownPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
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
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
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
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
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
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("issue-to-pr", root.GetProperty("kind").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("repo").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal(4, root.GetProperty("first_calls").GetArrayLength());
        Assert.True(root.GetProperty("contract_gate_sections").GetArrayLength() >= 9);
        Assert.True(root.GetProperty("forbidden_sources").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("outcome_classification").EnumerateObject().Count() >= 5);
        Assert.True(root.TryGetProperty("label_ownership", out _));
        Assert.True(root.TryGetProperty("worktree_friendly", out _));
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide worker issue-to-pr", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideWorkerIssueToPrRoutedThroughCommandRouter_Works()
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "worker", "issue-to-pr", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("issue-to-pr", document.RootElement.GetProperty("kind").GetString());
    }

    // ── focused-guide-worker-issue-to-pr-prompt-tests ───────────

    [Fact]
    public void Execute_Json_PromptForbidsGhIssueToPrSkill()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("gh-issue-to-pr", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not use the `gh-issue-to-pr` skill file", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Use the `gh-issue-to-pr` skill", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptContainsStandaloneContractGate()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Standalone contract gate", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Acceptance Criteria", prompt, StringComparison.Ordinal);
        Assert.Contains("declined-contract-incomplete", prompt, StringComparison.Ordinal);
        Assert.Contains("decline before creating any branch", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_PromptCoversOriginMainBranchCreation()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("origin/main", prompt, StringComparison.Ordinal);
        Assert.Contains("git fetch origin main", prompt, StringComparison.Ordinal);
        Assert.Contains("claude/<slug>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversWorkerClaimCompleteHandoff()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("intent-cli worker claim", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker complete", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker result-summary", prompt, StringComparison.Ordinal);
        Assert.Contains("pr-created", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversG717ClaimPrecedenceWorkerPath()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("claim registry", prompt, StringComparison.Ordinal);
        Assert.Contains("stale shadow state", prompt, StringComparison.Ordinal);
        Assert.Contains("active or unavailable claim remains an ownership stop", prompt, StringComparison.Ordinal);
        Assert.Contains("without adding/removing that label", prompt, StringComparison.Ordinal);
        Assert.Contains("claim acquire", prompt, StringComparison.Ordinal);
        Assert.Contains("no raw GitHub label mutation", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_PromptCoversReadyForReviewPrCreation_G733()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("ready-for-review PR", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gh pr create --title", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr create --draft", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-target", prompt, StringComparison.Ordinal);
        Assert.Contains("canonical child-cwd `intent-cli worker complete", prompt, StringComparison.Ordinal);
        Assert.Contains("may apply `intent-target` to the target-repository PR", prompt, StringComparison.Ordinal);
        Assert.Contains("distinct from host-state linkage/publication", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not use raw GitHub label mutation", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_EmitsSeatHostBoundaryAndExactHostDutyRequest_G733()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        var boundary = root.GetProperty("seat_host_boundary");
        var request = boundary.GetProperty("host_duty_request").GetString()!;
        Assert.Contains("intent-cli notify report", request, StringComparison.Ordinal);
        Assert.Contains("--from implementation --to orchestration", request, StringComparison.Ordinal);
        Assert.Contains("--status question", request, StringComparison.Ordinal);
        Assert.Contains("intent-cli claim acquire --scope execution-unit:<EU>", request, StringComparison.Ordinal);
        Assert.Contains("intent-cli claim verify --scope execution-unit:<EU>", request, StringComparison.Ordinal);
        Assert.Contains("--routing-root <host-routing-root> --report-root .", request, StringComparison.Ordinal);

        var claimSafety = boundary.GetProperty("claim_safety").GetString()!;
        Assert.Contains("push_succeeded=true", claimSafety, StringComparison.Ordinal);
        Assert.Contains("passed=true", claimSafety, StringComparison.Ordinal);
        Assert.Contains("compare-and-swap", claimSafety, StringComparison.Ordinal);
        Assert.Contains("plain push", claimSafety, StringComparison.Ordinal);

        var cannot = string.Join('\n', boundary.GetProperty("seat_cannot").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("host-repository GitHub API", cannot, StringComparison.Ordinal);
        Assert.Contains("acquire, release, or take over", cannot, StringComparison.Ordinal);

        var verification = string.Join('\n', boundary.GetProperty("verification").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("probe-should-fail", verification, StringComparison.Ordinal);
        Assert.Contains("Operation not permitted", verification, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_RendersG733BoundaryAndReadyPrRoute()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Seat/host duty boundary (G733)", output, StringComparison.Ordinal);
        Assert.Contains("### Exact host-duty request", output, StringComparison.Ordinal);
        Assert.Contains("### Claim safety", output, StringComparison.Ordinal);
        Assert.Contains("### Co-location rule", output, StringComparison.Ordinal);
        Assert.Contains("remote-herdr", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", output, StringComparison.Ordinal);
        Assert.Contains("ready-for-review PR", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_PromptContainsOutcomeClassificationWithAllRequiredOutcomes()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var outcomes = document.RootElement.GetProperty("outcome_classification")
            .EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Contains("pr-created", outcomes, StringComparer.Ordinal);
        Assert.Contains("declined-contract-incomplete", outcomes, StringComparer.Ordinal);
        Assert.Contains("clarification-required", outcomes, StringComparer.Ordinal);
        Assert.Contains("already-resolved", outcomes, StringComparer.Ordinal);
        Assert.Contains("failed", outcomes, StringComparer.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ContainsContractGateSectionsAndForbiddenSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Contract gate sections", output, StringComparison.Ordinal);
        Assert.Contains("Acceptance Criteria", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden rule sources", output, StringComparison.Ordinal);
        Assert.Contains("gh-issue-to-pr skill file", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
    }

    // ── G408 repeated-stall recovery ─────────────────────────────────────────

    /// <summary>
    /// G408: the issue-to-pr prompt must contain repeated-stall recovery guidance
    /// that triggers after two or more consecutive identical stalls.
    /// </summary>
    [Fact]
    public void Execute_Json_PromptContainsRepeatedStallRecoverySection()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
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
        Assert.Contains("intent-cli worker issue-preflight", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// G408: recovery must identify safe child-loop-owned repairs vs host/operator stops.
    /// </summary>
    [Fact]
    public void Execute_Json_PromptDistinguishesSafeRepairFromOperatorStop()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("child-selector-label-gap", prompt, StringComparison.Ordinal);
        Assert.Contains("host-artifact-repair-required", prompt, StringComparison.Ordinal);
        Assert.Contains("operator stop", prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: the JSON output must include a structured repeated_stall_recovery field.
    /// </summary>
    [Fact]
    public void Execute_Json_EmitsRepeatedStallRecoveryStructuredField()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
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
        var exitCode = GuideWorkerIssueToPrCommand.Execute(
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
