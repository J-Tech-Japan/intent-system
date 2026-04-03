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

    public bool Equals(SubSliceRow? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return SourceExecutionUnit == other.SourceExecutionUnit
            && Goal == other.Goal
            && TargetRepo == other.TargetRepo
            && TargetPath == other.TargetPath
            && TargetPart == other.TargetPart
            && DependsOnSubslices.SequenceEqual(other.DependsOnSubslices)
            && DependsOn.SequenceEqual(other.DependsOn)
            && RelatedIntents.SequenceEqual(other.RelatedIntents)
            && SourceConcepts.SequenceEqual(other.SourceConcepts)
            && SuccessSignal == other.SuccessSignal
            && ReviewMode == other.ReviewMode
            && CompletionAction == other.CompletionAction
            && LandingPolicy == other.LandingPolicy;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SourceExecutionUnit);
        hash.Add(Goal);
        hash.Add(TargetRepo);
        hash.Add(TargetPath);
        hash.Add(TargetPart);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, DependsOnSubslices);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, DependsOn);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, RelatedIntents);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, SourceConcepts);
        hash.Add(SuccessSignal);
        hash.Add(ReviewMode);
        hash.Add(CompletionAction);
        hash.Add(LandingPolicy);
        return hash.ToHashCode();
    }
}
