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

New to `intent-cli`? The typical path is:

1. [Install](#1-install) — one-time setup.
2. [Verify](#2-verify) — confirm `intent-cli` is on your PATH.
3. [Open a design thread and paste a prompt](#3-open-a-design-thread-and-paste-a-prompt) — the AI agent does the rest.

After install, you do not need to memorize `intent-cli` commands. Open a Claude,
Codex, or Copilot-style chat with repository access and paste one of the
[ready-made prompts](#design-thread-prompt-examples) below. The AI agent will
run `intent-cli` internally and bring questions or results back to you.

### 1. Install

The basic path is the .NET global tool from NuGet.org. You need a **.NET 10
SDK** (`dotnet --version` should report `10.x`).

**macOS, Windows, and Linux (same commands):**

```bash
# Install (NuGet package id: JTechJapan.IntentSystem.Cli; command: intent-cli)
dotnet tool install -g JTechJapan.IntentSystem.Cli

# Later, upgrade in place
dotnet tool update -g JTechJapan.IntentSystem.Cli
```

If the global tools directory is not yet on your `PATH`, the `dotnet tool
install` output prints the exact line to add (commonly `~/.dotnet/tools` on
macOS/Linux, `%USERPROFILE%\.dotnet\tools` on Windows).

> No .NET SDK? Use the self-contained binary instead — see
> [Install without a .NET SDK](#install-without-a-net-sdk). Need the preview
> channel? See [Preview install](#preview-install).

### 2. Verify

```bash
intent-cli --version
```

A released build prints just the version line. (OSS preview CI builds add a
`channel=preview built=… commit=…` trailer — see
[Preview install](#preview-install).)

### 3. Open a design thread and paste a prompt

Open a capable AI coding agent (Claude, Codex, Copilot, etc.) with access to
your repository. Paste one of the prompts below. The agent will run
`intent-cli` commands internally and bring back questions or results — you
focus on intent, priorities, and approval decisions.

**Start or continue a project:**

> I want to work on `<owner>/<repo>` with intent-cli.
> Please run `intent-cli guide start` and `intent-cli intent status`, then
> tell me which phase we're in and what I should decide next.

**Design/clarify intents before cutting work:**

> Ask intent-cli about the current design phase for `<owner>/<repo>` domain `<name>`.
> Run `intent-cli guide workflow --format json` and `intent-cli interview next-question`.
> Report back: what questions remain open and what is the recommended next action?

**Create a packet and publish GitHub issues:**

> For domain `<name>` in `<owner>/<repo>`, ask intent-cli to help me
> scaffold the next packet and publish a reviewed Child Issue Contract.
> Run `intent-cli guide workflow --format json`, then follow the packet
> and issue-publish workflow. Apply all labels through intent-cli; never
> hand-apply `intent-target`.

**Start an implementation loop (child agent):**

> Set up a child implementation loop for `<owner>/<repo>`.
> Run `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`
> and follow the guidance. Use `intent-cli worker` commands for all label
> transitions; never run raw `gh ... edit --add-label`.

**Start a review / next-slice loop (host agent):**

> Set up the host review and next-slice loop for `<owner>/<repo>`.
> Run `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>`
> and follow the guidance. Use `intent-cli automation` commands for all
> host-side label transitions.

**Recover when something looks wrong:**

> Something looks wrong with `<owner>/<repo>`.
> Run `intent-cli automation doctor --format json` and
> `intent-cli worker issue-preflight --repo <owner>/<repo> --issue <n> --format json`.
> Classify the gap and apply only the safe, in-scope repair intent-cli recommends.

---

## Command reference (agent-facing / power users)

The commands below are what the AI agent runs on your behalf. You do not need
to run them directly for routine use; they are documented here for transparency,
advanced troubleshooting, and power-user automation.

### Project setup

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work --format json
```

### Design / intents

```bash
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile
intent-cli guide workflow
```

### Packets / issues

```bash
intent-cli packet ...
intent-cli issue validate-body ...
intent-cli issue prepare ...
intent-cli issue publish-reviewed ...
```

### Implementation & review loops

```bash
# Fetch the complete loop prompt for an AI agent:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --repo <owner>/<repo>
```

Operator-dogfooding prompt templates that wire these loops entirely through the
deterministic worker/metadata commands live under
[`docs/automation-templates/`](./docs/automation-templates/README.md).

### Recovery

```bash
intent-cli worker issue-preflight       --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight  --repo <owner>/<repo> --pr <n>    --format json
intent-cli automation doctor --format json
```

### Rules of thumb

- **Use `intent-cli` transition commands, not raw edits.** Do not directly edit
  queue-state, workflow labels, packet publish metadata, or other host artifacts
  when an `intent-cli automation` / `intent-cli worker` command owns that
  transition. Apply labels through those commands, never `gh ... edit
  --add-label`.
- **Ask, don't read-and-guess.** Prefer `intent-cli guide ...` over reading
  local rule files; the guidance reflects the installed CLI's current contract.
- **`intent-cli` does not launch AI providers.** It emits deterministic
  guidance, validates contracts, and performs bounded GitHub/metadata
  transitions. The AI agent stays in the driver's seat.

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
# 1. Verify (run from the folder containing both files).
sha256sum -c intent-cli-<version>-linux-x64.tar.gz.sha256

# 2. Extract and place the binary on your PATH.
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

---

## Documentation

- Bilingual onboarding docs (installation, project start, intent organization,
  packet/issue creation, implementation & review loop setup, recovery) —
  **English: [`docs/en/`](./docs/en/index.md)**, **日本語: [`docs/ja/`](./docs/ja/index.md)**.
- Local coding-automation prompt templates:
  [`docs/automation-templates/`](./docs/automation-templates/README.md).

---

## Packaged invocation (local smoke)

The CLI is packaged as a .NET tool (package id `JTechJapan.IntentSystem.Cli`,
command `intent-cli`). To smoke-test a locally built package:

```bash
export INTENT_CLI_LOCAL_VERSION="0.3.1-local.$(date -u +%Y%m%d%H%M%S)"
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

For ongoing production automation, drive work through the host-side
review/next-slice loop and the provider-neutral label set described by
`intent-cli automation summary`. For nested-provider handoff steps, use
`intent-cli safety nested-provider-handoff` to emit a deterministic artifact.

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
| `JTechJapan.IntentSystem.Cli.<version>.nupkg` | The NuGet package consumed by `dotnet tool install`. |
| `JTechJapan.IntentSystem.Cli.<version>.nupkg.sha256` | SHA-256 checksum sidecar; verify before installing. |
| `preview-metadata.json` | Machine-readable build provenance (channel, version, build timestamp, commit, CI run identifiers). |
| `INSTALL.md` | Per-build install / update / verify / uninstall guide with this build's exact version and commit pre-filled. |

The package version pattern is `<nextVersion>-preview.<run_number>.<run_attempt>`
(e.g. `0.3.1-preview.42.1`), where `nextVersion` comes from `eng/version.json`.
Every CI run produces a distinct version. No PAT, source checkout, or public
NuGet feed is required — only a compatible .NET SDK / runtime and the unzipped
bundle. **OSS preview packages carry no expiry; they remain runnable indefinitely.**

```bash
# 1. Download and unzip the workflow artifact, then cd into it.
cd ./intent-cli-preview-0.3.1-preview.42.1

# 2. Verify the checksum (macOS: shasum; Linux: sha256sum). Prints
#    `JTechJapan.IntentSystem.Cli.<version>.nupkg: OK` on success. Do not install if it fails.
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
successfully. Source builds (`dotnet pack` without the CI properties) produce no
trailer and remain unrestricted.

---

## Version flow

The repository version policy lives in `eng/version.json`:

```json
{
  "stableVersion": "0.3.0",
  "nextVersion": "0.3.1"
}
```

| Stage | Version form | How it is derived |
| --- | --- | --- |
| Main CI preview | `0.3.1-preview.<run>.<attempt>` | `nextVersion` from `eng/version.json` |
| Release candidate (optional) | `0.3.1-rc.N` | Tag `v0.3.1-rc.N` triggers release workflow |
| Stable release | `0.3.1` | Tag `v0.3.1` triggers release workflow |
| Post-release main builds | `0.4.0-preview.<run>.<attempt>` | After bumping `nextVersion` to `0.4.0` |

**After releasing `v0.3.1`**, bump both fields in `eng/version.json`:

```json
{
  "stableVersion": "0.3.1",
  "nextVersion": "0.4.0"
}
```

This ensures the next main-branch CI build immediately produces
`0.4.0-preview.<run>.<attempt>` rather than continuing to emit `0.3.1-preview`
(which would collide with the stable release version).

---

## Community

Join the [J-Tech Japan Discord](https://discord.gg/kMdv978X) for community
discussion, questions, and lightweight support. Discord is for general chat;
for reproducible bugs or actionable feature requests, please open a
[GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) instead.
Security-sensitive reports go to [SECURITY.md](./SECURITY.md), not Discord.

---

## License

This project is licensed under the Apache License, Version 2.0 — see the
[`LICENSE`](./LICENSE) file for the full text and [`NOTICE`](./NOTICE) for
attribution. The published `intent-cli` NuGet package declares `Apache-2.0` via
SPDX license metadata.

Release artifacts (the NuGet package and self-contained binaries) and OSS
preview CI artifacts carry no expiration or private-use gating.
