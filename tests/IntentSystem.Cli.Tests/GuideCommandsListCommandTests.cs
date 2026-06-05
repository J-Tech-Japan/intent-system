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
        // G467: the table now carries the operator-role column.
        Assert.Contains("| group | role | classification | mutability | caller | purpose |", output, StringComparison.Ordinal);
        Assert.Contains("Operator-role categories (G467)", output, StringComparison.Ordinal);
        foreach (var group in new[] { "guide", "intent", "interview", "packet", "worker", "automation", "metadata", "review", "closeout", "issue", "queue", "grill", "stack", "next", "inspect" })
        {
            Assert.Contains(group, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_Json_IncludesRoleMetadataAndDesignSideCommands()
    {
        // G467: every entry carries an operator-role category, and the new
        // design-side commands appear in the catalog.
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var groups = document.RootElement.GetProperty("groups");
        var byName = groups.EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString()!, e => e);

        // New design-side commands present and categorized as design.
        foreach (var name in new[] { "grill", "stack", "next", "inspect", "improve" })
        {
            Assert.True(byName.ContainsKey(name), $"catalog missing '{name}'");
            Assert.Equal("design", byName[name].GetProperty("role").GetString());
        }

        // Role coverage spans all five operator-role categories.
        var roles = groups.EnumerateArray().Select(e => e.GetProperty("role").GetString()).ToHashSet();
        foreach (var role in new[] { "design", "host-review", "child-implementation", "recovery-diagnostics", "advanced-developer" })
        {
            Assert.Contains(role, roles);
        }

        // worker is the child-implementation surface.
        Assert.Equal("child-implementation", byName["worker"].GetProperty("role").GetString());

        // Loop-prompt creation surfaces are discoverable in the catalog —
        // prompt-template AND prompt-matrix each as their own role-categorized
        // entry (G467 review: prompt-matrix must be a discoverable entry, not
        // only mentioned inside prompt-template's purpose text).
        Assert.Contains("guide workflow task implementation-loop", byName.Keys);
        Assert.Contains("guide workflow task review-next-slice-loop", byName.Keys);
        Assert.Contains("guide prompt-template", byName.Keys);
        Assert.Contains("guide prompt-matrix", byName.Keys);
        Assert.Equal("design", byName["guide prompt-matrix"].GetProperty("role").GetString());
        Assert.Equal("support", byName["guide prompt-matrix"].GetProperty("classification").GetString());
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
        Assert.True(groups.GetArrayLength() >= 11);

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
        Assert.True(document.RootElement.GetProperty("groups").GetArrayLength() >= 11);
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
