namespace IntentSystem.Review;

public interface IReviewAcceptClient
{
    string MergePullRequest(string linkedPr);

    void CloseIssue(string linkedIssue);
}
