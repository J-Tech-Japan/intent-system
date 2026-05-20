using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: testability seam for posting and reading GitHub issue/PR
/// comments in the structured worker-signal protocol. The production
/// implementation shells out to <c>gh issue/pr comment</c> (write) and
/// <c>gh issue/pr view --json comments</c> (read); tests inject a fake
/// to avoid any GitHub network access and to verify the dry-run / write
/// split. Label transitions stay on the existing
/// <see cref="IGitHubLabelMutator"/> seam — this seam only touches
/// comments.
/// </summary>
internal interface IGitHubSignalGateway
{
    /// <summary>
    /// Post <paramref name="body"/> as a new comment on the issue/PR and
    /// return the created comment reference (the URL <c>gh</c> prints).
    /// </summary>
    string PostComment(string repo, string kind, int number, string body);

    /// <summary>
    /// Read the existing comments on the issue/PR. Read-only — never
    /// mutates GitHub.
    /// </summary>
    IReadOnlyList<GitHubSignalComment> ListComments(string repo, string kind, int number);
}

/// <summary>G374: a single GitHub issue/PR comment row.</summary>
internal sealed record GitHubSignalComment
{
    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public GitHubSignalCommentAuthor Author { get; init; } = new();
}

internal sealed record GitHubSignalCommentAuthor
{
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;
}

/// <summary>
/// G374: default gateway that shells out to <c>gh</c>. The only file in
/// the signal surface permitted to call <c>Process.Start</c> — the
/// contract, analyzer, and command layers stay pure. Comment bodies are
/// passed via stdin (<c>--body-file -</c>) so multi-line / special
/// characters never hit shell quoting.
/// </summary>
internal sealed class GhCliGitHubSignalGateway : IGitHubSignalGateway
{
    public static class Kinds
    {
        public const string Issue = "issue";
        public const string Pr = "pr";
    }

    /// <summary>G374: <c>gh ... view --json comments</c> field name.</summary>
    internal const string ViewCommentsJsonFields = "comments";

    /// <summary>G374: build the <c>gh issue/pr comment</c> argument list (body via stdin).</summary>
    internal static IReadOnlyList<string> BuildCommentArguments(string repo, string kind, int number)
    {
        ValidateKindAndNumber(repo, kind, number);
        return new List<string>
        {
            kind,
            "comment",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo,
            "--body-file", "-",
        };
    }

    /// <summary>G374: build the <c>gh issue/pr view --json comments</c> argument list.</summary>
    internal static IReadOnlyList<string> BuildViewCommentsArguments(string repo, string kind, int number)
    {
        ValidateKindAndNumber(repo, kind, number);
        return new List<string>
        {
            kind,
            "view",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", ViewCommentsJsonFields,
        };
    }

    public string PostComment(string repo, string kind, int number, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        var args = BuildCommentArguments(repo, kind, number);
        var stdout = RunGh(args, $"post comment on {kind} #{number} in {repo}", body);
        return stdout.Trim();
    }

    public IReadOnlyList<GitHubSignalComment> ListComments(string repo, string kind, int number)
    {
        var args = BuildViewCommentsArguments(repo, kind, number);
        var stdout = RunGh(args, $"read comments on {kind} #{number} in {repo}", standardInput: null);
        return DeserializeComments(stdout, $"`gh {kind} view #{number} --json comments` for {repo}");
    }

    private static void ValidateKindAndNumber(string repo, string kind, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "issue/PR number must be positive.");
        }
        if (!string.Equals(kind, Kinds.Issue, StringComparison.Ordinal)
            && !string.Equals(kind, Kinds.Pr, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"unrecognized kind '{kind}'. Supported: '{Kinds.Issue}', '{Kinds.Pr}'.",
                nameof(kind));
        }
    }

    private static string RunGh(IReadOnlyList<string> arguments, string description, string? standardInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
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

            if (standardInput is not null)
            {
                using (var stdin = process.StandardInput)
                {
                    stdin.Write(standardInput);
                }
            }

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

    private static IReadOnlyList<GitHubSignalComment> DeserializeComments(
        string stdout,
        string callDescription)
    {
        try
        {
            var view = JsonSerializer.Deserialize<CommentsView>(stdout);
            return view?.Comments ?? (IReadOnlyList<GitHubSignalComment>)Array.Empty<GitHubSignalComment>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse {callDescription} JSON: {exception.Message}",
                exception);
        }
    }

    private sealed record CommentsView
    {
        [JsonPropertyName("comments")]
        public IReadOnlyList<GitHubSignalComment> Comments { get; init; }
            = Array.Empty<GitHubSignalComment>();
    }
}
