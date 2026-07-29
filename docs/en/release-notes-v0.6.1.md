# Release Notes — intent-cli v0.6.1

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.6.1` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it bumps version metadata and docs and adds
> **no** publish steps. See the
> [pre-merge release-readiness gate](#release-readiness-gate-g554) and
> [publishing v0.6.1](#publishing-v061).

## What's in v0.6.1

v0.6.1 is a **patch release** covering exactly the two slices merged after
`v0.6.0`: **G552** and **G553**. It is a patch rather than a minor because
neither slice ships a new command surface: G553 is a gate bugfix, and G552
extends the existing `automation stalled-work` / `automation heartbeat`
detection surfaces and the orchestrator-thread guide. Minor bumps stay reserved
for new command surfaces and broad behavior changes. The package id remains
`JTechJapan.IntentSystem.Cli`; there are no package id, license, or
workflow-semantics changes.

Both slices close a measured field incident, and field consumers are waiting on
both.

### Design-decision holds are visible and bounded (G552)

A design-thread absence could stall the pipeline invisibly. Field incident,
2026-07-28 16:11 → 07-29 01:29: a review held its final verdict for **nine
hours** on a one-line wording ruling while every technical check was green. The
pending item was mechanically fact-checkable and both threads knew the answer,
but no authority delegation existed — and the hold lived only in agmsg messages,
so `automation stalled-work` reported `stalled: false` throughout. Fourth
design-absence stall in the field record.

- **Clarification-backed holds.** An orchestrator/reviewer hold blocked on a
  design decision is recorded as a clarification artifact through the canonical
  clarify surface. `clarify open` gains optional explicit inputs so the OPEN
  artifact carries the **real** content: `--question` lands in the artifact's
  `QuestionText` and always wins over the packet-derived synthesis, while
  `--recommended-answer` and `--evidence` land in the already-serialized
  `Reason` field under explicit labels. **No clarification schema change**, and
  omitting all three preserves the previous packet-derived behavior exactly. An
  agmsg-only hold is a **contract violation**: a block that exists only as
  messages is invisible to every supervision layer.
- **`design-decision-pending` detection.** `automation stalled-work` reads the
  domain's OPEN clarification artifacts and reports each with its age (from the
  artifact's own `createdAt`), blocking execution unit, and question summary;
  `automation heartbeat` carries it in `message_body` like any other kind. The
  recommendation names the exact clarification to answer plus the operator
  escalation path and **never** auto-answers. This is the only kind with no
  GitHub entity of its own — precisely why the stall it detects was invisible.
  Fails closed both ways: an unreadable artifact is excluded with its path
  rather than skipped as answered, and a clarification whose packet declares a
  different domain is excluded rather than attributed. Answering (or applying,
  or cancelling) clears it.
- **Bounded default authority.** The operator may pre-delegate four enumerated
  decision classes that are settled by *checking repository facts* rather than
  by judgment — count/enumeration corrections, wording corrections entailed by a
  cited fact, cross-reference corrections, and identifier mismatches against a
  named canonical source — each with its verification condition. Bounded in
  every direction: **granted** (never assumed), **enumerated** (that list is the
  whole scope), **evidence-logged** through the existing `clarify record
  --from-file` sink (Question / Decision / Rationale under `## Recently
  Resolved`, which stays readable for post-hoc amendment), **amendable** by
  design afterwards, and **never semantic** — intent shaping, packet content,
  release scope, and prioritization always go to design.
- **Periodic design-reminder loop.** While a clarification stays open the
  orchestrator re-sends a reminder from its existing long-interval automation —
  no new scheduler — at a 30–60 minute class interval, at most one reminder per
  interval per open clarification, stopping when it is answered.
- **Refined reviewer hold rule.** Green technical checks plus a fact-checkable
  non-semantic pending item resolve under granted authority with logged
  evidence; anything else becomes a recorded clarification and a visible pending
  state. There is no third option in which the reviewer simply waits.

### Queue-blocked units no longer starve the WIP gate (G553)

`automation host-review-preflight` counted every OPEN `intent-target` issue
toward `in_flight_issues`, blocked or not. Field finding
(sekiban-as-a-service, 2026-07-26 on 0.5.0): the gate returned
`skip-next-slice-due-to-wip` citing issue #1783, whose unit SKS-G818 had been
parked through the supported claim-preserving block transition. A blocked unit
is parked by design and cannot progress until it is unblocked, so counting it
starves publication exactly when the operator deliberately set work aside. G545
had exempted blocked units from `claimed-but-silent`, but only on the
stalled-work side — this gate was never covered.

- An issue whose queue item is in the **converged blocked state** (queue
  `state=blocked` **and** non-empty `blocked_by`) is excluded from
  `in_flight_issues`, so a next-slice candidate flips from
  `skip-next-slice-due-to-wip` to `candidate-ready` when the only in-flight
  items are blocked.
- **Convergence is required and two-sided.** `state=blocked` with an empty
  `blocked_by`, or a reason on an item that is not blocked, is **drift** per
  G545 — not an exemption. Half-converged items keep counting (fail-closed), and
  each direction emits a warning naming the canonical converging command
  (`automation issue-block … --reason "<why>" --write`, or the same command with
  `--clear --write` to release the unit).
- **The exemption is never silent.** Each exempted unit appears in the new
  `wip_exempt_blocked_units` diagnostics field with its execution unit, issue
  number, and `blocked_by` reasons, in both the JSON and text renderings.
- **Linkage is the queue item's own `linked_issue`** — repo **and** number. A
  blank or missing repo is not a wildcard (issue numbers are unique only within
  a repository), so such an item skips the exemption and says why.
- **Fail-closed on unreadable host state.** A missing queue-state exempts
  nothing silently; an unparseable one exempts nothing and warns. Unblocking
  restores counting on the very next call.
- `intent next-slice` already counted only `active`/`review`/`fixing` items as
  WIP, so blocked units were never counted there — no divergent copy of the rule
  was added.

## Post-release version roll (new flow rule)

A version-flow gap bit the field on 2026-07-29. After the `v0.6.0` Release was
published, `eng/version.json` was not rolled, so previews kept building as
`0.6.0-preview.N` — which sorts **below** the released `0.6.0` in SemVer.
`dotnet tool update` refused the newer build, and a manual uninstall/install was
required.

The fix is a flow rule, now part of the release closeout checklist: **the moment
a GitHub Release is published and verified, roll `eng/version.json` in a
follow-up commit** — `stableVersion` = the version just released, `nextVersion` =
the next patch. See
[Version flow](09-developer-reference.md#version-flow).

**If you track the preview channel**, this is the release that starts the new
discipline: after `v0.6.1` is published, the operator rolls `version.json` to
`stableVersion 0.6.1 / nextVersion 0.6.2` in a follow-up commit, and subsequent
previews build as `0.6.2-preview.N` — above the release, so
`dotnet tool update` works again without an uninstall. Preview artifacts
produced before this rule are **not** renumbered retroactively; if you are
currently pinned to a `0.6.0-preview.N` build, install `0.6.1` explicitly once
the Release is published.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.1
```

