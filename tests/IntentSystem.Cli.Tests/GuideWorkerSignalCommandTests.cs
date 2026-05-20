using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G374: tests for the read-only <c>guide worker signal</c> template
/// surface — it emits a template per signal kind, the canonical labels,
/// a marker example, and the host collection commands.
/// </summary>
public sealed class GuideWorkerSignalCommandTests
{
    [Fact]
    public void Execute_Json_EmitsTemplatePerKind_AndLabels()
    {
        using var writer = new StringWriter();
        var exit = GuideWorkerSignalCommand.Execute(
            CreateContext(),
            new[] { "--repo", "J-Tech-Japan/intent-system", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        var templates = root.GetProperty("templates");
        var kinds = templates.EnumerateArray().Select(t => t.GetProperty("kind").GetString()).ToHashSet();
        Assert.Contains("blocker", kinds);
        Assert.Contains("follow-up", kinds);
        Assert.Contains("scope-warning", kinds);

        Assert.Equal("intent-signal-sent", root.GetProperty("labels").GetProperty("sent").GetString());
        Assert.Equal("intent-signal-handled", root.GetProperty("labels").GetProperty("handled").GetString());
        Assert.Contains("intent-signal", root.GetProperty("marker_example").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HostCommandsReferenceCollectAndHandled()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideWorkerSignalCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer));

        using var doc = JsonDocument.Parse(writer.ToString());
        var commands = doc.RootElement.GetProperty("host_commands").EnumerateArray()
            .Select(c => c.GetString() ?? string.Empty).ToArray();
        Assert.Contains(commands, c => c.Contains("review collect-signals", StringComparison.Ordinal));
        Assert.Contains(commands, c => c.Contains("review signal-handled", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_RendersTemplatesAndCommandBlocks()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideWorkerSignalCommand.Execute(
            CreateContext(),
            new[] { "--format", "markdown" },
            writer));

        var output = writer.ToString();
        Assert.Contains("worker signal blocker", output, StringComparison.Ordinal);
        Assert.Contains("worker signal follow-up", output, StringComparison.Ordinal);
        Assert.Contains("worker signal scope-warning", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GuideWorkerRouter_DispatchesSignalSubcommand()
    {
        using var writer = new StringWriter();
        var exit = GuideWorkerCommand.Execute(
            CreateContext(),
            new[] { "signal", "--format", "json" },
            writer);

        Assert.Equal(0, exit);
        Assert.Contains("worker-signal", writer.ToString(), StringComparison.Ordinal);
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
