using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideAutomationCommandTests
{
    [Fact]
    public void Execute_HostReview_IntentCli_EmitsRecurringPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "host-review", "--domain", "intent-cli", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Automation: Host Review & Next Slice — intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("recurring schedule", output, StringComparison.Ordinal);
        Assert.Contains("domain: `intent-cli`", output, StringComparison.Ordinal);
        Assert.Contains("child repo: `J-Tech-Japan/intent-system`", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation summary --format text", output, StringComparison.Ordinal);
        Assert.Contains("Stage 1", output, StringComparison.Ordinal);
        Assert.Contains("Stage 2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReview_SekibanAsAService_EmitsDomainSpecificPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "host-review", "--domain", "sekiban-as-a-service"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("domain: `sekiban-as-a-service`", output, StringComparison.Ordinal);
        Assert.Contains("submodules/SekibanAsAService", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementUpdate_IntentSystem_EmitsRecurringWakePrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-update", "--repo", "J-Tech-Japan/intent-system", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Automation: Child Implement & Update Loop", output, StringComparison.Ordinal);
        Assert.Contains("recurring schedule", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli worker next-action", output, StringComparison.Ordinal);
        Assert.Contains("Run this loop from a `J-Tech-Japan/intent-system` child worktree root.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementUpdate_SekibanAsAService_EmbedsRepoNote()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-update", "--repo", "J-Tech-Japan/SekibanAsAService"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Run this loop from a `J-Tech-Japan/SekibanAsAService` child worktree root.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingKind_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
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
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "host", "--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--kind must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReview_MissingDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "host-review"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--domain is required for --kind host-review.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HostReview_UnsupportedDomain_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "host-review", "--domain", "unknown"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --domain 'unknown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementUpdate_MissingRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-update"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo is required for --kind child-implement-update.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildImplementUpdate_UnsupportedRepo_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "child-implement-update", "--repo", "other/repo"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --repo 'other/repo'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--kind", "host-review", "--domain", "intent-cli", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsageAndExitsZero()
    {
        using var writer = new StringWriter();
        var exitCode = GuideAutomationCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide automation", output, StringComparison.Ordinal);
        Assert.Contains("host-review", output, StringComparison.Ordinal);
        Assert.Contains("child-implement-update", output, StringComparison.Ordinal);
    }

    // ── G270 stale-guidance regression ───────────────────────────────────────

    [Fact]
    public void HostReviewIntentCliPrompt_DoesNotContainHardCodedHostPath()
    {
        Assert.DoesNotContain("/Users/tomohisa", GuideAutomationCommand.HostReviewIntentCliPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostReviewIntentCliPrompt_DoesNotContainStaleRuleFileReference()
    {
        Assert.DoesNotContain("intents/rules/automations/host-review-loop.md", GuideAutomationCommand.HostReviewIntentCliPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostReviewSekibanAsAServicePrompt_DoesNotContainHardCodedHostPath()
    {
        Assert.DoesNotContain("/Users/tomohisa", GuideAutomationCommand.HostReviewSekibanAsAServicePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostReviewSekibanAsAServicePrompt_DoesNotContainStaleRuleFileReference()
    {
        Assert.DoesNotContain("intents/rules/automations/host-review-loop.md", GuideAutomationCommand.HostReviewSekibanAsAServicePrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildImplementUpdateBody_DoesNotContainHardCodedHostPath()
    {
        Assert.DoesNotContain("/Users/tomohisa", GuideAutomationCommand.ChildImplementUpdateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildImplementUpdateBody_DoesNotRecommendLocalSkillFiles()
    {
        Assert.DoesNotContain("You may use implementation skills such as", GuideAutomationCommand.ChildImplementUpdateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void HostReviewIntentCliPrompt_DoesNotContainIntentNextSliceSkillRef()
    {
        Assert.DoesNotContain("intent-next-slice", GuideAutomationCommand.HostReviewIntentCliPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostReviewSekibanAsAServicePrompt_DoesNotContainIntentNextSliceSkillRef()
    {
        Assert.DoesNotContain("intent-next-slice", GuideAutomationCommand.HostReviewSekibanAsAServicePrompt, StringComparison.Ordinal);
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
