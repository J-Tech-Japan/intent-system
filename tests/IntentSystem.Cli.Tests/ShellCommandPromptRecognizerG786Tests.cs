using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ShellCommandPromptRecognizerG786Tests
{
    private const string FirstScratchPath = "/tmp/g781-evidence.GuzWkP";
    private const string SecondScratchPath = "/tmp/g781-default-evidence.iA5IUD";
    private const string CycleId = "g786-current-cycle";

    [Fact]
    public void MeasuredDialog_ExtractsOnlyTheCommandAndTreatsChoicesAsChrome()
    {
        var observed = MeasuredDialog();

        Assert.True(ShellCommandPromptRecognizer.TryExtract(observed, out var payload));
        Assert.NotNull(payload);
        Assert.Equal($"rm -rf {FirstScratchPath} {SecondScratchPath}", payload!.Command);
        Assert.DoesNotContain("Environment:", payload.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("don't ask again", payload.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("Press enter", payload.Command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1. Yes, proceed (y)")]
    [InlineData("› 1. Yes, proceed (y)")]
    [InlineData("2. Yes, and don't ask again for commands that")]
    [InlineData("3. No, and tell Codex what to do differently")]
    public void RawNumberedChoices_StartTheChromeBoundary(string choice)
    {
        var observed = Header
            + "\n$ printf '%s' safe\n"
            + choice
            + "\nPress enter to confirm or esc to cancel";

        Assert.True(ShellCommandPromptRecognizer.TryExtract(observed, out var payload));
        Assert.Equal("printf '%s' safe", payload!.Command);
    }

    [Fact]
    public void EnvironmentAndBareContinuation_AreNotChoicesOrPayloadChrome()
    {
        var observed = Header + "\nEnvironment: local\n"
            + "$ rm -rf " + FirstScratchPath + "\n"
            + SecondScratchPath + "\n"
            + "1. Yes, proceed (y)\n"
            + "Press enter to confirm or esc to cancel";

        Assert.True(ShellCommandPromptRecognizer.TryExtract(observed, out var payload));
        Assert.Equal($"rm -rf {FirstScratchPath}\n{SecondScratchPath}", payload!.Command);
    }

    [Theory]
    [InlineData("$ rm -rf /tmp/hidden-tail")]
    [InlineData("$rm -rf /tmp/hidden-tail")]
    [InlineData("$")]
    [InlineData("```sh")]
    public void HiddenCommandOrFenceAfterChoice_RejectsTheWholeDialog(string tail)
    {
        var observed = MeasuredDialog() + "\n" + tail;

        Assert.False(ShellCommandPromptRecognizer.TryExtract(observed, out var payload));
        Assert.Null(payload);
    }

    [Theory]
    [InlineData("rm -rf `unparseable`")]
    [InlineData("rm -rf $(pwd)")]
    public void SyntaxInsideTheCommand_RemainsShellAstUnparseable(string command)
    {
        Assert.True(ShellCommandPromptRecognizer.TryExtract(Dialog(command), out var payload));
        var authorization = ShellCommandPolicyRegistry.Evaluate(payload!, [], "/repo");

        Assert.Equal("escalate", authorization.Decision);
        Assert.Contains("shell-ast-unparseable", authorization.Rule, StringComparison.Ordinal);
    }

    [Fact]
    public void MeasuredDialog_UsesBothOwnedScratchLedgerPathsForBoundedAuthorization()
    {
        Assert.True(ShellCommandPromptRecognizer.TryExtract(MeasuredDialog(), out var payload));
        var covered = ShellCommandPolicyRegistry.Evaluate(
            payload!,
            [OwnedScratchPolicy([FirstScratchPath, SecondScratchPath])],
            "/repo",
            currentCycleId: CycleId);

        Assert.Equal("accept", covered.Decision);
        Assert.Contains("owned-scratch-delete", covered.MatchedScopes);
        Assert.Equal(["y", "enter"], covered.AnswerKeys);

        var missingOne = ShellCommandPolicyRegistry.Evaluate(
            payload!,
            [OwnedScratchPolicy([FirstScratchPath])],
            "/repo",
            currentCycleId: CycleId);

        Assert.Equal("escalate", missingOne.Decision);
        Assert.Contains("shell-segment-out-of-scope", missingOne.Rule, StringComparison.Ordinal);
        Assert.Contains(SecondScratchPath, missingOne.Summary, StringComparison.Ordinal);
    }

    private const string Header = "Would you like to run the following command?";

    private static string MeasuredDialog() => Dialog($"rm -rf {FirstScratchPath} {SecondScratchPath}");

    private static string Dialog(string command) => Header + "\n"
        + "Environment: local\n"
        + "$ " + command + "\n"
        + "› 1. Yes, proceed (y)\n"
        + "  2. Yes, and don't ask again for commands that\n"
        + "     start with `rm -rf …`\n"
        + "  3. No, and tell Codex what to do differently\n"
        + "Press enter to confirm or esc to cancel";

    private static NotifyScopedPromptPolicy OwnedScratchPolicy(IReadOnlyList<string> paths) => new()
    {
        PolicyId = "g786-owned-scratch",
        AgentKind = "codex",
        PromptClass = "shell-command",
        Scope = "owned-scratch-delete",
        Decision = "accept",
        Category = "destructive-scratch-cleanup",
        ArgvTokenPrefix = ["rm", "-rf"],
        Cwd = "/repo",
        PathConstraints = paths,
        ScratchLedgerPaths = paths,
        ScratchLedgerCycleId = CycleId,
        EffectTags = ["destructive"],
        Applicable = true,
    };
}
