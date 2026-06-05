using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G467: tests that `guide help` explains which surfaces are for design-side
/// planning, host review/next-slice, child implementation, recovery, and
/// loop-prompt creation.
/// </summary>
public sealed class GuideHelpRoleCatalogTests
{
    [Fact]
    public void Execute_Markdown_ExplainsSurfacesByOperatorRole()
    {
        using var writer = new StringWriter();
        var exitCode = GuideHelpCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        Assert.Contains("Surfaces by operator role", output, StringComparison.Ordinal);
        // AC: the four role buckets + loop-prompt creation are explained.
        Assert.Contains("Design-side planning", output, StringComparison.Ordinal);
        Assert.Contains("Host review / next-slice", output, StringComparison.Ordinal);
        Assert.Contains("Child implementation", output, StringComparison.Ordinal);
        Assert.Contains("Recovery / diagnostics", output, StringComparison.Ordinal);
        Assert.Contains("Loop-prompt creation", output, StringComparison.Ordinal);

        // AC: implementation-loop / review-next-slice-loop prompt generators are
        // discoverable from guide help.
        Assert.Contains("guide workflow task implementation-loop", output, StringComparison.Ordinal);
        Assert.Contains("review-next-slice-loop", output, StringComparison.Ordinal);
        // AC: the design-side commands are named in the role guidance.
        foreach (var cmd in new[] { "intent-cli grill", "intent-cli stack", "intent-cli inspect", "intent-cli next", "intent-cli improve" })
        {
            Assert.Contains(cmd, output, StringComparison.Ordinal);
        }
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
