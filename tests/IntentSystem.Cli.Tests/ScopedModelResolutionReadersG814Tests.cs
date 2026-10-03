using System.Diagnostics;
using System.Globalization;
using System.Text;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class ScopedModelResolutionReadersG814Tests
{
    [Fact]
    public async Task BoundedHerdrRunner_TimesOutAndTerminatesItsObserverProcess()
    {
        if (!File.Exists("/bin/sh"))
            return;

        var runner = new ScopedModelResolutionReaders(
            TimeSpan.FromMilliseconds(150),
            (_, _) =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("exec sleep 30");
                return startInfo;
            });

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("test-observer", ["agent", "list"], CancellationToken.None));

        Assert.Contains("deadline", exception.Message, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"Bounded Herdr reader took {stopwatch.Elapsed.ToString("c", CultureInfo.InvariantCulture)}.");
    }

    [Fact]
    public async Task LocalProcessIdentityReader_TimeoutDiscardsLateStartTime()
    {
        using var releaseRead = new ManualResetEventSlim(false);
        using var lateReadCompleted = new ManualResetEventSlim(false);
        var hostReadCount = 0;
        var reader = new ScopedLocalProcessIdentityReader(
            TimeSpan.FromMilliseconds(30),
            _ =>
            {
                releaseRead.Wait();
                return new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
            },
            () =>
            {
                Interlocked.Increment(ref hostReadCount);
                lateReadCompleted.Set();
                return "synthetic-host";
            });

        var result = await reader.ReadAsync(1234);
        Assert.False(result.Resolved);
        Assert.True(result.TimedOut);
        Assert.Null(result.Host);
        Assert.Null(result.ProcessStartTimeUtc);
        Assert.Equal(0, Volatile.Read(ref hostReadCount));

        releaseRead.Set();
        Assert.True(lateReadCompleted.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, Volatile.Read(ref hostReadCount));
        Assert.False(result.Resolved, "A late OS read must not mutate the timeout result into evidence.");
    }

    [Fact]
    public async Task LocalProcessIdentityReader_UsesStableCurrentProcessUtcStartTime()
    {
        var reader = new ScopedLocalProcessIdentityReader();

        var first = await reader.ReadAsync(Environment.ProcessId);
        var second = await reader.ReadAsync(Environment.ProcessId);

        Assert.True(first.Resolved, first.Reason);
        Assert.True(second.Resolved, second.Reason);
        Assert.Equal(Environment.MachineName, first.Host);
        Assert.Equal(Environment.MachineName, second.Host);
        Assert.Equal(DateTimeKind.Utc, first.ProcessStartTimeUtc!.Value.Kind);
        Assert.Equal(first.ProcessStartTimeUtc.Value.Ticks, second.ProcessStartTimeUtc!.Value.Ticks);
    }
}
