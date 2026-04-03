namespace IntentSystem.DogfoodingBridge.Models;

/// <summary>
/// Distinct return routes for clarify and interview loops.
/// </summary>
public sealed record DogfoodingReturnRoutes
{
    public required string ClarificationReturnPath { get; init; }

    public required IReadOnlyList<string> InterviewReturnToIntentPaths { get; init; }
}
