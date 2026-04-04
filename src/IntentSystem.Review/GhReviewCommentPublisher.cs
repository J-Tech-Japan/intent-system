using System.Text.Json;

namespace IntentSystem.Review;

public sealed class GhReviewCommentPublisher : IReviewCommentPublisher
{
    private readonly IReviewCommandRunner commandRunner;

    public GhReviewCommentPublisher()
        : this(new GhReviewCommandRunner())
    {
    }

    public GhReviewCommentPublisher(IReviewCommandRunner commandRunner)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string PostComment(string linkedPr, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedPr);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var pullRequest = GitHubPullRequestRef.Parse(linkedPr);
        var payloadPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(payloadPath, JsonSerializer.Serialize(new { body }));

            var result = commandRunner.Run(
                [
                    "api",
                    $"repos/{pullRequest.Owner}/{pullRequest.Repo}/issues/{pullRequest.PullNumber}/comments",
                    "--method",
                    "POST",
                    "--input",
                    payloadPath
                ]);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? "gh api comment post failed."
                    : result.StdErr.Trim();
                throw new InvalidOperationException(error);
            }

            using var document = JsonDocument.Parse(result.StdOut);
            if (!document.RootElement.TryGetProperty("html_url", out var htmlUrlElement)
                || string.IsNullOrWhiteSpace(htmlUrlElement.GetString()))
            {
                throw new InvalidOperationException("GitHub comment post response must contain html_url.");
            }

            return htmlUrlElement.GetString()
                ?? throw new InvalidOperationException("GitHub comment post response html_url was null.");
        }
        finally
        {
            if (File.Exists(payloadPath))
            {
                File.Delete(payloadPath);
            }
        }
    }
}
