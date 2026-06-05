using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G464: tests for the stack packet-backlog-creation guide surface.
/// </summary>
public sealed class GuideStackCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_DescribesBacklogAndFirstIssueBoundary()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide stack", output, StringComparison.Ordinal);
        // AC: ordered packet backlog + publishes at most one first issue by default.
        Assert.Contains("Ordered packet backlog creation", output, StringComparison.Ordinal);
        Assert.Contains("at most", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first", output, StringComparison.OrdinalIgnoreCase);
        // AC: output shape section present.
        Assert.Contains("Output shape", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasStableShapeAndOutputFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("task-stack", root.GetProperty("process").GetString());
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal("J-Tech-Japan/intent-system", root.GetProperty("target_repo").GetString());

        // AC: output shape lists created packets, recommended first issue, published issue, deferred items.
        var fields = root.GetProperty("output_shape").EnumerateArray()
            .Select(f => f.GetProperty("field").GetString()).ToArray();
        Assert.Equal(
            new[] { "created_packets", "recommended_first_issue", "published_issue", "deferred_items" },
            fields);
    }

    [Fact]
    public void Execute_Json_DistinguishesStackFromImproveGrillClarificationAndQueue()
    {
        // AC: guide output distinguishes stack from improve, grill, clarification, and queue transitions.
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var distinctions = string.Join("\n", doc.RootElement.GetProperty("distinctions").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("improve", distinctions, StringComparison.Ordinal);
        Assert.Contains("grill", distinctions, StringComparison.Ordinal);
        Assert.Contains("clarification", distinctions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("queue", distinctions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_BoundaryRespectsOpenQuestionsWipHostOnlyAndDurableCommit()
    {
        // AC: stack guidance respects open questions, WIP, host-only packet
        // boundaries, and durable commit/push before issue-publish.
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var boundary = string.Join("\n", doc.RootElement.GetProperty("boundary").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("open question", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WIP", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host-only", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("push", boundary, StringComparison.OrdinalIgnoreCase);

        // do-not-substitute names the unavailable-surface signal.
        var doNotSubstitute = string.Join("\n", doc.RootElement.GetProperty("do_not_substitute").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("stack guidance unavailable", doNotSubstitute, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomain_SubstitutesDomainInInspectionSources()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(CreateContext(), ["--domain", "aic", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var sources = string.Join("\n", doc.RootElement.GetProperty("inspection_sources").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("intents/aic/", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(CreateContext(), ["--format", "yaml"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideStackCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("stack", output, StringComparison.Ordinal);
        Assert.Contains("packet backlog", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuideHelp_ListsStackSubcommand_ForDiscoverability()
    {
        using var writer = new StringWriter();
        var exitCode = GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var names = doc.RootElement.GetProperty("subcommands").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        Assert.Contains("stack", names);
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
