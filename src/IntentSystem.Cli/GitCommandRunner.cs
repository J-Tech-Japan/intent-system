using System.Diagnostics;

namespace IntentSystem.Cli;

internal sealed class GitRemoteCommandRunner : IGitRemoteCommandRunner
{
    public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        => GitProcessRunner.Run(
            workingDirectory,
            arguments,
            timeout: null,
            nonInteractive: false);
}

/// <summary>
/// G727: the freshness probe is the only Git surface with a bounded,
/// non-interactive process policy. Other shared Git callers retain the
/// historical runner above and are not silently given this timeout.
/// </summary>
internal sealed class CheckoutFreshnessGitCommandRunner : IGitRemoteCommandRunner
{
    private readonly TimeSpan timeout;

    public CheckoutFreshnessGitCommandRunner(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        this.timeout = timeout;
    }

    public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        => GitProcessRunner.Run(
            workingDirectory,
            arguments,
            timeout,
            nonInteractive: true);
}

internal static class GitProcessRunner
{
    public static GitRemoteCommandResult Run(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout,
        bool nonInteractive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = nonInteractive,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            CreateNoWindow = true,
            UseShellExecute = false
        };

        if (nonInteractive)
        {
            // G727: a read-only freshness check must never wait for a
            // credential or SSH prompt. Closing stdin supplies EOF as an
            // additional guard for transports that consult stdin directly.
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            startInfo.Environment["GIT_SSH_COMMAND"] = NonInteractiveSshCommand();
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        if (nonInteractive)
        {
            process.StandardInput.Close();
        }

        // Read both pipes concurrently and include both reads in the same
        // completion task as process exit. A synchronous ReadToEnd on one
        // pipe can block before WaitForExit when the other pipe fills.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        var completion = Task.WhenAll(
            process.WaitForExitAsync(),
            stdOutTask,
            stdErrTask);

        try
        {
            if (timeout is { } boundedTimeout)
            {
                completion.WaitAsync(boundedTimeout).GetAwaiter().GetResult();
            }
            else
            {
                completion.GetAwaiter().GetResult();
            }
        }
        catch (TimeoutException)
        {
            KillProcessTree(process);
            return new GitRemoteCommandResult
            {
                ExitCode = -1,
                StdOut = stdOutTask.IsCompletedSuccessfully ? stdOutTask.Result : string.Empty,
                StdErr = $"git {string.Join(' ', arguments)} timed out after {timeout?.TotalSeconds:0.###} seconds",
                TimedOut = true,
            };
        }

        return new GitRemoteCommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdOutTask.Result,
            StdErr = stdErrTask.Result,
        };
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill attempt.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process may already have been reaped; the timeout result
            // remains the safe freshness answer either way.
        }
    }

    private static string NonInteractiveSshCommand()
    {
        var configured = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return "ssh -o BatchMode=yes";
        }

        return configured.Contains("BatchMode", StringComparison.OrdinalIgnoreCase)
            ? configured
            : $"{configured} -o BatchMode=yes";
    }
}
