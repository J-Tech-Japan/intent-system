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

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] [--claimed-silent-minutes <m>] --format json|markdown`
is a **read-only** inventory of pending pipeline transitions with ages, so a
single orchestrator wake (or an external heartbeat) can detect and recover a
stalled pipeline without a human cross-checking GitHub labels, PR state, and
queue-state by hand. It never mutates GitHub labels, queue-state, or
`runs.jsonl` — and an informational kind never sends the status-check it
recommends; that remains a human/orchestrator action.

Every item carries `is_informational` (`bool`) distinguishing two families:

**Actionable categories** (`is_informational: false` — `recommended_action`
is always a runnable `intent-cli` command):

- `published-not-delegated` — an OPEN issue carries `intent-target` but has
  no claim label (`intent-issue-in-progress` / `intent-pr-created`) yet, and
  no PR was ever created for it.
- `pr-created-not-reviewing` — the source issue carries `intent-pr-created`,
  its closing PR has not had the `review-start` transition applied (no
  `intent-pr-reviewing` / `intent-pr-approved` on the PR), AND the PR is not
  already in the repair or rereview lifecycle (see below — a PR in either of
  those states is reported under its own informational kind instead).
- `merged-not-closed-out` — a MERGED PR's linked queue-state item is not yet
  `Completed` (closeout — `pr-merged` + `closeout-recorded` runs events —
  has not been recorded).

**Informational categories (G533)** — `is_informational: true`,
`recommended_action` is descriptive prose (never a transition command), age
is reported for visibility only:

- `repair-pending` — a PR carrying `intent-pr-request-update` and/or
  `intent-pr-update-in-progress`. Field finding: an OPEN PR in exactly this
  state (PR #1750) was previously misreported as `pr-created-not-reviewing`
  with a `review-start` recommendation — semantically wrong mid-repair; a
  detector whose recommendation must be second-guessed loses its value.
  `age_minutes` is measured from the PR's own `updatedAt` rather than PR
  creation — a CONSERVATIVE approximation of "since entering the repair
  state", not the exact label-application moment: GitHub does not expose
  per-label-application timestamps, and `updatedAt` reflects the PR's most
  recent modification of any kind (which may postdate the specific label
  change) unless a dedicated label-event fetch is added.
- `rereview-pending` — a PR carrying `intent-pr-rereview-ready` (repair
  pushed, awaiting re-review). Same `updatedAt`-based age approximation as
  `repair-pending`.
- `claimed-but-silent` — an issue carrying `intent-issue-in-progress` with
  **no PR created yet** and no observable activity for longer than
  `--claimed-silent-minutes` (default **720** minutes / 12 hours — chosen so
  an ordinary work session never trips it). "Observable activity" is
  approximated as the more recent of the issue's own `updatedAt` (GitHub
  bumps this on any label change, comment, or other timeline event — the
  closest available proxy without a dedicated per-issue timeline-events
  fetch) and the `updatedAt` of any open PR whose closing references name
  the issue (a linked PR's own activity counts too, even before
  `intent-pr-created` is applied). A missing or malformed `updatedAt` — on
  the issue OR a linked PR — is NEVER treated as "old activity" by falling
  back to `createdAt` (which measures issue/PR OPEN time, not claim
  acquisition or last touch, and could manufacture a misleadingly old
  silence interval); it fails closed into `excluded[]`
  (`activity-data-unusable`, naming exactly which timestamp was unusable)
  instead. A parsed timestamp somehow in the future (clock skew) is
  clamped to "now" rather than trusted, which can only ever make a
  candidate look less silent. `recommended_action` always reads as a
  status-check request to the assigned worker — it never assumes
  completion, failure, or any transition from silence alone. Once a PR
  exists for the issue (`intent-pr-created`), the PR-lifecycle kinds take
  over instead; detecting a repair-state PR that is itself stale beyond a
  threshold is an explicit out-of-scope follow-up.

Each item reports `kind`, `execution_unit`, `issue` and/or `pr` (number +
url), `age_minutes`, `is_informational`, and `recommended_action`.
`--stale-minutes` filters out items younger than the given threshold
(default `0` — report everything with its age; callers pick their own
threshold) — this applies uniformly across all six kinds; `claimed-but-silent`
additionally gates on its OWN `--claimed-silent-minutes` threshold before an
item is even considered (so raising `--stale-minutes` alone can never make a
`claimed-but-silent` item appear earlier than its own threshold allows).
`age_minutes` is approximated from the relevant GitHub entity's
`createdAt`/`updatedAt` timestamp, since GitHub does not expose
per-label-application timestamps or per-issue timeline events without a
dedicated per-issue fetch this slice does not add. `published-not-delegated`
also checks the already-fetched PR closing references independently of issue
labels, so a completion label that has drifted out of sync with reality (an
open PR already closes the issue, but `intent-pr-created` was never applied
or was removed) never produces a false `worker claim` recommendation.

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

`automation heartbeat` (G526) wraps this same analyzer and reflects
`is_informational` into its `message_body`: the summary line splits into
"`N` pending transition(s)" and, when any informational item is present,
"`M` informational note(s)"; each per-item line ends with `— recommended:
` command `` for an actionable kind or `— FYI: ` prose `` for an
informational one, so a reader (human or orchestrator) can never mistake
"no transition needed" for an actionable next command.

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

### Queue robustness: list parsing, retired backfill, lifecycle-aware selection (G534)

Three related field findings against real, hand-authored packets and queue
state are fixed together here.

**`queue enqueue` accepts both YAML list-item conventions.** The packet
readers (`ProjectionPacketSerializer` for the current
`implementation_issue_packet` / `review_context_packet` schema, and
`ProjectionPacketRuntimeReader`'s legacy `execution_unit` /
`implementation_issue` fallback) previously recognized a block-sequence list
item only when indented with exactly 4 spaces plus `"- "` — the renderer's
own self-generated convention. A hand-authored (or foreign-tool-authored)
packet using the more common 2-space convention, where each list item sits
at the same column as its parent key, was rejected outright with `field
line is missing ':''` on every item, quoted or unquoted. Both readers now
detect a list item by content (a line that, after stripping leading
whitespace, starts with `"- "` or is exactly `"-"`) rather than by counting
columns, so either convention parses — and the two conventions may even be
mixed across different fields within the same file.

