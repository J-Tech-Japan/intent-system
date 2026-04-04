namespace IntentSystem.Review;

public static class ReviewArtifactPathResolver
{
    private const string ReviewsDirectory = ".intent-cli/reviews";

    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return $"{ReviewsDirectory}/{executionUnit.Trim()}.request.json";
    }
}
