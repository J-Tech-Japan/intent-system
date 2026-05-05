using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G258 — gate advanced runtime (root <c>run</c>, supervisor subprocess) away
/// from default chat-first guidance unless the operator explicitly opts in.
/// </summary>
public sealed class AdvancedRuntimeGuidanceGateTests
{
    [Theory]
    [InlineData("intent-cli に新機能を追加したい")]
    [InlineData("plan the next slice")]
    [InlineData("review PR 600")]
    [InlineData("implement issue 605")]
    [InlineData("we have a clarification blocker")]
    [InlineData("xyzzy unknown")]
    public void GuideWorkflowSuggest_DefaultRecommendedCommands_DoNotIncludeIntentCliRun(string goal)
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", goal, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        Assert.True(root.GetProperty("default_excludes_advanced_runtime").GetBoolean());
        Assert.False(root.GetProperty("advanced_runtime_included").GetBoolean());

        var commands = root.GetProperty("recommended_commands")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        foreach (var command in commands)
        {
            Assert.DoesNotContain("intent-cli run ", command!, StringComparison.Ordinal);
            Assert.DoesNotContain("supervisor subprocess", command!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("provider subprocess", command!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GuideWorkflowSuggest_WithFlag_IncludesAdvancedRuntimeForChildImplementation()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "implement issue 605", "--include-advanced-runtime", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        Assert.False(root.GetProperty("default_excludes_advanced_runtime").GetBoolean());
        Assert.True(root.GetProperty("advanced_runtime_included").GetBoolean());

        var commands = root.GetProperty("recommended_commands")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(commands, c => c!.Contains("intent-cli run start", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.Contains("intent-cli run supervise", StringComparison.Ordinal));

        var advanced = root.GetProperty("advanced_runtime_suggestions")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(advanced, item => item!.Contains("intent-cli run start", StringComparison.Ordinal));
    }

    [Fact]
    public void GuideWorkflowSuggest_DefaultChildImplementation_StillExposesSuggestionsForOptIn()
    {
        // Default: do not include in recommended_commands, but advanced_runtime_suggestions
        // is still populated so AI agents can see what's behind the gate.
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "implement issue 605", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        var commands = root.GetProperty("recommended_commands")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        foreach (var command in commands)
        {
            Assert.DoesNotContain("intent-cli run", command!, StringComparison.Ordinal);
        }

        var advanced = root.GetProperty("advanced_runtime_suggestions")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(advanced, item => item!.Contains("intent-cli run", StringComparison.Ordinal));

        var warning = root.GetProperty("advanced_runtime_warning").GetString();
        Assert.Contains("--include-advanced-runtime", warning!, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowSuggest_FeatureIntake_HasNoAdvancedRuntimeSuggestions()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "feature を一緒に追加したい", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        Assert.Equal(0, root.GetProperty("advanced_runtime_suggestions").GetArrayLength());
        Assert.Contains("no advanced-runtime suggestions", root.GetProperty("advanced_runtime_warning").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowSuggest_Markdown_NamesGateAndDefaultExclusion()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "implement issue 605", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("default excludes advanced runtime: yes", output, StringComparison.Ordinal);
        Assert.Contains("advanced runtime included: no", output, StringComparison.Ordinal);
        Assert.Contains("## Advanced runtime", output, StringComparison.Ordinal);
        Assert.Contains("--include-advanced-runtime", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideCommandsList_RunGroup_StaysClassifiedAsAdvanced()
    {
        var run = GuideCommandsListCommand.Groups.Single(group => group.Name == "run");
        Assert.Equal("advanced", run.Classification);
        Assert.Equal("advanced-runtime", run.RecommendedCaller);
    }

    [Fact]
    public void GuideCommandsList_DefaultMarkdown_FlagsRunAsAdvanced()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCommandsListCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // Confirm the row for the `run` group surfaces the advanced bucket.
        var runLine = output.Split('\n').First(line => line.StartsWith("| run ", StringComparison.Ordinal));
        Assert.Contains("| advanced |", runLine, StringComparison.Ordinal);
        Assert.Contains("| advanced-runtime |", runLine, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideCollaborate_DefaultOutput_DoesNotRecommendIntentCliRun()
    {
        using var writer = new StringWriter();
        var exitCode = GuideCollaborateCommand.Execute(
            CreateContext(),
            ["--kind", "feature-intake", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.DoesNotContain("intent-cli run ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideRules_DefaultTopics_DoNotRecommendIntentCliRun()
    {
        foreach (var topic in new[] { "label-ownership", "child-issue-contract", "clarification", "review-closeout", "intake-interview" })
        {
            using var writer = new StringWriter();
            var exitCode = GuideRulesCommand.Execute(
                CreateContext(),
                ["--topic", topic, "--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.DoesNotContain("intent-cli run ", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GuideRulesList_DoesNotRecommendIntentCliRun()
    {
        using var writer = new StringWriter();
        var exitCode = GuideRulesListCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.DoesNotContain("intent-cli run ", output, StringComparison.Ordinal);
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
