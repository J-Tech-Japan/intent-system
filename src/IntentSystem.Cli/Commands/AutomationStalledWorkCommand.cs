using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G523: <c>intent-cli automation stalled-work --domain &lt;d&gt; --repo &lt;r&gt;
/// [--stale-minutes &lt;m&gt;] [--claimed-silent-minutes &lt;m&gt;]
/// [--format json|markdown]</c> — read-only inventory of pending pipeline
/// transitions with ages, so a single orchestrator wake (or an external
/// heartbeat) can detect a stall without a human cross-checking GitHub
/// labels, PR state, and queue-state by hand.
///
/// Actionable categories (carry a runnable <c>recommended_action</c> command,
/// <see cref="StalledWorkItem.IsInformational"/> is <see langword="false"/>):
/// <list type="bullet">
/// <item><c>published-not-delegated</c> — an OPEN issue carries
///   <c>intent-target</c>, has no claim label
///   (<c>intent-issue-in-progress</c> / <c>intent-pr-created</c>), and no
///   open PR in this repo closes it (checked independently of label state,
///   since a label can drift out of sync with an already-created PR).</item>
/// <item><c>pr-created-not-reviewing</c> — the source issue carries
///   <c>intent-pr-created</c> and its closing PR has not had the
///   <c>review-start</c> transition applied (no <c>intent-pr-reviewing</c> /
///   <c>intent-pr-approved</c> on the PR), and the PR is NOT already in the
///   repair or rereview lifecycle (see <c>repair-pending</c>/
///   <c>rereview-pending</c> below — G533 review repair, field finding: PR
///   #1750 was misreported this way with a wrong review-start
///   recommendation mid-repair).</item>
/// <item><c>merged-not-closed-out</c> — a MERGED PR's linked queue item is
///   not <see cref="QueueItemState.Completed"/>.</item>
/// </list>
///
/// Informational categories (G533 — age for visibility only,
/// <c>recommended_action</c> is descriptive prose, never a transition,
/// <see cref="StalledWorkItem.IsInformational"/> is <see langword="true"/>):
/// <list type="bullet">
/// <item><c>repair-pending</c> — a PR carrying <c>intent-pr-request-update</c>
///   and/or <c>intent-pr-update-in-progress</c>; a review-start transition
///   would be actively wrong mid-repair.</item>
/// <item><c>rereview-pending</c> — a PR carrying
///   <c>intent-pr-rereview-ready</c>; repair pushed, awaiting re-review.</item>
/// <item><c>claimed-but-silent</c> — an issue carrying
///   <c>intent-issue-in-progress</c> (no PR yet) with no observable activity
///   for longer than <c>--claimed-silent-minutes</c> (default
///   <see cref="DefaultClaimedSilentMinutes"/> minutes) — the third measured
///   field stall class (silent completion / dead worker after claim).
///   Recommends a worker status check, never assumes completion or
///   failure from silence alone.</item>
/// </list>
///
/// Age is approximated from the relevant GitHub entity's `createdAt` /
/// `updatedAt` timestamp (GitHub does not expose per-label-application
/// timestamps, or per-issue timeline events without a dedicated per-issue
/// fetch this slice does not add), which is the closest available proxy for
/// "how long has this been pending" / "how long has this been silent".
///
/// Strictly read-only: no GitHub mutation, no queue-state/runs.jsonl write,
/// no label change, no message sent — informational kinds recommend a
/// status check but never send one themselves.
///
/// Execution-unit and domain identification (G532, review-repaired):
/// <list type="bullet">
/// <item>the execution unit is the LEADING ID token of the title
///   (<see cref="ExecutionUnitFromTitle"/>), not everything before the
///   first colon — a title like <c>"SKS-G815 G812 sub-slice 1: ..."</c>
///   resolves to <c>SKS-G815</c>, not the whole pre-colon phrase — and only
///   when a real packet.yaml corroborates it (a bare-looking match with no
///   corresponding packet is NOT trusted, e.g. <c>"G12abc"</c> never yields
///   unit <c>G12</c>);</item>
/// <item>when no leading ID token is corroborated, the candidate is matched
///   against every packet under <c>.intent-cli/issues/*/packet.yaml</c> by
///   that packet's own declared <c>source_execution_unit</c> appearing as a
///   whole token within the title (<see cref="MatchExecutionUnitBySourceExecutionUnit"/>).
///   Exactly one distinct matching packet is required — two or more
///   different packets' declared units both appearing in the same title is
///   reported as <see cref="ReasonExecutionUnitAmbiguous"/> rather than
///   guessed at (e.g. by picking the longest match);</item>
/// <item>domain is read from the packet's nested
///   <c>implementation_issue_packet.domain</c> field first, falling back to
///   a top-level <c>domain:</c> field as a compatibility alias (<see
///   cref="ReadPacketDeclaredDomain"/>);</item>
/// <item>domain confirmation uses the same G522 order as every other
///   execution-unit-resolving surface (<see cref="PacketDomainResolution"/>)
///   — but ONLY for a candidate whose execution unit is itself corroborated
///   by real packet/queue linkage (a matched packet.yaml, or — for
///   <c>merged-not-closed-out</c> — an already-matched queue-state item).
///   For such a candidate, an explicit <c>--domain</c> (always present — it
///   is a required argument here) wins whenever that linkage is silent on
///   domain, and is an error only when it actively CONTRADICTS a
///   packet-declared domain. A candidate whose execution unit could NOT be
///   corroborated at all is never assumed to belong to the requested
///   <c>--domain</c> just because one was passed — <c>--domain</c> scopes
///   the scan, it does not by itself identify an otherwise-unidentified
///   candidate as a member of it. Every exclusion (contradiction,
///   uncorroborated, or ambiguous) is reported in <c>excluded[]</c> with its
///   reason and the derivation attempted, never silent.</item>
/// </list>
/// </summary>
internal static class AutomationStalledWorkCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string KindPublishedNotDelegated = "published-not-delegated";
    public const string KindPrCreatedNotReviewing = "pr-created-not-reviewing";
    public const string KindMergedNotClosedOut = "merged-not-closed-out";

    /// <summary>
    /// G533: a PR whose source issue carries <c>intent-pr-created</c> but
    /// which is itself already in the repair lifecycle (<c>intent-pr-
    /// request-update</c> and/or <c>intent-pr-update-in-progress</c>) —
    /// informational, since a review-start transition would be wrong
    /// mid-repair (field finding: PR #1750 was misreported as
    /// <see cref="KindPrCreatedNotReviewing"/> with that exact wrong
    /// recommendation).
    /// </summary>
    public const string KindRepairPending = "repair-pending";

    /// <summary>
    /// G533: a PR carrying <c>intent-pr-rereview-ready</c> — repair pushed,
    /// awaiting re-review. Informational, same reasoning as
    /// <see cref="KindRepairPending"/>.
    /// </summary>
    public const string KindRereviewPending = "rereview-pending";

    /// <summary>
    /// G533: an issue claimed via <c>intent-issue-in-progress</c> (with no
    /// PR yet created) showing no observable activity for longer than the
    /// conservative default threshold — the third measured stall class
    /// from the field analysis (silent completion / dead worker after
    /// claim). Informational: recommends a worker status check, never a
    /// state transition (this command never assumes completion or failure
    /// from silence alone).
    /// </summary>
    public const string KindClaimedButSilent = "claimed-but-silent";

    /// <summary>
    /// G533: conservative default for <c>--claimed-silent-minutes</c> (12
    /// hours) — chosen so <c>claimed-but-silent</c> does not fire on an
    /// ordinary work session; only a genuinely long silence after claim.
    /// </summary>
    public const int DefaultClaimedSilentMinutes = 720;

    /// <summary>
    /// G532 review repair: a title matched more than one distinct packet's
    /// declared <c>source_execution_unit</c> as a token — the execution unit
    /// cannot be picked without guessing, so it is reported here instead of
    /// silently choosing the longest (or first-sorted) match.
    /// </summary>
    public const string ReasonExecutionUnitAmbiguous = "execution-unit-ambiguous";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation stalled-work --domain <name> --repo <owner/repo> [--stale-minutes <m>] [--claimed-silent-minutes <m>] [--format json|markdown]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var repo, out var staleMinutes, out var claimedSilentMinutes, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        AutomationStalledWorkResult result;
        try
        {
            result = Analyze(context, domain!, repo!, staleMinutes, claimedSilentMinutes);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to read GitHub state for {repo}: {exception.Message}");
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    /// <summary>
    /// G526: analyzer surface extracted from <see cref="Execute"/> so
    /// <c>automation heartbeat</c> can wrap the identical scan (through the
    /// identical <see cref="CandidateListerFactory"/> / <see
    /// cref="UtcNowFactory"/> seams) without re-shelling to <c>gh</c> or
    /// round-tripping through this command's own JSON output. Throws
    /// <see cref="IOException"/> / <see cref="InvalidOperationException"/>
    /// on a GitHub read failure — callers decide how to report it.
    /// <paramref name="claimedSilentMinutes"/> defaults to
    /// <see cref="DefaultClaimedSilentMinutes"/> so existing callers (e.g.
    /// <c>automation heartbeat</c>) keep compiling and get the conservative
    /// default without needing their own override plumbing.
    /// </summary>
    public static AutomationStalledWorkResult Analyze(
        CliContext context, string domain, string repo, int staleMinutes, int claimedSilentMinutes = DefaultClaimedSilentMinutes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);

        var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
        var openIssues = lister.ListIssues(repo, Array.Empty<string>());
        var openPrs = lister.ListPullRequests(repo, Array.Empty<string>());
        var mergedPrs = lister.ListMergedPullRequests(repo, Array.Empty<string>());

        var now = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var candidateDomains = DomainCandidateScanner.Scan(context);
        var items = new List<StalledWorkItem>();
        var excluded = new List<StalledWorkExcluded>();
        var warnings = new List<string>();

        CollectPublishedNotDelegated(context, domain, candidateDomains, openIssues, openPrs, repo, now, items, excluded);
        CollectPrCreatedNotReviewing(context, domain, candidateDomains, openIssues, openPrs, repo, now, items, excluded);
        CollectMergedNotClosedOut(context, domain, candidateDomains, repo, mergedPrs, now, items, excluded, warnings);
        CollectClaimedButSilent(context, domain, candidateDomains, openIssues, openPrs, repo, now, claimedSilentMinutes, items, excluded);

        var filtered = items
            .Where(item => item.AgeMinutes >= staleMinutes)
            .OrderByDescending(item => item.AgeMinutes)
            .ToArray();

        return new AutomationStalledWorkResult
        {
            Domain = domain,
            Repo = repo,
            StaleMinutesThreshold = staleMinutes,
            Stalled = filtered.Length > 0,
            Items = filtered,
            Excluded = excluded,
            Warnings = warnings,
        };
    }

    private static void CollectPublishedNotDelegated(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        foreach (var issue in openIssues)
        {
            if (!IsOpen(issue.State))
            {
                continue;
            }

            var labels = LabelSet(issue.Labels);
            if (!labels.Contains(WorkerNextActionConstants.Labels.IntentTarget))
            {
                continue;
            }

            // Already claimed or delegated — not a stall in this category.
            if (labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated))
            {
                continue;
            }

            // PR #1148 review repair: a completion label can drift out of
            // sync with reality — an open PR may already close this issue
            // even though `intent-pr-created` was never applied (or was
            // removed). Check the already-fetched PR closing references
            // independently of issue labels so a label-drifted, already-
            // implemented issue is never mis-recommended for `worker claim`.
            if (HasOpenClosingPr(issue.Number, openPrs, repo))
            {
                continue;
            }

            var resolution = ResolveExecutionUnit(context, issue.Title);
            var packetDeclaredDomain = resolution.Corroborated ? ReadPacketDeclaredDomain(context, resolution.ExecutionUnit) : null;
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindPublishedNotDelegated,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = new StalledWorkRef { Number = issue.Number, Url = issue.Url },
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindPublishedNotDelegated,
                ExecutionUnit = resolution.ExecutionUnit,
                Issue = new StalledWorkRef { Number = issue.Number, Url = issue.Url },
                Pr = null,
                AgeMinutes = ComputeAgeMinutes(issue.CreatedAt, now),
                IsInformational = false,
                RecommendedAction =
                    $"intent-cli worker claim --repo {repo} --kind issue --number {issue.Number} --github-only --write",
            });
        }
    }

    private static void CollectPrCreatedNotReviewing(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        var issuesWithPrCreated = openIssues
            .Where(issue => IsOpen(issue.State) && LabelSet(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentPrCreated))
            .ToDictionary(issue => issue.Number);

        if (issuesWithPrCreated.Count == 0)
        {
            return;
        }

        foreach (var pr in openPrs)
        {
            if (!IsOpen(pr.State) || pr.IsDraft)
            {
                continue;
            }

            var prLabels = LabelSet(pr.Labels);
            if (prLabels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing)
                || prLabels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrApproved))
            {
                continue;
            }

            GitHubAutomationIssueCandidate? matchedIssue = null;
            foreach (var reference in pr.ClosingIssuesReferences)
            {
                if (reference.Number > 0
                    && ReferenceMatchesRepo(reference, repo)
                    && issuesWithPrCreated.TryGetValue(reference.Number, out var candidate))
                {
                    matchedIssue = candidate;
                    break;
                }
            }

            if (matchedIssue is null)
            {
                continue;
            }

            // G533: a PR already in the repair or rereview lifecycle is NOT
            // a "review hasn't started" stall — recommending review-start
            // mid-repair is actively wrong (field finding: PR #1750 was
            // misreported this way). Report it as an informational kind
            // with age and no transition recommendation, rather than
            // silently excluding it or misreporting it.
            string kind;
            bool isInformational;
            string recommendedAction;
            if (prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate)
                || prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress))
            {
                kind = KindRepairPending;
                isInformational = true;
                recommendedAction =
                    "none — PR is in the repair lifecycle (change requested or repair in progress); "
                    + "no transition is recommended until the repair completes.";
            }
            else if (prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrRereviewReady))
            {
                kind = KindRereviewPending;
                isInformational = true;
                recommendedAction =
                    "none — PR has a repair pushed and is awaiting re-review; "
                    + "no transition is recommended until a reviewer or automation re-reviews it.";
            }
            else
            {
                kind = KindPrCreatedNotReviewing;
                isInformational = false;
                recommendedAction =
                    $"intent-cli automation pr-transition --repo {repo} --pr {pr.Number} --transition review-start --write";
            }

            var resolution = ResolveExecutionUnit(context, matchedIssue.Title);
            var packetDeclaredDomain = resolution.Corroborated ? ReadPacketDeclaredDomain(context, resolution.ExecutionUnit) : null;
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = kind,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                    Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            // G533: repair-pending/rereview-pending age is best approximated
            // by the PR's own `updatedAt` (bumped when the request-update/
            // update-in-progress/rereview-ready label was applied) rather
            // than `createdAt` — these are POST-creation lifecycle states,
            // so "how long has this been pending" means "since entering
            // this state", not "since the PR was opened".
            var ageSource = isInformational ? pr.UpdatedAt : pr.CreatedAt;

            items.Add(new StalledWorkItem
            {
                Kind = kind,
                ExecutionUnit = resolution.ExecutionUnit,
                Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                AgeMinutes = ComputeAgeMinutes(ageSource, now),
                IsInformational = isInformational,
                RecommendedAction = recommendedAction,
            });
        }
    }

    private static void CollectMergedNotClosedOut(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        IReadOnlyList<GitHubAutomationPrCandidate> mergedPrs,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded,
        List<string> warnings)
    {
        if (mergedPrs.Count == 0)
        {
            return;
        }

        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(context.RepoRoot, domain, repo);
        if (!File.Exists(queueStateLocation.Path))
        {
            warnings.Add($"queue-state not found at '{queueStateLocation.Path}'; skipped merged-not-closed-out check.");
            return;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStateLocation.Path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            warnings.Add($"queue-state at '{queueStateLocation.Path}' could not be parsed: {exception.Message}");
            return;
        }

        foreach (var pr in mergedPrs)
        {
            var prToken = pr.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // G532 review repair: a merged PR can be referenced by MORE
            // THAN ONE non-completed queue item (a stale duplicate, a
            // data-entry mistake, or two execution units both mistakenly
            // claiming the same issue/PR). The prior FirstOrDefault picked
            // whichever item happened to be first in JSON order and
            // silently ignored the rest — the selected execution unit
            // depended on queue-state ordering, not on evidence. Collect
            // every ACTIVE (non-completed) match first; more than one is
            // never resolved by picking one, it is reported as ambiguous.
            var activeMatches = queueState.Items
                .Where(item => MatchesLinkedPr(item.LinkedPr, repo, prToken) && item.State != QueueItemState.Completed)
                .ToArray();
            if (activeMatches.Length == 0)
            {
                continue;
            }

            if (activeMatches.Length > 1)
            {
                var itemDescriptions = activeMatches.Select(item =>
                {
                    var linkedIssueDescription = item.LinkedIssue is { } li ? $"{li.Repo}#{li.Number}" : "(no linked_issue)";
                    return $"`{item.ExecutionUnit}` (state={item.State}, linked_issue={linkedIssueDescription})";
                });
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindMergedNotClosedOut,
                    ExecutionUnit = string.Empty,
                    Issue = null,
                    Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                    Reason = ReasonExecutionUnitAmbiguous,
                    Detail =
                        $"{activeMatches.Length} active (non-completed) queue-state items reference merged PR "
                        + $"#{pr.Number} via linked_pr `{prToken}`: {string.Join("; ", itemDescriptions)}. "
                        + $"Queue-state path: `{queueStateLocation.Path}`. Exactly one authoritative queue item is "
                        + "required; not resolved by selecting the first in JSON order.",
                });
                continue;
            }

            var matchedItem = activeMatches[0];

            var issueRef = matchedItem.LinkedIssue is { Number: { } linkedIssueNumber } linkedIssue
                ? new StalledWorkRef { Number = linkedIssueNumber, Url = linkedIssue.Url ?? string.Empty }
                : null;
            var prRef = new StalledWorkRef { Number = pr.Number, Url = pr.Url };

            // G532 review repair: MatchesLinkedPr alone (a bare PR number,
            // possibly with no repository identity at all) is not enough
            // corroboration — on a shared/multi-repo queue-state, a bare
            // `linked_pr: "1300"` could coincidentally match an unrelated
            // repo's PR #1300. Cross-check the queue item's OWN declared
            // linked_issue (repo + number) against this merged PR's own
            // GitHub-reported closing references for the scanned repo —
            // genuine correspondence, not a number coincidence. Missing,
            // wrong-repo, or non-corresponding linkage fails closed.
            if (!QueueItemLinkedIssueCorroboratesPr(matchedItem.LinkedIssue, pr, repo))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindMergedNotClosedOut,
                    ExecutionUnit = matchedItem.ExecutionUnit,
                    Issue = issueRef,
                    Pr = prRef,
                    Reason = PacketDomainResolution.ReasonUnderivable,
                    Detail =
                        $"queue-state item `{matchedItem.ExecutionUnit}` links PR `{prToken}` by bare number only — "
                        + $"its declared linked_issue ({(matchedItem.LinkedIssue is { } li ? $"{li.Repo}#{li.Number}" : "(none)")}) "
                        + $"does not match any of merged PR #{pr.Number}'s own GitHub-reported closing references for "
                        + $"`{repo}`. A bare PR-number match alone is not sufficient corroboration on a shared/"
                        + "multi-repo queue-state; excluded rather than assumed.",
                });
                continue;
            }

            // The exact-linkage cross-check above IS corroborating
            // packet/queue linkage — this candidate's execution unit was
            // not guessed from a title, it came from a queue item whose
            // OWN declared linked_issue was just verified against the
            // merged PR's own GitHub-reported closing references — so it
            // is treated as corroborated here, even if packet.yaml itself
            // was since cleaned up from disk.
            var packetDeclaredDomain = ReadPacketDeclaredDomain(context, matchedItem.ExecutionUnit);
            var resolution = new ExecutionUnitResolution(
                matchedItem.ExecutionUnit, Corroborated: true, IsAmbiguous: false, CandidatePacketPaths: Array.Empty<string>());
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindMergedNotClosedOut,
                    ExecutionUnit = matchedItem.ExecutionUnit,
                    Issue = issueRef,
                    Pr = prRef,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindMergedNotClosedOut,
                ExecutionUnit = matchedItem.ExecutionUnit,
                Issue = issueRef,
                Pr = prRef,
                // Best-effort merge-time proxy: `gh pr list` does not expose a
                // dedicated `mergedAt` field in the requested field set;
                // `updatedAt` is set to the merge time for a merged PR.
                AgeMinutes = ComputeAgeMinutes(pr.UpdatedAt, now),
                IsInformational = false,
                RecommendedAction =
                    $"intent-cli closeout pr --pr {pr.Number} --repo {repo} --domain {domain} --pr-merged true --write --format json",
            });
        }
    }

    /// <summary>
    /// G533: an issue claimed via <c>intent-issue-in-progress</c> that has
    /// produced no observable activity for longer than
    /// <paramref name="claimedSilentMinutes"/> — the third measured field
    /// stall class (silent completion / dead worker after claim). Scoped to
    /// issues WITHOUT <c>intent-pr-created</c> yet — once a PR exists, the
    /// PR-lifecycle kinds (<see cref="KindPrCreatedNotReviewing"/>,
    /// <see cref="KindRepairPending"/>, <see cref="KindRereviewPending"/>)
    /// take over; detecting a repair-state PR that is itself stale is a
    /// deliberately separate, out-of-scope follow-up.
    ///
    /// "Observable activity" is approximated as the MORE RECENT of the
    /// issue's own <c>updatedAt</c> (GitHub bumps this on any label change,
    /// comment, or other timeline event — the closest available proxy
    /// without a dedicated per-issue timeline-events fetch) and the
    /// <c>updatedAt</c> of any open PR whose closing references name this
    /// issue (a linked PR's own activity counts too, even before
    /// <c>intent-pr-created</c> is applied — e.g. a freshly-opened draft).
    /// Conservative by construction: the default 720-minute threshold means
    /// an ordinary work session never fires this kind.
    /// </summary>
    private static void CollectClaimedButSilent(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo,
        DateTimeOffset now,
        int claimedSilentMinutes,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        foreach (var issue in openIssues)
        {
            if (!IsOpen(issue.State))
            {
                continue;
            }

            var labels = LabelSet(issue.Labels);
            if (!labels.Contains(WorkerNextActionConstants.Labels.IntentTarget))
            {
                continue;
            }
            if (!labels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress))
            {
                continue;
            }
            if (labels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated))
            {
                // A PR already exists — the PR-lifecycle kinds cover this
                // issue's ongoing state now, not the claim-silence check.
                continue;
            }

            var lastActivity = ParseTimestampOrFallback(issue.UpdatedAt, issue.CreatedAt);
            foreach (var pr in openPrs)
            {
                if (!IsOpen(pr.State))
                {
                    continue;
                }
                foreach (var reference in pr.ClosingIssuesReferences)
                {
                    if (reference.Number == issue.Number && ReferenceMatchesRepo(reference, repo))
                    {
                        var prActivity = ParseTimestampOrFallback(pr.UpdatedAt, pr.CreatedAt);
                        if (prActivity > lastActivity)
                        {
                            lastActivity = prActivity;
                        }
                        break;
                    }
                }
            }

            var silentMinutes = ComputeAgeMinutesFromInstant(lastActivity, now);
            if (silentMinutes < claimedSilentMinutes)
            {
                continue;
            }

            var resolution = ResolveExecutionUnit(context, issue.Title);
            var packetDeclaredDomain = resolution.Corroborated ? ReadPacketDeclaredDomain(context, resolution.ExecutionUnit) : null;
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindClaimedButSilent,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = new StalledWorkRef { Number = issue.Number, Url = issue.Url },
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindClaimedButSilent,
                ExecutionUnit = resolution.ExecutionUnit,
                Issue = new StalledWorkRef { Number = issue.Number, Url = issue.Url },
                Pr = null,
                AgeMinutes = silentMinutes,
                IsInformational = true,
                RecommendedAction =
                    $"status check: no observable activity on `{resolution.ExecutionUnit}` (issue #{issue.Number}) "
                    + $"for {silentMinutes}m since claim — ask the assigned worker for a status update; "
                    + "do not assume completion, failure, or transition state from silence alone.",
            });
        }
    }

    /// <summary>
    /// G532: aligns stalled-work's domain confirmation with the shared
    /// <see cref="PacketDomainResolution"/> order already used by every
    /// other execution-unit-resolving surface (explicit <c>--domain</c> >
    /// packet-declared domain > fail-loud). This supersedes the PR #1148
    /// tightening, which treated a missing/absent packet-declared domain as
    /// fail-closed on the theory that a broad multi-candidate scan cannot
    /// trust <c>--domain</c> alone for a candidate it cannot corroborate —
    /// in production that policy excluded exactly the stalls this surface
    /// exists to find (field findings against sekiban-as-a-service,
    /// 2026-07-15 SKS-G815 and 2026-07-18 SKS-G823), each papered over with
    /// a team workaround instead of surfaced. Since <c>--domain</c> is a
    /// REQUIRED argument for this command, <see cref="PacketDomainResolution.Resolve"/>
    /// always has an explicit domain to fall back on, so a candidate is
    /// excluded only on a genuine CONTRADICTION between <c>--domain</c> and
    /// a packet that actively declares a different domain — never merely
    /// because the packet is silent on domain.
    /// </summary>
    private static bool TryConfirmDomain(
        string domain,
        ExecutionUnitResolution executionUnitResolution,
        string? packetDeclaredDomain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        out string reason,
        out string detail)
    {
        var reinvocation = $"intent-cli automation stalled-work --domain <name> --repo {repo} --format json";

        if (!executionUnitResolution.Corroborated)
        {
            var candidates = candidateDomains.Count > 0
                ? string.Join(", ", candidateDomains)
                : "(none found under intents/)";

            if (executionUnitResolution.IsAmbiguous)
            {
                reason = ReasonExecutionUnitAmbiguous;
                detail =
                    "the execution unit is ambiguous: more than one packet's declared `source_execution_unit` "
                    + $"matches this title as a token — candidate packets: {string.Join(", ", executionUnitResolution.CandidatePacketPaths)}. "
                    + $"Candidate domains: {candidates}. Re-invoke with: {reinvocation}. "
                    + "Not assumed to belong to any one of them without an unambiguous match.";
                return false;
            }

            reason = PacketDomainResolution.ReasonUnderivable;
            detail =
                "the execution unit could not be corroborated by any packet: no leading ID token's packet.yaml "
                + "exists, and no packet under `.intent-cli/issues/*/packet.yaml` declares a `source_execution_unit` "
                + $"matching this title. Candidate domains: {candidates}. Re-invoke with: {reinvocation}. "
                + "An explicit --domain scopes the scan; it does not by itself establish that an otherwise-"
                + "unidentified candidate is a member of it, so this candidate is excluded rather than assumed.";
            return false;
        }

        var executionUnit = executionUnitResolution.ExecutionUnit;
        var resolution = PacketDomainResolution.Resolve(domain, packetDeclaredDomain, candidateDomains, reinvocation);
        if (resolution.IsError)
        {
            reason = resolution.Reason!;
            detail =
                $"`{executionUnit}`: {resolution.ErrorMessage} Derivation attempted: checked nested "
                + $"`implementation_issue_packet.domain` and top-level `domain:` alias at "
                + $"`.intent-cli/issues/{executionUnit}/packet.yaml`.";
            return false;
        }

        reason = string.Empty;
        detail = string.Empty;
        return true;
    }

    /// <summary>
    /// G532: the nested <c>implementation_issue_packet.domain</c> field is
    /// first-class; a top-level <c>domain:</c> field is accepted as a
    /// compatibility alias when the nested field is absent. Checking the
    /// nested path FIRST (rather than relying on the shared scalar parser's
    /// bare-key fallback, which favors whichever `domain:` line appears
    /// first in the file) avoids an unrelated same-named field elsewhere in
    /// the packet — e.g. a `review_context_packet` section — silently
    /// shadowing the real declaration.
    /// </summary>
    private static string? ReadPacketDeclaredDomain(CliContext context, string executionUnit)
    {
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            return null;
        }
        var packetYamlPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit, "packet.yaml");
        if (!File.Exists(packetYamlPath))
        {
            return null;
        }
        try
        {
            var fields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetYamlPath));
            return ReadFirstNonEmpty(fields, "implementation_issue_packet.domain", "domain");
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ReadFirstNonEmpty(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static bool HasOpenClosingPr(int issueNumber, IReadOnlyList<GitHubAutomationPrCandidate> openPrs, string repo)
    {
        foreach (var pr in openPrs)
        {
            if (!IsOpen(pr.State))
            {
                continue;
            }
            foreach (var reference in pr.ClosingIssuesReferences)
            {
                if (reference.Number == issueNumber && ReferenceMatchesRepo(reference, repo))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsOpen(string state) =>
        string.IsNullOrEmpty(state) || string.Equals(state, "OPEN", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> LabelSet(IReadOnlyList<GitHubAutomationLabel> labels) =>
        labels.Select(label => label.Name).ToHashSet(StringComparer.Ordinal);

    private static bool ReferenceMatchesRepo(GitHubPrClosingIssueReference reference, string repo)
    {
        if (reference.Repository is not { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
        {
            // No repository descriptor — assume same-repo (gh omits it for
            // same-repo closing references in some field-set combinations).
            return true;
        }
        var candidateRepo = $"{repository.Owner!.Login}/{repository.Name}";
        return string.Equals(candidateRepo, repo, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLinkedPr(string? linkedPr, string repo, string prToken)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return false;
        }

        if (string.Equals(linkedPr, prToken, StringComparison.Ordinal))
        {
            return true;
        }

        if (linkedPr!.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return linkedPr.StartsWith($"https://github.com/{repo}/pull/", StringComparison.OrdinalIgnoreCase)
                && linkedPr.EndsWith($"/{prToken}", StringComparison.Ordinal);
        }

        return linkedPr!.EndsWith($"/{prToken}", StringComparison.Ordinal);
    }

    /// <summary>
    /// G532 review repair: <see cref="MatchesLinkedPr"/> alone (a bare PR
    /// number, or a URL) is not sufficient corroboration for a queue item
    /// on a shared/multi-repo queue-state, where a bare number could
    /// coincidentally match an unrelated repo's PR of the same number. True
    /// only when the queue item declares a <c>linked_issue</c> with BOTH a
    /// repo matching the scanned <paramref name="repo"/> AND an issue
    /// number that genuinely appears among <paramref name="pr"/>'s own
    /// GitHub-reported closing-issue references for that repo — a missing,
    /// wrong-repo, or non-corresponding linked_issue fails closed.
    /// </summary>
    private static bool QueueItemLinkedIssueCorroboratesPr(
        LinkedIssue? linkedIssue, GitHubAutomationPrCandidate pr, string repo)
    {
        if (linkedIssue is not { Number: { } linkedIssueNumber })
        {
            return false;
        }
        if (!string.Equals(linkedIssue.Repo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number == linkedIssueNumber && ReferenceMatchesRepo(reference, repo))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// G532 review repair: resolves the candidate execution unit from an
    /// issue/PR title, requiring CORROBORATION by a real packet before
    /// trusting either identification path — first via the leading ID token
    /// (<see cref="ExecutionUnitFromTitle"/>) when a matching packet.yaml
    /// exists at that exact path, falling back to a packet-directory scan
    /// matching by declared <c>source_execution_unit</c> (<see
    /// cref="MatchExecutionUnitBySourceExecutionUnit"/>) otherwise. The
    /// returned <see cref="ExecutionUnitResolution.ExecutionUnit"/> is used
    /// ONLY to locate the candidate's packet.yaml — never as the
    /// domain-membership decision itself (see <see cref="TryConfirmDomain"/>).
    /// </summary>
    private static ExecutionUnitResolution ResolveExecutionUnit(CliContext context, string title)
    {
        var leadingToken = ExecutionUnitFromTitle(title);
        if (!string.IsNullOrEmpty(leadingToken))
        {
            var packetPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", leadingToken, "packet.yaml");
            if (File.Exists(packetPath))
            {
                return new ExecutionUnitResolution(leadingToken, Corroborated: true, IsAmbiguous: false, CandidatePacketPaths: [packetPath]);
            }
        }

        // Leading token absent, OR present but uncorroborated by any real
        // packet.yaml at that exact path — fall back to a full scan by
        // declared source_execution_unit rather than trusting an unverified
        // guess (e.g. "G12abc" must never yield unit "G12").
        var fallback = MatchExecutionUnitBySourceExecutionUnit(context, title);
        if (fallback.Corroborated || fallback.IsAmbiguous)
        {
            return fallback;
        }

        // Neither path corroborated anything. Still surface the leading
        // token as a best-effort GUESS for human readability in an
        // excluded[] entry — Corroborated stays false, so it is never
        // trusted for a domain decision (see TryConfirmDomain).
        return new ExecutionUnitResolution(leadingToken, Corroborated: false, IsAmbiguous: false, CandidatePacketPaths: Array.Empty<string>());
    }

    /// <summary>
    /// Extracts the LEADING ID token from a title — <c>^[A-Z]+-G?[0-9]+</c>
    /// (an optionally-alphanumeric prefix, a dash, an optional literal
    /// <c>G</c>, then digits — e.g. <c>SKS-G815</c>, <c>Z4R-G3</c>) or a
    /// bare <c>^G[0-9]+</c> (e.g. <c>G523</c>), with a mandatory RIGHT
    /// boundary (no immediately-following letter/digit) so <c>"SKS-G815foo"</c>
    /// or <c>"G12abc"</c> never yield a truncated <c>SKS-G815</c> / <c>G12</c>.
    /// G532: replaces the prior "everything before the first colon" rule,
    /// which broke on a title whose ID is not immediately followed by a
    /// colon (e.g. <c>"SKS-G815 G812 sub-slice 1: ..."</c> used to resolve
    /// to the whole pre-colon phrase instead of just <c>SKS-G815</c>).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex LeadingExecutionUnitPattern = new(
        @"^(?:[A-Z][A-Z0-9]*-G?[0-9]+|G[0-9]+)(?![A-Za-z0-9])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ExecutionUnitFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }
        var match = LeadingExecutionUnitPattern.Match(title.TrimStart());
        return match.Success ? match.Value : string.Empty;
    }

    /// <summary>
    /// G532 review repair: fallback for a title with no corroborated leading
    /// ID token — scans every packet under
    /// <c>.intent-cli/issues/*/packet.yaml</c> and matches the title against
    /// each packet's own declared <c>source_execution_unit</c> (nested
    /// <c>implementation_issue_packet.source_execution_unit</c> first, bare
    /// <c>source_execution_unit</c> as alias) appearing as a whole token
    /// anywhere in the title. Exactly one matching PACKET FILE is required
    /// to corroborate — not merely one distinct declared unit VALUE. Two
    /// packet files that happen to declare the identical
    /// <c>source_execution_unit</c> are two separate, possibly
    /// contradictory (e.g. different declared domains) sources of truth,
    /// and are never collapsed by their string value into one; that is
    /// reported the same as any other multi-match, via
    /// <see cref="ExecutionUnitResolution.IsAmbiguous"/>, naming every
    /// candidate path. Zero matches is simply uncorroborated. A single
    /// packet whose OWN nested field and top-level alias both name the same
    /// unit is still one packet file and is unaffected. Read-only; a
    /// missing or unreadable packet is skipped rather than failing the
    /// whole scan.
    /// </summary>
    private static ExecutionUnitResolution MatchExecutionUnitBySourceExecutionUnit(CliContext context, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new ExecutionUnitResolution(string.Empty, Corroborated: false, IsAmbiguous: false, CandidatePacketPaths: Array.Empty<string>());
        }

        var issuesDir = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesDir))
        {
            return new ExecutionUnitResolution(string.Empty, Corroborated: false, IsAmbiguous: false, CandidatePacketPaths: Array.Empty<string>());
        }

        var matches = new List<(string Unit, string Path)>();
        foreach (var unitDir in Directory.EnumerateDirectories(issuesDir).OrderBy(p => p, StringComparer.Ordinal))
        {
            var packetYamlPath = Path.Combine(unitDir, "packet.yaml");
            if (!File.Exists(packetYamlPath))
            {
                continue;
            }

            IReadOnlyDictionary<string, string> fields;
            try
            {
                fields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetYamlPath));
            }
            catch (FormatException)
            {
                continue;
            }

            var declaredUnit = ReadFirstNonEmpty(
                fields, "implementation_issue_packet.source_execution_unit", "source_execution_unit");
            if (string.IsNullOrWhiteSpace(declaredUnit) || !TitleContainsUnitToken(title, declaredUnit))
            {
                continue;
            }

            matches.Add((declaredUnit, packetYamlPath));
        }

        if (matches.Count == 0)
        {
            return new ExecutionUnitResolution(string.Empty, Corroborated: false, IsAmbiguous: false, CandidatePacketPaths: Array.Empty<string>());
        }

        if (matches.Count == 1)
        {
            return new ExecutionUnitResolution(matches[0].Unit, Corroborated: true, IsAmbiguous: false, CandidatePacketPaths: [matches[0].Path]);
        }

        // Two or more matching PACKET FILES — genuinely ambiguous even if
        // their declared unit strings happen to be identical (a duplicate
        // declaration across files is itself a data-integrity problem, not
        // a corroboration). Fail closed rather than guessing.
        var allPaths = matches.Select(m => m.Path).ToArray();
        return new ExecutionUnitResolution(string.Empty, Corroborated: false, IsAmbiguous: true, CandidatePacketPaths: allPaths);
    }

    /// <summary>
    /// True when <paramref name="unit"/> appears in <paramref name="title"/>
    /// as a whole token — bounded by a non-alphanumeric character (or the
    /// string edge) on both sides — so a short unit like <c>G1</c> never
    /// spuriously matches inside a longer one like <c>G15</c>.
    /// </summary>
    private static bool TitleContainsUnitToken(string title, string unit)
    {
        var searchStart = 0;
        while (true)
        {
            var index = title.IndexOf(unit, searchStart, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var beforeOk = index == 0 || !char.IsLetterOrDigit(title[index - 1]);
            var afterIndex = index + unit.Length;
            var afterOk = afterIndex >= title.Length || !char.IsLetterOrDigit(title[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            searchStart = index + 1;
        }
    }

    private static int ComputeAgeMinutes(string timestamp, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(timestamp)
            || !DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return 0;
        }
        var minutes = (now - parsed).TotalMinutes;
        return minutes > 0 ? (int)minutes : 0;
    }

    /// <summary>
    /// G533: parses <paramref name="primary"/> (typically an entity's own
    /// <c>updatedAt</c>), falling back to <paramref name="fallback"/>
    /// (typically its <c>createdAt</c>) when <paramref name="primary"/> is
    /// missing/unparseable — callers that pre-date the <c>updatedAt</c>
    /// field, or a genuinely empty value, still get a usable "last known
    /// activity" instant rather than an epoch-zero distortion.
    /// </summary>
    private static DateTimeOffset ParseTimestampOrFallback(string primary, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary)
            && DateTimeOffset.TryParse(primary, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedPrimary))
        {
            return parsedPrimary;
        }
        if (!string.IsNullOrWhiteSpace(fallback)
            && DateTimeOffset.TryParse(fallback, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsedFallback))
        {
            return parsedFallback;
        }
        return DateTimeOffset.MinValue;
    }

    private static int ComputeAgeMinutesFromInstant(DateTimeOffset instant, DateTimeOffset now)
    {
        if (instant == DateTimeOffset.MinValue)
        {
            return 0;
        }
        var minutes = (now - instant).TotalMinutes;
        return minutes > 0 ? (int)minutes : 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? repo,
        out int staleMinutes,
        out int claimedSilentMinutes,
        out string format,
        out string error)
    {
        domain = null;
        repo = null;
        staleMinutes = 0;
        claimedSilentMinutes = DefaultClaimedSilentMinutes;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--stale-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedMinutes)
                        || parsedMinutes < 0)
                    {
                        error = "--stale-minutes requires a non-negative integer.";
                        return false;
                    }
                    staleMinutes = parsedMinutes;
                    index++;
                    break;
                case "--claimed-silent-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedSilentMinutes)
                        || parsedSilentMinutes < 0)
                    {
                        error = "--claimed-silent-minutes requires a non-negative integer.";
                        return false;
                    }
                    claimedSilentMinutes = parsedSilentMinutes;
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "automation stalled-work requires '--domain <name>'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation stalled-work requires '--repo <owner/repo>'.";
            return false;
        }
        return true;
    }

    private static void WriteMarkdown(TextWriter writer, AutomationStalledWorkResult result)
    {
        writer.WriteLine($"# automation stalled-work — `{result.Domain}` / `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- stale_minutes_threshold: {result.StaleMinutesThreshold}");
        writer.WriteLine($"- stalled: {(result.Stalled ? "true" : "false")}");
        writer.WriteLine($"- items: {result.Items.Count}");
        writer.WriteLine($"- excluded: {result.Excluded.Count}");
        writer.WriteLine();

        if (result.Items.Count == 0)
        {
            writer.WriteLine("No stalled work detected.");
        }
        else
        {
            foreach (var item in result.Items)
            {
                var kindLabel = item.IsInformational ? $"{item.Kind}, informational" : item.Kind;
                writer.WriteLine($"## `{item.ExecutionUnit}` — {kindLabel} ({item.AgeMinutes}m)");
                if (item.Issue is { } issue)
                {
                    writer.WriteLine($"- issue: #{issue.Number} — {issue.Url}");
                }
                if (item.Pr is { } pr)
                {
                    writer.WriteLine($"- pr: #{pr.Number} — {pr.Url}");
                }
                // G533: informational kinds never recommend a transition —
                // rendered as `status` (descriptive prose) rather than
                // `recommended_action` (always a runnable command) so a
                // reader never mistakes one for the other.
                if (item.IsInformational)
                {
                    writer.WriteLine($"- status: {item.RecommendedAction}");
                }
                else
                {
                    writer.WriteLine($"- recommended_action: `{item.RecommendedAction}`");
                }
                writer.WriteLine();
            }
        }

        if (result.Excluded.Count > 0)
        {
            writer.WriteLine("## Excluded (domain could not be confirmed)");
            foreach (var item in result.Excluded)
            {
                writer.WriteLine($"- `{item.ExecutionUnit}` ({item.Kind}, {item.Reason}): {item.Detail}");
            }
            writer.WriteLine();
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }
}

