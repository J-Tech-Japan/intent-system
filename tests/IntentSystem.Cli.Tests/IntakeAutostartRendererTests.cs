using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeAutostartRendererTests
{
    [Fact]
    public void WriteSummary_GivenAutostartContext_WritesDeterministicSummary()
    {
        using var writer = new StringWriter();

        IntakeAutostartRenderer.WriteSummary(
            writer,
            "G31",
            "https://github.com/J-Tech-Japan/intent-system/issues/88",
            "/repo/.intent-cli/worktrees/G31",
            "issue-88-g31");

        var output = writer.ToString();
        Assert.Contains("Intake autostart completed for G31.", output, StringComparison.Ordinal);
        Assert.Contains("Linked issue: https://github.com/J-Tech-Japan/intent-system/issues/88", output, StringComparison.Ordinal);
        Assert.Contains("Worktree path: /repo/.intent-cli/worktrees/G31", output, StringComparison.Ordinal);
        Assert.Contains("Branch: issue-88-g31", output, StringComparison.Ordinal);
    }
}
