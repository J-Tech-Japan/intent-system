using System.Diagnostics;

namespace IntentSystem.Cli.Tests;

public sealed class NotifySupervisionShrinkG734WaitHelperTests
{
    [Fact]
    public async Task WaitUntilAsync_UsesMonotonicDeadlineAndRetriesAThrowingPredicate()
    {
        var attempts = 0;
        await NotifySupervisionShrinkG734Tests.WaitUntilAsync(
            () =>
            {
                attempts++;
                if (attempts < 4)
                {
                    throw new IOException($"transient read failure {attempts}");
                }

                return true;
            },
            TimeSpan.FromSeconds(10));

        Assert.Equal(4, attempts);

        var alwaysThrowingAttempts = 0;
        var failure = await Record.ExceptionAsync(() => NotifySupervisionShrinkG734Tests.WaitUntilAsync(
            () =>
            {
                alwaysThrowingAttempts++;
                throw new InvalidOperationException("predicate never succeeds");
            },
            TimeSpan.FromMilliseconds(120)));

        Assert.NotNull(failure);
        var message = failure!.Message;
        Assert.Contains("elapsed_ms=", message, StringComparison.Ordinal);
        Assert.Contains("passes=", message, StringComparison.Ordinal);
        Assert.Contains("predicate never succeeds", message, StringComparison.Ordinal);
        Assert.True(alwaysThrowingAttempts >= 1);
    }

    [Fact]
    public void DescribeLiveFixture_SeparatesStarvationFromAStoppedSupervisor()
    {
        var root = Directory.CreateTempSubdirectory("notify-g734-wait-helper-").FullName;
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var runningState = NotifySupervisionShrinkG734Tests.DescribeProcessState(currentProcess);
            Assert.Equal(
                "supervisor_process=running; cycles_lines=0; cycles_last=<none>",
                NotifySupervisionShrinkG734Tests.DescribeLiveFixture(
                    runningState,
                    Path.Combine(root, "missing-cycles.jsonl")));

            var cyclesPath = Path.Combine(root, "cycles.jsonl");
            File.WriteAllLines(cyclesPath, ["line-1", "line-2", "line-3"]);
            Assert.Equal(
                "supervisor_process=running; cycles_lines=3; cycles_last=line-3",
                NotifySupervisionShrinkG734Tests.DescribeLiveFixture(runningState, cyclesPath));

            using var child = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "this-command-does-not-exist-g840",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            Assert.NotNull(child);
            child.WaitForExit();
            Assert.NotEqual(0, child.ExitCode);
            var exitedState = NotifySupervisionShrinkG734Tests.DescribeProcessState(child);
            Assert.Equal(
                $"supervisor_process=exited:{child.ExitCode}; cycles_lines=3; cycles_last=line-3",
                NotifySupervisionShrinkG734Tests.DescribeLiveFixture(exitedState, cyclesPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
