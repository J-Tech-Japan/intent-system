using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunProcessRunner : IDirectRunProcessRunner
{
    private static readonly ConcurrentDictionary<int, Process> ActiveProcesses = new();

    public DirectRunProcessLaunchResult Start(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        bool inheritStandardInput,
        bool keepStandardInputOpen,
        TimeSpan earlyExitWindow,
        Action<int> onStarted,
        Action<int> onExited,
        Action<string> onStdOutLine,
        Action<string> onStdErrLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onStarted);
        ArgumentNullException.ThrowIfNull(onExited);
        ArgumentNullException.ThrowIfNull(onStdOutLine);
        ArgumentNullException.ThrowIfNull(onStdErrLine);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = !inheritStandardInput || keepStandardInputOpen,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start direct run process '{fileName}'.");

            if (!inheritStandardInput && !keepStandardInputOpen)
            {
                process.StandardInput.Close();
            }

            ActiveProcesses[process.Id] = process;

            onStarted(process.Id);

            var exitReported = 0;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                if (System.Threading.Interlocked.Exchange(ref exitReported, 1) != 0)
                {
                    return;
                }

                process.WaitForExit();
                onExited(process.ExitCode);
                ReleaseProcess(process);
            };

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrEmpty(eventArgs.Data))
                {
                    onStdOutLine(eventArgs.Data);
                }
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrEmpty(eventArgs.Data))
                {
                    onStdErrLine(eventArgs.Data);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var processId = process.Id;
            var exitedEarly = process.WaitForExit((int)earlyExitWindow.TotalMilliseconds);
            var exitCode = exitedEarly ? TryGetExitCode(process) : 0;

            if (exitedEarly)
            {
                if (System.Threading.Interlocked.Exchange(ref exitReported, 1) == 0)
                {
                    process.WaitForExit();
                    onExited(exitCode);
                    ReleaseProcess(process);
                }
            }

            return new DirectRunProcessLaunchResult
            {
                ProcessId = processId,
                ExitedEarly = exitedEarly,
                ExitCode = exitCode
            };
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to start direct run process '{fileName}': {exception.Message}",
                exception);
        }
    }

    private static void ReleaseProcess(Process process)
    {
        ActiveProcesses.TryRemove(process.Id, out _);
        process.Dispose();
    }

    private static int TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}
