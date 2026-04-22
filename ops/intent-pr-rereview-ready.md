# `intent-pr-rereview-ready` Operator Note

This note explains what `intent-pr-rereview-ready` means in the target-scoped PR repair loop.

## Producer

`intent-pr-rereview-ready` is produced by the request-update runner.

The request-update runner sets this label after it successfully applies the requested repair and pushes the updated PR branch.

## Trigger

The label is applied only after all of the following are true:

- the PR is already in the target-scoped automation lane through `intent-target`
- the PR was previously marked with `intent-pr-request-update`
- the request-update runner picked up the PR and marked active work with `intent-pr-update-in-progress`
- the requested fix was completed and pushed successfully

At that point, the request-update runner removes `intent-pr-update-in-progress`, removes `intent-pr-request-update`, and adds `intent-pr-rereview-ready`.

## Next Consumer

After `intent-pr-rereview-ready` is set, review-closeout should consume the PR next.

The expected next step is:

1. review-closeout selects the rereview-ready PR
2. review-closeout removes `intent-pr-rereview-ready`
3. review-closeout adds `intent-pr-reviewing`
4. review-closeout runs rereview on the updated PR

## How It Differs From Nearby Labels

### `intent-pr-request-update`

- `intent-pr-request-update` means review has found repairable issues and is asking for another implementation pass.
- `intent-pr-rereview-ready` means that requested implementation pass has already completed and the PR is ready to go back into review.

### `intent-pr-update-in-progress`

- `intent-pr-update-in-progress` means the request-update runner is actively working on the fix right now.
- `intent-pr-rereview-ready` means active fix work is finished and the PR has been handed back for the next review step.

## Operator Summary

- Producer: request-update runner
- Set when: a requested fix is pushed successfully
- Next consumer: review-closeout
- Next review step: remove `intent-pr-rereview-ready`, add `intent-pr-reviewing`, then run rereview
