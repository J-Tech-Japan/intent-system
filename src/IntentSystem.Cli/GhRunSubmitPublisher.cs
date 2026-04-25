using System.Text.Json;

namespace IntentSystem.Cli;

internal sealed class GhRunSubmitPublisher : IRunSubmitPublisher
{
    private const string PullRequestBase = "main";

    private readonly IGitHubCommandRunner commandRunner;

    public GhRunSubmitPublisher()
        : this(new GhCommandRunner())
    {
    }

    public GhRunSubmitPublisher(IGitHubCommandRunner commandRunner)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(headBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);

        var bodyPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(bodyPath, body);

            var result = commandRunner.Run(
                [
                    "pr",
                    "create",
                    "--draft",
                    "--base",
                    "main",
                    "--head",
                    headBranch,
                    "--title",
                    title,
                    "--body-file",
                    bodyPath,
                    "--repo",
                    targetRepo
                ]);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? "gh pr create failed."
                    : result.StdErr.Trim();
                if (IsExistingPullRequestError(error)
                    && TryFindExistingPullRequest(targetRepo, headBranch, out var existingPullRequestUrl))
                {
                    return existingPullRequestUrl;
                }

                throw new InvalidOperationException(error);
            }

            var pullRequestUrl = result.StdOut.Trim();
            if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("GitHub PR create response must contain an absolute URL.");
            }

            return pullRequestUrl;
        }
        finally
        {
            if (File.Exists(bodyPath))
            {
                File.Delete(bodyPath);
            }
        }
    }

    public bool TryFindExistingOpenPullRequest(
        string targetRepo,
        string headBranch,
        string linkedIssueUrl,
        out string pullRequestUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(headBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedIssueUrl);

        if (TryFindExistingPullRequest(targetRepo, headBranch, out pullRequestUrl))
        {
            return true;
        }

        return TryFindOpenPullRequestByLinkedIssueUrl(targetRepo, linkedIssueUrl, out pullRequestUrl);
    }

    private bool TryFindExistingPullRequest(
        string targetRepo,
        string headBranch,
        out string pullRequestUrl)
    {
        var result = commandRunner.Run(
            [
                "pr",
                "list",
                "--repo",
                targetRepo,
                "--head",
                headBranch,
                "--base",
                PullRequestBase,
                "--state",
                "open",
                "--json",
                "url,headRefName,baseRefName"
            ]);

        pullRequestUrl = string.Empty;
        if (result.ExitCode != 0)
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.StdOut);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryGetStringProperty(element, "url", out var candidateUrl)
                    || !TryGetStringProperty(element, "headRefName", out var candidateHead)
                    || !TryGetStringProperty(element, "baseRefName", out var candidateBase))
                {
                    continue;
                }

                if (!string.Equals(candidateHead, headBranch, StringComparison.Ordinal)
                    || !string.Equals(candidateBase, PullRequestBase, StringComparison.Ordinal)
                    || !Uri.TryCreate(candidateUrl, UriKind.Absolute, out _))
                {
                    continue;
                }

                pullRequestUrl = candidateUrl;
                return true;
            }
        }

        return false;
    }

    private bool TryFindOpenPullRequestByLinkedIssueUrl(
        string targetRepo,
        string linkedIssueUrl,
        out string pullRequestUrl)
    {
        var result = commandRunner.Run(
            [
                "pr",
                "list",
                "--repo",
                targetRepo,
                "--base",
                PullRequestBase,
                "--state",
                "open",
                "--json",
                "url,headRefName,baseRefName,body",
                "--limit",
                "100"
            ]);

        pullRequestUrl = string.Empty;
        if (result.ExitCode != 0)
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(result.StdOut);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (!TryGetStringProperty(element, "url", out var candidateUrl)
                    || !TryGetStringProperty(element, "baseRefName", out var candidateBase)
                    || !TryGetStringProperty(element, "body", out var candidateBody))
                {
                    continue;
                }

                if (!string.Equals(candidateBase, PullRequestBase, StringComparison.Ordinal)
                    || !Uri.TryCreate(candidateUrl, UriKind.Absolute, out _))
                {
                    continue;
                }

                if (!candidateBody.Contains(linkedIssueUrl, StringComparison.Ordinal))
                {
                    continue;
                }

                pullRequestUrl = candidateUrl;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        return false;
    }

    private static bool IsExistingPullRequestError(string error)
    {
        return error.Contains("pull request", StringComparison.OrdinalIgnoreCase)
            && error.Contains("already exist", StringComparison.OrdinalIgnoreCase);
    }
}