**`queue transition --to retired` backfills a queue-state entry — as a
guarded, idempotent, terminal transition.** A packet retired via
`intent-cli packet retire` (which writes only `lifecycle.yaml`, never
touches `queue-state.json`) or via `automation issue-retire` predating
queue tracking sometimes needs its queue-state item marked `retired`
directly, without hand-editing the JSON file. `retired` is now accepted as
a transition target — `queue transition <execution-unit> retired` — but,
unlike every other non-blocking target, it does **not** route through the
generic, source-state-agnostic transition path; it has its own guarded
entry point (`QueueManager.Retire`) consistent with `automation
issue-retire`'s own refusal (G525):

- legal from any state except `Completed` — a completed item refuses
  retirement with zero mutation and zero run event, since retirement only
  ever applies to work that can never be completed as authored;
- **the linked PR is the authoritative evidence, not queue-state itself** —
  queue-state can be stale, so before mutating anything the CLI boundary
  resolves the item's `linked_pr` (if any) via `gh pr view` and refuses
  retirement outright when that PR is confirmed **merged or closed**, even
  for a `Queued`/`Review`/`Fixing` item that queue-state still calls
  non-`Completed`. When the linked PR's state cannot be resolved (lookup
  failure, unparseable/wrong-repo URL, an ambiguous response) retirement
  also refuses — fail closed, never presumed open. An item with no linked
  PR at all skips this check entirely (nothing to verify). This lookup is
  the one place in the retirement path permitted to reach GitHub;
  `QueueManager.Retire` itself stays network-free and simply receives the
  already-verified evidence;
- idempotent when the item is already `Retired` — a no-op that changes
  nothing and never appends a duplicate `retired` run event, however many
  times it is re-run;
- terminal once applied — a retired item can never be transitioned to any
  other state (`queued`, `active`, `completed`, `blocked`, …) through
  `queue transition`; the generic non-blocking/blocking transition paths
  now refuse outright when the current state is `Retired`, closing what
  was previously a silent reactivation path.

