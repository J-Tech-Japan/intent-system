using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideCommandsListCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsTableForAllGroups()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide commands — top-level groups", output, StringComparison.Ordinal);
        Assert.Contains("| group | classification | mutability | caller | purpose |", output, StringComparison.Ordinal);
        foreach (var group in new[] { "guide", "intent", "interview", "packet", "worker", "automation", "metadata", "review", "closeout", "issue", "queue", "intake", "bug", "run" })
        {
            Assert.Contains(group, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_JsonFormat_EmitsClassifiedGroupArray()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var groups = document.RootElement.GetProperty("groups");
        Assert.True(groups.GetArrayLength() >= 18);

        var byName = groups.EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e);

        Assert.Equal("primary", byName["guide"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["intent"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["interview"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["packet"].GetProperty("classification").GetString());
        Assert.Equal("primary", byName["worker"].GetProperty("classification").GetString());

        Assert.Equal("support", byName["automation"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["metadata"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["review"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["closeout"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["issue"].GetProperty("classification").GetString());
        Assert.Equal("support", byName["queue"].GetProperty("classification").GetString());

        Assert.Equal("advanced", byName["run"].GetProperty("classification").GetString());
        Assert.Equal("advanced-runtime", byName["run"].GetProperty("recommended_caller").GetString());

        Assert.Equal("experimental", byName["intake"].GetProperty("classification").GetString());
        Assert.Equal("experimental", byName["bug"].GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_RoutedThroughGuideCommandsDispatcher_Works()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsCommand.Execute(
            CreateContext(),
            ["list", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.True(document.RootElement.GetProperty("groups").GetArrayLength() >= 18);
    }

    [Fact]
    public void Execute_GuideCommandsUnknownSubcommand_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsCommand.Execute(
            CreateContext(),
            ["explore"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown 'guide commands' subcommand 'explore'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GuideCommandsMissingSubcommand_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("guide commands requires a subcommand", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
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
        var exitCode = GuideCommandsListCommand.Execute(
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
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide commands list", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GroupsRegistry_CoversEveryClassificationValue()
    {
        var classifications = GuideCommandsListCommand.Groups
            .Select(g => g.Classification)
            .Distinct()
            .ToHashSet();

        Assert.Contains("primary", classifications);
        Assert.Contains("support", classifications);
        Assert.Contains("advanced", classifications);
        Assert.Contains("experimental", classifications);
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
