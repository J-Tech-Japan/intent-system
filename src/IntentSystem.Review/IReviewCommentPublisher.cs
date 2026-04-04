namespace IntentSystem.Review;

public interface IReviewCommentPublisher
{
    string PostComment(string linkedPr, string body);
}
