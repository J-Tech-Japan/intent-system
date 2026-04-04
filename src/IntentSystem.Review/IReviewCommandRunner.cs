namespace IntentSystem.Review;

public interface IReviewCommandRunner
{
    ReviewCommandResult Run(IReadOnlyList<string> arguments);
}
