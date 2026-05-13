namespace IntentSystem.Cli.Commands;

/// <summary>
/// G343: pure analyzer that detects the specific drift where a
/// GitHub issue was created via the publish-flow command and a
/// canonical <c>publish.yaml</c> records its
/// <c>created_issue_number</c>, but the GitHub issue itself never
/// received the <c>intent-target</c> label because the host loop
/// stopped before applying it (typically because durable-state
/// preflight classified the publish metadata as unsafe).
///
/// The analyzer proposes a deterministic recovery only when exactly
/// one canonical publish artifact identifies the issue and the
/// GitHub issue is still open without <c>intent-target</c>. Any
/// ambiguity — two publish artifacts claiming the same issue
/// number, the issue is closed, the issue already carries
/// <c>intent-target</c>, or the artifact is non-canonical — is
/// surfaced as a structured unsafe stop instead of a guess
/// (G343 AC2 / AC4).
///
/// Pure data in / pure data out: no <c>gh</c> calls, no file I/O.
/// The command layer captures the inputs and applies the
/// recommended <c>automation issue-publish --issue &lt;n&gt; --write</c>
/// when running with <c>--write</c>.
/// </summary>
internal static class IntentTargetGapAnalyzer
{
    public const string RepairType = "intent-target-gap-recovery";
    public const string UnsafeAmbiguousArtifacts = "ambiguous-publish-artifacts-for-issue";
    public const string UnsafeIssueClosed = "intent-target-gap-issue-closed";
    public const string UnsafeIssueMissing = "intent-target-gap-issue-missing";
    public const string UnsafeNonCanonicalArtifact = "intent-target-gap-non-canonical-artifact";
    public const string UnsafeRepoMismatch = "intent-target-gap-repo-mismatch";

