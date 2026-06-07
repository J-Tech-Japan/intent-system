using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G471: tests for the branch-aware policy description. The default-branch
/// descriptions must stay byte-identical to the historical prose (so existing
/// direct-main / main-ai projects are unchanged), while a non-default effective
/// branch (e.g. <c>develop-v2</c>) must be named consistently instead of
/// hard-coding <c>main</c>.
/// </summary>
public sealed class BaseBranchPolicyContractTests
{
    [Fact]
    public void DescribePolicy_DirectMain_KeepsCanonicalMainProse()
    {
        Assert.Equal(
            "Child PRs target `main` directly; host merges land on `main`.",
            BaseBranchPolicyContract.DescribePolicy("direct-main"));
    }

    [Fact]
    public void DescribePolicy_MainAi_KeepsCanonicalProse()
    {
        Assert.Equal(
            "Child PRs target `main-ai` (the AI integration branch); the human operator periodically opens a `main-ai → main` batch PR. Never open a child PR against `main` directly under this policy.",
            BaseBranchPolicyContract.DescribePolicy("main-ai"));
    }

    [Fact]
    public void DescribeEffectiveBranch_DirectMainDevelopV2_NamesDevelopV2_AndNeverMain()
    {
        var description = BaseBranchPolicyContract.DescribeEffectiveBranch("direct-main", "develop-v2");

        Assert.Equal(
            "Child PRs target `develop-v2` directly; host merges land on `develop-v2`.",
            description);
        Assert.DoesNotContain("`main`", description, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeEffectiveBranch_DefaultBranch_EqualsDescribePolicy()
    {
        // When the effective branch is the policy default the branch-aware
        // overload returns the same prose as DescribePolicy (byte-stable).
        Assert.Equal(
            BaseBranchPolicyContract.DescribePolicy("direct-main"),
            BaseBranchPolicyContract.DescribeEffectiveBranch("direct-main", "main"));
        Assert.Equal(
            BaseBranchPolicyContract.DescribePolicy("main-ai"),
            BaseBranchPolicyContract.DescribeEffectiveBranch("main-ai", "main-ai"));
    }

    [Fact]
    public void DescribeEffectiveBranch_MainAiOverrideBranch_ParameterizesIntegrationBranch()
    {
        var description = BaseBranchPolicyContract.DescribeEffectiveBranch("main-ai", "integration-x");

        Assert.Contains("Child PRs target `integration-x`", description, StringComparison.Ordinal);
        Assert.Contains("`integration-x → main` batch PR", description, StringComparison.Ordinal);
    }
}
