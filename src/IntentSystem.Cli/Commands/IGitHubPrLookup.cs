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
/// <c>gh pr view &lt;num&gt; --repo &lt;repo&gt; --json &lt;PrViewJsonFields&gt;</c>
/// and deserializes the JSON payload. This is the only file in the worker
/// pr-review-preflight surface that is permitted to call <c>Process.Start</c> —
/// the command and analyzer layers must remain pure.
///
/// G204 follow-up: the installed <c>gh</c> CLI rejects <c>merged</c> as a
/// JSON field on <c>gh pr view</c> (see the available-fields list in
/// <c>gh pr view --help</c>). Requesting it caused live invocations of both
/// <c>worker pr-review-preflight</c> and <c>worker pr-comment-preflight</c>
/// (which reuses this adapter) to exit non-zero before classification. The
/// supported subset below omits <c>merged</c> and the post-deserialization
/// step derives <c>Merged</c> from the supported <c>state</c> field instead
/// (<c>state == "MERGED"</c>). Adapter-shape regression tests in
/// <c>WorkerPrReviewPreflightCommandTests</c> lock the field list and the
/// derivation against accidental regression without requiring live GitHub.
/// </summary>
internal sealed class GhCliGitHubPrLookup : IGitHubPrLookup
{
    /// <summary>
    /// G204 follow-up: comma-separated <c>gh pr view --json</c> field list.
    /// Exposed internally so adapter-shape regression tests can assert it
    /// does not regress to including unsupported fields like <c>merged</c>
    /// (which the installed <c>gh</c> CLI rejects). Merged-state is derived
    /// from the supported <c>state</c> field instead — see
    /// <see cref="DeriveMergedFromState"/>.
    /// </summary>
    internal const string PrViewJsonFields =
        "number,state,title,body,labels,isDraft,closed,mergedAt,closedAt,closingIssuesReferences,baseRefName";

    /// <summary>
    /// G204 follow-up: builds the <c>gh pr view</c> argument list shared by
    /// the live adapter and the deterministic adapter-shape tests. Tests can
    /// assert against the returned list without spawning a real <c>gh</c>
    /// process, locking the supported-field subset at the call-shape layer.
    /// </summary>
    internal static IReadOnlyList<string> BuildPrViewArguments(string repo, int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        return new[]
        {
            "pr",
            "view",
            prNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo",
            repo,
            "--json",
            PrViewJsonFields
        };
    }

    /// <summary>
    /// G204 follow-up: derives <c>Merged</c> from the supported <c>state</c>
    /// field (<c>"MERGED"</c> case-insensitive). The installed <c>gh</c> CLI
    /// does not expose <c>merged</c> as a JSON field, so we infer it from
    /// the supported <c>state</c> value instead.
    /// </summary>
    internal static bool DeriveMergedFromState(string? state) =>
        !string.IsNullOrWhiteSpace(state)
        && string.Equals(state, "MERGED", StringComparison.OrdinalIgnoreCase);

    public GitHubPrLookupResult Lookup(string repo, int prNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var arguments = BuildPrViewArguments(repo, prNumber);

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

            // G204 follow-up: derive Merged from the supported `state` field
            // since the installed gh CLI rejects `merged` as a JSON field.
            return result with { Merged = DeriveMergedFromState(result.State) };
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse `gh pr view` JSON for PR {prNumber} in {repo}: {exception.Message}",
                exception);
        }
    }
}
