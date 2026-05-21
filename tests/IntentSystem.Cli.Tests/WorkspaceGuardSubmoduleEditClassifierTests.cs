using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G384: pure tests for the redundant-in-submodule-edit classifier — the
/// PR-head-match redundant case, the protected unique/unproven cases, the
/// dedupe fingerprint, and the required-CI blocker precedence.
/// </summary>
public sealed class WorkspaceGuardSubmoduleEditClassifierTests
{
    [Fact]
    public void Classify_RedundantWithPrHeadMatch_IsRedundant_WithSafeRepair()
    {
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submodulePath: "submodules/X/.github/workflows/ci.yml",
            localDiffFingerprint: "abc123",
            prHeadFingerprint: "abc123",
            hasUniqueLocalContent: false,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit, decision.Classification);
        Assert.True(decision.SafeRepairAvailable);
        Assert.NotNull(decision.RecommendedRepair);
    }

    [Fact]
    public void Classify_UniqueLocalContent_IsProtected_NoAutoRepair()
    {
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submodulePath: "submodules/X/f.cs",
            localDiffFingerprint: "abc",
            prHeadFingerprint: "abc",
            hasUniqueLocalContent: true,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.ProtectedOperatorWork, decision.Classification);
        Assert.False(decision.SafeRepairAvailable);
        Assert.Null(decision.RecommendedRepair);
    }

    [Fact]
    public void Classify_UnprovenRedundancy_FingerprintsDiffer_IsProtected()
    {
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submodulePath: "submodules/X/f.cs",
            localDiffFingerprint: "abc",
            prHeadFingerprint: "different",
            hasUniqueLocalContent: false,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.ProtectedOperatorWork, decision.Classification);
        Assert.False(decision.SafeRepairAvailable);
    }

    [Fact]
    public void Classify_MissingFingerprints_IsProtected_NotRedundant()
    {
        // Empty fingerprints are not "matching" — redundancy must be proven.
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submodulePath: "submodules/X/f.cs",
            localDiffFingerprint: "",
            prHeadFingerprint: "",
            hasUniqueLocalContent: false,
            selectedPr: null);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.ProtectedOperatorWork, decision.Classification);
    }

    [Fact]
    public void Classify_NotInternalSubmoduleEdit_IsNotInLane()
    {
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: false,
            submodulePath: "submodules/X",
            localDiffFingerprint: "abc",
            prHeadFingerprint: "abc",
            hasUniqueLocalContent: false,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.NotInSubmoduleLane, decision.Classification);
    }

    [Fact]
    public void IsDuplicateReport_SameFingerprint_IsTrue_DifferentIsFalse()
    {
        var a = WorkspaceGuardSubmoduleEditClassifier.Classify(true, "p", "abc", "abc", false, 1090);
        var b = WorkspaceGuardSubmoduleEditClassifier.Classify(true, "p", "abc", "abc", false, 1090);
        var c = WorkspaceGuardSubmoduleEditClassifier.Classify(true, "p", "xyz", "xyz", false, 1090);

        Assert.True(WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(a.DedupeFingerprint, b.DedupeFingerprint));
        Assert.False(WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(a.DedupeFingerprint, c.DedupeFingerprint));
        Assert.False(WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(null, a.DedupeFingerprint));
    }

    [Fact]
    public void ResolvePrimaryBlocker_FailingCiVisibleEvenWhenRedundantSafe()
    {
        // Redundant-safe workspace + failing required CI → CI is the primary
        // (implementation-actionable) blocker.
        Assert.Equal(
            WorkspaceGuardSubmoduleEditClassifier.Blockers.RequiredCiFailing,
            WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(
                requiredCiFailing: true,
                WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit));

        // Protected operator work is the primary blocker regardless of CI.
        Assert.Equal(
            WorkspaceGuardSubmoduleEditClassifier.Blockers.ProtectedOperatorWork,
            WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(
                requiredCiFailing: true,
                WorkspaceGuardSubmoduleEditClassifier.Classifications.ProtectedOperatorWork));

        // Redundant-safe + green CI → nothing blocks.
        Assert.Equal(
            WorkspaceGuardSubmoduleEditClassifier.Blockers.None,
            WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(
                requiredCiFailing: false,
                WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit));
    }
}
