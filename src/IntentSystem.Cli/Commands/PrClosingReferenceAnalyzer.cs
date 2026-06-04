using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G311: pure analyzer that classifies whether a PR body carries a
/// deterministic GitHub closing reference (<c>Closes #N</c> /
/// <c>Fixes #N</c> / <c>Resolves #N</c>) to a known source issue.
///
/// GitHub recognizes the following keywords (case-insensitive) as
/// closing references when followed by <c>#N</c> or
/// <c>OWNER/REPO#N</c>: <c>close</c>, <c>closes</c>, <c>closed</c>,
/// <c>fix</c>, <c>fixes</c>, <c>fixed</c>, <c>resolve</c>,
/// <c>resolves</c>, <c>resolved</c>. Bare links such as <c>#723</c>
/// or "see #723" do NOT close the issue and must not be classified
/// as closing references.
///
/// Pure data in / pure data out: no I/O, no <c>gh</c> calls. Inputs
/// are the PR body text and the source issue number; outputs are the
/// classification, the parsed reference list, and a remediation
/// snippet the agent can append to the PR body to fix the missing
/// or wrong reference.
///
/// Classifications:
/// <list type="bullet">
/// <item><c>valid</c> — exactly one closing reference whose number matches the source issue.</item>
/// <item><c>missing</c> — no closing references in the body.</item>
/// <item><c>wrong-issue</c> — closing references exist but none match the source issue.</item>
/// <item><c>multiple-issues</c> — multiple distinct closing references; ambiguous (refuse rather than guess).</item>
/// </list>
/// </summary>
internal static class PrClosingReferenceAnalyzer
{
    public const string ClassificationValid = "valid";
    public const string ClassificationMissing = "missing";
    public const string ClassificationWrongIssue = "wrong-issue";
    public const string ClassificationMultipleIssues = "multiple-issues";

    // Matches "<keyword> [optional owner/repo]#<number>" in any case.
    // Optional owner/repo prefix lets us detect cross-repo closing refs
    // (which are still valid on GitHub but only close the *targeted*
    // repo's issue — we record the prefix so callers can decide whether
    // to count it).
    private static readonly Regex ClosingReferenceRegex = new(
        @"\b(?<keyword>close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b\s*:?\s*(?<repo>[A-Za-z0-9._-]+/[A-Za-z0-9._-]+)?#(?<number>\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Classify the PR body against a known source issue. <paramref name="prBody"/>
    /// may be null/empty (treated as <c>missing</c>). <paramref name="repo"/> is
    /// the PR's repo (e.g. <c>J-Tech-Japan/intent-system</c>); cross-repo
    /// closing references that name a different repo are excluded.
    /// </summary>
    public static PrClosingReferenceResult Analyze(
        string? prBody,
        int sourceIssueNumber,
        string repo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        if (sourceIssueNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceIssueNumber),
                sourceIssueNumber,
                "source issue number must be a positive integer.");
        }

        var references = ExtractReferences(prBody, repo);
        var distinctNumbers = references
            .Select(r => r.IssueNumber)
            .Distinct()
            .ToArray();

        if (references.Count == 0)
        {
            return new PrClosingReferenceResult
            {
                Classification = ClassificationMissing,
                Ok = false,
                References = references,
                SourceIssueNumber = sourceIssueNumber,
                Repo = repo,
                Remediation = BuildMissingRemediation(sourceIssueNumber),
                Summary = $"PR body has no GitHub closing reference to source issue #{sourceIssueNumber}. Add `Closes #{sourceIssueNumber}` to the PR body before completion (G311).",
            };
        }

