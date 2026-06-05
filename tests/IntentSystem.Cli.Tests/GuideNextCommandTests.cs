using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G465: tests for the design-side action advisor guide surface.
/// </summary>
public sealed class GuideNextCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_DescribesDecisionSetAndReadOnly()
    {
        using var writer = new StringWriter();
        var exitCode = GuideNextCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide next", output, StringComparison.Ordinal);
        // AC: the natural-language ask is present verbatim.
        Assert.Contains("次に何をしたらいいか教えてください", output, StringComparison.Ordinal);
        // AC: read-only by default.
        Assert.Contains("read-only: yes", output, StringComparison.Ordinal);
        // AC: decision set explains when to choose each process.
        Assert.Contains("Decision set", output, StringComparison.Ordinal);
        Assert.Contains("Recommendation output shape", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_IncludesAllEightDesignSideActions()
    {
        using var writer = new StringWriter();
        var exitCode = GuideNextCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("design-action-next-advisor", root.GetProperty("process").GetString());
        Assert.True(root.GetProperty("read_only").GetBoolean());

        // AC: decision set includes grill, stack, improve, inspect, issue-publish, review, recovery, idle.
        var actions = root.GetProperty("decision_set").EnumerateArray()
            .Select(a => a.GetProperty("action").GetString()).ToArray();
        foreach (var required in new[]
                 {
                     "grill", "stack", "improve", "inspect",
                     "issue-publish", "review", "recovery", "idle",
                 })
        {
            Assert.Contains(required, actions);
        }

        // AC: every action carries a paste-ready suggested prompt.
        foreach (var a in root.GetProperty("decision_set").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(a.GetProperty("suggested_prompt").GetString()));
        }

        // G467 AC: inspect routes to evidence-backed observation and points to
        // `intent-cli inspect`, NOT to status / next-slice checking.
        var inspect = root.GetProperty("decision_set").EnumerateArray()
            .Single(a => a.GetProperty("action").GetString() == "inspect");
        Assert.Contains("evidence-backed", inspect.GetProperty("when_to_choose").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("intent-cli inspect", inspect.GetProperty("suggested_prompt").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_RecommendationOutputShapeAndSafetyBoundaryPresent()
    {
        using var writer = new StringWriter();
        var exitCode = GuideNextCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        // AC: output includes recommended action, reason, evidence, suggested prompt, safety boundary.
        var fields = root.GetProperty("recommendation_output_shape").EnumerateArray()
            .Select(f => f.GetProperty("field").GetString()).ToArray();
        Assert.Equal(
            new[] { "recommended_action", "reason", "evidence_checked", "suggested_prompt", "safety_boundary" },
            fields);

        // Read-only safety boundary present + do-not-substitute unavailable signal.
        var safety = string.Join("\n", root.GetProperty("safety_boundary").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Read-only", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No auto-execute", safety, StringComparison.OrdinalIgnoreCase);

        var doNotSubstitute = string.Join("\n", root.GetProperty("do_not_substitute").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("next advisor unavailable", doNotSubstitute, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomain_SubstitutesDomainInEvidence()
    {
        using var writer = new StringWriter();
        var exitCode = GuideNextCommand.Execute(CreateContext(), ["--domain", "aic", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var evidence = string.Join("\n", doc.RootElement.GetProperty("evidence_to_check").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("intents/aic/", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideNextCommand.Execute(CreateContext(), ["--format", "yaml"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideNextCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("next", output, StringComparison.Ordinal);
        Assert.Contains("action advisor", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuideHelp_ListsNextSubcommand_ForDiscoverability()
    {
        using var writer = new StringWriter();
        var exitCode = GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var names = doc.RootElement.GetProperty("subcommands").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        Assert.Contains("next", names);
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
