# Intent Automation Label State Machine

This document explains the label contract used by the target-scoped automation flow in `J-Tech-Japan/intent-system`.

## Purpose of `intent-target`

`intent-target` is the opt-in marker for automation-managed work.

- On Issues, it means the issue may be picked up by the issue runner.
- On PRs, it means the PR stays inside the intent review and request-update loop.
- Automation should ignore Issues and PRs that do not carry `intent-target`.

## Issue Labels

### `intent-target`

- Meaning: the Issue is eligible for automation-managed implementation.
- Applied when: an operator wants the issue runner to consider the Issue.

### `intent-issue-in-progress`

- Meaning: the issue runner is currently working on the Issue.
- Applied when: the issue runner selects an eligible Issue and starts implementation.
- Removed when:
  - implementation is declined because the Issue body is not a sufficient standalone contract
  - implementation, validation, push, or PR creation fails
  - a draft PR is created successfully

### `intent-pr-created`

- Meaning: the issue runner created a draft PR for the Issue.
- Applied when: issue-to-PR automation completes successfully.
- This is the terminal Issue-side automation marker for the current model.

## PR Labels

### `intent-target`

- Meaning: the PR stays in the target-scoped automation lane.
- Source: propagated from the source Issue when issue-to-PR succeeds.

### `intent-pr-reviewing`

- Meaning: review-closeout is currently reviewing the PR.
- Applied when: review-closeout selects an eligible target PR for review.
- Removed when: review-closeout finishes that review pass.

### `intent-pr-request-update`

- Meaning: review-closeout found repairable issues and requested another implementation pass.
- Applied when: review-closeout finishes with actionable findings that should be fixed in the child repo.
- Removed when: the request-update runner completes and pushes the requested fix.

### `intent-pr-update-in-progress`

- Meaning: the request-update runner is actively fixing the PR.
- Applied when: the request-update runner selects a PR with `intent-pr-request-update`.
- Removed when:
  - the fix is pushed successfully
  - the fix cannot be completed
  - clarification is required before a safe fix can be made

### `intent-pr-rereview-ready`

- Meaning: the requested update was pushed and the PR is ready for another review pass.
- Applied when: the request-update runner completes successfully.
- This label indicates a handoff back to review-closeout.

### `intent-pr-approved`

- Meaning: review-closeout accepted the PR and completed its closeout path.
- Applied when: review-closeout finishes with acceptance.
- This is the terminal PR-side automation marker for the current model.

## Transition Summary

Supported mechanical label transitions should be applied through the
installed `intent-cli` command surfaces, not by normal runbook raw
`gh issue edit` / `gh pr edit` label mutation:

```bash
intent-cli automation issue-publish --repo "$CHILD_REPO" --issue "$ISSUE_NUMBER" --write --format json
intent-cli automation pr-transition --repo "$CHILD_REPO" --pr "$PR_NUMBER" --transition review-start --write --format json
intent-cli automation pr-transition --repo "$CHILD_REPO" --pr "$PR_NUMBER" --transition request-update --write --format json
intent-cli automation pr-transition --repo "$CHILD_REPO" --pr "$PR_NUMBER" --transition approved --write --format json
```

Use `intent-cli automation doctor --format json` and
`intent-cli automation host-review-preflight --repo "$CHILD_REPO"
--format json` as stale-CLI/readiness checks before host-side PR
transition mutation. `intent-pr-created` remains issue-only and must not
be applied to PRs by installed commands or fallback paths.

For local smoke testing, run the dry-run examples in
`docs/automation-templates/dry-run-checklist.md` before enabling
command-only host runbooks. The smoke path uses `automation doctor`,
`automation host-review-preflight`, `automation issue-publish` without
`--write`, and `automation pr-transition` without `--write`. Add
`--write` only after the host publish/claim/verdict boundary is valid
for the real issue or PR being transitioned.

## 1. Issue Runner

The issue runner owns the Issue-to-PR transition.

Expected flow:

1. Eligible Issue starts with `intent-target`.
2. Issue runner selects it and adds `intent-issue-in-progress`.
3. Issue runner implements from the Issue body contract and opens a draft PR.
4. On success:
   - Issue removes `intent-issue-in-progress`
   - Issue adds `intent-pr-created`
   - PR carries `intent-target`
5. On decline or failure:
   - Issue removes `intent-issue-in-progress`
   - Issue does not add `intent-pr-created`

## 2. Review-Closeout

Review-closeout owns the review and acceptance or repair request decision.

Expected flow:

1. Eligible target PR is selected for review.
2. Review-closeout adds `intent-pr-reviewing`.
3. Review-closeout performs the review pass.
4. If accepted:
   - PR removes `intent-pr-reviewing`
   - PR adds `intent-pr-approved`
5. If repair is required:
   - PR removes `intent-pr-reviewing`
   - PR adds `intent-pr-request-update`

## 3. Request-Update Runner

The request-update runner owns PR comment repair and the handoff back to review.

Expected flow:

1. Eligible PR has `intent-target` and `intent-pr-request-update`.
2. Request-update runner adds `intent-pr-update-in-progress`.
3. Request-update runner fixes the latest actionable review comment and pushes the branch.
4. On success:
   - PR removes `intent-pr-update-in-progress`
   - PR removes `intent-pr-request-update`
   - PR adds `intent-pr-rereview-ready`
5. If the fix cannot be completed or clarification is needed:
   - PR removes `intent-pr-update-in-progress`
   - PR keeps `intent-pr-request-update`
   - PR does not add `intent-pr-rereview-ready`

## End-to-End Operator View

The normal happy-path state machine is:

1. Issue: `intent-target`
2. Issue: `intent-target` + `intent-issue-in-progress`
3. Issue: `intent-target` + `intent-pr-created`
4. PR: `intent-target`
5. PR: `intent-target` + `intent-pr-reviewing`
6. PR:
   - `intent-target` + `intent-pr-approved`, or
   - `intent-target` + `intent-pr-request-update`
7. If update is requested:
   - PR: `intent-target` + `intent-pr-request-update` + `intent-pr-update-in-progress`
   - PR: `intent-target` + `intent-pr-rereview-ready`
   - PR returns to review-closeout for another pass

## Operator Notes

- `intent-target` is the stable opt-in flag across both Issue and PR phases.
- `*-in-progress` labels are exclusive work markers. If one already exists for the same automation lane, that lane should not start another item.
- Failure cleanup should prefer removing only the active `*-in-progress` label and leaving the source work request visible.
- This document describes the current automation model only. It does not redefine prompts, rename labels, or change automation behavior.
