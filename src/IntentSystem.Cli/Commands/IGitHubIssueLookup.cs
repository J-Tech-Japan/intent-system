using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G202: Testability seam for <c>intent-cli worker issue-preflight</c>. The
/// production implementation shells out to <c>gh issue view</c>, but tests
/// inject a fake to avoid GitHub network access.
/// </summary>
internal interface IGitHubIssueLookup
{
    GitHubIssueLookupResult Lookup(string repo, int issueNumber);
}

/// <summary>
/// G202: Default <see cref="IGitHubIssueLookup"/> that shells out to
/// <c>gh issue view &lt;num&gt; --repo &lt;repo&gt; --json number,state,labels,title,body,closed,closedAt</c>
/// and deserializes the JSON payload. This is the only file in the worker
/// preflight surface that is permitted to call <c>Process.Start</c> — the
/// command and analyzer layers must remain pure.
/// </summary>
internal sealed class GhCliGitHubIssueLookup : IGitHubIssueLookup
{
    public GitHubIssueLookupResult Lookup(string repo, int issueNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var arguments = new List<string>
        {
            "issue",
            "view",
            issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo",
            repo,
            "--json",
            "number,state,labels,title,body,closed,closedAt"
        };

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        string stdout;
        string stderr;
        int exitCode;

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("failed to start `gh` process for issue lookup");
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new InvalidOperationException(
                $"could not invoke `gh` to look up issue {issueNumber} in {repo}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh issue view {issueNumber} --repo {repo}` failed with exit {exitCode}: {errorBody.Trim()}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<GitHubIssueLookupResult>(stdout);
            if (result is null)
            {
                throw new InvalidOperationException(
                    $"`gh issue view` returned an empty payload for issue {issueNumber} in {repo}");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh issue view` JSON for issue {issueNumber} in {repo}: {exception.Message}",
                exception);
        }
    }
}
