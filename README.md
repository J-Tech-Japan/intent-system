# intent-system / `intent-cli`

`intent-cli` is **deterministic support tooling** for running an intent-driven
development workflow on top of GitHub. It helps you organize intents, prepare
and publish Child Issue Contracts, drive implementation and review loops, and
recover when a loop looks wrong — all through explicit, inspectable commands.

> `intent-cli` never launches Claude, Codex, or any other AI provider. It emits
> guidance, validates contracts, and performs bounded GitHub/metadata
> transitions. The AI agent (you, or your coding assistant) stays in the driver's
> seat and **asks `intent-cli` what to do next**.

- Package id / command: `intent-cli`
- License: [Apache-2.0](#license)
- Repository: <https://github.com/J-Tech-Japan/intent-system>

---

## Quickstart (public / OSS)

New to `intent-cli`? Follow this path top to bottom. Every step has a
`intent-cli ...` command that tells you what to do next, so you rarely need to
memorize anything.

1. [Install](#1-install)
2. [Verify](#2-verify)
3. [Ask intent-cli first](#3-ask-intent-cli-first) ← the core habit
4. [Start a project](#4-start-a-project)
5. [Organize intents](#5-organize-intents)
6. [Create packets / publish GitHub issues](#6-create-packets--publish-github-issues)
7. [Set up implementation & review loops](#7-set-up-implementation--review-loops)
8. [Recover when a loop looks wrong](#8-recover-when-a-loop-looks-wrong)

### 1. Install

The basic path is the .NET global tool from NuGet.org. You need a **.NET 10
SDK** (`dotnet --version` should report `10.x`).

**macOS, Windows, and Linux (same commands):**

```bash
# Install
dotnet tool install -g intent-cli

# Later, upgrade in place
dotnet tool update -g intent-cli
```

If the global tools directory is not yet on your `PATH`, the `dotnet tool
install` output prints the exact line to add (commonly `~/.dotnet/tools` on
macOS/Linux, `%USERPROFILE%\.dotnet\tools` on Windows).

> No .NET SDK? Use the self-contained binary instead — see
> [Install without a .NET SDK](#install-without-a-net-sdk). Need the internal
> testing channel? See [Preview install](#preview-install).

### 2. Verify

```bash
intent-cli --version
```

A released build prints just the version line. (OSS preview CI builds add a
`channel=preview built=… commit=…` trailer — see
[Preview install](#preview-install).)

### 3. Ask intent-cli first

**This is the operating rule of the whole system.** Before you edit metadata,
move a workflow label, hand-write a packet, or tweak an automation prompt — ask
`intent-cli` for the current guidance and let it own the transition. Guessing at
label/metadata behavior is the most common cause of broken automation; the CLI
exists so you never have to guess.

Copy-paste starting points:

```bash
# What can intent-cli do, and which command owns which transition?
intent-cli guide help
intent-cli guide commands list --format json

# The exact step-by-step prompt for a given loop/role:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --repo <owner>/<repo>

# The provider-neutral automation/label contract for your domain:
intent-cli automation summary --domain <domain> --format json

# Any command's current flags:
intent-cli <group> <command> --help
```

Rules of thumb the docs and guidance enforce:

- **Use `intent-cli` transition commands, not raw edits.** Do not directly edit
  queue-state, workflow labels, packet publish metadata, or other host artifacts
  when an `intent-cli automation` / `intent-cli worker` command owns that
  transition. Apply labels through those commands, never `gh ... edit
  --add-label`.
- **Ask, don't read-and-guess.** Prefer `intent-cli guide ...` over reading
  local rule files; the guidance reflects the installed CLI's current contract.
- **Never ask `intent-cli` to launch an AI provider**, and never call
  `intent-cli run` as a production orchestrator (it is smoke/replay/dogfooding
  tooling only — see [CLI command roles](#cli-command-roles)).

### 4. Start a project

Initialize a host domain and inspect its state (read-only without `--write`):

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work --format json   # what the work surfaces expect
```

### 5. Organize intents

Capture and compile durable intent before cutting work:

```bash
intent-cli interview next-question        # durable per-domain Q/A
intent-cli interview record-answer ...
intent-cli interview compile
intent-cli guide workflow                 # suggested end-to-end flow
```

### 6. Create packets / publish GitHub issues

Scaffold the canonical packet (read-only without `--write`) and publish a
reviewed Child Issue Contract. The publish boundary applies host labels through
`intent-cli` — you never hand-apply `intent-target`:

```bash
intent-cli packet ...                     # packet.yaml / implementation.md / review-context.md / github-body.md
intent-cli issue validate-body ...        # enforce the Standalone Child Issue Contract
intent-cli issue prepare ...
intent-cli issue publish-reviewed ...     # reviewed-issue publish boundary
```

### 7. Set up implementation & review loops

Two cooperating loops, each with a ready-made prompt from
`intent-cli guide oneshot`:

- **Child implementation loop** (`--kind child-implement-or-update`): selects one
  GitHub target via `intent-cli worker next-action`, claims it, implements the
  smallest change, opens a ready-for-review PR (with a mandatory
  `Closes #<issue>` reference), and records the outcome via
  `intent-cli worker result-summary` + `intent-cli worker complete`.
- **Host review / next-slice loop** (`--kind host-review-next-slice`): reviews PRs
  against the packet/intent contract, requests updates, approves/merges, and cuts
  the next slice.

Operator-dogfooding prompt templates that wire these loops entirely through the
deterministic worker/metadata commands live under
[`docs/automation-templates/`](./docs/automation-templates/README.md).

### 8. Recover when a loop looks wrong

Don't hand-fix state — ask the CLI to classify and (where safe) repair:

```bash
intent-cli worker issue-preflight       --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight  --repo <owner>/<repo> --pr <n>    --format json
intent-cli automation doctor --format json      # CLI freshness / host-state resolution
```

These read-only surfaces tell you whether a safe, in-scope repair is available
and which command owns it, instead of guessing.

---

## Host agents vs. child implementation agents

The workflow distinguishes two agent roles, and the docs/guidance keep them
separate on purpose:

| Agent | Source of truth | Owns |
| --- | --- | --- |
| **Host / review agent** | parent host `.intent-cli/` state + intent tree | publishing issues, applying `intent-target`, review/approve/merge, next-slice planning, label transitions via `intent-cli automation` |
| **Child implementation agent** | the **GitHub issue/PR + repo-local code** (NOT host metadata) | implementing the issue contract, opening/updating the PR, recording outcomes via `intent-cli worker` |

Child implementation agents operate GitHub-contract-only: they must not read or
mutate the parent host's queue-state, runs logs, packet directories, or intent
tree, and they treat the GitHub issue body as the standalone contract.

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

Each archive ships with a `.sha256` sidecar; verify it before use.

```bash
# 1. Verify the checksum (run from the folder containing both files).
shasum -a 256 -c intent-cli-<version>-osx-arm64.tar.gz.sha256

# 2. Extract and place the binary on your PATH.
tar -xzf intent-cli-<version>-osx-arm64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. Confirm.
intent-cli --version
```

On Windows, verify with `CertUtil -hashfile intent-cli-<version>-win-x64.zip SHA256`,
unzip, and place `intent-cli.exe` on your `PATH`.

Release binaries and OSS preview CI artifacts carry no build-time expiry.

---

## Documentation

- Bilingual onboarding docs (installation, project start, intent organization,
  packet/issue creation, implementation & review loop setup, recovery) —
  **English: [`docs/en/`](./docs/en/index.md)**, **日本語: [`docs/ja/`](./docs/ja/index.md)**.
- Local coding-automation prompt templates:
  [`docs/automation-templates/`](./docs/automation-templates/README.md).

---

## Packaged invocation (local smoke)

The CLI is packaged as a .NET tool (package id `intent-cli`, command
`intent-cli`). To smoke-test a locally built package:

```bash
export INTENT_CLI_LOCAL_VERSION="0.2.0-local.$(date -u +%Y%m%d%H%M%S)"
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj \
  -p:Version="$INTENT_CLI_LOCAL_VERSION" \
  -o .artifacts/packages
mkdir -p .artifacts/smoke-repo/.intent-cli
cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'
default_domain = "intent-cli"
artifact_root = ".intent-cli"
worktree_root = ".intent-cli/worktrees"
EOF
(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" intent-cli project status)
```

Equivalent `dnx` path:

```bash
(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" intent-cli project status)
```

Project-local best-practice and model-registry starter docs live under
`.intent/best-practices/` and `.intent/model-registry/` as bounded child-repo
knowledge-base inputs for `generate-from-current best-practice` — not a
replacement for parent intent refs or runtime command logic.

---

## CLI command roles

The accepted production automation boundary lives in the parent host-side
review/next-slice loop, which uses provider-neutral GitHub labels
(`intent-target`, `intent-pr-reviewing`, `intent-pr-request-update`, etc.),
durable parent state, and explicit handoff artifacts. The child CLI is a tasking
companion to that loop, not a replacement.

| Surface | Role |
|---------|------|
| `intent-cli guide …` | Ask-first guidance: command roles, oneshot prompts, review/worker/workflow help |
| `intent-cli status brief` / `context collect` | Compact / richer AI-thread inputs |
| `intent-cli clarify draft` / `clarify record` | Owner clarification flow |
| `intent-cli issue validate-body` | Standalone Child Issue Contract enforcement |
| `intent-cli issue prepare` / `issue publish-reviewed` | Reviewed issue body publish boundary (never applies `intent-target`) |
| `intent-cli worker next-action` / `claim` / `result-summary` / `complete` | Child implementation loop selector + bounded label transitions |
| `intent-cli automation summary` | Provider-neutral label-driven automation contract emitter |
| `intent-cli safety nested-provider-handoff` | Artifact-only nested-provider safety guard (never spawns providers) |
| `intent-cli run …` | **Integration smoke, deterministic replay, and local dogfooding only** — not the primary production orchestrator |

For ongoing production automation, drive work through the host-side
review/next-slice loop and the provider-neutral label set described by
`intent-cli automation summary`. For nested-provider handoff steps, use
`intent-cli safety nested-provider-handoff` to emit a deterministic artifact
instead of recursively launching providers from inside `run`.

---

## Preview install

> OSS preview channel. Public users should use the
> [Quickstart install](#1-install) (stable NuGet) or a
> [Release binary](#release-binary) above. This section is for users who want
> the latest merged changes before a stable release.

The `preview-pack` GitHub Actions workflow runs on every merge to `main` and
uploads a self-contained install bundle as a workflow artifact named
`intent-cli-preview-<version>`. The bundle contains:

| File | Purpose |
| --- | --- |
| `intent-cli.<version>.nupkg` | The NuGet package consumed by `dotnet tool install`. |
| `intent-cli.<version>.nupkg.sha256` | SHA-256 checksum sidecar; verify before installing. |
| `preview-metadata.json` | Machine-readable build provenance (channel, version, build timestamp, commit, CI run identifiers). |
| `INSTALL.md` | Per-build install / update / verify / uninstall guide with this build's exact version and commit pre-filled. |

The package version pattern is `<nextVersion>-preview.<run_number>.<run_attempt>`
(e.g. `0.3.0-preview.42.1`), where `nextVersion` comes from `eng/version.json`.
Every CI run produces a distinct version. No PAT, source checkout, or public
NuGet feed is required — only a compatible .NET SDK / runtime and the unzipped
bundle. **OSS preview packages carry no expiry; they remain runnable indefinitely.**

```bash
# 1. Download and unzip the workflow artifact, then cd into it.
cd ./intent-cli-preview-0.3.0-preview.42.1

# 2. Verify the checksum (macOS: shasum; Linux: sha256sum). Prints
#    `intent-cli.<version>.nupkg: OK` on success. Do not install if it fails.
shasum -a 256 -c intent-cli.*.nupkg.sha256

# 3. Install (or update) the .NET tool from this local folder:
dotnet tool install --global --add-source . \
  --version 0.3.0-preview.42.1 intent-cli
# Upgrade-in-place:
dotnet tool update --global --add-source . \
  --version 0.3.0-preview.42.1 intent-cli

# Uninstall:
dotnet tool uninstall --global intent-cli
```

The installed binary exposes the preview metadata via `intent-cli --version`:

```text
intent-cli 0.3.0-preview.42.1-<short-sha>-G<unit>
channel=preview built=<iso-utc> commit=<full-sha>
```

The `channel=preview` trailer confirms the embedded preview metadata loaded
successfully. Source builds (`dotnet pack` without the CI properties) produce no
trailer and remain unrestricted.

---

## Version flow

The repository version policy lives in `eng/version.json`:

```json
{
  "stableVersion": "0.2.0",
  "nextVersion": "0.3.0"
}
```

| Stage | Version form | How it is derived |
| --- | --- | --- |
| Main CI preview | `0.3.0-preview.<run>.<attempt>` | `nextVersion` from `eng/version.json` |
| Release candidate (optional) | `0.3.0-rc.N` | Tag `v0.3.0-rc.N` triggers release workflow |
| Stable release | `0.3.0` | Tag `v0.3.0` triggers release workflow |
| Post-release main builds | `0.4.0-preview.<run>.<attempt>` | After bumping `nextVersion` to `0.4.0` |

**After releasing `v0.3.0`**, bump both fields in `eng/version.json`:

```json
{
  "stableVersion": "0.3.0",
  "nextVersion": "0.4.0"
}
```

This ensures the next main-branch CI build immediately produces
`0.4.0-preview.<run>.<attempt>` rather than continuing to emit `0.3.0-preview`
(which would collide with the stable release version).

---

## License

This project is licensed under the Apache License, Version 2.0 — see the
[`LICENSE`](./LICENSE) file for the full text and [`NOTICE`](./NOTICE) for
attribution. The published `intent-cli` NuGet package declares `Apache-2.0` via
SPDX license metadata.

Release artifacts (the NuGet package and self-contained binaries) and OSS
preview CI artifacts carry no expiration or private-use gating.
