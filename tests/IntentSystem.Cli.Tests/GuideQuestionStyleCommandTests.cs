using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G380: tests for the direct <c>intent-cli guide question-style</c>
/// surface — the required elements, the copyable template, the
/// record-after-answer pointer, and the discourage-old-interview-probing
/// guidance, in both markdown and JSON.
/// </summary>
public sealed class GuideQuestionStyleCommandTests
{
    [Fact]
    public void Execute_Json_IncludesRequiredElements_Template_AndRecording()
    {
        using var writer = new StringWriter();
        var exit = GuideQuestionStyleCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.Equal("question-style", root.GetProperty("kind").GetString());

        var elements = root.GetProperty("required_elements").EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty).ToArray();
        // The seven required elements the issue enumerates.
        Assert.Contains(elements, e => e.Contains("Restate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(elements, e => e.Contains("ONE focused question", StringComparison.Ordinal));
        Assert.Contains(elements, e => e.Contains("options", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(elements, e => e.Contains("tradeoffs", StringComparison.OrdinalIgnoreCase) || e.Contains("pros/cons", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(elements, e => e.Contains("recommendation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(elements, e => e.Contains("recorded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(elements, e => e.Contains("clarification-required", StringComparison.Ordinal));

        // Copyable template with a single focused question + options.
        var template = root.GetProperty("template").GetString()!;
        Assert.Contains("Understood request:", template, StringComparison.Ordinal);
        Assert.Contains("Question:", template, StringComparison.Ordinal);
        Assert.Contains("Options:", template, StringComparison.Ordinal);
        Assert.Contains("Recommendation:", template, StringComparison.Ordinal);

        // Recording happens only after the answer, via interview record-answer.
        var recording = root.GetProperty("recording").EnumerateArray()
            .Select(r => r.GetString() ?? string.Empty).ToArray();
        Assert.Contains(recording, r => r.Contains("interview record-answer", StringComparison.Ordinal));
        Assert.Contains(recording, r => r.Contains("after the user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_Json_DiscouragesProbingOldInterviewCommands()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideQuestionStyleCommand.Execute(CreateContext(), new[] { "--format", "json" }, writer));

        using var doc = JsonDocument.Parse(writer.ToString());
        var avoid = doc.RootElement.GetProperty("avoid_probing").EnumerateArray()
            .Select(a => a.GetString() ?? string.Empty).ToArray();
        Assert.Contains(avoid, a => a.Contains("interview start", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_RendersAllSectionsAndTemplateFence()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideQuestionStyleCommand.Execute(CreateContext(), new[] { "--format", "markdown" }, writer));

        var output = writer.ToString();
        Assert.Contains("# Guide — clarification question style", output, StringComparison.Ordinal);
        Assert.Contains("## Required elements", output, StringComparison.Ordinal);
        Assert.Contains("## Copyable question template", output, StringComparison.Ordinal);
        Assert.Contains("## Recording the answer", output, StringComparison.Ordinal);
        Assert.Contains("Question:", output, StringComparison.Ordinal);
        Assert.Contains("interview record-answer", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DefaultFormatIsMarkdown()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideQuestionStyleCommand.Execute(CreateContext(), Array.Empty<string>(), writer));
        Assert.Contains("# Guide — clarification question style", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownFormat()
    {
        using var writer = new StringWriter();
        var exit = GuideQuestionStyleCommand.Execute(CreateContext(), new[] { "--format", "yaml" }, writer);
        Assert.Equal(1, exit);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Router_DispatchesGuideQuestionStyle_ExitZero()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["guide", "question-style", "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exit);
        Assert.Contains("question-style", writer.ToString(), StringComparison.Ordinal);
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
