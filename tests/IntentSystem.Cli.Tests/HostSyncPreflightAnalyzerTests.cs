using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class HostSyncPreflightAnalyzerTests
{
    [Fact]
    public void Analyze_CleanWorkingTree_AndUpToDateBranch_ReturnsClean()
    {
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: Array.Empty<HostSyncWorkingTreeEntry>());

        Assert.Equal("clean", result.Classification);
        Assert.True(result.ProceedAllowed);
        Assert.Empty(result.NextSteps);
        Assert.Contains("Pre-wake sync boundary satisfied", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_BehindOrigin_ReturnsBehindClassification_WithFastForwardSteps()
    {
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 3,
            workingTreeEntries: Array.Empty<HostSyncWorkingTreeEntry>());

        Assert.Equal("behind-origin", result.Classification);
        Assert.False(result.ProceedAllowed);
        Assert.Contains("behind origin by 3 commit(s)", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.NextSteps, s => s.Contains("git pull --ff-only", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(".intent-cli/queue-state.json")]
    [InlineData(".intent-cli/runs.jsonl")]
    [InlineData(".intent-cli/issues/G300/publish.yaml")]
    [InlineData("intents/intent-cli/clarifications/open.md")]
    public void Analyze_DirtyHostDurableStatePath_RefusesToProceed(string path)
    {
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[] { Entry(path, " M") });

        Assert.Equal("dirty-host-durable-state", result.Classification);
        Assert.False(result.ProceedAllowed);
        Assert.Single(result.DirtyDurableStatePaths);
        Assert.Empty(result.DirtyUnrelatedPaths);
        Assert.Contains(result.NextSteps, s => s.Contains("commit and push", StringComparison.Ordinal));
        Assert.Contains(result.NextSteps, s => s.Contains("revert them", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DirtyUnrelatedSubmodulePointer_AllowsSafeStashLane_G306()
    {
        // G306: with ONLY unrelated dirty paths (no durable-state dirt),
        // the wake proceeds via the safe-stash lane instead of hard
        // aborting. proceed_allowed flips to true and SafeStashPaths is
        // populated with the same dirty entries.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[] { Entry("submodules/some-other-repo", " m") });

        Assert.Equal("dirty-unrelated-submodule", result.Classification);
        Assert.True(result.ProceedAllowed);
        Assert.True(result.SafeStashRequired);
        Assert.Single(result.SafeStashPaths);
        Assert.Empty(result.DirtyDurableStatePaths);
        Assert.Single(result.DirtyUnrelatedPaths);
        Assert.Contains(result.NextSteps, s => s.Contains("automation workspace-guard --mode begin --write", StringComparison.Ordinal));
        Assert.Contains(result.NextSteps, s => s.Contains("--mode end --write", StringComparison.Ordinal));
        // G352: updated wording covers both stash-pop and gitlink restore conflicts.
        Assert.Contains(result.NextSteps, s => s.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_DirtyMixed_ListsBothBuckets()
    {
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[]
            {
                Entry(".intent-cli/queue-state.json", " M"),
                Entry("submodules/some-repo", " m")
            });

        Assert.Equal("dirty-mixed", result.Classification);
        Assert.False(result.ProceedAllowed);
        // G306: dirty-mixed is NOT eligible for the safe-stash lane —
        // automation cannot safely stash files in the same wake that
        // might mutate them. SafeStashRequired stays false; the operator
        // must reconcile the durable bucket through the G304 fail-closed
        // path before re-running.
        Assert.False(result.SafeStashRequired);
        Assert.Single(result.DirtyDurableStatePaths);
        Assert.Single(result.DirtyUnrelatedPaths);
        Assert.Contains("durable-state path(s) AND", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.NextSteps, s => s.Contains("`submodules/some-repo`", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_DirtyAndBehind_DirtyDominatesClassification()
    {
        // Dirty durable-state is the more urgent failure mode; the
        // analyzer must prioritise it over behind-origin so the host loop
        // does not pull and overwrite local durable-state changes.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 5,
            workingTreeEntries: new[] { Entry(".intent-cli/queue-state.json", " M") });

        Assert.Equal("dirty-host-durable-state", result.Classification);
        Assert.Equal(5, result.BehindOriginCommits);
        Assert.False(result.ProceedAllowed);
    }

    [Fact]
    public void Analyze_RejectsNegativeBehindCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HostSyncPreflightAnalyzer.Analyze("main", -1, Array.Empty<HostSyncWorkingTreeEntry>()));
    }

    // G352: next-steps guidance alignment with updated workspace-guard capabilities.

    [Fact]
    public void Analyze_G352_DirtyUnrelatedSubmodule_NextSteps_MentionCheckoutLaneAndManualFallback()
    {
        // host-sync-preflight should surface guidance that workspace-guard now
        // handles gitlink-only submodule paths via the checkout lane (G352),
        // and that a proceed_allowed:false result means manual intervention is needed.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[] { Entry("submodules/SekibanAsAService", " m") });

        Assert.Equal("dirty-unrelated-submodule", result.Classification);
        Assert.True(result.ProceedAllowed);
        // Guidance must mention checkout lane (G352) and the stash lane.
        Assert.Contains(result.NextSteps, s => s.Contains("checkout lane", StringComparison.Ordinal));
        // Guidance must mention the fallback for submodule internal changes.
        Assert.Contains(result.NextSteps, s => s.Contains("proceed_allowed: false", StringComparison.Ordinal));
        Assert.Contains(result.NextSteps, s => s.Contains("manual recovery", StringComparison.OrdinalIgnoreCase));
    }

    // ── G357 submodule-checkout-mismatch ────────────────────────────────

    [Fact]
    public void Analyze_G357_AllDirtyPathsAreGitlinkMismatches_ReturnsSubmoduleCheckoutMismatch()
    {
        // G357: when ALL dirty-other paths are known gitlink mismatches
        // (parent gitlink advanced, submodule itself is clean), classify as
        // submodule-checkout-mismatch and recommend git submodule update.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[] { Entry("child-repo", " M") },
            submoduleGitlinkMismatchPaths: new[] { "child-repo" });

        Assert.Equal("submodule-checkout-mismatch", result.Classification);
        // Must NOT be proceed-allowed (requires git submodule update first).
        Assert.False(result.ProceedAllowed);
        // Must NOT activate the safe-stash lane — this uses the update lane.
        Assert.False(result.SafeStashRequired);
        Assert.Empty(result.SafeStashPaths);
        // The mismatch path is surfaced in SubmoduleCheckoutMismatchPaths.
        Assert.Single(result.SubmoduleCheckoutMismatchPaths);
        Assert.Equal("child-repo", result.SubmoduleCheckoutMismatchPaths[0]);
        // Summary must identify the gitlink mismatch and the repair command.
        Assert.Contains("gitlink mismatch", result.Summary, StringComparison.Ordinal);
        Assert.Contains("git submodule update --init", result.Summary, StringComparison.Ordinal);
        Assert.Contains("child-repo", result.Summary, StringComparison.Ordinal);
        // Next steps must describe the update command and follow-up preflight.
        Assert.Contains(result.NextSteps, s => s.Contains("git submodule update --init child-repo", StringComparison.Ordinal));
        Assert.Contains(result.NextSteps, s => s.Contains("host-sync-preflight", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_G357_MultipleGitlinkMismatchPaths_AllListedInNextSteps()
    {
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[]
            {
                Entry("sub/repo-a", " M"),
                Entry("sub/repo-b", " M")
            },
            submoduleGitlinkMismatchPaths: new[] { "sub/repo-a", "sub/repo-b" });

        Assert.Equal("submodule-checkout-mismatch", result.Classification);
        Assert.False(result.ProceedAllowed);
        Assert.Equal(2, result.SubmoduleCheckoutMismatchPaths.Count);
        // Both paths appear in the update command in next steps.
        Assert.Contains(result.NextSteps, s =>
            s.Contains("sub/repo-a", StringComparison.Ordinal) &&
            s.Contains("sub/repo-b", StringComparison.Ordinal));
    }

    [Fact]
    public void Analyze_G357_PartialGitlinkMismatch_RemainsUnrelatedSubmodule()
    {
        // If only SOME dirty-other paths are gitlink mismatches, the remaining
        // unrelated changes must go through the safe-stash lane (G306), not the
        // update lane — so we keep the dirty-unrelated-submodule classification.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[]
            {
                Entry("child-repo", " M"),
                Entry("some-other-file.txt", "??")
            },
            submoduleGitlinkMismatchPaths: new[] { "child-repo" });

        Assert.Equal("dirty-unrelated-submodule", result.Classification);
        Assert.True(result.ProceedAllowed);
        Assert.True(result.SafeStashRequired);
        Assert.Empty(result.SubmoduleCheckoutMismatchPaths);
    }

    [Fact]
    public void Analyze_G357_GitlinkMismatchWithDurableDirt_ReturnsDirtyMixed()
    {
        // Durable-state dirt takes precedence: dirty-mixed is returned regardless
        // of whether the other dirty paths are gitlink mismatches.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[]
            {
                Entry(".intent-cli/queue-state.json", " M"),
                Entry("child-repo", " M")
            },
            submoduleGitlinkMismatchPaths: new[] { "child-repo" });

        Assert.Equal("dirty-mixed", result.Classification);
        Assert.False(result.ProceedAllowed);
        Assert.False(result.SafeStashRequired);
        Assert.Empty(result.SubmoduleCheckoutMismatchPaths);
    }

    [Fact]
    public void Analyze_G357_CleanRepo_NullSubmodulePaths_ReturnsClean()
    {
        // Passing null for submoduleGitlinkMismatchPaths (default) does not affect
        // the clean case.
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: Array.Empty<HostSyncWorkingTreeEntry>(),
            submoduleGitlinkMismatchPaths: null);

        Assert.Equal("clean", result.Classification);
        Assert.True(result.ProceedAllowed);
        Assert.Empty(result.SubmoduleCheckoutMismatchPaths);
    }

    private static HostSyncWorkingTreeEntry Entry(string path, string status) =>
        new() { Path = path, Status = status };
}
