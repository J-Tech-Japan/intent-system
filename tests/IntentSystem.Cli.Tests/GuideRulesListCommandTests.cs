using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideRulesListCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsTopicTable()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesListCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide rules — supported topics", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide rules --topic <id>", output, StringComparison.Ordinal);
        Assert.Contains("| id | category | title | description |", output, StringComparison.Ordinal);
        foreach (var topic in new[] { "label-ownership", "child-issue-contract", "clarification", "review-closeout", "intake-interview" })
        {
            Assert.Contains(topic, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_JsonFormat_EmitsStructuredTopicList()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesListCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var topics = document.RootElement.GetProperty("topics");
        Assert.Equal(5, topics.GetArrayLength());
        var ids = topics.EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Contains("label-ownership", ids);
        Assert.Contains("child-issue-contract", ids);
        Assert.Contains("clarification", ids);
        Assert.Contains("review-closeout", ids);
        Assert.Contains("intake-interview", ids);

        var categories = topics.EnumerateArray().Select(e => e.GetProperty("category").GetString()).ToArray();
        Assert.Contains("automation", categories);
        Assert.Contains("issue-contract", categories);
        Assert.Contains("review", categories);
        Assert.Contains("interview", categories);
    }

    [Fact]
    public void Execute_RoutedThroughGuideRulesNested_EmitsList()
    {
        // `intent-cli guide rules list --format json` is dispatched through
        // GuideRulesCommand which delegates to the list command.
        using var writer = new StringWriter();
        var exitCode = GuideRulesCommand.Execute(
            CreateContext(),
            ["list", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(5, document.RootElement.GetProperty("topics").GetArrayLength());
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesListCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesListCommand.Execute(
            CreateContext(),
            ["--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesListCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide rules list", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TopicIds_MatchGuideRulesTopicCommandRegistry()
    {
        // Each id listed by `guide rules list` must be resolvable by `guide rules --topic`.
        foreach (var topic in GuideRulesListCommand.Topics)
        {
            using var writer = new StringWriter();
            var exitCode = GuideRulesCommand.Execute(
                CreateContext(),
                ["--topic", topic.Id, "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Contains($"\"topic\": \"{topic.Id}\"", writer.ToString(), StringComparison.Ordinal);
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