Or download the self-contained binary from the
[v0.6.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.1).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.6.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.6.1
```

This release is **additive and corrective within existing surfaces**; there are
no new commands and no argument/flag removals.

- **Additive, no action needed**: `clarify open`'s `--question` /
  `--recommended-answer` / `--evidence` are optional — omitting them preserves
  the previous packet-derived behavior byte for byte. The new
  `design-decision-pending` stalled-work kind and the
  `wip_exempt_blocked_units` diagnostics field add output rather than changing
  existing output.
- **Corrective — existing command behavior changes**:
  - **G553** — `automation host-review-preflight` no longer counts a
    converged-blocked issue toward `in_flight_issues`. A caller that expected
    `skip-next-slice-due-to-wip` while a deliberately parked unit was open will
    now see `candidate-ready`, with the exempted unit named in
    `wip_exempt_blocked_units`. Half-converged items are unchanged (still
    counted) but now emit a repair warning.
  - **G552** — `automation stalled-work` reports a new actionable kind, so a
    caller filtering on `stalled == true` will see `design-decision-pending`
    items where a domain has open clarification artifacts.

No package id, license, or CLI argument/flag shape changes.

## Release-readiness gate (G554)

These items must hold **before the GitHub Release for `v0.6.1` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G552 (PR #1208) and G553 (PR #1210), plus this G554 release-notes prep.
      Confirm on the host/review side via the host queue-state / GitHub PR
      state — the child implementation loop must not read parent queue-state, so
      this is a host-owned precondition.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` shows `stableVersion` `0.6.0` and `nextVersion` `0.6.1`
      (the intended release version). `release.yml` builds the package version
      from the published Release/tag;
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` derives its local default
      from the same policy.
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.
- [ ] **Post-merge build + pack evidence** on the merge commit is recorded in
      the PR (mirroring the G528/G538/G551 readiness gate).

## Publishing v0.6.1

This packet does **not** publish the release and adds **no** publish steps. The
version-bump merge does **not** create a GitHub Release or tag on its own.

1. After this version bump is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.6.1` (tagging the release commit). This is a
   post-merge host/operator/external action.
2. Publishing that GitHub Release fires `.github/workflows/release.yml`
   (`on: release: published`), which builds and publishes the NuGet package and
   the per-platform binary archives (with `.sha256` checksums) and attaches them
   to the triggering Release.

Post-release verification (after the GitHub Release is published and
`release.yml` has run):

- [ ] NuGet.org package page links all resolve correctly.
- [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`) are
      accessible.
- [ ] `.sha256` checksums match the downloaded artifacts.
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` (or
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.1`)
      then `intent-cli --version` reports `0.6.1`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.6.1`.
- [ ] **Design-decision visibility smoke** (G552): `intent-cli clarify open
      --help` shows the `--question` / `--recommended-answer` / `--evidence`
      inputs, and `intent-cli automation stalled-work --domain <d> --repo <r>
      --format json` reports `design-decision-pending` where a domain has an
      open clarification.
- [ ] **WIP-gate smoke** (G553): `intent-cli automation host-review-preflight
      --repo <r> --format json` renders the `wip_exempt_blocked_units` field
      (empty when nothing is parked).
- [ ] **ROLL `eng/version.json` NOW** — per the new post-release rule, advance
      it in a follow-up commit immediately: `stableVersion → 0.6.1`,
      `nextVersion → 0.6.2`, so previews build as `0.6.2-preview.N` and sort
      ABOVE the release. Skipping this is exactly the 2026-07-29 defect this
      release documents. See
      [Version flow](09-developer-reference.md#version-flow).
- [ ] Notify the operator and downstream consumers that publication **and**
      verification of `v0.6.1` are complete — including sekiban-as-a-service-orch,
      for whom the `#1783`-class WIP starvation is fixed once `v0.6.1` is
      installed. (The publish request itself belongs to the pre-release phase
      above; by this point the Release is already published.)
