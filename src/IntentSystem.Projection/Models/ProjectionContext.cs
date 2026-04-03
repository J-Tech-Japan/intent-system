namespace IntentSystem.Projection.Models;

public sealed record ProjectionContext
{
    public required string IssueTitle { get; init; }

    public required IssueKind IssueKind { get; init; }

    public required string ParentIntentRoot { get; init; }

    public required string ClarificationReturnPath { get; init; }

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    public IReadOnlyList<string> DeterministicReviewChecks { get; init; } = [];

    public IReadOnlyList<string> VerificationEvidence { get; init; } = [];

    public IReadOnlyList<string> TechnicalBaseline { get; init; } = [];

    public IReadOnlyList<string> ProjectLocalGuide { get; init; } = [];

    public IReadOnlyList<string> IntentBaseline { get; init; } = [];

    public IReadOnlyList<string> AdditionalInScope { get; init; } = [];

    public IReadOnlyList<string> OutOfScope { get; init; } = [];

    public bool Equals(ProjectionContext? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return IssueTitle == other.IssueTitle
            && IssueKind == other.IssueKind
            && ParentIntentRoot == other.ParentIntentRoot
            && ClarificationReturnPath == other.ClarificationReturnPath
            && AcceptanceCriteria.SequenceEqual(other.AcceptanceCriteria)
            && DeterministicReviewChecks.SequenceEqual(other.DeterministicReviewChecks)
            && VerificationEvidence.SequenceEqual(other.VerificationEvidence)
            && TechnicalBaseline.SequenceEqual(other.TechnicalBaseline)
            && ProjectLocalGuide.SequenceEqual(other.ProjectLocalGuide)
            && IntentBaseline.SequenceEqual(other.IntentBaseline)
            && AdditionalInScope.SequenceEqual(other.AdditionalInScope)
            && OutOfScope.SequenceEqual(other.OutOfScope);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(IssueTitle);
        hash.Add(IssueKind);
        hash.Add(ParentIntentRoot);
        hash.Add(ClarificationReturnPath);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, AcceptanceCriteria);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, DeterministicReviewChecks);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, VerificationEvidence);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, TechnicalBaseline);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, ProjectLocalGuide);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, IntentBaseline);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, AdditionalInScope);
        ReadOnlyListHashCodeExtensions.AddValues(ref hash, OutOfScope);
        return hash.ToHashCode();
    }
}
