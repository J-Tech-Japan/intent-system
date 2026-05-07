# G289 Continue next-slice after closeout using fresh WIP state

## Why this slice

A SekibanAsAService host wake successfully reviewed, approved, merged,
and closed out PR #498 / SKS-G189, but then stopped with
`wip-cap-blocked` because diagnostics still reported the just-closed
source issue #497 as in-flight. The next packet SKS-G190 was already
`issue-cut-ready`, but no next issue was published. The reviewer had
to step in for what should have been a deterministic chained publish.

## What changed

### Defensive closed-state filter in the diagnostic analyzer

`GitHubAutomationIssueCandidate` and `GitHubAutomationPrCandidate` gain
a `state` field populated from the `state` JSON column added to the gh
list calls (`number,title,url,createdAt,labels,state` for issues;
`number,title,url,body,createdAt,updatedAt,labels,closingIssuesReferences,state`
for PRs).

`AutomationHostReviewDiagnosticsAnalyzer.Analyze` now defensively
excludes any issue with `state == CLOSED` and any PR with `state ∈
{CLOSED, MERGED}` from WIP detection, even when the caller bypassed
`--state open`. The label-only filter (`intent-target`) is preserved on
top of the new state filter.

Empty / missing `state` is treated as open for backward compatibility
with legacy callers (e.g. tests that pre-date this slice). That keeps
the existing `wip-cap-blocked` path intact when state is genuinely
unknown.

### Host-loop guide: post-closeout fresh-state reload

Stage 2 of `guide prompt-matrix --mode host-loop` now names
`intent next-slice --dry-run` as the authoritative `wip` /
`recommended_outcome` source after a closeout, and reminds operators
to refresh local parent state before re-running the gate:

> **Post-closeout fresh-state reload (G289)**: when Stage 1 just
> merged a PR and pushed parent durable state, refresh the host's
> local `queue-state.json`, runs.jsonl, and submodule pointer
> (`git pull --ff-only` on the host repo) BEFORE re-running
> `intent next-slice --dry-run`. The diagnostic's WIP filter already
> excludes closed issues / merged PRs (G289 defensive filter), but
> `intent next-slice --dry-run` reads the local queue-state file and
> is the authoritative `wip` / `recommended_outcome` source for
> Stage 2; treat its `wip: []` + `issue-cut-ready` as the green light
> to proceed even if a stale read of `automation host-review-diagnostics`
> would have said otherwise.

## Boundaries

- Read-only on the diagnostic side: this slice only adds the `state`
  field to candidate records and a defensive filter to the analyzer.
- WIP cap remains the safe default for unattended automation.
- Genuinely-open `intent-target` issues / PRs still produce
  `wip-cap-blocked`. The filter only excludes closed/merged items.
- Hard Clarification, contract completeness, unsafe-metadata, and
  stale-host-cli stops are unchanged.
- Empty / missing `state` is treated as open (backward compat); legacy
  fakes in older tests still behave the same way.
- No raw `gh` label mutation. No change to child loop behavior.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationHostReviewDiagnostics|FullyQualifiedName~GuidePromptMatrix"

git diff --check
```

Four new focused `AutomationHostReviewDiagnosticsCommandTests` cover:

- SKS-G189-shaped sequence: a closed `intent-target` issue with
  `intent-pr-created` retained → not counted as WIP; with
  `--candidate SKS-G190` the wake routes to `issue-publish-ready`.
- Regression guard: a genuinely-open `intent-target` issue still
  produces `wip-cap-blocked`.
- Backward compat: legacy callers passing empty `state` still produce
  `wip-cap-blocked` for items that look open by their labels.
- Defensive: a PR with `state == MERGED` carrying `intent-target` is
  excluded from WIP; the wake routes to `issue-publish-ready`.

One new `GuidePromptMatrixCommandTests` confirms the host-loop prompt
mentions `Post-closeout fresh-state reload (G289)`,
`intent next-slice --dry-run`, and the word `authoritative`.

Full suite: 2097 passed, 1 skipped.
