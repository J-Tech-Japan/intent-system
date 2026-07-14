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
  "stableVersion": "0.3.14",
  "nextVersion": "0.3.15"
}
```

| Stage | Version form | How it is derived |
| --- | --- | --- |
| Local pack / install | `0.3.15-<sha>-<G-unit>` | `nextVersion` from `eng/version.json` (G468) |
| Main CI preview | `0.3.15-preview.<run>.<attempt>` | `nextVersion` from `eng/version.json` |
| Release candidate (optional) | `0.3.15-rc.N` | Publishing the GitHub Release for tag `v0.3.15-rc.N` triggers `release.yml` (`on: release: published`); the tag supplies the version |
| Stable release | `0.3.15` | Publishing the GitHub Release for tag `v0.3.15` triggers `release.yml` (`on: release: published`); the tag supplies the version (`-p:Version=<tag>` wins) |
| Post-release main builds | `0.3.16-preview.<run>.<attempt>` | After bumping `nextVersion` to `0.3.16` |

**After releasing `v0.3.15`**, bump both fields in `eng/version.json`:

```json
{
  "stableVersion": "0.3.15",
  "nextVersion": "0.3.16"
}
```

This ensures the next main-branch CI build (and local pack) immediately produces
`0.3.16-preview.<run>.<attempt>` / `0.3.16-<sha>-<G-unit>` rather than continuing to
emit `0.3.15` (which would collide with the stable release version).

### Next release readiness (v0.3.15)

**`v0.3.14` shipped** (GitHub Release + NuGet) and the version policy was bumped
to the `0.3.15` development line. The repository is now on the in-development
**`0.3.15`** `nextVersion`; G519 is **prepare-only** — it bumps the version
metadata and docs and adds no publish steps. The version-bump merge does **not**
create a GitHub Release or tag. After it merges and the
[release-readiness gate](release-notes-v0.3.15.md#release-readiness-gate-g519)
holds, a **maintainer/operator (or external release automation) creates and
publishes the GitHub Release** for `v0.3.15`; publishing that Release fires
`.github/workflows/release.yml` (`on: release: published`), which builds and
publishes the NuGet package and the per-platform binary artifacts. Full
changelog and operator checklist:
[release-notes-v0.3.15.md](release-notes-v0.3.15.md).

**To ship in `v0.3.15` (changes since `v0.3.14`) — orchestrator/agmsg
operational fixes:**

- **Claude project-settings diagnosis for a missing agmsg Monitor** (G517) —
  when `ToolSearch select:Monitor` finds no Claude Code `Monitor` tool at all
  (a tool-surface problem, not the `1 shell` vs `1 monitor` delivery-mode
  confusion), the guide adds a known-good comparison checklist, names suspect
  project-level `env` overrides, and documents safe operator remediation.
- **orchestrator-mode timers shift to a design-side watchdog** (G518) — the
  normal steady state is now message-driven (implementation/review replies
  wake the orchestrator), with an explicit orchestrator timer supported only
  as a fallback/legacy polling option, and a new optional, low-frequency
  design-side watchdog as the recommended safety net.
- Orchestrator mode remains **preview/experimental**: opt-in, still being
  hardened, with the timer-loop mode fully supported and unchanged. See
  [Agent-message orchestration](12-agent-message-orchestration.md).

**Release-readiness verification (run before merging the `v0.3.15` version
bump):**

```bash
# 1. Confirm the version policy records the release-to-be-cut.
cat eng/version.json   # stableVersion 0.3.14 (published), nextVersion 0.3.15 (to release)

# 2. Build and confirm the display version identity (version + git SHA + G-unit).
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   expected shape: intent-cli 0.3.15-<sha>-G51x   (NOT a stale literal)

# 3. Pack and confirm the NuGet package version matches the policy.
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.3.15.nupkg

# 4. Confirm package metadata (id / command / license / project URL).
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

After the version-bump merge lands on `main`, a maintainer/operator (or external
release automation) creates and publishes the GitHub Release for `v0.3.15`;
publishing it triggers `release.yml` (`on: release: published`) to build and
publish the NuGet package and the per-platform binary artifacts. Once it has
published, apply the post-release `eng/version.json` bump above
(`stableVersion → 0.3.15`, `nextVersion → 0.3.16`).

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
