using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideRulesCommandTests
{
    [Theory]
    [InlineData("label-ownership")]
    [InlineData("child-issue-contract")]
    [InlineData("clarification")]
    [InlineData("review-closeout")]
    [InlineData("intake-interview")]
    public void Execute_GivenSupportedTopicMarkdown_EmitsAllSections(string topic)
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--topic", topic, "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains($"# Guide rules — {topic}", output, StringComparison.Ordinal);
        Assert.Contains("## Summary", output, StringComparison.Ordinal);
        Assert.Contains("## Source references", output, StringComparison.Ordinal);
        Assert.Contains("## Installed command hints", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_LabelOwnershipJson_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--topic", "label-ownership", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("label-ownership", root.GetProperty("topic").GetString());
        Assert.True(root.GetProperty("summary").GetArrayLength() >= 3);
        Assert.True(root.GetProperty("source_references").GetArrayLength() >= 1);
        Assert.True(root.GetProperty("installed_command_hints").GetArrayLength() >= 2);
    }

    [Fact]
    public void Execute_ReviewCloseoutMarkdown_MentionsClosingCommands()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--topic", "review-closeout"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli review closeout-plan", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli closeout pr", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide review", output, StringComparison.Ordinal);

        // G472: the review-closeout topic must no longer point at the historical
        // local `intent-review-closeout` skill as a source reference. It may only
        // appear in a sentence that explicitly marks it as a non-canonical adapter.
        foreach (var line in output.Split('\n'))
        {
            if (line.Contains("intent-review-closeout", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("convenience adapters", line, StringComparison.Ordinal);
            }
        }
        Assert.DoesNotContain("intents/rules/skills/intent-review-closeout.md", output, StringComparison.Ordinal);

        // CI-pending deferral guidance is recorded as a closeout rule.
        Assert.Contains("CI-pending is a defer condition", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingTopic_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--format", "markdown"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--topic is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedTopic_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--topic", "merge-strategy"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unsupported --topic 'merge-strategy'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--topic", "label-ownership", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--topic", "label-ownership", "--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsageAndAllTopics()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide rules", output, StringComparison.Ordinal);
        foreach (var topic in new[] { "label-ownership", "child-issue-contract", "clarification", "review-closeout", "intake-interview" })
        {
            Assert.Contains(topic, output, StringComparison.Ordinal);
        }
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
