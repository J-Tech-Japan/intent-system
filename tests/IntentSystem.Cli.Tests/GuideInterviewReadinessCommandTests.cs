using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G382: tests for the <c>intent-cli guide interview-readiness</c>
/// surface — the static checklist (no input), the evaluated verdict with
/// concrete missing dimensions + next question (with input), and both
/// output formats.
/// </summary>
public sealed class GuideInterviewReadinessCommandTests
{
    [Fact]
    public void Execute_NoResolved_Json_EmitsChecklistWithElevenDimensions()
    {
        using var writer = new StringWriter();
        var exit = GuideInterviewReadinessCommand.Execute(CreateContext(), new[] { "--format", "json" }, writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var dimensions = doc.RootElement.GetProperty("dimensions").EnumerateArray().ToArray();
        Assert.Equal(11, dimensions.Length);
        // Every dimension carries a tier classification.
        Assert.Contains(dimensions, d => d.GetProperty("key").GetString() == "acceptance" && d.GetProperty("tier").GetString() == "issue");
        Assert.Contains(dimensions, d => d.GetProperty("key").GetString() == "risks" && d.GetProperty("tier").GetString() == "packet");
        Assert.Contains(dimensions, d => d.GetProperty("key").GetString() == "owner-decision" && d.GetProperty("tier").GetString() == "blocking");
    }

    [Fact]
    public void Execute_ResolvedAll_Json_ClassifiesPacketReady_WithNoMissing()
    {
        using var writer = new StringWriter();
        var exit = GuideInterviewReadinessCommand.Execute(
            CreateContext(),
            new[]
            {
                "--resolved",
                "owner-decision,open-decisions,goal,scope,non-goals,constraints,target,acceptance,verification,dependencies,risks",
                "--format", "json",
            },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("packet-ready", doc.RootElement.GetProperty("classification").GetString());
        Assert.Empty(doc.RootElement.GetProperty("missing_dimensions").EnumerateArray());
    }

    [Fact]
    public void Execute_ResolvedPartial_Json_ClassifiesRemainingGaps_ListsMissing_AndNextQuestion()
    {
        using var writer = new StringWriter();
        var exit = GuideInterviewReadinessCommand.Execute(
            CreateContext(),
            new[] { "--resolved", "owner-decision,open-decisions,goal,scope", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("remaining-gaps", root.GetProperty("classification").GetString());

        var missing = root.GetProperty("missing_dimensions").EnumerateArray().Select(m => m.GetString()).ToArray();
        Assert.Contains("target", missing);
        Assert.Contains("acceptance", missing);
        Assert.Contains("verification", missing);

        Assert.Equal("target", root.GetProperty("next_question_dimension").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("next_question").GetString()));
    }

    [Fact]
    public void Execute_Markdown_NoResolved_RendersChecklistAndLegend_NoVerdict()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideInterviewReadinessCommand.Execute(CreateContext(), Array.Empty<string>(), writer));

        var output = writer.ToString();
        Assert.Contains("# Guide — interview readiness checklist", output, StringComparison.Ordinal);
        Assert.Contains("## Dimensions", output, StringComparison.Ordinal);
        Assert.Contains("## Classification legend", output, StringComparison.Ordinal);
        // Without --resolved there is no evaluated verdict header.
        Assert.DoesNotContain("## Verdict", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Resolved_RendersVerdictAndCheckboxes()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideInterviewReadinessCommand.Execute(
            CreateContext(),
            new[] { "--resolved", "owner-decision,open-decisions,goal,scope" },
            writer));

        var output = writer.ToString();
        Assert.Contains("## Verdict: `remaining-gaps`", output, StringComparison.Ordinal);
        Assert.Contains("[x] goal", output, StringComparison.Ordinal);
        Assert.Contains("[ ] target", output, StringComparison.Ordinal);
        Assert.Contains("## Next question", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownFormat()
    {
        using var writer = new StringWriter();
        var exit = GuideInterviewReadinessCommand.Execute(CreateContext(), new[] { "--format", "yaml" }, writer);
        Assert.Equal(1, exit);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Router_DispatchesGuideInterviewReadiness_ExitZero()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["guide", "interview-readiness", "--resolved", "goal,scope", "--format", "json"],
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
