namespace IntentSystem.Cli.Commands;

/// <summary>
/// G294: canonical mapping between a host's <c>base_branch_policy</c> configuration
/// and the resulting expected GitHub PR base branch. Two modes are supported:
///
/// <list type="bullet">
/// <item>
/// <description><c>direct-main</c> (default): every child PR targets <c>main</c>; the
/// host merges directly to <c>main</c>.</description>
/// </item>
/// <item>
/// <description><c>main-ai</c>: every child PR targets <c>main-ai</c>; the operator
/// periodically opens a human-reviewed <c>main-ai → main</c> batch PR.</description>
/// </item>
/// </list>
///
/// The contract is consumed by:
/// <list type="bullet">
/// <item><description><c>intent-cli guide prompt-matrix</c> (child-loop and host-loop), which
/// surfaces the expected base branch to the AI agent so it cannot rely on hidden
/// human memory.</description></item>
/// <item><description><c>intent-cli automation base-branch-check</c>, which compares the
/// observed PR base branch against the configured policy and surfaces a structured
/// mismatch result.</description></item>
/// </list>
/// </summary>
internal static class BaseBranchPolicyContract
{
    public static bool IsKnownPolicy(string policy)
    {
        return string.Equals(policy, CliRuntimeContracts.DirectMainBaseBranchPolicy, StringComparison.Ordinal)
            || string.Equals(policy, CliRuntimeContracts.MainAiBaseBranchPolicy, StringComparison.Ordinal);
    }

    public static string ResolveExpectedBaseBranch(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);

        return policy switch
        {
            CliRuntimeContracts.DirectMainBaseBranchPolicy => CliRuntimeContracts.DirectMainBaseBranch,
            CliRuntimeContracts.MainAiBaseBranchPolicy => CliRuntimeContracts.MainAiIntegrationBaseBranch,
            _ => throw new InvalidOperationException(
                $"Unknown base branch policy '{policy}'. Expected '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}'.")
        };
    }

    public static string DescribePolicy(string policy)
        => DescribeEffectiveBranch(policy, ResolveExpectedBaseBranch(policy));

    /// <summary>
    /// G471: branch-aware policy description. When the effective implementation /
    /// PR base branch is the policy default (e.g. <c>main</c> for
    /// <c>direct-main</c>), this returns the canonical policy prose unchanged.
    /// When a configured / explicit / topology override sets a non-default
    /// branch (e.g. <c>develop-v2</c>), the prose names that branch consistently
    /// instead of hard-coding <c>main</c>, so the guide never tells an
    /// implementation / review loop to target <c>main</c> while the effective
    /// branch is something else.
    /// </summary>
    public static string DescribeEffectiveBranch(string policy, string effectiveBranch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveBranch);

        var branch = effectiveBranch.Trim();
        return policy switch
        {
            CliRuntimeContracts.DirectMainBaseBranchPolicy =>
                $"Child PRs target `{branch}` directly; host merges land on `{branch}`.",
            CliRuntimeContracts.MainAiBaseBranchPolicy =>
                $"Child PRs target `{branch}` (the AI integration branch); the human operator periodically opens a `{branch} → main` batch PR. Never open a child PR against `main` directly under this policy.",
            _ => throw new InvalidOperationException(
                $"Unknown base branch policy '{policy}'.")
        };
    }
}
