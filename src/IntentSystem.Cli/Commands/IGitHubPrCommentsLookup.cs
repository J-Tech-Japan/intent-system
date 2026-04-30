using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G204: Testability seam for <c>intent-cli worker pr-comment-preflight</c>.
/// The production implementation shells out to <c>gh pr view ... --json
/// reviews,comments,reviewThreads</c>, but tests inject a fake to avoid
/// GitHub network access.
/// </summary>
internal interface IGitHubPrCommentsLookup
{
    GitHubPrCommentsLookupResult Lookup(string repo, int prNumber);
}

/// <summary>
/// G204: Default <see cref="IGitHubPrCommentsLookup"/> that shells out to
/// <c>gh pr view &lt;num&gt; --repo &lt;repo&gt; --json reviews,comments,reviewThreads</c>
/// and deserializes the JSON payload. This is the only file in the worker
/// pr-comment-preflight surface that is permitted to call
/// <c>Process.Start</c> — the command and analyzer layers must remain pure.
/// </summary>
internal sealed class GhCliGitHubPrCommentsLookup : IGitHubPrCommentsLookup
{
    public GitHubPrCommentsLookupResult Lookup(string repo, int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var arguments = new List<string>
        {
            "pr",
            "view",
            prNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo",
            repo,
            "--json",
            "reviews,comments,reviewThreads"
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
                ?? throw new InvalidOperationException(
                    "failed to start `gh` process for PR comments lookup");
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
                $"could not invoke `gh` to look up comments for PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh pr view {prNumber} --repo {repo} --json reviews,comments,reviewThreads` failed with exit {exitCode}: {errorBody.Trim()}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<GitHubPrCommentsLookupResult>(stdout);
            if (result is null)
            {
                throw new InvalidOperationException(
                    $"`gh pr view` returned an empty comments payload for PR {prNumber} in {repo}");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh pr view` comments JSON for PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }
    }
}
