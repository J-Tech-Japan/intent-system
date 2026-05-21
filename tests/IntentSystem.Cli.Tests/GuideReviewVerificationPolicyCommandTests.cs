using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G383: tests for the <c>intent-cli guide review-verification-policy</c>
/// surface — the static classification legend (no input), the evaluated
/// decision/route (with input), and both output formats.
/// </summary>
public sealed class GuideReviewVerificationPolicyCommandTests
{
    [Fact]
    public void Execute_NoInput_Json_ListsThreeClassificationsAndSummaryRequirements()
    {
        using var writer = new StringWriter();
        var exit = GuideReviewVerificationPolicyCommand.Execute(CreateContext(), new[] { "--format", "json" }, writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("review-verification-policy", root.GetProperty("kind").GetString());

        var names = root.GetProperty("classifications").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToArray();
        Assert.Contains("standing-policy-approve", names);
        Assert.Contains("implementation-finding", names);
        Assert.Contains("review-policy-gap", names);

        // No evaluated decision without input.
        Assert.False(root.TryGetProperty("decision", out _));

        // Summary must require stating what was verified vs not run.
        var requirements = root.GetProperty("summary_requirements").EnumerateArray()
            .Select(r => r.GetString() ?? string.Empty).ToArray();
        Assert.Contains(requirements, r => r.Contains("NOT run", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_StandingPolicyWithEvidence_Json_ApprovesViaProceedRoute()
    {
        using var writer = new StringWriter();
        var exit = GuideReviewVerificationPolicyCommand.Execute(
            CreateContext(),
            new[] { "--standing-policy", "--evidence", "source-mapping", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var decision = doc.RootElement.GetProperty("decision");
        Assert.Equal("standing-policy-approve", decision.GetProperty("decision").GetString());
        Assert.Equal("proceed-approve", decision.GetProperty("route").GetString());
    }

    [Fact]
    public void Execute_PolicyGap_Json_RoutesToDurableHostSignalRecordedOnce()
    {
        using var writer = new StringWriter();
        var exit = GuideReviewVerificationPolicyCommand.Execute(
            CreateContext(),
            new[] { "--evidence", "none", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var decision = doc.RootElement.GetProperty("decision");
        Assert.Equal("review-policy-gap", decision.GetProperty("decision").GetString());
        Assert.Equal("host-durable-signal-once", decision.GetProperty("route").GetString());
        Assert.True(decision.GetProperty("record_host_gap_once").GetBoolean());
        Assert.False(decision.GetProperty("post_pr_feedback").GetBoolean());
    }

    [Fact]
    public void Execute_Markdown_NoInput_RendersProtocolAndClassifications()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideReviewVerificationPolicyCommand.Execute(CreateContext(), Array.Empty<string>(), writer));

        var output = writer.ToString();
        Assert.Contains("# Guide — host review visible-verification policy", output, StringComparison.Ordinal);
        Assert.Contains("## Classifications", output, StringComparison.Ordinal);
        Assert.Contains("do NOT ask the operator", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Decision:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_WithEvidence_RendersDecision()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideReviewVerificationPolicyCommand.Execute(
            CreateContext(),
            new[] { "--evidence", "none" },
            writer));

        Assert.Contains("## Decision: `review-policy-gap`", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Router_DispatchesGuideReviewVerificationPolicy_ExitZero()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["guide", "review-verification-policy", "--standing-policy", "--evidence", "source-mapping", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not yet implemented", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Directory.GetCurrentDirectory(),
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" },
        },
    };
}
