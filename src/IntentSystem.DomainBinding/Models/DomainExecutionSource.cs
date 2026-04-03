namespace IntentSystem.DomainBinding.Models;

/// <summary>
/// Canonical, repo-local summary of a private execution source that can be
/// embedded in fixtures without exposing private paths or URLs.
/// </summary>
public sealed record DomainExecutionSource
{
    public required DomainExecutionSourceKind SourceKind { get; init; }

    public required DogfoodingTrack DogfoodingTrack { get; init; }

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

    public required string EmbeddedCanonicalSummary { get; init; }
}
