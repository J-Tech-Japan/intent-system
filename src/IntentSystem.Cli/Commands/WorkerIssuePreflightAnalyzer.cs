using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G202: Pure deterministic classifier for <c>intent-cli worker issue-preflight</c>.
/// Given a fetched <see cref="GitHubIssueLookupResult"/>, target repo, and
/// implementation workdir, runs the seven-step first-match-wins precedence and
/// returns a stable <see cref="WorkerIssuePreflightResult"/>. Reuses
/// <see cref="IssueValidateBodyValidator.Validate"/> for body contract checks
/// (G183). Never mutates state, never invokes any external process, never
/// touches GitHub.
/// </summary>
internal static class WorkerIssuePreflightAnalyzer
{
    private static readonly Regex RepositoryHeaderRegex = new(
        @"Repository:\s*([A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static WorkerIssuePreflightResult Analyze(
        GitHubIssueLookupResult lookup,
        string repo,
        int issueNumber,
        string workdir)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(workdir);

        var labels = lookup.Labels?
            .Select(label => label.Name ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray()
            ?? Array.Empty<string>();

        var labelsSet = new HashSet<string>(labels, StringComparer.Ordinal);
        var state = string.IsNullOrWhiteSpace(lookup.State) ? "unknown" : lookup.State;
        var stateNormalized = state.ToLowerInvariant();
        var body = lookup.Body ?? string.Empty;
        var title = lookup.Title ?? string.Empty;

        // Step 1: non-actionable — closed or non-open state.
        // gh emits state in upper case ("OPEN", "CLOSED"); also honor the
        // explicit `closed` boolean.
        if (lookup.Closed
            || string.Equals(stateNormalized, "closed", StringComparison.Ordinal)
            || !string.Equals(stateNormalized, "open", StringComparison.Ordinal))
        {
            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.NonActionable,
                [$"issue is {stateNormalized}"],
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 2: already-pr-created.
        if (labelsSet.Contains(WorkerIssuePreflightConstants.Labels.IntentPrCreated))
        {
            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.AlreadyPrCreated,
                ["issue carries intent-pr-created label; a PR was already opened from this issue"],
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.NoAction);
        }

        // Step 3: already-in-progress.
        if (labelsSet.Contains(WorkerIssuePreflightConstants.Labels.IntentIssueInProgress))
        {
            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.AlreadyInProgress,
                ["issue carries intent-issue-in-progress label; an issue-to-PR worker has already claimed this issue"],
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.NoAction);
        }

