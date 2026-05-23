namespace IntentSystem.Cli.Commands;

/// <summary>
/// G388: pure resolver for the effective implementation / PR base branch that
/// <c>intent-cli guide</c> emits, so the guide is the deterministic source of
/// the fixed branch condition instead of an AI agent hand-reading local files.
///
/// The reported bug: a domain configured with
/// <c>implementation_base_branch = develop-v2</c> still saw <c>guide</c> emit
/// the <c>direct-main/main</c> default, because the guide consulted only the
/// <c>base_branch_policy</c> name and not the configured branch (which
/// <c>automation summary</c> already honors via G350/G362). This resolver
/// centralizes the precedence so every guide surface agrees with
/// <c>automation summary</c>:
///
/// <list type="number">
/// <item><description>explicit CLI / setup argument (<c>--implementation-base</c>)</description></item>
/// <item><description>domain config / automation bindings (<c>implementation_base_branch</c>)</description></item>
/// <item><description>same-repo topology mapping when applicable</description></item>
/// <item><description>default from the base-branch policy (<c>direct-main</c> → <c>main</c>)</description></item>
/// </list>
///
/// When an explicit argument and a configured branch disagree the resolver
/// returns a structured conflict (so the guide can refuse rather than silently
/// pick one) unless an explicit override is allowed. When nothing is
/// configured it returns the policy default flagged as a default, with a note
/// telling the operator where to configure a different branch.
///
/// No I/O — the command layer supplies the candidate strings (read from config
/// / bindings / args), so every branch is unit-testable.
/// </summary>
internal static class ImplementationBaseBranchResolver
{
    /// <summary>Where the resolved branch came from (the winning precedence tier).</summary>
    public static class Sources
    {
        public const string Explicit = "explicit-argument";
        public const string DomainConfig = "domain-config";
        public const string SameRepoTopology = "same-repo-topology";
        public const string Default = "policy-default";
        public const string Conflict = "conflict";
    }

    /// <summary>
    /// Resolve the effective implementation / PR base branch.
    /// </summary>
    /// <param name="explicitBranch">Operator-supplied branch (e.g. <c>--implementation-base develop-v2</c>); highest precedence.</param>
    /// <param name="configuredBranch">Branch from domain config / bindings (<c>implementation_base_branch</c>).</param>
    /// <param name="sameRepoTopologyBranch">Branch implied by same-repo topology mapping, when applicable.</param>
    /// <param name="policyDefaultBranch">The base-branch-policy default (e.g. <c>main</c> for <c>direct-main</c>); the final fallback.</param>
    /// <param name="allowOverride">When true an explicit branch wins over a differing configured branch instead of producing a conflict.</param>
    public static ImplementationBaseBranchDecision Resolve(
        string? explicitBranch,
        string? configuredBranch,
        string? sameRepoTopologyBranch,
        string policyDefaultBranch,
        bool allowOverride = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyDefaultBranch);

        var explicitTrimmed = Normalize(explicitBranch);
        var configTrimmed = Normalize(configuredBranch);
        var topologyTrimmed = Normalize(sameRepoTopologyBranch);
        var defaultTrimmed = Normalize(policyDefaultBranch);

        // Conflict: an explicit branch disagrees with the configured branch and
        // no override was requested. Refuse with a structured diagnostic rather
        // than silently choosing one.
        if (explicitTrimmed.Length > 0
            && configTrimmed.Length > 0
            && !string.Equals(explicitTrimmed, configTrimmed, StringComparison.Ordinal)
            && !allowOverride)
        {
            return new ImplementationBaseBranchDecision
            {
                Resolved = false,
                Branch = string.Empty,
                Source = Sources.Conflict,
                IsDefault = false,
                HasConflict = true,
                Diagnostic =
                    $"implementation base branch conflict: explicit '--implementation-base {explicitTrimmed}' "
                    + $"disagrees with the configured 'implementation_base_branch = {configTrimmed}'. "
                    + "Re-run with the matching branch, fix the domain config, or pass the override flag "
                    + "to intentionally use the explicit branch.",
                Note = string.Empty,
            };
        }

        if (explicitTrimmed.Length > 0)
        {
            return Decision(explicitTrimmed, Sources.Explicit, isDefault: false);
        }

        if (configTrimmed.Length > 0)
        {
            return Decision(configTrimmed, Sources.DomainConfig, isDefault: false);
        }

        if (topologyTrimmed.Length > 0)
        {
            return Decision(topologyTrimmed, Sources.SameRepoTopology, isDefault: false);
        }

        return new ImplementationBaseBranchDecision
        {
            Resolved = true,
            Branch = defaultTrimmed,
            Source = Sources.Default,
            IsDefault = true,
            HasConflict = false,
            Diagnostic = string.Empty,
            Note =
                $"using policy default base branch '{defaultTrimmed}'. "
                + "To target a different branch, set 'implementation_base_branch' in the domain config / "
                + "automation bindings, or pass '--implementation-base <branch>'.",
        };
    }

    private static ImplementationBaseBranchDecision Decision(string branch, string source, bool isDefault)
        => new()
        {
            Resolved = true,
            Branch = branch,
            Source = source,
            IsDefault = isDefault,
            HasConflict = false,
            Diagnostic = string.Empty,
            Note = string.Empty,
        };

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();
}

/// <summary>G388: the verdict from <see cref="ImplementationBaseBranchResolver.Resolve"/>.</summary>
internal sealed record ImplementationBaseBranchDecision
{
    /// <summary>True when a branch was resolved; false only on an unresolved conflict.</summary>
    public required bool Resolved { get; init; }

    /// <summary>The effective implementation / PR base branch (empty on conflict).</summary>
    public required string Branch { get; init; }

    /// <summary>Which precedence tier won (see <see cref="ImplementationBaseBranchResolver.Sources"/>).</summary>
    public required string Source { get; init; }

    /// <summary>True when the branch is the policy default rather than an explicit/configured value.</summary>
    public required bool IsDefault { get; init; }

    /// <summary>True when an explicit branch conflicted with a configured branch and no override was allowed.</summary>
    public required bool HasConflict { get; init; }

    /// <summary>Operator-facing conflict diagnostic (set only when <see cref="HasConflict"/>).</summary>
    public required string Diagnostic { get; init; }

    /// <summary>Operator-facing note explaining a defaulted branch and where to configure it (set only when <see cref="IsDefault"/>).</summary>
    public required string Note { get; init; }
}
