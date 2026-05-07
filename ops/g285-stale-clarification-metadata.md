# G285 Treat clarified no-blocker files as non-blocking next-slice gates

## Why this slice

After SKS-G186 closeout, SKS-G187 was visible as a complete candidate with
no missing contract sections and `Current Open Blockers: None`, but
`intent-cli intent next-slice --dry-run` returned
`recommended_outcome: clarification-required` because the file's
front-matter still said `intent_state: open`. The frontmatter was stale
metadata, not a product clarification, and the only fix was a manual
`open` → `clarified` edit. A complete candidate plus a body that
explicitly records no current blockers / open questions must not stop a
publish on metadata alone.

## What changed

### `ClarificationOpenDetector` (analysis surface)

`ClarificationOpenDetector.HasOpenBlocker` is preserved as the boolean
predicate every existing caller uses (`IntentStatus`,
`StatusBriefAnalyzer`, `ContextCollectAnalyzer`, `ClarifyDraftAnalyzer`,
`NextSliceClassifyAnalyzer`). A new `Analyze` method returns a
structured `ClarificationStateAnalysis` so callers that need the
diagnostic flags can ask for them without re-parsing.

The analysis covers two sections symmetrically:

- `## Current Open Blockers`
- `## Open Questions`

Each section is scanned for substantive bullets and for explicit
no-blocker / no-question signals. Recognised signals:

- a bare `None` line under the heading
- a bullet `- None` (or `* None`) — newly recognised as a sentinel; the
  pre-G285 detector treated this as a substantive blocker, which was the
  surface bug
- an English bullet starting with `No blockers`, `No open blockers`,
  `No current open blockers`, `No questions`, `No open questions`,
  `No current open questions`, `No current blockers`, or
  `No current questions`
- the established Japanese sentinel
  (`現時点で child issue cut を要する root blocker はない`)
- the existing English fallback
  (`no root blocker requiring child issue cut`)

`StaleClarificationMetadata` is `true` when:

- front-matter `intent_state: open` is set, AND
- `HasOpenBlocker` is `false` (no substantive bullets in either section), AND
- at least one section explicitly records a no-blocker / no-question
  signal.

A file with `intent_state: open` and no sections at all does not
synthesise the warning — there is no explicit "no blockers" signal.

### `intent next-slice --dry-run`

`IntentNextSliceCommand.Analyze` now uses the structured analysis and
adds two fields to the JSON / markdown output:

- `stale_clarification_metadata` — boolean diagnostic mirroring
  `ClarificationStateAnalysis.StaleClarificationMetadata`
- `warnings` — list of structured codes; currently
  `["stale-clarification-metadata"]` when the diagnostic fires

The boolean `clarification_open` reflects the analyzer's decision: when
the body is explicitly cleared, it is `false` even if the front-matter
still says `open`. That means a complete candidate plus empty WIP plus
stale-but-cleared clarification falls through to `issue-cut-ready` and
publishes — with the warning so the host can re-stamp the file later.

### Host-loop / host-oneshot guide

Stage 2 of `guide prompt-matrix --mode host-loop` and
`--mode host-oneshot` now classifies `stale-clarification-metadata`
explicitly:

> When the result includes `warnings: ["stale-clarification-metadata"]`,
> do NOT treat it as Hard Clarification. The `recommended_outcome` is
> the source of truth — if it is `issue-cut-ready`, proceed with publish;
> the warning is a host-side repair hint (re-stamp the clarification
> file's front-matter to `clarified` after the slice publishes), not a
> stop signal. A real Hard Clarification surfaces as
> `recommended_outcome: clarification-required` with substantive blocker
> / question text.

## Boundaries

- Read-only: this slice only changes the read-only `intent next-slice
  --dry-run` analyzer and prompt rendering. No new mutating flag.
- Substantive blocker / open question text still returns
  `clarification-required` — the analyzer does not guess past
  unresolved bullets.
- Candidate contract completeness is unchanged; missing required
  sections still return `clarification-required`.
- WIP cap is unchanged; an in-flight item still returns
  `skip-next-slice-due-to-wip`.
- No raw `gh` mutation; no new command surface for the child loop.
- The `intent_state: clarified` short-circuit (G275) is preserved.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~IntentNextSliceCommandTests|FullyQualifiedName~GuidePromptMatrixCommandTests"

git diff --check
```

Five new focused `IntentNextSliceCommandTests` cover:

- stale `intent_state: open` + body `Current Open Blockers: None` and
  `Open Questions: - None` + complete candidate → `issue-cut-ready` with
  `stale-clarification-metadata` warning
- stale `intent_state: open` + substantive `Open Questions` bullet →
  `clarification-required`, no warning
- bare `- None` bullet alone → not a blocker (the pre-G285 surface bug)
- stale metadata + missing required contract sections →
  `clarification-required` (contract incompleteness wins)
- `intent_state: open` with no body sections → no synthesised warning

Two new `GuidePromptMatrixCommandTests` confirm the host-loop and
host-oneshot prompts mention `stale-clarification-metadata` and
classify it explicitly as non-stop, not Hard Clarification.
