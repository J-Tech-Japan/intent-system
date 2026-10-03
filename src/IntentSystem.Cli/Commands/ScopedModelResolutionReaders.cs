using System.Diagnostics;
using System.Globalization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Dedicated read-only Herdr process boundary for scoped model resolution.
/// It intentionally does not share the unbounded notify runner.
/// </summary>
internal sealed class ScopedModelResolutionReaders : INotifyProcessRunner
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan timeout;
    private readonly Func<string, IReadOnlyList<string>, ProcessStartInfo> startInfoFactory;

    public ScopedModelResolutionReaders()
        : this(DefaultTimeout, CreateStartInfo)
    {
    }

    internal ScopedModelResolutionReaders(
        TimeSpan timeout,
        Func<string, IReadOnlyList<string>, ProcessStartInfo>? startInfoFactory = null)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        this.timeout = timeout;
        this.startInfoFactory = startInfoFactory ?? CreateStartInfo;
    }

    public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
        RunAsync(fileName, arguments, CancellationToken.None).GetAwaiter().GetResult();

    public async Task<NotifyProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedRead(arguments))
            throw new InvalidOperationException("Scoped model resolution permits only `herdr agent list` and exact-pane `herdr pane process-info` reads.");
        cancellationToken.ThrowIfCancellationRequested();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var process = new Process { StartInfo = startInfoFactory(fileName, arguments) };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Scoped Herdr reader '{fileName}' did not start.");

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException();
            deadline.CancelAfter(remaining);

            var standardOutput = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var standardError = process.StandardError.ReadToEndAsync(deadline.Token);
            var exited = process.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(exited, standardOutput, standardError)
                .WaitAsync(deadline.Token)
                .ConfigureAwait(false);

            return new NotifyProcessResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TerminateOnlyObserver(process);
            throw new InvalidOperationException($"Scoped Herdr read exceeded its {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}-second deadline.");
        }
        catch (TimeoutException)
        {
            TerminateOnlyObserver(process);
            throw new InvalidOperationException($"Scoped Herdr read exceeded its {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)}-second deadline.");
        }
        catch (OperationCanceledException)
        {
            TerminateOnlyObserver(process);
            throw;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            TerminateOnlyObserver(process);
            throw new InvalidOperationException($"Scoped Herdr reader '{fileName}' could not complete: {exception.Message}", exception);
        }
        catch
        {
            // Stream failures and unexpected wait errors must not leave the
            // observer process running after this read has failed.
            TerminateOnlyObserver(process);
            throw;
        }
    }

    private static bool IsAllowedRead(IReadOnlyList<string> arguments) =>
        arguments.Count == 2
            && arguments[0] == "agent"
            && arguments[1] == "list"
        || arguments.Count == 4
            && arguments[0] == "pane"
            && arguments[1] == "process-info"
            && arguments[2] == "--pane"
            && !string.IsNullOrWhiteSpace(arguments[3]);

    private static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static void TerminateOnlyObserver(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The observer exited during timeout handling.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Do not block while trying to clean up an already-failing observer.
        }
    }
}

internal sealed record ScopedLocalProcessIdentityReadResult(
    bool Resolved,
    long Pid,
    string? Host,
    DateTime? ProcessStartTimeUtc,
    bool TimedOut,
    string? Reason);

/// <summary>
/// Reads a real local process generation. A timed-out OS call is abandoned;
/// its eventual value is never returned as evidence.
/// </summary>
internal sealed class ScopedLocalProcessIdentityReader
{
    private readonly TimeSpan timeout;
    private readonly Func<long, DateTime> startTimeReader;
    private readonly Func<string> hostReader;

    public ScopedLocalProcessIdentityReader()
        : this(ScopedModelResolutionReaders.DefaultTimeout, ReadActualStartTimeUtc, () => Environment.MachineName)
    {
    }

    internal ScopedLocalProcessIdentityReader(
        TimeSpan timeout,
        Func<long, DateTime> startTimeReader,
        Func<string> hostReader)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        this.timeout = timeout;
        this.startTimeReader = startTimeReader;
        this.hostReader = hostReader;
    }

    public async Task<ScopedLocalProcessIdentityReadResult> ReadAsync(long pid)
    {
        if (pid <= 0 || pid > int.MaxValue)
            return Failure(pid, "invalid-pid");

        var operation = Task.Run(() =>
        {
            var startTimeUtc = startTimeReader(pid).ToUniversalTime();
            var host = hostReader();
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("The local machine name is unavailable.");
            return (Host: host, StartTimeUtc: startTimeUtc);
        });

        try
        {
            var identity = await operation.WaitAsync(timeout).ConfigureAwait(false);
            return new ScopedLocalProcessIdentityReadResult(
                true, pid, identity.Host, identity.StartTimeUtc, false, null);
        }
        catch (TimeoutException)
        {
            // The OS API has no cancellation contract. Discard the late value;
            // the read task owns and disposes its Process instance.
            return new ScopedLocalProcessIdentityReadResult(false, pid, null, null, true, "process-start-time-timeout");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or ArgumentException
            or NotSupportedException)
        {
            return Failure(pid, "process-start-time-unavailable");
        }
    }

    private static DateTime ReadActualStartTimeUtc(long pid)
    {
        using var process = Process.GetProcessById(checked((int)pid));
        return process.StartTime.ToUniversalTime();
    }

    private static ScopedLocalProcessIdentityReadResult Failure(long pid, string reason) =>
        new(false, pid, null, null, false, reason);
}
