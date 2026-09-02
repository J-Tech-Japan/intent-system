# Release Notes — intent-cli v0.30.0

> **DRAFT / UNRELEASED.** This is the v0.30.0 contract entry for G780. It does
> not create a tag, GitHub Release, package publish, or workflow change.

## Same-repo claim targets (G780)

Claims now honor the existing same-repository topology declaration. When the
invoking checkout's `.intent-cli/config.toml` has both
`same_repo_topology = true` and a non-empty `metadata_write_branch`, claim
acquire, release, takeover, verification, and the worker store probe all use
`refs/heads/<metadata_write_branch>`. Hosts without that declaration continue
to use the G747 remote default branch unchanged.

The resolver fails closed: a missing or unsafe metadata write branch is an
error, not permission to fall back to the remote default or current checkout
branch. `claim stranded` also supports the reverse G763 migration direction:
on a declared host, records on the remote default are migrated transactionally
to `metadata_write_branch`, with dry-run, receipt verification, and an
unchanged source branch.

G779's rejected-push classification and fields remain intact on either target.
This lets an unprotected `intent-metadata` branch accept claims while a
protected `main` still reports an honest `push-rejected` result when the
same-repo declaration is absent.

## Minor-version justification

This is a minor contract change because an already documented, existing
`[project]` topology declaration now changes the externally observable claim
target and enables same-repository hosts that could not acquire a claim on a
protected product branch. It adds no new configuration key and preserves the
default-topology behavior, but it changes the canonical claim location for a
declared host; that is broader than a patch-level correction.
