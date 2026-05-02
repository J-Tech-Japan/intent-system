using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: Testability seam for <c>intent-cli worker next-action</c>. The
/// production implementation shells out to <c>gh pr list</c> and
/// <c>gh issue list</c> with label filters; tests inject a fake to avoid
/// any GitHub network access.
/// </summary>
internal interface IGitHubAutomationCandidateLister
{
    IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels);

    IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels);
}

/// <summary>
/// G206: Single PR candidate row returned by
/// <see cref="IGitHubAutomationCandidateLister.ListPullRequests"/>.
/// </summary>
internal sealed record GitHubAutomationPrCandidate
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
        = Array.Empty<GitHubAutomationLabel>();

    [JsonPropertyName("closingIssuesReferences")]
    public IReadOnlyList<GitHubPrClosingIssueReference> ClosingIssuesReferences { get; init; }
        = Array.Empty<GitHubPrClosingIssueReference>();
}

/// <summary>
/// G206: Single issue candidate row returned by
/// <see cref="IGitHubAutomationCandidateLister.ListIssues"/>.
/// </summary>
internal sealed record GitHubAutomationIssueCandidate
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
        = Array.Empty<GitHubAutomationLabel>();
}

internal sealed record GitHubAutomationLabel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// G206: Default lister that shells out to <c>gh pr list</c> and
/// <c>gh issue list</c>. The only file in the worker next-action surface
/// permitted to call <c>Process.Start</c> — the analyzer and command layers
/// must remain pure. Both calls request stable supported field subsets.
/// PR listing also includes body and closing issue metadata so host selectors
/// can model issue-linked PR fallback without extra mutation-capable calls.
/// </summary>
internal sealed class GhCliGitHubAutomationCandidateLister : IGitHubAutomationCandidateLister
{
    /// <summary>
    /// G206: comma-separated <c>gh pr list --json</c> field list. Exposed
    /// internally so adapter-shape regression tests can lock the supported
    /// subset.
    /// </summary>
    internal const string ListJsonFields = "number,title,url,createdAt,labels";

    internal const string PrListJsonFields = "number,title,url,body,createdAt,labels,closingIssuesReferences";

    /// <summary>
    /// G206: builds the <c>gh pr list</c> argument list shared by the live
    /// adapter and adapter-shape tests.
    /// </summary>
    internal static IReadOnlyList<string> BuildPrListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "pr",
            "list",
            "--repo", repo,
            "--state", "open",
            "--json", PrListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    /// <summary>
    /// G206: builds the <c>gh issue list</c> argument list.
    /// </summary>
    internal static IReadOnlyList<string> BuildIssueListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "issue",
            "list",
            "--repo", repo,
            "--state", "open",
            "--json", ListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildPrListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list PRs in {repo}");
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildIssueListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list issues in {repo}");
        return DeserializeList<GitHubAutomationIssueCandidate>(stdout, $"`gh issue list` for {repo}");
    }

    private static string RunGh(IReadOnlyList<string> arguments, string description)
    {
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
                    $"failed to start `gh` process to {description}");
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
                $"could not invoke `gh` to {description}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh` failed to {description} with exit {exitCode}: {errorBody.Trim()}");
        }

        return stdout;
    }

    private static IReadOnlyList<T> DeserializeList<T>(string stdout, string callDescription)
    {
        try
        {
            var result = JsonSerializer.Deserialize<List<T>>(stdout);
            return (IReadOnlyList<T>?)result ?? Array.Empty<T>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse {callDescription} JSON: {exception.Message}",
                exception);
        }
    }
}
