namespace IntentSystem.DomainBinding.Models;

/// <summary>
/// Generic projection-ready binding output that preserves the execution fields
/// needed by projection and queue layers while staying thin.
/// </summary>
public sealed record ProjectionReadySlice
{
    public required string ExecutionUnit { get; init; }

    public required string Goal { get; init; }

    public required string TargetRepo { get; init; }

    public required string TargetPath { get; init; }

    public required string TargetPart { get; init; }

    public required IReadOnlyList<string> Dependencies { get; init; }

    public required string SuccessSignal { get; init; }

    public required string ReviewMode { get; init; }

    public required string CompletionAction { get; init; }

    public required string LandingPolicy { get; init; }

    public required DogfoodingTrack DogfoodingTrack { get; init; }

    public required string EmbeddedCanonicalSummary { get; init; }
}
