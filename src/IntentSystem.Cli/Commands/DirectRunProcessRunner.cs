using System.ComponentModel;
using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

internal sealed class DirectRunProcessRunner : IDirectRunProcessRunner
{
    public DirectRunProcessLaunchResult Start(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan earlyExitWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start direct run process '{fileName}'.");

            var exitedEarly = process.WaitForExit((int)earlyExitWindow.TotalMilliseconds);
            var exitCode = exitedEarly ? process.ExitCode : 0;
            var processId = process.Id;

            if (exitedEarly)
            {
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
