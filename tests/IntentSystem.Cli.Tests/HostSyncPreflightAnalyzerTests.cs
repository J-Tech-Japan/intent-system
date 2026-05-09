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
    public void Analyze_DirtyUnrelatedSubmodulePointer_ReturnsSeparateClassification()
    {
        var result = HostSyncPreflightAnalyzer.Analyze(
            "main",
            behindOriginCommits: 0,
            workingTreeEntries: new[] { Entry("submodules/some-other-repo", " m") });

        Assert.Equal("dirty-unrelated-submodule", result.Classification);
        Assert.False(result.ProceedAllowed);
        Assert.Empty(result.DirtyDurableStatePaths);
        Assert.Single(result.DirtyUnrelatedPaths);
        Assert.Contains(result.NextSteps, s => s.Contains("Surface the dirty unrelated paths", StringComparison.Ordinal));
        Assert.Contains(result.NextSteps, s => s.Contains("Do NOT silently stash", StringComparison.Ordinal));
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

    private static HostSyncWorkingTreeEntry Entry(string path, string status) =>
        new() { Path = path, Status = status };
}
