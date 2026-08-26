using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G317: Explicit one-shot task planner surface. A controller thread or
/// operator who already knows the target (issue #N, PR #N, or "publish
/// the next issue") can ask intent-cli for a bounded executable contract
/// instead of starting from a selector loop. Four subcommands:
/// <list type="bullet">
///   <item><c>task issue-to-pr --repo &lt;r&gt; --issue &lt;n&gt; --workdir &lt;path&gt;</c></item>
///   <item><c>task review-pr --repo &lt;r&gt; --pr &lt;n&gt; --domain &lt;d&gt;</c></item>
///   <item><c>task fix-pr-comments --repo &lt;r&gt; --pr &lt;n&gt; --workdir &lt;path&gt;</c></item>
///   <item><c>task publish-next-issue --domain &lt;d&gt; --target-repo &lt;r&gt;</c></item>
/// </list>
///
/// Each planner is read-only: it validates the explicit numbers/paths,
/// builds the exact CLI step sequence the controller should run, and
/// names the label transitions and abort conditions. It does not call
/// <c>gh</c>, does not mutate state, and never launches an AI provider —
/// the steps it returns DO mutate state when the controller chooses to
/// run them. Selector-driven loops (`worker next-action`,
/// `host-review-preflight`, `intent next-slice`) remain valid and
/// unchanged; this surface is the controller-dispatch alternative.
/// </summary>
internal static class TaskCommand
{
    private const string KindIssueToPr = "issue-to-pr";
    private const string KindReviewPr = "review-pr";
    private const string KindFixPrComments = "fix-pr-comments";
    private const string KindPublishNextIssue = "publish-next-issue";

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli task <issue-to-pr|review-pr|fix-pr-comments|publish-next-issue> [--repo <r>] "
        + "[--issue <n>] [--pr <n>] [--workdir <path>] [--domain <d>] [--target-repo <r>] "
        + "[--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 0
            || (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal)))
        {
            WriteHelp(writer);
            return args.Length == 0 ? 1 : 0;
        }

        var kind = args[0];
        var rest = args[1..];

        return kind switch
        {
            KindIssueToPr => PlanIssueToPr(rest, writer),
            KindReviewPr => PlanReviewPr(rest, writer),
            KindFixPrComments => PlanFixPrComments(rest, writer),
            KindPublishNextIssue => PlanPublishNextIssue(rest, writer),
            _ => UnknownKind(kind, writer)
        };
    }

    private static int UnknownKind(string kind, TextWriter writer)
    {
        writer.WriteLine($"Unknown task kind '{kind}'.");
        writer.WriteLine(UsageLine);
        return 1;
    }

    // ---- task issue-to-pr ---------------------------------------------------

    private static int PlanIssueToPr(string[] args, TextWriter writer)
    {
        if (!TryParse(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var refusals = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed.Repo))
        {
            refusals.Add("--repo <owner/repo> is required for task issue-to-pr.");
        }
        if (parsed.Issue is null)
        {
            refusals.Add("--issue <n> is required for task issue-to-pr.");
        }
        if (string.IsNullOrWhiteSpace(parsed.Workdir))
        {
            refusals.Add("--workdir <path> is required for task issue-to-pr (the child implementation worktree).");
        }

        if (refusals.Count > 0)
        {
            return EmitRefusal(KindIssueToPr, parsed, refusals, writer);
        }

        var plan = new TaskPlan
        {
            Task = KindIssueToPr,
            Repo = parsed.Repo,
            Issue = parsed.Issue,
            Pr = null,
            Workdir = parsed.Workdir,
            Domain = parsed.Domain,
            TargetRepo = parsed.TargetRepo,
            Summary = $"Implement {parsed.Repo}#{parsed.Issue}: consume host claim evidence, claim the child issue through the GitHub-only worker path, branch from origin/main in {parsed.Workdir}, run the issue-to-PR workflow on the source URL ONLY, open a ready-for-review PR with `Closes #{parsed.Issue}` (G311), then summarize and complete without a host round trip.",
            Preconditions = new[]
            {
                "Operator/controller has authoritatively selected this issue number — no selector lookup is performed by this planner.",
                $"Workdir `{parsed.Workdir}` is the child-cwd implementation repo (no `.intent-cli/` per G300; presence is fine but child loop must NOT read it).",
                "`gh auth status` is clean for the target child repository and the child GitHub API path is available (G305 abort gates).",
                "The host role has returned execution-unit claim JSON with `status=acquired`, `push_succeeded=true`, matching scope/actor/team and commit, plus `claim verify` evidence `passed=true` / `status=owned`.",
                "The child does not run host `intent-cli` preflight/acquire, read host metadata, use host credentials, or call the host-repository GitHub API; if host evidence is absent, send the exact G733 `intent-cli notify report` request in the implementation steps."
            },
            Steps = new[]
            {
                $"intent-cli worker claim --kind issue --repo {parsed.Repo} --number {parsed.Issue} --github-only --write --format json",
                $"Host-duty request when claim evidence is absent: {ChildHostDutyBoundaryGuidance.Build(parsed.Domain ?? "<DOMAIN>").HostDutyRequest}",
                $"cd {parsed.Workdir} && git fetch origin main && git checkout -b claude/g-issue-{parsed.Issue} origin/main",
                $"# Read GitHub issue body (gh issue view {parsed.Issue} --repo {parsed.Repo} --json title,body,labels) — it IS the implementation contract; do not read repo skill files for routine work.",
                "# Implement the narrow scope; tests + build green.",
                $"cd {parsed.Workdir} && git push -u origin claude/g-issue-{parsed.Issue}",
                $"# Open ready-for-review PR via `gh pr create --repo {parsed.Repo} --base main --title \"...\" --body \"... Closes #{parsed.Issue}\"` (G311 — body MUST contain Closes/Fixes/Resolves #{parsed.Issue}; `worker complete` validates and refuses on missing/wrong/multiple).",
                $"intent-cli automation base-branch-check --repo {parsed.Repo} --pr <new-pr-n> --policy direct-main --actual-base $(gh pr view <new-pr-n> --repo {parsed.Repo} --json baseRefName --jq .baseRefName) --format json",
                $"IS_DRAFT=$(gh pr view <new-pr-n> --repo {parsed.Repo} --json isDraft --jq .isDraft)",
                $"intent-cli worker result-summary --kind issue-to-pr --repo {parsed.Repo} --outcome pr-created --issue {parsed.Issue} --pr <new-pr-n> --pr-draft $IS_DRAFT --format json",
                $"intent-cli worker complete --kind issue --repo {parsed.Repo} --number {parsed.Issue} --outcome pr-created --pr <new-pr-n> --github-only --write --format json"
            },
            LabelTransitions = new[]
            {
                "host claim acquire/verify → execution-unit ownership; the child consumes the returned evidence and does not acquire it.",
                "child worker claim → add `intent-issue-in-progress` (issue side) through the GitHub-only path; no raw label mutation is allowed.",
                "canonical `worker complete --kind issue --outcome pr-created --github-only --write` → swap `intent-issue-in-progress` → `intent-pr-created` on the source ISSUE and apply target-repository `intent-target` to the PR; this command-owned transition is distinct from host-state linkage/publication (G287/G317/G733).",
                "The child never manually applies `intent-target` or `intent-pr-created` to a PR, and never edits host metadata or host-state linkage/publication."
            },
            AbortConditions = new[]
            {
                "Wrong repo: `gh pr view` reports a different `repository.nameWithOwner` than --repo → stop, do not push, surface the gap.",
                "Active lease conflict: `worker claim` returns errors other than the expected linked_pr_synced warning → stop, do not re-claim without operator authorization.",
                "Missing closing reference: `worker complete` refuses because PR body lacks `Closes #" + parsed.Issue + "` → repair PR body and retry; never approve without the reference (G311).",
                "Base branch policy mismatch: `automation base-branch-check` reports a non-`main` base → stop, surface the gap.",
                "`linked_pr_synced: false` from `worker complete` is the EXPECTED child-cwd warning (G300/G330); not an abort. In child-cwd mode the result records `child_cwd: true` and surfaces a pointer to the host-side `review closeout-plan --write-recovered-linkage` recovery path (G329)."
            },
            Notes = new[]
            {
                "`task issue-to-pr` is the explicit-target counterpart to `worker next-action` returning `action: issue-to-pr`. The dispatch contract is identical; only the discovery path differs.",
                "Selector loops remain valid — use `worker next-action` for autonomous polling, this planner for controller-driven dispatch.",
                "Never launch Claude/Codex/AI providers from `intent-cli`; this planner is text-only."
            },
            Prohibitions = GuidanceProhibitionCatalog.All
        };

        WritePlan(plan, parsed.Format, writer);
        return 0;
    }

    // ---- task review-pr -----------------------------------------------------

    private static int PlanReviewPr(string[] args, TextWriter writer)
    {
        if (!TryParse(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var refusals = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed.Repo))
        {
            refusals.Add("--repo <owner/repo> is required for task review-pr.");
        }
        if (parsed.Pr is null)
        {
            refusals.Add("--pr <n> is required for task review-pr.");
        }
        if (string.IsNullOrWhiteSpace(parsed.Domain))
        {
            refusals.Add("--domain <name> is required for task review-pr (G316 packet/intent-aware review).");
        }

        if (refusals.Count > 0)
        {
            return EmitRefusal(KindReviewPr, parsed, refusals, writer);
        }

        var plan = new TaskPlan
        {
            Task = KindReviewPr,
            Repo = parsed.Repo,
            Issue = null,
            Pr = parsed.Pr,
            Workdir = parsed.Workdir,
            Domain = parsed.Domain,
            TargetRepo = parsed.TargetRepo,
            Summary = $"Host-review {parsed.Repo}#{parsed.Pr} for domain `{parsed.Domain}`. Decision branches: pass → approve, then direct merge+closeout or operator-merge patient wait followed by post-human-merge closeout (G678/G297); fail → actionable comment + request-update (G316 classification); host-metadata blockers → recovery via `automation publish-recovery` / `automation reconcile`, never PR comments (G287/G313/G315).",
            Preconditions = new[]
            {
                "Operator/controller has authoritatively selected this PR number — no `host-review-preflight` selector pass is performed by this planner.",
                $"PR is non-draft (G297): `IS_DRAFT=$(gh pr view {parsed.Pr} --repo {parsed.Repo} --json isDraft --jq .isDraft)` → if `true`, classify `draft-merge-blocked`, run `pr-transition --transition review-release --write` to drop `intent-pr-reviewing` cleanly, surface gap. Never apply final approval to a draft PR.",
                "Pre-wake host sync (G304): host has run `automation host-sync-preflight` and either `clean` or `verified-commit-ready` via `durable-state-preflight`; refuse to mutate parent durable state from this planner's contract until that gate is clean.",
                "G316 intent-and-packet-aware review is REQUIRED — passing tests is necessary but NEVER sufficient for approval."
            },
            Steps = new[]
            {
                $"intent-cli automation base-branch-check --repo {parsed.Repo} --pr {parsed.Pr} --policy direct-main --actual-base $(gh pr view {parsed.Pr} --repo {parsed.Repo} --json baseRefName --jq .baseRefName) --format json",
                $"intent-cli review closeout-plan --pr {parsed.Pr} --repo {parsed.Repo} --domain {parsed.Domain} --format json",
                $"intent-cli guide review --pr {parsed.Pr} --repo {parsed.Repo} --domain {parsed.Domain} --format json",
                $"# Read packet_paths (packet.yaml/implementation.md/review-context.md/github-body.md) and intent_reference_paths (PR-specific paths from packet only — never broad domain dirs); cite each in the approval summary.",
                "# Decision A: review passes — emit approval summary satisfying `approval_summary_requirements` (packet contract, AC, OOS, intent reference, tests-pass-paired-with-evidence, G311 Closes ref). Then:",
                $"  intent-cli automation pr-transition --transition approved --repo {parsed.Repo} --pr {parsed.Pr} --write --format json",
                "  # inspect immutable routing_snapshot.landing_mode before any landing action",
                "  # direct: merge via the host's existing merge step, verify merged, then close out",
                $"  # operator-merge: enter awaiting-operator-merge; never merge/urge/age-escalate; after human merge run closeout only",
                $"  IS_MERGED=$(gh pr view {parsed.Pr} --repo {parsed.Repo} --json merged --jq .merged)",
                $"  intent-cli closeout pr --pr {parsed.Pr} --repo {parsed.Repo} --pr-merged $IS_MERGED --write --format json   # only after merged == true",
                "# Decision B: review needs implementation repair (`blocker_classification: implementation-review-finding`) — leave actionable PR comment satisfying `request_update_requirements` (classify each finding implementation-finding | host-metadata-blocked | intent-ambiguity; tie to packet/intent clause). Then:",
                $"  intent-cli automation pr-transition --transition request-update --repo {parsed.Repo} --pr {parsed.Pr} --write --format json",
                "# Decision C: host-metadata-blocked (no queue item, missing linked_issue/linked_pr, etc.) — DO NOT post PR comment (G287). Run recovery:",
                $"  intent-cli automation publish-recovery --repo {parsed.Repo} --format json   # G313/G315 lane",
                $"  intent-cli automation reconcile --lane host-review --repo {parsed.Repo} --format json   # fallback after publish-recovery",
                "  # If reconcile reports unsafe stops, surface a structured operator stop. Release the lease cleanly with `pr-transition --transition review-release --write`."
            },
            LabelTransitions = new[]
            {
                "review-start → add `intent-pr-reviewing` (recommended via host-review-preflight; not part of this explicit planner unless the controller asks).",
                "approved → swap `intent-pr-reviewing` → `intent-pr-approved` on the PR.",
                "request-update → swap `intent-pr-reviewing` → `intent-pr-request-update` on the PR.",
                "review-release (used on draft-merge-blocked or host-metadata-blocked stops) → remove `intent-pr-reviewing` without adding `intent-pr-request-update`."
            },
            AbortConditions = new[]
            {
                "Wrong repo: `closeout-plan` or `guide review` returns a queue item targeting a different repo → stop, surface the gap.",
                "Wrong domain: `guide review` returns an execution unit whose packet does not belong to --domain → stop with structured clarification.",
                "Missing closing reference: PR body lacks `Closes #<source-issue>` → fail review with implementation-finding (G311), never approve.",
                "Unsafe metadata: publish-recovery / reconcile report `unsafe_stops` → surface a structured operator stop, never write parent state.",
                "Tests-only evidence: approval summary has only `tests passed` and no packet/intent citations → request-update, never approve (G316)."
            },
            Notes = new[]
            {
                "`task review-pr` is the explicit-target counterpart to autonomous host-review. Selector-driven host-review-preflight remains valid; this is for controllers who already know the PR.",
                "G316 is non-negotiable: every approval summary must satisfy `guide review` `approval_summary_requirements`.",
                "Never launch AI providers from intent-cli; this planner only emits text the controller runs.",
                "G454/G678: `intent-pr-approved` is intermediate — see `continuation_contract`. Direct lanes merge+closeout; operator-merge lanes wait in visible patient state and resume closeout only after a human merge. No intent-cli path merges an operator-merge lane."
            },
            Prohibitions = GuidanceProhibitionCatalog.All,
            ContinuationContract = HostLoopContinuationContract.Default
        };

        WritePlan(plan, parsed.Format, writer);
        return 0;
    }

    // ---- task fix-pr-comments ----------------------------------------------

    private static int PlanFixPrComments(string[] args, TextWriter writer)
    {
        if (!TryParse(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var refusals = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed.Repo))
        {
            refusals.Add("--repo <owner/repo> is required for task fix-pr-comments.");
        }
        if (parsed.Pr is null)
        {
            refusals.Add("--pr <n> is required for task fix-pr-comments.");
        }
        if (string.IsNullOrWhiteSpace(parsed.Workdir))
        {
            refusals.Add("--workdir <path> is required for task fix-pr-comments (the child implementation worktree).");
        }

        if (refusals.Count > 0)
        {
            return EmitRefusal(KindFixPrComments, parsed, refusals, writer);
        }

        var plan = new TaskPlan
        {
            Task = KindFixPrComments,
            Repo = parsed.Repo,
            Issue = null,
            Pr = parsed.Pr,
            Workdir = parsed.Workdir,
            Domain = parsed.Domain,
            TargetRepo = parsed.TargetRepo,
            Summary = $"Repair {parsed.Repo}#{parsed.Pr} from the latest actionable review/comment ONLY. Checkout the existing PR head in {parsed.Workdir}, apply the narrow requested change, push to the same branch, then `worker result-summary --kind pr-comment-fix --outcome repair-pushed` and `worker complete --kind pr --outcome repair-pushed --write` to swap labels into rereview-ready.",
            Preconditions = new[]
            {
                "Operator/controller has authoritatively selected this PR number; the latest actionable review/comment on this PR is the ONLY source of repair scope.",
                "Do NOT rediscover unrelated work via `worker next-action` while a `pr-comment-fix` claim is active.",
                "PR has `intent-pr-request-update` (or equivalent rereview-blocking label) so a claim transitions cleanly into `intent-pr-update-in-progress`.",
                "`gh auth status` is clean; `intent-cli automation doctor` reports `status: ok`."
            },
            Steps = new[]
            {
                $"intent-cli worker claim --kind pr --repo {parsed.Repo} --number {parsed.Pr} --write --format json",
                $"cd {parsed.Workdir} && git fetch origin && git checkout $(gh pr view {parsed.Pr} --repo {parsed.Repo} --json headRefName --jq .headRefName) && git pull --ff-only",
                $"# Read latest actionable review/comment via `gh pr view {parsed.Pr} --repo {parsed.Repo} --json comments,reviews` — apply ONLY the narrow requested change.",
                "# Tests + build green; do NOT broaden scope, do NOT rebase onto unrelated work.",
                $"git push origin $(gh pr view {parsed.Pr} --repo {parsed.Repo} --json headRefName --jq .headRefName)",
                $"intent-cli worker result-summary --kind pr-comment-fix --repo {parsed.Repo} --outcome repair-pushed --pr {parsed.Pr} --format json",
                $"intent-cli worker complete --kind pr --repo {parsed.Repo} --number {parsed.Pr} --outcome repair-pushed --write --format json"
            },
            LabelTransitions = new[]
            {
                "claim → add `intent-pr-update-in-progress` to the PR (already has `intent-pr-request-update`).",
                "complete repair-pushed → swap `intent-pr-update-in-progress` → `intent-pr-rereview-ready` and remove `intent-pr-request-update` from the PR.",
                "Child loop NEVER applies `intent-target` or `intent-pr-created` from this lane."
            },
            AbortConditions = new[]
            {
                "Wrong PR head: `gh pr view` reports a head SHA different from the local checkout after `git pull` → stop, do not push.",
                "Scope drift: the diff touches files not referenced by the actionable comment → stop, narrow the change before push.",
                "Active lease held by another worker without operator re-claim → stop, surface the gap (G305 abort condition).",
                "`worker complete` returns errors other than the expected linked_pr_synced / child-cwd warning → stop, do not retry blindly. In child-cwd mode the result records `child_cwd: true` (G330) — pr-comment-fix completion does not touch parent durable state."
            },
            Notes = new[]
            {
                "`task fix-pr-comments` is the explicit-target counterpart to `worker next-action` returning `action: pr-comment-fix`. Process at most one repair per wake.",
                "Latest-actionable-comment selection is the controller's responsibility; this planner emits the contract once selection is made."
            },
            Prohibitions = GuidanceProhibitionCatalog.All
        };

        WritePlan(plan, parsed.Format, writer);
        return 0;
    }

    // ---- task publish-next-issue -------------------------------------------

    private static int PlanPublishNextIssue(string[] args, TextWriter writer)
    {
        if (!TryParse(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var refusals = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed.Domain))
        {
            refusals.Add("--domain <name> is required for task publish-next-issue.");
        }
        if (string.IsNullOrWhiteSpace(parsed.TargetRepo))
        {
            refusals.Add("--target-repo <owner/repo> is required for task publish-next-issue.");
        }

        if (refusals.Count > 0)
        {
            return EmitRefusal(KindPublishNextIssue, parsed, refusals, writer);
        }

        var plan = new TaskPlan
        {
            Task = KindPublishNextIssue,
            Repo = parsed.Repo,
            Issue = null,
            Pr = null,
            Workdir = parsed.Workdir,
            Domain = parsed.Domain,
            TargetRepo = parsed.TargetRepo,
            Summary = $"Publish ONE next-slice issue for domain `{parsed.Domain}` against `{parsed.TargetRepo}`. Bounded one-shot: `intent next-slice --dry-run` must return `issue-cut-ready`, no Hard Clarification open, candidate Child Issue Contract complete; then `packet draft --write` → `issue publish-flow --write` → `automation issue-publish --write`. WIP cap is honored unless the operator explicitly requested `--allow-wip-cap-override`.",
            Preconditions = new[]
            {
                "Pre-wake host sync (G304): `automation host-sync-preflight` is `clean` (or has been advanced through `durable-state-preflight` and committed).",
                "Post-merge fresh-state reload (G289): if Stage 1 just merged a PR, `git pull --ff-only` on the host repo BEFORE running `intent next-slice --dry-run`.",
                "WIP cap honored: no open `intent-target` issue/PR remains, OR operator explicitly approved `--allow-wip-cap-override` for queue warming (G288).",
                "No Hard Clarification open for the candidate (`recommended_outcome != clarification-required`); stale-clarification-metadata warnings (G285) are NOT a stop."
            },
            Steps = new[]
            {
                $"intent-cli intent next-slice --dry-run --domain {parsed.Domain} --target-repo {parsed.TargetRepo} --format json",
                "# Confirm `recommended_outcome: issue-cut-ready` (or `wip-cap-overridden` warning when --allow-wip-cap-override is approved).",
                $"intent-cli packet draft --execution-unit <id> --target-repo {parsed.TargetRepo} --dry-run --format markdown",
                $"intent-cli packet draft --execution-unit <id> --target-repo {parsed.TargetRepo} --format json",
                $"intent-cli issue publish-flow <id> --repo {parsed.TargetRepo} --write --format json",
                "# After parent durable state is pushed, apply the published label:",
                $"intent-cli automation issue-publish --repo {parsed.TargetRepo} --issue <new-issue-n> --write --format json"
            },
            LabelTransitions = new[]
            {
                "`automation issue-publish --write` adds `intent-target` to the newly created issue. The host owns this label; child loops never apply it.",
                "No PR-side label transitions — this is an issue-publish task."
            },
            AbortConditions = new[]
            {
                "`intent next-slice --dry-run` returns `clarification-required` with substantive blocker text → run `clarification next` for the structured product-owner question (G310); do not silently publish.",
                "WIP cap blocks: an open `intent-target` issue/PR remains AND the operator did NOT approve queue warming → stop.",
                "Child Issue Contract incomplete (missing AC / OOS / Verification) → repair the contract first; do not publish.",
                "`automation host-sync-preflight` reports `dirty-host-durable-state` without `durable-state-preflight` having converged on `verified-commit-ready` → stop, do not publish on top of a dirty host."
            },
            Notes = new[]
            {
                "`task publish-next-issue` is the explicit one-shot for the host's Stage 2 publish; selector-driven `intent next-slice` remains valid.",
                "At most ONE issue is published per wake; this planner intentionally returns no loop or scheduler instruction."
            },
            Prohibitions = GuidanceProhibitionCatalog.All
        };

        WritePlan(plan, parsed.Format, writer);
        return 0;
    }

    // ---- shared planner emission -------------------------------------------

    private static int EmitRefusal(string task, ParsedArgs parsed, IReadOnlyList<string> refusals, TextWriter writer)
    {
        var plan = new TaskPlan
        {
            Task = task,
            Repo = parsed.Repo,
            Issue = parsed.Issue,
            Pr = parsed.Pr,
            Workdir = parsed.Workdir,
            Domain = parsed.Domain,
            TargetRepo = parsed.TargetRepo,
            Summary = "structured-refusal: required arguments missing or invalid",
            Refusals = refusals,
            Preconditions = Array.Empty<string>(),
            Steps = Array.Empty<string>(),
            LabelTransitions = Array.Empty<string>(),
            AbortConditions = Array.Empty<string>(),
            Notes = new[]
            {
                "Explicit-target planners never guess missing identifiers. Re-run with the required flags supplied.",
                UsageLine
            }
        };
        WritePlan(plan, parsed.Format, writer);
        return 1;
    }

    private static void WritePlan(TaskPlan plan, string format, TextWriter writer)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(plan, JsonOptions));
            writer.WriteLine();
            return;
        }

        WriteMarkdown(plan, writer);
    }

    private static void WriteMarkdown(TaskPlan plan, TextWriter writer)
    {
        writer.WriteLine($"# task {plan.Task}");
        writer.WriteLine();
        if (!string.IsNullOrEmpty(plan.Repo))
        {
            writer.WriteLine($"- repo: `{plan.Repo}`");
        }
        if (plan.Issue is not null)
        {
            writer.WriteLine($"- issue: #{plan.Issue}");
        }
        if (plan.Pr is not null)
        {
            writer.WriteLine($"- pr: #{plan.Pr}");
        }
        if (!string.IsNullOrEmpty(plan.Workdir))
        {
            writer.WriteLine($"- workdir: `{plan.Workdir}`");
        }
        if (!string.IsNullOrEmpty(plan.Domain))
        {
            writer.WriteLine($"- domain: `{plan.Domain}`");
        }
        if (!string.IsNullOrEmpty(plan.TargetRepo))
        {
            writer.WriteLine($"- target-repo: `{plan.TargetRepo}`");
        }
        writer.WriteLine();
        writer.WriteLine($"_Summary:_ {plan.Summary}");
        writer.WriteLine();

        if (plan.Refusals is { Count: > 0 } refusals)
        {
            writer.WriteLine("## Refusals");
            foreach (var refusal in refusals)
            {
                writer.WriteLine($"- {refusal}");
            }
            writer.WriteLine();
        }

        WriteSection(writer, "Preconditions", plan.Preconditions);
        WriteSection(writer, "Steps", plan.Steps);
        WriteSection(writer, "Label transitions", plan.LabelTransitions);
        WriteSection(writer, "Abort conditions", plan.AbortConditions);
        WriteSection(writer, "Notes", plan.Notes);

        if (plan.ContinuationContract is not null)
        {
            writer.WriteLine("## Continuation contract");
            writer.WriteLine(HostLoopContinuationContract.RenderMarkdown(plan.Repo ?? "<r>"));
            writer.WriteLine();
        }
    }

    private static void WriteSection(TextWriter writer, string title, IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }
        writer.WriteLine($"## {title}");
        foreach (var entry in entries)
        {
            writer.WriteLine($"- {entry}");
        }
        writer.WriteLine();
    }

    // ---- parsing -----------------------------------------------------------

    private readonly record struct ParsedArgs(
        string? Repo,
        int? Issue,
        int? Pr,
        string? Workdir,
        string? Domain,
        string? TargetRepo,
        string Format);

    private static bool TryParse(string[] args, out ParsedArgs parsed, out string error)
    {
        string? repo = null;
        int? issue = null;
        int? pr = null;
        string? workdir = null;
        string? domain = null;
        string? targetRepo = null;
        var format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo":
                    if (!Next(args, ref i, "--repo", out repo, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    break;
                case "--issue":
                    if (!NextInt(args, ref i, "--issue", out var issueValue, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    issue = issueValue;
                    break;
                case "--pr":
                    if (!NextInt(args, ref i, "--pr", out var prValue, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    pr = prValue;
                    break;
                case "--workdir":
                    if (!Next(args, ref i, "--workdir", out workdir, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    break;
                case "--domain":
                    if (!Next(args, ref i, "--domain", out domain, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    break;
                case "--target-repo":
                    if (!Next(args, ref i, "--target-repo", out targetRepo, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    break;
                case "--format":
                    if (!Next(args, ref i, "--format", out var requested, out error))
                    {
                        parsed = default;
                        return false;
                    }
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        parsed = default;
                        return false;
                    }
                    format = requested!;
                    break;
                default:
                    error = $"Unknown argument '{args[i]}'.";
                    parsed = default;
                    return false;
            }
        }

        parsed = new ParsedArgs(repo, issue, pr, workdir, domain, targetRepo, format);
        return true;
    }

    private static bool Next(string[] args, ref int i, string flag, out string? value, out string error)
    {
        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
        {
            value = null;
            error = $"{flag} requires a value.";
            return false;
        }
        value = args[++i].Trim();
        error = string.Empty;
        return true;
    }

    private static bool NextInt(string[] args, ref int i, string flag, out int value, out string error)
    {
        if (!Next(args, ref i, flag, out var raw, out error))
        {
            value = 0;
            return false;
        }
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value)
            || value <= 0)
        {
            error = $"{flag} must be a positive integer (got '{raw}').";
            return false;
        }
        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("task — explicit one-shot planners for controller-driven dispatch (G317).");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Subcommands:");
        writer.WriteLine($"- {KindIssueToPr} --repo <r> --issue <n> --workdir <path> [--format markdown|json]");
        writer.WriteLine($"- {KindReviewPr} --repo <r> --pr <n> --domain <d> [--format markdown|json]");
        writer.WriteLine($"- {KindFixPrComments} --repo <r> --pr <n> --workdir <path> [--format markdown|json]");
        writer.WriteLine($"- {KindPublishNextIssue} --domain <d> --target-repo <r> [--format markdown|json]");
        writer.WriteLine();
        writer.WriteLine("Each subcommand returns a bounded executable contract: preconditions, steps, label transitions, abort conditions, and notes. The planner is read-only — it does not call gh, does not mutate state, and never launches an AI provider.");
    }
}

/// <summary>
/// G317: structured planner output for a `task &lt;kind&gt;` invocation.
/// </summary>
internal sealed record TaskPlan
{
    [JsonPropertyName("task")]
    public required string Task { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("issue")]
    public int? Issue { get; init; }

    [JsonPropertyName("pr")]
    public int? Pr { get; init; }

    [JsonPropertyName("workdir")]
    public string? Workdir { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("refusals")]
    public IReadOnlyList<string>? Refusals { get; init; }

    [JsonPropertyName("preconditions")]
    public required IReadOnlyList<string> Preconditions { get; init; }

    [JsonPropertyName("steps")]
    public required IReadOnlyList<string> Steps { get; init; }

    [JsonPropertyName("label_transitions")]
    public required IReadOnlyList<string> LabelTransitions { get; init; }

    [JsonPropertyName("abort_conditions")]
    public required IReadOnlyList<string> AbortConditions { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>
    /// G323: structured prohibitions list. Every task planner advertises
    /// the same canonical safety prohibitions
    /// (<see cref="GuidanceProhibitionCatalog"/>) so controllers can
    /// dispatch on the structured <c>id</c> values without parsing
    /// prose, and tests can assert each planner output carries the
    /// guardrails.
    /// </summary>
    [JsonPropertyName("prohibitions")]
    public IReadOnlyList<GuidanceProhibition>? Prohibitions { get; init; }

    /// <summary>
    /// G454: the canonical host-loop continuation contract. Present on
    /// <c>task review-pr</c> so a controller dispatching a review knows that
    /// <c>intent-pr-approved</c> is intermediate (not terminal), that
    /// approval continues to merge + closeout in the same wake, how to repair
    /// and retry a recoverable blocker once, and how to classify a wake that
    /// stopped after a partial label transition.
    /// </summary>
    [JsonPropertyName("continuation_contract")]
    public HostLoopContinuationContractModel? ContinuationContract { get; init; }
}
