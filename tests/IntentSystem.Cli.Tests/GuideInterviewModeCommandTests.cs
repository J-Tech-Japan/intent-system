using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G381: tests for the persistent goal-seeking <c>intent-cli guide
/// interview-mode</c> surface — the question structure, research-first /
/// one-at-a-time / persist-to-ready agent behavior, the explicit stop
/// conditions, and the decision-record output, in markdown and JSON.
/// </summary>
public sealed class GuideInterviewModeCommandTests
{
    [Fact]
    public void Execute_Json_IncludesQuestionStructure_Behavior_AndStopConditions()
    {
        using var writer = new StringWriter();
        var exit = GuideInterviewModeCommand.Execute(CreateContext(), new[] { "--format", "json" }, writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.Equal("interview-mode", root.GetProperty("kind").GetString());

        // The eight question-structure elements the issue enumerates.
        var structure = root.GetProperty("question_structure").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty).ToArray();
        Assert.Equal(8, structure.Length);
        Assert.Contains(structure, s => s.Contains("Current understanding", StringComparison.Ordinal));
        Assert.Contains(structure, s => s.Contains("Why this question matters", StringComparison.Ordinal));
        Assert.Contains(structure, s => s.Contains("One focused question", StringComparison.Ordinal));
        Assert.Contains(structure, s => s.Contains("What the answer will decide", StringComparison.Ordinal));
        Assert.Contains(structure, s => s.Contains("Remaining gaps", StringComparison.Ordinal));

        // The five named stop conditions, in dependency-of-ready order.
        var stops = root.GetProperty("stop_conditions").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString() ?? string.Empty).ToArray();
        Assert.Equal(
            new[] { "packet-ready", "issue-ready", "clarification-required", "blocked-by-user-decision", "insufficient-context-after-research" },
            stops);
    }

    [Fact]
    public void Execute_Json_RequiresResearchFirst_AndPersistToReady()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideInterviewModeCommand.Execute(CreateContext(), new[] { "--format", "json" }, writer));

        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        var behavior = root.GetProperty("agent_behavior").EnumerateArray()
            .Select(b => b.GetString() ?? string.Empty).ToArray();
        // Research-before-question is required.
        Assert.Contains(behavior, b => b.Contains("Research first", StringComparison.Ordinal));
        // Persist-to-ready: a shallow answer is not a stopping point.
        Assert.Contains(behavior, b => b.Contains("Persist to ready", StringComparison.Ordinal));

        // The prompt makes clear that stopping after a shallow answer is not enough.
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains("shallow answer", prompt, StringComparison.OrdinalIgnoreCase);

        // The durable output is a decision record, not a transcript.
        var decision = root.GetProperty("decision_record").EnumerateArray()
            .Select(d => d.GetString() ?? string.Empty).ToArray();
        Assert.Contains(decision, d => d.Contains("decision record", StringComparison.OrdinalIgnoreCase) && d.Contains("transcript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(decision, d => d.Contains("interview record-answer", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_RendersAllSectionsAndStopConditions()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideInterviewModeCommand.Execute(CreateContext(), new[] { "--format", "markdown" }, writer));

        var output = writer.ToString();
        Assert.Contains("# Guide — persistent goal-seeking interview mode", output, StringComparison.Ordinal);
        Assert.Contains("## Question structure", output, StringComparison.Ordinal);
        Assert.Contains("## Agent behavior", output, StringComparison.Ordinal);
        Assert.Contains("## Stop conditions", output, StringComparison.Ordinal);
        Assert.Contains("## Decision record", output, StringComparison.Ordinal);
        Assert.Contains("`packet-ready`", output, StringComparison.Ordinal);
        Assert.Contains("`insufficient-context-after-research`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DefaultFormatIsMarkdown()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideInterviewModeCommand.Execute(CreateContext(), Array.Empty<string>(), writer));
        Assert.Contains("# Guide — persistent goal-seeking interview mode", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownFormat()
    {
        using var writer = new StringWriter();
        var exit = GuideInterviewModeCommand.Execute(CreateContext(), new[] { "--format", "yaml" }, writer);
        Assert.Equal(1, exit);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Router_DispatchesGuideInterviewMode_ExitZero()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["guide", "interview-mode", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exit);
        Assert.Contains("interview-mode", writer.ToString(), StringComparison.Ordinal);
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
