# Review-Closeout Thread Summary

This note explains what an accepted review-closeout thread summary should
contain after a successful host-side review, merge, and closeout pass.

## Purpose

An accepted closeout thread summary gives operators one compact record of what
review-closeout completed and what happened immediately after acceptance.

The summary should make the accepted result, the linked issue close result, the
continuation outcome, and any next-step publication visible in one place.

## Minimum Summary Fields

An accepted review-closeout thread summary should report these minimum fields:

- reviewed PR number
- accepted / merged result
- linked issue close result
- continuation outcome
- next issue created, if any
- whether `intent-target` was applied to that next issue
- whether a one-step lookahead hint was recorded
- where the lookahead hint was recorded
- label transitions performed on the reviewed PR

## Required Field Meanings

### Reviewed PR Number

The summary should identify which PR review-closeout accepted.

This gives operators a direct reference for the accepted review and merge
outcome.

### Accepted / Merged Result

The summary should explicitly state that the reviewed PR was accepted and
merged.

This confirms that the closeout path finished on the accepted branch rather
than on request-update or transient failure handling.

### Linked Issue Close Result

The summary should state the close result for the linked issue.

This makes the closeout side effect visible alongside the PR acceptance result.

### Continuation Outcome

The summary should explicitly report the continuation outcome chosen after
accepted closeout.

The allowed continuation outcomes are:

- `issue-cut-ready`
- `clarification-required`
- `no-actionable-item`

This field records what happened after accepted closeout instead of leaving the
thread to imply the next step.

### Next Issue Created

If accepted closeout produced a next child issue, the summary should identify
that issue explicitly.

If no next issue was created, the summary should make that absence clear.

Accepted closeout may publish at most one next child issue. The summary should
not imply that multiple sibling issues were launched.

### `intent-target` Reporting

If a next issue was created, the summary should report whether `intent-target`
was applied to that issue.

This confirms whether the next issue entered the automation-ready lane.

### One-Step Lookahead Hint Reporting

The summary should report whether a one-step lookahead hint was recorded.

If a hint was recorded, the summary should also report where that hint was
recorded so operators can inspect it directly.

One-step lookahead hints are advisory only. Their presence must not be reported
as if they created a second speculative issue.

### PR Label Transitions

The summary should record the label transitions performed on the reviewed PR.

This gives operators a concise state-history view for the reviewed PR during
the accepted closeout pass.

## Operator Summary Shape

An operator-readable accepted closeout summary should always cover:

1. which PR was reviewed
2. that the PR was accepted and merged
3. that the linked issue was closed
4. which continuation outcome was selected
5. whether a next issue was created
6. whether `intent-target` was applied to that next issue
7. whether a one-step lookahead hint was recorded and where
8. which PR label transitions were performed

## Operator Notes

- This document is documentation-only and does not change runtime code,
  automation prompts, or labels.
- Continuation outcome reporting is required even when no next issue is
  created.
- Next-issue reporting and `intent-target` reporting should be present together
  whenever accepted closeout publishes one next child issue.
- Lookahead hint reporting should remain informational and should not be framed
  as a second issue launch.
