using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Clarify.Models;
using IntentSystem.Clarify.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G523: <c>intent-cli automation stalled-work --domain &lt;d&gt; --repo &lt;r&gt;
/// [--stale-minutes &lt;m&gt;] [--claimed-silent-minutes &lt;m&gt;]
/// [--repair-silent-minutes &lt;m&gt;] [--format json|markdown]</c> — read-only inventory of pending pipeline
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
/// <item><c>ci-all-green-not-transitioned</c> — the same PR lifecycle point,
///   with every reported check terminal and no failed conclusion. Carries
///   the exact head SHA, normalized outcome breakdown, and a stable dedupe
///   key; recommends the existing review-start transition but never runs it.
///   G657 permits the declared <c>intent-pr-created</c> label as a fallback
///   when no exact-head wait exists.</item>
/// <item><c>ci-failed-not-transitioned</c> — every reported check is terminal
///   and at least one failed under a durable wait for the current exact head,
///   with no claimed repair or newer head. Carries the same stable evidence
///   but routes the next action to repair/escalation by ownership rather than
///   review.</item>
/// <item><c>merged-not-closed-out</c> — a MERGED PR's linked queue item is
///   not <see cref="QueueItemState.Completed"/>.</item>
/// <item><c>approved-not-merged</c> — an OPEN PR carries
///   <c>intent-pr-approved</c> past the scan's stale threshold. Reports the
///   age and canonical merge-then-closeout continuation so the last-net
///   heartbeat still detects it when every immediate wake source fails.</item>
/// <item><c>backlog-ready-idle</c> — WIP is empty for the domain (no open PR
///   or <c>intent-target</c> issue confirmed for it, each resolved through
///   its own closing-issue/packet linkage — a confirmed OTHER-domain PR or
///   issue does not block), the canonical selector
///   (<see cref="IntentNextSliceCommand.Analyze"/>) has a publishable
///   candidate, and no runs.jsonl activity for longer than
///   <c>--backlog-idle-minutes</c> (G544 — the last uncovered stall class:
///   work that is ready but never started).</item>
/// <item><c>repair-stalled</c> — a PR in the repair lifecycle
///   (<c>intent-pr-request-update</c>, <c>intent-pr-update-in-progress</c>,
///   or <c>intent-pr-rereview-ready</c>) with no observable activity for
///   longer than <c>--repair-silent-minutes</c> (default
///   <see cref="DefaultRepairSilentMinutes"/> minutes) — G546, promoting the
///   G533 informational repair kinds once, and only once, the silence is
///   long enough to mean a dead worker rather than a repair in progress.
///   Actionable, but the <c>recommended_action</c> is a status check to the
///   responsible thread, NEVER a transition (see
///   <see cref="BuildRepairStalledAction"/>). Covers draft PRs too, which
///   every other PR kind here deliberately skips — the four-day G545 stall
///   was invisible precisely because its repair PR was a draft.</item>
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
/// <item><c>ci-pending</c> — a PR at the pre-review lifecycle point whose exact
///   head still has pending/running checks. This is a legitimate active wait,
///   not a blocker or transition recommendation; its terminal counterpart is
///   deliberately a different kind so a watcher can wake exactly when useful.</item>
/// <item><c>claimed-but-silent</c> — an issue carrying
///   <c>intent-issue-in-progress</c> (no PR yet) with no observable activity
///   for longer than <c>--claimed-silent-minutes</c> (default
///   <see cref="DefaultClaimedSilentMinutes"/> minutes) — the third measured
///   field stall class (silent completion / dead worker after claim).
///   Recommends a worker status check, never assumes completion or
///   failure from silence alone. EXEMPTS a unit whose queue-state item is
///   <see cref="QueueItemState.Blocked"/> — see <c>blocked-label-drift</c>
///   below (G545).</item>
/// <item><c>blocked-label-drift</c> (G545) — a queue-blocked unit
///   (<c>state=blocked</c>) whose GitHub labels have not yet been
///   reconciled onto <see cref="WorkerNextActionConstants.Labels.IntentIssueBlocked"/>
///   — never <c>claimed-but-silent</c>, since the unit is legitimately
///   waiting, not silently stalled. Names the canonical <c>automation
///   issue-block</c> reconcile command.</item>
/// </list>
///
/// G546: <c>repair-pending</c> and <c>rereview-pending</c> stay exactly as
/// described above INSIDE <c>--repair-silent-minutes</c>; past it they are
/// promoted to the actionable <c>repair-stalled</c> kind above.
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
    public const string KindCiPending = "ci-pending";
    public const string KindCiAllGreenNotTransitioned = "ci-all-green-not-transitioned";
    public const string KindCiFailedNotTransitioned = "ci-failed-not-transitioned";
    public const string KindCiHeadMoved = "ci-head-moved";
    public const string KindMergedNotClosedOut = "merged-not-closed-out";
    public const string KindBranchLaneDecisionPending = "branch-lane-decision-pending";
    public const string KindBranchRoutingConflict = "branch-routing-conflict";

    /// <summary>
    /// G596: an explicitly opened durable obligation that only the human
    /// operator can clear. It is actionable, but never orchestrator-actionable.
    /// </summary>
    public const string KindOperatorAttentionPending = "operator-attention-pending";

    /// <summary>
    /// G596: the durable store exists but cannot be trusted. A corrupt store
    /// can never be collapsed into a healthy/no-attention result.
    /// </summary>
    public const string KindOperatorAttentionCannotDetermine = "operator-attention-cannot-determine";

    /// <summary>
    /// G582: an approved PR is an intermediate workflow state, not completion.
    /// This actionable kind closes the last-net gap when an open approved PR
    /// sits past <c>--stale-minutes</c> without any wake source advancing the
    /// canonical merge and closeout continuation.
    /// </summary>
    public const string KindApprovedNotMerged = "approved-not-merged";

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
    /// G545: an issue carrying <c>intent-target</c> + <c>intent-issue-in-progress</c>
    /// (no PR yet) whose queue-state item is <c>state=blocked</c>, but whose
    /// GitHub labels do not yet carry <see
    /// cref="WorkerNextActionConstants.Labels.IntentIssueBlocked"/> — a
    /// transitional GitHub/queue-state mismatch, never a stall: the unit is
    /// legitimately waiting on its recorded <c>blocked_by</c> reason, it
    /// simply hasn't been reconciled onto GitHub yet. Informational;
    /// <c>recommended_action</c> names the canonical <c>automation
    /// issue-block</c> reconcile command. Field finding, 2026-07-21
    /// (sekiban-as-a-service): SKS-G818 and four peers were queue-blocked on
    /// an explicit dependency but reported as <see cref="KindClaimedButSilent"/>
    /// every wake because <c>claimed-but-silent</c> read only GitHub labels.
    /// </summary>
    public const string KindBlockedLabelDrift = "blocked-label-drift";

    /// <summary>
    /// G544: fires when WIP is empty for the requested domain (no open
    /// <c>intent-target</c> issue confirmed for this domain, and no open PR
    /// at all — a PR never carries <c>intent-target</c> itself, so any open
    /// PR is treated as in-flight work), the canonical selector
    /// (<see cref="IntentNextSliceCommand.Analyze"/> — the SAME evaluator
    /// <c>issue publish-flow</c> preflight uses, so this is not a new
    /// heuristic) reports a publishable candidate, and no runs.jsonl
    /// activity has been recorded for longer than
    /// <see cref="DefaultBacklogIdleMinutes"/> (override
    /// <c>--backlog-idle-minutes</c>). Actionable — <c>recommended_action</c>
    /// is the canonical publish command for the named unit. The last
    /// measured field incident (2026-07-20, immediately after the G539
    /// closeout) is exactly this shape: empty WIP, four ready packets,
    /// <c>stalled-work</c> reporting healthy regardless.
    /// </summary>
    public const string KindBacklogReadyIdle = "backlog-ready-idle";

    /// <summary>
    /// G574: a pre-publish queue item whose blocked representation is fully
    /// converged (<c>state=blocked</c> and non-empty <c>blocked_by</c>).
    /// Informational only: parking is intentional, so this kind names the
    /// recorded reason and never recommends publishing or changing state.
    /// </summary>
    public const string KindBlockedParked = "blocked-parked";

    /// <summary>
    /// G574: a queue item whose two blocked fields disagree. This is
    /// actionable drift rather than an intentional park; its recommendation
    /// points at the canonical block/clear surface without choosing the
    /// operator's intended direction.
    /// </summary>
    public const string KindStateDrift = "state-drift";

    /// <summary>
    /// G544: default <c>--backlog-idle-minutes</c> threshold (45 minutes) —
    /// below G523's typical single-message recovery latency, matching
    /// <see cref="AutomationHeartbeatCommand.DefaultStaleMinutes"/>, so a
    /// healthy pipeline that publishes and moves on within its normal
    /// rhythm never trips a false alarm.
    /// </summary>
    public const int DefaultBacklogIdleMinutes = 45;

    /// <summary>
    /// G533: conservative default for <c>--claimed-silent-minutes</c> (12
    /// hours) — chosen so <c>claimed-but-silent</c> does not fire on an
    /// ordinary work session; only a genuinely long silence after claim.
    /// </summary>
    public const int DefaultClaimedSilentMinutes = 720;

    /// <summary>
    /// G546: a PR in the repair lifecycle (<c>intent-pr-request-update</c>,
    /// <c>intent-pr-update-in-progress</c>, or <c>intent-pr-rereview-ready</c>)
    /// with no observable activity for longer than
    /// <see cref="DefaultRepairSilentMinutes"/> (override
    /// <c>--repair-silent-minutes</c>). ACTIONABLE — but the recommendation is
    /// always a status check to the responsible thread, never a transition:
    /// the transition owner is unchanged, and silence alone never establishes
    /// that a repair succeeded, failed, or should be taken away from its
    /// current owner.
    ///
    /// G533 deliberately left <see cref="KindRepairPending"/> /
    /// <see cref="KindRereviewPending"/> thresholdless pending field data.
    /// The data arrived twice: a G545 repair claimed
    /// <c>intent-pr-update-in-progress</c> went silent for FOUR DAYS
    /// (2026-07-23 → 07-27) after the implement session died, and a G538 PR
    /// sat <c>intent-pr-rereview-ready</c> for 105 minutes
    /// (2026-07-20) — both recovered only by a manual ping. The G545 case is
    /// the sharper one: its PR was a DRAFT, and
    /// <see cref="CollectPrCreatedNotReviewing"/> skips draft PRs outright, so
    /// <c>stalled-work</c> reported <c>stalled=false, items=[]</c> throughout.
    /// A dead worker mid-repair was the only measured stall class the detector
    /// could not see at all; this kind closes it on both the draft and
    /// non-draft paths.
    /// </summary>
    public const string KindRepairStalled = "repair-stalled";

    /// <summary>
    /// G546: conservative default for <c>--repair-silent-minutes</c> (3
    /// hours). A repair has a well-defined observable footprint (new head
    /// commits, PR comments, label changes — GitHub bumps the PR's
    /// <c>updatedAt</c> on every one of them), so a long-but-not-absurd
    /// threshold catches a multi-hour worker death without flagging a repair
    /// that is merely mid-thought. Both measured field incidents exceed it
    /// (105 minutes is under it by design — G538 was recovered by a ping
    /// before it became a genuine stall; the four-day G545 case exceeds it
    /// by more than thirtyfold).
    /// </summary>
    public const int DefaultRepairSilentMinutes = 180;

    /// <summary>
    /// G532 review repair: a title matched more than one distinct packet's
    /// declared <c>source_execution_unit</c> as a token — the execution unit
    /// cannot be picked without guessing, so it is reported here instead of
    /// silently choosing the longest (or first-sorted) match.
    /// </summary>
    public const string ReasonExecutionUnitAmbiguous = "execution-unit-ambiguous";

    /// <summary>
    /// G533 review repair: <see cref="KindClaimedButSilent"/>'s own
    /// last-activity timestamp (the issue's own <c>updatedAt</c>, or a
    /// linked open PR's <c>updatedAt</c>) is missing or malformed —
    /// silence can never be reliably established, so the candidate is
    /// excluded rather than substituting a different field (e.g.
    /// <c>createdAt</c>) that measures a different event entirely.
    /// </summary>
    public const string ReasonActivityDataUnusable = "activity-data-unusable";

    /// <summary>
    /// G552: an open clarification artifact is on disk but cannot be read or
    /// deserialized. The hold it represents is real — that is exactly why it
    /// must never be dropped silently — so the artifact is reported here with
    /// its path rather than skipped, and never guessed into
    /// <see cref="StalledWorkItem"/>s on unusable evidence.
    /// </summary>
    public const string ReasonClarificationUnreadable = "clarification-unreadable";

    /// <summary>
    /// G552: a hold blocked on a DESIGN DECISION, recorded as an open
    /// clarification artifact through the canonical clarify surface. Reports
    /// the blocking execution unit, the clarification's age, and its question
    /// summary; <c>recommended_action</c> names the exact clarification to
    /// answer (design) or the escalation path (operator). ACTIONABLE — like
    /// <see cref="KindRepairStalled"/>, the recommendation is a directed
    /// request rather than a state transition this command could run itself:
    /// the answer is human content, and nothing here ever auto-answers a
    /// clarification.
    ///
    /// Field incident (2026-07-28 16:11 → 07-29 01:29): the G551 review held
    /// its final verdict for NINE HOURS on a one-line wording ruling while
    /// every technical check was green. The hold lived only in agmsg
    /// messages, so <c>stalled-work</c> reported <c>stalled=false</c>
    /// throughout and no supervision layer could see it — the fourth
    /// design-absence stall in the field record. This kind is the detection
    /// half of the fix; the guide's clarification-backed hold rule (an
    /// agmsg-only hold is a contract violation) is what puts the artifact on
    /// disk for it to read.
    /// </summary>
    public const string KindDesignDecisionPending = "design-decision-pending";

    /// <summary>
    /// G564: a CLOSED-OUT unit whose packet DECLARED a knowledge write-back
    /// (<c>knowledge_updates.*.required</c> or
    /// <c>closeout_learning.write_back_required</c>) with no
    /// <see cref="KnowledgeWriteBackRecord"/> on disk — the promised intent-tree
    /// / ADR / diagram / docs update either did not happen or was never
    /// recorded, and until it is recorded the two are indistinguishable.
    /// ACTIONABLE: <c>recommended_action</c> names the recording command and
    /// the declared target paths. Nothing here writes intent content — the
    /// write-back is design's host-side act (G300); this kind only makes its
    /// absence visible and aging.
    ///
    /// Field evidence (pre-v0.7.0 audit, 2026-07-31): G559 shipped while node
    /// 09 still described a pre-implementation design; node 02 recorded none of
    /// the seven release-flow rules the docs implement; node 08 lagged the wake
    /// contract by releases. Weeks of drift with NO structural signal — a
    /// manual, operator-ordered audit was the only detector, and it cost a full
    /// review cycle immediately before a release. The ingredients already
    /// existed (packets declare the obligation; write-backs happen as host
    /// commits) but nothing said "done", so nothing could say "not done".
    /// </summary>
    public const string KindKnowledgeWritebackPending = "knowledge-writeback-pending";

    /// <summary>
    /// G661: the record exists locally but git still reports its exact path as
    /// untracked, ignored, staged, or modified. This is distinct from an
    /// absent record: the write-back was recorded, but another checkout cannot
    /// observe it until the operator commits and pushes it.
    /// </summary>
    public const string KindKnowledgeWritebackRecordedUncommitted =
        "knowledge-writeback-recorded-uncommitted";

    /// <summary>
    /// G645: a closed-out unit whose packet declared guide routes but whose
    /// host has not recorded the route update. This is closeout debt, never a
    /// merge gate, and the report names the declared guide and role.
    /// </summary>
    public const string KindGuideReachabilityPending = "guide-reachability-pending";

    /// <summary>
    /// G564: a closed-out unit's write-back metadata — its packet's G461
    /// declaration, the runs log the closeout is read from, or an existing
    /// write-back record — is present but unreadable. Reported here WITH the
    /// path rather than skipped: unreadable evidence is not evidence of a
    /// cleared obligation, and silently downgrading it to "nothing pending" is
    /// exactly the false all-clear this kind exists to prevent.
    /// </summary>
    public const string ReasonKnowledgeMetadataUnreadable = "knowledge-metadata-unreadable";

    /// <summary>G645: a packet carries no readable guide-reachability declaration.</summary>
    public const string ReasonGuideReachabilityDeclarationMissing = "guide-reachability-declaration-missing";

    /// <summary>G645: a declared route or record could not be read safely.</summary>
    public const string ReasonGuideReachabilityMetadataUnreadable = "guide-reachability-metadata-unreadable";

    /// <summary>
    /// G645: closeouts before the reachability detector shipped are outside its
    /// default scan. Operators may opt into older closeouts explicitly.
    /// </summary>
    public static readonly DateTimeOffset GuideReachabilityActivationUtc =
        new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// G564: units closed out BEFORE this detection shipped are out of scope
    /// (the pre-v0.7.0 backlog was cleared by the 2026-07-31 operator audit),
    /// so the scan has a floor: a closeout recorded before this instant never
    /// produces an item. Override with
    /// <c>--knowledge-writeback-since &lt;iso-8601&gt;</c> to scan further back
    /// deliberately — retroactive detection is a choice the operator makes, not
    /// a default that lights up every historical unit on the first wake after
    /// an upgrade.
    /// </summary>
    public static readonly DateTimeOffset KnowledgeWriteBackActivationUtc =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>G564: the closeout event this detection reads as "the unit is closed out".</summary>
    private const string CloseoutRecordedEvent = "closeout-recorded";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation stalled-work --domain <name> --repo <owner/repo> [--stale-minutes <m>] [--claimed-silent-minutes <m>] [--backlog-idle-minutes <m>] [--repair-silent-minutes <m>] [--knowledge-writeback-since <iso-8601>] [--guide-reachability-since <iso-8601>] [--format json|markdown]";

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

        if (!TryParseArguments(args, out var domain, out var repo, out var staleMinutes, out var claimedSilentMinutes, out var backlogIdleMinutes, out var repairSilentMinutes, out var knowledgeWriteBackSince, out var guideReachabilitySince, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        AutomationStalledWorkResult result;
        try
        {
            result = Analyze(context, domain!, repo!, staleMinutes, claimedSilentMinutes, backlogIdleMinutes, repairSilentMinutes, knowledgeWriteBackSince, guideReachabilitySince);
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
    /// <see cref="DefaultClaimedSilentMinutes"/> and <paramref
    /// name="backlogIdleMinutes"/> defaults to
    /// <see cref="DefaultBacklogIdleMinutes"/>, so existing callers (e.g.
    /// <c>automation heartbeat</c>) keep compiling and get the conservative
    /// defaults without needing their own override plumbing.
    /// </summary>
    public static AutomationStalledWorkResult Analyze(
        CliContext context,
        string domain,
        string repo,
        int staleMinutes,
        int claimedSilentMinutes = DefaultClaimedSilentMinutes,
        int backlogIdleMinutes = DefaultBacklogIdleMinutes,
        int repairSilentMinutes = DefaultRepairSilentMinutes,
        DateTimeOffset? knowledgeWriteBackSince = null,
        DateTimeOffset? guideReachabilitySince = null)
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
        var branchLaneQueueState = TryLoadQueueStateForBranchLaneRouting(context, domain, repo, warnings);
        var closedPrs = branchLaneQueueState?.Items.Any(item => item.RoutingSnapshot is not null) == true
            ? lister.ListClosedPullRequests(repo, Array.Empty<string>())
            : Array.Empty<GitHubAutomationPrCandidate>();
        CollectBranchLaneDecisionPending(
            context,
            domain,
            candidateDomains,
            repo,
            now,
            branchLaneQueueState,
            items,
            excluded);
        CollectBranchRoutingConflicts(
            context,
            domain,
            candidateDomains,
            repo,
            openIssues,
            openPrs.Concat(mergedPrs).Concat(closedPrs).ToArray(),
            branchLaneQueueState,
            items,
            excluded);
        CollectApprovedNotMerged(context, domain, candidateDomains, openIssues, openPrs, repo, now, items, excluded, warnings);
        var ciWaitRead = CiWaitStore.ReadOpen(context.RepoRoot, domain, repo);
        if (ciWaitRead.Error is not null)
        {
            warnings.Add($"durable CI wait store could not be read at '{ciWaitRead.Path}': {ciWaitRead.Error}");
        }

        CollectPrCreatedNotReviewing(context, domain, candidateDomains, openIssues, openPrs, repo, now, repairSilentMinutes, ciWaitRead.Records, items, excluded);
        CollectDraftRepairStalled(context, domain, candidateDomains, openIssues, openPrs, repo, now, repairSilentMinutes, items, excluded);
        CollectMergedNotClosedOut(context, domain, candidateDomains, repo, mergedPrs, now, items, excluded, warnings);
        CollectClaimedButSilent(context, domain, candidateDomains, openIssues, openPrs, repo, now, claimedSilentMinutes, items, excluded, warnings);
        CollectBacklogReadyIdle(context, domain, candidateDomains, openIssues, openPrs, repo, now, backlogIdleMinutes, items, excluded);
        CollectDesignDecisionPending(context, domain, candidateDomains, repo, now, items, excluded);
        CollectKnowledgeWritebackPending(
            context,
            domain,
            candidateDomains,
            repo,
            now,
            knowledgeWriteBackSince ?? KnowledgeWriteBackActivationUtc,
            items,
            excluded);
        CollectGuideReachabilityPending(
            context,
            domain,
            candidateDomains,
            repo,
            now,
            guideReachabilitySince ?? GuideReachabilityActivationUtc,
            items,
            excluded,
            warnings);

        var operatorAttention = CollectOperatorAttention(context, domain, now, items);

        // G670: knowledge/guide/operator collectors intentionally run after
        // backlog-ready-idle. Reconcile only the exact G670 preview records
        // once every official collector has contributed, so a later item or
        // exclusion cannot leave a placeholder explanation alongside the
        // actual stalled-work finding.
        ReconcileG670ContractIncompleteExclusions(items, excluded);

        var filtered = items
            // G596: explicit human obligations and unreadable human-obligation
            // state are load-bearing immediately; they do not wait for the
            // generic GitHub staleness threshold before becoming visible.
            .Where(item => item.Kind is KindOperatorAttentionPending or KindOperatorAttentionCannotDetermine
                or KindCiPending or KindCiAllGreenNotTransitioned or KindCiFailedNotTransitioned or KindCiHeadMoved
                or KindBranchRoutingConflict
                || item.AgeMinutes >= staleMinutes)
            .OrderByDescending(item => item.AgeMinutes)
            .ToArray();

        return new AutomationStalledWorkResult
        {
            Domain = domain,
            Repo = repo,
            StaleMinutesThreshold = staleMinutes,
            BacklogIdleMinutesThreshold = backlogIdleMinutes,
            // G589: a still-pending CI item remains visible, but it must not
            // by itself trip a heartbeat/watcher wake. The kind changes when
            // the exact head becomes terminal, and that terminal item is the
            // dedupe-ready actionable signal. Other historical informational
            // kinds retain their established stalled semantics.
            Stalled = filtered.Any(item => item.Kind != KindCiPending),
            Items = filtered,
            Excluded = excluded,
            Warnings = warnings,
            // A host that has never used the new lifecycle retains the exact
            // pre-G596 stalled-work shape. Its independent query still says
            // check-not-completed; once a store exists (or is corrupt), this
            // scanner emits the load-bearing status.
            OperatorAttentionStatus = operatorAttention.Status == OperatorAttentionReadStatus.Missing
                ? null
                : operatorAttention.Status,
            OperatorAttentionError = operatorAttention.Status == OperatorAttentionReadStatus.Missing
                ? null
                : operatorAttention.Error,
        };
    }

    /// <summary>
    /// G670: keeps a backlog-ready-idle contract-incomplete exclusion only
    /// when it is the sole explanation/action lane. The detail-shape check is
    /// deliberate: <see cref="KindBacklogReadyIdle"/> and the shared reason
    /// are existing public fields, so the source wording distinguishes this
    /// preview from any future contract-incomplete exclusion using the same
    /// kind/reason pair for another purpose.
    /// </summary>
    private static void ReconcileG670ContractIncompleteExclusions(
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        if (!excluded.Any(IsG670ContractIncompleteExclusion))
        {
            return;
        }

        var hasOtherItem = items.Count > 0;
        var hasOtherExclusion = excluded.Any(candidate => !IsG670ContractIncompleteExclusion(candidate));
        if (!hasOtherItem && !hasOtherExclusion)
        {
            return;
        }

        excluded.RemoveAll(IsG670ContractIncompleteExclusion);
    }

    private static bool IsG670ContractIncompleteExclusion(StalledWorkExcluded candidate) =>
        string.Equals(candidate.Kind, KindBacklogReadyIdle, StringComparison.Ordinal)
        && string.Equals(candidate.Reason, NextSliceReadinessClass.ContractIncomplete, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(candidate.ExecutionUnit)
        && candidate.Detail.Contains(
            "was excluded from backlog-ready-idle candidacy by the shared publish gate:",
            StringComparison.Ordinal)
        && candidate.Detail.Contains("no publish action is emitted for this unit.", StringComparison.Ordinal);

    private static OperatorAttentionReadResult CollectOperatorAttention(
        CliContext context,
        string domain,
        DateTimeOffset now,
        List<StalledWorkItem> items)
    {
        var read = OperatorAttentionStore.Read(context.RepoRoot);
        if (read.Status == OperatorAttentionReadStatus.CannotDetermine)
        {
            items.Add(new StalledWorkItem
            {
                Kind = KindOperatorAttentionCannotDetermine,
                ExecutionUnit = "operator-attention-store",
                Issue = null,
                Pr = null,
                AgeMinutes = 0,
                IsInformational = false,
                RecommendedAction = read.Error!,
                RequiredActor = "operator",
                OrchestratorActionable = false,
                OperatorAttentionRecordId = null,
                OperatorAttentionOwner = "operator",
                BlockingReference = OperatorAttentionStore.RelativePath,
                DedupeKey = $"operator-attention:cannot-determine:{domain}",
            });
            return read;
        }

        if (read.Status != OperatorAttentionReadStatus.Readable)
        {
            // Missing is deliberately exposed as check-not-completed on the
            // result, but it is not invented into an open obligation. Only an
            // explicit `judgment-wait open --write` may create one.
            return read;
        }

        var domainRecords = read.Document!.Records
            .Where(record => string.Equals(record.Domain, domain, StringComparison.Ordinal))
            .ToArray();
        var openRecords = domainRecords
            .Where(record => string.Equals(record.Status, OperatorAttentionStatus.Open, StringComparison.Ordinal))
            .ToArray();
        foreach (var record in openRecords)
        {
            items.Add(new StalledWorkItem
            {
                Kind = KindOperatorAttentionPending,
                ExecutionUnit = record.RecordId,
                Issue = null,
                Pr = null,
                AgeMinutes = Math.Max(0, (int)Math.Floor((now - record.OpenedAt).TotalMinutes)),
                IsInformational = false,
                RecommendedAction = record.ActionNeeded,
                // G599: the compatibility-named lifecycle records an owner;
                // this projection must not replace it with a literal.
                RequiredActor = record.Owner,
                OrchestratorActionable = false,
                OperatorAttentionRecordId = record.RecordId,
                OperatorAttentionOwner = record.Owner,
                BlockingReference = record.BlockingReference,
                DedupeKey = $"operator-attention:{record.Domain}:{record.Team}:{record.Owner}:{record.RecordId}",
            });
        }

        return read with
        {
            Status = openRecords.Length > 0
                ? OperatorAttentionQueryStatus.AttentionPending
                : domainRecords.Length > 0
                    ? OperatorAttentionQueryStatus.NoAttentionPending
                    : OperatorAttentionQueryStatus.CheckNotCompleted,
        };
    }

    private static QueueState? TryLoadQueueStateForBranchLaneRouting(
        CliContext context,
        string domain,
        string repo,
        List<string> warnings)
    {
        var location = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(context.RepoRoot, domain, repo);
        if (!File.Exists(location.Path))
        {
            return null;
        }

        try
        {
            return QueueStateSerializer.Deserialize(File.ReadAllText(location.Path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            warnings.Add($"queue-state at '{location.Path}' could not be parsed; skipped branch-lane routing findings: {exception.Message}");
            return null;
        }
    }

    private static void CollectBranchLaneDecisionPending(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        DateTimeOffset now,
        QueueState? queueState,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        if (queueState is null)
        {
            return;
        }

        foreach (var queueItem in queueState.Items.Where(item =>
                     item.State == QueueItemState.Queued && item.RoutingSnapshot is not null))
        {
            if (!TryReadLanePacketSnapshot(context, queueItem.ExecutionUnit, out _))
            {
                continue;
            }

            var packetDomain = ReadPacketDeclaredDomain(context, queueItem.ExecutionUnit);
            if (!TryConfirmDomain(
                    domain,
                    new ExecutionUnitResolution(queueItem.ExecutionUnit, true, false, Array.Empty<string>()),
                    packetDomain,
                    candidateDomains,
                    repo,
                    out var reason,
                    out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindBranchLaneDecisionPending,
                    ExecutionUnit = queueItem.ExecutionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            var gate = BranchLaneDecisionGate.Evaluate(context.RepoRoot, queueItem.ExecutionUnit);
            if (gate.Passed)
            {
                continue;
            }

            var propose = BranchLaneDecisionStore.ReadPropose(context.RepoRoot, queueItem.ExecutionUnit);
            var confirm = BranchLaneDecisionStore.ReadConfirm(context.RepoRoot, queueItem.ExecutionUnit);
            var ageSource = propose.Record?.RecordedAt ?? queueState.UpdatedAt;
            var ageMinutes = ComputeAgeMinutesFromInstant(ClampToNow(ageSource, now), now);
            var missing = propose.Record is null
                ? "propose and confirm"
                : confirm.Record is null
                    ? "confirm"
                    : "valid propose/confirm pair";

            items.Add(new StalledWorkItem
            {
                Kind = KindBranchLaneDecisionPending,
                ExecutionUnit = queueItem.ExecutionUnit,
                Issue = queueItem.LinkedIssue is { Number: int issueNumber } linkedIssue
                    ? new StalledWorkRef
                    {
                        Number = issueNumber,
                        Url = linkedIssue.Url ?? string.Empty,
                    }
                    : null,
                Pr = null,
                AgeMinutes = ageMinutes,
                IsInformational = false,
                RecommendedAction =
                    $"record the missing {missing} lane decision for {queueItem.ExecutionUnit}; "
                    + "confirmation must be an independent orchestration judgment with actor, timestamp, evidence, and fingerprint. "
                    + $"Gate detail: {gate.Error}",
                RequiredActor = propose.Record is null ? "design" : "orchestration",
                OrchestratorActionable = false,
                BlockingReference = BranchLaneDecisionStore.ResolveRelativePath(queueItem.ExecutionUnit, true),
                DedupeKey = $"branch-lane-decision-pending:{queueItem.ExecutionUnit}",
            });
        }
    }

    private static void CollectBranchRoutingConflicts(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        IReadOnlyList<GitHubAutomationIssueCandidate> issues,
        IReadOnlyList<GitHubAutomationPrCandidate> prs,
        QueueState? queueState,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        if (queueState is null)
        {
            return;
        }

        var distinctPrs = prs
            .Where(pr => pr.Number > 0)
            .GroupBy(pr => pr.Number)
            .Select(group => group.First())
            .ToArray();

        foreach (var queueItem in queueState.Items.Where(item => item.RoutingSnapshot is not null))
        {
            if (!TryReadLanePacketSnapshot(context, queueItem.ExecutionUnit, out var packetSnapshot))
            {
                continue;
            }

            var packetDomain = ReadPacketDeclaredDomain(context, queueItem.ExecutionUnit);
            if (!TryConfirmDomain(
                    domain,
                    new ExecutionUnitResolution(queueItem.ExecutionUnit, true, false, Array.Empty<string>()),
                    packetDomain,
                    candidateDomains,
                    repo,
                    out var reason,
                    out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindBranchRoutingConflict,
                    ExecutionUnit = queueItem.ExecutionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            var linkedIssueNumber = queueItem.LinkedIssue is { Number: int queueIssueNumber }
                && string.Equals(queueItem.LinkedIssue.Repo, repo, StringComparison.OrdinalIgnoreCase)
                ? queueIssueNumber
                : (int?)null;
            var issue = linkedIssueNumber is int knownIssue
                ? issues.FirstOrDefault(candidate => candidate.Number == knownIssue && IsOpen(candidate.State))
                : issues.FirstOrDefault(candidate =>
                    IsOpen(candidate.State)
                    && ResolveExecutionUnit(context, candidate.Title).ExecutionUnit == queueItem.ExecutionUnit);

            var pr = distinctPrs.FirstOrDefault(candidate =>
                issue is not null
                    && candidate.ClosingIssuesReferences.Any(reference =>
                        reference.Number == issue.Number && ReferenceMatchesRepo(reference, repo)));
            if (pr is null && queueItem.LinkedPr is not null)
            {
                pr = distinctPrs.FirstOrDefault(candidate =>
                    MatchesLinkedPr(queueItem, repo, candidate.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            AddRoutingSnapshotValues(values, "packet", packetSnapshot);
            AddRoutingSnapshotValues(values, "queue", queueItem.RoutingSnapshot!);
            if (issue is not null)
            {
                foreach (var pair in ReadIssueRoutingValues(issue.Body))
                {
                    values[$"issue.{pair.Key}"] = pair.Value;
                }
            }
            if (pr is not null && !string.IsNullOrWhiteSpace(pr.BaseRefName))
            {
                values["pr.pr_base_branch"] = pr.BaseRefName;
            }

            if (!HasRoutingConflict(values))
            {
                continue;
            }

            var issueRef = issue is null
                ? null
                : new StalledWorkRef { Number = issue.Number, Url = issue.Url };
            var prRef = pr is null
                ? null
                : new StalledWorkRef { Number = pr.Number, Url = pr.Url };

            items.Add(new StalledWorkItem
            {
                Kind = KindBranchRoutingConflict,
                ExecutionUnit = queueItem.ExecutionUnit,
                Issue = issueRef,
                Pr = prRef,
                AgeMinutes = 0,
                IsInformational = false,
                RecommendedAction =
                    $"resolve branch routing conflict for {queueItem.ExecutionUnit} before publish; "
                    + string.Join(", ", values.Select(pair => $"{pair.Key}={pair.Value}")),
                RequiredActor = "orchestration",
                OrchestratorActionable = false,
                RoutingValues = values,
                DedupeKey = $"branch-routing-conflict:{queueItem.ExecutionUnit}",
            });
        }
    }

    private static bool TryReadLanePacketSnapshot(
        CliContext context,
        string executionUnit,
        out BranchRoutingSnapshot snapshot)
    {
        snapshot = default!;
        var path = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit, "packet.yaml");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            if (!PacketYamlDocument.TryParse(File.ReadAllText(path), out var document, out _)
                || document is null
                || string.IsNullOrWhiteSpace(BranchLaneResolver.TryReadDeclaredLane(document.Fields)))
            {
                return false;
            }

            snapshot = BranchLaneResolver.TryReadSnapshot(document.Fields)
                ?? throw new InvalidOperationException("lane-declaring packet has no complete routing snapshot.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void AddRoutingSnapshotValues(
        IDictionary<string, string> values,
        string source,
        BranchRoutingSnapshot snapshot)
    {
        values[$"{source}.lane_id"] = snapshot.LaneId;
        values[$"{source}.definition_revision"] = snapshot.DefinitionRevision;
        values[$"{source}.start_branch"] = snapshot.StartBranch;
        values[$"{source}.pr_base_branch"] = snapshot.PrBaseBranch;
        values[$"{source}.landing_mode"] = snapshot.LandingMode;
    }

    private static void AddRoutingSnapshotValues(
        IDictionary<string, string> values,
        string source,
        QueueRoutingSnapshot snapshot)
    {
        values[$"{source}.lane_id"] = snapshot.LaneId;
        values[$"{source}.definition_revision"] = snapshot.DefinitionRevision;
        values[$"{source}.start_branch"] = snapshot.StartBranch;
        values[$"{source}.pr_base_branch"] = snapshot.PrBaseBranch;
        values[$"{source}.landing_mode"] = snapshot.LandingMode;
    }

    private static bool HasRoutingConflict(IReadOnlyDictionary<string, string> values)
    {
        return values
            .GroupBy(pair => pair.Key[(pair.Key.IndexOf('.', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal)
            .Any(group => group
                .Select(pair => pair.Value)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any());
    }

    private static IReadOnlyDictionary<string, string> ReadIssueRoutingValues(string? body)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(body))
        {
            return values;
        }

        AddIssueValue(values, body, "lane_id", "Lane");
        AddIssueValue(values, body, "definition_revision", "Registry definition revision");
        AddIssueValue(values, body, "start_branch", "Start branch");
        AddIssueValue(values, body, "pr_base_branch", "Expected PR base branch");
        AddIssueValue(values, body, "landing_mode", "Landing mode");
        return values;
    }

    private static void AddIssueValue(
        IDictionary<string, string> values,
        string body,
        string key,
        string label)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            body,
            $@"(?im)^\s*(?:[-*]\s*)?{System.Text.RegularExpressions.Regex.Escape(label)}\s*:\s*[\x60]?(?<value>[A-Za-z0-9._/-]+)[\x60]?\s*$");
        if (match.Success)
        {
            values[key] = match.Groups["value"].Value;
        }
    }

    /// <summary>
    /// G582 F5: detects the intermediate approved-but-unmerged state using the
    /// existing global stale threshold. The collector is deliberately silent
    /// for non-open PRs, a draft that is simultaneously back in request-update,
    /// and a source unit blocked either on GitHub or in canonical queue-state.
    /// </summary>
    private static void CollectApprovedNotMerged(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded,
        List<string> warnings)
    {
        var queueState = TryLoadQueueStateForApprovedNotMerged(context, domain, repo, warnings);

        foreach (var pr in openPrs)
        {
            if (!IsOpen(pr.State))
            {
                continue;
            }

            var prLabels = LabelSet(pr.Labels);
            if (!prLabels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrApproved))
            {
                continue;
            }

            // A draft handed back for updates is a repair wait, not a merge
            // continuation, even if a stale approved label is still present.
            if (pr.IsDraft
                && prLabels.Contains(WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate))
            {
                continue;
            }

            var matchedIssue = pr.ClosingIssuesReferences
                .Where(reference => reference.Number > 0 && ReferenceMatchesRepo(reference, repo))
                .Select(reference => openIssues.FirstOrDefault(issue =>
                    issue.Number == reference.Number && IsOpen(issue.State)))
                .FirstOrDefault(issue => issue is not null);
            if (matchedIssue is null)
            {
                continue;
            }

            var issueLabels = LabelSet(matchedIssue.Labels);
            if (issueLabels.Contains(WorkerNextActionConstants.Labels.IntentIssueBlocked))
            {
                continue;
            }

            var resolution = ResolveExecutionUnit(context, matchedIssue.Title);
            if (queueState?.Items.FirstOrDefault(candidate =>
                    string.Equals(candidate.ExecutionUnit, resolution.ExecutionUnit, StringComparison.Ordinal))
                is { State: QueueItemState.Blocked })
            {
                continue;
            }

            var packetDeclaredDomain = resolution.Corroborated
                ? ReadPacketDeclaredDomain(context, resolution.ExecutionUnit)
                : null;
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindApprovedNotMerged,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                    Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindApprovedNotMerged,
                ExecutionUnit = resolution.ExecutionUnit,
                Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                // GitHub updates the PR when the approved label is applied;
                // this is the same conservative label-age proxy used by the
                // other post-creation lifecycle kinds.
                AgeMinutes = ComputeAgeMinutes(pr.UpdatedAt, now),
                IsInformational = false,
                RecommendedAction =
                    $"canonical merge/closeout: merge PR #{pr.Number} through the repository-approved merge "
                    + "operation, verify merged == true, then run "
                    + $"intent-cli closeout pr --pr {pr.Number} --repo {repo} --domain {domain} "
                    + "--pr-merged true --write --format json",
            });
        }
    }

    private static QueueState? TryLoadQueueStateForApprovedNotMerged(
        CliContext context, string domain, string repo, List<string> warnings)
    {
        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(context.RepoRoot, domain, repo);
        if (!File.Exists(queueStateLocation.Path))
        {
            return null;
        }

        try
        {
            return QueueStateSerializer.Deserialize(File.ReadAllText(queueStateLocation.Path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            warnings.Add($"queue-state at '{queueStateLocation.Path}' could not be parsed: {exception.Message}; skipped the approved-not-merged blocked exemption.");
            return null;
        }
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
        int repairSilentMinutes,
        IReadOnlyList<CiWaitRecord> ciWaits,
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
            StalledWorkCiProjection? ci = null;
            var ciWait = ciWaits.FirstOrDefault(wait => wait.Pr == pr.Number
                && string.Equals(wait.Repo, repo, StringComparison.OrdinalIgnoreCase));
            var exactHeadCiWait = ciWait is not null
                && string.Equals(ciWait.ObservedHead, pr.HeadRefOid, StringComparison.OrdinalIgnoreCase);
            var observedHead = exactHeadCiWait ? ciWait!.ObservedHead : null;
            var owedTransition = exactHeadCiWait ? ciWait!.OwedTransition : null;
            var ciWaitState = exactHeadCiWait ? "pending" : null;
            string? ciClassificationSource = null;
            var currentHead = pr.HeadRefOid;
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
                ci = ProjectCiState(pr);
                if (ciWait is not null && !exactHeadCiWait
                    && ci is not { Outcome: StalledWorkCiOutcomes.AllGreen })
                {
                    // A new head is evidence that repair is advancing. The
                    // old exact-head result cannot classify the new head and
                    // supervision remains silent until that head is observed.
                    continue;
                }
                else
                {
                    if (ci is { Outcome: StalledWorkCiOutcomes.Pending })
                    {
                        kind = KindCiPending;
                        isInformational = true;
                        recommendedAction =
                            $"none — CI for PR #{pr.Number} head {pr.HeadRefOid} is still pending; keep it as an active "
                            + "wait and let the mode-specific CI completion wake producer re-check the exact head.";
                    }
                    else if (ci is { Outcome: StalledWorkCiOutcomes.AllGreen })
                    {
                        kind = KindCiAllGreenNotTransitioned;
                        isInformational = false;
                        var transition = owedTransition ?? "review-start";
                        recommendedAction =
                            $"intent-cli automation pr-transition --repo {repo} --pr {pr.Number} --transition {transition} --write";
                        ciWaitState = exactHeadCiWait ? "terminal" : null;
                        ciClassificationSource = exactHeadCiWait
                            ? "ci-wait-record"
                            : "declared-label-fallback";
                    }
                    else if (ci is { Outcome: StalledWorkCiOutcomes.Failed })
                    {
                        if (!exactHeadCiWait)
                        {
                            // Settled-red is actionable only when a durable
                            // exact-head wait proves this is an owed workflow
                            // transition rather than an unclaimed observation.
                            continue;
                        }
                        kind = KindCiFailedNotTransitioned;
                        isInformational = false;
                        recommendedAction =
                            $"inspect failed checks for PR #{pr.Number} at head {pr.HeadRefOid}; route branch-owned "
                            + "failures to implementation repair and product/design or canonical failures to escalation; "
                            + "do not start review.";
                        ciWaitState = "terminal";
                        ciClassificationSource = "ci-wait-record";
                    }
                    else
                    {
                        kind = KindPrCreatedNotReviewing;
                        isInformational = false;
                        recommendedAction =
                            $"intent-cli automation pr-transition --repo {repo} --pr {pr.Number} --transition review-start --write";
                    }
                }
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

            // G533 review repair: repair-pending/rereview-pending age uses
            // the PR's own `updatedAt` rather than `createdAt` — these are
            // POST-creation lifecycle states, so "how long has this been
            // pending" should mean "since entering this state", not "since
            // the PR was opened". `updatedAt` is only a CONSERVATIVE
            // approximation of that, though, not the exact label-application
            // moment: GitHub does not expose per-label-application
            // timestamps, and `updatedAt` reflects the PR's most recent
            // modification of ANY kind (which may postdate the specific
            // label change) unless a dedicated label-event fetch is added.
            var isRepairLifecycle = kind is KindRepairPending or KindRereviewPending;
            var ageSource = isRepairLifecycle
                ? pr.UpdatedAt
                : exactHeadCiWait ? ciWait!.RecordedAt.ToString("O") : pr.CreatedAt;
            var ageMinutes = ComputeAgeMinutes(ageSource, now);

            // G546: promote a repair-lifecycle PR that has been observably
            // silent past the threshold. Inside the threshold — and whenever
            // the silence cannot be established at all — the G533
            // informational item below is emitted completely unchanged.
            if (isRepairLifecycle
                && TryPromoteToRepairStalled(pr, prLabels, repo, now, repairSilentMinutes, out var promotedAction, out var silentMinutes))
            {
                kind = KindRepairStalled;
                isInformational = false;
                recommendedAction = promotedAction;
                ageMinutes = silentMinutes;
            }

            items.Add(new StalledWorkItem
            {
                Kind = kind,
                ExecutionUnit = resolution.ExecutionUnit,
                Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                AgeMinutes = ageMinutes,
                IsInformational = isInformational,
                RecommendedAction = recommendedAction,
                PrHeadSha = ci is null && kind != KindCiHeadMoved ? null : pr.HeadRefOid,
                CiOutcome = ci?.Outcome,
                CiBreakdown = ci?.Breakdown,
                DedupeKey = ci is null && kind != KindCiHeadMoved
                    ? null
                    : !exactHeadCiWait
                        ? $"{kind}:pr-{pr.Number}:{pr.HeadRefOid}"
                        : $"{kind}:pr-{pr.Number}:{pr.HeadRefOid}:observed-{observedHead}",
                OwedTransition = owedTransition,
                ObservedHeadSha = observedHead,
                CurrentHeadSha = kind == KindCiHeadMoved ? currentHead : null,
                CiWaitState = ciWaitState,
                CiClassificationSource = ciClassificationSource,
            });
        }
    }

    /// <summary>
    /// G589: normalize GitHub's CheckRun / StatusContext union for the exact
    /// PR head. Pending dominates until every row is terminal; after that,
    /// any failed/error/cancelled conclusion makes the outcome failed.
    /// A zero-row or headless rollup is not a completed CI run and preserves
    /// the pre-G589 stalled-work kind.
    /// </summary>
    private static StalledWorkCiProjection? ProjectCiState(GitHubAutomationPrCandidate pr)
    {
        if (string.IsNullOrWhiteSpace(pr.HeadRefOid) || pr.StatusCheckRollup.Count == 0)
        {
            return null;
        }

        var passed = 0;
        var failed = 0;
        var skipped = 0;
        var pending = 0;
        foreach (var check in pr.StatusCheckRollup)
        {
            switch (ClassifyCheck(check))
            {
                case StalledWorkCheckOutcome.Passed:
                    passed++;
                    break;
                case StalledWorkCheckOutcome.Failed:
                    failed++;
                    break;
                case StalledWorkCheckOutcome.Skipped:
                    skipped++;
                    break;
                default:
                    pending++;
                    break;
            }
        }

        var outcome = pending > 0
            ? StalledWorkCiOutcomes.Pending
            : failed > 0
                ? StalledWorkCiOutcomes.Failed
                : StalledWorkCiOutcomes.AllGreen;
        return new StalledWorkCiProjection(
            outcome,
            new StalledWorkCiBreakdown
            {
                Passed = passed,
                Failed = failed,
                Skipped = skipped,
                Pending = pending,
                Total = passed + failed + skipped + pending,
            });
    }

    private static StalledWorkCheckOutcome ClassifyCheck(GitHubAutomationStatusCheckCandidate check)
    {
        var status = check.Status.Trim().ToUpperInvariant();
        if (status.Length > 0 && !string.Equals(status, "COMPLETED", StringComparison.Ordinal))
        {
            return StalledWorkCheckOutcome.Pending;
        }

        var conclusion = check.Conclusion.Trim().ToUpperInvariant();
        if (conclusion.Length > 0)
        {
            return conclusion switch
            {
                "SUCCESS" => StalledWorkCheckOutcome.Passed,
                "SKIPPED" or "NEUTRAL" => StalledWorkCheckOutcome.Skipped,
                _ => StalledWorkCheckOutcome.Failed,
            };
        }

        var state = check.State.Trim().ToUpperInvariant();
        if (state.Length > 0)
        {
            return state switch
            {
                "SUCCESS" => StalledWorkCheckOutcome.Passed,
                "PENDING" or "EXPECTED" => StalledWorkCheckOutcome.Pending,
                _ => StalledWorkCheckOutcome.Failed,
            };
        }

        // A completed CheckRun with no conclusion is terminal but not green;
        // an otherwise shape-less row cannot prove terminality and waits.
        return string.Equals(status, "COMPLETED", StringComparison.Ordinal)
            ? StalledWorkCheckOutcome.Failed
            : StalledWorkCheckOutcome.Pending;
    }

    /// <summary>
    /// G546: the draft half of <see cref="KindRepairStalled"/>.
    /// <see cref="CollectPrCreatedNotReviewing"/> skips draft PRs outright
    /// (correctly — a draft PR is not "review hasn't started"), which is
    /// exactly why the four-day G545 stall was invisible: its repair PR was a
    /// draft carrying <c>intent-pr-update-in-progress</c>, so no kind covered
    /// it and <c>stalled-work</c> reported healthy for four days. A draft PR
    /// in the repair lifecycle is still a claimed repair, and a claimed repair
    /// that stops emitting activity is still a dead worker.
    ///
    /// Deliberately narrow, so the draft path adds no new false positives:
    /// it fires ONLY past the threshold. Inside the threshold a draft repair
    /// PR stays invisible exactly as it is today — no informational item is
    /// invented for it, keeping current output byte-compatible. The two paths
    /// are disjoint by construction (this one handles drafts,
    /// <see cref="CollectPrCreatedNotReviewing"/> handles non-drafts), so a PR
    /// can never be reported twice.
    /// </summary>
    private static void CollectDraftRepairStalled(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo,
        DateTimeOffset now,
        int repairSilentMinutes,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        var openIssuesByNumber = openIssues
            .Where(issue => IsOpen(issue.State))
            .ToDictionary(issue => issue.Number);

        foreach (var pr in openPrs)
        {
            if (!IsOpen(pr.State) || !pr.IsDraft)
            {
                continue;
            }

            var prLabels = LabelSet(pr.Labels);
            if (!CarriesRepairLifecycleLabel(prLabels))
            {
                continue;
            }

            if (!TryPromoteToRepairStalled(pr, prLabels, repo, now, repairSilentMinutes, out var recommendedAction, out var silentMinutes))
            {
                continue;
            }

            // The linked issue supplies the execution unit and the domain
            // corroboration every other collector here relies on. A repair PR
            // with no resolvable open source issue cannot be domain-confirmed,
            // so it is left alone rather than reported into a domain it may
            // not belong to.
            GitHubAutomationIssueCandidate? matchedIssue = null;
            foreach (var reference in pr.ClosingIssuesReferences)
            {
                if (reference.Number > 0
                    && ReferenceMatchesRepo(reference, repo)
                    && openIssuesByNumber.TryGetValue(reference.Number, out var candidate))
                {
                    matchedIssue = candidate;
                    break;
                }
            }

            if (matchedIssue is null)
            {
                continue;
            }

            var resolution = ResolveExecutionUnit(context, matchedIssue.Title);
            var packetDeclaredDomain = resolution.Corroborated ? ReadPacketDeclaredDomain(context, resolution.ExecutionUnit) : null;
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindRepairStalled,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                    Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindRepairStalled,
                ExecutionUnit = resolution.ExecutionUnit,
                Issue = new StalledWorkRef { Number = matchedIssue.Number, Url = matchedIssue.Url },
                Pr = new StalledWorkRef { Number = pr.Number, Url = pr.Url },
                AgeMinutes = silentMinutes,
                IsInformational = false,
                RecommendedAction = recommendedAction,
            });
        }
    }

    private static bool CarriesRepairLifecycleLabel(ISet<string> prLabels) =>
        prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate)
        || prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress)
        || prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrRereviewReady);

    /// <summary>
    /// G546: decides whether a repair-lifecycle PR has been observably silent
    /// past <paramref name="repairSilentMinutes"/>, and if so produces the
    /// status-check recommendation for the responsible thread.
    ///
    /// "Observable activity" is the PR's own <c>updatedAt</c> — the same
    /// proxy <see cref="CollectClaimedButSilent"/> already relies on, and the
    /// one field that covers ALL THREE activity classes this kind cares
    /// about: GitHub bumps a PR's <c>updatedAt</c> on a push to its head
    /// branch, on any PR/review comment, and on any label change. The
    /// approximation is conservative in the only direction that matters: it
    /// can be bumped by activity that is not repair progress (making a stalled
    /// repair look alive), but it cannot stay still while a repair is
    /// genuinely progressing — so it never manufactures a false stall.
    ///
    /// Fails closed like <see cref="CollectClaimedButSilent"/>: a missing or
    /// malformed <c>updatedAt</c> means silence cannot be established, so the
    /// PR is NOT promoted (it keeps its existing informational treatment, or
    /// stays invisible on the draft path) rather than being flagged on
    /// unusable evidence. A future-dated timestamp is clamped to
    /// <paramref name="now"/>, which can only ever make the PR look less
    /// silent.
    /// </summary>
    private static bool TryPromoteToRepairStalled(
        GitHubAutomationPrCandidate pr,
        ISet<string> prLabels,
        string repo,
        DateTimeOffset now,
        int repairSilentMinutes,
        out string recommendedAction,
        out int silentMinutes)
    {
        recommendedAction = string.Empty;
        silentMinutes = 0;

        if (!TryParseActivityTimestamp(pr.UpdatedAt, out var activity, out _))
        {
            return false;
        }

        var minutes = ComputeAgeMinutesFromInstant(ClampToNow(activity, now), now);
        if (minutes < repairSilentMinutes)
        {
            return false;
        }

        silentMinutes = minutes;
        recommendedAction = BuildRepairStalledAction(pr, prLabels, repo, minutes);
        return true;
    }

    /// <summary>
    /// G546: the recommendation is always a status check addressed to the
    /// thread that owns the current repair state — never a transition. Which
    /// thread depends on the label: <c>intent-pr-request-update</c> /
    /// <c>intent-pr-update-in-progress</c> are owned by the implement thread
    /// (a repair was requested of, or claimed by, the implementer), whereas
    /// <c>intent-pr-rereview-ready</c> is owned by review dispatch (the repair
    /// is pushed and it is the review side that has gone quiet).
    /// </summary>
    private static string BuildRepairStalledAction(
        GitHubAutomationPrCandidate pr, ISet<string> prLabels, string repo, int silentMinutes)
    {
        var (state, thread) =
            prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress)
                ? (WorkerNextActionConstants.Labels.IntentPrUpdateInProgress, "implement")
                : prLabels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate)
                    ? (WorkerNextActionConstants.Labels.IntentPrRequestUpdate, "implement")
                    : (WorkerNextActionConstants.Labels.IntentPrRereviewReady, "review-dispatch");

        return
            $"status check: PR #{pr.Number} in {repo} has carried `{state}` with no observable activity "
            + $"(head commits, comments, label changes) for {silentMinutes}m — ask the responsible `{thread}` "
            + "thread for a status update; do not transition the PR or reassign the repair from silence alone.";
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
                .Where(item => MatchesLinkedPr(item, repo, prToken) && item.State != QueueItemState.Completed)
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
    ///
    /// G533 review repair: a missing or malformed <c>updatedAt</c> — on
    /// either the issue OR a linked PR — is never silently treated as "old
    /// activity" by falling back to <c>createdAt</c>. <c>createdAt</c>
    /// measures a DIFFERENT event (issue open time / PR open time, not
    /// claim acquisition or the linked PR's own last touch) and could
    /// manufacture a silence interval that begins long before the claim
    /// was ever made, firing this kind on unusable evidence rather than
    /// genuine silence. Unusable activity data fails closed into
    /// <c>excluded[]</c> (never <c>items[]</c>) with a structured
    /// diagnostic naming exactly which timestamp was unusable and why. A
    /// parsed timestamp that is somehow in the future relative to
    /// <paramref name="now"/> (clock skew, bad data) is clamped to
    /// <paramref name="now"/> — conservative in the same direction as
    /// everything else here: it can only ever make the candidate look
    /// LESS silent, never manufacture a false positive.
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
        List<StalledWorkExcluded> excluded,
        List<string> warnings)
    {
        // G545: consulted once per call, not per issue. Missing/unparseable
        // queue-state is never fatal to this kind — it simply means the
        // blocked exemption below cannot be evaluated for any candidate,
        // preserving the exact pre-G545 behavior (a domain that never uses
        // queue-state.json must not lose claimed-but-silent detection).
        var queueState = TryLoadQueueStateForClaimedButSilent(context, domain, repo, warnings);

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

            // Best-effort execution-unit naming for diagnostics below —
            // used regardless of whether corroboration/domain confirmation
            // ultimately succeeds, matching the pattern every other
            // collector in this file already follows.
            var resolution = ResolveExecutionUnit(context, issue.Title);
            var issueRef = new StalledWorkRef { Number = issue.Number, Url = issue.Url };

            // G545 field finding (sekiban-as-a-service, 2026-07-21,
            // SKS-G818): a unit legitimately `state=blocked` in queue-state
            // (an explicit `blocked_by` reason recorded via `queue
            // transition <unit> blocked --reason <text>`) still carries
            // `intent-issue-in-progress` on GitHub — there is no GitHub-side
            // signal this collector previously consulted to tell "silently
            // stalled" apart from "correctly waiting on a recorded
            // dependency". A blocked queue item exempts its issue from
            // claimed-but-silent entirely; if the reconcile label hasn't
            // been applied yet, the mismatch is surfaced as the
            // transitional, informational `blocked-label-drift` kind
            // instead — never as a silent-stall false positive.
            var queueItem = queueState?.Items.FirstOrDefault(
                candidate => string.Equals(candidate.ExecutionUnit, resolution.ExecutionUnit, StringComparison.Ordinal));
            if (queueItem is { State: QueueItemState.Blocked })
            {
                if (!labels.Contains(WorkerNextActionConstants.Labels.IntentIssueBlocked))
                {
                    var blockedReason = string.Join("; ", queueItem.BlockedBy);
                    items.Add(new StalledWorkItem
                    {
                        Kind = KindBlockedLabelDrift,
                        ExecutionUnit = resolution.ExecutionUnit,
                        Issue = issueRef,
                        Pr = null,
                        AgeMinutes = ComputeAgeMinutes(issue.UpdatedAt, now),
                        IsInformational = true,
                        RecommendedAction =
                            $"queue-state reports `{resolution.ExecutionUnit}` as blocked (blocked_by: {blockedReason}), "
                            + $"but issue #{issue.Number} does not yet carry the "
                            + $"`{WorkerNextActionConstants.Labels.IntentIssueBlocked}` label — reconcile via: "
                            + $"intent-cli automation issue-block {resolution.ExecutionUnit} --repo {repo} "
                            + $"--issue {issue.Number} --reason \"{blockedReason}\" --write --format json",
                    });
                }

                // Legitimately blocked either way — never claimed-but-silent.
                continue;
            }

            if (!TryParseActivityTimestamp(issue.UpdatedAt, out var issueActivity, out var issueProblem))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindClaimedButSilent,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = issueRef,
                    Pr = null,
                    Reason = ReasonActivityDataUnusable,
                    Detail =
                        $"issue #{issue.Number}'s updatedAt is {issueProblem} — claimed-but-silent requires a "
                        + "valid last-activity timestamp and never substitutes createdAt (issue OPEN time, not "
                        + "claim acquisition time) as a stand-in, since that could manufacture a misleadingly old "
                        + "silence interval.",
                });
                continue;
            }

            var lastActivity = ClampToNow(issueActivity, now);
            var linkedPrActivityUnusable = false;
            string? linkedPrProblemDetail = null;

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
                        if (!TryParseActivityTimestamp(pr.UpdatedAt, out var prActivity, out var prProblem))
                        {
                            linkedPrActivityUnusable = true;
                            linkedPrProblemDetail =
                                $"linked PR #{pr.Number}'s updatedAt is {prProblem} — its real activity could not "
                                + "be verified, so this candidate is conservatively excluded rather than risking "
                                + "an under-counted (falsely silent) result.";
                        }
                        else
                        {
                            var clampedPrActivity = ClampToNow(prActivity, now);
                            if (clampedPrActivity > lastActivity)
                            {
                                lastActivity = clampedPrActivity;
                            }
                        }
                        break;
                    }
                }
            }

            if (linkedPrActivityUnusable)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindClaimedButSilent,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = issueRef,
                    Pr = null,
                    Reason = ReasonActivityDataUnusable,
                    Detail = linkedPrProblemDetail!,
                });
                continue;
            }

            var silentMinutes = ComputeAgeMinutesFromInstant(lastActivity, now);
            if (silentMinutes < claimedSilentMinutes)
            {
                continue;
            }

            var packetDeclaredDomain = resolution.Corroborated ? ReadPacketDeclaredDomain(context, resolution.ExecutionUnit) : null;
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindClaimedButSilent,
                    ExecutionUnit = resolution.ExecutionUnit,
                    Issue = issueRef,
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
                Issue = issueRef,
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
    /// G545: tolerant queue-state load for <see cref="CollectClaimedButSilent"/>'s
    /// blocked exemption — missing or malformed queue-state is warned about
    /// (mirroring <see cref="CollectMergedNotClosedOut"/>'s own convention)
    /// but never fatal to the caller: it simply means the blocked exemption
    /// cannot be evaluated for any candidate this call, so every issue falls
    /// through to the pre-G545 claimed-but-silent logic unchanged.
    /// </summary>
    private static QueueState? TryLoadQueueStateForClaimedButSilent(
        CliContext context, string domain, string repo, List<string> warnings)
    {
        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(context.RepoRoot, domain, repo);
        if (!File.Exists(queueStateLocation.Path))
        {
            return null;
        }

        try
        {
            return QueueStateSerializer.Deserialize(File.ReadAllText(queueStateLocation.Path));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            warnings.Add($"queue-state at '{queueStateLocation.Path}' could not be parsed: {exception.Message}; skipped the claimed-but-silent blocked exemption.");
            return null;
        }
    }

    /// <summary>
    /// G544: WIP is empty for <paramref name="domain"/> when (a) no open PR
    /// resolves (or fails to rule itself out) as belonging to
    /// <paramref name="domain"/> — see <see cref="PrBlocksDomainWip"/> — and
    /// (b) no open issue carrying <c>intent-target</c> resolves (or fails to
    /// rule itself out) as belonging to it either. In both cases, a
    /// candidate whose domain cannot be corroborated at all is
    /// conservatively treated as blocking EVERY domain's WIP-empty check
    /// (unlike every other collector here, which excludes an uncorroborated
    /// candidate from firing) — the risk here runs the other way: falsely
    /// reporting the backlog idle when something IS actually in flight would
    /// recommend a publish nothing should stop, so an unresolved candidate
    /// must never be silently ignored. Only a candidate CONCLUSIVELY
    /// confirmed to belong to a DIFFERENT domain is excused.
    /// </summary>
    private static bool DomainWipIsEmpty(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo)
    {
        foreach (var pr in openPrs)
        {
            if (!IsOpen(pr.State))
            {
                continue;
            }

            if (PrBlocksDomainWip(context, domain, candidateDomains, openIssues, pr, repo))
            {
                return false;
            }
        }

        foreach (var issue in openIssues)
        {
            if (!IsOpen(issue.State))
            {
                continue;
            }

            if (!LabelSet(issue.Labels).Contains(WorkerNextActionConstants.Labels.IntentTarget))
            {
                continue;
            }

            var resolution = ResolveExecutionUnit(context, issue.Title);
            if (!resolution.Corroborated)
            {
                // Domain membership cannot be ruled out either way —
                // conservatively treat as WIP rather than risk a false
                // "idle" report.
                return false;
            }

            var packetDeclaredDomain = ReadPacketDeclaredDomain(context, resolution.ExecutionUnit);
            if (TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo, out _, out _))
            {
                // Confirmed to belong to THIS domain — genuinely in flight.
                return false;
            }

            // TryConfirmDomain only returns false here on a genuine
            // CONTRADICTION (a packet that actively declares a DIFFERENT
            // domain — resolution.Corroborated is already true, so this is
            // not the uncorroborated case handled above); a confirmed
            // other-domain issue does not block this domain's WIP.
        }

        return true;
    }

    /// <summary>
    /// G544 review repair: a PR never itself carries <c>intent-target</c>,
    /// so its domain is corroborated through its CLOSING ISSUE (never the
    /// PR's own title, which is not guaranteed to mirror the issue's) using
    /// the same resolution rules every other collector in this file already
    /// applies to issues. A PR with no closing-issue reference for
    /// <paramref name="repo"/>, or whose closing issue cannot be found among
    /// <paramref name="openIssues"/> or corroborated to a packet, or that
    /// closes more than one issue where any single reference is uncertain
    /// or same-domain, conservatively BLOCKS this domain's WIP-empty check.
    /// Only a PR whose EVERY closing reference is conclusively confirmed to
    /// belong to a DIFFERENT domain is excused.
    /// </summary>
    private static bool PrBlocksDomainWip(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        GitHubAutomationPrCandidate pr,
        string repo)
    {
        var closingReferences = pr.ClosingIssuesReferences
            .Where(reference => reference.Number > 0 && ReferenceMatchesRepo(reference, repo))
            .ToArray();

        if (closingReferences.Length == 0)
        {
            // No closing-issue link for this repo at all — this PR's domain
            // cannot be corroborated by anything. Conservatively blocks.
            return true;
        }

        foreach (var reference in closingReferences)
        {
            var matchedIssue = openIssues.FirstOrDefault(candidate => candidate.Number == reference.Number);
            if (matchedIssue is null)
            {
                // The closing issue is not among the open issues this scan
                // fetched (closed, or an unresolvable mismatch) — cannot
                // corroborate; conservatively blocks.
                return true;
            }

            var resolution = ResolveExecutionUnit(context, matchedIssue.Title);
            if (!resolution.Corroborated)
            {
                return true;
            }

            var packetDeclaredDomain = ReadPacketDeclaredDomain(context, resolution.ExecutionUnit);
            if (TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo, out _, out _))
            {
                // Confirmed to belong to THIS domain — genuinely in flight.
                return true;
            }

            // This reference confirmed a DIFFERENT domain — keep checking
            // any remaining closing references before excusing the PR.
        }

        // Every closing reference resolved to a confirmed OTHER domain.
        return false;
    }

    /// <summary>
    /// G544: fires <see cref="KindBacklogReadyIdle"/> when WIP is empty for
    /// <paramref name="domain"/> (<see cref="DomainWipIsEmpty"/>), the SAME
    /// canonical selector <c>issue publish-flow</c> preflight itself uses
    /// (<see cref="IntentNextSliceCommand.Analyze"/> — no separate
    /// heuristic) reports a publishable candidate, and no <c>runs.jsonl</c>
    /// activity has been recorded for at least <paramref
    /// name="backlogIdleMinutes"/>. "Activity" here is the most recent
    /// <c>ts</c> across every row in <c>runs.jsonl</c> — a different signal
    /// than every other collector's GitHub-entity-timestamp approach, since
    /// by construction nothing has been published yet for this candidate to
    /// carry a GitHub timestamp of its own. A missing/unparseable/empty
    /// runs.jsonl cannot establish a baseline and fails closed into
    /// <c>excluded[]</c>, never a guessed age — same philosophy as
    /// <see cref="ReasonActivityDataUnusable"/> above.
    /// </summary>
    private static void CollectBacklogReadyIdle(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string repo,
        DateTimeOffset now,
        int backlogIdleMinutes,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        if (!DomainWipIsEmpty(context, domain, candidateDomains, openIssues, openPrs, repo))
        {
            return;
        }

        // Keep the pre-existing item/exclusion counts separate from G574's
        // own blocked-state diagnostics. They are consulted only by the
        // incomplete-candidate G670 preview branch below; the publishable
        // G544 flow remains independent of unrelated collector evidence.
        var itemsBeforeBacklogBlockedState = items.Count;
        var exclusionsBeforeBacklogBlockedState = excluded.Count;

        // G574: inspect the two-field blocked representation before asking
        // next-slice for a publishable candidate. The selector's fallback can
        // surface state=blocked packets, while the reverse half-converged
        // shape (non-blocked + blocked_by) is filtered out entirely. Reading
        // queue-state directly here is therefore necessary both to suppress
        // the unsafe publish recommendation and to keep reverse drift visible.
        var nonPublishableUnits = CollectBacklogBlockedState(
            context, domain, repo, now, items, excluded);

        IntentNextSliceResult nextSlice;
        try
        {
            nextSlice = IntentNextSliceCommand.Analyze(context, domain, repo, runtimeCreationAllowed: true);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindBacklogReadyIdle,
                ExecutionUnit = string.Empty,
                Issue = null,
                Pr = null,
                Reason = ReasonActivityDataUnusable,
                Detail = $"canonical next-slice candidate selection failed: {exception.Message}",
            });
            return;
        }

        // G670: only an incomplete candidate is a readiness-preview lane.
        // Keep its evidence subject to the existing G544 eligibility gates;
        // notably, G574 diagnostics created above are not part of the
        // pre-existing snapshots, so a later eligible unit keeps its G544
        // behavior.
        if (nextSlice.Candidate is { PublishGateReady: false } incompleteCandidate
            && nextSlice.ReadinessExclusions.Count > 0)
        {
            if (itemsBeforeBacklogBlockedState > 0
                || exclusionsBeforeBacklogBlockedState > 0
                || nextSlice.Wip.Count > 0
                || nextSlice.ClarificationOpen)
            {
                return;
            }

            var incompleteExecutionUnit = incompleteCandidate.ExecutionUnit;
            if (nonPublishableUnits.Contains(incompleteExecutionUnit))
            {
                return;
            }

            // An incomplete packet is not a publishable G544 candidate, so
            // runs.jsonl is only an eligibility probe for the named G670
            // exclusion. Missing, malformed, empty, or young evidence is
            // deliberately silent; the activity-data-unusable diagnostic
            // belongs to the original publishable-candidate flow below.
            var incompleteRunLogPath = context.GetRunLogPath();
            if (!File.Exists(incompleteRunLogPath))
            {
                return;
            }

            DateTimeOffset? incompleteLastActivity = null;
            try
            {
                foreach (var runEvent in RunLogSerializer.DeserializeAll(File.ReadAllText(incompleteRunLogPath)))
                {
                    if (incompleteLastActivity is null || runEvent.Ts > incompleteLastActivity.Value)
                    {
                        incompleteLastActivity = runEvent.Ts;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
            {
                return;
            }

            if (incompleteLastActivity is null)
            {
                return;
            }

            var incompleteIdleMinutes = ComputeAgeMinutesFromInstant(
                ClampToNow(incompleteLastActivity.Value, now),
                now);
            if (incompleteIdleMinutes < backlogIdleMinutes)
            {
                return;
            }

            foreach (var exclusion in nextSlice.ReadinessExclusions)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindBacklogReadyIdle,
                    ExecutionUnit = exclusion.ExecutionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = NextSliceReadinessClass.ContractIncomplete,
                    Detail = $"packet '{exclusion.ExecutionUnit}' was excluded from backlog-ready-idle candidacy by the shared publish gate: {exclusion.Cause}; no publish action is emitted for this unit.",
                });
            }

            return;
        }

        // G544: preserve the original publishable-candidate flow. Its
        // activity-data-unusable exclusions and its tolerance of unrelated
        // domain collectors are intentionally unchanged.
        if (nextSlice.Candidate is null
            || !string.Equals(nextSlice.RecommendedOutcome, NextSliceReadinessClass.IssueCutReady, StringComparison.Ordinal))
        {
            // Nothing publishable right now (WIP-per-queue-state, a
            // dependency/lifecycle/clarification gate, an incomplete
            // contract, or a genuinely empty backlog) — never fires.
            return;
        }

        var executionUnit = nextSlice.Candidate.ExecutionUnit;
        if (nonPublishableUnits.Contains(executionUnit))
        {
            // blocked-parked/state-drift already explains why this packet is
            // not publishable. Never append the contradictory G544 action.
            return;
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindBacklogReadyIdle,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                Reason = ReasonActivityDataUnusable,
                Detail = $"no runs log found at '{runLogPath}'; cannot establish a last-activity baseline for the idle threshold.",
            });
            return;
        }

        DateTimeOffset? lastActivity = null;
        try
        {
            foreach (var runEvent in RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath)))
            {
                if (lastActivity is null || runEvent.Ts > lastActivity.Value)
                {
                    lastActivity = runEvent.Ts;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindBacklogReadyIdle,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                Reason = ReasonActivityDataUnusable,
                Detail = $"runs log at '{runLogPath}' could not be parsed: {exception.Message}",
            });
            return;
        }

        if (lastActivity is null)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindBacklogReadyIdle,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                Reason = ReasonActivityDataUnusable,
                Detail = $"runs log at '{runLogPath}' contains no rows; cannot establish a last-activity baseline for the idle threshold.",
            });
            return;
        }

        var idleMinutes = ComputeAgeMinutesFromInstant(ClampToNow(lastActivity.Value, now), now);
        if (idleMinutes < backlogIdleMinutes)
        {
            return;
        }

        items.Add(new StalledWorkItem
        {
            Kind = KindBacklogReadyIdle,
            ExecutionUnit = executionUnit,
            Issue = null,
            Pr = null,
            AgeMinutes = idleMinutes,
            IsInformational = false,
            RecommendedAction = $"intent-cli issue publish-flow {executionUnit} --repo {repo} --write --format json",
        });
    }

    /// <summary>
    /// G574: classifies queue items whose <c>state</c>/<c>blocked_by</c>
    /// representation is either converged-blocked or half-converged. Only
    /// packet-backed items belonging to the requested domain/repository are
    /// considered, matching the backlog detector's own child-packet scope.
    /// The returned set is an explicit publish-suppression set even when age
    /// evidence is unusable: missing history must never turn a parked/drifted
    /// unit back into a publish recommendation.
    /// </summary>
    private static HashSet<string> CollectBacklogBlockedState(
        CliContext context,
        string domain,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        var nonPublishableUnits = new HashSet<string>(StringComparer.Ordinal);
        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(context.RepoRoot, domain, repo);
        if (!File.Exists(queueStateLocation.Path))
        {
            return nonPublishableUnits;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStateLocation.Path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            // Preserve the pre-G574 result for ordinary queued work. The
            // canonical selector owns its existing queue-state diagnostics.
            return nonPublishableUnits;
        }

        var candidates = queueState.Items
            // This is the pre-publish backlog lane: queued and blocked are
            // its only valid states. ClarifyBlocked legitimately carries a
            // reason under a different state machine and must not be
            // misclassified as G574 half-convergence.
            .Where(item => item.State is QueueItemState.Queued or QueueItemState.Blocked
                && (item.State == QueueItemState.Blocked || item.BlockedBy.Count > 0)
                && QueueItemMatchesBacklogScope(context, item, domain, repo))
            .ToArray();
        if (candidates.Length == 0)
        {
            return nonPublishableUnits;
        }

        IReadOnlyList<RunEvent> runEvents;
        var runLogPath = context.GetRunLogPath();
        try
        {
            runEvents = File.Exists(runLogPath)
                ? RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath))
                : Array.Empty<RunEvent>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            runEvents = Array.Empty<RunEvent>();
        }

        foreach (var queueItem in candidates)
        {
            var stateBlocked = queueItem.State == QueueItemState.Blocked;
            var hasBlockedBy = queueItem.BlockedBy.Count > 0;
            var kind = stateBlocked && hasBlockedBy ? KindBlockedParked : KindStateDrift;
            nonPublishableUnits.Add(queueItem.ExecutionUnit);

            var transition = runEvents
                .Where(runEvent => string.Equals(runEvent.ExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal)
                    && (!stateBlocked || !hasBlockedBy || string.Equals(runEvent.Event, "blocked", StringComparison.Ordinal)))
                .OrderByDescending(runEvent => runEvent.Ts)
                .FirstOrDefault();

            if (transition is null)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = kind,
                    ExecutionUnit = queueItem.ExecutionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonActivityDataUnusable,
                    Detail = kind == KindBlockedParked
                        ? "queue-state is converged blocked, but runs.jsonl has no blocked event for this unit; age-since-block cannot be established, and publishing remains suppressed."
                        : "queue-state is half-converged, but runs.jsonl has no transition event for this unit; drift age cannot be established, and publishing remains suppressed.",
                });
                continue;
            }

            var ageMinutes = ComputeAgeMinutesFromInstant(ClampToNow(transition.Ts, now), now);
            var blockedReason = hasBlockedBy
                ? string.Join("; ", queueItem.BlockedBy)
                : "(missing blocked_by reason)";

            items.Add(new StalledWorkItem
            {
                Kind = kind,
                ExecutionUnit = queueItem.ExecutionUnit,
                Issue = null,
                Pr = null,
                AgeMinutes = ageMinutes,
                IsInformational = kind == KindBlockedParked,
                RecommendedAction = kind == KindBlockedParked
                    ? $"parked by blocked_by: {blockedReason}; no publish action while the blocked state remains converged."
                    : BuildBlockedStateDriftAction(queueItem, repo, blockedReason),
            });
        }

        return nonPublishableUnits;
    }

    private static bool QueueItemMatchesBacklogScope(
        CliContext context,
        QueueItem item,
        string domain,
        string repo)
    {
        var packetPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", item.ExecutionUnit, "packet.yaml");
        if (!File.Exists(packetPath))
        {
            return false;
        }

        var normalizedReturnPath = item.ClarificationReturnPath.Replace('\\', '/');
        var returnPathParts = normalizedReturnPath.Split('/');
        if (returnPathParts.Length >= 2
            && string.Equals(returnPathParts[0], "intents", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(returnPathParts[1], domain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var fields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetPath));
            var packetDomain = ReadFirstNonEmpty(fields, "implementation_issue_packet.domain", "domain");
            if (!string.IsNullOrWhiteSpace(packetDomain)
                && !string.Equals(packetDomain, domain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var targetRepo = ReadFirstNonEmpty(fields, "implementation_issue_packet.target_repo", "target_repo");
            return string.IsNullOrWhiteSpace(targetRepo)
                || string.Equals(targetRepo, repo, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or FormatException)
        {
            return false;
        }
    }

    private static string BuildBlockedStateDriftAction(QueueItem item, string repo, string blockedReason)
    {
        if (item.LinkedIssue is { Repo: { } linkedRepo, Number: { } linkedNumber }
            && string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase))
        {
            var issueNumber = linkedNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"state and blocked_by disagree; choose the intended canonical convergence: "
                + $"intent-cli automation issue-block {item.ExecutionUnit} --repo {repo} --issue {issueNumber} "
                + $"--reason \"{blockedReason}\" --write --format json, or "
                + $"intent-cli automation issue-block {item.ExecutionUnit} --repo {repo} --issue {issueNumber} "
                + "--clear --write --format json; never publish while drifted.";
        }

        if (item.LinkedIssue is null)
        {
            return $"state and blocked_by disagree; choose the intended canonical block/unblock convergence; "
                + $"to release this unpublished unit run intent-cli automation issue-block {item.ExecutionUnit} "
                + "--clear --pre-publish --write --format json; never publish while drifted.";
        }

        return "state and blocked_by disagree and linked_issue is incomplete; repair linkage, then use the canonical "
            + "intent-cli automation issue-block block/clear surface; never publish while drifted.";
    }

    /// <summary>
    /// G552: reads the domain's OPEN clarification artifacts and reports each
    /// as <see cref="KindDesignDecisionPending"/> with its age, blocking
    /// execution unit, and question summary.
    ///
    /// This collector is deliberately GitHub-free: a design-decision hold has
    /// no GitHub entity of its own, which is precisely why the nine-hour G551
    /// hold was invisible to every other kind here. Its evidence is the
    /// clarification artifact the canonical clarify surface wrote, and its
    /// age is that artifact's own <c>createdAt</c> — the moment the block was
    /// recorded, which is the interval an operator actually cares about.
    ///
    /// Fail-closed, in the same direction as every other collector: an
    /// artifact that cannot be read or deserialized goes to
    /// <c>excluded[]</c> with its path (<see cref="ReasonClarificationUnreadable"/>),
    /// and a clarification whose domain cannot be confirmed against its own
    /// packet-declared domain is excluded rather than attributed to the
    /// requested domain. An ANSWERED, applied, or cancelled clarification is
    /// simply not open, so it produces nothing at all — answering is what
    /// clears the item.
    /// </summary>
    private static void CollectDesignDecisionPending(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        DateTimeOffset now,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        var clarificationsRoot = Path.Combine(context.RepoRoot, ".intent-cli", "clarifications");
        if (!Directory.Exists(clarificationsRoot))
        {
            // No clarification surface in this checkout — nothing to report.
            // Absence is never a stall signal on its own.
            return;
        }

        string[] artifactPaths;
        try
        {
            artifactPaths = Directory
                .EnumerateFiles(clarificationsRoot, "request.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindDesignDecisionPending,
                ExecutionUnit = string.Empty,
                Issue = null,
                Pr = null,
                Reason = ReasonClarificationUnreadable,
                Detail =
                    $"could not enumerate clarification artifacts under `{clarificationsRoot}`: {exception.Message}. "
                    + "A design-decision hold may be present but unreadable — resolve the read failure rather than "
                    + "treating this as a healthy pipeline.",
            });
            return;
        }

        foreach (var artifactPath in artifactPaths)
        {
            ClarificationItem clarification;
            try
            {
                clarification = ClarificationSerializer.Deserialize(File.ReadAllText(artifactPath));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or NotSupportedException)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindDesignDecisionPending,
                    ExecutionUnit = string.Empty,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonClarificationUnreadable,
                    Detail =
                        $"clarification artifact `{artifactPath}` could not be read: {exception.Message}. "
                        + "Excluded rather than assumed answered — an unreadable artifact is not evidence of an "
                        + "unblocked pipeline.",
                });
                continue;
            }

            if (clarification.Status != ClarificationStatus.Open)
            {
                // Answered / applied / cancelled: the hold is over. This is
                // the clearing path — no item, no exclusion, no noise.
                continue;
            }

            // The artifact itself is the corroborating linkage: it was
            // written by the canonical clarify surface against a named
            // execution unit, not guessed from an issue/PR title. Domain
            // confirmation still applies, so a clarification whose packet
            // declares a different domain never leaks into this domain's
            // report.
            var resolution = new ExecutionUnitResolution(
                clarification.ExecutionUnit, Corroborated: true, IsAmbiguous: false, CandidatePacketPaths: Array.Empty<string>());
            var packetDeclaredDomain = ReadPacketDeclaredDomain(context, clarification.ExecutionUnit);
            if (!TryConfirmDomain(domain, resolution, packetDeclaredDomain, candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindDesignDecisionPending,
                    ExecutionUnit = clarification.ExecutionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindDesignDecisionPending,
                ExecutionUnit = clarification.ExecutionUnit,
                Issue = null,
                Pr = null,
                AgeMinutes = ComputeAgeMinutesFromInstant(ClampToNow(clarification.CreatedAt, now), now),
                IsInformational = false,
                RecommendedAction = BuildDesignDecisionPendingAction(clarification, domain),
            });
        }
    }

    /// <summary>
    /// G564: closed-out units whose DECLARED knowledge write-back has no
    /// record. The closeout fact comes from the runs log
    /// (<c>closeout-recorded</c>, written by <c>closeout pr --write</c>), the
    /// obligation from the packet's own G461 declaration, and the clearance
    /// from <see cref="KnowledgeWriteBackRecord"/>. A unit that declared
    /// nothing required is silent — declining is a legitimate answer, and this
    /// detects broken promises, not missing enthusiasm.
    /// </summary>
    private static void CollectKnowledgeWritebackPending(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        DateTimeOffset now,
        DateTimeOffset since,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            // No runs log in this checkout — no closeout has been recorded
            // here, so there is no obligation to have broken.
            return;
        }

        IReadOnlyList<RunEvent> events;
        try
        {
            events = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or NotSupportedException)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindKnowledgeWritebackPending,
                ExecutionUnit = string.Empty,
                Issue = null,
                Pr = null,
                Reason = ReasonKnowledgeMetadataUnreadable,
                Detail =
                    $"the runs log `{runLogPath}` could not be read: {exception.Message}. Closed-out units cannot be "
                    + "enumerated, so a pending knowledge write-back may exist and be invisible — repair the log "
                    + "rather than reading this as a clean pipeline.",
            });
            return;
        }

        var closeouts = events
            .Where(runEvent =>
                string.Equals(runEvent.Event, CloseoutRecordedEvent, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(runEvent.ExecutionUnit))
            .GroupBy(runEvent => runEvent.ExecutionUnit, StringComparer.Ordinal)
            // The EARLIEST closeout is when the obligation started; a repeated
            // closeout (retry, re-application) must not reset the item's age.
            .Select(group => (Unit: group.Key, ClosedAt: group.Min(runEvent => runEvent.Ts)))
            .OrderBy(entry => entry.Unit, StringComparer.Ordinal)
            .ToArray();

        foreach (var (executionUnit, closedAt) in closeouts)
        {
            if (closedAt < since)
            {
                // Closed out before this detection shipped: out of scope by
                // contract (see KnowledgeWriteBackActivationUtc).
                continue;
            }

            // G564 review repair: the unit here comes from the runs log, which
            // is data, not a trusted identifier. Validate BEFORE any path is
            // built from it — a traversal/rooted/malformed unit is reported
            // with its own diagnostic rather than resolved and stat'ed.
            if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindKnowledgeWritebackPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonKnowledgeMetadataUnreadable,
                    Detail =
                        $"a `{CloseoutRecordedEvent}` event in `{runLogPath}` names a non-canonical execution unit: "
                        + $"{unitError} No packet or record path is derived from it — repair the runs log rather "
                        + "than resolving an identifier that is not one.",
                });
                continue;
            }

            var packetYamlPath = KnowledgeWriteBackRecord.ResolvePacketPath(context.RepoRoot, executionUnit);
            var resolution = new ExecutionUnitResolution(
                executionUnit,
                Corroborated: File.Exists(packetYamlPath),
                IsAmbiguous: false,
                CandidatePacketPaths: Array.Empty<string>());
            if (!TryConfirmDomain(domain, resolution, ReadPacketDeclaredDomain(context, executionUnit), candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindKnowledgeWritebackPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            KnowledgeWriteBackDeclaration declaration;
            try
            {
                declaration = KnowledgeWriteBackDeclaration.Read(File.ReadAllText(packetYamlPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindKnowledgeWritebackPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonKnowledgeMetadataUnreadable,
                    Detail =
                        $"`{executionUnit}`: packet `{packetYamlPath}` could not be read for its knowledge write-back "
                        + $"declaration: {exception.Message}. Excluded with its path rather than assumed to declare "
                        + "nothing — an unreadable declaration establishes neither that a write-back is owed nor "
                        + "that it is not.",
                });
                continue;
            }

            if (!declaration.IsRequired)
            {
                // Declared nothing (or declined every facet): no obligation,
                // no item, no noise. This is the `required=false` silence the
                // acceptance criteria require.
                continue;
            }

            var recordPath = KnowledgeWriteBackRecord.ResolveFullPath(context.RepoRoot, executionUnit);
            if (File.Exists(recordPath))
            {
                try
                {
                    // G564 review repair: the record must NAME this unit and
                    // carry SHA-shaped evidence. Clearing on any deserializable
                    // file let a record carrying a different unit's id — or a
                    // host_commit that is not a commit — discharge this unit's
                    // obligation.
                    _ = KnowledgeWriteBackRecord.Deserialize(File.ReadAllText(recordPath), executionUnit);
                    var relativeRecordPath = KnowledgeWriteBackRecord.ResolveRelativePath(executionUnit);
                    if (IsGitPathUncommitted(context.RepoRoot, relativeRecordPath))
                    {
                        items.Add(new StalledWorkItem
                        {
                            Kind = KindKnowledgeWritebackRecordedUncommitted,
                            ExecutionUnit = executionUnit,
                            Issue = null,
                            Pr = null,
                            AgeMinutes = ComputeAgeMinutesFromInstant(ClampToNow(closedAt, now), now),
                            IsInformational = false,
                            DeclaredWriteBackTargets = declaration.DeclaredTargets,
                            RecordPath = relativeRecordPath,
                            RecommendedAction =
                                $"commit and push `{relativeRecordPath}` in the host repo, then re-run stalled-work. "
                                + "The record exists only in this checkout until both steps complete; intent-cli never auto-commits.",
                        });
                    }

                    // A committed record discharges the obligation. If git
                    // status cannot be established (legacy/non-git fixture),
                    // preserve the pre-G661 clearing behavior rather than
                    // manufacturing a dirty finding without evidence.
                    continue;
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    excluded.Add(new StalledWorkExcluded
                    {
                        Kind = KindKnowledgeWritebackPending,
                        ExecutionUnit = executionUnit,
                        Issue = null,
                        Pr = null,
                        Reason = ReasonKnowledgeMetadataUnreadable,
                        Detail =
                            $"`{executionUnit}`: write-back record `{recordPath}` could not be read: "
                            + $"{exception.Message}. Excluded with its path rather than counted as cleared — an "
                            + "unreadable record is not evidence that the write-back happened.",
                    });
                    continue;
                }
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindKnowledgeWritebackPending,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                AgeMinutes = ComputeAgeMinutesFromInstant(ClampToNow(closedAt, now), now),
                IsInformational = false,
                DeclaredWriteBackTargets = declaration.DeclaredTargets,
                RecommendedAction = BuildKnowledgeWritebackPendingAction(executionUnit, declaration),
            });
        }
    }

    private static bool IsGitPathUncommitted(string repoRoot, string relativePath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add("status");
            process.StartInfo.ArgumentList.Add("--porcelain=v1");
            process.StartInfo.ArgumentList.Add("--untracked-files=all");
            process.StartInfo.ArgumentList.Add("--ignored=matching");
            process.StartInfo.ArgumentList.Add("--");
            process.StartInfo.ArgumentList.Add(relativePath);
            if (!process.Start())
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// G564: names the recording command and what the packet promised. The
    /// write-back itself is design's act; the recommendation is to perform it
    /// and then record it — never a state transition this command could run,
    /// and never an auto-write of intent content.
    /// </summary>
    private static string BuildKnowledgeWritebackPendingAction(
        string executionUnit,
        KnowledgeWriteBackDeclaration declaration)
    {
        var facets = string.Join(", ", declaration.RequiredFacets);
        var targets = declaration.DeclaredTargets.Count > 0
            ? string.Join(", ", declaration.DeclaredTargets)
            : "(no target paths named in the packet)";

        return
            $"perform and record the declared knowledge write-back for `{executionUnit}` "
            + $"(declared: {facets}; targets: {targets}) — design writes the tree/ADR/diagram/docs in the host repo, "
            + $"then: `{IntentTreeCoEvolutionDuty.RecordCommand(executionUnit)}`. "
            + "This item stays visible until a record exists; closing the PR does not clear it, and nothing here "
            + "writes intent content on design's behalf.";
    }

    /// <summary>
    /// G645: closed-out units whose packet declared one or more guide routes
    /// but whose host has not recorded the route update. This mirrors the
    /// knowledge-write-back collector deliberately: declaration, closeout,
    /// and recording are separate facts, and an explicit no-surface answer is
    /// silent while an absent declaration is reported as distinguishable
    /// metadata rather than guessed into no debt.
    /// </summary>
    private static void CollectGuideReachabilityPending(
        CliContext context,
        string domain,
        IReadOnlyList<string> candidateDomains,
        string repo,
        DateTimeOffset now,
        DateTimeOffset since,
        List<StalledWorkItem> items,
        List<StalledWorkExcluded> excluded,
        List<string> warnings)
    {
        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            return;
        }

        IReadOnlyList<RunEvent> events;
        try
        {
            events = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or NotSupportedException)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = KindGuideReachabilityPending,
                ExecutionUnit = string.Empty,
                Issue = null,
                Pr = null,
                Reason = ReasonGuideReachabilityMetadataUnreadable,
                Detail =
                    $"the runs log '{runLogPath}' could not be read: {exception.Message}. Guide reachability "
                    + "closeouts cannot be enumerated, so the scan fails closed rather than claiming silence.",
            });
            return;
        }

        var closeouts = events
            .Where(runEvent =>
                string.Equals(runEvent.Event, CloseoutRecordedEvent, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(runEvent.ExecutionUnit))
            .GroupBy(runEvent => runEvent.ExecutionUnit, StringComparer.Ordinal)
            .Select(group => (Unit: group.Key, ClosedAt: group.Min(runEvent => runEvent.Ts)))
            .OrderBy(entry => entry.Unit, StringComparer.Ordinal)
            .ToArray();

        foreach (var (executionUnit, closedAt) in closeouts)
        {
            if (closedAt < since)
            {
                continue;
            }

            if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out _))
            {
                // The knowledge-writeback collector owns the canonical-unit
                // exclusion for this shared closeout event. Do not duplicate
                // that legacy exclusion merely because the G645 detector is
                // also scanning the same runs log.
                continue;
            }

            string packetYamlPath;
            try
            {
                packetYamlPath = GuideReachabilityRecord.ResolvePacketPath(context.RepoRoot, executionUnit);
            }
            catch (InvalidOperationException exception)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindGuideReachabilityPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonGuideReachabilityMetadataUnreadable,
                    Detail = $"packet path could not be resolved: {exception.Message}",
                });
                continue;
            }

            var resolution = new ExecutionUnitResolution(
                executionUnit,
                Corroborated: File.Exists(packetYamlPath),
                IsAmbiguous: false,
                CandidatePacketPaths: Array.Empty<string>());
            if (!TryConfirmDomain(domain, resolution, ReadPacketDeclaredDomain(context, executionUnit), candidateDomains, repo,
                    out var reason, out var detail))
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindGuideReachabilityPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = reason,
                    Detail = detail,
                });
                continue;
            }

            GuideReachabilityDeclaration declaration;
            try
            {
                declaration = GuideReachabilityDeclaration.Read(File.ReadAllText(packetYamlPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindGuideReachabilityPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonGuideReachabilityMetadataUnreadable,
                    Detail =
                        $"'{executionUnit}': packet '{packetYamlPath}' could not be read for guide reachability: "
                        + $"{exception.Message}. The declaration is not treated as absent.",
                });
                continue;
            }

            if (!declaration.IsDeclared)
            {
                // Legacy packets remain quiet in the item/excluded arrays so
                // adding this detector does not rewrite every pre-G645 JSON
                // shape. The warning is the explicit distinction: absent is
                // not the same as the valid no-surface answer.
                warnings.Add(
                    $"'{executionUnit}': packet '{packetYamlPath}' has no guide_reachability declaration; "
                    + "absence is distinct from explicit no_role_facing_surface: true and should be repaired "
                    + "before a role-facing surface is shipped. Paste exactly one accepted YAML form:\n\n"
                    + "Route form:\n"
                    + GuideReachabilityDeclaration.RouteYaml
                    + "\n\nExplicit no-surface form:\n"
                    + GuideReachabilityDeclaration.NoSurfaceYaml);
                continue;
            }

            if (declaration.NoRoleFacingSurface)
            {
                continue;
            }

            string recordPath;
            try
            {
                recordPath = GuideReachabilityRecord.ResolveFullPath(context.RepoRoot, executionUnit);
            }
            catch (InvalidOperationException exception)
            {
                excluded.Add(new StalledWorkExcluded
                {
                    Kind = KindGuideReachabilityPending,
                    ExecutionUnit = executionUnit,
                    Issue = null,
                    Pr = null,
                    Reason = ReasonGuideReachabilityMetadataUnreadable,
                    Detail = $"guide-reachability record path could not be resolved: {exception.Message}",
                });
                continue;
            }

            if (File.Exists(recordPath))
            {
                try
                {
                    _ = GuideReachabilityRecord.Deserialize(File.ReadAllText(recordPath), executionUnit);
                    continue;
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    excluded.Add(new StalledWorkExcluded
                    {
                        Kind = KindGuideReachabilityPending,
                        ExecutionUnit = executionUnit,
                        Issue = null,
                        Pr = null,
                        Reason = ReasonGuideReachabilityMetadataUnreadable,
                        Detail =
                            $"'{executionUnit}': guide-reachability record '{recordPath}' could not be read: "
                            + $"{exception.Message}. An unreadable record is not evidence of clearance.",
                    });
                    continue;
                }
            }

            items.Add(new StalledWorkItem
            {
                Kind = KindGuideReachabilityPending,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                AgeMinutes = ComputeAgeMinutesFromInstant(ClampToNow(closedAt, now), now),
                IsInformational = false,
                DeclaredGuideSurfaces = declaration.Routes.Select(route => route.GuideSurface).Distinct(StringComparer.Ordinal).ToArray(),
                DeclaredGuideRoles = declaration.Routes.Select(route => route.Role).Distinct(StringComparer.Ordinal).ToArray(),
                RecommendedAction = BuildGuideReachabilityPendingAction(executionUnit, declaration),
            });
        }
    }

    private static string BuildGuideReachabilityPendingAction(
        string executionUnit,
        GuideReachabilityDeclaration declaration)
    {
        var routes = string.Join(
            "; ",
            declaration.Routes.Select(route => $"{route.GuideSurface} -> {route.Role} -> {route.TargetSurface}"));

        return
            $"confirm and record the declared guide route(s) for '{executionUnit}' ({routes}) — design updates the "
            + $"named guide in the host, then: '{GuideReachabilityDuty.RecordCommand(executionUnit)}'. "
            + "This is closeout debt, not a merge gate; reachability is never inferred and guide wording is never judged here.";
    }

    /// <summary>
    /// G552: names the exact clarification to answer (design) and the
    /// escalation path (operator). Never an auto-answer: the answer is human
    /// content, and this command only ever emits text.
    /// </summary>
    private static string BuildDesignDecisionPendingAction(ClarificationItem clarification, string domain)
    {
        var summary = SummarizeQuestion(clarification.QuestionText);
        return
            $"answer clarification `{clarification.QuestionId}` on `{clarification.ExecutionUnit}` (\"{summary}\") — "
            + $"design: `intent-cli clarify answer --execution-unit {clarification.ExecutionUnit} "
            + $"--question-id {clarification.QuestionId} --answer \"<decision>\"`; "
            + $"operator: escalate for domain `{domain}` if design is unavailable. "
            + "Never auto-answer: the decision is design's, and a resolution taken under bounded default authority "
            + "must cite its verifying facts and remain amendable by design.";
    }

    /// <summary>
    /// G552: a one-line question summary for the report. Collapses whitespace
    /// (a clarification question is often multi-line prose) and truncates so a
    /// single long question cannot swamp a heartbeat message body.
    /// </summary>
    private static string SummarizeQuestion(string questionText)
    {
        const int MaxLength = 120;

        var collapsed = string.Join(' ', (questionText ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length == 0)
        {
            return "(no question text recorded)";
        }

        return collapsed.Length <= MaxLength
            ? collapsed
            : collapsed[..MaxLength].TrimEnd() + "…";
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

    private static bool MatchesLinkedPr(QueueItem item, string repo, string prToken)
    {
        return int.TryParse(prToken, out var number)
            && GitHubWorkItemIdentity.MatchesPullRequest(item, repo, number);
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
    /// G533 review repair: parses <paramref name="timestamp"/> (an entity's
    /// own <c>updatedAt</c>) for <see cref="KindClaimedButSilent"/>'s
    /// activity determination — deliberately NEVER falls back to a
    /// different field (e.g. <c>createdAt</c>) on failure, unlike
    /// <see cref="ComputeAgeMinutes"/>'s "unparseable means age zero"
    /// convention used by the other (actionable) kinds. Those other kinds
    /// treat a missing timestamp as "just happened" (age 0, never
    /// over-reports staleness); doing the same here — or worse, falling
    /// back to <c>createdAt</c> — would let claimed-but-silent fire (or
    /// under-fire) on evidence that was never actually observed. Returns
    /// <see langword="false"/> with a human-readable <paramref name="problem"/>
    /// string ("missing" or "malformed (could not parse '...')") on
    /// failure, so the caller can fail closed with a structured diagnostic
    /// instead of guessing.
    /// </summary>
    private static bool TryParseActivityTimestamp(string timestamp, out DateTimeOffset instant, out string? problem)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
        {
            instant = default;
            problem = "missing";
            return false;
        }
        if (!DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out instant))
        {
            problem = $"malformed (could not parse '{timestamp}')";
            return false;
        }
        problem = null;
        return true;
    }

    /// <summary>
    /// G533 review repair: a parsed activity timestamp that is somehow in
    /// the future relative to <paramref name="now"/> (clock skew, bad
    /// data) is clamped to <paramref name="now"/> rather than trusted
    /// verbatim — this can only ever make a candidate look LESS silent
    /// (conservative), never manufacture a false <see cref="KindClaimedButSilent"/>
    /// positive from an implausible timestamp.
    /// </summary>
    private static DateTimeOffset ClampToNow(DateTimeOffset instant, DateTimeOffset now) =>
        instant > now ? now : instant;

    private static int ComputeAgeMinutesFromInstant(DateTimeOffset instant, DateTimeOffset now)
    {
        var minutes = (now - instant).TotalMinutes;
        return minutes > 0 ? (int)minutes : 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? repo,
        out int staleMinutes,
        out int claimedSilentMinutes,
        out int backlogIdleMinutes,
        out int repairSilentMinutes,
        out DateTimeOffset? knowledgeWriteBackSince,
        out DateTimeOffset? guideReachabilitySince,
        out string format,
        out string error)
    {
        domain = null;
        repo = null;
        staleMinutes = 0;
        claimedSilentMinutes = DefaultClaimedSilentMinutes;
        backlogIdleMinutes = DefaultBacklogIdleMinutes;
        repairSilentMinutes = DefaultRepairSilentMinutes;
        knowledgeWriteBackSince = null;
        guideReachabilitySince = null;
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
                case "--backlog-idle-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedBacklogIdleMinutes)
                        || parsedBacklogIdleMinutes < 0)
                    {
                        error = "--backlog-idle-minutes requires a non-negative integer.";
                        return false;
                    }
                    backlogIdleMinutes = parsedBacklogIdleMinutes;
                    index++;
                    break;
                case "--repair-silent-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedRepairSilentMinutes)
                        || parsedRepairSilentMinutes < 0)
                    {
                        error = "--repair-silent-minutes requires a non-negative integer.";
                        return false;
                    }
                    repairSilentMinutes = parsedRepairSilentMinutes;
                    index++;
                    break;
                // G564: deliberate opt-in to scanning closeouts older than the
                // activation floor. Retroactive detection is never a default.
                case "--knowledge-writeback-since":
                    if (index + 1 >= args.Length
                        || !DateTimeOffset.TryParse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var parsedSince))
                    {
                        error = "--knowledge-writeback-since requires an ISO-8601 instant (e.g. 2026-08-01T00:00:00Z).";
                        return false;
                    }
                    knowledgeWriteBackSince = parsedSince;
                    index++;
                    break;
                // G645: deliberate opt-in to scanning closeouts older than
                // the reachability detector's activation floor.
                case "--guide-reachability-since":
                    if (index + 1 >= args.Length
                        || !DateTimeOffset.TryParse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var parsedGuideReachabilitySince))
                    {
                        error = "--guide-reachability-since requires an ISO-8601 instant (e.g. 2026-08-07T00:00:00Z).";
                        return false;
                    }
                    guideReachabilitySince = parsedGuideReachabilitySince;
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
        writer.WriteLine($"- backlog_idle_minutes_threshold: {result.BacklogIdleMinutesThreshold}");
        writer.WriteLine($"- stalled: {(result.Stalled ? "true" : "false")}");
        writer.WriteLine($"- items: {result.Items.Count}");
        writer.WriteLine($"- excluded: {result.Excluded.Count}");
        if (result.OperatorAttentionStatus is not null)
        {
            writer.WriteLine($"- operator_attention_status: {result.OperatorAttentionStatus}");
        }
        if (result.OperatorAttentionError is not null)
        {
            writer.WriteLine($"- operator_attention_error: {result.OperatorAttentionError}");
        }
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
                if (item.PrHeadSha is { } headSha)
                {
                    writer.WriteLine($"- pr_head_sha: {headSha}");
                }
                if (item.OwedTransition is { } owedTransition)
                {
                    writer.WriteLine($"- owed_transition: {owedTransition}");
                }
                if (item.ObservedHeadSha is { } observedHeadSha)
                {
                    writer.WriteLine($"- observed_head_sha: {observedHeadSha}");
                }
                if (item.CurrentHeadSha is { } currentHeadSha)
                {
                    writer.WriteLine($"- current_head_sha: {currentHeadSha}");
                }
                if (item.CiWaitState is { } ciWaitState)
                {
                    writer.WriteLine($"- ci_wait_state: {ciWaitState}");
                }
                if (item.CiOutcome is { } ciOutcome)
                {
                    writer.WriteLine($"- ci_outcome: {ciOutcome}");
                }
                if (item.CiBreakdown is { } breakdown)
                {
                    writer.WriteLine(
                        $"- ci_breakdown: passed={breakdown.Passed}, failed={breakdown.Failed}, "
                        + $"skipped={breakdown.Skipped}, pending={breakdown.Pending}, total={breakdown.Total}");
                }
                if (item.DedupeKey is { } dedupeKey)
                {
                    writer.WriteLine($"- dedupe_key: {dedupeKey}");
                }
                if (item.RequiredActor is { } requiredActor)
                {
                    writer.WriteLine($"- required_actor: {requiredActor}");
                }
                if (item.OrchestratorActionable is { } orchestratorActionable)
                {
                    writer.WriteLine($"- orchestrator_actionable: {(orchestratorActionable ? "true" : "false")}");
                }
                if (item.OperatorAttentionRecordId is { } recordId)
                {
                    writer.WriteLine($"- operator_attention_record_id: {recordId}");
                }
                if (item.OperatorAttentionOwner is { } owner)
                {
                    writer.WriteLine($"- operator_attention_owner: {owner}");
                }
                if (item.BlockingReference is { } blockingReference)
                {
                    writer.WriteLine($"- blocking_reference: {blockingReference}");
                }
                // G564: the declared targets belong in the item itself, so a
                // reader knows WHAT the packet promised without opening it.
                if (item.DeclaredWriteBackTargets is { Count: > 0 } declaredTargets)
                {
                    writer.WriteLine($"- declared_write_back_targets: {string.Join(", ", declaredTargets)}");
                }
                if (item.RecordPath is { } recordPath)
                {
                    writer.WriteLine($"- record_path: {recordPath}");
                }
                if (item.DeclaredGuideSurfaces is { Count: > 0 } declaredGuides)
                {
                    writer.WriteLine($"- declared_guide_surfaces: {string.Join(", ", declaredGuides)}");
                }
                if (item.DeclaredGuideRoles is { Count: > 0 } declaredRoles)
                {
                    writer.WriteLine($"- declared_guide_roles: {string.Join(", ", declaredRoles)}");
                }
                if (item.RoutingValues is { Count: > 0 } routingValues)
                {
                    writer.WriteLine(
                        $"- routing_values: {string.Join(", ", routingValues.Select(pair => $"{pair.Key}={pair.Value}"))}");
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

    /// <summary>G544: the <c>--backlog-idle-minutes</c> threshold used to gate <c>backlog-ready-idle</c>.</summary>
    [JsonPropertyName("backlog_idle_minutes_threshold")]
    public required int BacklogIdleMinutesThreshold { get; init; }

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

    [JsonPropertyName("operator_attention_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? OperatorAttentionStatus { get; init; }

    [JsonPropertyName("operator_attention_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? OperatorAttentionError { get; init; }
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
    /// actionable kinds, where <see cref="RecommendedAction"/> names the
    /// canonical action or runnable <c>intent-cli</c> command.
    /// </summary>
    [JsonPropertyName("is_informational")]
    public required bool IsInformational { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }

    /// <summary>G589: exact PR head SHA for CI-aware kinds.</summary>
    [JsonPropertyName("pr_head_sha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PrHeadSha { get; init; }

    /// <summary>G589: pending / all-green / failed normalized terminal state.</summary>
    [JsonPropertyName("ci_outcome")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CiOutcome { get; init; }

    /// <summary>G589: stable pass/fail/skip/pending counts for the exact head.</summary>
    [JsonPropertyName("ci_breakdown")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StalledWorkCiBreakdown? CiBreakdown { get; init; }

    /// <summary>G589: stable watcher key; kind + PR number + exact head SHA.</summary>
    [JsonPropertyName("dedupe_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DedupeKey { get; init; }

    /// <summary>G638: transition owed by a durable CI wait, when present.</summary>
    [JsonPropertyName("owed_transition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwedTransition { get; init; }

    /// <summary>G638: exact head captured when the wait was recorded.</summary>
    [JsonPropertyName("observed_head_sha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ObservedHeadSha { get; init; }

    /// <summary>G638: current GitHub head when the recorded wait is stale.</summary>
    [JsonPropertyName("current_head_sha")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentHeadSha { get; init; }

    /// <summary>G638: pending, terminal, or stale-head durable wait state.</summary>
    [JsonPropertyName("ci_wait_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CiWaitState { get; init; }

    /// <summary>G657: durable record path or declared-label green fallback.</summary>
    [JsonPropertyName("ci_classification_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CiClassificationSource { get; init; }

    /// <summary>
    /// G564: for <see cref="AutomationStalledWorkCommand.KindKnowledgeWritebackPending"/>,
    /// the target paths the packet DECLARED — so the report says what was
    /// promised and where, not merely that something is outstanding. Null for
    /// every other kind.
    /// </summary>
    [JsonPropertyName("declared_write_back_targets")]
    public IReadOnlyList<string>? DeclaredWriteBackTargets { get; init; }

    /// <summary>G661: exact locally recorded but not-yet-committed path.</summary>
    [JsonPropertyName("record_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordPath { get; init; }

    /// <summary>
    /// G645: guide surfaces the packet declared for a role-facing addition.
    /// Null for every other stall kind, including knowledge write-back.
    /// </summary>
    [JsonPropertyName("declared_guide_surfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DeclaredGuideSurfaces { get; init; }

    /// <summary>G645: routing roles paired with the declared guide surfaces.</summary>
    [JsonPropertyName("declared_guide_roles")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DeclaredGuideRoles { get; init; }

    /// <summary>G596: actor that can actually discharge the finding.</summary>
    [JsonPropertyName("required_actor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequiredActor { get; init; }

    /// <summary>G596: false for human obligations; orchestration must route, not act.</summary>
    [JsonPropertyName("orchestrator_actionable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OrchestratorActionable { get; init; }

    [JsonPropertyName("operator_attention_record_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperatorAttentionRecordId { get; init; }

    [JsonPropertyName("operator_attention_owner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperatorAttentionOwner { get; init; }

    [JsonPropertyName("blocking_reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlockingReference { get; init; }

    /// <summary>
    /// G669: the independently observed routing values for a conflict. The
    /// keys identify packet, issue, queue, and PR sources so no disagreement
    /// is collapsed into one guessed branch.
    /// </summary>
    [JsonPropertyName("routing_values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? RoutingValues { get; init; }
}

internal sealed record StalledWorkCiBreakdown
{
    [JsonPropertyName("passed")]
    public required int Passed { get; init; }

    [JsonPropertyName("failed")]
    public required int Failed { get; init; }

    [JsonPropertyName("skipped")]
    public required int Skipped { get; init; }

    [JsonPropertyName("pending")]
    public required int Pending { get; init; }

    [JsonPropertyName("total")]
    public required int Total { get; init; }
}

internal static class StalledWorkCiOutcomes
{
    public const string Pending = "pending";
    public const string AllGreen = "all-green";
    public const string Failed = "failed";
}

internal readonly record struct StalledWorkCiProjection(
    string Outcome,
    StalledWorkCiBreakdown Breakdown);

internal enum StalledWorkCheckOutcome
{
    Pending,
    Passed,
    Failed,
    Skipped,
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
