namespace IntentSystem.Review;

public sealed record GitHubIssueRef
{
    public required string Owner { get; init; }

    public required string Repo { get; init; }

    public required int IssueNumber { get; init; }

    public static GitHubIssueRef Parse(string linkedIssue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedIssue);

        if (!Uri.TryCreate(linkedIssue, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Linked issue '{linkedIssue}' must be an absolute URL.");
        }

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length != 4
            || !string.Equals(segments[2], "issues", StringComparison.Ordinal)
            || !int.TryParse(segments[3], out var issueNumber))
        {
            throw new InvalidOperationException($"Linked issue '{linkedIssue}' must use the GitHub issue URL shape.");
        }

        return new GitHubIssueRef
        {
            Owner = segments[0],
            Repo = segments[1],
            IssueNumber = issueNumber
        };
    }
}
