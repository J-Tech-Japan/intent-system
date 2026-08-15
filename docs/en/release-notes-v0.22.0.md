# Release Notes — intent-cli v0.22.0

> **prepare-only / UNRELEASED.** This document prepares the v0.22.0 release
> body and readiness evidence. It creates no GitHub Release or tag, publishes
> no package, and does not handle credentials. `v0.22.0` is not released by
> this preparation PR.

Install verification after a separately approved release: `JTechJapan.IntentSystem.Cli --version 0.22.0`.
The eventual Release location is
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.22.0.
The preceding shipped scope remains in
[release-notes-v0.21.0.md](release-notes-v0.21.0.md); it is linked, not restated.

## Preview lane — read before the feature description

G695-G708 remain preview-through-1.x surfaces. They are outside the
[1.0 compatibility promise](1.0-compatibility-promise.md) until that promise
is explicitly updated; this release note records the boundary so a consumer
does not infer a stability guarantee from the minor version alone.

## Fourteen merged feature units

v0.22.0 contains exactly fourteen merged feature units, G695 through G708.
This inventory was derived with `git log --first-parent v0.21.0..origin/main`.
When the remote-tracking name is unavailable, the equivalent checked-out-main
verification is `git log --first-parent v0.21.0..main`. That exact first-parent range contains fifteen commits: the post-v0.21.0 roll
and the fourteen merge commits below. Every listed PR is MERGED and every full
merge commit below resolves on `main`.

- G695 — PR #1504; durable continuation-chain guidance emits the next transition or a named blocker, with merge commit `dfb6a539fe5c8c76bf29c54eafb643b63af3e48d` (verified on `main`).
- G696 — PR #1506; per-kind seat command-style guidance is reachable from the installed CLI, with merge commit `1a9cf3a9b733de4ffe600c5d528f0e9b30cf5339` (verified on `main`).
- G697 — PR #1508; topology workspace move is first-class so a team rebuild does not require hand-edits, with merge commit `2021f1d6196fab2b8bb23fb28176f26dddbeb59b` (verified on `main`).
- G698 — PR #1510; role-scoped closeout records let two roles' evidence coexist instead of racing, with merge commit `86f1ffdf9d9704d15d440b21d4db628bff607cf6` (verified on `main`).
- G699 — PR #1512; supervision emission hygiene adds same-key backoff with named park and status debounce, with merge commit `48ca83a0f1cf13080f7ddf04a699f42942d919c9` (verified on `main`).
- G700 — PR #1514; host-state git writes get bounded, observable `index.lock` retry, with merge commit `c2e7d6002a912b2b712a04f0bc4976d6ba76e47b` (verified on `main`).
- G701 — PR #1517; ADR-0006 makes guide primacy normative, adds the registry-backed herdr layout, and defines the three-tier dialog rule, with merge commit `b95a2d7634cdd72b2ef69fce983062aca6dcbab8` (verified on `main`).
- G702 — PR #1520; the npm distribution channel provides `intent-cli` for global installs and npx guidance without self-installing, with merge commit `1746a6d0c2133f7724c57f7a26caed55c93a3e8f` (verified on `main`).
- G703 — PR #1522; `intent-cli update` derives its channel from the executable path and applies a per-channel action, with merge commit `abf6dc640eb3131564d146df9783d453d0e5c70a` (verified on `main`).
- G704 — PR #1526; supervise install validates its setup, names logs, and proves the first cycle, with merge commit `2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd` (verified on `main`).
- G705 — PR #1529; feedback-channel guidance is render-only and never files a GitHub issue, with merge commit `0c49569129635be6a35a07a3e9cfdf3621b44c4c` (verified on `main`).
- G706 — PR #1524; the design-thread terminal rule scopes pane reading to liveness evidence and restores its fallback observation route, with merge commit `be29c896b01df6a48502748e155e07b076c563c6` (verified on `main`).
- G707 — PR #1531; supervision findings are corroborated within their own cycle before escalation, with merge commit `6163d9b3589d331c6a82bb72923a91a15aef029b` (verified on `main`).
- G708 — PR #1533; closeout output reports what it wrote and makes runs gaps explicitly repairable, with merge commit `55d54951b677e8aa6f2d2f0bd49d278ed4e63531` (verified on `main`).

