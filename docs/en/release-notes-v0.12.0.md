# Release Notes — intent-cli v0.12.0

> Prepare-only: this PR creates no GitHub Release, tag, package publish,
> workflow run, merge, or post-release version roll. The operator creates the
> Release only after the readiness gate below is satisfied.

## Scope

This minor release contains exactly these verified `main` merges:

- G610 — [PR #1324](https://github.com/J-Tech-Japan/intent-system/pull/1324), merge `48204646`.
- G611 — [PR #1328](https://github.com/J-Tech-Japan/intent-system/pull/1328), merge `4f4106f947e5`.
- G612 — [PR #1326](https://github.com/J-Tech-Japan/intent-system/pull/1326), merge `1b1206a56e71`.
- G613 — [PR #1330](https://github.com/J-Tech-Japan/intent-system/pull/1330), merge `f3d0838a1da0`.
- G614 — [PR #1334](https://github.com/J-Tech-Japan/intent-system/pull/1334), merge `a260b63bd4a1`.
- G615 — [PR #1332](https://github.com/J-Tech-Japan/intent-system/pull/1332), merge `940997c6b767`.
- G616 — [PR #1336](https://github.com/J-Tech-Japan/intent-system/pull/1336), merge `21f6fb3c8a3b`.
- G617 — [PR #1338](https://github.com/J-Tech-Japan/intent-system/pull/1338), merge `207a3d2e20e0`.
- G618 — [PR #1340](https://github.com/J-Tech-Japan/intent-system/pull/1340), merge `7f2bb23bd4a5`.
- G619 — [PR #1342](https://github.com/J-Tech-Japan/intent-system/pull/1342), merge `36b89ac9fbfc`.
- G620 — [PR #1344](https://github.com/J-Tech-Japan/intent-system/pull/1344), merge `72878b63ff97`.
- G621 — [PR #1346](https://github.com/J-Tech-Japan/intent-system/pull/1346), merge `a1886218f56c`.
- G623 — [PR #1350](https://github.com/J-Tech-Japan/intent-system/pull/1350), merge `c04e137`.
- G624 — [PR #1352](https://github.com/J-Tech-Japan/intent-system/pull/1352), merge `ccd4f29`.
- G625 — [PR #1354](https://github.com/J-Tech-Japan/intent-system/pull/1354), merge `06f1a71`.
- G626 — [PR #1356](https://github.com/J-Tech-Japan/intent-system/pull/1356), merge `2bb20d3`.
- G627 — [PR #1358](https://github.com/J-Tech-Japan/intent-system/pull/1358), merge `5b86977`.
- G628 — [PR #1360](https://github.com/J-Tech-Japan/intent-system/pull/1360), merge `f464a04`.

These eighteen units were enumerated by running `git log v0.11.1..main`, not
by hand. Every commit in that range is accounted for as one of these eighteen
unit merges, G622's prepare-only notes authoring commit (excluded as the
prepare-only slice), or the residual version roll and guard-fix commits. See
[v0.11.1](release-notes-v0.11.1.md) and [v0.11.0](release-notes-v0.11.0.md)
for the preceding shipped scopes.

## Why MINOR

This is a verifiable minor bump. Compared with `v0.11.1`, the command surfaces
`session-layer topology update-kind`, `session-layer topology retire-legacy`,
`session-layer topology update-field`, `judgment-wait`, and
`automation issue-publish --execution-unit` are new, and a recipe can newly
declare `delivery_method: file-backed`. None of those surfaces exists at
`v0.11.1`; the version policy reserves a minor bump for new command surface.

## Behaviour changes

1. **Supported seat operations.** Change an agent kind through `topology
   update-kind` only when the stated current kind matches; declare a
   previously-absent field through registry-limited `topology update-field`; and
   retire the legacy fixed topology file with `topology retire-legacy` and
   recorded evidence. `record` is unchanged and still refuses conflicts.
2. **File-backed delivery.** A recipe may declare `delivery_method:
   file-backed`; intent-cli writes a durable, addressable task envelope and the
   pane receives only a one-line pointer. With no declaration, inline delivery
   remains unchanged.
3. **Unattended-seat readiness.** An autopilot seat silently auto-denies an
   out-of-allowlist action. READY evidence must prove an allowed action and a
   denial; review evidence must inspect denials rather than treat liveness as
   success.
4. **Documentation guards.** The repository-wide Markdown link/anchor guard
   and rolling Japanese terminology guard now fail CI on regressions.

## Further behaviour changes in G623–G628

5. **Judgment vocabulary (G623).** `judgment-wait` replaces
   `operator-attention`. The old command remains a `deprecate-with-alias`
   compatibility alias through 1.x; its machine output carries a
   `deprecation_warning` naming `judgment-wait`, and removal is only allowed in
   the next MAJOR release.
6. **Transport graduation (G624).** `herdr-only` is no longer preview and is
   preferred because it has fewer dependencies. `agmsg + herdr` remains
   supported and explicitly is not retired; neither transport is called
   primary.
7. **Dispatch outcome vocabulary (G625–G626).** `issue-publish` accepts
   `--execution-unit`, and unresolved work is reported as unresolved. A
   delivery that reaches an observed working transition now reports a
   successful non-terminal state instead of `working-did-not-settle` or
   `not-observed-within-bound`. A caller matching either old name will
   **silently stop matching**; update it to match the machine's
   `working_transition` field.

### Breaking change: legacy fixed-path topology read removed (G627)

Hosts that have only the legacy `role-pane-mapping.json` and no per-team
record now fail closed. The diagnostic names `topology record` and
`topology retire-legacy` as recovery commands; no automatic migration occurs.
This is a breaking change for that legacy-only host population.

8. **Versioning policy (G628).** The 1.0 feature set is frozen at v0.12.0.
   A surface added after the freeze ships as `preview`, is recorded in the
   ledger as `preview-through-1.x`, sits outside the 1.0 compatibility promise,
   and is formalised at a later MAJOR release.

## Operational purpose

v0.12.0 lets a team change seat occupants and declared delivery method without
hand-editing topology, avoids wedging a paste-sensitive agent, makes the
Japanese documentation read as Japanese, and publishes the 1.0 compatibility
promise with its ledger.

## Compatibility promise policy

The v0.12.0 freeze and the post-freeze preview lane are defined in the
[1.0 compatibility promise](1.0-compatibility-promise.md). Use that promise
and its ledger to tell whether a 1.x surface is covered or preview.

## Install or upgrade

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.12.0
```

## Release-readiness gate

- [ ] `eng/version.json` is `stableVersion` `0.11.1` / `nextVersion` `0.12.0`.
- [ ] These EN/JA notes name exactly the eighteen units G610–G621 and G623–G628,
      with the verified PRs and merge commits above; the enumeration comes from
      `git log v0.11.1..main`.
- [ ] The minor comparison confirms `judgment-wait`,
      `automation issue-publish --execution-unit`, the three topology
      subcommands, and declared `delivery_method` are absent from `v0.11.1`.
- [ ] Verify the G627 breaking change is understood: legacy-only hosts fail
      closed and must use `topology record` or `topology retire-legacy`; verify
      the v0.12.0 freeze and preview policy before creating the Release.
- [ ] G475, focused release-note checks, full suite, diff check, and exact-head
      CI are green.
- [ ] The operator explicitly approves creating and publishing the v0.12.0 GitHub Release.

## Publishing v0.12.0

After this preparation is merged and every gate is green, the operator may
create [the v0.12.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.12.0).
This PR itself does not publish a package or create that Release.
