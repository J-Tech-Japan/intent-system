using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G637: the workspace convention is a render-only plan. These tests keep the
/// canonical no-op, the non-conforming temporary-tab order, and the EN/JA
/// onboarding/preview markers executable.
/// </summary>
public sealed class WorkspaceLayoutGuideG637Tests
{
    [Fact]
    public void CanonicalWorkspace_WithCanonicalLabels_IsAReadOnlyNoOp()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkspaceLayoutCommand.Execute(
            CreateContext(),
            [
                "--workspace-id", "w1", "--tab-id", "w1:t1", "--shape", "canonical",
                "--orchestration-pane", "w1:p1", "--implementation-pane", "w1:p2", "--review-pane", "w1:p3",
                "--actual-left-ratio", "0.4", "--actual-top-right-ratio", "0.5", "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("preview").GetBoolean());
        Assert.False(root.GetProperty("structure_differs").GetBoolean());
        Assert.Empty(root.GetProperty("commands").EnumerateArray());
        Assert.Equal(0.4m, root.GetProperty("convention").GetProperty("left_width").GetDecimal());
        Assert.Equal(0.5m, root.GetProperty("convention").GetProperty("right_split").GetDecimal());
    }

    [Fact]
    public void NonConformingWorkspace_EmitsTemporaryRoundTripBeforeRenameAndResize()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkspaceLayoutCommand.Execute(
            CreateContext(),
            [
                "--workspace-id", "w1", "--tab-id", "w1:t1", "--temporary-tab-id", "w1:t-scratch",
                "--shape", "three-column", "--orchestration-pane", "w1:p1", "--implementation-pane", "w1:p2",
                "--review-pane", "w1:p3", "--orchestration-label", "orchestrator", "--implementation-label", "implement",
                "--review-label", "reviewer", "--actual-left-ratio", "0.6", "--actual-top-right-ratio", "0.4",
                "--format", "json"
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("structure_differs").GetBoolean());
        var commands = root.GetProperty("commands").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal("herdr tab create --workspace w1 --label g637-layout-scratch --no-focus", commands[0]);
        Assert.Equal("herdr pane move w1:p3 --tab w1:t-scratch --no-focus", commands[1]);
        Assert.Contains("--target-pane w1:p2", commands[2], StringComparison.Ordinal);
        Assert.Contains("herdr pane rename w1:p1 orchestration", commands);
        Assert.Contains("herdr pane rename w1:p2 implementation", commands);
        Assert.Contains("herdr pane rename w1:p3 review", commands);
        Assert.Contains("--direction left --amount 0.2", commands.Single(command => command.Contains("w1:p1", StringComparison.Ordinal) && command.Contains("resize", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains("--direction down --amount 0.1", commands.Single(command => command.Contains("w1:p2", StringComparison.Ordinal) && command.Contains("resize", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.DoesNotContain(commands, command => command.Contains("Process.Start", StringComparison.Ordinal));
    }

    [Fact]
    public void Markdown_StatesMeasuredScopeSafetyAndPreviewBoundaries()
    {
        using var writer = new StringWriter();
        var exitCode = GuideWorkspaceLayoutCommand.Execute(
            CreateContext(),
            ["--shape", "mirrored", "--format", "markdown"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("G637 — preview-through-1.x", output, StringComparison.Ordinal);
        Assert.Contains("40%", output, StringComparison.Ordinal);
        Assert.Contains("60%", output, StringComparison.Ordinal);
        Assert.Contains("herdr 0.8.0 on macOS", output, StringComparison.Ordinal);
        Assert.Contains("changed: false", output, StringComparison.Ordinal);
        Assert.Contains("scratch tab first", output, StringComparison.Ordinal);
        Assert.Contains("never executes herdr", output, StringComparison.Ordinal);
        Assert.Contains("single-pane", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Documentation_AndLedger_ExposeTheSamePreviewSurfaceInEnglishAndJapanese()
    {
        foreach (var language in new[] { "en", "ja" })
        {
            var root = RepoVersionPolicySource.RepoRoot();
            var orchestration = File.ReadAllText(Path.Combine(root, "docs", language, "12-agent-message-orchestration.md"));
            var onboarding = File.ReadAllText(Path.Combine(root, "docs", language, "02a-getting-started-orchestration.md"));
            var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            var promise = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-promise.md"));

            Assert.Contains("G637", orchestration, StringComparison.Ordinal);
            Assert.Contains("workspace-layout", orchestration, StringComparison.Ordinal);
            Assert.Contains("40%", orchestration, StringComparison.Ordinal);
            Assert.Contains("60%", orchestration, StringComparison.Ordinal);
            Assert.Contains("guide workspace-layout", onboarding, StringComparison.Ordinal);
            Assert.Contains("workspace-layout guide", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
            Assert.Contains("G637", promise, StringComparison.Ordinal);
        }
    }

    private static CliContext CreateContext() => new()
    {
        RepoRoot = Path.GetTempPath(),
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };
}
