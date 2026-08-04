# Release Notes — intent-cli v0.11.0

> Prepare-only: this PR creates no GitHub Release, tag, package publish,
> announcement, merge, or post-release version roll. The operator creates a
> Release only after the readiness gate below is satisfied.

## Scope

This minor release contains exactly these verified `main` merges:

- G601 — [PR #1306](https://github.com/J-Tech-Japan/intent-system/pull/1306), merge `237ff790ecf9`.
- G602 — [PR #1308](https://github.com/J-Tech-Japan/intent-system/pull/1308), merge `de80aabf7fb7`.
- G603 — [PR #1310](https://github.com/J-Tech-Japan/intent-system/pull/1310), merge `2912127275eb`.
- G604 — [PR #1312](https://github.com/J-Tech-Japan/intent-system/pull/1312), merge `72ccaba3a859`.
- G605 — [PR #1314](https://github.com/J-Tech-Japan/intent-system/pull/1314), merge `d9afcaa915fa`.

Each commit was verified to resolve on `main`; no other slice is included.
See [v0.10.0](release-notes-v0.10.0.md) for the previously shipped scope.

## Why MINOR

This is a verifiable minor bump, not an assumed one. Compared with `v0.10.0`,
`session-layer marker generate` is a new command absent from that tag. The
per-team topology surface also first ships here: `topology record`, `show`, and
`validate` require an explicit `--domain` (and `--team`) and persist per-team
topology. The version policy reserves a minor bump for a new command surface.

## Operational purpose

v0.11.0 makes recorded truth carry its own identity. A workspace can show which
mode and team it serves from a generated marker bound to recorded truth;
mode-switching produces a concrete migration plan and surfaces residue; a
same-number issue in another domain cannot mutate foreign state; and any number
of teams can share one host's topology surface. The operating guide documents
the measured herdr 0.8.0 baseline and its live-handoff recovery caveats.

## Behaviour changes and migration

1. **Explicit topology identity.** `session-layer topology record`, `show`, and
   `validate` now require `--domain` and `--team`. Invocations that relied on
   configuration `default_domain` now stop with usage guidance; update runbook
   snippets to pass the recorded domain and team.
2. **Machine-local per-team topology.** Topology now lives at
   `.intent-cli/topology/<domain>/<team>.json` with a CLI-owned directory-local
   gitignore. The legacy fixed `role-pane-mapping.json` is read only when the
   new file is absent and warns to re-record; no machine values are auto-copied.
   A new-and-legacy disagreement fails closed in validate, doctor/preflight,
   show, and notify. Migrate by re-recording each team on its machine.
3. **Repo-qualified worker completion.** `worker complete` matches a queue item
   by repo-qualified issue identity. A colliding number from another
   repository is never a match, and cross-domain writes fail closed while
   naming both identities. Corrupted completed linkage is repaired only from
   recorded, merged evidence.
4. **Clearer preflight causes.** A documented empty marker placeholder is the
   informational `marker-not-generated` finding rather than malformed and
   not-ready. When advisory other-mode residue and a structural notify failure
   coexist, the structural failure remains the reported cause.

## Install or upgrade

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.11.0
```

## Release-readiness gate

- [ ] `eng/version.json` is `stableVersion` `0.10.0` / `nextVersion` `0.11.0`.
- [ ] These EN/JA notes name exactly the five verified merges above.
- [ ] The minor-command comparison confirms the new marker-generation and
      explicit-domain topology surfaces are absent from `v0.10.0`.
- [ ] The release/version guards, full suite, diff check, and exact-head CI are green.
- [ ] The operator explicitly approves creating and publishing the v0.11.0 GitHub Release.

## Publishing v0.11.0

After this preparation is merged and every gate is green, the operator may
create [the v0.11.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.11.0).
This PR itself does not publish a package or create that Release.
