using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G336: tests for <c>intent-cli guide workflow task intent-interview</c>.
/// The surface must explain the canonical question structure
/// (background / question / options / pros-cons / recommendation),
/// distinguish interview (new concept) from clarification (existing
/// blocker), surface the durable-artifact paths, list the canonical
/// commands per mode, and enumerate stop conditions plus the
/// no-hand-edit / no-host-state / no-AI-launch invariants.
/// </summary>
public sealed class GuideWorkflowTaskIntentInterviewCommandTests
{
    [Fact]
    public void Execute_NoFilter_ListsBothModesAndAllStructureSections_ExitZero()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIntentInterviewCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        // Both modes present.
        Assert.Contains("Mode: interview", output, StringComparison.Ordinal);
        Assert.Contains("Mode: clarification", output, StringComparison.Ordinal);
        // All five question structure sections present (acceptance criterion 1).
        foreach (var section in new[] { "background", "question", "options", "pros-cons", "recommendation" })
        {
            Assert.Contains(section, output, StringComparison.Ordinal);
        }
        // Durable artifact paths surfaced (acceptance criterion 4).
        Assert.Contains("intents/<domain>/interview", output, StringComparison.Ordinal);
        Assert.Contains("intents/<domain>/clarifications", output, StringComparison.Ordinal);
        // Canonical commands (acceptance criterion 3).
        Assert.Contains("intent-cli interview next-question", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli interview record-answer", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli interview compile", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli clarification next", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli clarification answer", output, StringComparison.Ordinal);
        // Stop conditions section appears per mode.
        Assert.Contains("Stop conditions", output, StringComparison.Ordinal);
        // Interview vs clarification distinction (acceptance criterion 2).
        Assert.Contains("Interview is for shaping a NEW concept; clarification is for repairing an EXISTING intent.", output, StringComparison.Ordinal);
        // No AI provider launch invariant.
        Assert.Contains("Never launch AI providers", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_ReturnsStableShape()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIntentInterviewCommand.Execute(
            CreateContext(),
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("usage", out _));
        Assert.True(root.TryGetProperty("question_structure", out var structureProp));
        Assert.Equal(5, structureProp.GetArrayLength());
        // Section IDs are part of the stable shape consumers can pin.
        var sectionIds = structureProp.EnumerateArray().Select(s => s.GetProperty("section").GetString()).ToArray();
        Assert.Equal(new[] { "background", "question", "options", "pros-cons", "recommendation" }, sectionIds);
        Assert.True(root.TryGetProperty("modes", out var modesProp));
        Assert.Equal(2, modesProp.GetArrayLength());
        var modeIds = modesProp.EnumerateArray().Select(m => m.GetProperty("mode").GetString()).ToArray();
        Assert.Equal(new[] { "interview", "clarification" }, modeIds);
        // Each mode must carry the required keys.
        foreach (var mode in modesProp.EnumerateArray())
        {
            Assert.True(mode.TryGetProperty("purpose", out _));
            Assert.True(mode.TryGetProperty("durable_artifact_path", out _));
            Assert.True(mode.TryGetProperty("commands", out _));
            Assert.True(mode.TryGetProperty("stop_conditions", out _));
            Assert.True(mode.TryGetProperty("follow_up", out _));
        }
        Assert.True(root.TryGetProperty("invariants", out var invariants));
        Assert.True(invariants.GetArrayLength() >= 6);
    }

    [Theory]
    [InlineData("interview")]
    [InlineData("clarification")]
    public void Execute_ModeFilter_ReturnsOnlyThatMode(string requestedMode)
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIntentInterviewCommand.Execute(
            CreateContext(),
            new[] { "--mode", requestedMode, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var modes = document.RootElement.GetProperty("modes");
        Assert.Equal(1, modes.GetArrayLength());
        Assert.Equal(requestedMode, modes[0].GetProperty("mode").GetString());
        Assert.Equal(requestedMode, document.RootElement.GetProperty("focus_mode").GetString());
    }

    [Fact]
    public void Execute_UnknownMode_ExitsOneWithError()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIntentInterviewCommand.Execute(
            CreateContext(),
            new[] { "--mode", "bogus" },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("did not resolve", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOneWithError()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskIntentInterviewCommand.Execute(
            CreateContext(),
            new[] { "--bogus" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Invariants_IncludeMetadataMutationGuidance()
    {
        // G336 acceptance: routine automation must prefer intent-cli-
        // backed metadata mutation over hand-editing.
        var combined = string.Join(" ", GuideWorkflowTaskIntentInterviewCommand.Invariants);
        Assert.Contains("Prefer intent-cli-backed metadata mutation", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void Invariants_IncludeChildIsolationRule()
    {
        // G336 acceptance: child implementation loops must not inspect
        // or mutate parent host state.
        var combined = string.Join(" ", GuideWorkflowTaskIntentInterviewCommand.Invariants);
        Assert.Contains("Child implementation loops MUST NOT inspect or mutate parent host queue-state", combined, StringComparison.Ordinal);
    }

    // ---- dispatcher tests --------------------------------------------------

    [Fact]
    public void GuideWorkflowTaskCommand_NoTask_NamesIntentInterview()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            Array.Empty<string>(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Supported: init-host, intent-interview", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowTaskCommand_IntentInterviewDispatch_ReturnsIntentInterviewGuidance()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowTaskCommand.Execute(
            CreateContext(),
            new[] { "intent-interview", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        // The intent-interview payload is identifiable by its usage line.
        Assert.Contains("guide workflow task intent-interview", document.RootElement.GetProperty("usage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowCommand_DispatchesIntentInterviewThroughTask()
    {
        // `guide workflow task intent-interview ...` must reach the
        // task dispatcher through the existing GuideWorkflowCommand
        // entry, mirroring the G335 init-host path.
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(),
            new[] { "task", "intent-interview", "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide workflow task intent-interview", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideWorkflowCommand_HelpMentionsIntentInterview()
    {
        using var writer = new StringWriter();

        var exitCode = GuideWorkflowCommand.Execute(
            CreateContext(),
            new[] { "--help" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("intent-interview", output, StringComparison.Ordinal);
        Assert.Contains("G336", output, StringComparison.Ordinal);
    }

    // ---- guide help integration (G334/G336) --------------------------------

    [Fact]
    public void GuideHelpCommand_WorkflowGuides_InterviewPhasePointsToIntentInterview()
    {
        // G336: the `interview` workflow guide pointer must now route
        // an external agent to `task intent-interview` first.
        var interviewPointer = GuideHelpCommand.WorkflowGuides
            .FirstOrDefault(p => string.Equals(p.Phase, "interview", StringComparison.Ordinal));
        Assert.NotNull(interviewPointer);
        Assert.Contains("guide workflow task intent-interview", interviewPointer!.Command, StringComparison.Ordinal);
        var seeAlso = string.Join(" ", interviewPointer.SeeAlso ?? Array.Empty<string>());
        Assert.Contains("interview next-question", seeAlso, StringComparison.Ordinal);
        Assert.Contains("interview record-answer", seeAlso, StringComparison.Ordinal);
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