        if (distinctNumbers.Length > 1)
        {
            return new PrClosingReferenceResult
            {
                Classification = ClassificationMultipleIssues,
                Ok = false,
                References = references,
                SourceIssueNumber = sourceIssueNumber,
                Repo = repo,
                Remediation = BuildMultipleRemediation(sourceIssueNumber, distinctNumbers),
                Summary = $"PR body has multiple distinct closing references ({string.Join(", ", distinctNumbers.Select(n => "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)))}); refuse to guess which is the source issue (G311).",
            };
        }

        var single = distinctNumbers[0];
        if (single != sourceIssueNumber)
        {
            return new PrClosingReferenceResult
            {
                Classification = ClassificationWrongIssue,
                Ok = false,
                References = references,
                SourceIssueNumber = sourceIssueNumber,
                Repo = repo,
                Remediation = BuildWrongIssueRemediation(sourceIssueNumber, single),
                Summary = $"PR body closes #{single} but the source issue selected by `worker next-action` is #{sourceIssueNumber}; correct the closing reference before completion (G311).",
            };
        }

        return new PrClosingReferenceResult
        {
            Classification = ClassificationValid,
            Ok = true,
            References = references,
            SourceIssueNumber = sourceIssueNumber,
            Repo = repo,
            Remediation = Array.Empty<string>(),
            Summary = $"PR body has a deterministic closing reference to source issue #{sourceIssueNumber} (G311).",
        };
    }

    /// <summary>
    /// G455: public access to the canonical closing-reference parser so the
    /// linkage-recovery fallback (<see cref="PrBodyClosingReferenceFallbackAnalyzer"/>)
    /// can derive closing issues from a PR body when GitHub's
    /// <c>closingIssuesReferences</c> API is empty (which happens for
    /// non-default-base PRs). Returns only canonical references
    /// (Close/Fix/Resolve forms) targeting <paramref name="repo"/>; loose
    /// prose like <c>see #N</c> / <c>Linked Issue #N</c> and cross-repo
    /// references are already excluded.
    /// </summary>
    public static IReadOnlyList<PrClosingReference> ExtractCanonicalReferences(string? prBody, string repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        return ExtractReferences(prBody, repo);
    }

    private static IReadOnlyList<PrClosingReference> ExtractReferences(string? prBody, string repo)
    {
        if (string.IsNullOrWhiteSpace(prBody))
        {
            return Array.Empty<PrClosingReference>();
        }

        var refs = new List<PrClosingReference>();
        foreach (Match match in ClosingReferenceRegex.Matches(prBody))
        {
            var keyword = match.Groups["keyword"].Value;
            var crossRepo = match.Groups["repo"].Success ? match.Groups["repo"].Value : null;
            if (!int.TryParse(
                    match.Groups["number"].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number)
                || number <= 0)
            {
                continue;
            }

            // GitHub closes the issue in the *referenced* repo. A
            // cross-repo reference like `Closes other-org/other#42` does
            // not close an issue in this repo, so exclude it.
            if (!string.IsNullOrEmpty(crossRepo)
                && !string.Equals(crossRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            refs.Add(new PrClosingReference
            {
                Keyword = keyword,
                IssueNumber = number,
                CrossRepo = crossRepo,
            });
        }

        return refs;
    }

    private static IReadOnlyList<string> BuildMissingRemediation(int sourceIssue) =>
        new[]
        {
            $"Update PR body to include `Closes #{sourceIssue}` so GitHub closes the source issue when the PR merges (acceptable keywords: Close/Closes/Closed, Fix/Fixes/Fixed, Resolve/Resolves/Resolved).",
            "Use `gh pr edit <pr> --body \"$NEW_BODY\"` (no raw label edits) and re-run `intent-cli worker complete --kind issue --number <issue> --outcome pr-created --pr <pr> --write`.",
        };

    private static IReadOnlyList<string> BuildWrongIssueRemediation(int sourceIssue, int referencedIssue) =>
        new[]
        {
            $"PR body currently closes #{referencedIssue}; replace with `Closes #{sourceIssue}` (or remove the wrong reference if the worker selected the wrong target — re-run `worker next-action`).",
            "After repair, re-run `intent-cli worker complete --kind issue --number <issue> --outcome pr-created --pr <pr> --write`.",
        };

    private static IReadOnlyList<string> BuildMultipleRemediation(int sourceIssue, int[] referenced) =>
        new[]
        {
            $"PR body has multiple closing references ({string.Join(", ", referenced.Select(n => "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)))}). Keep only the source issue closure (`Closes #{sourceIssue}`) and remove the others, OR escalate to host operator if this PR genuinely closes multiple issues — the host then chooses the source-of-truth issue.",
            "Do not silently pick one — refuse and surface the gap until the operator confirms the canonical source issue.",
        };
}

internal sealed record PrClosingReference
{
    public required string Keyword { get; init; }
    public required int IssueNumber { get; init; }
    public string? CrossRepo { get; init; }
}

internal sealed record PrClosingReferenceResult
{
    public required string Classification { get; init; }
    public required bool Ok { get; init; }
    public required IReadOnlyList<PrClosingReference> References { get; init; }
    public required int SourceIssueNumber { get; init; }
    public required string Repo { get; init; }
    public required IReadOnlyList<string> Remediation { get; init; }
    public required string Summary { get; init; }
}
