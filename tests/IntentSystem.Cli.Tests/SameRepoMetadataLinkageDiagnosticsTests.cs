using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G390: tests for the same-repo metadata-branch linkage recovery classifier —
/// the PR #3639-style high-confidence repair, the wrong-branch refusal, the
/// advisory-only ambiguity, and the not-applicable (single-root) case.
/// </summary>
public sealed class SameRepoMetadataLinkageDiagnosticsTests
{
    [Fact]
    public void Classify_HighConfidenceLinkedPrRecovery_IsRepairReadyAndWriteable()
    {
        var result = SameRepoMetadataLinkageDiagnostics.Classify(
            sameRepoMetadataBranchConfigured: true,
            recoveryTargetsImplementationBranch: false,
            selectedPrInTargetRepo: true,
            prUniquelyClosesLinkedIssue: true,
            executionUnitIdentified: true,
            onlyLinkedPrMissing: true);

        Assert.Equal(SameRepoMetadataLinkageDiagnostics.Classifications.RepairReady, result.Classification);
        Assert.True(result.Writeable);
        Assert.Contains("publish-recovery", result.RecommendedCommand, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_RecoveryTargetsImplementationBranch_IsWrongBranchUnsafe()
    {
        var result = SameRepoMetadataLinkageDiagnostics.Classify(
            sameRepoMetadataBranchConfigured: true,
            recoveryTargetsImplementationBranch: true,
            selectedPrInTargetRepo: true,
            prUniquelyClosesLinkedIssue: true,
            executionUnitIdentified: true,
            onlyLinkedPrMissing: true);

        Assert.Equal(SameRepoMetadataLinkageDiagnostics.Classifications.WrongBranchUnsafe, result.Classification);
        Assert.False(result.Writeable);
        Assert.Contains("metadata branch", result.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_AmbiguousEvidence_IsAdvisoryOnly()
    {
        // PR does not uniquely close the issue → ambiguous → advisory only.
        var result = SameRepoMetadataLinkageDiagnostics.Classify(
            sameRepoMetadataBranchConfigured: true,
            recoveryTargetsImplementationBranch: false,
            selectedPrInTargetRepo: true,
            prUniquelyClosesLinkedIssue: false,
            executionUnitIdentified: true,
            onlyLinkedPrMissing: true);

        Assert.Equal(SameRepoMetadataLinkageDiagnostics.Classifications.AdvisoryOnly, result.Classification);
        Assert.False(result.Writeable);
    }

    [Fact]
    public void Classify_MoreThanLinkedPrMissing_IsAdvisoryOnly()
    {
        var result = SameRepoMetadataLinkageDiagnostics.Classify(
            sameRepoMetadataBranchConfigured: true,
            recoveryTargetsImplementationBranch: false,
            selectedPrInTargetRepo: true,
            prUniquelyClosesLinkedIssue: true,
            executionUnitIdentified: true,
            onlyLinkedPrMissing: false);

        Assert.Equal(SameRepoMetadataLinkageDiagnostics.Classifications.AdvisoryOnly, result.Classification);
        Assert.False(result.Writeable);
    }

    [Fact]
    public void Classify_NotSameRepoTopology_IsNotApplicable()
    {
        var result = SameRepoMetadataLinkageDiagnostics.Classify(
            sameRepoMetadataBranchConfigured: false,
            recoveryTargetsImplementationBranch: false,
            selectedPrInTargetRepo: true,
            prUniquelyClosesLinkedIssue: true,
            executionUnitIdentified: true,
            onlyLinkedPrMissing: true);

        Assert.Equal(SameRepoMetadataLinkageDiagnostics.Classifications.NotApplicable, result.Classification);
        Assert.False(result.Writeable);
    }
}
