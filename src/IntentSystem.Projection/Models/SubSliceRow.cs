namespace IntentSystem.Projection.Models;

public sealed record SubSliceRow
{
    public required string SourceExecutionUnit { get; init; }

    public required string Goal { get; init; }

    public required string TargetRepo { get; init; }

    public required string TargetPath { get; init; }

    public required string TargetPart { get; init; }

    public IReadOnlyList<string> DependsOnSubslices { get; init; } = [];

    public IReadOnlyList<string> DependsOn { get; init; } = [];

    public IReadOnlyList<string> RelatedIntents { get; init; } = [];

    public IReadOnlyList<string> SourceConcepts { get; init; } = [];

    public required string SuccessSignal { get; init; }

    public required string ReviewMode { get; init; }

    public required string CompletionAction { get; init; }

    public required string LandingPolicy { get; init; }
}
