using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideOneshotCommandTests
{
    [Fact]
    public void Execute_HostReviewNextSlice_IntentCli_EmitsUsablePrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# One-shot: Host Review and Next Slice — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("domain: `intent-cli`", output, StringComparison.Ordinal);
        Assert.Contains("child repo: `J-Tech-Japan/intent-system`", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation summary --format text", output, StringComparison.Ordinal);
        Assert.Contains("Stage 1 review/closeout, then Stage 2 next-slice", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSlice_SekibanAsAService_EmitsDomainSpecificPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "sekiban-as-a-service"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("domain: `sekiban-as-a-service`", output, StringComparison.Ordinal);
        Assert.Contains("submodules/SekibanAsAService", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementOrUpdate_IntentSystem_EmitsUsablePrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-or-update", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# One-shot: Child Implement or PR Comment Update", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker next-action --repo <OWNER>/<REPO> --format json", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker result-summary", output, StringComparison.Ordinal);
        Assert.Contains("`intent-pr-created` belongs to the source issue only", output, StringComparison.Ordinal);
        Assert.Contains("Run this prompt from a `J-Tech-Japan/intent-system` child worktree root.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementOrUpdate_SekibanAsAService_EmbedsRepoNote()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-or-update", "--repo", "J-Tech-Japan/SekibanAsAService"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Run this prompt from a `J-Tech-Japan/SekibanAsAService` child worktree root.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind is required.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review", "--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSlice_MissingDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain is required for --kind host-review-next-slice.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReviewNextSlice_UnsupportedDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "unknown"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --domain 'unknown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementOrUpdate_MissingRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-or-update"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required for --kind child-implement-or-update.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementOrUpdate_UnsupportedRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-or-update", "--repo", "other/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --repo 'other/repo'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsageAndExitsZero()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide oneshot", output, StringComparison.Ordinal);
        Assert.Contains("host-review-next-slice", output, StringComparison.Ordinal);
        Assert.Contains("child-implement-or-update", output, StringComparison.Ordinal);
    }

    // ── G270 stale-guidance regression ───────────────────────────────────────

    [Fact]
    public void HostIntentCliPrompt_DoesNotContainHardCodedHostPath()
    {
        Assert.DoesNotContain("/Users/tomohisa", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostIntentCliPrompt_DoesNotContainStaleRuleFileReference()
    {
        Assert.DoesNotContain("intents/rules/automations/runbook.md", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostSekibanAsAServicePrompt_DoesNotContainHardCodedHostPath()
    {
        Assert.DoesNotContain("/Users/tomohisa", GuideOneshotCommand.HostSekibanAsAServicePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostSekibanAsAServicePrompt_DoesNotContainStaleRuleFileReference()
    {
        Assert.DoesNotContain("intents/rules/automations/runbook.md", GuideOneshotCommand.HostSekibanAsAServicePrompt, StringComparison.Ordinal);
    }

    // ── G402 publish ownership non-interactive contract ───────────────────────

    /// <summary>
    /// G402: the host review/next-slice prompt for intent-cli must instruct the
    /// operator NOT to ask about publish ownership when a concurrent publisher
    /// is observed.
    /// </summary>
    [Fact]
    public void HostIntentCliPrompt_ContainsPublishOwnershipRule_DoNotAskOperator()
    {
        // The prompt must say publish ownership is never a question for the
        // operator chat, so automated host loops never pause to ask.
        Assert.Contains(
            "do NOT ask the operator",
            GuideOneshotCommand.HostIntentCliPrompt,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Publish ownership follows",
            GuideOneshotCommand.HostIntentCliPrompt,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// G402: the host review/next-slice prompt must encode that publish
    /// ownership disambiguation is NOT a valid clarification reason.
    /// </summary>
    [Fact]
    public void HostIntentCliPrompt_PublishOwnershipDisambiguationNotAClarificationReason()
    {
        Assert.Contains(
            "Publish ownership disambiguation is NOT a valid clarification reason",
            GuideOneshotCommand.HostIntentCliPrompt,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// G402: the guidance emitted by Execute for intent-cli domain must
    /// include the non-interactive publish ownership rule so an automation
    /// that observes a concurrent publisher proceeds without asking.
    /// </summary>
    [Fact]
    public void Execute_HostReviewNextSlice_IntentCli_EmitsConcurrentPublisherOwnershipRule()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // Must explicitly prohibit asking the operator.
        Assert.Contains("do NOT ask the operator", output, StringComparison.OrdinalIgnoreCase);
        // Must reference the concurrent publisher scenario.
        Assert.Contains("concurrent", output, StringComparison.OrdinalIgnoreCase);
        // Must name intent-cli as the source of truth for publish ownership.
        Assert.Contains("intent-cli", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single source of truth", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G402: same non-interactive ownership rule must be present in the
    /// sekiban-as-a-service host prompt.
    /// </summary>
    [Fact]
    public void HostSekibanAsAServicePrompt_ContainsPublishOwnershipRule_DoNotAskOperator()
    {
        Assert.Contains(
            "do NOT ask the operator",
            GuideOneshotCommand.HostSekibanAsAServicePrompt,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Publish ownership disambiguation is NOT a valid clarification reason",
            GuideOneshotCommand.HostSekibanAsAServicePrompt,
            StringComparison.Ordinal);
    }

    // ── G408 repeated-stall recovery ─────────────────────────────────────────

    /// <summary>
    /// G408: the host intent-cli oneshot prompt must contain repeated-stall
    /// recovery guidance that triggers after two or more consecutive identical stalls.
    /// </summary>
    [Fact]
    public void HostIntentCliPrompt_ContainsRepeatedStallRecoveryGuidance()
    {
        Assert.Contains("Repeated-stall recovery", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("two or more consecutive", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli automation doctor", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.Ordinal);
        Assert.Contains("operator stop", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: the host intent-cli prompt must distinguish safe host-loop-owned repairs
    /// from operator-escalation stops.
    /// </summary>
    [Fact]
    public void HostIntentCliPrompt_DistinguishesSafeRepairFromOperatorStop()
    {
        Assert.Contains("intent-cli automation", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.Ordinal);
        Assert.Contains("operator", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured operator stop", GuideOneshotCommand.HostIntentCliPrompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: the sekiban-as-a-service host prompt must also contain repeated-stall recovery.
    /// </summary>
    [Fact]
    public void HostSekibanAsAServicePrompt_ContainsRepeatedStallRecoveryGuidance()
    {
        Assert.Contains("Repeated-stall recovery", GuideOneshotCommand.HostSekibanAsAServicePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("two or more consecutive", GuideOneshotCommand.HostSekibanAsAServicePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli automation doctor", GuideOneshotCommand.HostSekibanAsAServicePrompt, StringComparison.Ordinal);
        Assert.Contains("structured operator stop", GuideOneshotCommand.HostSekibanAsAServicePrompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: the child implement-or-update oneshot prompt must contain repeated-stall
    /// recovery guidance specific to the child loop.
    /// </summary>
    [Fact]
    public void ChildPromptBody_ContainsRepeatedStallRecoveryGuidance()
    {
        Assert.Contains("Repeated-stall recovery", GuideOneshotCommand.ChildPromptBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("two or more consecutive", GuideOneshotCommand.ChildPromptBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli worker issue-preflight", GuideOneshotCommand.ChildPromptBody, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker pr-comment-preflight", GuideOneshotCommand.ChildPromptBody, StringComparison.Ordinal);
        Assert.Contains("child-selector-label-gap", GuideOneshotCommand.ChildPromptBody, StringComparison.Ordinal);
        Assert.Contains("structured operator stop", GuideOneshotCommand.ChildPromptBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: the child oneshot prompt must limit recovery to at most one repair per cycle.
    /// </summary>
    [Fact]
    public void ChildPromptBody_LimitsRecoveryToOneRepairPerCycle()
    {
        Assert.Contains("at most one guided repair", GuideOneshotCommand.ChildPromptBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G408: executing the child implement-or-update prompt must emit the recovery guidance.
    /// </summary>
    [Fact]
    public void Execute_ChildImplementOrUpdate_EmitsRepeatedStallRecoveryGuidance()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-or-update", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Repeated-stall recovery", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("child-selector-label-gap", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// G408: executing the host review prompt must emit the recovery guidance.
    /// </summary>
    [Fact]
    public void Execute_HostReviewNextSlice_IntentCli_EmitsRepeatedStallRecoveryGuidance()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOneshotCommand.Execute(
            CreateContext(),
            ["--kind", "host-review-next-slice", "--domain", "intent-cli", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Repeated-stall recovery", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured operator stop", output, StringComparison.OrdinalIgnoreCase);
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
