using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G337: tests for <c>intent-cli guide workflow task issue-publish</c>.
/// The surface must explain the draft / create / publish-flow /
/// automation-issue-publish boundary, name the intent-target FINAL
/// publish boundary, and surface missing contract sections before
/// any GitHub mutation.
/// </summary>
public sealed class GuideWorkflowTaskIssuePublishCommandTests
{
    [Fact]
    public void Execute_ListsFourStagesInOrder_ExitZero()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var stages = new[] { "draft", "create", "publish-flow", "automation issue-publish" };
        var lastIndex = -1;
        foreach (var stage in stages)
        {
            var idx = output.IndexOf($"## {stage}", StringComparison.Ordinal);
            // `## automation issue-publish` shows up with that literal
            // heading; the others as `## draft`, `## create`, `## publish-flow`.
            Assert.True(idx > lastIndex, $"Stage `{stage}` did not appear in expected order in the output.");
            lastIndex = idx;
        }
    }

    [Fact]
    public void Execute_AdvertisesIntentTargetFinalBoundary()
    {
        // Acceptance criterion: "The guide warns that intent-target
        // is final publish boundary, not issue creation default."
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-target", output, StringComparison.Ordinal);
        Assert.Contains("FINAL publish boundary", output, StringComparison.Ordinal);
        // The forbidden raw-gh path must be called out.
        Assert.Contains("FORBIDDEN", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_SurfacesMissingContractSectionsBeforeMutation()
    {
        // Acceptance criterion: "It surfaces missing contract
        // sections before GitHub mutation."
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("BEFORE", output, StringComparison.Ordinal);
        Assert.Contains("issue validate-body", output, StringComparison.Ordinal);
        Assert.Contains("issue publish-flow", output, StringComparison.Ordinal);
        Assert.Contains("--write", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_HasStableShape()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("usage", out _));
        Assert.True(root.TryGetProperty("stages", out var stages));
        Assert.Equal(4, stages.GetArrayLength());
        var stageIds = stages.EnumerateArray().Select(s => s.GetProperty("stage").GetString()).ToArray();
        Assert.Equal(new[] { "draft", "create", "publish-flow", "automation issue-publish" }, stageIds);
        foreach (var stage in stages.EnumerateArray())
        {
            Assert.True(stage.TryGetProperty("command", out _));
            Assert.True(stage.TryGetProperty("purpose", out _));
            Assert.True(stage.TryGetProperty("boundary", out _));
            Assert.True(stage.TryGetProperty("fails_open", out _));
        }
        Assert.True(root.TryGetProperty("stop_conditions", out _));
        Assert.True(root.TryGetProperty("invariants", out _));
    }

    [Fact]
    public void Stages_PublishFlowCommand_FlagsExistInActualCliCommandSource()
    {
        // The publish-flow stage example must use the real
        // IssuePublishFlowCommand flag set. Regression for the G336
        // PR #776 finding: keep guide examples in sync with real
        // command surfaces.
        var publishFlowStage = GuideWorkflowTaskIssuePublishCommand.Stages
            .First(s => string.Equals(s.Stage, "publish-flow", StringComparison.Ordinal));
        var flagPattern = new System.Text.RegularExpressions.Regex(
            @"--[a-z][a-z-]*",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var repoRoot = FindRepoRoot();
        var publishFlowSource = File.ReadAllText(Path.Combine(repoRoot, "src/IntentSystem.Cli/Commands/IssuePublishFlowCommand.cs"));

        foreach (System.Text.RegularExpressions.Match m in flagPattern.Matches(publishFlowStage.Command))
        {
            var flag = m.Value;
            Assert.True(
                publishFlowSource.Contains($"\"{flag}\"", StringComparison.Ordinal),
                $"`{flag}` is missing from IssuePublishFlowCommand.cs but appears in guide example: `{publishFlowStage.Command}`");
        }
    }

    [Fact]
    public void Stages_AutomationIssuePublishCommand_FlagsExistInActualCliCommandSource()
    {
        var stage = GuideWorkflowTaskIssuePublishCommand.Stages
            .First(s => string.Equals(s.Stage, "automation issue-publish", StringComparison.Ordinal));
        var flagPattern = new System.Text.RegularExpressions.Regex(
            @"--[a-z][a-z-]*",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src/IntentSystem.Cli/Commands/AutomationIssuePublishCommand.cs"));

        foreach (System.Text.RegularExpressions.Match m in flagPattern.Matches(stage.Command))
        {
            var flag = m.Value;
            Assert.True(
                source.Contains($"\"{flag}\"", StringComparison.Ordinal),
                $"`{flag}` is missing from AutomationIssuePublishCommand.cs but appears in guide example: `{stage.Command}`");
        }
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOne()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            new[] { "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_DispatchesIssuePublish()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "issue-publish", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("issue-publish", document.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideHelpCommand_IssuePhase_PointsToTaskIssuePublish()
    {
        var issuePointer = GuideHelpCommand.WorkflowGuides
            .First(p => string.Equals(p.Phase, "issue", StringComparison.Ordinal));
        Assert.Contains("guide workflow task issue-publish", issuePointer.Command, StringComparison.Ordinal);
        var seeAlso = string.Join(" ", issuePointer.SeeAlso ?? Array.Empty<string>());
        Assert.Contains("publish-flow", seeAlso, StringComparison.Ordinal);
        Assert.Contains("automation issue-publish", seeAlso, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_UnknownTask_NamesBothG337Tasks()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "bogus-task" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("packet-draft", output, StringComparison.Ordinal);
        Assert.Contains("issue-publish", output, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
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
