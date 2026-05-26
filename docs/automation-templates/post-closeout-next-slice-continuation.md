# Post-Closeout Next-Slice Continuation

This note explains the canonical continuation step after accepted review-closeout.

Accepted review-closeout must hand off into parent next-slice planning. The
flow should not stop at raw merge and closeout when the next child step can be
determined without clarification.

## Required Handoff After Accepted Closeout

After review-closeout accepts the PR, merges it, and closes out the linked
child issue, the next step is:

1. run parent next-slice planning
2. evaluate the resulting continuation state
3. either cut exactly one next child issue or stop cleanly

This continuation stays singular. Automation should not fan out into multiple
sibling issue launches after one accepted closeout.

## Canonical Planning Outcomes

Parent next-slice planning has three canonical outcomes:

- `issue-cut-ready`
- `clarification-required`
- `no-actionable-item`

These outcomes determine whether automation publishes one next issue or stops.

## `issue-cut-ready`

`issue-cut-ready` means the next child slice is fully ready to become one new
child issue.

For this outcome to apply, the slice must be:

- exactly one next child slice
- clarification-free
- issue-cut ready as a standalone child contract

When planning returns `issue-cut-ready`, automation should:

1. publish exactly one standalone child issue
2. add `intent-target` to that issue
3. stop after that single publish

The post-closeout continuation must remain a one-issue handoff. It must not
expand into sibling issue bursts or multi-issue fan-out.

## `clarification-required`

`clarification-required` means the next step cannot be cut safely as a child
issue without additional clarification.

When planning returns `clarification-required`, automation should:

1. stop child issue creation
2. ask for clarification through the parent-side flow
3. avoid inventing or guessing a child issue

Clarification is a clean stop condition, not a reason to create a speculative
issue.

## `no-actionable-item`

`no-actionable-item` means there is no next child slice that should be cut
right now.

When planning returns `no-actionable-item`, automation should:

1. stop cleanly
2. create no child issue
3. leave the flow without forced follow-up work

This outcome is valid even after a successful closeout. Accepted closeout does
not imply that a new child issue must always be created.

## Operator Summary

- Accepted review-closeout must hand off into parent next-slice planning.
- `issue-cut-ready` publishes exactly one standalone child issue and adds
  `intent-target`.
- `clarification-required` stops issue creation and waits for clarification.
- `no-actionable-item` stops cleanly with no child issue.
- Post-closeout continuation stays singular and does not fan out into sibling
  issue launches.

## Operator Notes

- This document is documentation-only and does not change runtime code,
  `intent-cli run`, or automation prompts.
- The canonical continuation step happens after accepted review-closeout, not
  before merge or during repair handling.
- `intent-target` remains part of the automation-ready publish contract for the
  single next child issue when one is cut.
