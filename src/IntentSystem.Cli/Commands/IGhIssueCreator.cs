namespace IntentSystem.Cli.Commands;

/// <summary>
/// G184 testability seam: minimal interface for creating a single GitHub issue
/// from a reviewed publish packet via <c>gh issue create</c>. The contract is
/// deliberately narrow — it has no label parameter so the host-owned
/// <c>intent-target</c> boundary cannot be crossed by accident.
/// </summary>
internal interface IGhIssueCreator
{
    /// <summary>
    /// Creates one issue in <paramref name="repo"/> with the given
    /// <paramref name="title"/> and Markdown body file. Returns the URL printed
    /// by <c>gh issue create</c> (one line, e.g.
    /// <c>https://github.com/owner/repo/issues/123</c>).
    /// </summary>
    string CreateIssue(string repo, string title, string bodyFilePath);
}
