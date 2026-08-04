using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G603: GitHub issue and pull-request numbers are scoped by repository.
/// Keep the comparison at the boundary so a number from another repository
/// can never be treated as the same work item on a shared host.
/// </summary>
internal static class GitHubWorkItemIdentity
{
    public static bool MatchesIssue(LinkedIssue? issue, string repo, int number) =>
        issue is { Number: { } recordedNumber, Repo: { Length: > 0 } recordedRepo }
        && recordedNumber == number
        && string.Equals(recordedRepo, repo, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesPullRequest(string? linkedPr, string repo, int number)
    {
        if (string.IsNullOrWhiteSpace(linkedPr)
            || !Uri.TryCreate(linkedPr, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            // A legacy bare number has no repository identity. Failing closed
            // is safer than resolving a same-number PR in a different repo.
            return false;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            && string.Equals($"{parts[0]}/{parts[1]}", repo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "pull", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[3], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var recordedNumber)
            && recordedNumber == number;
    }

    public static bool MatchesPullRequest(QueueItem item, string repo, int number)
    {
        if (MatchesPullRequest(item.LinkedPr, repo, number)) return true;
        // Legacy queue-state can contain a bare linked_pr number. Its only
        // safe compatibility interpretation is alongside a linked_issue that
        // itself names the requested repository; otherwise it is ambiguous.
        return int.TryParse(item.LinkedPr, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var recordedNumber)
            && recordedNumber == number
            && item.LinkedIssue is { Repo: { Length: > 0 } linkedIssueRepo }
            && string.Equals(linkedIssueRepo, repo, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesClosingIssue(GitHubPrClosingIssueReference reference, string repo, int number)
    {
        if (reference.Number != number) return false;
        // GitHub may omit the repository descriptor for same-repo refs.
        if (reference.Repository is null) return true;
        var owner = reference.Repository.Owner?.Login;
        var name = reference.Repository.Name;
        return !string.IsNullOrWhiteSpace(owner)
            && !string.IsNullOrWhiteSpace(name)
            && string.Equals($"{owner}/{name}", repo, StringComparison.OrdinalIgnoreCase);
    }

    public static int? GetPullRequestNumberForRepo(string? linkedPr, string repo)
    {
        if (string.IsNullOrWhiteSpace(linkedPr)
            || !Uri.TryCreate(linkedPr, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4
            && string.Equals($"{parts[0]}/{parts[1]}", repo, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "pull", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(parts[3], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }
}
