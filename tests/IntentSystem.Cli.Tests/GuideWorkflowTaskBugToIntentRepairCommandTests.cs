using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G339: tests for <c>intent-cli guide workflow task bug-to-intent-repair</c>.
/// The surface explains the report → triage → plan → intent-repair →
/// implementation-repair chain, classifies five gap types, recommends
/// packet creation when the bug is in intent-cli rules/guidance, and
/// preserves the original instruction / linked issue / PR refs across
/// the chain.
/// </summary>
public sealed class GuideWorkflowTaskBugToIntentRepairCommandTests
{
    [Fact]
    public void Execute_ListsFiveStagesInOrder_ExitZero()
    {
        // Acceptance criterion: "guide workflow task bug-to-intent-repair
        // explains report, triage, plan, intent-repair, and
        // implementation-repair paths."
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        var stages = new[] { "report", "triage", "plan", "intent-repair", "implementation-repair" };
        var lastIndex = -1;
        foreach (var stage in stages)
        {
            var idx = output.IndexOf($"### {stage}", StringComparison.Ordinal);
            Assert.True(idx > lastIndex, $"Stage `{stage}` did not appear in expected order in the output.");
            lastIndex = idx;
        }
    }

    [Fact]
    public void Execute_ClassifiesFiveGapTypes()
    {
        // Acceptance criterion: "The guide classifies implementation
        // mismatch, intent gap, packet gap, rule gap, and
        // metadata/workflow gap."
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("`implementation-mismatch`", output, StringComparison.Ordinal);
        Assert.Contains("`intent-gap`", output, StringComparison.Ordinal);
        Assert.Contains("`packet-gap`", output, StringComparison.Ordinal);
        Assert.Contains("`rule-gap`", output, StringComparison.Ordinal);
        Assert.Contains("`metadata-workflow-gap`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RecommendsPacketCreationForRuleAndIntentGaps()
    {
        // Acceptance criterion: "It can recommend packet creation
        // when the bug is in intent-cli rules/guidance rather than
        // child implementation." The intent-gap / packet-gap /
        // rule-gap classifications must route to the intent-repair
        // lane (which scaffolds a packet via `packet draft`), and
        // the intent-repair stage must mention `packet draft`.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var classifications = document.RootElement.GetProperty("classifications").EnumerateArray().ToList();

        foreach (var name in new[] { "intent-gap", "packet-gap", "rule-gap" })
        {
            var match = classifications.First(c => string.Equals(c.GetProperty("id").GetString(), name, StringComparison.Ordinal));
            Assert.Equal("intent-repair", match.GetProperty("repair_lane").GetString());
        }

        // The intent-repair stage must mention `packet draft` —
        // either in the `command` line (when the stage's headline
        // surface is `packet draft`) or in the `output` description
        // (when the headline surface is the bug-lifecycle wrapper
        // `bug intent-repair` and the packet scaffold is the
        // follow-up step). Either form satisfies the acceptance
        // criterion that the lane recommends packet creation.
        var intentRepair = document.RootElement.GetProperty("stages")
            .EnumerateArray()
            .First(s => string.Equals(s.GetProperty("stage").GetString(), "intent-repair", StringComparison.Ordinal));
        var combined = intentRepair.GetProperty("command").GetString() + " " + intentRepair.GetProperty("output").GetString();
        Assert.Contains("packet draft", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PreservesOriginalAndLinkedRefsAcrossTheChain()
    {
        // Acceptance criterion: "It preserves original instruction
        // refs and linked issue/PR refs." The invariants list must
        // explicitly require chain preservation, and the
        // intent-repair stage must require G311 closing reference
        // to the bug report on the repair PR.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());

        var invariants = document.RootElement.GetProperty("invariants")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        var chainInvariant = invariants.First(line => line.Contains("Original refs", StringComparison.Ordinal));
        Assert.Contains("bug report", chainInvariant, StringComparison.Ordinal);
        Assert.Contains("triage", chainInvariant, StringComparison.Ordinal);
        Assert.Contains("plan", chainInvariant, StringComparison.Ordinal);
        Assert.Contains("repair packet", chainInvariant, StringComparison.Ordinal);
        Assert.Contains("Closes #", chainInvariant, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_StopConditionsSurfaceBeforeGitHubMutation()
    {
        // Mirrors the G337 acceptance pattern: stop conditions must
        // catch missing artifacts BEFORE GitHub mutation.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var stops = document.RootElement.GetProperty("stop_conditions")
            .EnumerateArray()
            .Select(e => e.GetString() ?? string.Empty)
            .ToArray();
        Assert.NotEmpty(stops);
        // Missing original instruction reference must be caught.
        Assert.Contains(stops, s => s.Contains("original instruction reference", StringComparison.Ordinal));
        // `issue validate-body` must gate publishing.
        Assert.Contains(stops, s => s.Contains("issue validate-body", StringComparison.Ordinal));
        // Child cwd / metadata-workflow-gap stop must be present.
        Assert.Contains(stops, s => s.Contains("metadata-workflow-gap", StringComparison.Ordinal)
            && s.Contains("child cwd", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_AdvertisesChildIsolationAndIntentTargetFinalBoundary()
    {
        // The G300/G330/G333 child isolation rule and the G337
        // intent-target FINAL-boundary rule must carry through the
        // bug-to-intent-repair surface.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Child implementation isolation", output, StringComparison.Ordinal);
        Assert.Contains("intent-target", output, StringComparison.Ordinal);
        Assert.Contains("FORBIDDEN", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_PrefersIntentCliBackedMutationOverHandEditing()
    {
        // The G338 / G339 baseline: the guide must tell agents to
        // ask intent-cli which command performs a metadata
        // transition, run that command, and validate the result.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-cli-backed metadata mutation", output, StringComparison.Ordinal);
        Assert.Contains("guide commands list", output, StringComparison.Ordinal);
        Assert.Contains("automation summary", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_HasStableShape()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("usage", out _));
        Assert.True(root.TryGetProperty("stages", out _));
        Assert.True(root.TryGetProperty("classifications", out _));
        Assert.True(root.TryGetProperty("stop_conditions", out _));
        Assert.True(root.TryGetProperty("invariants", out _));

        foreach (var stage in root.GetProperty("stages").EnumerateArray())
        {
            Assert.True(stage.TryGetProperty("stage", out _));
            Assert.True(stage.TryGetProperty("purpose", out _));
            Assert.True(stage.TryGetProperty("command", out _));
            Assert.True(stage.TryGetProperty("output", out _));
            Assert.True(stage.TryGetProperty("boundary", out _));
            Assert.True(stage.TryGetProperty("fails_open", out _));
        }
        foreach (var classification in root.GetProperty("classifications").EnumerateArray())
        {
            Assert.True(classification.TryGetProperty("id", out _));
            Assert.True(classification.TryGetProperty("description", out _));
            Assert.True(classification.TryGetProperty("repair_lane", out _));
            Assert.True(classification.TryGetProperty("example_signal", out _));
        }
    }

    [Fact]
    public void Execute_StageCommands_NameRealCliSurfaces()
    {
        // PR #782 review finding: the previous guide advertised
        // `intent-cli guide automation report` (which does not
        // accept `report`) and `intent-cli intent draft-issue`
        // (which is not yet implemented). Regression: walk each
        // stage's `command` string, peel the first
        // `intent-cli <group> <subcommand>` pair, and assert the
        // matching command source file exists. Mirrors the G338
        // PR #780 "wrapper usage line" regression and the G336 /
        // G337 flag-against-source pattern.
        var repoRoot = FindRepoRoot();
        var commandsDir = Path.Combine(repoRoot, "src/IntentSystem.Cli/Commands");

        // Stable mapping from `intent-cli <group> <subcommand>` to
        // the source file owning the dispatcher entry. Verified
        // against `CommandRouter.CommandsByGroup` on PR #782 head.
        var subcommandToSourceFile = new (string Prefix, string FileName)[]
        {
            ("bug report", "BugReportCommand.cs"),
            ("bug triage", "BugTriageCommand.cs"),
            // The dispatcher key is `bug plan`; the underlying
            // class is BugExecutionCommand.cs (renamed dispatcher,
            // kept class name).
            ("bug plan", "BugExecutionCommand.cs"),
            ("bug intent-repair", "BugIntentRepairCommand.cs"),
            ("bug implementation-repair", "BugImplementationRepairCommand.cs"),
            ("packet draft", "PacketDraftCommand.cs"),
            ("issue draft", "IssueDraftCommand.cs"),
            ("issue validate-body", "IssueValidateBodyCommand.cs"),
            ("issue publish-flow", "IssuePublishFlowCommand.cs"),
            ("automation issue-publish", "AutomationIssuePublishCommand.cs"),
            ("automation doctor", "AutomationDoctorCommand.cs"),
            ("intent next-slice", "IntentNextSliceCommand.cs"),
            ("clarification next", "ClarificationCommand.cs"),
            ("review closeout-plan", "ReviewCloseoutPlanCommand.cs"),
            ("automation publish-recovery", "AutomationPublishRecoveryCommand.cs"),
            ("automation reconcile", "AutomationReconcileCommand.cs"),
            ("guide commands list", "GuideCommandsCommand.cs"),
            ("automation summary", "AutomationSummaryCommand.cs")
        };

        // Match `intent-cli <token1> <token2>` inside backticks
        // anywhere in the Command string.
        var commandSnippet = new System.Text.RegularExpressions.Regex(
            @"intent-cli\s+([a-z][a-z0-9-]*)\s+([a-z][a-z0-9-]*)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var stage in GuideWorkflowTaskBugToIntentRepairCommand.Stages)
        {
            var match = commandSnippet.Match(stage.Command);
            Assert.True(
                match.Success,
                $"Stage `{stage.Stage}` command does not start with `intent-cli <group> <subcommand>`: `{stage.Command}`");

            var prefix = $"{match.Groups[1].Value} {match.Groups[2].Value}";
            var row = subcommandToSourceFile.FirstOrDefault(r => string.Equals(r.Prefix, prefix, StringComparison.Ordinal));
            Assert.False(
                row.Prefix is null,
                $"Stage `{stage.Stage}` advertises `intent-cli {prefix}` but that prefix is not in the parity allow-list. Add a row (group/subcommand → source file) if the command is real, or change the stage to use an existing surface.");

            var sourcePath = Path.Combine(commandsDir, row.FileName);
            Assert.True(
                File.Exists(sourcePath),
                $"Stage `{stage.Stage}` advertises `intent-cli {prefix}` but the matching source file is missing: {sourcePath}");
        }
    }

    [Fact]
    public void StageCommands_AreReachableThroughCommandRouter()
    {
        // Defense-in-depth: even if the source file exists, the
        // command must be wired into CommandRouter.CommandsByGroup
        // for `intent-cli <group> <subcommand>` to actually
        // dispatch. Walk every stage command and invoke it
        // through CommandRouter.Execute with no args; the router
        // must NOT reject with "Unknown 'group' subcommand" /
        // "Command '... ...' is not yet implemented" / "Unknown
        // command group".
        var commandSnippet = new System.Text.RegularExpressions.Regex(
            @"intent-cli\s+([a-z][a-z0-9-]*)\s+([a-z][a-z0-9-]*)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var stage in GuideWorkflowTaskBugToIntentRepairCommand.Stages)
        {
            var match = commandSnippet.Match(stage.Command);
            Assert.True(match.Success);
            var group = match.Groups[1].Value;
            var subcommand = match.Groups[2].Value;

            using var writer = new StringWriter();
            // Run with just the group/subcommand to confirm the
            // dispatcher reaches the command's argument parser
            // (which will reject with a command-specific usage
            // message). The key signal we are guarding against is
            // the router-level "Unknown 'group' subcommand" /
            // "is not yet implemented" responses that mean a
            // missing dispatcher entry.
            CommandRouter.Execute(
                new[] { group, subcommand },
                CreateContext(),
                writer);
            var output = writer.ToString();

            Assert.DoesNotContain(
                $"Unknown '{group}' subcommand",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"Command '{group} {subcommand}' is not yet implemented",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Unknown command group",
                output,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOne()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            new[] { "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidFormat_ExitsOne()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            CreateContext(),
            new[] { "--format", "yaml" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_DispatchesBugToIntentRepair()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "bug-to-intent-repair", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Contains("bug-to-intent-repair", document.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_UnknownTask_NamesG339Task()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "bogus-task" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("bug-to-intent-repair", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideHelpCommand_BugToIntentRepairPhase_PointsToTaskBugToIntentRepair()
    {
        var pointer = GuideHelpCommand.WorkflowGuides
            .First(p => string.Equals(p.Phase, "bug-to-intent-repair", StringComparison.Ordinal));
        Assert.Contains("guide workflow task bug-to-intent-repair", pointer.Command, StringComparison.Ordinal);
        var seeAlso = string.Join(" ", pointer.SeeAlso ?? Array.Empty<string>());
        Assert.Contains("packet draft", seeAlso, StringComparison.Ordinal);
        Assert.Contains("issue-publish", seeAlso, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandRouter_TopLevelHelp_NamesBugToIntentRepairPhase()
    {
        var bugRepairLines = CommandRouter.WorkflowGuidePointersHelp
            .Where(line => line.StartsWith("bug-to-intent-repair —", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(bugRepairLines);
        Assert.Contains("guide workflow task bug-to-intent-repair", bugRepairLines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DoesNotRequireParentHostRoot()
    {
        // Pure read-only surface: must produce the guidance from a
        // fresh tmp cwd (no parent host root, no .intent-cli/).
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "bootstrap",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };

        var exitCode = GuideWorkflowTaskBugToIntentRepairCommand.Execute(
            context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(5, document.RootElement.GetProperty("stages").GetArrayLength());
        Assert.Equal(5, document.RootElement.GetProperty("classifications").GetArrayLength());
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