The item must already exist in queue-state (create it first via `queue
enqueue` if it doesn't). Naming an unsupported target still refuses and
lists the full allowed set, which now includes `retired`.

**The publish selector combines queue-state and packet-lifecycle evidence
explicitly, and fails closed on ambiguous lifecycle metadata.** `intent
next-slice` reads each candidate's `lifecycle.yaml` (if any) into one of
four explicit states — absent (no sidecar; not an error), valid-active
(`lifecycle: ready`), valid-retired (`absorbed`/`retired`/`superseded`), or
invalid (unreadable, missing the `lifecycle` key, blank, or an
unrecognized value) — and combines that with the queue-state `Retired`
signal:

- either signal alone recording retirement excludes the unit — this holds
  even when there is no queue-state entry whatsoever (a lifecycle-only
  retirement) or no `lifecycle.yaml` at all (a queue-only retirement, e.g.
  via `automation issue-retire` or `queue transition --to retired`);
- an explicit `lifecycle: ready` does **not** override a queue-state
  `Retired` record, and a non-publishable `lifecycle.yaml` does **not** get
  overridden by a *present* queue-state entry that is not `Retired`
  (`queued`/`active`/`review`/`fixing`/…) — both directions are
  contradictions, still excluded, and both are now surfaced as an
  actionable diagnostic (`lifecycle-metadata-diagnostic` warning, with a
  note naming the unit, the sidecar path, and both states) instead of
  either direction resolving silently — a later, unrelated candidate can
  never hide an earlier unit's inconsistent evidence;
- agreement (both signals retired) and a lifecycle-only retirement with
  **no queue entry at all** are not contradictions and stay silent —
  exactly as before;
- invalid lifecycle metadata (unreadable, blank, missing key, or an
  unrecognized value) excludes the unit and raises the same diagnostic
  regardless of queue state — ambiguous retirement evidence must never
  resolve to "publishable," so a malformed sidecar can never let its
  packet quietly surface as the next candidate, even when it is the only
  packet directory available.

Together, these three fixes let a repo recover from a stuck or
pre-queue-tracking retirement entirely through `queue enqueue` / `queue
transition` / `intent next-slice`, with zero manual `queue-state.json`
edits.

---

### `request-update` supersedes a stale `intent-pr-rereview-ready` (G535)

Field finding #5 (SKS-G824 / PR #1760): `intent-cli automation pr-transition
--transition request-update` added its repair labels (`intent-pr-request-update`,
and cleared `intent-pr-reviewing`) but left a pre-existing
`intent-pr-rereview-ready` in place. `worker claim` correctly refuses any PR
still carrying `intent-pr-rereview-ready` (a rereview-ready PR is the
reviewer's to pick up, not the worker's) — so a design amendment arriving
while a PR was rereview-ready produced a PR that `request-update` marked for
repair but `claim` refused to touch: a deadlock between two canonical rules,
with no installed command able to proceed. The only escape was a non-obvious
`review-start` → `request-update` detour.

`request-update` now clears `intent-pr-rereview-ready` (and its legacy
`rereview-ready` string form) in the **same** write that adds
`intent-pr-request-update` and removes `intent-pr-reviewing` — a repair
request always supersedes pending rereview-readiness.

**Truthful audit output in both modes.** Unlike `review-start`/`approved`/
`review-release` (whose `--dry-run` intentionally reports the *full*
planned removal set regardless of presence), `request-update`'s reported
`remove_labels` — in **both** `--dry-run` and `--write` — is always derived
from the already-fetched current labels: a PR carrying only
`intent-pr-rereview-ready` is reported as superseding exactly that label,
never an absent `intent-pr-reviewing` or absent legacy `rereview-ready`
alongside it. A rerun (or a PR that was never rereview-ready) reports and
applies an empty removal set — genuinely idempotent, not merely
non-erroring.

**One atomic GitHub request, not sequential add/remove.** `gh <kind> edit
--add-label --remove-label` is a CLI convenience wrapper — its atomicity
from GitHub's perspective is not guaranteed. `request-update`'s `--write`
path instead computes the full desired label set (every current label
minus the ones being superseded, plus `intent-pr-request-update`) and
replaces it in **one** GitHub REST call — `PUT
/repos/{repo}/issues/{number}/labels` — via the internal
`IGitHubLabelSetReplacer.ReplaceLabelSet` seam. If the desired set already
equals the current set (order-insensitive), this is a genuine no-op — zero
GitHub calls, not merely an empty removal list. This atomic-replace path is
used only by `request-update`; every other transition keeps using the
pre-existing `ApplyLabelTransitions` add/remove path, unchanged.

**Phase-aware failure reporting — stated honestly, never overclaiming
safety.** A single HTTP call means there is no window where *this call's
own actions* land half-applied — but that is not the same as every failure
being known-harmless, and the command's error reporting reflects that
distinction precisely:

- if the `gh` process for the PUT never starts (e.g. the executable can't
  be launched), nothing was transmitted — reported as a plain failure,
  `applied: false`, `may_have_applied: false`;
- once that process has started, ANY failure (non-zero exit, a
  write/read error, a timeout) is ambiguous — `gh` may already have
  transmitted the request and GitHub may already have applied it before
  the failure surfaced. Reported as `applied: false`, **`may_have_applied:
  true`**, with the `intended_labels` the mutation was attempting to
  establish and an exact `recovery_command` (`gh <kind> view <n> --repo
  <repo> --json labels`) to resolve the ambiguity — never a false "nothing
  changed" claim;
- if the PUT itself reports success but the post-write verification read
  fails, or succeeds but reads back a mismatched set, both are *also*
  reported as `may_have_applied: true` with the same recovery info — never
  as a rollback or "no mutation" signal, since the PUT very likely applied
  in both cases.

**Bounded concurrency model — the honest limit of the post-write
verification.** GitHub's "Set labels" endpoint has no conditional/If-Match
support for optimistic concurrency, so a label change racing between the
caller's initial read (used to compute the desired set) and the PUT cannot
be prevented outright. The post-write verification read only detects a
mismatch that is *still present at the moment of that read*. A label added
by another process **after** the initial read but **before** the PUT — and
therefore never reflected in the desired set — can be silently overwritten
by the PUT; if nothing else changes labels between the PUT and the
verification read, that read will equal the intended set exactly and the
command will report success even though a concurrent addition was just
lost. This race is fundamentally undetectable by a read-after-write check
alone, and the docs and code say so explicitly rather than implying full
protection.

With this landed, the SKS-G824 recovery sequence (`review-start` then
`request-update` to clear a stuck rereview-ready) is no longer necessary —
`request-update` alone now leaves the PR in a state `worker claim` accepts.
`worker claim` itself is unchanged: it still refuses a rereview-ready PR
that has not been through `request-update`.

---

### `issue publish-flow` idempotent rerun independently verifies and restores all three durable artifacts (G536)

Field incidents (2026-07-19, publishing G530 as issue #1164 and G531 as
issue #1166): host `main` advanced concurrently after each GitHub issue was
created, forcing a stash + fast-forward sync mid-publish. For #1164,
`publish.yaml` survived with its `issue-created` record intact, but
`queue-state.json`'s `linked_issue` and the `runs.jsonl` `issue-created`
event both reverted to their pre-publish (absent) state. For #1166, the
loss was worse: `queue-state.json`'s `linked_issue` AND `publish.yaml`'s
`issue-created` record were both lost — `runs.jsonl`'s `issue-created`
event was the *only* surviving signal. The pre-G536 idempotent rerun only
ever consulted `publish.yaml` or `queue-state.json` and never read
`runs.jsonl` as an identity source at all, so the #1166 shape fell through
to the normal create path — risking a **second GitHub issue** for the same
execution unit, the single most severe defect this repair fixes.

**A single shared analyzer, `PublishDurableArtifactAnalyzer`, now backs
both `issue publish-flow`'s idempotent rerun and `automation
publish-recovery`.** It independently parses all three durable artifacts —
`queue-state.json`'s `linked_issue`, `publish.yaml`'s `issue-created`
record, and every canonical `issue-created` event in `runs.jsonl` — and
resolves a single canonical issue identity, or fails closed. Both commands
report the identical, stable gap identifiers
(`queue_linked_issue_missing`, `publish_yaml_missing`,
`runs_event_missing`) for the same durable-state shape, so the two
surfaces can never disagree about what's missing.

**`runs.jsonl` is classified, not just checked for presence.** An
execution unit's `issue-created` events are read as a set and classified:

- **Zero** matching events → the `runs_event_missing` gap.
- **Exactly one**, or **duplicate-identical** (multiple events all naming
  the same issue number — e.g. a retried append) → present, no gap.
- **Conflicting** (events naming *different* issue numbers for the same
  execution unit) → fails closed as `runs_event_conflicting`, a data
  contradiction, never silently resolved either way.

**Malformed data fails closed, distinct from "missing."** An unparseable
`publish.yaml` (`publish_yaml_malformed`) or an `issue-created` run event
that carries neither a recognizable `linked_issue` (`repo#number`) nor a
`reason` issue URL (`runs_malformed`) is never silently treated as absent
— treating malformed data as "missing" would invite an unsafe overwrite of
a file that might carry a real, just-corrupted record. The whole analysis
short-circuits to a fail-closed result before any gap/restoration logic
runs.

**Genuine cross-artifact contradictions fail loud instead of picking a
side.** If the artifacts disagree on the issue number for the same
execution unit, that is a data contradiction, not a missing artifact — the
command refuses (exit 1, `cross_artifact_contradiction`), names every
conflicting value, and never silently trusts one side over the other.

**Restoration only touches genuinely-missing artifacts, and is verified by
re-reading, not by trusting write helpers' return values.** The rerun
iterates the analyzer's gap list and restores only those artifacts using
the same write helpers the first-run success path uses
(`TryPatchQueueStateLinkedIssue`, `WritePublishArtifact`,
`AppendIssueCreatedRunEvent`). After attempting restoration, it
re-invokes `PublishDurableArtifactAnalyzer.Analyze` a **second time**
against the freshly-written files and reports `durable_state_synced:
true` only when that independent re-read confirms zero remaining gaps —
a write helper reporting success is never enough on its own. `gh` is
never re-invoked during any idempotent rerun.

**Restoration failure also fails loud, naming exactly what's missing and
how to recover.** If an artifact cannot be restored (e.g. `queue-state.json`
no longer has any item for this execution unit to patch), the command
exits non-zero, reports `durable_state_synced: false`, and its `error`
names precisely which artifact(s) remain missing/inconsistent (from the
post-restoration re-analysis) plus the exact recovery commands (`issue
publish-flow ... --write` to retry, or `automation publish-recovery
... --write` to reconcile queue-state linkage). Artifact verification is
independent, not all-or-nothing: an artifact that *could* be restored
still gets restored even when another one couldn't.

**Dry-run plans only — it never writes, and reports `would_restore`.**
`issue publish-flow <unit> --repo <owner/repo>` (no `--write`) runs the
same read-only analysis and, when an existing issue identity is found,
reports `would_restore` — the exact gap list a subsequent `--write` rerun
would restore — without ever invoking a write helper or `gh`.

**Before ever creating on a "zero local signal" unit, a GitHub-side
existence check runs first.** When the analyzer finds no identity across
all three local artifacts, falling straight through to `gh issue create`
would risk a genuine duplicate if every local artifact was reset/lost but
the GitHub issue itself was never re-created. A `gh issue list --search`
corroboration check runs before create; if it finds a title match, the
command refuses (exit 1) and points the operator at `automation
publish-recovery --write` or a manual backfill, rather than creating a
duplicate or attempting to reconstruct identity from a fuzzy match.

**`automation publish-recovery` reports the identical gap.** Every unsafe
stop now carries a `durable_artifact_gaps` field — the output of the same
shared analyzer, called on the same paths for the same execution unit —
so an operator (or a test) can directly compare a `publish-recovery`
unsafe stop against what `issue publish-flow`'s own rerun independently
detects and restores for the identical durable state.

**Round-4 review repair — the canonical identity is a full tuple, not just
a number.** A subsequent review round found that treating "same issue
number" as sufficient still let contradictory or self-inconsistent data
through: two artifacts naming the same issue *number* but a different
*repo*, or a single artifact whose own repo/number/URL fields disagreed
with each other, were silently accepted. The analyzer now requires every
present signal to be:

- **Internally self-consistent** — `queue-state.json`'s `linked_issue`
  (which carries `repo`, `number`, and `url` as three separate fields) and
  each `runs.jsonl` event (which may carry both a `linked_issue`
  `repo#number` descriptor and a `reason` issue URL) must agree with
  themselves before they're even accepted as a signal at all.
- **A canonical GitHub issue URL** — `https://github.com/<owner>/<repo>/issues/<number>`
  exactly; any `/issues/`-containing string that doesn't match this shape
  is no longer accepted as if it were canonical.
- **Checked against the confirmed target repo directly**, not merely
  pairwise between artifacts — a signal whose repo doesn't match the
  `--repo` this command run is scoped to is a contradiction even when it's
  the ONLY present signal.

A malformed/unreadable `queue-state.json` now also fails closed exactly
like `publish.yaml`/`runs.jsonl` already did (`queue_state_malformed`),
rather than being silently treated as absent — a surviving `publish.yaml`
or `runs.jsonl` signal must never authorize restoration around a
`queue-state.json` this analyzer couldn't actually read. `publish.yaml`'s
own `execution_unit` field is validated against the unit the packet path
is scoped to (`publish_yaml_malformed` on mismatch — data copied from
another unit's packet is corruption, not a signal for this unit).

**The GitHub existence check is now a classification (zero / exactly-one /
multiple), not a bare boolean, and requires exact title AND body
linkage.** The prior round's `gh issue list --search ... --limit 20` with
prefix-boundary title matching could both miss a real duplicate beyond the
first 20 results and match a merely similarly-titled unrelated issue. The
rewritten check:

- Retrieves candidates with `--limit 1000` (vs. the prior round's 20) so a
  legitimate duplicate is never silently dropped by client-side
  truncation for any realistic repo's issue history — GitHub's own
  `in:title` search is still used as a fast pre-filter, not the identity
  decision itself.
- Requires the candidate's title to equal the resolved expected title
  **exactly** (no prefix/boundary heuristic — a similarly-titled unrelated
  issue can never match).
- Additionally requires the candidate's body to match the local packet's
  `github-body.md` content byte-for-byte (normalized for line endings) —
  the same content that was (or would be) posted via `gh issue create
  --body-file`, giving a genuine content-linkage check rather than a title
  guess alone.
- Classifies the result: **zero** matches → safe to create; **exactly
  one** → that confirmed GitHub identity is fed directly into the same
  `PublishDurableArtifactAnalyzer`-backed restoration path used for a
  local-signal rerun, restoring all three local artifacts **without ever
  calling `gh issue create`**; **multiple** matches → fails closed,
  non-mutating, exit 1 — ambiguity is never resolved automatically.

**Round-5 review repair — GitHub enumeration is real cursor pagination, not
a raised limit; body normalization is stricter; queue-state cardinality is
checked.** A further review round found that a fixed `--limit` (however
high) is still a cap that can, in principle, silently drop a real
duplicate once the filtered result exceeds it — and that a blanket
`Trim()` on the candidate/expected body could accept Markdown with
different leading indentation (e.g. a code block) as if it were identical.

- The GitHub existence check now uses `gh api graphql` with a real
  `search(... first: 100, after: $cursor)` cursor-pagination loop —
  `state=all` is preserved (open and closed issues both participate), and
  the loop continues while `pageInfo.hasNextPage` is true, accumulating
  every page rather than stopping at a fixed count. A page reporting
  `hasNextPage: true` with no `endCursor`, or a result set that doesn't
  terminate within an internal safety ceiling (50 pages / 5,000
  candidates), fails loud (`InvalidOperationException`) instead of
  silently truncating.
- Body normalization now does **only** line-ending conversion (`\r\n`/`\r`
  → `\n`) and treats "ends with exactly one trailing newline" as
  equivalent to "no trailing newline" (GitHub's own storage convention) —
  it no longer calls `Trim()`. Leading indentation, inner spacing, and any
  trailing whitespace *before* the newline are preserved and compared
  exactly, so a body that differs by so much as a single leading or
  interior space is correctly treated as a **different** issue, not a
  match.
- `queue-state.json`'s `ReadQueueSignal` now collects **every** item
  matching the execution unit rather than returning on the first match —
  a second item for the same unit fails closed
  (`queue_state_duplicate_execution_unit`, naming every matching index and
  its identity) regardless of whether the duplicate entries agree or
  conflict; identity must never depend on JSON array order.
- New tests exercise the **real** `GhCliExistingIssueChecker` production
  class end-to-end (not just the `IGitHubExistingIssueChecker` interface
  stub used at the command level) via a `PageFetcherOverride` test seam
  that replaces only the literal `gh` process spawn with canned GraphQL
  JSON — covering multi-page open+closed accumulation, the two
  fail-loud-on-truncation paths, and the body-normalization matrix
  (byte-identical / CRLF vs LF / single-trailing-newline / leading /
  inner / trailing whitespace drift).

**Round-6 review repair — the GraphQL provider now fails closed on
authoritative-response defects, not just structural pagination gaps.** A
further review round found that the checker still trusted an
authoritative-looking response even when it wasn't: a GraphQL response can
carry non-empty `errors` alongside otherwise-plausible `data`; a
misbehaving server could return the same `endCursor` twice, looping the
fetch forever short of the safety cap; the search `type: ISSUE` field
actually matches both issues AND pull requests unless the query itself
excludes PRs; and an individual candidate could be incomplete (null body,
null/empty title, non-positive number, or a URL that doesn't exactly match
the requested repo) without being rejected before it reached
classification.

- Every GraphQL response is checked for a non-empty `errors` array
  **before** its `data` is ever read — the spec permits both to be present
  simultaneously, and partial `data` alongside an error is never treated as
  authoritative.
- The search query now includes `is:issue` (`repo:<repo> <unit> in:title
  is:issue`) so a similarly-titled pull request is excluded server-side
  rather than risking an empty/default-deserialized node; `state:` remains
  deliberately absent so both open and closed issues stay in scope. The
  exact literal query string is pinned by a test.
- Each page's `endCursor` is tracked in a seen-cursors set; a repeated
  cursor value fails loud immediately (`InvalidOperationException`) rather
  than looping until the 50-page safety cap.
- Every fetched candidate is validated **before** it is accumulated (not
  after, and never discovered only once classification or a restoration
  write is already underway): a positive issue number, a non-null/
  non-empty title, a non-null body (a null body is an invalid provider
  response — never silently substituted with empty text), and a URL
  matching the canonical `https://github.com/<requested repo>/issues/<number>`
  shape **exactly** for the repo this check was scoped to.
- New production-provider tests cover GraphQL errors alongside partial
  data, repeated-cursor detection, the literal `is:issue`/no-`state:`
  query pin, page-fetcher failure propagation, malformed JSON, and every
  candidate-validation failure mode (non-positive number, null/empty
  title, null body, and URL mismatches — wrong repo, wrong number, wrong
  scheme, or null).

**Round-7 review repair — the last two provider fail-closed gaps: a
non-null-but-empty cursor, and JSON `null` in place of a "non-nullable"
shape.** A further review round found that `hasNextPage=true` with an
**empty or whitespace-only** `endCursor` slipped past the null-only check
from round 6 — it would be sent back as `cursor=` on the next request and
recorded into the seen-cursors set as if it were a real value. Separately,
System.Text.Json silently assigns `null` to a declared-non-nullable
reference-type property when the JSON value is `null` (C# doesn't enforce
non-null at runtime) — so `pageInfo: null`, `nodes: null`, or a `null`
entry inside `nodes` would previously degrade into an incidental
`NullReferenceException` rather than an intentional provider diagnostic.

- `endCursor` is now checked with `string.IsNullOrWhiteSpace` — null,
  empty, and whitespace-only are all treated as "missing cursor" and fail
  loud before the loop would fetch again.
- `pageInfo`, `nodes`, and each individual node are explicitly checked for
  `null` immediately after parsing — a `null` in any of these positions
  fails loud with a specific diagnostic naming which piece was missing,
  never an unhandled NRE.
- New tests pin the full malformed-shape matrix through
  `FetchAllCandidates` itself: empty/whitespace-only `endCursor`, a
  `null`/wrong-type top-level envelope (`null`, `[]`, a bare number, a
  bare string), empty process output, `pageInfo: null`, `nodes: null`, and
  a `null` entry inside `nodes`.

---

### Canonical publish-order override — queue priority (G537)

Field incident (2026-07-19): after the G529 closeout, the orchestrator
ruled — with justification — to publish field-impact fixes G532/G534
ahead of a G530/G531 continuation. `queue-state.json`'s `priority` field
(values like `high` observed in the field) already existed, but no
selection surface consulted it — the orchestrator faced the forbidden
choice of hand-editing ordering state or abandoning the ruling, correctly
abandoned it, and reported the gap.

**`intent-cli queue reprioritize <execution-unit> --priority
<high|normal|low> --reason <text> [--write]`** is the bounded canonical
transition that closes this gap:

- Only ever mutates a **queued, not-yet-published** item's `priority` —
  refuses (no mutation, naming why) when the item's state isn't `queued`,
  or when it already has a linked GitHub issue.
- `--reason <text>` is required — a priority change without a recorded
  reason is never permitted.
- **Dry-run by default.** Without `--write`, the command reports the
  exact mutation that would happen (old priority, requested priority,
  whether anything would actually change) without touching
  `queue-state.json`. `--write` is required to mutate and append the
  `priority-changed` runs event (old/new priority plus the operator's
  reason).
- Requesting the item's current priority is a no-op (idempotent) — no
  write, no runs event, `changed: false`.

**`intent next-slice` orders eligible candidates priority-class-first
(high > normal > low), with authoring order (queue-state array order) as
the in-class tiebreak.** Every existing eligibility gate — packet
directory presence, execution-unit namespace regex, domain/repo filter,
**dependency completeness / non-empty `blocked_by`** (review repair: the
same rule `QueueSelection.SelectNext` already enforces — every
`dependencies` entry must be `completed`, and `blocked_by` must be
empty), G534's lifecycle-aware exclusion, and the legacy-retirement-marker
check — still runs exactly as before, per candidate, inside the same
loop; priority never lets a candidate skip a gate it would otherwise
fail. A "high" priority queued unit with an incomplete dependency or a
non-empty `blocked_by` is never selected ahead of an eligible
lower-priority unit — the loop simply tries the next candidate in
priority/authoring order, same as any other gate failure. Priority only
reorders which already-eligible candidate is tried, and in which order,
before that per-candidate gate loop runs. Because the reorder uses a
**stable** sort, a host where every item carries the enqueue default
(`"normal"`) — i.e. no priorities meaningfully set — produces
byte-identical output to pre-G537 behavior.

`QueueItem.Priority` remains a plain, unvalidated `string` at the schema
level (unchanged) — `queue reprioritize` is the only writer that
normalizes and validates it (`high`/`normal`/`low`, case-insensitive);
`next-slice`'s ranking function treats any unrecognized/missing value as
`normal` rather than erroring, so hand-authored or historical
`queue-state.json` files never fail closed on this field.

**Review repair — `queue reprioritize --write` uses a fail-closed,
repairable write order.** Writing `queue-state.json` before appending the
required `priority-changed` runs event could leave a durable priority
mutation with no audit record if the append step then failed. The order
is reversed — the runs event is appended **first**, `queue-state.json`
**second**:

- If the event append fails, `queue-state.json` is never touched at all
  — no durable change happened, and a plain retry starts fresh.
- If the event append succeeds but the `queue-state.json` write then
  fails, the audit trail already proves the attempted change and its
  reason even though the state file doesn't yet reflect it — never a
  silent, unaudited mutation. Re-running the exact same command detects
  the already-recorded event and retries **only** the `queue-state.json`
  write, so convergence never produces a duplicate event.

**Round-2 review repair — the dedup match is bound to the queue-state
generation, not merely the reusable transition text.** Matching only on
execution unit + event name + the deterministic reason text has a real
collision: replaying the exact same transition later (e.g. `normal→high`
reason `R`, then `high→normal` reason `S`, then `normal→high` reason `R`
again — a genuine third mutation) produces a reason string
byte-identical to the first event's, so the naive dedup would wrongly
treat that stale historical event as the pending audit for the third
mutation and skip appending its own — violating "one reasoned event per
mutation." The match now additionally requires the candidate event's
`Ts` to be **at or after** the `UpdatedAt` value read from
`queue-state.json` at the top of the current invocation. Every successful
write this command makes advances `UpdatedAt` to the same timestamp used
for its own event — so an event from any prior generation is necessarily
older than the current `UpdatedAt` and can never be mistaken for a
pending retry, while an event genuinely written moments ago by an
immediately-preceding failed attempt (against this same still-unmutated
generation) always satisfies the bound.

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
  usage error (mirrors `intent search --facet`'s validation). Every
  comma-separated option (`--facets` and `--scope` alike) requires each
  element to be non-empty after trimming and dedupes repeats in first-seen
  order — `--scope ","` or `--facets "vocabulary,,decider"` (an empty
  element) is a usage error, never silently "no scope" / a dropped element.
- `--scope <comma-separated paths>` narrows every group to nodes whose path
  OVERLAPS a hint, checked symmetrically in BOTH directions — a hint naming
  an ancestor directory of a node, or a node whose own path is an ancestor
  of a (more specific) hint, both count, on top of an exact match. Every
  hint form reduces to the same domain-relative segment list a node's own
  id already uses before comparing, so these are all equivalent: an
  absolute filesystem path under the domain root, the repo-relative
  `intents/<domain>/...` form, and the short domain-relative id form (with
  or without a trailing `.md`). A `..` segment is rejected (never silently
  resolved — it could otherwise walk a hint outside the domain it claims to
  scope); comparison is case-sensitive throughout. Omitting `--scope`
  entirely returns every domain facet node; passing `--scope` with hints
  that all turn out invalid or outside the domain matches NOTHING — it
  never silently falls back to "no scoping requested".
- A rejected `--scope` hint is never silent either: it produces a
  `facet_context_scope_warnings` entry (`hint`, `reason`) naming the exact
  hint and why it was rejected (outside the domain root, a `..` traversal,
  etc.) — so "matched nothing because every hint was invalid" is never
  indistinguishable from "matched nothing because a genuinely valid hint
  just didn't overlap any node". A mixed list still applies the valid
  hints while reporting every rejected one; `facet_context_all_scope_hints_rejected`
  is `true` only when every requested hint was rejected.
- A domain with ZERO facet-annotated nodes at all (not merely a
  `--scope`/`--facets` query that matched nothing) sets `facet_context_note`
  and renders an explicit note instead of an empty section — graceful
  degradation, never an error. Facets are optional; this is the norm before
  a tree adopts them.
- A malformed `facets:` declaration, or a Present declaration carrying an
  unknown value, is never silently dropped: both produce a `facet_context_warnings`
  entry (`path`, `reason`) in JSON and a `Warnings` list in Markdown, naming
  exactly what was excluded and why — so "genuinely no facets here" never
  looks identical to "facets were present but excluded". An unknown value
  does not disqualify a node from its OTHER valid facets.
- JSON shape: `facet_context: [{facet, nodes: [{id, facets, summary,
  path}]}]` (always 4 entries, or fewer when `--facets` is passed),
  `facet_context_note: string | null`, `facet_context_warnings: [{path,
  reason}]`, `facet_context_scope_warnings: [{hint, reason}]`,
  `facet_context_all_scope_hints_rejected: bool`.

**`intent-cli packet draft`** generates a `## Facet context` section inside
the scaffolded `review-context.md`, listing the facet nodes overlapping the
packet's own `implementation_issue_packet.intent_references` — the exact
same overlap logic `context collect`'s `--scope` uses (including the same
rejected-reference visibility above), so the two surfaces can never
disagree about what "overlaps". The generated content lives between two
HTML-comment markers (`<!-- BEGIN/END GENERATED FACET CONTEXT (G530) -->`);
the rest of `review-context.md` is hand-owned and untouched. Marker
handling is fail-closed — mutation is attempted ONLY when the file carries
EXACTLY one begin marker and one end marker in that order:

- **File does not exist yet**: written fresh, in full, with the current
  `intent_references` (read from an EXISTING `packet.yaml` on disk if one
  is already present — e.g. an operator hand-edited it after an earlier
  `packet draft` run — never the freshly-templated empty `[]` this same
  invocation might otherwise write for `packet.yaml` itself). Reported as
  `created`.
- **File exists AND carries exactly one correctly-ordered marker pair**:
  only the content strictly BETWEEN them is recomputed from the packet's
  CURRENT `intent_references` and replaced, using the FILE'S OWN existing
  newline convention (CRLF or LF — never hardcoded, so an existing CRLF
  file is never left with mixed line endings) — this is what keeps the
  section current through the ordinary workflow (scaffold with empty
  references → operator adds real references to `packet.yaml` → rerun
  `packet draft` → the block reflects them). Everything before the begin
  marker and after the end marker, including any hand-written prose around
  the block, is preserved byte-for-byte. Reported as `updated` when the
  recomputed content differs from what is already there, `skipped` when it
  doesn't (a genuine no-op, not a spurious update).
- **File exists but has NO markers at all** (predates this feature, or an
  operator removed them): left completely untouched, exactly like the
  other three scaffold files' plain skip-if-exists behavior. The markers
  are never retroactively injected into hand-owned content. Reported as
  `skipped`.
- **File exists with markers in any OTHER shape** — duplicate begin and/or
  end markers, an end marker appearing before its begin, or only one of the
  two present: also left completely untouched (never a silent partial
  update, never guessing which pair is "the real one"), but reported
  distinctly as `markers-malformed` with a `detail` string naming exactly
  what shape was found — this state must never look like the healthy
  no-markers-at-all case.

An empty `intent_references` list is itself a meaningful scope — "this
packet references nothing (yet)" — so the block shows every facet group as
empty, never the whole domain's facet nodes; only a domain with zero facet
nodes AT ALL renders the graceful-degradation note.

Both surfaces share one selector (`FacetContextSelector`) for scanning,
classifying, grouping, and scope-overlap matching, so ordering, filtering,
warning, and degradation semantics can't drift apart between them. Only
VALID facet values (the closed G529 set) are ever bucketed.

---

### Facet-check (G531)

`intent-cli intent facet-check` is a read-only lexical scaffold that points
a change proposal AT the G529 facet nodes G530 makes reachable — it checks a
proposal's candidate command/event terms against existing `vocabulary`/
`invariant` nodes so reviewers see naming collisions and coverage gaps
early, instead of reconstructing that mapping by hand. It is explicitly
**not** a semantic verifier and **never** a gate: matching is lexical (an
exact match once both sides are case/`-`/`_` normalized), false negatives
are expected, and the command always exits `0` regardless of findings.

```bash
intent-cli intent facet-check --domain <d> --packet G531 --format json
intent-cli intent facet-check --domain <d> --terms CreateOrder,ShipPackage --format json
```

- Exactly one of `--packet <execution-unit>` or `--terms <comma-list>` is
  required (mutually exclusive; a usage error otherwise). `--terms` rejects
  an empty element the same way `--facets`/`--scope` do elsewhere.
- **`--packet` mode** extracts candidate terms from that packet's
  `github-body.md` and `implementation.md` (concatenated, github-body
  first) — a term is extracted when it is a bare identifier inside
  backticks (e.g. `` `CreateOrder` `` — a backtick span containing
  whitespace or other punctuation, like a full command example, is
  skipped, since it is not a term; a backslash-escaped backtick, e.g.
  `` \` ``, never opens a span), a plain-text word with an internal
  camelCase/PascalCase boundary (e.g. `CreateOrder`), or a plain-text word
  ending in `Command`, `Event`, or `Query`. Noise is excluded BEFORE either
  rule runs, Markdown-aware:
  - **Fenced code blocks** are blanked (a class name inside one is never a
    term) — honoring CommonMark's actual boundary, not a loose
    approximation: a fence opener/closer may be indented AT MOST 3 spaces
    (4+ spaces, or any leading tab, is NOT recognized as a fence at all —
    see the separate indented-code-block pass immediately below for why
    that does not mean "not noise"); tilde (`~~~`) fences and
    4-or-more-backtick fences are recognized; a backtick fence's info
    string may never itself contain a backtick (CommonMark rejects that as
    an opener, ambiguous with inline code); a closer must use the SAME
    fence character and be at least as long as the opener — a wrong-
    character, too-short, or over-indented line is never mistaken for a
    closer and does not end the fence early; CRLF and LF line endings are
    both handled; and an UNCLOSED fence fails CLOSED: everything from the
    opening fence to end-of-document is masked as code rather than left
    free to leak identifiers. Inline single-backtick spans are a separate,
    unaffected concern, and a backslash-escaped backtick never opens one.
  - **Indented code blocks** — a SEPARATE, independent masking pass from
    fence recognition above: "this line does not open a fence" and "this
    line is not code" are different questions. A line qualifies once its
    leading whitespace reaches VISUAL COLUMN 4, computed the way
    CommonMark itself computes it — a tab advances to the NEXT multiple-
    of-4 column stop, not "4 columns flat" — so a single space followed by
    a tab (column 1 → tab advances to column 4) qualifies exactly the same
    as 4 literal spaces or one leading tab does; only 1–3 VISUAL columns
    is ordinary, merely-aligned prose and is deliberately excluded. A
    maximal run of consecutive lines that are each either indented (per
    the column rule above) or blank is masked as one block; a blank line
    WITHIN the run does not end it (mirroring CommonMark's own tolerance
    for blank continuation lines inside an indented code block), and the
    run always terminates at the first genuinely non-blank, non-indented
    line, where ordinary extraction resumes immediately. This pass
    carries exactly ONE documented simplification — it does not replicate
    CommonMark's full list-item-continuation disambiguation (indentation
    measured relative to a list marker rather than column 0), so a
    4-column-indented continuation line inside a list item is treated the
    same as top-level indented code; an accepted, documented limitation of
    this scaffold, not a bug.
  - **Inline Markdown/image links** — `[label](destination title)` /
    `![alt](destination title)` — are masked SELECTIVELY via a small
    hand-written scanner (not a naive first-`)`-wins regex): only the
    destination and optional title are blanked, while the bracketed label/
    alt text survives untouched, since it is intentional authored proposal
    text (e.g. `` [CreateOrder](design.md) `` still yields the term
    `CreateOrder`). The destination may be an angle-bracketed form
    (`<...>`, may contain spaces) or a bare form with BALANCED, escapable
    parentheses (`docs/(v1)/x.md` masks correctly in full, not just up to
    the first `)`); the optional title may be double-quoted, single-
    quoted, or parenthesized.
  - **Reference-style links** — a link's USAGE, `[label][ref]`/
    `[label][]`/a bare `[label]`, has no adjacent destination of its own,
    so only its label is ever at stake and it is left untouched (fully
    extractable, same as any other visible text). A link's DEFINITION line
    (`[ref]: destination "title"`) is pure destination/title metadata —
    the WHOLE line is blanked.
  - **Autolinks** (`<scheme://...>`) are blanked in their entirety — unlike
    `[label](url)` there is no separate visible label to preserve.
  - **Bare URLs** and **multi-segment paths** (e.g.
    `src/Commands/CreateOrder.cs`) are blanked outright.

  Extraction is appearance-ordered across the whole concatenated document
  (not "all backtick hits, then all plain-word hits" regardless of
  position — implementation.md's own candidates always sort after every
  github-body.md candidate, since concatenation order is what defines
  "document order" here), deduplicated by the same normalization as term
  matching, first-seen casing kept — so a term mentioned in
  `github-body.md` and again (in a different form) in `implementation.md`
  keeps the `github-body.md` occurrence's casing. A missing packet
  directory for the given execution-unit is a usage error (exit `1`); a
  packet directory with neither source file present simply extracts zero
  terms.
- **`--terms` mode** takes the term list explicitly — no packet, no
  extraction, no coverage section (there is no packet scope to check
  coverage against, so `coverage` is `null`, never a fabricated gap).
- Every term is checked against the domain's facet nodes lexically, using
  full-token equality only (never a substring search) against two
  node-authored surfaces: the node's own domain-relative id's LAST segment
  (its filename-derived name, e.g. `commands/create-order` → `create-order`)
  and its title (the extracted `summary`, typically the node's heading).
  Both are normalized the same way the term is (lowercase, camelCase/
  PascalCase boundaries and any run of `-`/`_`/other punctuation folded
  into single hyphens) before comparing — so `CreateOrder`, `create-order`,
  and `create_order` are all "the same term", and a node titled "Create
  Order" matches even when its id doesn't. A node carrying more than one
  facet is reported once (its highest-priority facet group wins for
  ordering), not once per facet.
  - `related_nodes`: every matching node, across all four facets, in the
    canonical `vocabulary → invariant → decider → acceptance-property`
    order. Each entry is `{node: {id, facets, summary, path}, evidence}` —
    `evidence` is a list of RECORDS, one per node-authored surface that
    matched: `{field: "id" | "title", value, match_kind}`, where `value`
    is the actual raw authored text compared (the node's own id-last-
    segment or title) and `match_kind` is `"exact"` when THAT specific
    field's raw text was identical to the term, else `"normalized"` (equal
    only after folding). There is deliberately no single aggregate
    match-kind for the whole match — a node whose id only normalized-
    matched but whose title matched exactly reports BOTH facts distinctly,
    never blended into one flag.
  - `collisions`: the subset of `related_nodes` whose node carries the
    `vocabulary` facet — an existing named concept the proposal's term
    duplicates or conflicts with, carrying the same per-field `evidence`.
  - `unmatched`: `true` when `related_nodes` is empty (no facet coverage at
    all for that term).
- **`--packet` mode only**: a `coverage` section reports the
  `acceptance-property` nodes overlapping the packet's own
  `implementation_issue_packet.intent_references` — the exact same G530
  scope-overlap logic `context collect --scope`/`packet draft` use,
  including the same rejected-INDIVIDUAL-reference `scope_warnings`
  visibility. `gap` is `true` when no acceptance-property node overlaps the
  packet's scope. A `scope_status` field distinguishes WHY the scope was
  what it was — `"valid-empty"` (an authored, deliberate
  `intent_references: []`), `"valid-non-empty"`, `"missing"` (no
  `packet.yaml`, or no `intent_references` key at all),
  `"malformed"` (the file fails to parse as YAML), or `"wrong-shape"` (the
  key exists but isn't a sequence) — with an accompanying
  `scope_status_detail` string for every non-valid status. This exists
  because a missing/malformed/wrong-shape packet scope degrades to the
  SAME computed `gap: true` as a genuinely authored empty list (an empty/
  broken scope hint still narrows coverage to nothing) — `scope_status`
  is what keeps those two cases from looking identical. A genuine I/O
  failure reading an EXISTING packet source file — `github-body.md`,
  `implementation.md`, or `packet.yaml` (not "missing" — an actual read
  error) — is treated as a real execution error (exit `1`, "Failed to read
  packet source..."), never silently folded into an empty scope or an
  empty term list.
- A domain with ZERO facet-annotated nodes at all sets `no_facet_data: true`
  (not an error — facets are optional) but still reports each term's
  extraction/match result (trivially all `unmatched`), so a caller can tell
  "nothing to check against" apart from "checked, found nothing". This
  field is unconditional in BOTH JSON and Markdown — Markdown always
  renders an explicit `No facet data: yes|no` line, never omitting it when
  false.
- A malformed `facets:` declaration or an unknown facet value on a node
  produces the same `warnings` entries (`path`, `reason`) G530 surfaces —
  never silently dropped.
- Every result carries a `disclaimer` field stating the lexical-scaffold,
  non-gate positioning explicitly, in JSON and Markdown alike.
- JSON shape: `{domain, disclaimer, no_facet_data, terms: [{term,
  related_nodes: [{node: {id, facets, summary, path}, evidence: [{field,
  value, match_kind}]}], collisions: [...], unmatched}], coverage:
  {nodes: [...], gap, scope_status, scope_status_detail,
  scope_warnings: [{hint, reason}]} | null, warnings: [{path, reason}]}`.
- Out of scope for this slice (see the G531 issue for the full boundary):
  semantic/embedding-based matching, any blocking/gating behavior, wiring
  into reviewer guidance or orchestrator delegation preflight, and
  annotating any domain tree — this command only reads and reports.

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