        // Step 4: missing-target-label.
        if (!labelsSet.Contains(WorkerIssuePreflightConstants.Labels.IntentTarget))
        {
            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.MissingTargetLabel,
                ["issue is missing intent-target label; not marked as an automation target"],
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.DeclineWithSummary);
        }

        // Step 4.5: host-only-packet (G462). The issue carries intent-target
        // (passed step 4) but its declared `Target paths:` are exclusively
        // host/design-owned (intents/**, .intent-cli/**). A GitHub-contract-only
        // child loop must not edit host metadata, so this is NOT a child
        // implementation issue — it must be released from the child target (or
        // retargeted to child-owned paths) on the host/design side. Checked
        // BEFORE target-mismatch so the more specific host-only signal wins over
        // the generic mismatch heuristics. (G458 / issue #1018 regression.)
        var hostOnlyVerdict = HostOnlyPacketClassifier.Classify(body);
        if (hostOnlyVerdict.IsHostOnly)
        {
            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.HostOnlyPacket,
                new[]
                {
                    "issue is a host-only packet: every declared target path is host/design-owned ("
                        + string.Join(", ", hostOnlyVerdict.HostOwnedPaths)
                        + ") with no child-owned implementation path (src/**, tests/**, docs/**, README.md, ...).",
                    "a GitHub-contract-only child loop must not edit host metadata (intents/**, .intent-cli/**); host/design packets stay in the host/design workflow unless retargeted to child-owned paths.",
                    $"release this issue from the child target via `intent-cli automation issue-release --repo {repo} --issue {issueNumber} --write` (never raw gh label mutation), or retarget the packet to child-owned paths.",
                },
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.ReleaseFromTarget);
        }

        var declaredPaths = HostOnlyPacketClassifier.ExtractTargetPaths(body);
        if (declaredPaths.Count == 0)
        {
            return Build(
                lookup, repo, issueNumber, title, state, labels,
                WorkerIssuePreflightConstants.Classifications.MissingTargetDeclaration,
                ["declaration-derived: missing `Target paths:` declaration; cannot classify the issue from prose"],
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.WaitForClarification);
        }

        // Step 5: target-mismatch.
        var mismatchReasons = DetectTargetMismatch(body, repo, workdir, declaredPaths);
        if (mismatchReasons.Count > 0)
        {
            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.TargetMismatch,
                mismatchReasons,
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.SwitchRepo);
        }

        var advisories = DetectAdvisories(body, declaredPaths);

        // Step 6: contract-incomplete (reuse G183 validator).
        var sourcePath = $"github://{repo}/issues/{issueNumber}";
        var validation = IssueValidateBodyValidator.Validate(sourcePath, body);
        if (!validation.IsValid)
        {
            var reasons = new List<string>();
            foreach (var heading in validation.MissingHeadings)
            {
                reasons.Add($"missing required heading: {heading}");
            }
            if (validation.RelatedLinksInvalid && !string.IsNullOrWhiteSpace(validation.RelatedLinksReason))
            {
                reasons.Add(validation.RelatedLinksReason!);
            }
            else if (validation.RelatedLinksInvalid)
            {
                reasons.Add("Related Links section is invalid");
            }

            return Build(
                lookup,
                repo,
                issueNumber,
                title,
                state,
                labels,
                WorkerIssuePreflightConstants.Classifications.ContractIncomplete,
                reasons,
                actionable: false,
                WorkerIssuePreflightConstants.RecommendedActions.WaitForClarification);
        }

        // Step 7: ready-to-implement.
        return Build(
            lookup,
            repo,
            issueNumber,
            title,
            state,
            labels,
            WorkerIssuePreflightConstants.Classifications.ReadyToImplement,
            Array.Empty<string>(),
            actionable: true,
            WorkerIssuePreflightConstants.RecommendedActions.Implement,
            advisories);
    }

    private static IReadOnlyList<string> DetectTargetMismatch(string body, string repo, string workdir, IReadOnlyList<string> declaredPaths)
    {
        var reasons = new List<string>();
        if (string.IsNullOrEmpty(body))
        {
            return reasons;
        }

        // Heuristic 1: explicit "Repository: <owner/repo>" header that names a
        // different repo than --repo.
        var repoMatch = RepositoryHeaderRegex.Match(body);
        if (repoMatch.Success)
        {
            var declared = repoMatch.Groups[1].Value;
            if (!string.Equals(declared, repo, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add(
                    $"issue body declares Repository: {declared} which does not match --repo {repo}");
            }
        }

        // Declaration-derived: a target inside a submodule must run in that
        // submodule worktree. Prose mentions are advisory only.
        if (declaredPaths.Any(p => p.Replace('\\', '/').TrimStart('/').StartsWith("submodules/", StringComparison.OrdinalIgnoreCase)))
        {
            var workdirNormalized = workdir.Replace('\\', '/');
            if (workdirNormalized.IndexOf("submodules/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                reasons.Add(
                    "declaration-derived: declared target paths are inside submodules/ but --workdir is outside a submodules/ tree");
            }
        }

        return reasons;
    }

    private static IReadOnlyList<string> DetectAdvisories(string body, IReadOnlyList<string> declaredPaths)
    {
        var advisories = new List<string>();
        var declaredChild = declaredPaths.Any(p => !p.Replace('\\', '/').TrimStart('/').StartsWith("submodules/", StringComparison.OrdinalIgnoreCase));
        if (declaredChild && body.Contains("submodules/", StringComparison.OrdinalIgnoreCase))
        {
            advisories.Add("advisory-derived: prose mentions a submodules/ path, but the declared target is the child repository");
        }
        if (declaredChild && (body.Contains("parent-host", StringComparison.OrdinalIgnoreCase) || body.Contains("MyIntentHost", StringComparison.Ordinal)))
        {
            advisories.Add("advisory-derived: prose mentions parent-host/MyIntentHost, but the declared target is the child repository");
        }
        return advisories;
    }

    private static WorkerIssuePreflightResult Build(
        GitHubIssueLookupResult lookup,
        string repo,
        int issueNumber,
        string title,
        string state,
        IReadOnlyList<string> labels,
        string classification,
        IReadOnlyList<string> reasons,
        bool actionable,
        string recommendedAction,
        IReadOnlyList<string>? advisories = null)
    {
        _ = lookup;
        var summaryLine =
            $"Worker issue-preflight for {repo}#{issueNumber} — classification={classification}, actionable={actionable.ToString().ToLowerInvariant()}.";

        return new WorkerIssuePreflightResult
        {
            Actionable = actionable,
            Classification = classification,
            Issue = issueNumber,
            Repo = repo,
            Title = title,
            IssueState = state,
            Labels = labels,
            Reasons = reasons,
            Advisories = advisories ?? Array.Empty<string>(),
            RecommendedAction = recommendedAction,
            SummaryLine = summaryLine
        };
    }
}
