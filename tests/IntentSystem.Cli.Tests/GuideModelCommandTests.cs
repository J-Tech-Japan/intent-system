using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideModelCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsAllSections()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide model — primary collaboration model", output, StringComparison.Ordinal);
        Assert.Contains("## Primary model", output, StringComparison.Ordinal);
        Assert.Contains("## Roles", output, StringComparison.Ordinal);
        Assert.Contains("## Primary data paths", output, StringComparison.Ordinal);
        Assert.Contains("## Optional advanced runtime", output, StringComparison.Ordinal);
        Assert.Contains("## Hard rules", output, StringComparison.Ordinal);

        Assert.Contains("Human product owner", output, StringComparison.Ordinal);
        Assert.Contains("Codex / Claude coding agent", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("Host git repository", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Contains("chat-first", root.GetProperty("primary_model").GetString()!, StringComparison.Ordinal);
        Assert.Equal(4, root.GetProperty("roles").GetArrayLength());
        Assert.True(root.GetProperty("primary_data_paths").GetArrayLength() >= 4);
        Assert.True(root.GetProperty("optional_advanced_runtime").GetArrayLength() >= 2);
        Assert.True(root.GetProperty("hard_rules").GetArrayLength() >= 3);

        var roles = root.GetProperty("roles").EnumerateArray().Select(e => e.GetProperty("actor").GetString()).ToArray();
        Assert.Contains("Human product owner", roles);
        Assert.Contains("Codex / Claude coding agent", roles);
        Assert.Contains("intent-cli", roles);
        Assert.Contains("Host git repository", roles);
    }

    [Fact]
    public void Execute_Output_NamesIntentCliRunAsOptional()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var optional = document.RootElement.GetProperty("optional_advanced_runtime")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(optional, item => item!.Contains("intent-cli run", StringComparison.Ordinal));
        Assert.Contains(optional, item => item!.Contains("subprocess", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(optional, item => item!.Contains("Supervisor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_HardRules_NameLabelOwnershipAndProviderBoundary()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rules = document.RootElement.GetProperty("hard_rules")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(rules, rule => rule!.Contains("must not launch Codex/Claude", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule!.Contains("intent-target", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule!.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
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
        var exitCode = GuideModelCommand.Execute(
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
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide model", writer.ToString(), StringComparison.Ordinal);
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
