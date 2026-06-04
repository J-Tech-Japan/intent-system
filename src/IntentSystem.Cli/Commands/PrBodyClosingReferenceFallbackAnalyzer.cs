namespace IntentSystem.Cli.Commands;

/// <summary>
/// G455: pure analyzer that resolves the effective closing-issue numbers for
/// linkage recovery, falling back to the PR body's canonical
/// <c>Closes</c>/<c>Fixes</c>/<c>Resolves</c> reference when GitHub's
/// <c>closingIssuesReferences</c> API is empty.
///
/// The failure mode (observed on AIC PR #3750): a PR targeting a non-default
/// base branch (<c>develop-v2</c>) was green, non-draft, mergeable, and
/// contained <c>Closes #3749</c> — but GitHub's <c>closingIssuesReferences</c>
/// came back EMPTY because the base is not the repository default branch. The
/// host loop then stopped with <c>host-metadata-blocked</c> (no <c>linked_pr</c>
/// for the queue item) even though the PR body carried deterministic evidence.
/// The only way out was the manual
/// <c>review closeout-plan --closing-issues 3749 --write-recovered-linkage</c>
/// flag — knowledge a routine wake should not need.
///
/// This analyzer makes the recovery automatic and deterministic:
/// <list type="bullet">
///   <item>GitHub closing references present → use them (no change in behavior).</item>
///   <item>GitHub empty + exactly ONE canonical same-repo body reference → use
///   it as the closing issue (the <c>pr-body-fallback</c> source).</item>
///   <item>GitHub empty + MULTIPLE distinct body references → ambiguous; refuse
///   to guess (G455 out-of-scope).</item>
///   <item>GitHub empty + no canonical reference (or only loose prose /
///   cross-repo references) → no recovery; fall through to the existing
///   host-metadata-blocked path.</item>
/// </list>
///
/// Pure: no I/O, no GitHub, no process launch. Cross-repo references and loose
/// prose are already excluded by
/// <see cref="PrClosingReferenceAnalyzer.ExtractCanonicalReferences"/>.
/// </summary>
internal static class PrBodyClosingReferenceFallbackAnalyzer
{
    public const string SourceGitHub = "github-closing-references";
    public const string SourcePrBodyFallback = "pr-body-fallback";

    public const string ClassificationGitHub = "github-closing-references";
    public const string ClassificationBodyFallback = "pr-body-fallback";
    public const string ClassificationAmbiguousBody = "ambiguous-body-references";
    public const string ClassificationNoReference = "no-closing-reference";

    public static PrBodyClosingReferenceFallbackDecision Resolve(
        IReadOnlyList<int>? githubClosingIssues,
        string? prBody,
        string repo)
    {
        ArgumentNullException.ThrowIfNull(repo);

        var github = (githubClosingIssues ?? Array.Empty<int>())
            .Where(n => n > 0)
            .Distinct()
            .ToArray();

        // GitHub closing references are authoritative when present — no
        // fallback needed. This preserves the existing (default-base) behavior
        // exactly.
        if (github.Length > 0)
        {
            return new PrBodyClosingReferenceFallbackDecision
            {
                Classification = ClassificationGitHub,
                Source = SourceGitHub,
                ResolvedClosingIssues = github,
                Ambiguous = false,
                Reason = $"GitHub closingIssuesReferences provided {github.Length} closing issue(s); PR-body fallback not needed.",
            };
        }

        // GitHub empty → derive from canonical same-repo body references.
        var bodyRefs = PrClosingReferenceAnalyzer.ExtractCanonicalReferences(prBody, repo);
        var distinctIssues = bodyRefs
            .Select(r => r.IssueNumber)
            .Distinct()
            .ToArray();

        if (distinctIssues.Length == 0)
        {
            return new PrBodyClosingReferenceFallbackDecision
            {
                Classification = ClassificationNoReference,
                Source = null,
                ResolvedClosingIssues = Array.Empty<int>(),
                Ambiguous = false,
                Reason = "GitHub closingIssuesReferences is empty and the PR body has no canonical Closes/Fixes/Resolves reference in this repo (loose prose like `see #N` / `Linked Issue #N` and cross-repo references do not count).",
            };
        }

        if (distinctIssues.Length > 1)
        {
            // Multiple distinct canonical references — refuse to guess.
            return new PrBodyClosingReferenceFallbackDecision
            {
                Classification = ClassificationAmbiguousBody,
                Source = null,
                ResolvedClosingIssues = Array.Empty<int>(),
                Ambiguous = true,
                Reason = $"GitHub closingIssuesReferences is empty and the PR body has {distinctIssues.Length} distinct canonical closing references ({string.Join(", ", distinctIssues.Select(n => "#" + n))}); refusing to guess which is the source issue.",
            };
        }

        return new PrBodyClosingReferenceFallbackDecision
        {
            Classification = ClassificationBodyFallback,
            Source = SourcePrBodyFallback,
            ResolvedClosingIssues = distinctIssues,
            Ambiguous = false,
            Reason = $"GitHub closingIssuesReferences is empty; recovered a single canonical body reference `Closes #{distinctIssues[0]}` in {repo} (typical of a non-default-base PR where GitHub does not populate closingIssuesReferences).",
        };
    }
}

/// <summary>
/// G455: resolution of the effective closing-issue set for linkage recovery,
/// plus the <see cref="Source"/> tag and an <see cref="Ambiguous"/> flag so the
/// caller can surface a structured ambiguity gap instead of guessing.
/// </summary>
internal sealed record PrBodyClosingReferenceFallbackDecision
{
    public required string Classification { get; init; }

    /// <summary>Provenance of the resolved issues; null when none resolved.</summary>
    public string? Source { get; init; }

    public required IReadOnlyList<int> ResolvedClosingIssues { get; init; }

    /// <summary>True when the body had multiple distinct canonical references (refuse-to-guess).</summary>
    public required bool Ambiguous { get; init; }

    public required string Reason { get; init; }
}
