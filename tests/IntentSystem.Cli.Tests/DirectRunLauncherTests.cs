using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class DirectRunLauncherTests
{
    [Fact]
    public void Launch_GivenCodexProvider_StartsCodexExecWithPromptAgainstArtifact()
    {
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 4321,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G19",
            "implement",
            ".intent-cli/runs/G19.request.json",
            "Codex",
            "gpt-5.4",
            "stdio",
            DateTimeOffset.Parse("2026-04-09T10:15:00Z"),
            "/repo/.intent-cli/worktrees/G19",
            "/repo/.intent-cli/implement/G19.request.md");

        Assert.Equal("codex", runner.FileName);
        Assert.Equal("/repo/.intent-cli/worktrees/G19", runner.WorkingDirectory);
        Assert.Equal(["exec", "--model", "gpt-5.4", "Use the request artifact at '/repo/.intent-cli/implement/G19.request.md' as the bounded source of truth for this direct run."], runner.Arguments);
        Assert.Equal("pid:4321", result.ProviderSessionId);
        Assert.Contains("stdio transport launched via 'codex'", result.TransportSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_GivenClaudeProvider_StartsClaudePrintWithPromptAgainstArtifact()
    {
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 8765,
                ExitedEarly = false,
                ExitCode = 0
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var result = launcher.Launch(
            "G20",
            "fix",
            ".intent-cli/runs/G20.request.json",
            "Claude",
            "sonnet",
            "stdio",
            DateTimeOffset.Parse("2026-04-09T10:25:00Z"),
            "/repo/.intent-cli/worktrees/G20",
            "/repo/.intent-cli/fix/G20.request.md");

        Assert.Equal("claude", runner.FileName);
        Assert.Equal(
            ["--print", "--model", "sonnet", "--output-format", "json", "Use the request artifact at '/repo/.intent-cli/fix/G20.request.md' as the bounded source of truth for this direct run."],
            runner.Arguments);
        Assert.Equal("pid:8765", result.ProviderSessionId);
    }

    [Fact]
    public void Launch_GivenEarlyNonZeroExit_Throws()
    {
        var runner = new FakeDirectRunProcessRunner
        {
            Result = new DirectRunProcessLaunchResult
            {
                ProcessId = 999,
                ExitedEarly = true,
                ExitCode = 17
            }
        };
        var launcher = new DirectRunLauncher(runner);

        var exception = Assert.Throws<InvalidOperationException>(() => launcher.Launch(
            "G9",
            "review",
            ".intent-cli/runs/G9.request.json",
            "Codex",
            "gpt-5.4-mini",
            "grpc",
            DateTimeOffset.Parse("2026-04-09T10:35:00Z"),
            "/repo",
            "/repo/.intent-cli/reviews/G9.request.json"));

        Assert.Contains("exit code 17", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeDirectRunProcessRunner : IDirectRunProcessRunner
    {
        public string WorkingDirectory { get; private set; } = string.Empty;

        public string FileName { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public DirectRunProcessLaunchResult Result { get; set; } = new()
        {
            ProcessId = 1,
            ExitedEarly = false,
            ExitCode = 0
        };

        public DirectRunProcessLaunchResult Start(
            string workingDirectory,
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan earlyExitWindow)
        {
            WorkingDirectory = workingDirectory;
            FileName = fileName;
            Arguments = arguments.ToArray();
            return Result;
        }
    }
}
