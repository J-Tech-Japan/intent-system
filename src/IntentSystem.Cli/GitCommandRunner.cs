using System.Diagnostics;

namespace IntentSystem.Cli;

internal sealed class GitRemoteCommandRunner : IGitRemoteCommandRunner
{
    public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitRemoteCommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdOut,
            StdErr = stdErr
        };
    }
}
