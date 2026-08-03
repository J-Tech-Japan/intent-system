# Release Notes — intent-cli v0.10.0

> Prepare-only: this PR creates no Release, tag, package publish, announcement,
> merge, or post-release version roll. The operator creates a Release only after
> the readiness gate below is satisfied.

## Scope

This minor release contains exactly these first-parent `main` merges:

- G596 — [PR #1296](https://github.com/J-Tech-Japan/intent-system/pull/1296), merge `4c5aec043cd03f488535cf10021a2afe81c5d328`.
- G598 — [PR #1298](https://github.com/J-Tech-Japan/intent-system/pull/1298), merge `18db1ea2d4a09e175aa1e093598df8fe59c023fb`.
- G597 — [PR #1300](https://github.com/J-Tech-Japan/intent-system/pull/1300), merge `7509cf6be504cdefacba0ae0a1f520f897609769`.
- G599 — [PR #1302](https://github.com/J-Tech-Japan/intent-system/pull/1302), merge `be64197768459a219b233628cfd8ae6932f1068f`.

Each commit was verified as a first-parent ancestor of `main`; no other slice is
included.

## Why MINOR

Command-router comparison of `v0.9.1` and merged `main` shows additions only:
`operator-attention` and its `query`, `resolve`, and `supersede` subcommands are
absent from `v0.9.1`, with no command removal. The documented policy reserves a
minor bump for a new command surface.

## Operational changes

G596/G599 make a blocking obligation a durable, queryable record owed to the
party who must judge it. A design-owned open record reports `operator-required`
with `route_to: design` and `ROUTE TO DESIGN` / `DESIGN REQUIRED`; resolving it
returns `actionable-stall` with the route cleared. Before G599 the same rendering
reported zero pending transitions.

G597 gives a design thread one heartbeat answer: wait, nudge a role, ask the
owner, or repair the monitor. `automation heartbeat` now requires `--team` to
decide; a team-less invocation returns `cannot-determine`, so runbook snippets
must provide the recorded team.

G598 records herdr `delivered` when an unattended working transition is observed.
Settle evidence is separate and carries typed `resend_permitted`; it never
negates the observed delivery.

See [v0.9.1](release-notes-v0.9.1.md) and [v0.9.0](release-notes-v0.9.0.md) for
earlier shipped scope.

## Install or upgrade

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.10.0
```

## Release-readiness gate

- [ ] `eng/version.json` is `stableVersion` `0.9.1` / `nextVersion` `0.10.0`.
- [ ] These EN/JA notes name exactly the four verified merges above.
- [ ] Release/version guards, build/pack, full Release suite, diff check, and
      exact-head CI are green.
- [ ] The operator explicitly approves creating and publishing the v0.10.0
      GitHub Release.

## Publishing v0.10.0

After this preparation is merged and every gate is green, the operator may
create [the v0.10.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.10.0).
This PR itself does not publish a package or create that Release.