    public static IntentTargetGapAnalysis Analyze(
        string repo,
        IReadOnlyList<IntentTargetGapCandidate> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(candidates);

        // Detect ambiguity first: count how many candidates claim
        // each issue number. Any number with >1 claimants is unsafe
        // for all claimants.
        var issueOccurrences = new Dictionary<int, int>();
        foreach (var c in candidates)
        {
            if (c.Artifact?.CreatedIssueNumber is int n && n > 0)
            {
                issueOccurrences[n] = issueOccurrences.TryGetValue(n, out var existing) ? existing + 1 : 1;
            }
        }

        var repairs = new List<IntentTargetGapRepair>();
        var unsafeStops = new List<IntentTargetGapUnsafeStop>();

        foreach (var candidate in candidates)
        {
            // No artifact → not in scope for this lane (G303
            // publish-recovery covers the publish-artifact-missing
            // case via its own unsafe-stop kind).
            if (candidate.Artifact is null)
            {
                continue;
            }

            // Non-canonical artifact → unsafe stop. The repair must
            // not run against operator-edited or unparseable
            // metadata.
            if (!candidate.ArtifactIsCanonical)
            {
                unsafeStops.Add(new IntentTargetGapUnsafeStop
                {
                    Kind = UnsafeNonCanonicalArtifact,
                    ExecutionUnit = candidate.ExecutionUnit,
                    PublishArtifactPath = candidate.PublishArtifactPath,
                    IssueNumber = candidate.Artifact.CreatedIssueNumber,
                    Reason = $"publish.yaml at '{candidate.PublishArtifactPath}' for '{candidate.ExecutionUnit}' "
                        + "is not classified canonical by PublishYamlCanonicalAnalyzer; refusing to apply "
                        + $"intent-target. Run `{PublishYamlCanonicalAnalyzer.RecoveryCommand}` first."
                });
                continue;
            }

            if (candidate.Artifact.CreatedIssueNumber is not int issueNumber || issueNumber <= 0)
            {
                // Canonical artifact but no created_issue_number —
                // publish flow never reached GitHub. Not in scope.
                continue;
            }

            // Repo mismatch defense: if the artifact's
            // created_issue_url points at a different repo, refuse
            // to act.
            if (!string.IsNullOrEmpty(candidate.Artifact.CreatedIssueUrl)
                && !ArtifactUrlMatchesRepo(candidate.Artifact.CreatedIssueUrl!, repo))
            {
                unsafeStops.Add(new IntentTargetGapUnsafeStop
                {
                    Kind = UnsafeRepoMismatch,
                    ExecutionUnit = candidate.ExecutionUnit,
                    PublishArtifactPath = candidate.PublishArtifactPath,
                    IssueNumber = issueNumber,
                    Reason = $"publish.yaml at '{candidate.PublishArtifactPath}' targets "
                        + $"'{candidate.Artifact.CreatedIssueUrl}' which does not belong to repo '{repo}'; "
                        + "refusing to apply intent-target against the wrong repository."
                });
                continue;
            }

            if (issueOccurrences.TryGetValue(issueNumber, out var occurrences) && occurrences > 1)
            {
                unsafeStops.Add(new IntentTargetGapUnsafeStop
                {
                    Kind = UnsafeAmbiguousArtifacts,
                    ExecutionUnit = candidate.ExecutionUnit,
                    PublishArtifactPath = candidate.PublishArtifactPath,
                    IssueNumber = issueNumber,
                    Reason = $"{occurrences} publish.yaml artifacts in repo '{repo}' claim "
                        + $"created_issue_number = #{issueNumber}; cannot deterministically decide "
                        + "which execution unit owns the recovery."
                });
                continue;
            }

            // Issue must exist on GitHub.
            if (!candidate.IssueExistsOnGitHub)
            {
                unsafeStops.Add(new IntentTargetGapUnsafeStop
                {
                    Kind = UnsafeIssueMissing,
                    ExecutionUnit = candidate.ExecutionUnit,
                    PublishArtifactPath = candidate.PublishArtifactPath,
                    IssueNumber = issueNumber,
                    Reason = $"publish.yaml for '{candidate.ExecutionUnit}' records "
                        + $"created_issue_number = #{issueNumber} but no GitHub issue with that "
                        + $"number exists in '{repo}'; operator must investigate before recovery."
                });
                continue;
            }

            // Issue must be open. If the operator already closed it
            // without intent-target ever being applied, that's a
            // separate decision — refuse to silently reopen the
            // automation lane.
            if (string.Equals(candidate.IssueState, "closed", StringComparison.OrdinalIgnoreCase))
            {
                unsafeStops.Add(new IntentTargetGapUnsafeStop
                {
                    Kind = UnsafeIssueClosed,
                    ExecutionUnit = candidate.ExecutionUnit,
                    PublishArtifactPath = candidate.PublishArtifactPath,
                    IssueNumber = issueNumber,
                    Reason = $"GitHub issue #{issueNumber} in '{repo}' is closed; refusing to apply "
                        + "intent-target to a closed issue. Operator must either reopen the issue "
                        + "before recovery or close out the execution unit through the normal lifecycle."
                });
                continue;
            }

            // Already has intent-target → no gap, nothing to repair.
            if (candidate.IssueLabels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
            {
                continue;
            }

            // Already has the intent-issue-in-progress / intent-pr-created
            // labels — automation has clearly progressed past the
            // publish boundary by a different route; do not re-apply.
            if (candidate.IssueLabels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress, StringComparer.Ordinal)
                || candidate.IssueLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
            {
                continue;
            }

            repairs.Add(new IntentTargetGapRepair
            {
                Type = RepairType,
                ExecutionUnit = candidate.ExecutionUnit,
                PublishArtifactPath = candidate.PublishArtifactPath,
                IssueNumber = issueNumber,
                IssueUrl = candidate.Artifact.CreatedIssueUrl ?? $"https://github.com/{repo}/issues/{issueNumber}",
                RecoveryCommand = $"intent-cli automation issue-publish --repo {repo} --issue {issueNumber} --write",
                Evidence = new[]
                {
                    $"canonical publish.yaml at '{candidate.PublishArtifactPath}' records created_issue_number = #{issueNumber}.",
                    $"GitHub issue #{issueNumber} in '{repo}' is open and does not carry `intent-target`.",
                    "no other publish artifact claims this issue number; recovery is deterministic."
                },
                Summary = $"Apply `intent-target` to issue #{issueNumber} for '{candidate.ExecutionUnit}' "
                    + "via automation issue-publish (publish boundary stalled before label transition)."
            });
        }

        return new IntentTargetGapAnalysis
        {
            Repo = repo,
            SafeRepairs = repairs,
            UnsafeStops = unsafeStops
        };
    }

    private static bool ArtifactUrlMatchesRepo(string artifactUrl, string repo)
    {
        var marker = $"github.com/{repo}/issues/";
        return artifactUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal sealed record IntentTargetGapCandidate
{
    public required string ExecutionUnit { get; init; }
    public required string PublishArtifactPath { get; init; }
    public required IssuePublishArtifact? Artifact { get; init; }
    /// <summary>
    /// G343: the caller must run <see cref="PublishYamlCanonicalAnalyzer"/>
    /// against the artifact content first and set this flag. Recovery
    /// only proceeds against canonical artifacts.
    /// </summary>
    public required bool ArtifactIsCanonical { get; init; }
    public required bool IssueExistsOnGitHub { get; init; }
    public required string? IssueState { get; init; }
    public required IReadOnlyCollection<string> IssueLabels { get; init; }
}

internal sealed record IntentTargetGapAnalysis
{
    public required string Repo { get; init; }
    public required IReadOnlyList<IntentTargetGapRepair> SafeRepairs { get; init; }
    public required IReadOnlyList<IntentTargetGapUnsafeStop> UnsafeStops { get; init; }
}

internal sealed record IntentTargetGapRepair
{
    public required string Type { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string PublishArtifactPath { get; init; }
    public required int IssueNumber { get; init; }
    public required string IssueUrl { get; init; }
    public required string RecoveryCommand { get; init; }
    public required IReadOnlyList<string> Evidence { get; init; }
    public required string Summary { get; init; }
}

internal sealed record IntentTargetGapUnsafeStop
{
    public required string Kind { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string PublishArtifactPath { get; init; }
    public required int? IssueNumber { get; init; }
    public required string Reason { get; init; }
}
