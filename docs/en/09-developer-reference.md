# Developer reference

> English version. 日本語版: [`../ja/09-developer-reference.md`](../ja/09-developer-reference.md)

This page covers install options, packaged invocation smoke testing, the preview
channel, and the version policy. It is aimed at maintainers, contributors, and
power users — not at beginners following the [Quickstart](../../README.md#quickstart).

---

## Install without a .NET SDK

Each [GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest)
attaches SDK-free, self-contained binaries (the .NET runtime is bundled, so no
SDK is required).

| Platform | Asset |
| --- | --- |
| macOS (Apple Silicon) | `intent-cli-<version>-osx-arm64.tar.gz` |
| Windows (x64) | `intent-cli-<version>-win-x64.zip` |
| Linux (x64) | `intent-cli-<version>-linux-x64.tar.gz` |

Each archive ships with a `.sha256` sidecar; download both files into the same
directory and verify before use.

**macOS:**

```bash
# 1. Verify (run from the folder containing both files).
shasum -a 256 -c intent-cli-<version>-osx-arm64.tar.gz.sha256

# 2. Extract and place the binary on your PATH.
tar -xzf intent-cli-<version>-osx-arm64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. Confirm.
intent-cli --version
```

**Linux:**

```bash
# 1. Verify.
sha256sum -c intent-cli-<version>-linux-x64.tar.gz.sha256

# 2. Extract and place on PATH.
tar -xzf intent-cli-<version>-linux-x64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. Confirm.
intent-cli --version
```

**Windows:** Download `intent-cli-<version>-win-x64.zip` and its `.sha256` sidecar.
Compare the hash from `CertUtil -hashfile intent-cli-<version>-win-x64.zip SHA256`
against the first field in the `.sha256` file, unzip, and place `intent-cli.exe`
on your `PATH`.

Release binaries and OSS preview CI artifacts carry no build-time expiry.

### Japanese / non-UTF-8 Windows consoles (G484)

intent-cli reads the GitHub CLI (`gh`) subprocess output as **UTF-8 regardless
of the ambient console code page**, so Japanese issue/PR titles and bodies stay
valid JSON on a Japanese Windows console (cp932/932). `worker next-action`,
`worker issue-preflight`, `worker pr-comment-preflight`, and the host/review
preflight paths all share this decoding. You do **not** need to run
`chcp 65001` or set `$OutputEncoding` / `[Console]::OutputEncoding` manually.
macOS/Linux behavior is unchanged (those consoles are already UTF-8).

---

## Packaged invocation (local smoke)

The CLI is packaged as a .NET tool (package id `JTechJapan.IntentSystem.Cli`,
command `intent-cli`). To smoke-test a locally built package:

```bash
export INTENT_CLI_LOCAL_VERSION="0.3.2-local.$(date -u +%Y%m%d%H%M%S)"
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj \
  -p:Version="$INTENT_CLI_LOCAL_VERSION" \
  -o .artifacts/packages
mkdir -p .artifacts/smoke-repo/.intent-cli
cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'
default_domain = "intent-cli"
artifact_root = ".intent-cli"
worktree_root = ".intent-cli/worktrees"
EOF
(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" JTechJapan.IntentSystem.Cli project status)
```

Equivalent `dnx` path:

```bash
(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" JTechJapan.IntentSystem.Cli project status)
```

---

## Preview install

> OSS preview channel. Public users should use the stable NuGet install
> (`dotnet tool install -g JTechJapan.IntentSystem.Cli`) or a release binary
> above. This section is for users who want the latest merged changes before a
> stable release.

The `preview-pack` GitHub Actions workflow runs on every merge to `main` and
uploads a self-contained install bundle as a workflow artifact named
`intent-cli-preview-<version>`. The bundle contains:

| File | Purpose |
| --- | --- |
| `JTechJapan.IntentSystem.Cli.<version>.nupkg` | The NuGet package consumed by `dotnet tool install`. |
| `JTechJapan.IntentSystem.Cli.<version>.nupkg.sha256` | SHA-256 checksum sidecar; verify before installing. |
| `preview-metadata.json` | Machine-readable build provenance (channel, version, build timestamp, commit, CI run identifiers). |
| `INSTALL.md` | Per-build install / update / verify / uninstall guide with this build's exact version and commit pre-filled. |

The package version pattern is `<nextVersion>-preview.<run_number>.<run_attempt>`
(e.g. `0.3.1-preview.42.1`).

```bash
# 1. Download and unzip the workflow artifact, then cd into it.
cd ./intent-cli-preview-0.3.1-preview.42.1

# 2. Verify the checksum (macOS: shasum; Linux: sha256sum).
shasum -a 256 -c JTechJapan.IntentSystem.Cli.*.nupkg.sha256

# 3. Install (or update) the .NET tool from this local folder:
dotnet tool install --global --add-source . \
  --version 0.3.1-preview.42.1 JTechJapan.IntentSystem.Cli
# Upgrade-in-place:
dotnet tool update --global --add-source . \
  --version 0.3.1-preview.42.1 JTechJapan.IntentSystem.Cli

# Uninstall:
dotnet tool uninstall --global JTechJapan.IntentSystem.Cli
```

The installed binary exposes the preview metadata via `intent-cli --version`:

```text
intent-cli 0.3.1-preview.42.1-<short-sha>-G<unit>
channel=preview built=<iso-utc> commit=<full-sha>
```

The `channel=preview` trailer confirms the embedded preview metadata loaded
successfully. **OSS preview packages carry no expiry; they remain runnable indefinitely.**

---

## Same-repo metadata topology (G485)

Same-repo topology keeps the **code branch** and the **metadata branch** in one
GitHub repository — e.g. code on `main`, metadata (`.intent-cli/` queue-state,
runs, packets, `intents/<domain>/`) on `main-metadata`. Configure it in
`.intent-cli/config.toml` under `[project]`:

```toml
[project]
domain = "estivo"
artifact_root = ".intent-cli"
same_repo_topology = true
metadata_source_branch = "main-metadata"   # branch the host loop READS metadata from
metadata_write_branch  = "main-metadata"   # branch the host loop WRITES metadata to
```

These exact keys are what `intent-cli automation same-repo-metadata-preflight`
and `intent-cli automation summary` read. If `same-repo-metadata-preflight`
reports `not-configured`, the keys above are not being resolved — check they are
under `[project]` (not a different table) and spelled exactly
`metadata_source_branch` / `metadata_write_branch`.

Host vs child bootstrap (G514): the host-side automation commands
(`automation summary`, `automation same-repo-metadata-preflight`,
`automation queue-seed-from-packet`) load `.intent-cli/config.toml` from the
resolved repo root, so they see the same effective `[project]` config — and the
same configured same-repo topology — as every other host command. A
child/standalone implementation repo that carries **no** `.intent-cli/config.toml`
keeps the safe default bootstrap behavior (no parent metadata required). If you
run a host command from a same-repo host repo and still see default behavior,
confirm the command is run from within the repo (the resolver walks up to the
`.intent-cli/` directory) and that the config file exists.

The supported publish path for a packet is **`automation queue-seed-from-packet`
→ `issue publish-flow` → `automation issue-publish`**, with no manual
queue-state edits or raw `gh issue create`. The domain's `execution_unit_regex`
(declared in `intents/<domain>/automation/bindings.md`, e.g. `^E\d{3,}$`) is
resolved from one shared source, so `automation summary --domain <d>` and
`queue-seed-from-packet --execution-unit <unit>` always agree on which units are
valid. A unit that does not match the active domain's regex is refused with a
precise diagnostic that names the consulted bindings source.

### Domain resolution order for execution-unit-resolving surfaces (G522)

Surfaces that resolve an execution unit from `--pr` or `--execution-unit`
(`review closeout-plan`, `automation queue-seed-from-packet`,
`automation publish-recovery`, and peers using the same lookup) apply this
resolution order when `--domain` is omitted:

1. an explicit `--domain` wins; it is an error if it contradicts the domain
   declared by the resolved packet's own `domain:` scalar;
2. otherwise the domain declared by the resolved packet.yaml / queue metadata
   is used;
3. otherwise the surface fails loud, naming candidate domains (scanned from
   `intents/*/`) and the exact `--domain` re-invocation — it never silently
   falls back to the host's default domain binding (`[project] domain` in
   `.intent-cli/config.toml`).