/// <summary>
/// G532 review repair: outcome of resolving a candidate's execution unit —
/// carries whether that resolution is CORROBORATED by real packet/queue
/// linkage (a matched packet.yaml, or an already-matched queue-state item),
/// since only a corroborated candidate may have its domain confirmed by an
/// explicit <c>--domain</c> alone when its packet is silent on domain (see
/// <see cref="AutomationStalledWorkCommand.TryConfirmDomain"/>).
/// <see cref="IsAmbiguous"/> distinguishes "no packet corroborates this at
/// all" from "more than one distinct packet's declared unit matches" — both
/// leave <see cref="Corroborated"/> false, but warrant different exclusion
/// reasons and diagnostics.
/// </summary>
internal readonly record struct ExecutionUnitResolution(
    string ExecutionUnit,
    bool Corroborated,
    bool IsAmbiguous,
    IReadOnlyList<string> CandidatePacketPaths);

internal sealed record AutomationStalledWorkResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("stale_minutes_threshold")]
    public required int StaleMinutesThreshold { get; init; }

    [JsonPropertyName("stalled")]
    public required bool Stalled { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<StalledWorkItem> Items { get; init; }

    /// <summary>
    /// PR #1148 review repair (G522 domain-isolation boundary): candidates
    /// whose domain could not be confirmed against the candidate's own
    /// packet-declared domain (underivable or contradicting) are reported
    /// here instead of silently joining or silently vanishing from
    /// <see cref="Items"/>.
    /// </summary>
    [JsonPropertyName("excluded")]
    public required IReadOnlyList<StalledWorkExcluded> Excluded { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }
}

