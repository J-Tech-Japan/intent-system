using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideWorkflowSuggestCommandTests
{
    [Theory]
    [InlineData("intent-cli に新機能を追加したい", "feature-intake")]
    [InlineData("operator wants to add a feature", "feature-intake")]
    [InlineData("plan the next slice", "next-slice-planning")]
    [InlineData("次のスライスを決めたい", "next-slice-planning")]
    [InlineData("review PR 600 and approve if it passes", "review")]
    [InlineData("PR をレビューして merge する", "review")]
    [InlineData("implement issue 605", "child-implementation")]
    [InlineData("issue 605 を実装してほしい", "child-implementation")]
    [InlineData("source-of-truth が曖昧な部分がある", "clarification")]
    [InlineData("we have a clarification blocker", "clarification")]
    [InlineData("I want to start agmsg orchestrator mode", "orchestrator-setup")]
    [InlineData("set up an orchestrator to run orchestration", "orchestrator-setup")]
    [InlineData("オーケストレーターを立ち上げたい", "orchestrator-setup")]
    // G500: the natural-language setup requests named in the packet must route
    // to orchestrator setup intake, not generic explanation or feature intake.
    [InlineData("orchestrator を使いたい", "orchestrator-setup")]
    [InlineData("新しい intent-cli オーケストレーションを使ってみたい", "orchestrator-setup")]
    [InlineData("agmsg orchestrator を試したい", "orchestrator-setup")]
    [InlineData("オーケストレーションスレッドを使いたい", "orchestrator-setup")]
    // G540 repair round 1: a GENERIC multi-thread implementation/review goal
    // (no literal "orchestrator"/"agmsg" mention) must still route to
    // orchestrator-setup rather than falling through to an ordinary review
    // or single-issue implementation classification. EN/JA parity required.
    [InlineData("I want to set up multiple threads for implementation and review", "orchestrator-setup")]
    [InlineData("set up a multi-thread implementation and review workflow", "orchestrator-setup")]
    [InlineData("run implementation and review in parallel across four threads", "orchestrator-setup")]
    [InlineData("実装とレビューのために複数スレッドをセットアップしたい", "orchestrator-setup")]
    [InlineData("マルチスレッドで実装とレビューを行いたい", "orchestrator-setup")]
    [InlineData("xyzzy mumble", "unknown")]
    public void Execute_ClassifiesGoalIntoExpectedWorkflow(string goal, string expectedWorkflow)
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", goal, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(expectedWorkflow, document.RootElement.GetProperty("workflow").GetString());
    }

    [Fact]
    public void Execute_OrchestratorSetup_RoutesToOrchestratorThreadGuide_G494()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "I want to run agmsg orchestration", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("orchestrator-setup", document.RootElement.GetProperty("workflow").GetString());

        // Routes to the orchestrator-thread guide (not generic feature intake).
        var commands = document.RootElement.GetProperty("recommended_commands").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide orchestrator-thread", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, c => c!.StartsWith("intent-cli guide collaborate", StringComparison.Ordinal));

        // Summary names the loopless-receiver / signal-only invariants.
        var summary = document.RootElement.GetProperty("summary").GetString()!;
        Assert.Contains("loopless", summary, StringComparison.Ordinal);
        Assert.Contains("signal layer only", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("I want to set up multiple threads for implementation and review")]
    [InlineData("実装とレビューのために複数スレッドをセットアップしたい")]
    public void Execute_GenericMultiThreadGoal_RoutesToOrchestratorSetup_NotOrdinaryReview_G540(string goal)
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", goal, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());

        // The PRIMARY four-thread orchestrator setup is recommended first —
        // this must never fall through to an ordinary single-PR review
        // classification just because the goal also mentions "review".
        Assert.Equal("orchestrator-setup", document.RootElement.GetProperty("workflow").GetString());

        var commands = document.RootElement.GetProperty("recommended_commands").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide orchestrator-thread", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_FromFile_ReadsGoalFromDisk()
    {
        using var workspace = new ScratchWorkspace();
        var path = workspace.WriteFile("goal.txt", "We need to plan the next slice for intent-cli");

        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--from-file", path, "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("next-slice-planning", document.RootElement.GetProperty("workflow").GetString());
    }

    [Fact]
    public void Execute_RecommendedCommandsReferenceOtherInstalledSurfaces()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "feature を一緒に追加したい", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var commands = document.RootElement.GetProperty("recommended_commands").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(commands, c => c!.StartsWith("intent-cli guide collaborate", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli intent status", StringComparison.Ordinal));
        Assert.Contains(commands, c => c!.StartsWith("intent-cli intent draft-from-interview", StringComparison.Ordinal));

        var ruleTopics = document.RootElement.GetProperty("rule_topics").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("intake-interview", ruleTopics);
    }

    [Fact]
    public void Execute_MarkdownFormat_EmitsHumanReadableOutput()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "review the latest PR", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide workflow suggest — review", output, StringComparison.Ordinal);
        Assert.Contains("## Recommended commands", output, StringComparison.Ordinal);
        Assert.Contains("## Rule topics", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide review", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_MissingGoalAndFile_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--goal <text> or --from-file <path> is required", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_BothGoalAndFile_ReturnsUsageError()
    {
        using var workspace = new ScratchWorkspace();
        var path = workspace.WriteFile("goal.txt", "x");

        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "review", "--from-file", path],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("mutually exclusive", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_FromFileMissing_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--from-file", "/tmp/this/does/not/exist.txt"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-file path not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--goal", "review", "--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowSuggestCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide workflow suggest", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideWorkflowSuggestThroughGuideWorkflowCommand_Works()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(),
            ["suggest", "--goal", "review", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("review", document.RootElement.GetProperty("workflow").GetString());
    }

    [Fact]
    public void Dispatch_GuideWorkflowUnknownSubcommand_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(),
            ["analyze"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown 'guide workflow' subcommand 'analyze'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatch_GuideWorkflowMissingSubcommand_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("guide workflow requires a subcommand", writer.ToString(), StringComparison.Ordinal);
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

    private sealed class ScratchWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("guide-workflow-suggest-tests-")
            .FullName;

        public string WriteFile(string name, string content)
        {
            var path = Path.Combine(rootPath, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