This closes a multi-domain-host gap: the default binding fallback could
previously report or validate against the WRONG domain for a packet whose
own `domain:` field says otherwise (e.g. `review closeout-plan --pr <n>`
reporting the host's default domain instead of the resolved packet's actual
domain, or `queue-seed-from-packet` running the wrong domain's
`execution_unit_regex` check). The default binding mechanism itself is
unchanged and still used elsewhere; only what these surfaces consult when
`--domain` is omitted has changed.

All three surfaces apply the full order strictly — none of them fall back to
`[project] domain` when a domain cannot be derived:

- `automation queue-seed-from-packet` — when neither `--domain` nor the
  packet's `domain:` field is available, the command refuses to seed.
- `review closeout-plan` — when a domain cannot be derived for the resolved
  queue item (no matched item, or its packet.yaml declares no `domain:`
  field), the command fails loud naming candidate domains and the exact
  `--domain` re-invocation, instead of reporting the host's default domain
  binding.
- `automation publish-recovery` resolves a domain for EVERY candidate
  execution unit before it may join repair analysis — from `--domain` when
  given (erroring per-candidate on contradiction with that candidate's own
  packet-declared domain) or otherwise from that candidate's own
  packet-declared domain. A candidate with neither becomes a structured
  `domain-underivable` unsafe stop rather than silently joining (or being
  silently dropped from) the scan; a candidate contradicting an explicit
  `--domain` becomes a structured `domain-contradiction` unsafe stop. This
  applies to both the `--pr`-scoped path and the broad (unscoped) scan.
  Omitting `--domain` entirely does not request cross-candidate scoping, so
  multiple candidates with different (but each individually derivable)
  domains may still coexist in one broad-scan result.
