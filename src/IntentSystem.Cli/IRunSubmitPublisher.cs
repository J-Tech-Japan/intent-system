namespace IntentSystem.Cli;

internal interface IRunSubmitPublisher
{
    string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body);

    bool TryFindExistingOpenPullRequest(
        string targetRepo,
        string headBranch,
        string linkedIssueUrl,
        out string pullRequestUrl);
}
