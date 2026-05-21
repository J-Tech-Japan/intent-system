using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G384: pure tests for the redundant-in-submodule-edit classifier — the
/// PR-head-match redundant case (with a bounded per-file repair command),
/// the protected unique/unproven cases, the dedupe fingerprint, and the
/// required-CI blocker precedence.
/// </summary>
public sealed class WorkspaceGuardSubmoduleEditClassifierTests
{
    [Fact]
    public void Classify_RedundantWithPrHeadMatch_IsRedundant_WithSafeRepair()
    {
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submoduleRoot: "submodules/X",
            dirtyPath: ".github/workflows/ci.yml",
            localDiffFingerprint: "abc123",
            prHeadFingerprint: "abc123",
            hasUniqueLocalContent: false,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit, decision.Classification);
        Assert.True(decision.SafeRepairAvailable);
        Assert.NotNull(decision.RecommendedRepair);
    }

    [Fact]
    public void Classify_RedundantNestedFilePath_RepairTargetsSubmoduleRootAndRelativeFile()
    {
        // G384 review follow-up: -C must point at the submodule WORKTREE ROOT,
        // and the pathspec must be the single relative file (never `.` / the
        // whole submodule). A full dirty path is accepted and reduced to the
        // relative form.
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submoduleRoot: "submodules/X",
            dirtyPath: "submodules/X/.github/workflows/ci.yml",
            localDiffFingerprint: "abc123",
            prHeadFingerprint: "abc123",
            hasUniqueLocalContent: false,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit, decision.Classification);
        Assert.NotNull(decision.RecommendedRepair);
        // -C targets the submodule root; pathspec is the single relative file.
        Assert.Contains("git -C submodules/X checkout -- .github/workflows/ci.yml", decision.RecommendedRepair!, StringComparison.Ordinal);
        // The broken form (-C pointing at the file) must NOT appear.
        Assert.DoesNotContain("git -C submodules/X/.github", decision.RecommendedRepair!, StringComparison.Ordinal);
        // Never the whole-submodule pathspec.
        Assert.DoesNotContain("checkout -- .\n", decision.RecommendedRepair!, StringComparison.Ordinal);
        Assert.DoesNotContain("checkout -- . ", decision.RecommendedRepair!, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeToRoot_StripsRootPrefix_OrLeavesRelativeUnchanged()
    {
        Assert.Equal(
            ".github/workflows/ci.yml",
            WorkspaceGuardSubmoduleEditClassifier.RelativeToRoot("submodules/X", "submodules/X/.github/workflows/ci.yml"));
        Assert.Equal(
            ".github/workflows/ci.yml",
            WorkspaceGuardSubmoduleEditClassifier.RelativeToRoot("submodules/X", ".github/workflows/ci.yml"));
    }

    [Fact]
    public void Classify_UniqueLocalContent_IsProtected_NoAutoRepair()
    {
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submoduleRoot: "submodules/X",
            dirtyPath: "f.cs",
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
            submoduleRoot: "submodules/X",
            dirtyPath: "f.cs",
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
        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            submoduleRoot: "submodules/X",
            dirtyPath: "f.cs",
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
            submoduleRoot: "submodules/X",
            dirtyPath: "f.cs",
            localDiffFingerprint: "abc",
            prHeadFingerprint: "abc",
            hasUniqueLocalContent: false,
            selectedPr: 1090);

        Assert.Equal(WorkspaceGuardSubmoduleEditClassifier.Classifications.NotInSubmoduleLane, decision.Classification);
    }

    [Fact]
    public void IsDuplicateReport_SameFingerprint_IsTrue_DifferentIsFalse()
    {
        var a = WorkspaceGuardSubmoduleEditClassifier.Classify(true, "submodules/X", "f", "abc", "abc", false, 1090);
        var b = WorkspaceGuardSubmoduleEditClassifier.Classify(true, "submodules/X", "f", "abc", "abc", false, 1090);
        var c = WorkspaceGuardSubmoduleEditClassifier.Classify(true, "submodules/X", "f", "xyz", "xyz", false, 1090);

        Assert.True(WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(a.DedupeFingerprint, b.DedupeFingerprint));
        Assert.False(WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(a.DedupeFingerprint, c.DedupeFingerprint));
        Assert.False(WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(null, a.DedupeFingerprint));
    }

    [Fact]
    public void ResolvePrimaryBlocker_FailingCiVisibleEvenWhenRedundantSafe()
    {
        Assert.Equal(
            WorkspaceGuardSubmoduleEditClassifier.Blockers.RequiredCiFailing,
            WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(
                requiredCiFailing: true,
                WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit));

        Assert.Equal(
            WorkspaceGuardSubmoduleEditClassifier.Blockers.ProtectedOperatorWork,
            WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(
                requiredCiFailing: true,
                WorkspaceGuardSubmoduleEditClassifier.Classifications.ProtectedOperatorWork));

        Assert.Equal(
            WorkspaceGuardSubmoduleEditClassifier.Blockers.None,
            WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(
                requiredCiFailing: false,
                WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit));
    }
}
