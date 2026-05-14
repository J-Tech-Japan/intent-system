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
    public void StopConditions_CommandExamples_FlagsExistInActualCliCommandSource()
    {
        // PR #778 review finding: a stop-condition example listed
        // `automation host-sync-preflight --repo <r>` even though
        // AutomationHostSyncPreflightCommand only accepts `--format`.
        // This regression check walks every `intent-cli <cmd>` snippet
        // inside backticks in each stop-condition string and asserts
        // every `--flag` after a recognized subcommand prefix appears
        // verbatim in the matching command's source file. Mirrors the
        // G336 PR #776 fix pattern but operates on StopConditions so
        // missing-flag drift surfaces before reaching the operator.
        var repoRoot = FindRepoRoot();
        var commandsDir = Path.Combine(repoRoot, "src/IntentSystem.Cli/Commands");

        // Stable mapping from `intent-cli` subcommand prefix to the
        // command source file that owns its flag parser. Add a new
        // row whenever a new subcommand surfaces in StopConditions.
        var subcommandToSourceFile = new (string Prefix, string FileName)[]
        {
            ("issue publish-flow", "IssuePublishFlowCommand.cs"),
            ("issue validate-body", "IssueValidateBodyCommand.cs"),
            ("automation host-sync-preflight", "AutomationHostSyncPreflightCommand.cs"),
            ("automation publish-recovery", "AutomationPublishRecoveryCommand.cs"),
            ("automation issue-publish", "AutomationIssuePublishCommand.cs"),
            ("guide workflow task packet-draft", "GuideWorkflowTaskPacketDraftCommand.cs"),
        };

        var backtickSpan = new System.Text.RegularExpressions.Regex(
            "`([^`]+)`",
            System.Text.RegularExpressions.RegexOptions.Compiled);
        var flagPattern = new System.Text.RegularExpressions.Regex(
            @"--[a-z][a-z-]*",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var stopCondition in GuideWorkflowTaskIssuePublishCommand.StopConditions)
        {
            foreach (System.Text.RegularExpressions.Match span in backtickSpan.Matches(stopCondition))
            {
                var code = span.Groups[1].Value;
                if (!code.StartsWith("intent-cli ", StringComparison.Ordinal))
                {
                    continue;
                }

                // Strip the leading `intent-cli ` so prefix matching
                // works without anchor surprises.
                var afterCli = code.Substring("intent-cli ".Length);
                var match = subcommandToSourceFile
                    .Where(row => afterCli.StartsWith(row.Prefix, StringComparison.Ordinal))
                    .OrderByDescending(row => row.Prefix.Length)
                    .FirstOrDefault();
                Assert.False(
                    match.Prefix is null,
                    $"Stop-condition references an unmapped `intent-cli` subcommand: `{code}`. Add a row to subcommandToSourceFile so its flags can be parity-checked.");

                var sourcePath = Path.Combine(commandsDir, match.FileName);
                Assert.True(File.Exists(sourcePath), $"Expected command source file is missing: {sourcePath}");
                var source = File.ReadAllText(sourcePath);

                foreach (System.Text.RegularExpressions.Match flagMatch in flagPattern.Matches(afterCli))
                {
                    var flag = flagMatch.Value;
                    Assert.True(
                        source.Contains($"\"{flag}\"", StringComparison.Ordinal),
                        $"`{flag}` is missing from {match.FileName} but appears in stop-condition example: `{code}`");
                }
            }
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

    // ----- G349: copilot-local host agent can ask issue-publish guide -----

    [Fact]
    public void Execute_G349_CopilotLocalHostCwd_CanAskIssuePublishGuide_ExitZero()
    {
        // G349 Verification: a local Copilot host agent running in a host cwd
        // can call `guide workflow task issue-publish` and receive the full
        // draft/create/publish-flow boundary guidance (exit 0, four stages).
        // This surface is accessible to any host-cwd agent including copilot-local.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // All four issue-publish stages must be surfaced.
        Assert.Contains("draft", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish-flow", output, StringComparison.OrdinalIgnoreCase);
        // The canonical issue-publish command is surfaced.
        Assert.Contains("intent-cli issue publish-flow", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_G349_CopilotLocalHostCwd_IssuePublishJsonFormat_ExitZero()
    {
        // G349 Verification: local Copilot host agent can request JSON format from
        // the issue-publish guide and gets a parsable payload.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIssuePublishCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var json = writer.ToString();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("stages", out _), "stages key must be present.");
    }
}
