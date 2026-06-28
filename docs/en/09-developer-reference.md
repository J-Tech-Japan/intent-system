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

The supported publish path for a packet is **`automation queue-seed-from-packet`
→ `issue publish-flow` → `automation issue-publish`**, with no manual
queue-state edits or raw `gh issue create`. The domain's `execution_unit_regex`
(declared in `intents/<domain>/automation/bindings.md`, e.g. `^E\d{3,}$`) is
resolved from one shared source, so `automation summary --domain <d>` and
`queue-seed-from-packet --execution-unit <unit>` always agree on which units are
valid. A unit that does not match the active domain's regex is refused with a
precise diagnostic that names the consulted bindings source.

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
  "stableVersion": "0.3.11",
  "nextVersion": "0.3.12"
}
```

| Stage | Version form | How it is derived |
| --- | --- | --- |
| Local pack / install | `0.3.12-<sha>-<G-unit>` | `nextVersion` from `eng/version.json` (G468) |
| Main CI preview | `0.3.12-preview.<run>.<attempt>` | `nextVersion` from `eng/version.json` |
| Release candidate (optional) | `0.3.12-rc.N` | Tag `v0.3.12-rc.N` triggers release workflow |
| Stable release | `0.3.12` | Tag `v0.3.12` triggers release workflow (`-p:Version=<tag>` wins) |
| Post-release main builds | `0.3.13-preview.<run>.<attempt>` | After bumping `nextVersion` to `0.3.13` |

**After releasing `v0.3.12`**, bump both fields in `eng/version.json`:

```json
{
  "stableVersion": "0.3.12",
  "nextVersion": "0.3.13"
}
```

This ensures the next main-branch CI build (and local pack) immediately produces
`0.3.13-preview.<run>.<attempt>` / `0.3.13-<sha>-<G-unit>` rather than continuing to
emit `0.3.12` (which would collide with the stable release version).

### Next release readiness (v0.3.12)

**`v0.3.11` shipped** (GitHub Release + NuGet) and the version policy was bumped
to the `0.3.12` development line. The repository is now on the in-development
**`0.3.12`** `nextVersion`; G504 (this packet) prepares the `v0.3.12` patch
release — the next release is published by tagging `v0.3.12` once the
[release-readiness gate](release-notes-v0.3.12.md#release-readiness-gate-g504)
passes. Preparing the release does not cut it. Full changelog and operator
checklist: [release-notes-v0.3.12.md](release-notes-v0.3.12.md).

**To ship in `v0.3.12` (changes since `v0.3.11`) — orchestrator-mode preview
patch fixes:**

- **agmsg receiver startup ordering** (G502) — the orchestrator setup guidance
  now requires a strict startup order and a ping/ack handshake before real
  delegation, so work is not sent before receiver sessions are launched/
  restarted, the monitor/bridge is attached, and the ack succeeds. Includes a
  copy-paste recovery message for receivers launched after the initial sends.
- **approved PR label cleanup** (G503) — the `approved` PR transition now removes
  a stale `intent-pr-rereview-ready` (and other in-flight review labels), and
  `automation reconcile` repairs a PR that carries both, so an approved PR no
  longer visibly shows `intent-pr-approved` and `intent-pr-rereview-ready`
  together.
- Orchestrator mode remains **preview/experimental**: opt-in, still being
  hardened, with the timer-loop mode fully supported and unchanged. See
  [Agent-message orchestration](12-agent-message-orchestration.md).

**Release-readiness verification (run before tagging the next `v0.3.12`):**

```bash
# 1. Confirm the version policy records the release-to-be-cut.
cat eng/version.json   # stableVersion 0.3.11 (published), nextVersion 0.3.12 (to release)

# 2. Build and confirm the display version identity (version + git SHA + G-unit).
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   expected shape: intent-cli 0.3.12-<sha>-G50x   (NOT a stale literal)

# 3. Pack and confirm the NuGet package version matches the policy.
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.3.12.nupkg

# 4. Confirm package metadata (id / command / license / project URL).
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

The official release is then cut by publishing a GitHub Release tagged `v0.3.12`;
the release workflow passes `-p:Version=0.3.12` (which wins over the local
default). After the release publishes, apply the post-release `eng/version.json`
bump above (`stableVersion → 0.3.12`, `nextVersion → 0.3.13`).

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
