namespace IntentSystem.Cli;

internal interface IRunSubmitPublisher
{
    string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body);
}
