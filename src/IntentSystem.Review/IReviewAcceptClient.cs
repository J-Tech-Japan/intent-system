namespace IntentSystem.Review;

public interface IReviewAcceptClient
{
    void MarkPullRequestReady(string linkedPr);

    string MergePullRequest(string linkedPr);

    void CloseIssue(string linkedIssue);
}
