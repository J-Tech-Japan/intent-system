namespace IntentSystem.Review;

public sealed record GitHubPullRequestRef
{
    public required string Owner { get; init; }

    public required string Repo { get; init; }

    public required int PullNumber { get; init; }

    public static GitHubPullRequestRef Parse(string linkedPr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedPr);

        if (!Uri.TryCreate(linkedPr, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Linked PR '{linkedPr}' must be an absolute URL.");
        }

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 4
            || !string.Equals(segments[2], "pull", StringComparison.Ordinal)
            || !int.TryParse(segments[3], out var pullNumber))
        {
            throw new InvalidOperationException($"Linked PR '{linkedPr}' must use the GitHub pull request URL shape.");
        }

        return new GitHubPullRequestRef
        {
            Owner = segments[0],
            Repo = segments[1],
            PullNumber = pullNumber
        };
    }
}
