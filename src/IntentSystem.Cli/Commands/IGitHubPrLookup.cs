using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G203: Testability seam for <c>intent-cli worker pr-review-preflight</c>. The
/// production implementation shells out to <c>gh pr view</c>, but tests inject
/// a fake to avoid GitHub network access.
/// </summary>
internal interface IGitHubPrLookup
{
    GitHubPrLookupResult Lookup(string repo, int prNumber);
}

/// <summary>
/// G203: Default <see cref="IGitHubPrLookup"/> that shells out to
/// <c>gh pr view &lt;num&gt; --repo &lt;repo&gt; --json number,state,title,body,labels,isDraft,closed,merged,mergedAt,closedAt,closingIssuesReferences</c>
/// and deserializes the JSON payload. This is the only file in the worker
/// pr-review-preflight surface that is permitted to call <c>Process.Start</c> —
/// the command and analyzer layers must remain pure.
/// </summary>
internal sealed class GhCliGitHubPrLookup : IGitHubPrLookup
{
    public GitHubPrLookupResult Lookup(string repo, int prNumber)
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
            "number,state,title,body,labels,isDraft,closed,merged,mergedAt,closedAt,closingIssuesReferences"
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
                ?? throw new InvalidOperationException("failed to start `gh` process for PR lookup");
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
                $"could not invoke `gh` to look up PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh pr view {prNumber} --repo {repo}` failed with exit {exitCode}: {errorBody.Trim()}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<GitHubPrLookupResult>(stdout);
            if (result is null)
            {
                throw new InvalidOperationException(
                    $"`gh pr view` returned an empty payload for PR {prNumber} in {repo}");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh pr view` JSON for PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }
    }
}
