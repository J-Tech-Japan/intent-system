using System.Text.Json;

namespace IntentSystem.Review;

public sealed class GhReviewAcceptClient : IReviewAcceptClient
{
    private readonly IReviewCommandRunner commandRunner;

    public GhReviewAcceptClient()
        : this(new GhReviewCommandRunner())
    {
    }

    public GhReviewAcceptClient(IReviewCommandRunner commandRunner)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public void MarkPullRequestReady(string linkedPr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedPr);

        var pullRequest = GitHubPullRequestRef.Parse(linkedPr);
        var result = commandRunner.Run(
            [
                "api",
                $"repos/{pullRequest.Owner}/{pullRequest.Repo}/pulls/{pullRequest.PullNumber}/ready_for_review",
                "--method",
                "POST"
            ]);

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? "gh api pull request ready_for_review failed."
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }
    }

    public string MergePullRequest(string linkedPr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedPr);

        var pullRequest = GitHubPullRequestRef.Parse(linkedPr);
        var result = commandRunner.Run(
            [
                "api",
                $"repos/{pullRequest.Owner}/{pullRequest.Repo}/pulls/{pullRequest.PullNumber}/merge",
                "--method",
                "PUT",
                "-f",
                "merge_method=merge"
            ]);

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? "gh api pull request merge failed."
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }

        using var document = JsonDocument.Parse(result.StdOut);
        if (!document.RootElement.TryGetProperty("sha", out var shaElement)
            || string.IsNullOrWhiteSpace(shaElement.GetString()))
        {
            throw new InvalidOperationException("GitHub pull request merge response must contain sha.");
        }

        return shaElement.GetString()
            ?? throw new InvalidOperationException("GitHub pull request merge response sha was null.");
    }

    public void CloseIssue(string linkedIssue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedIssue);

        var issue = GitHubIssueRef.Parse(linkedIssue);
        var result = commandRunner.Run(
            [
                "api",
                $"repos/{issue.Owner}/{issue.Repo}/issues/{issue.IssueNumber}",
                "--method",
                "PATCH",
                "-f",
                "state=closed"
            ]);

        if (result.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(result.StdErr)
                ? "gh api issue close failed."
                : result.StdErr.Trim();
            throw new InvalidOperationException(error);
        }
    }
}
