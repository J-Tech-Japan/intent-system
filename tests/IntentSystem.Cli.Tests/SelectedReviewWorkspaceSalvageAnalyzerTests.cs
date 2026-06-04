using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G453: pure tests for the salvage classifier that prevents review-host
/// cleanup from destroying unpublished host metadata. The central regression
/// fixture is the AIC PR #3746 failure mode: a selected review PR blocked by a
/// dirty-mixed host worktree that contains unpublished queue-state, runs.jsonl,
/// packet directories, and intent-tree files — where a `git reset`/`clean`
/// would delete the metadata that `closeout-plan` / `guide review` need and
/// produce a later `host-metadata-blocked` review.
/// </summary>
public sealed class SelectedReviewWorkspaceSalvageAnalyzerTests
{
    // The AIC #3746 dirty set, reproduced as a documented regression fixture.
    private static readonly string[] Aic3746DurablePaths =
    {
        ".intent-cli/queue-state.json",
        ".intent-cli/runs.jsonl",
        ".intent-cli/issues/AIC-G34",
        ".intent-cli/issues/AIC-G38",
        "intents/aic/features/login.md",
        "intents/aic/intent-tree/00-map.md",
    };

    private static readonly string[] Aic3746UnrelatedPaths =
    {
        "package-lock.json",
        "pnpm-lock.yaml",
    };

    [Fact]
    public void Aic3746Fixture_DirtyUnpublishedMetadata_ClassifiesBlockedAndWarnsAgainstCleanup()
    {
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            Repo = "J-Tech-Japan/intent-system",
            Domain = "aic",
            DirtyDurablePaths = Aic3746DurablePaths,
            DirtyUnrelatedPaths = Aic3746UnrelatedPaths,
            CiStatus = "passing",
        });

        // AC1: a SPECIFIC classification, not a generic dirty stop.
        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.SelectedReviewBlockedByUnpublishedHostMetadata,
            decision.Classification);

        // AC2: explicitly warns that reset/clean may remove needed metadata.
        Assert.True(decision.DestructiveCleanupWarning);
        Assert.Contains(decision.RecommendedSalvageActions,
            a => a.Contains("git reset", StringComparison.OrdinalIgnoreCase)
                && a.Contains("do NOT", StringComparison.OrdinalIgnoreCase));

        // AC3: salvage-first — commit/push, recovery commands appear BEFORE any
        // cleanup, and the entry point is durable-state-preflight.
        Assert.True(decision.SalvageFirst);
        Assert.Equal(
            "intent-cli automation durable-state-preflight --domain aic --target-repo J-Tech-Japan/intent-system --format json",
            decision.RecommendedNextCommand);
        Assert.Contains(decision.RecommendedSalvageActions, a => a.Contains("durable-state-preflight", StringComparison.Ordinal));
        Assert.Contains(decision.RecommendedSalvageActions, a => a.Contains("publish-recovery", StringComparison.Ordinal));
        Assert.Contains(decision.RecommendedSalvageActions, a => a.Contains("clean review worktree", StringComparison.OrdinalIgnoreCase));

        // The surfaced facts separate durable metadata from unrelated residue.
        Assert.Equal(Aic3746DurablePaths.Length, decision.UnpublishedMetadataPaths.Count);
        Assert.Equal(Aic3746UnrelatedPaths.Length, decision.UnrelatedPaths.Count);

        // Out of scope: never a blind auto-commit of operator-owned intents.
        Assert.Contains(decision.RecommendedSalvageActions,
            a => a.Contains("intents/**", StringComparison.Ordinal) && a.Contains("never", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WrongReviewWorktreeBranch_SameRepoTopology_OnImplementationBranch_IsDetected()
    {
        // AC4: same-repo topology configured + review running from an
        // implementation feature branch (not the metadata branch).
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            Repo = "J-Tech-Japan/intent-system",
            SameRepoTopology = true,
            MetadataSourceBranch = "main-metadata",
            CurrentBranch = "g34-implementation",
            DirtyDurablePaths = Aic3746DurablePaths,
            CiStatus = "passing",
        });

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.WrongReviewWorktreeBranch,
            decision.Classification);
        Assert.True(decision.SalvageFirst);
        // The fix is to switch to a clean review worktree on the metadata branch.
        Assert.Contains(decision.RecommendedSalvageActions,
            a => a.Contains("main-metadata", StringComparison.Ordinal)
                && a.Contains("clean review worktree", StringComparison.OrdinalIgnoreCase));
        // Even on the wrong branch, the unpublished metadata must not be cleaned.
        Assert.True(decision.DestructiveCleanupWarning);
    }

    [Fact]
    public void SameRepoTopology_OnMetadataBranch_IsNotWrongBranch()
    {
        // On the metadata branch with no dirty metadata, it is review-ready.
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            SameRepoTopology = true,
            MetadataSourceBranch = "main-metadata",
            CurrentBranch = "main-metadata",
            CiStatus = "passing",
        });

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.WorkspaceCleanReviewReady,
            decision.Classification);
        Assert.False(decision.DestructiveCleanupWarning);
    }

    [Fact]
    public void CiFailing_WithCleanWorkspace_IsReportedSeparatelyFromWorkspaceDirty()
    {
        // AC5: CI failing is its own classification, not a workspace problem.
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            CiStatus = "failing",
        });

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.RequiredCiFailing,
            decision.Classification);
        Assert.False(decision.DestructiveCleanupWarning);
        Assert.False(decision.SalvageFirst);
        Assert.True(decision.CiFailing);
    }

    [Fact]
    public void CiFailing_AndDirtyMetadata_MetadataBlockerTakesClassification_ButCiStaysVisible()
    {
        // When BOTH conditions hold, the cleanup-safety classification wins (so
        // metadata is not destroyed), but the CI failure must remain visible.
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            DirtyDurablePaths = new[] { ".intent-cli/queue-state.json" },
            CiStatus = "failing",
        });

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.SelectedReviewBlockedByUnpublishedHostMetadata,
            decision.Classification);
        Assert.True(decision.CiFailing); // not lost
        Assert.True(decision.DestructiveCleanupWarning);
    }

    [Fact]
    public void CleanWorkspace_PassingCi_IsReviewReady()
    {
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            CiStatus = "passing",
        });

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.WorkspaceCleanReviewReady,
            decision.Classification);
        Assert.False(decision.DestructiveCleanupWarning);
        Assert.False(decision.SalvageFirst);
        Assert.Empty(decision.RecommendedSalvageActions);
    }

    [Fact]
    public void NonSameRepo_DirtyMetadata_StillClassifiesBlocked_NoWrongBranchFalsePositive()
    {
        // Without same-repo topology, a non-metadata current branch must NOT
        // trigger wrong-branch — wrong-branch is same-repo-only.
        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = 3746,
            SameRepoTopology = false,
            CurrentBranch = "some-branch",
            DirtyDurablePaths = new[] { ".intent-cli/runs.jsonl" },
            CiStatus = "passing",
        });

        Assert.Equal(
            AutomationHostReviewDiagnosticsClassifications.SelectedReviewBlockedByUnpublishedHostMetadata,
            decision.Classification);
    }
}
