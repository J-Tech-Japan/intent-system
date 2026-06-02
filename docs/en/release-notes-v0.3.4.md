# Release Notes — intent-cli v0.3.4

> **Release checklist for maintainers:** see [Creating the v0.3.4 GitHub Release](#creating-the-v034-github-release).
> **Do NOT tag until the [release-readiness gate](#release-readiness-gate-g446) passes.**

## What's in v0.3.4

v0.3.4 is a reliability and host-loop-guidance release. It collects the
release-blocking first-run and CI fixes plus the host-loop / review policy
hardening completed after the corrected `v0.3.3` release. No package id,
license, or workflow-semantics changes. The package id remains
`JTechJapan.IntentSystem.Cli`.

### First-run host initialization deadlock fix (G441)

- `intent init-tree --write` now scaffolds `intents/<domain>/automation/bindings.md`
  with a recognized `execution_unit_regex` (and `child_repo` when known), so a
  freshly initialized host is recognized by `next-slice`, `host-check`, and
  `automation summary` without hand-authoring.
- `intent init --write` now scaffolds the durable-state skeletons
  `.intent-cli/queue-state.json` (empty, schema 1) and `.intent-cli/runs.jsonl`,
  so `host-check` reports `ok` instead of `partially-initialized` on a brand-new
  host.
- First-run docs steer agents to ask intent-cli for the next action rather than
  read source or hand-author `bindings.md`.

### Release CI stabilization (G443)

- The installed-CLI surface probe (`AutomationInstalledCliSurfaceProbe`) now
  retries on the Linux `Text file busy` (ETXTBSY) exec race and degrades a
  persistent failure to a `missing` surface instead of crashing the command —
  fixing the two flaky tests that blocked the earlier `v0.3.3` release CI.
- Release/CI/preview workflows write uniquely named `*.trx` (`LogFilePrefix`)
  so per-project results no longer overwrite one shared file.

### Host-loop scheduler invariant + duplicate-publish guard (G444)

- `guide prompt-matrix` host-loop guidance states the safe scheduling invariant:
  exactly one active wake per host repo + domain. A 5-minute same-thread
  sequential loop is allowed; independent concurrent schedulers are not. Agents
  proceed instead of stopping for scheduler-policy confirmation when the
  invariant is satisfiable.
- `automation host-loop-next-action` adds `stale-next-slice-reconcile`: when
  `next-slice` reports `issue-cut-ready` for an execution unit that GitHub
  already has an open issue/PR for (token-boundary matched), it routes to
  `automation reconcile --lane next-slice` instead of publishing a duplicate.

### Device-gated review evidence policy (G445)

- `guide review` emits a standing `device_gated_evidence_policy`: when to
  approve-with-recorded-gap vs hard-block for device/operator/hardware-gated
  acceptance criteria, the no-false-claim rule, durable follow-up tracking, and
  not re-asking the standing-policy question per packet.

> Version metadata note: G442 advanced the development version source to the
> 0.3.4 line (`eng/version.json` `stableVersion: 0.3.3`, `nextVersion: 0.3.4`);
> G446 (this packet) is the release-readiness gate.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.4
```

Or download the self-contained binary from the
[v0.3.4 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.4).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.3

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.4
```

There are no breaking changes from v0.3.3.

## Release-readiness gate (G446)

Do not create the `v0.3.4` tag/release until ALL of the following hold
(this gate fails closed — if any item is unmet, stop and do not tag):

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G441, G443, G444, G445 (and G442 version bump, G446 this prep). Confirm
      on the host/review side via the host queue-state / GitHub PR state — the
      child implementation loop must not read parent queue-state, so this is a
      host-owned precondition.
- [ ] `eng/version.json` `nextVersion` is `0.3.4` (the intended release version)
      and matches the tag to be created (`v0.3.4`). The release workflow derives
      the package version from the tag; `-p:Version=` overrides the static
      `<Version>` in `src/IntentSystem.Cli/IntentSystem.Cli.csproj`.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.

## Creating the v0.3.4 GitHub Release

1. Confirm the [release-readiness gate](#release-readiness-gate-g446) — do not
   proceed if any item is unmet.
2. Tag the release commit: `git tag v0.3.4 && git push origin v0.3.4`.
3. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums (version derived from the tag). Wait for it to complete green.
4. The workflow creates the GitHub Release draft. Review it, paste the content
   of this file as the release body, and publish.
5. Confirm the NuGet publish step pushed `JTechJapan.IntentSystem.Cli 0.3.4`.
6. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly.
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
         accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` then
         `intent-cli --version` reports `0.3.4`.
   - [ ] Local preview/dry-run version metadata uses the next development line
         after `0.3.4` (bump `eng/version.json` per the post-release step in
         [Version flow](09-developer-reference.md#version-flow)).
