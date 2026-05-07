# G279 Parameterized guide prompt rendering

## Why this slice

The previous `intent-cli guide prompt-matrix` always emitted prompts with
`<DOMAIN>` and `<TARGET-REPO>` placeholders, and chat agents had to fill in
domain, target repo, scheduling primitive (`/loop` for Claude vs current-thread
for Codex), and frequency by hand. Stale or vague loop setup keeps surfacing
in exactly that gap.

## What changed

`intent-cli guide prompt-matrix` now accepts:

- `--domain <name>` — substituted into the rendered prompt and first-call
  sequence (already supported).
- `--target-repo <owner/repo>` — substituted into the rendered prompt and
  first-call sequence (already supported).
- `--agent claude|codex|generic` — drives the scheduling primitive line.
  Claude renders a same-thread `/loop <interval>` hint; Codex renders a
  current-thread local automation / heartbeat hint; generic stays neutral.
  Default: `generic`.
- `--frequency <NNm|NNh>` — operator-resolved interval. When provided, the
  rendered prompt names the interval and the agent-specific scheduling
  primitive. When omitted, the rendered prompt explicitly tells the agent
  to ask the operator for the desired frequency before creating any cron,
  monitor, or wakeup; no default interval is silently chosen.

Two new fields are added to the `prompt-matrix` JSON entries: `agent` and
`frequency` (the latter omitted when not resolved).

The host-loop and host-oneshot rendered prompts always include the Stage 3
safe reconcile lane and the Stage 1/2 sequencing introduced in earlier slices.
The host-loop prompt now treats the operator's request to set up the loop as
**pre-approval to publish exactly one next-slice issue per wake** when ALL of:

- `intent next-slice --dry-run` returned `issue-cut-ready`,
- no open `intent-target` issue/PR is in flight (WIP empty),
- no Hard Clarification is open for the candidate, and
- the candidate's standalone Child Issue Contract is complete.

In that case the loop proceeds to `intent-cli packet draft ... --format json`
and `intent-cli issue publish-flow ... --write` without asking for an extra
operator acceptance. If any precondition fails, the loop stops and surfaces
the gap. The host-oneshot prompt still requires explicit operator acceptance
because the operator is presumed to be in the loop for a single execution.

## Stale CLI abort wording

Both host-loop and child-loop rendered prompts include an explicit instruction
to abort the wake before any mutation if the installed CLI surface is stale or
any required automation command is missing. The canonical signal is
`intent-cli automation doctor --format json` (or
`automation host-review-preflight` reporting `stale-host-cli`); the operator
should refresh the installed CLI rather than fall back to raw `gh` label
mutation.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~GuidePromptMatrix"

git diff --check
```

The new focused tests cover: concrete child-loop with `--agent claude` +
`--frequency 5m`; concrete host-loop with `--agent codex` + `--frequency 20m`;
omitted `--frequency` rendering the ask-the-operator instruction (no default);
host-loop pre-approval phrasing; host-oneshot still requiring explicit
operator acceptance; stale-CLI abort wording; invalid `--agent` rejection;
`frequency_guidance` field reflecting the resolved interval; and generic
agent omitting agent-specific scheduling primitives.
