using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G463: tests for the persistent grill interview-mode guide surface.
/// </summary>
public sealed class GuideGrillCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_DescribesPersistentInterviewModeAndBacklog()
    {
        using var writer = new StringWriter();
        var exitCode = GuideGrillCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide grill", output, StringComparison.Ordinal);
        // AC: persistent interview mode using interview artifacts internally.
        Assert.Contains("persistent", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("interview", output, StringComparison.OrdinalIgnoreCase);
        // AC: open-question backlog generation + continuation-after-answer.
        Assert.Contains("Open-question backlog generation", output, StringComparison.Ordinal);
        Assert.Contains("Continuation after each answer", output, StringComparison.Ordinal);
        // AC: one question at a time.
        Assert.Contains("one question at a time", output, StringComparison.OrdinalIgnoreCase);
        // AC: empty-backlog phrase only after backlog empty + rediscovery.
        Assert.Contains("今のところ追加質問はありません", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasStableShapeAndAllRequiredStopConditions()
    {
        using var writer = new StringWriter();
        var exitCode = GuideGrillCommand.Execute(CreateContext(), ["--domain", "intent-cli", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("persistent-grill-interview", root.GetProperty("process").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("今のところ追加質問はありません", root.GetProperty("empty_backlog_response").GetString());

        // AC: stop conditions include all six required names.
        var stops = root.GetProperty("stop_conditions").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        foreach (var required in new[]
                 {
                     "no-more-questions", "packet-ready", "intent-update-ready",
                     "clarification-needed", "blocked-by-user-decision", "too-broad-split-needed",
                 })
        {
            Assert.Contains(required, stops);
        }

        // AC: backlog generation + continuation behavior present.
        Assert.True(root.GetProperty("backlog_generation").GetArrayLength() > 0);
        Assert.True(root.GetProperty("continuation_behavior").GetArrayLength() > 0);

        // AC: built on interview artifacts — references the real interview commands.
        var integration = string.Join("\n", root.GetProperty("interview_integration").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("interview next-question", integration, StringComparison.Ordinal);
        Assert.Contains("interview record-answer", integration, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_DoesNotSubstituteClarificationOrImprove()
    {
        using var writer = new StringWriter();
        var exitCode = GuideGrillCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var doNotSubstitute = string.Join("\n", doc.RootElement.GetProperty("do_not_substitute").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("clarification", doNotSubstitute, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("improve", doNotSubstitute, StringComparison.Ordinal);
        // AC: missing grill surface reports `grill guidance unavailable`.
        Assert.Contains("grill guidance unavailable", doNotSubstitute, StringComparison.Ordinal);

        // grill never auto-publishes.
        var notThis = string.Join("\n", doc.RootElement.GetProperty("not_this").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("auto-publish", notThis, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WithDomain_SubstitutesDomainInInspectionSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideGrillCommand.Execute(CreateContext(), ["--domain", "aic", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var sources = string.Join("\n", doc.RootElement.GetProperty("inspection_sources").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("intents/aic/", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideGrillCommand.Execute(CreateContext(), ["--format", "yaml"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsageWithStartPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideGrillCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("grill", output, StringComparison.Ordinal);
        Assert.Contains("persistent interview mode", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideHelp_ListsGrillSubcommand_ForDiscoverability()
    {
        using var writer = new StringWriter();
        var exitCode = GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var names = doc.RootElement.GetProperty("subcommands").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        Assert.Contains("grill", names);
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