- `automation stalled-work` (G532) also applies this order — see below —
  but only once a candidate's execution unit is itself corroborated by real
  packet/queue linkage. `--domain` is a REQUIRED argument for
  `stalled-work`, so for a corroborated candidate it is always available to
  stand in for linkage that is silent on domain; but `--domain` scopes the
  scan, it does not by itself identify an otherwise-unidentified candidate
  as a member of it — an uncorroborated candidate is still excluded.

### Stalled-work detection (G523)

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] --format json|markdown`
is a **read-only** inventory of pending pipeline transitions with ages, so a
single orchestrator wake (or an external heartbeat) can detect and recover a
stalled pipeline without a human cross-checking GitHub labels, PR state, and
queue-state by hand. It never mutates GitHub labels, queue-state, or
`runs.jsonl`.

Categories:

- `published-not-delegated` — an OPEN issue carries `intent-target` but has
  no claim label (`intent-issue-in-progress` / `intent-pr-created`) yet, and
  no PR was ever created for it.
- `pr-created-not-reviewing` — the source issue carries `intent-pr-created`
  and its closing PR has not had the `review-start` transition applied (no
  `intent-pr-reviewing` / `intent-pr-approved` on the PR).
- `merged-not-closed-out` — a MERGED PR's linked queue-state item is not yet
  `Completed` (closeout — `pr-merged` + `closeout-recorded` runs events —
  has not been recorded).

Each item reports `kind`, `execution_unit`, `issue` and/or `pr` (number +
url), `age_minutes`, and `recommended_action` — the exact canonical command
to run next (`worker claim`, `automation pr-transition --transition
review-start`, or `closeout pr`, respectively). `--stale-minutes` filters
out items younger than the given threshold (default `0` — report everything
with its age; callers pick their own threshold). `age_minutes` is
approximated from the relevant GitHub entity's `createdAt`/`updatedAt`
timestamp, since GitHub does not expose per-label-application timestamps.
`published-not-delegated` also checks the already-fetched PR closing
references independently of issue labels, so a completion label that has
drifted out of sync with reality (an open PR already closes the issue, but
`intent-pr-created` was never applied or was removed) never produces a
false `worker claim` recommendation.

**Execution-unit and domain identification (G532)**

The candidate execution unit is the LEADING ID token of the issue/PR title —
`^[A-Z][A-Z0-9]*-G?[0-9]+` (an alphanumeric prefix, e.g. `SKS-G815` or
`Z4R-G3`) or a bare `^G[0-9]+` (e.g. `G523`), with a mandatory RIGHT boundary
(no letter/digit immediately after) — not everything before the first colon.
A title like `"SKS-G815 G812 sub-slice 1: ..."` resolves to `SKS-G815`,
never the whole pre-colon phrase; a title like `"G12abc: ..."` never
truncates to `G12`. This leading token is trusted only when a real
`.intent-cli/issues/<token>/packet.yaml` corroborates it. When it is absent,
or present but uncorroborated, the candidate is matched instead against
every packet under `.intent-cli/issues/*/packet.yaml` by that packet's own
declared `source_execution_unit` (nested
`implementation_issue_packet.source_execution_unit` first, bare
`source_execution_unit` as alias) appearing as a whole token anywhere in the
title. Exactly ONE matching packet FILE is required to corroborate — not
merely one distinct declared unit VALUE. Two or more matching packet files
are ambiguous (`execution-unit-ambiguous`, naming every candidate path)
even if their declared units happen to be identical strings (a duplicate
declaration across files is a data-integrity problem, never collapsed by
value); a single packet whose own nested field and top-level alias name the
same unit is still one file and is unaffected. Ambiguity is never resolved
by guessing (e.g. picking the longest match or the first-sorted directory).

This execution-unit string is used ONLY to locate the candidate's
`.intent-cli/issues/<unit>/packet.yaml` — never as the domain-membership
decision itself. Domain is read from that packet's nested
`implementation_issue_packet.domain` field first, falling back to a
top-level `domain:` field as a compatibility alias when the nested field is
absent.

For `merged-not-closed-out`, the execution unit and its corroborating
linkage come from queue-state instead of a title: a merged PR's own PR
number is matched against a queue item's `linked_pr`, but that bare number
match alone is NOT sufficient corroboration on a shared/multi-repo
queue-state (a coincidental same-number PR in an unrelated repo). The queue
item's own declared `linked_issue` (repo + number) must additionally match
one of the merged PR's OWN GitHub-reported closing-issue references for the
scanned repo — a missing, wrong-repo, or non-corresponding `linked_issue`
fails closed into `excluded[]` rather than being assumed. Every ACTIVE
(non-completed) queue item referencing the same merged PR is collected
first — exactly one is required; two or more (whether they collapse to the
same repo+issue with different execution units, or one validates while
another does not) is ambiguous (`execution-unit-ambiguous`, naming every
attempted queue item's unit, state, and linkage, plus the queue-state path)
regardless of JSON ordering. A completed duplicate alongside one genuinely
active item is NOT ambiguous — only active items compete for authority.

**Domain confirmation applies the same G522 order as every other
execution-unit-resolving surface** (`--domain` > packet-declared domain >
fail-loud) — but ONLY for a candidate whose execution unit is itself
corroborated by real packet/queue linkage as described above. For such a
candidate, since `--domain` is a REQUIRED argument for `stalled-work`, it is
always available to stand in for linkage that is silent on domain — the
candidate is excluded from `items[]` only on a genuine CONTRADICTION between
`--domain` and a packet that actively declares a different domain. This is
narrower than an earlier (PR #1148) tightening that fail-closed on ANY
missing/absent packet-declared domain, including cases where the candidate's
execution unit itself was never corroborated by anything — that broader
tightening excluded exactly the stalls this surface exists to find when the
identification logic itself was wrong (field findings against a downstream
adopter, 2026-07-15 and 2026-07-18), each papered over with a team
workaround instead of surfaced.

A candidate whose execution unit could NOT be corroborated at all — no
leading token's packet.yaml exists, and no packet's declared
`source_execution_unit` matches the title — is still excluded
(`domain-underivable`): an explicit `--domain` SCOPES the scan, it does not
by itself establish that an otherwise-unidentified candidate is a member of
it. `excluded[]` (`kind`, `execution_unit`, `issue`/`pr`, `reason`,
`detail`) reports every exclusion — `domain-contradiction` (naming the
specific conflicting packet-declared domain and the derivation attempted:
which of the nested field / top-level alias was checked, at which
packet.yaml path), `domain-underivable` (uncorroborated execution unit), or
`execution-unit-ambiguous` (naming every candidate packet path that matched)
— always with its reason and the derivation attempted, never silent.

This slice is detection only — consuming the surface from the orchestrator
wake procedure and from an external heartbeat are separate follow-up slices.

### Retiring a stuck published issue (G525)

`intent-cli automation issue-retire --repo <r> --issue <n> --reason <superseded|decomposed|obsolete> [--note <text>] [--domain <name>] [--write]`
is the canonical, atomic transition for a published `intent-target` issue
that can never be started as authored (e.g. a research pass proves the
slice must be decomposed). Before this command existed, the only escape
from this deadlock was an operator-authorized noncanonical recovery (manual
GitHub close, manual label strip, hand-authored queue-state edit) that
`metadata validate` then failed to recognize.

`--write` performs, in order:

1. closes the GitHub issue as **not planned**, with a comment naming the
   reason and the optional note (skipped if the issue is already closed —
   see partial-failure recovery below);
2. removes `intent-target` and any other workflow labels present on the
   issue;
3. marks the corresponding queue-state item's lifecycle `retired` (with the
   reason) — **creating the entry if none existed yet**, since a
   published-but-never-delegated issue commonly has no queue-state entry at
   all; this is what lets `metadata validate` recognize the retired
   lifecycle instead of reporting a missing queue entry;
4. appends a `packet-retired` event to `runs.jsonl`.

Without `--write` it is a dry-run that lists the exact planned mutations.
It **fails closed** (no mutation at all) when:

- an OPEN PR in the same repo closes the issue — merge, close, or release
  that PR first;
- the issue carries `intent-issue-in-progress` — an active claim is in
  flight; release it first (e.g. `intent-cli worker complete --kind issue
  --number <n> --outcome declined-contract-incomplete --write`);
- the matched queue item is already `Completed` (merged/finished work) —
  retirement only applies to work that was published but can never be
  completed as authored; this refusal touches neither GitHub nor local
  state;
- the resolved domain is underivable or contradicts an explicit `--domain`
  (see domain resolution below).

**Partial-failure recovery**: the target issue is resolved via a direct
per-issue GitHub lookup — regardless of open/closed state — instead of
scanning the OPEN-issues list. If a `--write` dies mid-sequence (issue
closed but label removal, the queue-state write, or the `runs.jsonl` append
did not complete), simply re-running the same command finds the issue again
and finishes the remaining steps instead of dead-ending on "not found among
OPEN issues." Recovery for an already-CLOSED issue is only authorized when
GitHub's own close reason is **not planned** (the exact reason this command
uses) — a closed issue with any other reason (e.g. completed via merge) is
left untouched.

**Domain resolution (G522 boundary)**: queue-item matching requires an
exact `(repo, issue number)` pair — a same-numbered issue in a different
repo can never match this execution unit. The execution unit's domain is
resolved in the same order as other execution-unit-resolving surfaces: an
explicit `--domain` wins (it is an error if it contradicts the domain
declared by the resolved packet.yaml); otherwise the packet-declared
`domain:` field is used; otherwise the command fails loud, naming candidate
domains and the exact `--domain` re-invocation. This applies to both an
existing queue item and a brand-new one derived from the issue title — a
misleading title prefix alone can never authorize queue creation without a
packet.yaml (or an explicit operator-supplied `--domain`) confirming it.

**Idempotent**: re-running on an execution unit whose queue-state entry is
already `retired` is a safe no-op — durable state (not a fragile GitHub
state re-check) is the source of truth for idempotency. If `--write` is
used and the queue-state was retired but the `runs.jsonl` event from a
prior partial write is missing, the retry finishes exactly that missing
step (with zero GitHub calls) instead of silently dropping it forever.
Packet directories and the issue comment trail are never touched or
deleted.

Retired items clear WIP gating automatically: `automation
host-review-preflight`'s in-flight scan reads OPEN, `intent-target`-labeled
GitHub issues/PRs live, so a closed, unlabeled issue simply disappears from
it — no separate code path needed.

---

### Facet-aware context supply (G530)

Building on G529's four semantic facets (`vocabulary`, `invariant`,
`decider`, `acceptance-property`), two read-only surfaces now supply
facet-classified nodes as the preferred, localized semantic context a
change must respect — instead of an implement/review agent reconstructing
that surface by hand.

**`intent-cli context collect`** gains a `## Facet context` section,
rendered AHEAD of the unclassified queue-state/clarification/automation-
bindings/recent-events context below it (it is the semantic core, not an
afterthought). The section holds one group per facet, always in the
canonical order `vocabulary → invariant → decider → acceptance-property`,
each node reported as `id`, `facets` (all of that node's facet values, not
just the current group's), `summary` (first non-blank line after
frontmatter), and `path` (`intents/<domain>/...`):

```bash
intent-cli context collect --domain <d> --format json
intent-cli context collect --domain <d> --facets invariant,decider   # restrict to these facets only
intent-cli context collect --domain <d> --scope intents/<d>/means,identity/mission.md  # narrow by overlap
```

- `--facets <comma-separated>` restricts which facet groups appear at all
  (still rendered in canonical order); an unrecognized facet name is a
  usage error (mirrors `intent search --facet`'s validation).
- `--scope <comma-separated paths>` narrows every group to nodes whose path
  overlaps a hint — an exact match, a hint naming an ancestor directory, or
  the shorter domain-relative id form (no `intents/<domain>/` prefix, no
  `.md`) all count as overlap. Omitting `--scope` returns every domain
  facet node.
- A domain with ZERO facet-annotated nodes at all (not merely a
  `--scope`/`--facets` query that matched nothing) sets `facet_context_note`
  and renders an explicit note instead of an empty section — graceful
  degradation, never an error. Facets are optional; this is the norm before
  a tree adopts them.
- JSON shape: `facet_context: [{facet, nodes: [{id, facets, summary,
  path}]}]` (always 4 entries, or fewer when `--facets` is passed),
  `facet_context_note: string | null`.

**`intent-cli packet draft`** generates a `## Facet context` section inside
the scaffolded `review-context.md`, listing the facet nodes overlapping the
packet's own `implementation_issue_packet.intent_references` — the exact
same overlap logic `context collect`'s `--scope` uses, so the two surfaces
can never disagree about what "overlaps". Because `packet draft` never
overwrites an existing file, this only applies the first time
`review-context.md` is written: if `packet.yaml` already exists (e.g. an
operator hand-edited its `intent_references` after an earlier `packet
draft` run, before `review-context.md` was ever generated), that packet.yaml
ON DISK — never the freshly-templated empty `intent_references: []` this
same invocation might otherwise write — is what gets read. Once
`review-context.md` exists, re-running `packet draft` never touches it,
preserving hand-edits exactly as it already does for the other three
scaffold files.

Both surfaces share one selector (`FacetContextSelector`) for scanning,
classifying, grouping, and scope-overlap matching, so ordering, filtering,
and degradation semantics can't drift apart between them. Only VALID facet
values (the closed G529 set) are ever bucketed; a malformed `facets:`
declaration is excluded from every group exactly like an absent one is —
validating and reporting malformed/unknown values is `lint-layout`'s job,
not these consumption surfaces'.

---

## Version flow

The repository version policy lives in `eng/version.json` — the single source of
truth for `stableVersion` (the latest published stable line) and `nextVersion`
(the release being prepared / in-development line). Since G468 the local
`dotnet pack` default `<Version>` is derived from this file, so a local pack and
install report the in-development `nextVersion` rather than a stale csproj
literal:

```json
{
  "stableVersion": "0.3.15",
  "nextVersion": "0.4.0"
}
```

| Stage | Version form | How it is derived |
| --- | --- | --- |
| Local pack / install | `0.4.0-<sha>-<G-unit>` | `nextVersion` from `eng/version.json` (G468) |
| Main CI preview | `0.4.0-preview.<run>.<attempt>` | `nextVersion` from `eng/version.json` |
| Release candidate (optional) | `0.4.0-rc.N` | Publishing the GitHub Release for tag `v0.4.0-rc.N` triggers `release.yml` (`on: release: published`); the tag supplies the version |
| Stable release | `0.4.0` | Publishing the GitHub Release for tag `v0.4.0` triggers `release.yml` (`on: release: published`); the tag supplies the version (`-p:Version=<tag>` wins) |
| Post-release main builds | `0.4.1-preview.<run>.<attempt>` | After bumping `nextVersion` to `0.4.1` |

**After releasing `v0.4.0`**, bump both fields in `eng/version.json`:

```json
{
  "stableVersion": "0.4.0",
  "nextVersion": "0.4.1"
}
```

This ensures the next main-branch CI build (and local pack) immediately produces
`0.4.1-preview.<run>.<attempt>` / `0.4.1-<sha>-<G-unit>` rather than continuing to
emit `0.4.0` (which would collide with the stable release version).

### Next release readiness (v0.4.0)

**`v0.3.15` shipped** (GitHub Release + NuGet) and the version policy was bumped
to the `0.4.0` development line — a **minor** bump, not a patch: three new
automation commands plus a visible fail-loud behavior change justify more than
a patch release. The repository is now on the in-development **`0.4.0`**
`nextVersion`; G528 is **prepare-only** — it bumps the version metadata and
docs and adds no publish steps. The version-bump merge does **not** create a
GitHub Release or tag. After it merges and the
[release-readiness gate](release-notes-v0.4.0.md#release-readiness-gate-g528)
holds, a **maintainer/operator (or external release automation) creates and
publishes the GitHub Release** for `v0.4.0`; publishing that Release fires
`.github/workflows/release.yml` (`on: release: published`), which builds and
publishes the NuGet package and the per-platform binary artifacts. Full
changelog and operator checklist:
[release-notes-v0.4.0.md](release-notes-v0.4.0.md).

**To ship in `v0.4.0` (changes since `v0.3.15`) — orchestrator stall-prevention
batch, fail-loud domain resolution, and a parser fix:**

- **Three new automation commands** — `automation stalled-work` (G523),
  `automation heartbeat` (G526), and `automation issue-retire` (G525): a
  read-only pending-transition inventory, a read-only wrapper of it that
  emits a ready-to-send reconcile message for an external low-frequency
  scheduler, and a canonical atomic transition to retire a published issue
  that can never be started as authored.
- **Fail-loud domain resolution** (G522) — execution-unit-resolving surfaces
  (`automation queue-seed-from-packet`, `review closeout-plan`,
  `automation publish-recovery`) now resolve domain as: explicit `--domain`
  wins (erroring on contradiction with the packet's own `domain:` field) >
  the packet-declared domain > fail loud naming candidate domains and the
  exact re-invocation — never a silent fallback to the host's config-default
  domain. **Migration:** any script or automation that relied on the previous
  silent fallback must now either pass `--domain` explicitly or ensure the
  resolved packet.yaml declares its `domain:` field.
- **Orchestrator wake contract** (G524) — publish and delegate in the SAME
  wake (no more "deferred to the next wake"); the message cap is reframed as
  "at most one delegation per receiver per wake"; a new end-of-wake
  `automation stalled-work` check with a never-defer rule; the receiver
  completion-or-blocked report is now a REQUIRED FINAL STEP of every
  delegation; and dispatch roster verification (`team.sh`) before every send.
- **Managed review worktrees + design-alignment checks** (G520) — review
  worktrees are enforced under the managed root
  (`.intent-cli/worktrees/review-<unit>`), never `/tmp`; a review `completed`
  reply missing design-alignment evidence (packet, review-context, intent
  tree, ADR/decision notes) is now incomplete.
- **Codex monitor (beta) guidance** (G521) — a setup preflight and three new
  troubleshooting entries (silent launcher, static TUI, doubled responses)
  for the agmsg Codex bridge.
- **Packet-yaml parser fix** (G527) — `PreparedPacketYamlScalarParser`'s
  quote-balance check is now delimiter-aware, fixing the exact field
  incident where a double-quoted value merely containing an apostrophe was
  wrongly rejected.
- Orchestrator mode remains **preview/experimental**: opt-in, still being
  hardened, with the timer-loop mode fully supported and unchanged. See
  [Agent-message orchestration](12-agent-message-orchestration.md).

**Release-readiness verification (run before merging the `v0.4.0` version
bump):**

```bash
# 1. Confirm the version policy records the release-to-be-cut.
cat eng/version.json   # stableVersion 0.3.15 (published), nextVersion 0.4.0 (to release)

# 2. Build and confirm the display version identity (version + git SHA + G-unit).
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   expected shape: intent-cli 0.4.0-<sha>-G52x   (NOT a stale literal)

# 3. Pack and confirm the NuGet package version matches the policy.
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.4.0.nupkg

# 4. Confirm package metadata (id / command / license / project URL).
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

After the version-bump merge lands on `main`, a maintainer/operator (or external
release automation) creates and publishes the GitHub Release for `v0.4.0`;
publishing it triggers `release.yml` (`on: release: published`) to build and
publish the NuGet package and the per-platform binary artifacts. Once it has
published, apply the post-release `eng/version.json` bump above
(`stableVersion → 0.4.0`, `nextVersion → 0.4.1`).

### Re-creating a deleted release tag (`v0.3.3`)

`v0.3.3` was tagged too early and the tag was deleted. **Only re-create the
`v0.3.3` tag/release after both release-blocking packets are merged to `main`
and the release CI test job is green:**

- **G441** — first-run host initialization deadlock fix.
- **G443** — release CI stabilization (the installed-CLI surface probe is
  hardened against the `Text file busy` / ETXTBSY exec race on Linux runners,
  and each test project writes a uniquely named `*.trx` so release CI results
  are diagnosable).

Re-tagging before a green CI run on a commit that contains both fixes will
reproduce the original failing release job.
