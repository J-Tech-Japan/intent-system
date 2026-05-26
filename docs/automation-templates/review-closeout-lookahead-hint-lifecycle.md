# Review-Closeout Lookahead Hint Lifecycle

This note explains how later accepted review-closeout passes should treat a
previously recorded one-step lookahead hint.

One-step lookahead hints are advisory only. They are not issued work items and
must not create a second speculative child issue.

## Lifecycle Purpose

The purpose of the hint lifecycle is to keep later accepted closeout summaries
aligned with the latest accepted baseline instead of repeating stale next-step
language.

A later accepted pass should re-evaluate an older hint against the latest
accepted baseline before carrying that hint forward.

## Required Baseline Re-Read

Before reusing any previously recorded lookahead hint, the later accepted pass
must re-read the latest accepted baseline.

That re-read determines whether the older hint is still current, should be
superseded by a narrower continuation, or should be discarded because the
latest accepted baseline no longer supports actionable follow-up.

The latest accepted baseline is authoritative. A prior hint is not.

## Lifecycle States

A previously recorded one-step lookahead hint has three allowed lifecycle
outcomes in a later accepted pass:

- reuse
- supersede
- discard

## Reuse

Reuse is correct when the latest accepted baseline still supports the same next
likely continuation that the older hint described.

When a later pass reuses a hint, operators should read that as:

- the latest accepted baseline was re-read
- the older hint still matches the current accepted state
- no narrower or conflicting continuation replaced it

Reusing a hint does not create a second child issue. It only carries forward an
advisory expectation that remains current.

## Supersede

Supersede is correct when the latest accepted baseline now points to a newer or
narrower likely continuation than the older hint described.

When a later pass supersedes a hint, operators should read that as:

- the older hint is no longer the best current summary of likely continuation
- the newer accepted baseline provides a more accurate next-step expectation
- parent source of truth should be updated to reflect the newer hint

Superseding a hint updates the advisory record only. It must not create a
second speculative child issue or imply multi-issue fan-out.

## Discard

Discard is correct when the latest accepted baseline shows that the older hint
should no longer be carried forward at all.

This is appropriate when:

- the latest accepted baseline now leads to `no-actionable-item`
- the latest accepted baseline now requires a different continuation that makes
  the older hint obsolete
- the older hint no longer reflects the accepted state well enough to remain
  useful

Discarding a hint is a valid clean outcome. It means the advisory hint is no
longer current, not that the earlier pass was incorrect at the time.

## Operator Summary Guidance

When a later accepted pass reports lookahead hint handling, operators should be
able to tell:

1. that the latest accepted baseline was re-read
2. whether the prior hint was reused, superseded, or discarded
3. that any hint treatment remained advisory only
4. that no second speculative child issue was created

## Operator Notes

- This document is documentation-only and does not change runtime code,
  prompts, or issue publication rules.
- A prior lookahead hint should never override the latest accepted baseline.
- Supersede and discard are both valid outcomes when the accepted state has
  moved forward.
- Reuse is only correct when the later accepted pass has confirmed that the
  older hint is still current after re-reading the latest accepted baseline.
