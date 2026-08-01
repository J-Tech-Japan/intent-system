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

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] [--claimed-silent-minutes <m>] [--backlog-idle-minutes <m>] --format json|markdown`
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
- `backlog-ready-idle` (G544) — the last uncovered stall class: work that is
  **ready but never started**. Fires when ALL of the following hold: (1) WIP
  is empty for the requested domain — no open PR resolves as belonging to
  it, and no open issue carrying `intent-target` resolves as belonging to it
  either (or fails to rule itself out). A PR never itself carries
  `intent-target`; its domain is instead resolved through its CLOSING ISSUE
  (never the PR's own title), using the same execution-unit/domain
  corroboration rules used everywhere else in this surface. A candidate
  (PR or issue) whose domain cannot be corroborated at all — no closing-
  issue link, the closing issue not found among open issues, or its
  execution unit uncorroborated — is conservatively treated as blocking
  EVERY domain, since a false "idle" report is the dangerous direction
  here; only a candidate CONCLUSIVELY confirmed to belong to a DIFFERENT
  domain is excused;
  (2) the SAME canonical selector `issue publish-flow` preflight itself uses
  (`intent next-slice`'s candidate selection — dependency/blocked-by,
  lifecycle, domain, and contract-completeness gates all included, not a
  separate heuristic) reports a publishable (`issue-cut-ready`) candidate;
  (3) no `runs.jsonl` activity has been recorded for at least
  `--backlog-idle-minutes` (default **45**). "Activity" here is the MAXIMUM
  `ts` across every row in `runs.jsonl` — a different signal than every
  other kind's GitHub-entity-timestamp approach, since by construction
  nothing has been published yet for this candidate to carry a GitHub
  timestamp of its own. A missing, empty, or unparseable `runs.jsonl` can
  never establish a baseline and fails closed into `excluded[]`
  (`activity-data-unusable`), never a guessed age. `recommended_action` is
  the canonical publish command for the named unit (`intent-cli issue
  publish-flow <unit> --repo <r> --write --format json`). Field incident,
  2026-07-20 (immediately after the G539 closeout): WIP was empty, four
  authored packets (G540–G543) were `issue-cut-ready` and unpublished, and
  `stalled-work` reported `stalled: false` regardless — recovery required an
  explicit human/design WAKE message. `backlog_idle_minutes_threshold` is
  reported alongside `stale_minutes_threshold` in every result.
- `repair-stalled` (G546) — a PR in the repair lifecycle
  (`intent-pr-request-update`, `intent-pr-update-in-progress`, or
  `intent-pr-rereview-ready`) with no observable activity for longer than
  `--repair-silent-minutes` (default **180** minutes). This is the promotion
  G533 deferred "until field data exists"; the data arrived twice. The
  sharper incident: a G545 repair claimed `intent-pr-update-in-progress` and
  went silent for **four days** (2026-07-23 → 07-27) after the implement
  session died, while `stalled-work` reported `stalled: false, items: []`
  throughout — recovery needed a manual ping. That PR was a **draft**, and
  every other PR kind here skips draft PRs, so no kind covered it at all;
  `repair-stalled` therefore covers drafts too (inside the threshold a draft
  repair PR stays invisible exactly as before — no informational item is
  invented for it). `recommended_action` is always a **status check to the
  responsible thread** — `implement` for `intent-pr-request-update` /
  `intent-pr-update-in-progress`, `review-dispatch` for
  `intent-pr-rereview-ready` — and **never** a transition or a reassignment:
  silence alone never establishes that a repair succeeded, failed, or should
  be taken from its owner. Observable activity is the PR's own `updatedAt`,
  the one field covering all three activity classes (GitHub bumps it on a
  push to the head branch, on any comment, and on any label change). Fails
  closed like `claimed-but-silent`: a missing or malformed `updatedAt` means
  silence cannot be established, so the PR is **not** promoted rather than
  flagged on unusable evidence.
- `design-decision-pending` (G552) — a hold blocked on a **design decision**,
  recorded as an OPEN clarification artifact through the canonical clarify
  surface (`intent-cli clarify open`). Reports the blocking execution unit,
  the clarification's age (from the artifact's own `createdAt` — the moment
  the block was recorded), and a one-line question summary.
  `recommended_action` names the exact clarification to answer
  (`intent-cli clarify answer --execution-unit <unit> --question-id <id>
  --answer "<decision>"`) plus the operator escalation path; it **never**
  auto-answers, because the answer is design's content. Answering (or
  applying, or cancelling) the clarification is what clears the item — no
  threshold and no separate transition. This is the only kind here with no
  GitHub entity of its own, which is exactly why the stall it detects was
  invisible: field incident 2026-07-28 16:11 → 07-29 01:29, where the G551
  review held its final verdict for **nine hours** on a one-line wording
  ruling while every technical check was green, the hold lived only in agmsg
  messages, and `stalled-work` reported `stalled: false` throughout — the
  fourth design-absence stall in the field record. Fails closed in both
  directions: an artifact that cannot be read or deserialized goes to
  `excluded[]` with its path (`clarification-unreadable`) rather than being
  skipped as answered, and a clarification whose packet declares a different
  domain is excluded rather than attributed to the requested domain. The
  guide's clarification-backed hold rule is what puts the artifact on disk
  for this kind to read — an agmsg-only hold is a contract violation, and if
  the hold is real but this kind is absent, the artifact was never recorded.
- `knowledge-writeback-pending` (G564) — a **closed-out** unit (a
  `closeout-recorded` event in `runs.jsonl`) whose packet **declared** a
  knowledge write-back — any `knowledge_updates.*.required: true`
  (`intent_tree` / `adr` / `diagram` / `docs`) or
  `closeout_learning.write_back_required: true` — with **no write-back
  record** at `.intent-cli/knowledge-writebacks/<unit>/record.json`. The item
  carries the age from closeout (the EARLIEST `closeout-recorded` event, so a
  retried closeout cannot reset it), the declared facets, and
  `declared_write_back_targets`; `recommended_action` names
  `intent-cli automation knowledge-writeback-record`. A unit that declared
  nothing required never appears — declining is a legitimate answer, and this
  kind detects broken promises, not missing enthusiasm. Fails closed in both
  directions: an unreadable packet declaration, an unreadable `runs.jsonl`, and
  an unreadable existing record all go to `excluded[]` **with their path**
  (`knowledge-metadata-unreadable`) rather than being read as "nothing
  pending". Units closed out before this shipped are out of scope by default
  (floor: `2026-08-01T00:00:00Z`); `--knowledge-writeback-since <iso-8601>`
  opts into scanning further back. Nothing here writes intent content — the
  tree is written by design (G300); this kind only makes an unrecorded
  obligation visible and aging. Field evidence: the pre-v0.7.0 audit
  (2026-07-31), where node 09 still described a pre-implementation design,
  node 02 recorded none of the seven release-flow rules the docs implement,
  and node 08 lagged the wake contract by releases — weeks of drift with no
  structural signal.

**Informational categories (G533)** — `is_informational: true`,
`recommended_action` is descriptive prose (never a transition command), age
is reported for visibility only:

- `repair-pending` — a PR carrying `intent-pr-request-update` and/or
  `intent-pr-update-in-progress`, **inside** `--repair-silent-minutes`
  (G546 — past it, the item is promoted to the actionable `repair-stalled`
  kind above; inside it the output is unchanged). Field finding: an OPEN PR in exactly this
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
  pushed, awaiting re-review), **inside** `--repair-silent-minutes` (G546).
  Same `updatedAt`-based age approximation as `repair-pending`.
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
  threshold is an explicit out-of-scope follow-up. **G545: exempts a unit
  whose queue-state item is `state=blocked`** — consulted once per
  `stalled-work` call, tolerant of a missing/malformed `queue-state.json`
  (falls through to this kind's pre-G545 GitHub-labels-only behavior
  unchanged, so a domain that never uses `queue-state.json` never loses
  this detection). A queue-blocked unit is never reported here; see
  `blocked-label-drift` below.
- `blocked-label-drift` (G545) — a transitional GitHub/queue-state mismatch,
  never a stall: the queue-state item for this unit is `state=blocked` with
  an explicit `blocked_by` reason, but its GitHub issue does not yet carry
  `intent-issue-blocked` — the label side hasn't been reconciled yet.
  `recommended_action` names the exact canonical reconcile command
  (`intent-cli automation issue-block <unit> --repo <r> --issue <n> --reason
  "<blocked_by text>" --write --format json`). Once that command converges
  the label, the same unit disappears from `stalled-work` entirely (neither
  `claimed-but-silent` nor `blocked-label-drift` fires — GitHub and
  queue-state agree). Field finding, 2026-07-21 (sekiban-as-a-service): 5
  items (SKS-G818, SKS-G837, SKS-G835, SKS-G839, SKS-G840) were `state=blocked`
  in queue-state with an explicit `blocked_by` dependency, yet each was
  reported as `claimed-but-silent` every wake because `claimed-but-silent`
  read only GitHub labels and no issue-level "blocked" representation
  existed at all.

**`intent-cli automation issue-block <execution-unit> --repo <owner/repo>
--issue <n> --reason <text> [--write] [--dry-run] [--format text|json]`**
(and its `--clear` counterpart, `--reason` omitted) is the ONE canonical,
bounded transition that converges BOTH authoritative representations of
"blocked" for a single execution unit:

- **queue-state** — `state=blocked` and `blocked_by: ["<reason>"]`, applied
  through the existing, unmodified `QueueManager` blocking transition (the
  same mechanism `queue transition <unit> blocked` uses), plus a durable
  `runs.jsonl` audit event (`event: blocked` / `queued`, `by: intent-cli
  automation issue-block`, carrying the reason).
- **GitHub** — the `intent-issue-blocked` label, applied through the same
  `IGitHubLabelMutator` seam `worker claim`/`worker complete` use. Raw `gh
  ... edit --add-label`/`--remove-label` is never permitted.

`--clear` converges both sides back: it restores `state=queued` **and
empties `blocked_by`**, then removes the label. Emptying `blocked_by` is not
cosmetic — `intent next-slice`'s eligibility gate rejects any item with a
non-empty `blocked_by` regardless of its state, so leaving the stale reason
behind would keep a "cleared" unit permanently unselectable, merely
relocating the drift from GitHub into queue-state.

The execution unit is a **required positional argument**, never inferred
from the issue title. The queue item must additionally carry a **complete
`linked_issue`** — both a repo and a number — and both must agree with
`--repo`/`--issue`: the repo is compared canonically (URL/ssh/`.git`/
trailing-slash shapes normalized to `owner/repo`) and case-insensitively,
the number exactly. Missing linkage, a different repo, and a different
number are each refused; a missing linkage is absent evidence, not consent,
and number-only agreement proves nothing because issue #818 exists in
almost every repository. A unit already blocked with a *different* reason is
likewise refused, not silently overwritten; clear it first.

A present but unreadable/unparseable `runs.jsonl` is also a hard stop: the
command refuses before appending, writing queue-state, or even *reading*
GitHub labels, and names `intent-cli automation runs-audit` as the repair
path. Transitioning against a trail it cannot parse would corrupt that trail
further and would decide retry-vs-fresh-append from no evidence at all. A
**missing** run log remains the valid first-event case. Every refusal above
happens before any interaction with the run log, queue-state, or GitHub, so
all three sides are left byte-identical.

**Write ordering is fail-loud and repairable.** The `runs.jsonl` audit event
is appended FIRST and `queue-state.json` written SECOND (matching `queue
reprioritize`'s convention), so no queue mutation is ever silently
unaudited. The two sides are then converged **independently**: each side
checks its own current state and mutates only if it is not already at the
target. Re-running the exact same command after any partial failure
therefore retries only what has not converged — a completed step is never
repeated. Queue-side audit idempotency is decided by the run log itself: a
matching event is reused (never duplicated) only when it is the most recent
event for that unit *and* queue-state has not yet caught up to it, which
distinguishes a genuine partial-failure retry from a later re-block that
happens to reuse the same reason text after a full block/unblock cycle.

Dry-run by default: it reports what would change on both sides and touches
neither `queue-state.json`, `runs.jsonl`, nor GitHub, and never reports
`converged: true`. `--reason` is required when applying and never permitted
with `--clear` — a blocked transition without a recorded reason is refused,
mirroring `queue reprioritize`'s reason requirement. `intent-issue-blocked`
coexists with `intent-issue-in-progress` rather than replacing it: the
worker still owns the issue, it just cannot currently proceed.

Each item reports `kind`, `execution_unit`, `issue` and/or `pr` (number +
url), `age_minutes`, `is_informational`, and `recommended_action`.
`--stale-minutes` filters out items younger than the given threshold
(default `0` — report everything with its age; callers pick their own
threshold) — this applies uniformly across all nine kinds; `claimed-but-silent`,
`backlog-ready-idle` and `repair-stalled` each additionally gate on their OWN
`--claimed-silent-minutes` / `--backlog-idle-minutes` / `--repair-silent-minutes`
threshold before an item is even considered (so raising `--stale-minutes`
alone can never make any of them appear earlier than its own threshold
allows).
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

### Session-layer mode: which transport the four threads use (G570)

`intent-cli session-layer show --domain <d> [--team <t>] [--format markdown|json]`
`intent-cli session-layer set --domain <d> [--team <t>] --mode agmsg|herdr-only [--dry-run|--write] [--format markdown|json]`

The four-thread model (design / orchestrator / implementation / review) is one
thing; the SESSION LAYER those threads talk over is another, and per the
operator ruling of 2026-08-01 it is now selectable rather than fixed.

- **`agmsg` is PRIMARY** — the practiced, maintained transport, and the default
  when nothing is recorded.
- **`herdr-only` is PREVIEW** — a single-machine alternative where herdr is the
  terminal controller and no separate message bridge runs. **The preview
  qualifier scopes the TRANSPORT only.** The four-thread model itself stays
  PRIMARY and unqualified in both modes, exactly as G540 ruled; choosing a
  transport never makes the model provisional.
- **One mode per team.** Mixing agmsg and herdr-only delivery inside one team is
  a contract violation, not a fallback: two transports mean two views of who was
  told what.

Semantics:

- **Scope** — recorded per domain, and per team where teams are modeled. A
  team-scoped record wins over a domain-wide one; the team is the narrower
  statement.
- **Default** — `agmsg` when no record exists. `show` never writes.
- **Persistence** — `.intent-cli/session-layer-mode.json`, written ONLY by
  `session-layer set --write` (G548 lineage: durable state changes through
  canonical commands, never by hand).
- **Idempotent** — re-recording the mode already in force at the same scope is a
  no-op and records no transition, so a setup script can assert the mode without
  filling the trail with runs instead of decisions.
- **Reversible, with a trail** — each entry keeps every transition (`from`,
  `to`, `at`). Going back to agmsg is as ordinary as going to herdr-only, and
  the record shows both.
- **Fail-closed** — an unknown mode is refused rather than recorded, and an
  unreadable record is refused rather than overwritten.

**Routing.** The recorded mode selects which operating sections
`guide orchestrator-thread` renders:

- under `agmsg`, the guide renders exactly as before this slice — the routing is
  the identity, so the practiced path cannot shift because a mode concept exists;
- under `herdr-only`, the wholly agmsg-specific operational sections (setup and
  registration, receiver readiness, monitor/bridge diagnostics, the agmsg reply
  contract, design-receiver registration) are replaced by pointers to the
  herdr-only operating sections, which ship in **G571**;
- mode-independent canon renders in BOTH modes — supervision, isolation,
  liveness, the wake contract, publish authority, the design↔orchestrator
  double-check rule, dependency planning, escalation. Those are properties of
  the model, and the model does not change with the transport.

**Applicability is declared per section, and it is four-valued** (design ruling,
host main `fb1913c8`): `agmsg-only`, `herdr-only`, `mode-independent`, and
`mode-independent-with-transport-mechanics`. The renderer selects whole sections
from the recorded mode — in markdown and in JSON alike, so a field consumer and
a prose reader never disagree about what applies.

- **agmsg-only** sections are replaced, whole, by one *Session-layer switch
  checklist* section naming what it replaced. They are not annotated and not
  rendered.
- **mode-independent** sections render unchanged in both modes.
- **mode-independent-with-transport-mechanics** sections carry canon that binds
  in both modes but expresses it through an agmsg mechanic. The section is KEPT
  and only its mechanic-bearing sentences become pointer-only text — the rule
  still binds, only the agmsg way of carrying it out is pointed away.

Pointer-only text is G570 routing metadata: it says what does not apply and
where the counterpart ships. It never says what to run instead, because a
concrete herdr procedure is G571 content and is forbidden here.

Substring/token replacement was tried and rejected as the correctness
mechanism: it is simultaneously too weak (operative prose such as "wait for an
agmsg delegation" carries no mechanic token) and too strong (an earlier draft
deleted the timer-loop canon because it mentioned agmsg). Applicability is a
property of a section's subject, so it is declared once, in
`SessionLayerSections`, where it can be reviewed.

Setup intake is mode-aware for the same reason: the agmsg team name and
delivery mode are agmsg-only inputs, so a herdr-only setup is never told it is
missing fields its transport has no concept of.

**Fail-closed state.** An invalid PRESENT record is not absence. A malformed
file, an unknown mode, or a record whose current mode disagrees with its own
transition trail (proof it was not written by `session-layer set --write`) makes
every mode-dependent surface fail with a named `session-layer-mode-unreadable`
error and render NO guidance — rendering the default would hand the reader
instructions for a transport the team may not be running. `set` refuses to
overwrite such a record rather than repairing it silently.

`guide model` and `guide onboarding` describe both modes, and onboarding reads
the mode before any transport-specific step so a fresh agent never follows the
wrong setup.

### Intent-tree co-evolution: recording a performed knowledge write-back (G564)

`intent-cli automation knowledge-writeback-record --execution-unit <u> --commit <host-commit-sha> [--target <path>]... [--note <text>] [--dry-run|--write] [--format json|markdown]`

records that the write-backs a packet **declared** were performed, with the
host commit as evidence. It is the clearing half of
`knowledge-writeback-pending` above.

- **Records only.** The write-back itself is design's host-side act — this
  command never writes intent content and never mutates the intent tree
  (G300). Hand-editing the artifact is not the path.
- **Artifact.** `.intent-cli/knowledge-writebacks/<unit>/record.json`, one per
  execution unit: `artifact_kind`, `execution_unit`, `host_commit`,
  `recorded_at`, `targets`, `note`.
- **Idempotent.** Re-recording the SAME commit (case-insensitively) is a
  no-op success — `already_recorded: true`, `applied: false`, and the file is
  left byte-identical, so a retried closeout wake cannot move `recorded_at`
  off the real event.
- **Fails closed.** An execution unit with no `.intent-cli/issues/<u>/packet.yaml`
  is UNKNOWN and refused; evidence that is not a 7–40 character hexadecimal
  SHA is refused; an existing record with a DIFFERENT commit is refused rather
  than overwritten (replacing evidence silently is how an audit trail stops
  being one); an existing record that cannot be read is refused rather than
  clobbered.
- **The execution unit is a canonical identifier, validated before any
  filesystem access.** ASCII letters, digits, `-`, `_` and `.`, never leading
  with `.` and never containing `..` — which structurally excludes path
  separators, rooted paths, drive/ADS colons, dot-segments, whitespace, and
  control characters. Both derived paths (the packet and the record) are then
  re-checked for containment beneath `.intent-cli/issues` and
  `.intent-cli/knowledge-writebacks`. The same validation is applied to
  execution units read out of `runs.jsonl` by the detector, since a runs log is
  data rather than a trusted identifier; a non-canonical unit there is reported
  in `excluded[]` and no path is derived from it.
- **A record is evidence only for the unit it names.** On every consumption —
  the detector's clearing path and the recorder's idempotency/refusal path
  alike — the record's embedded `execution_unit` must equal the unit it is
  stored under, and `host_commit` must be SHA-shaped. A record under
  `…/G564/record.json` declaring `execution_unit: G999`, or carrying evidence
  that is not a commit, is reported as unreadable-with-path rather than
  clearing `knowledge-writeback-pending`.
- **`--dry-run` is the default.** `--write` is required to persist.
- Recording against a unit that declared nothing required still succeeds, but
  the result carries a warning: if the tree genuinely owed something there,
  the packet's declaration was dishonest, and that is the defect to fix.

The duty this enforces is stated in the guides rather than only here: the
design-thread playbook (`guide orchestrator-thread`), the packet-authoring
prompts (`guide workflow task packet-draft`), and the closeout prompt
(`guide closeout run`, Stage 5b) all carry it, single-sourced so they cannot
drift apart. The orchestrator's closeout report to the design thread
enumerates the packet's declared write-backs and whether each is recorded or
pending — read-only propagation of packet metadata, no host mutation.

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

**Queue-blocked units are exempt from the WIP gate (G553).** Retirement is one
way work leaves WIP; being *parked* is the other. An issue whose queue item is
in the **converged blocked state** — queue `state=blocked` **and** a non-empty
`blocked_by` — no longer counts toward `in_flight_issues`, so a next-slice
candidate flips from `skip-next-slice-due-to-wip` to `candidate-ready` when the
only in-flight items are blocked. A blocked unit is parked by design and cannot
progress until it is unblocked; counting it starves publication exactly when the
operator deliberately set work aside. Field finding (sekiban-as-a-service,
2026-07-26 on 0.5.0): the gate cited issue #1783 whose unit SKS-G818 had been
parked through the supported claim-preserving block transition — G545 exempted
blocked units from `claimed-but-silent`, but this gate was never covered.

- **Convergence is required, and it is two-sided.** `state=blocked` with an
  empty `blocked_by`, or a `blocked_by` reason on an item that is not
  `state=blocked`, is **drift** per G545 — not an exemption. Half-converged
  items keep counting (fail-closed), and the state/reason mismatch is reported
  as a warning naming the repair commands.
- **The exemption is never silent.** Each exempted unit appears in the new
  `wip_exempt_blocked_units` diagnostics field with its execution unit, issue
  number, and `blocked_by` reasons, in both the JSON and text renderings.
- **Linkage is the queue item's own `linked_issue`** (repo + number) — the
  canonical record `issue publish-flow` writes — never a title guess. An issue
  that cannot be linked to a queue item is not exempt, and a `linked_issue`
  naming a different repo never excuses this repo's issue.
- **Fail-closed on unreadable host state.** A missing queue-state exempts
  nothing silently (pre-G553 behavior exactly); an unparseable one exempts
  nothing and warns. Unblocking restores counting on the very next call.
- **Peer surfaces**: `intent next-slice` already counts only
  `active`/`review`/`fixing` items as WIP, so blocked units were never counted
  there — no divergent copy of this rule was needed or added.

Unchanged by G553: the block/clear transitions and convergence rules (G545 owns
them), WIP-cap semantics for non-blocked units, and `automation stalled-work`
(G545 already exempts blocked units there).

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

**G544 review repair — the all-packet fallback preserves the same
dependency/blocked-by gate.** When the primary `queued`-ordered loop finds
no eligible candidate, `next-slice` falls back to re-enumerating every
packet directory under `.intent-cli/issues/*` (covering runtime-created
packets with no queue-state entry at all). That fallback was NOT
re-applying the dependency/blocked-by gate the primary loop had just used
to reject a queue-known unit — silently resurrecting it as `issue-cut-ready`
regardless. The fallback now applies the identical gate to any unit
queue-state tracks as `Queued`; a unit with no queue-state entry (nothing to
gate on) is unaffected. This was surfaced by G544's `backlog-ready-idle`
detection, which depends on this same selector never reporting a false
`issue-cut-ready`.

`QueueItem.Priority` remains a plain, unvalidated `string` at the schema
level (unchanged) — `queue reprioritize` is the only writer that
normalizes and validates it (`high`/`normal`/`low`, case-insensitive);
`next-slice`'s ranking function treats any unrecognized/missing value as
`normal` rather than erroring, so hand-authored or historical
`queue-state.json` files never fail closed on this field.

**Legacy/out-of-enum priority values and their ordering rule (G543).**
Field observation, 2026-07-20: the host `queue-state.json` (1467 items)
has `high` 1405, `medium` 59, `normal` 3 — `medium` is not in the
documented `high|normal|low` enum. The documented enum itself is **not**
expanded to include `medium` or any other legacy value; instead, this
defines exactly how out-of-enum data behaves, so the selector's behavior
on real data is never undefined:

- **What priority is for**: it only orders already-**eligible** candidates
  (see above — every gate still dominates). With a host at 1405/1467
  items on `high`, priority-first selection degenerates to authoring
  order among the `high` bucket — not a defect, just the shape this
  mechanism produces when almost everything shares one priority class.
- **Ordering rule for every value, including legacy ones**: `high` ranks
  first, `low` ranks last, and **every other value — missing, empty, or
  any out-of-enum/legacy string such as `medium` — ranks exactly like an
  explicit `normal`**, between `high` and `low`. This is total and
  deterministic: there is no priority value for which the selector's
  ordering position is undefined. `QueuePriorityClassification.Rank` (in
  `IntentSystem.Cli.Commands`) is the single shared implementation both
  `next-slice` ordering and the drift report (below) use, so the two
  surfaces can never disagree. A regression fixture with a literal
  `"medium"` item proves this position.
- **Migration recipe (no new command needed)**: `queue reprioritize
  <execution-unit> --priority <high|normal|low> --reason <text> --write`
  already works as the canonical migration path off a legacy value —
  only the *requested* value is validated against the documented enum;
  the *existing* value is read, reported (`old_priority`), and compared
  with **no** validation at all, so an item currently at `medium` (or any
  other legacy value) can move to any documented value without
  hand-editing `queue-state.json`. The same fail-closed, audited
  `priority-changed` runs event described below applies unchanged.
- **Drift visibility**: **`intent-cli queue priority-drift [--format
  json|markdown]`** is a new, read-only report — never mutates
  `queue-state.json` or `runs.jsonl` — listing the item count for every
  distinct priority value present, always including `high`/`normal`/`low`
  (even at zero) for a stable report shape, and flagging any value
  outside that documented enum (`has_drift: true`,
  `out_of_enum_values: [...]`). Out-of-enum values are ordered by count
  descending (biggest drift first), tie-broken alphabetically. This is
  how the 59-item `medium` case becomes visible without a hand-written
  script.
- **No silent rewriting**: no command mutates `priority` as a side effect
  of an unrelated operation — e.g. `queue transition` re-serializes the
  whole `QueueState` to change `state`/`blocked_by`, but a pre-existing
  `medium` value on that same item survives byte-for-byte untouched.

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

**Round-2 review repair (superseded by round 3 below) — the dedup match
was first bound to `queue-state.json`'s `UpdatedAt` timestamp,** to fix a
real collision: replaying the exact same transition later (e.g.
`normal→high` reason `R`, then `high→normal` reason `S`, then
`normal→high` reason `R` again — a genuine third mutation) produces a
reason string byte-identical to the first event's, so a naive dedup on
execution unit + event name + reason text alone would wrongly treat that
stale historical event as the pending audit for the third mutation.

**Round-3 review repair (superseded by round 4 below) — the dedup match
was next bound to a SHA-256 content fingerprint of the pre-mutation
`queue-state.json` bytes,** to eliminate all wall-clock dependence from
round 2's `Ts >= UpdatedAt` bound (which broke at timestamp equality, on
clock rollback, and because the write path never guaranteed `changedAt`
strictly advances past `UpdatedAt` in the first place).

**Round-4 review repair — a content fingerprint identifies BYTES, and
this state machine can revisit identical bytes; the dedup token is now a
durable, injective `priority_revision` counter, not a fingerprint of
anything.** The round-3 fingerprint is collision-resistant for different
content, but genuinely revisitable: `normal→high(R)` then `high→normal(S)`
under one fixed clock reproduces the *exact original file bytes* (same
priority, same `updated_at`, same everything) — a real revisit, not a
hypothetical one. A subsequent `normal→high(R)` request then computes the
identical fingerprint and tagged reason as the first event, so the
fingerprint-based dedup wrongly treats that stale first event as pending
for the third, genuinely distinct mutation.

`QueueItem` now carries `priority_revision` — a plain `int`, deliberately
NOT `required`, so a legacy `queue-state.json` predating this field
simply deserializes it as `0` (the correct migration semantics: revision
counting starts from the first `queue reprioritize` ever applied to a
given item). Every successful `--write` bumps it by exactly 1. The
recorded reason now carries the `fromRevision->toRevision` pair (e.g.
`... (revision 0->1)`), and the dedup match is an exact match on that
tagged reason (plus execution unit + event name), same as before — only
the tag's *source* changed:

- `toRevision` can mathematically never be produced by two distinct
  successful mutations of the same item: each mutation strictly consumes
  the "next" integer in the durably-persisted sequence, and once
  consumed it is never the "next" one again — **regardless of whether
  every other field of `queue-state.json` later cycles back to
  byte-identical content**, since the counter itself is part of that same
  durable content and only ever moves forward.
- A genuine retry (failed queue-state write, then re-run) reads the SAME
  `fromRevision` from the still-unmutated file both times — the failed
  attempt never wrote the bump — so it computes the identical
  `fromRevision->toRevision` pair and finds its own already-recorded
  event.
- A historical event with no revision tag at all (data predating this
  fix, or hand-edited) can never exact-match a freshly-tagged reason.

**Round-5 review repair — the revision counter itself needed input
validation; recovery needed explicit cardinality/ownership classification
instead of a bare existence check; and the final write needed protection
against a concurrent writer.**

- `PriorityRevision` was an unconstrained `int` — a negative value
  deserialized successfully, and `fromRevision + 1` was unchecked
  arithmetic that would silently wrap to `int.MinValue` at
  `int.MaxValue`, directly violating the monotonic/injective invariant.
  Both dry-run and `--write` now validate `PriorityRevision >= 0` and
  compute `toRevision` with `checked` arithmetic **before** previewing or
  mutating anything — a negative or exhausted revision fails closed with
  no event and no queue-state write, requiring manual repair.
- Recovery used `events.Any(...)` — a bare existence check. Since the
  revision pair *is* the operation identity, two IDENTICAL events already
  claiming the same pair were silently accepted as "one pending attempt"
  (masking a real duplication bug), and a genuinely CONFLICTING event —
  same pair, different reason or direction — was silently ignored, with a
  second, different event appended right past it. Recovery is now an
  explicit classification: **zero** matches → safe to append; **exactly
  one EXACT match** (same reason too) → the pending audit for an
  in-progress retry; **two or more identical matches**, or **any
  mismatched-reason match** on the same pair → fails closed (exit 1,
  queue-state untouched, naming the conflicting/duplicate event) rather
  than silently resolved either direction.
- The read (top of `Execute`) → event-append → queue-write sequence is
  now protected against a stale concurrent writer. Immediately before the
  final `queue-state.json` write, the file is re-read fresh; if the
  target item's `priority_revision` no longer equals the `fromRevision`
  this attempt started from, the write refuses (the audit event is
  already durably recorded, so this is never silent) rather than
  blindly overwriting whatever a concurrent writer produced. The final
  mutation is also applied onto that **fresh** re-read (not the stale
  copy from the top of `Execute`), so an unrelated concurrent change to
  any *other* field or item is preserved rather than clobbered.

**Round-6 review repair — the round-5 "re-read + compare" was still a
TOCTOU check, not authoritative mutual exclusion.** Two concurrent
invocations could both read the same `priority_revision`, both classify
zero event claims, both append their own event, both re-read and see the
still-unchanged revision, and both then commit — with identical requests
that duplicates the audit trail; with different requests it silently
produces last-writer-wins state with a conflicting orphaned event.

`--write` now acquires a **non-blocking, OS-level exclusive lock**
(`FileShare.None` on a stable sibling file next to `queue-state.json`,
e.g. `queue-state.reprioritize.lock`) **before** the authoritative
queue-state/runs.jsonl read, and holds it across revision validation,
event-claim classification/append, the fresh re-read, and the final
commit — released only once the invocation is completely done. A second,
concurrent invocation that cannot acquire the same lock fails closed
**immediately** (no wait, no retry) rather than racing to the compare
point at all. Dry-run never mutates and never takes the lock. The
round-5 fresh-re-read-and-rebuild is retained *underneath* the lock — it
still protects against a non-cooperating writer (any tool that mutates
`queue-state.json` without going through this lock), while the lock
itself is what makes two *cooperating* `queue reprioritize` invocations
mutually exclusive.

**Round-7 review repair — the lock could still leak on a throwing
test callback, one boundary short of the round-6 guarantee.** The
test-only `OnLockAcquiredForTest` hook fired *before* entering the
`try`/`finally` that disposes the acquired lock stream. Any exception
from that callback (or, by the same shape, any future post-acquisition
code accidentally placed ahead of the `try`) would leave the OS-level
lock handle undisposed — a subsequent independent invocation would stay
locked out until GC/finalization eventually closed the handle, an
unbounded and non-deterministic window.

Every post-acquisition operation, including the callback, now runs
*inside* the `try`/`finally` that disposes the lock stream — acquisition
and the guarded region are adjacent with nothing but the callback
invocation between them. A callback (or any post-acquisition step) that
throws still releases the lock immediately as the exception unwinds,
before any other invocation could observe it as unavailable for longer
than necessary. A new deterministic test seeds a throwing callback,
confirms the first call propagates the exception with the queue/runs
state left byte-unchanged, and then confirms a second, independent call
acquires the same lock immediately and completes normally.

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

### Safety-net repositioning: design-thread watchdog recommended, external OS scheduler retired (G539)

G526's external cron/launchd heartbeat recommendation failed silently on
**every run for five continuous days** (2026-07-15..07-20) — the wrapper's
`gh`/agmsg auth lives in a login keychain a cron job cannot reach, so it
never got past the credential step. A 105-minute stall on 2026-07-20 (G538 /
PR #1179) went unrecovered even though `automation stalled-work` correctly
detected it (`pr-created-not-reviewing, age=105m`); only a human ping
surfaced it. `intent-cli guide orchestrator-thread` is repositioned
accordingly:

- **Design-thread watchdog (recommended default)** — a watchdog loop run
  from the **design** thread at a **30-minute-class** interval: it calls
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo>
  --format json` and, when `stale=true`, sends AT MOST ONE canonical nudge
  to the orchestrator using the returned `message_body` — completely silent
  otherwise. It runs INSIDE a live, human-monitored agent session rather
  than an invisible external process, needs no separate credential/keychain
  setup, and is visible on the operator's screen the moment it breaks. The
  pre-existing watchdog safety rules (never duplicate a delegation, never
  clear a permission prompt, never cancel in-flight work, never force-close,
  never hand-edit durable state) and stop condition are preserved verbatim.
- **Failure visibility is not the same as staleness.** Silence is reserved
  for a healthy `stale=false` heartbeat result ONLY. A heartbeat command
  execution failure or malformed/non-object output must be surfaced VISIBLY
  in the watchdog's own turn output this wake — never silently swallowed or
  silently retried, since silent failure is exactly the defect this slice
  retires the external OS scheduler for — while still never fabricating or
  sending an agmsg nudge from broken input; only a genuine `stale=true`
  result ever produces a sent message.
- **Orchestrator-side long-interval automation (selectable alternative)** —
  the same `automation heartbeat` call, run directly from a long-interval
  automation (Codex automation or Claude same-thread `/loop`) IN THE
  ORCHESTRATOR'S OWN THREAD at a 30-60 minute-class interval, instead of the
  design thread. Trade-off: design-side (recommended) keeps the orchestrator
  strictly loopless at the cost of one extra hop (design watchdog to
  orchestrator); orchestrator-side removes that hop but requires the
  orchestrator itself to run a recurring loop — exactly what
  orchestrator-message mode is designed to avoid in steady state.
- **External OS-scheduler heartbeat is RETIRED.** The cron/launchd
  recommendation is retired outright (not merely demoted): credential-store
  access, invisible failure, and running entirely outside the agmsg model
  are all disqualifying. `intent-cli automation heartbeat` /
  `automation stalled-work` themselves are UNCHANGED and remain
  scheduler-agnostic — any scheduler, including cron, can still call them —
  the guide simply no longer recommends an external OS scheduler as the
  mechanism. The 5-minute in-session orchestrator fallback timer (legacy,
  discouraged) is unchanged in meaning.

Full detail: [Agent-message orchestration](12-agent-message-orchestration.md).

---

### Runs-log schema audit and repair; domain-scoped publish-flow validation (G542)

Field incident, 2026-07-20: publishing G539 (domain `intent-cli`) was
refused **twice** at durable-state analysis by legacy `runs.jsonl` rows
belonging to the **sekiban-as-a-service** domain — first one row missing
`ts`/`by`, then 16 rows missing `execution_unit`. The pre-G542 validator
(`RunLogSerializer.DeserializeAll`) parses the whole file in one call and
throws on the FIRST malformed row anywhere in it, regardless of domain — so
each repair merely revealed the next offender, one at a time, and no
canonical bulk-audit/repair surface existed.

**`intent-cli automation runs-audit [--repo <r>] [--domain <d>] [--write]
[--apply-inferred] --format json|markdown`** is a read-only-by-default
surface that reports **every** malformed row in **one pass** — line number,
missing required field(s) (`ts`, `event`, `execution_unit`, `by`), an
inferred owning domain, and — when one exists — a repair derived from
**within the record itself**:

- **`ts`** ← the record's own `timestamp` field.
- **`execution_unit`** ← `wip[0].eu` for a `skip-next-slice-due-to-wip` row,
  or `stage1.eu` for a `pr-merged-closeout` row. In the real legacy rows the
  branch discriminator lives one level down: every such row's own `event` is
  the literal string `wake-summary`, and the branch is selected by that
  record's own `status` field instead. A row whose `event` is directly
  `skip-next-slice-due-to-wip` or `pr-merged-closeout` (no
  `wake-summary`/`status` wrapper) is still matched the same way — direct-
  event compatibility is intentionally retained alongside the
  `wake-summary`/`status` shape, not replaced by it.

These are the only two documented **within-record** derivations (design
ruling, 2026-07-20) — the value already exists inside the record under a
different key, so copying it into the canonical key is lossless
normalization, not a guess. A field with **no** within-record source (most
commonly `by`) is always reported `non_derivable`; the report may still
carry an `inferred_suggestion` (the majority `by` value among valid peer
rows of the same `event`, with its evidence, e.g. "all 12 peer record(s) of
event 'issue-created' use by=issue-publish-flow"), but that is evidence, not
the record — `runs.jsonl` is an audit trail, and writing "this record was
authored by X" when the record does not say so records a fact that isn't
there.

- **`--write`** applies ONLY within-record repairs, appends one
  `runs-repair` audit event per repair (naming the line, the repaired
  field(s), the derivation class, and the source), and preserves every
  other byte of the file — and every other byte of the repaired line itself
  — untouched (the missing key/value pair is inserted immediately after the
  line's opening `{`, nothing else moves). A row whose `execution_unit`
  itself is missing with no within-record source is refused entirely under
  `--write` (even for its OTHER derivable fields) — there is no safe unit to
  attribute a `runs-repair` audit event to, and fabricating "unknown" would
  itself be a guess on a durable trail.
- **`--apply-inferred`** (separate, explicit, **never** implied by
  `--write` alone; refused with a usage error if passed without `--write`)
  additionally applies peer-convention `inferred_suggestion` values, in a
  **separate** `runs-repair` event recording `derivation:
  inferred-peer-convention` — the two derivation classes are never mixed
  into the same audit event, even when they repair the same line.
- Unparseable lines (not valid JSON, or valid JSON that isn't an object) are
  reported with every required field listed as missing and are never
  repaired by either mode.
- Clean report + exit `0` when nothing is malformed.

**`issue publish-flow` durable-state analysis is now domain-scoped.** The
shared `PublishDurableArtifactAnalyzer` (G536) now parses `runs.jsonl`
**line by line** instead of one whole-file `DeserializeAll` call, and
resolves each malformed row's owning domain the same way `runs-audit` does
(that unit's own `packet.yaml` `domain:` field when it still exists on
disk; otherwise a unique match against exactly one candidate domain's
`execution_unit_regex` from `intents/<domain>/automation/bindings.md`;
otherwise a domain-like prefix in the row's `by` field, corroborated
against a real domain directory). A malformed row that resolves to a
domain **other than** the one being published becomes a **warning** naming
`runs-audit` (surfaced in the result's `warnings`) instead of a hard block
— publishing proceeds. A malformed row that resolves to the **same**
domain, or whose owning domain cannot be resolved at all (never assumed to
belong to someone else), still **fails closed** exactly as before — this
narrows the blast radius of a legacy row, it does not weaken validation of
the domain actually being published. `automation publish-recovery` was
deliberately left on the legacy whole-file behavior (out of scope for this
slice); `RunLogSerializer` / the RunEvent required-field contract are
unchanged.

### Queue-state writes are guarded: no-item-loss invariant and stale-base re-application (G548)

`queue-state.json` is **one file shared by every domain** on a multi-domain
host, written concurrently by several loops from different checkouts. Every
canonical writer deserializes the whole file, mutates in memory, and
reserializes the whole file — so a read-modify-write race does not merely
conflict, it **silently erases** whatever the stale in-memory copy happened
not to contain.

**Field incident, 2026-07-23** (host commit `2ab082cf`): a sekiban-domain
write recorded a G841 PR linkage from a base read an hour earlier and dropped
the intent-cli G545 queue item seeded in between. Nothing errored; the commit
message claimed only the linkage change. The loss stayed invisible for four
days, then surfaced as `closeout-plan host-metadata-blocked` and combined
with the `pr-is-draft` recovery gate into a circular deadlock. Restoration
took three canonical surfaces plus an operator (host commit `c0897649`).

Every canonical mutation now writes through one shared guard,
`QueueStatePersistence`. It lives in **`IntentSystem.Supervisor`** — the
assembly that owns the queue-state model and serializer, and the only one
*both* `IntentSystem.Cli` and `IntentSystem.Drift` reference — so "every
canonical writer" means every writer in the solution, including the drift
service's corrective enqueue. It enforces three things:

1. **Stale-base detection and re-application.** The state the caller *read*
   is compared against what is on disk *now*, at persist time (through the
   same serializer round-trip, so pure formatting drift is never mistaken for
   a concurrent write). On a mismatch the caller's mutation — derived
   automatically as an item-level delta between its base and its outgoing
   state — is re-applied to the **fresh** state instead of persisting the
   stale copy, and the re-application is reported back so it is never
   invisible.
2. **No-item-loss invariant.** Any execution unit present on disk but missing
   from the outgoing state, and not named as an expected removal, **aborts
   the write** — naming the exact units and the canonical recovery path
   (`queue-seed-from-packet` → idempotent `issue publish-flow` rerun →
   `closeout-plan --write-recovered-linkage`). The file is left untouched.
   An on-disk state that cannot be *read* also aborts, since neither
   guarantee can be established against it.
3. **Item-scoped re-application.** A re-applied mutation touches only the
   units its delta actually covers, plus `updated_at`. Every unrelated item
   is carried through byte-identically from the fresh state, in the fresh
   state's order — so a stale copy's older view of another item can never
   overwrite a newer one.

**Explicit removals stay legitimate.** Retire (G525), the completed-item
lifecycle, and any operation whose contract names the item it may remove pass
those units as expected removals. The invariant targets **unrequested** loss
only; an allow-list entry excuses that unit and nothing else. Retire itself
needs no entry — it rewrites the item as `state=retired` rather than removing
it.

**Re-application is reported, never silent.** A canonical command whose
write was re-applied says so in its own output, naming the execution units it
was re-applied for — a writer that quietly repaired itself against a
concurrent write teaches an operator nothing about the contention that caused
it. `queue transition`, for example, prints `note: queue-state changed after
it was read (a concurrent canonical write); this transition was re-applied to
the current state for <units> and no other item was modified.`

**The one raw-text writer.** `metadata update` is the bounded controlled
metadata writer: it mutates queue-state as raw JSON so it never rewrites a
field it does not own, and it accepts documents that need not satisfy the
full `QueueItem` contract. It uses `PersistRawJson`, which checks the
invariant on `items[].execution_unit` read straight out of the JSON — never
by deserializing — and on a clean base writes the caller's own text verbatim.
When a concurrent write *is* detected, the re-application happens at the
**JSON level** too — items this writer did not touch are carried across as
the fresh document's own nodes, so fields the model does not know about
survive on both sides and a stale copy's older view of another item can never
overwrite a newer one.

Because `metadata update` is the bounded **linkage** writer — the role writer
B played in the 2ab082cf incident — a re-application on this path is reported
in its own result: `queue_state_reapplied` / `queue_state_reapplied_execution_units`
in JSON, and a `queue_state_reapplied:` block in the text output.

**Multi-writer expectations for shared hosts.** Concurrent canonical writers
are supported and expected: a losing writer is repaired (re-applied), not
rejected, so no loop has to serialize against another. What is *not*
supported is a writer that bypasses the guard — hand-editing
`queue-state.json`, or a new command calling `File.WriteAllText` directly — a
source-level fixture (`QueueStateWriterCoverageTests`) fails with the file and
line of any writer added anywhere in `src/` that bypasses the guard, so the
all-writers claim cannot regress by hand-verification again.
Deliberately out of scope, and unchanged by this slice: a per-domain queue
file split (a future design decision; a recurrence after this lands is the
escalation criterion), file-locking daemons, cross-process mutexes, and
git-level merge strategy — the 2ab082cf loss happened inside a
fast-forward-clean history, so the defense has to sit at the writer, before
anything reaches a commit.

---

### Cross-platform agent skill: one embedded source, `intent-cli skill` (G559)

Claude Code, Codex, and Copilot all read the **same** `SKILL.md` format. Only
the **location** differs:

| Target | Scope(s) | Path |
| --- | --- | --- |
| `claude` | `repo` (default), `user` | `<repo>/.claude/skills/<name>/SKILL.md`, `~/.claude/skills/<name>/SKILL.md` |
| `codex` | `user` | `~/.codex/skills/<name>/SKILL.md` |
| `copilot` | `repo` | `<repo>/.github/skills/<name>/SKILL.md` |

Because only the location differs, hand-copying is what people actually do —
and hand-copied skills drift. The evidence was in this project's own host: the
`host-review-loop` skill existed as separate copies under `~/.claude/skills`
and `~/.codex/skills`, and the copies had already diverged. Two files claiming
to be the same skill, neither one authoritative, is a worse failure than no
skill at all: an agent follows the stale one and reports a workflow the tool no
longer runs.

So the skill ships as **one source**: `skills/<name>/SKILL.md` in the
repository, embedded into the tool package at build time. There is exactly one
file to edit, it is versioned with the code it describes, and it travels with
the released package.

```bash
intent-cli skill list                              # every target/scope, and its state
intent-cli skill install --target all              # install into every platform's own location
intent-cli skill install --target claude --scope user
intent-cli skill diff --target claude              # what an edited copy changed
```

`list` and `diff` accept `--format text|json`; `install` accepts
`--target claude|codex|copilot|all`, `--scope user|repo`, `--skill <name>`,
`--force`, and `--format`.

Four properties are the contract:

1. **A scope the platform does not define is refused, not written.** `--scope
   repo` for `codex` fails with the supported scopes named. Writing to a
   plausible-looking directory the platform never reads would look like a
   successful install and behave like no install at all.
2. **An edited copy is never replaced silently.** Install compares the
   installed file against the embedded source; on a difference it reports
   `refused-drifted`, leaves the file byte-identical, and **exits non-zero** so
   a script notices. `--force` is the explicit opt-in to replace it. Line-ending
   differences are not drift, so a Windows checkout does not report every
   install as edited.
3. **The whole plan is resolved and inspected before the first write.**
   Install runs in two phases: it validates every target/scope pair, resolves
   every destination path, and inspects every destination's state — and only
   then writes. An unsupported target/scope pair *or* a drifted destination
   **anywhere** in the plan aborts the entire run with nothing created and
   nothing changed; the writable destinations are reported
   `skipped-plan-aborted` so it is clear they were part of the plan and were
   deliberately not written. Inspecting and writing in one pass is not
   "validated before any write" — under `--target all` an earlier missing
   target would already be on disk by the time a later drifted one was found,
   leaving a partial install behind an exit code that claims nothing happened.
   `--force` is what makes that same plan succeed end to end.
4. **Nothing is written that is already current.** A matching copy reports
   `already-current` and the file is not touched.

**The skill itself is a dispatcher, not a manual.** It restates none of the
workflow. It carries the rule *"installed guide output wins"* and a table
mapping what the user wants to the `intent-cli guide ...` command that answers
it. That is deliberate: a skill file that copies out the workflow is a second
source of truth that ages against the tool, which is the drift problem one
level up. The guide surfaces move with the CLI; a pointer to them does not go
stale.

`SkillCommandTests` proves the behaviour against real writes into a throwaway
repo root and a throwaway user home — including that a refused install leaves
the operator's edited file untouched, and that the embedded resource is
byte-identical to `skills/intent-cli/SKILL.md` (a build that failed to package
the asset fails the test rather than shipping an installer that writes
nothing).

---

### Publish-priority ordering as a lifecycle (G561)

When a slice must jump the queue, the sanctioned way to express that is **not**
hand-picking the next unit and **not** retiring the one that would have gone
first. It is a lifecycle with three states, and each one has a canonical
command:

1. **Block the unpublished unit** — the unit that should wait is set to
   `state=blocked` with the reason recorded in `blocked_by`. The reason is
   normally the execution unit it is waiting on.
2. **The selector skips it while blocked.** `intent next-slice` excludes any
   item in a blocked state *and* any item with a non-empty `blocked_by`. Both
   conditions matter: an item that reports itself unblocked while still
   carrying a stale reason is, in effect, still blocked, and will never be
   picked.
3. **Clear it once the priority reason is gone** — the exit that makes the unit
   selectable again.

Step 3 is where the pattern used to break. `automation issue-block --clear`
requires a **complete** `linked_issue` before touching anything, and rightly so:
it also converges the GitHub `intent-issue-blocked` label, and issue #818 exists
in almost every repository. But a unit blocked *before publish* has no issue at
all — `linked_issue` is null — so that path could not run. A bare
`queue transition` would move the state and strand `blocked_by` populated,
producing exactly the half-converged unit step 2 excludes. There was no
canonical exit, and the design thread had to issue a one-off ruling to get a
single unit moving (field incident, 2026-07-31, G559 wake).

The pre-publish exit closes that:

```bash
intent-cli automation issue-block <execution-unit> --clear --pre-publish --write
```

It converges the queue side **only** — `state=queued` and `blocked_by` emptied
in one guarded write, with the run-log event naming the wait reason it cleared —
and performs no GitHub interaction at all, because there is no issue to
interact with. It does not read labels, and it does not construct a mutator.

It fails closed in two ways, both deliberately:

- **The unit has a `linked_issue` at all.** The rule is **absolute absence**:
  only `linked_issue: null` is a pre-publish unit. A published unit is owned by
  the two-sided path, which also converges the label; taking the queue-only
  shortcut for it would leave the label behind — the precise drift the
  two-sided command exists to prevent. A *partial* linkage is refused for the
  same reason, and so is an **empty object** `{repo: "", number: null}`: the
  object's presence is evidence that something recorded a linkage, and "the
  fields happen to be blank" is not the same claim as "this unit was never
  published". An empty object is refused by the two-sided path too (it demands
  a complete linkage), so such an item has no exit until the linkage is
  repaired — deliberately. Malformed linkage is a data defect to fix, not a
  state to route around, and the error message says which repair to make.
- **`--repo` / `--issue` were supplied.** They are refused rather than ignored,
  because a caller who passes identifiers expects them to be acted on, and this
  path touches no GitHub side. Silently accepting them would let the caller
  believe a GitHub side was converged when none was.

`--pre-publish` is an *exit* only: it requires `--clear`. Blocking a unit before
publish already worked; what was missing was the way back.

**The next use of this pattern needs no design ruling.** Block, let the selector
skip, clear pre-publish, publish — all four steps are canonical commands.

### `clarify open` works on scaffolded packets (G561)

Recording a blocking design question is most valuable **early** — while the
packet is still a draft and the wrong answer has not yet been built. Until G561
that was exactly when it was impossible: `clarify open` deserialized `packet.yaml`
through the full projection contract, which requires a `review_context_packet`
section and twenty populated `implementation_issue_packet` fields. A packet from
`intent-cli packet draft` has neither — it carries
`implementation_issue_packet` / `intent_placement` / `knowledge_updates` /
`closeout_learning`, and review context lives in `review-context.md` rather than
in the packet. Every freshly scaffolded packet was rejected before any mutation,
so the G552 design-decision flow was structurally unavailable at the moment it
exists for.

`clarify open` now reads only the facts a clarification record actually
contains, and the strictness is asymmetric on purpose:

- the packet's `source_execution_unit` is **required** and must match the queue
  item — a clarification filed against the wrong unit is worse than none, so
  identity never degrades;
- every other packet field is optional, because a scaffold has not filled it in
  yet and an unfilled TODO is not a reason to refuse to record a blocking
  question. Derived question/reason text degrades field by field and makes the
  gap explicit rather than asserting detail the packet does not contain;
- routing is decided by the **declaration**, not by what the declaration turns
  out to contain. A packet that declares a `review_context_packet` section is
  claiming to be a complete projection packet, so it is deserialized by the
  **unchanged** `ProjectionPacketSerializer` — same required fields, same type
  checks, same validation order and messages, same failures — and every
  previous cross-check still runs on top. A declared-but-broken packet
  (missing a required field, a wrong-typed field, or a section declared with a
  scalar body) fails exactly as loudly as it always did, before any mutation.
  Tolerance is never applied to a packet that says it is complete;
- only a packet with **no such declaration** — the `packet draft` scaffold,
  which never claimed completeness — takes the tolerant path;
- `review-context.md` is read by the same canonical parser, so its
  execution-unit rules are unchanged (a present-but-malformed `# Execution Unit`
  section still fails). The one accommodation is the `# Deterministic Review
  Checks` section the scaffold does not yet have, whose absence costs only the
  derived question text — and `--question` overrides that anyway.

The strict projection serializer is **unchanged**. Publish-flow and review
legitimately require a complete contract; loosening it there would let an
incomplete packet through publication. The tolerance is scoped to `clarify open`.

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
  "stableVersion": "<stableVersion>",
  "nextVersion": "<nextVersion>"
}
```

The shape is written with placeholders on purpose: **read the actual values from
`eng/version.json`**, and see [Next release readiness](#next-release-readiness)
for the line currently being cut. A worked example here would be a second copy
of the version pair that goes stale on the next roll — the defect G557/G560
exist to remove.

| Stage | Version form | How it is derived |
| --- | --- | --- |
| Local pack / install | `<nextVersion>-<sha>-<G-unit>` | `nextVersion` from `eng/version.json` (G468) |
| Main CI preview | `<nextVersion>-preview.<run>.<attempt>` | `nextVersion` from `eng/version.json` |
| Release candidate (optional) | `<nextVersion>-rc.N` | Publishing the GitHub Release for tag `v<nextVersion>-rc.N` triggers `release.yml` (`on: release: published`); the tag supplies the version |
| Stable release | `<nextVersion>` | Publishing the GitHub Release for tag `v<nextVersion>` triggers `release.yml` (`on: release: published`); the tag supplies the version (`-p:Version=<tag>` wins) |
| Post-release main builds | `<nextPatch>-preview.<run>.<attempt>` | After rolling `nextVersion` to `<nextPatch>` |

### Post-release version roll (G554) — required, immediate

**The moment a GitHub Release is published and verified, roll `eng/version.json`
in a follow-up commit**: `stableVersion` = the version just released,
`nextVersion` = the next patch. This is not an optional tidy-up; it is a step of
release closeout, and skipping it breaks the preview channel.

```json
{
  "stableVersion": "<the version just released>",
  "nextVersion": "<the next patch>"
}
```

**Why it is required.** `nextVersion` is what preview and local-pack builds are
derived from. If it still names the version that was just released, every
subsequent preview builds as `<released>-preview.N` — and a prerelease sorts
**below** its own release version in SemVer. Field incident, 2026-07-29: after
`v0.6.0` was published the roll was skipped, so previews kept building as
`0.6.0-preview.N`; `dotnet tool update` refused the newer build as older, and a
manual uninstall/install was the only way forward. Rolling immediately makes the
next preview `0.6.2-preview.N`, which sorts above `0.6.1`, and `dotnet tool
update` works again.

**Release closeout checklist** (the roll is step 4 — do not stop at step 3, and
the roll is not done until step 6):

1. Publish the GitHub Release for the version (this fires `release.yml`).
2. Verify the published artifacts: NuGet page, release assets, `.sha256`
   checksums, and `intent-cli --version` after `dotnet tool update`.
3. Notify the operator and any waiting downstream consumers.
4. **Roll `eng/version.json` in a follow-up commit** — `stableVersion` = the
   released version, `nextVersion` = the next patch — **and, in the same
   commit, add DRAFT `docs/{en,ja}/release-notes-v<nextVersion>.md` stubs.**
   The G475 guard requires notes to exist for whatever `nextVersion` names, so
   a roll that moves the field without the stubs turns main red the moment it
   lands. The stubs carry no changelog content — the next release-prep packet
   authors that.
5. **Refresh the "Next release readiness" section to the new line** in the same
   roll — it names the release being cut, so a roll that moves `nextVersion`
   without it leaves the section describing the previous cycle. Update both
   language mirrors.
6. **Verify child main CI is green after pushing the roll.** The roll is
   complete only when CI is: a red main blocks every unrelated PR that inherits
   it, so the roller owns the result, not just the commit.

Existing preview artifacts are **not** renumbered retroactively; the rule fixes
the channel going forward.

> **Why steps 4-6 read this way (G557, G560).** The roll's first live execution
> (commit `00936844`, `nextVersion` 0.6.1 → 0.6.2) moved the field alone and
> turned main red on four checks: three tests pinned the version pair by value,
> and the G475 guard demanded `release-notes-v0.6.2.md`. An unrelated PR
> inherited the red main and was frozen until a hotfix landed. The assertions
> are now derived from `eng/version.json` so a correct roll cannot break them,
> and the two steps above close the rest: create the stubs with the roll, and
> confirm green before calling it done.
>
> **Second incident (G560), roll 0.6.2 → 0.6.3.** The amended rule worked — the
> post-roll CI check caught that the readiness section still described the
> previous line, which had been passing only incidentally because the new
> version happened to appear in unrelated preview examples. Refreshing it then
> flipped four transitional test theories that pinned the *previous* cycle's
> readiness heading by literal. Hence step 5 above, and the rule below.

**Release-prep guidance: never add a current-state version literal.** When a
release-prep packet writes or updates a guard over the developer reference, the
README, or any other file describing the repository *as it is now*, the expected
version comes from `eng/version.json` — never from a typed-in string. Two live
incidents came from breaking this rule, and both cost an unrelated PR its CI:
the first pinned the version pair itself (G557), the second pinned the readiness
section's heading (G560). A literal is only safe where the artifact is **frozen**:
`release-notes-v<X>.md` for an already-released `X` will never change, so
asserting its content is stable by construction — as are incident records like
the paragraphs above, which describe what happened rather than what is.

For the same reason the version-flow example above uses placeholders rather than
a worked version pair: a second copy of the current versions is a second thing
to keep in sync, and it goes stale on exactly the roll nobody is watching.

### Next release readiness (v0.7.2)

**`v0.7.1` shipped** (GitHub Release + NuGet) and the version policy was rolled
to the `0.7.2` development line. The v0.7.1 batch was a **patch** bump covering G565 (unified packet YAML
parsing for projection and clarify), G566 (roll-simulation fix, test-only),
G567 (queue-seed through the unified parser with malformed YAML failing
closed), G568 (dependency-fidelity queue seeding with the canonical
diagnose/repair utility), and G569 (test-determinism: clock seam and the
mutable-static audit). Minor stays reserved for new command surfaces and
broad behavior changes; the G568 repair utility completes a bugfix and does
not trigger that reservation.

The repository is now on the in-development **`0.7.2`** `nextVersion`. What
ships in `v0.7.2` is not decided here: the next release-prep packet selects the
merged slices, authors the real
[release-notes-v0.7.2.md](release-notes-v0.7.2.md) content over the DRAFT
stubs, and states the bump rationale. Until then the notes remain stubs and no
`v0.7.2` GitHub Release may be published.

**Release-readiness verification (run before merging the `v0.7.2` version
bump):**

```bash
# 1. Confirm the version policy records the release-to-be-cut.
cat eng/version.json   # stableVersion 0.7.1 (published), nextVersion 0.7.2 (to release)

# 2. Build and confirm the display version identity (version + git SHA + G-unit).
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   expected shape: intent-cli 0.7.2-<sha>-G56x   (NOT a stale literal)

# 3. Pack and confirm the NuGet package version matches the policy.
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.7.2.nupkg

# 4. Confirm package metadata (id / command / license / project URL).
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

After the version-bump merge lands on `main`, a maintainer/operator (or external
release automation) creates and publishes the GitHub Release for `v0.7.2`;
publishing it triggers `release.yml` (`on: release: published`) to build and
publish the NuGet package and the per-platform binary artifacts. **Then roll
`eng/version.json` immediately** — `stableVersion → 0.7.2`, `nextVersion →
0.7.3` — carrying, per **steps 4–6** of the
[post-release version roll](#post-release-version-roll-g554--required-immediate):
the **DRAFT note stubs in the same commit** (step 4), the **"Next release
readiness" section refreshed to the new line in both language mirrors** (step
5), and a **post-roll green child-main CI check** before the roll counts as
complete (step 6).

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
