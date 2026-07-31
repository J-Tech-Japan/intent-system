# Release Notes — intent-cli v0.7.0

> **Release model:** a maintainer/operator (or external release automation)
> **creates and publishes the GitHub Release** for `v0.7.0` — the version-bump
> merge does **not** create a Release or tag on its own. Publishing the GitHub
> Release fires `.github/workflows/release.yml` (`on: release: published`), which
> then builds and publishes the NuGet package and platform binary artifacts.
> This packet is **prepare-only**: it authors the notes and adds **no** publish
> steps. See the [pre-merge release-readiness gate](#release-readiness-gate-g562)
> and [publishing v0.7.0](#publishing-v070).

## What's in v0.7.0

v0.7.0 covers exactly the five slices merged after `v0.6.2`: **G559**,
**G560**, **G561**, **G563**, and **G564**.

**Why minor, not patch.** The documented policy reserves a minor bump for a new
command surface. G559 adds one: `intent-cli skill list | install | diff` is a
new top-level command group, not an extension of an existing one. That alone
decides it — G560, G561, G563, and G564 would each have been a patch on their own. Nothing
is removed or renamed, so the bump is minor rather than major: every v0.6.x
command, argument, and flag keeps its shape. The package id remains
`JTechJapan.IntentSystem.Cli`; there are no package id, license, or workflow-
semantics changes.

The headline is the skill surface. The other four close release-flow and
publish-priority machinery gaps that each cost a real incident, reconcile the
guide corpus with the skill this release ships, and make intent-tree
co-evolution enforceable instead of aspirational.

### Cross-platform agent skill, installed by one command (G559)

Claude Code, Codex, and Copilot all read the **same** `SKILL.md` format. Only
the **location** differs. That is exactly why the file gets hand-copied, and
hand-copied skills drift: this project's own host carried the
`host-review-loop` skill as two already-divergent copies under
`~/.claude/skills` and `~/.codex/skills`. Two files claiming to be the same
skill, neither authoritative, is worse than no skill at all — an agent follows
the stale one and reports a workflow the tool no longer runs.

So the skill ships as **one source**, embedded in the tool package at build,
with an installer that puts it in each platform's own location:

```bash
intent-cli skill list                    # every target/scope, and its state
intent-cli skill install --target all    # install everywhere at once
intent-cli skill install --target claude --scope user
intent-cli skill diff --target claude    # what an edited copy changed
```

| Target | Scope(s) | Path |
| --- | --- | --- |
| `claude` | `repo` (default), `user` | `<repo>/.claude/skills/intent-cli/SKILL.md`, `~/.claude/skills/intent-cli/SKILL.md` |
| `codex` | `user` | `~/.codex/skills/intent-cli/SKILL.md` |
| `copilot` | `repo` | `<repo>/.github/skills/intent-cli/SKILL.md` |

**No store, marketplace, or registry step is required on any platform.** All
three discover skills by reading a directory: Claude Code and Codex read their
skill directories, and Copilot reads `.github/skills/` in the consuming
repository. Installing the file **is** the installation — there is nothing to
register, submit, or approve, and `skill install` is the whole process.

**The skill is a dispatcher, not a manual.** It restates none of the workflow.
It carries one rule — *installed guide output wins* — and a table mapping what
you want to do to the `intent-cli guide ...` command that answers it. A skill
file that copies out the workflow is a second source of truth that ages against
the tool, which is the drift problem one level up. The guide surfaces move with
the CLI; a pointer to them does not go stale.

**Install is three-phase and never writes a partial result.** The command
validates every target/scope pair, then resolves every destination and inspects
its state, and only then writes. A drifted destination **anywhere** in the plan
aborts the whole run before any directory is created or any file is written —
so `--target all` cannot install two platforms and error on the third. The
destinations that would have been written report `skipped-plan-aborted`, so it
is visible that they were planned and deliberately left alone.

Two further protections come with it:

- **An edited copy is never replaced silently.** Install compares the installed
  file against the embedded source; on a difference it reports
  `refused-drifted`, leaves the file byte-identical, and **exits non-zero** so a
  script notices. `--force` is the explicit opt-in to replace it. Line-ending
  differences are not drift, so a Windows checkout does not report every install
  as edited.
- **A scope a platform does not define is refused, not written.** `--scope repo`
  for `codex` fails and names the supported scopes. Writing to a
  plausible-looking directory the platform never reads would look like a
  successful install and behave like none.

### Version-agnostic current-state guards, and the roll rule completed (G560)

The v0.6.2 → 0.6.3 roll — the second live execution of the amended rule — turned
child main red again. The rule itself worked; the **guards** did not. Several
documentation checks still pinned the current version by value, so performing a
correct roll broke them.

- **Current-state guards derive from `eng/version.json`.** Every assertion about
  the release being cut now reads the policy instead of a literal, and the
  version-bearing checks are scoped to the active readiness section so they
  cannot be satisfied incidentally by text elsewhere in the file — which is
  exactly how the previous guard passed until a roll exposed it.
- **A roll simulation proves it.** The regression is not "some assertions were
  wrong once", it is that current-state guards flip on **every** roll. So the
  proof is a roll: a temporary bumped `eng/version.json` read through the real
  policy reader, checked with the **same** helper the current-state assertions
  use. A guard that regains a literal fails there even while it still passes
  against today's docs.
- **The version-flow examples became placeholders.** `<stableVersion>` /
  `<nextVersion>` / `<nextPatch>` are not something a roll has to rewrite,
  which is the point of the conversion.
- **The roll rule now has six steps**, adding the readiness-section refresh in
  both language mirrors alongside the same-commit DRAFT stubs and the post-roll
  green-CI check. A roll that bumps the policy but leaves the readiness section
  describing the previous line is not finished.

*(This release is the first live proof of those guards: retargeting from 0.6.3
to 0.7.0 flipped no current-state check.)*

### Publish-priority ordering gets a canonical exit, and `clarify open` works on drafts (G561)

Two machinery gaps surfaced in the same incident, and each had forced a one-off
design ruling to move a single unit.

**A pre-publish block had no canonical way out.** Publish-priority ordering
works by blocking a not-yet-published unit so the selector skips it and the
priority unit goes first. But the two-sided unblock requires a complete
`linked_issue` before touching anything — rightly, since it also converges the
GitHub blocked label — and an unpublished unit has none. A bare queue transition
would move the state and leave `blocked_by` populated, which the selector still
treats as blocked.

```bash
intent-cli automation issue-block <execution-unit> --clear --pre-publish --write
```

converges the queue side only — `state=queued` and `blocked_by` emptied in one
guarded write, with the run-log event naming the wait reason it cleared — and
performs no GitHub interaction at all, because there is no issue to interact
with. It fails closed when the unit has a `linked_issue` (the rule is absolute
absence: even an empty `{repo: "", number: null}` object is refused, since the
object's presence is evidence that something recorded a linkage) and when
`--repo`/`--issue` are supplied, which it can neither verify nor act on. It
requires `--clear`: it is an exit, not a way to block.

**`clarify open` rejected every freshly scaffolded packet.** It deserialized
`packet.yaml` through the full projection contract, which a packet from
`packet draft` does not satisfy — so recording a blocking design question was
impossible at exactly the moment it is most valuable, while the packet is still
a draft and the wrong answer has not yet been built. It now reads only the facts
a clarification record contains. The identity check never relaxes: the packet's
execution unit is still required and must still match the queue item. And a
packet that **declares** a `review_context_packet` section is claiming to be
complete, so it still goes through the unchanged strict serializer — same
required fields, same messages, same failures. Tolerance applies only to a
packet that never claimed completeness.

### Guides stop forbidding the skill this release ships (G563)

A pre-release guide↔intent-tree reconciliation found five coherence defects, all
fixed here: every local-skill prohibition now carries an explicit carve-out for
the CLI-owned `intent-cli` dispatcher skill (workflow-restating local skills stay
forbidden); `guide skill-pack` is retired to a pointer at the `skill` group so
exactly one artifact is named `intent-cli`; `guide commands list` gains the
missing `skill` row; both paste-ready 5-minute fallback prompts carry the
per-receiver delegation cap instead of the superseded "at most one message"
rule; and the provisioning Authority-boundary sentence enumerates the same four
MAY-answer classes the supervision section grants.

### A stale intent tree is now visible work, not a silent fault (G564)

The same pre-release audit that produced G563 is the evidence for this one: G559
shipped while intent-tree node 09 still described a pre-implementation design,
node 02 recorded none of the seven release-flow rules the docs implement, and
node 08 lagged the wake contract by releases. Development moved for weeks with
**no structural signal** that the tree was stale — a manual, operator-ordered
audit was the only detector, and it cost a full review cycle immediately before
a release.

The ingredients already existed and nothing connected them: packets declare
`knowledge_updates.*.required` and `closeout_learning.write_back_required`, and
the write-back itself happens as a host commit the child repo never sees. No
record said "done", so nothing could say "not done".

**Recording.** A new subcommand states that a declared write-back was performed,
with the host commit as evidence:

```bash
intent-cli automation knowledge-writeback-record \
  --execution-unit G564 --commit <host-commit-sha> \
  --target intents/<domain>/intent-tree/means/03-state-and-audit-strategy.md --write
```

It is idempotent for the same commit, refuses a *different* commit rather than
overwriting evidence, and fails closed on an unknown execution unit or
non-SHA evidence. `--dry-run` is the default. It records only — the tree is
written by design, never by tooling.

**Detection.** `automation stalled-work` gains a `knowledge-writeback-pending`
kind, carried by `automation heartbeat`: a closed-out unit whose packet declared
a write-back with no record becomes an actionable, aging item that names the
declared facets and target paths and recommends the recording command. A unit
that declared nothing required never appears; unreadable packet metadata or an
unreadable record is reported in `excluded` **with its path**, never silently
read as "nothing pending". Units closed out before this shipped are out of scope
by default (`--knowledge-writeback-since <iso-8601>` opts into scanning further
back).

**Duty.** The guides now state the operator's 2026-07-31 ruling directly: the
intent tree moves *with* development, and leaving it unupdated while
implementation advances is a serious fault in its own right. Packet-authoring
guidance requires honest declarations for slices that add a surface or change
behavior; closeout guidance performs and records the write-back in the **same**
wake; and the orchestrator's closeout report to the design thread enumerates the
packet's declared write-backs and whether each is recorded or pending.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.0
```

Or download the self-contained binary from the
[v0.7.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.7.0).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.6.2

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.7.0
```

This release is **additive in the CLI surface and corrective in the automation
and release flows**. No command, argument, or flag was removed or renamed.

- **Additive — new `skill` command group.** Nothing that worked before behaves
  differently; there is simply a new group. After upgrading, run
  `intent-cli skill install --target all` once to place the dispatcher skill in
  each platform's own location, then `intent-cli skill list` to confirm. Re-run
  `skill install` after future upgrades to pick up a newer embedded skill;
  `skill diff` shows what an edited copy changed, and `--force` is required to
  replace one.
- **Corrective — automation surfaces.** `automation issue-block` gains
  `--clear --pre-publish`; the existing two-sided block/unblock path is
  unchanged. `clarify open` now succeeds on packets it previously rejected;
  packets it previously accepted are validated exactly as before.
- **Additive — write-back recording and detection (G564).** `automation
  knowledge-writeback-record` is a new subcommand, and `automation stalled-work`
  / `automation heartbeat` gain the `knowledge-writeback-pending` kind. Existing
  kinds, thresholds, and output fields are unchanged. Nothing is reported
  retroactively: only units closed out after this release can produce the new
  item, so an upgrade does not light up historical units.
- **Corrective — release flow only.** G560 changes how the repository's own
  documentation guards are asserted and completes the post-release roll rule. It
  affects maintainers cutting a release, not consumers of the CLI.

No package id, license, or CLI argument/flag shape changes.

## Release-readiness gate (G562)

These items must hold **before the GitHub Release for `v0.7.0` is published**.
This gate fails closed — if any item is unmet, do not publish the Release yet.

- [ ] Every release-bound packet is **complete and its PR merged to `main`**:
      G559 (PR #1224), G560 (PR #1222), G561 (PR #1226), G563
      (PR #1230), and G564 (PR #<G564-PR>), plus the G562 release-prep
      (PR #1228). Confirm on the
      host/review side via the host queue-state /
      GitHub PR state — the child implementation loop must not read parent
      queue-state, so this is a host-owned precondition.
- [ ] **No `v0.6.3` notes remain.** `0.6.3` is a version that will never be cut;
      its DRAFT stubs are removed by this packet so no stale notes file can be
      mistaken for a pending release.
- [ ] No open intent-system PR or WIP packet intended for this release is
      accidentally skipped (check the host queue / open PR list before
      publishing).
- [ ] `eng/version.json` shows `stableVersion` `0.6.2` and `nextVersion` `0.7.0`
      (the intended release version).
- [ ] Package metadata is correct: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` point to
      `https://github.com/J-Tech-Japan/intent-system`,
      `PackageLicenseExpression = Apache-2.0`, README/docs links resolve, and
      the official service site `https://www.intent-driven-development.com/` is
      linked from the README.
- [ ] **Main CI is green** (`Build and test (source contract)`) on the release
      commit, and the **preview-pack** workflow is green.
- [ ] **Post-merge build + pack evidence** on the merge commit is recorded in
      the PR (mirroring the G528/G538/G551/G554/G558 readiness gate).

## Publishing v0.7.0

This packet does **not** publish the release and adds **no** publish steps. The
merge of these notes does **not** create a GitHub Release or tag on its own.

1. After this packet is merged and the readiness gate above holds, a
   **maintainer/operator (or external release automation) creates and publishes
   the GitHub Release** for `v0.7.0` (tagging the release commit). This is a
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.0`)
      then `intent-cli --version` reports `0.7.0`.
- [ ] Binary artifact smoke check: download the platform archive, verify its
      `.sha256`, extract, and run `./intent-cli --version` → `0.7.0`.
- [ ] **Skill smoke** (G559): `intent-cli skill list` names the `intent-cli`
      skill and every target/scope, and `intent-cli skill install --target all`
      places `SKILL.md` in each platform's own location.
- [ ] **Write-back surface smoke** (G564): `intent-cli automation
      knowledge-writeback-record --help` prints its usage, and
      `intent-cli automation stalled-work --help` lists
      `--knowledge-writeback-since`.
- [ ] **ROLL `eng/version.json` NOW**, per the G554 rule as amended by G557 and
      completed by G560: `stableVersion → 0.7.0`, `nextVersion → 0.7.1`, **in
      the same commit as new DRAFT `release-notes-v0.7.1.md` stubs (EN/JA)**,
      with the **"Next release readiness" section refreshed to the new line in
      both language mirrors**, then **verify child main CI is green** before
      calling the roll complete. See
      [Version flow](09-developer-reference.md#version-flow).
- [ ] Notify the operator and downstream consumers that publication **and**
      verification of `v0.7.0` are complete. (The publish request itself belongs
      to the pre-release phase above; by this point the Release is already
      published.)
