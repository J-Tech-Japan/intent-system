# Request-Update Repair Loop Handoff

This note explains the request-update repair loop for target-scoped PR automation.

## Ordered Repair Loop

The repair loop runs in this order:

1. Review-closeout leaves an actionable review comment on the PR.
2. Review-closeout marks the PR with `intent-pr-request-update`.
3. The child-side request-update runner picks up the PR and performs the fix work.
4. After the fix is pushed successfully, the request-update runner removes
   `intent-pr-request-update`, adds `intent-pr-rereview-ready`, and only then
   hands the PR back for host-side rereview.
5. Host-side review-closeout picks the PR up again for rereview.

## Child-Side Repair Ownership

Child-side repair is owned by the request-update runner.

Its responsibilities are:

- consume a PR that carries `intent-pr-request-update`
- perform the requested child-side fix work
- push the repair commit to the PR branch
- remove the active repair marker when work completes
- remove `intent-pr-request-update` after a successful repair push
- hand the PR back by adding `intent-pr-rereview-ready`

During active repair work, the PR may also carry `intent-pr-update-in-progress`.

## Host-Side Rereview Ownership

Host-side rereview is owned by review-closeout.

After the child-side fix is pushed, review-closeout should:

1. select the PR that now carries `intent-pr-rereview-ready`
2. remove `intent-pr-rereview-ready`
3. add `intent-pr-reviewing`
4. run host-side rereview on the updated PR

This keeps repair execution on the child side and rereview judgment on the host side.

## Label Handoff Summary

- `intent-pr-request-update`: the PR needs child-side repair work
- child-side fix work: the request-update runner is responsible for making and pushing the repair
- `intent-pr-rereview-ready`: the fix was pushed and the PR is ready to re-enter host-side review
- host-side rereview pickup: review-closeout resumes ownership and performs rereview

## Operator Notes

- An actionable review comment is the trigger that starts the request-update repair loop.
- Child-side repair and host-side rereview are separate ownership phases and should not be conflated.
- This document is documentation-only and does not change automation behavior, labels, or scheduling.
