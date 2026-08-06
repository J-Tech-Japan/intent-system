using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Default <see cref="IGhIssueCreator"/> implementation. Shells out to
/// <c>gh issue create --repo &lt;repo&gt; --title &lt;title&gt; --body-file &lt;path&gt;</c>
/// and returns the trimmed stdout (the issue URL printed by gh). No labels are
/// passed; that boundary is owned by the host-review-loop runbook.
/// </summary>
internal sealed class GhIssueCreator : IGhIssueCreator
{
    public string CreateIssue(string repo, string title, string bodyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyFilePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom
        };

        startInfo.ArgumentList.Add("issue");
        startInfo.ArgumentList.Add("create");
        startInfo.ArgumentList.Add("--repo");
        startInfo.ArgumentList.Add(repo);
        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add("--body-file");
        startInfo.ArgumentList.Add(bodyFilePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"gh issue create failed (exit {process.ExitCode}): {stdErr.Trim()}");
        }

        return stdOut.Trim();
    }
}
