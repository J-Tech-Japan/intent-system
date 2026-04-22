# Intent-Target Publish Boundary

This note explains the publish boundary for `intent-target` after accepted
closeout creates a next child issue.

`intent-target` is not the default label that must exist at issue creation
time. It is the final publish boundary that makes a newly created child issue
visible to downstream automation.

## Why The Boundary Exists

After accepted closeout, parent-side state must be updated before the next
child issue becomes automation-visible.

That ordering matters because downstream automation should not observe a next
child issue while parent queue, runs, or clarification state is still only
partially updated.

## Required Publish Order

The intended publish order for a newly created next child issue is:

1. create the issue first without `intent-target`
2. update parent queue, runs, and clarification state
3. make the parent updates durable
4. add `intent-target` last

Only the final step publishes the issue into the automation-visible lane.

## Step Meanings

### 1. Create The Issue First

The issue may be created before it is automation-visible.

At this point, the issue exists, but it is not yet published for downstream
automation pickup because `intent-target` has not been applied.

### 2. Update Parent State

After issue creation, parent source of truth should be updated first.

This includes the parent queue, runs, and clarification-related state that must
reflect the new continuation before child automation is allowed to observe it.

### 3. Make Parent State Durable

Parent updates must be durable before publication.

Operators should read this as the point where parent state is no longer a
partial in-flight trace and can safely support downstream automation behavior.

### 4. Add `intent-target` Last

`intent-target` is applied only after the parent updates are durable.

This final label application is the publish boundary. It is the step that makes
the issue visible to label-driven automation.

## Operator Interpretation

Operators should read `intent-target` on a newly created next child issue as a
signal that:

- the issue already exists
- the parent queue / runs / clarification state was updated first
- those parent updates were made durable
- the issue is now safe for downstream automation to observe

## Why Label-Last Matters

Create-first and label-last ordering avoids exposing half-updated parent state
to child automation.

If `intent-target` were applied before parent state became durable, downstream
automation could observe the issue too early and act against incomplete queue,
runs, or clarification trace.

## Operator Notes

- This document is documentation-only and does not change runtime code,
  prompts, or label names.
- `intent-target` remains the opt-in label for automation-owned issues and PRs.
- This note only fixes publish ordering after accepted closeout creates a next
  child issue.
- The publish boundary does not authorize creating multiple child issues from
  one accepted closeout.
