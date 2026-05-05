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
