using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G455: pure tests for the PR-body closing-reference fallback. The central
/// regression fixture is AIC PR #3750: a PR targeting the non-default base
/// `develop-v2`, green/non-draft/mergeable, containing `Closes #3749`, but with
/// an EMPTY GitHub `closingIssuesReferences` (GitHub does not populate it for
/// non-default-base PRs). The fallback must recover the single canonical body
/// reference deterministically while refusing to guess on ambiguity.
/// </summary>
public sealed class PrBodyClosingReferenceFallbackAnalyzerTests
{
    private const string Repo = "J-Tech-Japan/intent-system";

    [Fact]
    public void Aic3750Fixture_EmptyGitHubRefs_SingleBodyCloses_RecoversIssue()
    {
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: "Implements the develop-v2 work.\n\nCloses #3749\n",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationBodyFallback, decision.Classification);
        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.SourcePrBodyFallback, decision.Source);
        Assert.Equal(new[] { 3749 }, decision.ResolvedClosingIssues);
        Assert.False(decision.Ambiguous);
    }

    [Fact]
    public void GitHubRefsPresent_UsesGitHub_NoBodyFallback()
    {
        // When GitHub provides closing references, behavior is unchanged — the
        // body is not even consulted.
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: new[] { 100 },
            prBody: "Closes #999",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationGitHub, decision.Classification);
        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.SourceGitHub, decision.Source);
        Assert.Equal(new[] { 100 }, decision.ResolvedClosingIssues);
    }

    [Fact]
    public void MultipleDistinctBodyReferences_IsAmbiguous_RefusesToGuess()
    {
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: "Closes #10\nFixes #11\n",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationAmbiguousBody, decision.Classification);
        Assert.True(decision.Ambiguous);
        Assert.Empty(decision.ResolvedClosingIssues);
        Assert.Null(decision.Source);
    }

    [Fact]
    public void SameIssueRepeatedAcrossKeywords_IsNotAmbiguous_DistinctSingle()
    {
        // `Closes #42` and `Fixes #42` reference the same issue — one distinct
        // issue, so it is recoverable, not ambiguous.
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: "Closes #42\n... later ...\nFixes #42\n",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationBodyFallback, decision.Classification);
        Assert.Equal(new[] { 42 }, decision.ResolvedClosingIssues);
    }

    [Fact]
    public void LongProseReference_DoesNotCount_NoReference()
    {
        // Loose prose like `see #N` / `Linked Issue #N` is not a canonical
        // closing reference.
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: "Linked Issue #3749 — see #3749 for context.",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationNoReference, decision.Classification);
        Assert.Empty(decision.ResolvedClosingIssues);
        Assert.Null(decision.Source);
    }

    [Fact]
    public void CrossRepoReference_DoesNotCount_NoReference()
    {
        // A cross-repo `Closes other-org/other#42` does not close an issue in
        // THIS repo, so it is excluded.
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: "Closes other-org/other-repo#42",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationNoReference, decision.Classification);
        Assert.Empty(decision.ResolvedClosingIssues);
    }

    [Fact]
    public void SameRepoQualifiedReference_Counts()
    {
        // An explicit same-repo qualified reference `Closes owner/repo#N` is
        // valid for this repo.
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: $"Resolves {Repo}#3749",
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationBodyFallback, decision.Classification);
        Assert.Equal(new[] { 3749 }, decision.ResolvedClosingIssues);
    }

    [Fact]
    public void EmptyBody_NoReference()
    {
        var decision = PrBodyClosingReferenceFallbackAnalyzer.Resolve(
            githubClosingIssues: Array.Empty<int>(),
            prBody: null,
            repo: Repo);

        Assert.Equal(PrBodyClosingReferenceFallbackAnalyzer.ClassificationNoReference, decision.Classification);
    }
}
