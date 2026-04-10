using System.ComponentModel;
using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunProcessRunner : IDirectRunProcessRunner
{
    public DirectRunProcessLaunchResult Start(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan earlyExitWindow,
        Action<int> onStarted,
        Action<string> onStdOutLine,
        Action<string> onStdErrLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onStarted);
        ArgumentNullException.ThrowIfNull(onStdOutLine);
        ArgumentNullException.ThrowIfNull(onStdErrLine);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start direct run process '{fileName}'.");

            onStarted(process.Id);

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

            var exitedEarly = process.WaitForExit((int)earlyExitWindow.TotalMilliseconds);
            var exitCode = exitedEarly ? process.ExitCode : 0;
            var processId = process.Id;

            if (exitedEarly)
            {
                process.WaitForExit();
                process.Dispose();
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
}