internal sealed record StalledWorkItem
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("issue")]
    public StalledWorkRef? Issue { get; init; }

    [JsonPropertyName("pr")]
    public StalledWorkRef? Pr { get; init; }

    [JsonPropertyName("age_minutes")]
    public required int AgeMinutes { get; init; }

    /// <summary>
    /// G533: <see langword="true"/> for <see cref="AutomationStalledWorkCommand.KindRepairPending"/>,
    /// <see cref="AutomationStalledWorkCommand.KindRereviewPending"/>, and
    /// <see cref="AutomationStalledWorkCommand.KindClaimedButSilent"/> —
    /// these kinds carry age for visibility but never recommend a state
    /// transition (<see cref="RecommendedAction"/> is descriptive prose, not
    /// an executable command). <see langword="false"/> for the original
    /// three actionable kinds, where <see cref="RecommendedAction"/> is
    /// always a runnable <c>intent-cli</c> command.
    /// </summary>
    [JsonPropertyName("is_informational")]
    public required bool IsInformational { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }
}

internal sealed record StalledWorkExcluded
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("issue")]
    public StalledWorkRef? Issue { get; init; }

    [JsonPropertyName("pr")]
    public StalledWorkRef? Pr { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("detail")]
    public required string Detail { get; init; }
}

internal sealed record StalledWorkRef
{
    [JsonPropertyName("number")]
    public required int Number { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }
}
