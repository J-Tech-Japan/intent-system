using System.Diagnostics;

namespace IntentSystem.Cli;

internal sealed class GhCommandRunner : IGitHubCommandRunner
{
    public GitHubCommandResult Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            StandardErrorEncoding = GitHubCliProcessEncoding.Utf8NoBom
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new GitHubCommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdOut,
            StdErr = stdErr
        };
    }
}