### Full first-parent range accounting

The complete range is accounted for here. The post-v0.21.0 roll is context,
not a release execution unit; the other fourteen rows are the G695-G708 units.

| first-parent commit | meaning |
| --- | --- |
| `8ee71bc81697b91b9e155a52a25b64225ecc7427` | PR #1502, post-v0.21.0 version roll; not a release execution unit |
| `dfb6a539fe5c8c76bf29c54eafb643b63af3e48d` | G695, PR #1504 |
| `1a9cf3a9b733de4ffe600c5d528f0e9b30cf5339` | G696, PR #1506 |
| `2021f1d6196fab2b8bb23fb28176f26dddbeb59b` | G697, PR #1508 |
| `86f1ffdf9d9704d15d440b21d4db628bff607cf6` | G698, PR #1510 |
| `48ca83a0f1cf13080f7ddf04a699f42942d919c9` | G699, PR #1512 |
| `c2e7d6002a912b2b712a04f0bc4976d6ba76e47b` | G700, PR #1514 |
| `b95a2d7634cdd72b2ef69fce983062aca6dcbab8` | G701, PR #1517 |
| `1746a6d0c2133f7724c57f7a26caed55c93a3e8f` | G702, PR #1520 |
| `abf6dc640eb3131564d146df9783d453d0e5c70a` | G703, PR #1522 |
| `2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd` | G704, PR #1526 |
| `0c49569129635be6a35a07a3e9cfdf3621b44c4c` | G705, PR #1529 |
| `be29c896b01df6a48502748e155e07b076c563c6` | G706, PR #1524 |
| `6163d9b3589d331c6a82bb72923a91a15aef029b` | G707, PR #1531 |
| `55d54951b677e8aa6f2d2f0bd49d278ed4e63531` | G708, PR #1533 |

### Origins and minor rationale

The external defects are distinguishable from neighboring measurement and
operator work: G695 follows external defect #1491, G706 follows #1516, G707
follows #1518, and G708 follows #1527. The measured neighboring supervise
stall led to G704. G696-G700 are improvements from the own backlog, while
guide primacy, npm distribution, update channels, and feedback guidance record
operator decisions. The minor version is justified by these fourteen
user-visible orchestration and distribution surfaces; the preview boundary and
the 1.0 compatibility promise remain explicit.

## Distribution boundary — npm is skipped for v0.22.0

npm publication is skipped for v0.22.0 because npm organisation (organization) access and
package-name reservation are incomplete operator account actions. Therefore
the G702 npm publish step does not run for v0.22.0. This is a distribution gap,
not a defect. No credentials are requested or handled, and no package or
registry state is changed by this prepare-only PR. The existing npm entry-point
guidance remains documented; publication is a later operator action after the
account and reservation prerequisites are complete.

## Release-readiness gate (G709)

- [ ] Keep `eng/version.json` at stable `0.21.0` and next `0.22.0` until an
      operator separately approves a release.
- [ ] Verify the fourteen-unit inventory and all fifteen first-parent commits
      with the command above, then run the focused guards and full Release suite.
- [ ] Build the CLI and execute the existing metadata-free
      `intent-cli guide orchestrator-thread` route before following this
      readiness section; the installed guide is the operator/agent entry point.
- [ ] Confirm EN/JA notes and readiness have the same unit, merge, and npm-skip
      contract.
- [ ] Do not create a tag or GitHub Release, publish NuGet or npm, or handle
      credentials in this prepare-only slice.

Publishing v0.22.0 remains an explicitly operator-approved future action. The
G702 npm publish step does not run in this release-preparation cycle; the
incomplete npm organization and package-name reservation remain a distribution
gap, not a defect.
