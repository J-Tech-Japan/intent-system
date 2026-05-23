using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G388: tests for the pure implementation/PR base-branch precedence resolver.
/// Covers the four required cases — a <c>develop-v2</c> domain config, the
/// direct-main default, an explicit override, and an explicit/config conflict —
/// plus the same-repo topology tier and the defaulted-branch note.
/// </summary>
public sealed class ImplementationBaseBranchResolverTests
{
    [Fact]
    public void Resolve_DomainConfigDevelopV2_WinsOverPolicyDefault()
    {
        var decision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: null,
            configuredBranch: "develop-v2",
            sameRepoTopologyBranch: null,
            policyDefaultBranch: "main");

        Assert.True(decision.Resolved);
        Assert.Equal("develop-v2", decision.Branch);
        Assert.Equal(ImplementationBaseBranchResolver.Sources.DomainConfig, decision.Source);
        Assert.False(decision.IsDefault);
        Assert.False(decision.HasConflict);
    }

    [Fact]
    public void Resolve_NoConfig_DefaultsToPolicyBranch_WithNote()
    {
        var decision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: null,
            configuredBranch: null,
            sameRepoTopologyBranch: null,
            policyDefaultBranch: "main");

        Assert.True(decision.Resolved);
        Assert.Equal("main", decision.Branch);
        Assert.Equal(ImplementationBaseBranchResolver.Sources.Default, decision.Source);
        Assert.True(decision.IsDefault);
        Assert.False(decision.HasConflict);
        // Must state it is a default and where to configure a different branch.
        Assert.Contains("default", decision.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("implementation_base_branch", decision.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ExplicitArgument_WinsOverConfig_WhenOverrideAllowed()
    {
        var decision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: "develop-v2",
            configuredBranch: "main",
            sameRepoTopologyBranch: null,
            policyDefaultBranch: "main",
            allowOverride: true);

        Assert.True(decision.Resolved);
        Assert.Equal("develop-v2", decision.Branch);
        Assert.Equal(ImplementationBaseBranchResolver.Sources.Explicit, decision.Source);
        Assert.False(decision.HasConflict);
    }

    [Fact]
    public void Resolve_ExplicitConflictsWithConfig_NoOverride_ReturnsStructuredConflict()
    {
        var decision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: "main",
            configuredBranch: "develop-v2",
            sameRepoTopologyBranch: null,
            policyDefaultBranch: "main",
            allowOverride: false);

        Assert.False(decision.Resolved);
        Assert.True(decision.HasConflict);
        Assert.Equal(ImplementationBaseBranchResolver.Sources.Conflict, decision.Source);
        Assert.Equal(string.Empty, decision.Branch);
        Assert.Contains("conflict", decision.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("develop-v2", decision.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ExplicitMatchesConfig_NoConflict()
    {
        var decision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: "develop-v2",
            configuredBranch: "develop-v2",
            sameRepoTopologyBranch: null,
            policyDefaultBranch: "main");

        Assert.True(decision.Resolved);
        Assert.False(decision.HasConflict);
        Assert.Equal("develop-v2", decision.Branch);
        Assert.Equal(ImplementationBaseBranchResolver.Sources.Explicit, decision.Source);
    }

    [Fact]
    public void Resolve_SameRepoTopology_UsedWhenNoExplicitOrConfig()
    {
        var decision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: null,
            configuredBranch: null,
            sameRepoTopologyBranch: "main-metadata",
            policyDefaultBranch: "main");

        Assert.True(decision.Resolved);
        Assert.Equal("main-metadata", decision.Branch);
        Assert.Equal(ImplementationBaseBranchResolver.Sources.SameRepoTopology, decision.Source);
        Assert.False(decision.IsDefault);
    }

    [Fact]
    public void Resolve_BlankPolicyDefault_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ImplementationBaseBranchResolver.Resolve(null, null, null, "  "));
    }
}
