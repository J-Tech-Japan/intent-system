# intent-system

## Install

`intent-cli` is published from GitHub Releases (G386). Each release publishes the
NuGet package and attaches SDK-free self-contained binaries for macOS, Windows,
and Linux.

### With a .NET SDK (NuGet)

If you have a .NET 10 SDK, install the global tool from NuGet.org:

```bash
dotnet tool install -g intent-cli
# later, to upgrade:
dotnet tool update -g intent-cli
```

Then run `intent-cli --version` to confirm the install.

### Without a .NET SDK (self-contained binary)

Download the archive for your platform from the
[latest GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest)
and run it directly — the .NET runtime is bundled, so no SDK is required.

| Platform | Asset |
| --- | --- |
| macOS (Apple Silicon) | `intent-cli-<version>-osx-arm64.tar.gz` |
| Windows (x64) | `intent-cli-<version>-win-x64.zip` |
| Linux (x64) | `intent-cli-<version>-linux-x64.tar.gz` |

Each archive ships with a `.sha256` sidecar; verify it before use. Example for
macOS / Linux:

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

Release binaries carry no build-time expiry (unlike the
`private-preview-pack` artifacts described below).

## Project-local best-practice inputs

Project-local best-practice and model-registry starter docs live under:

- `.intent/best-practices/`
- `.intent/model-registry/`

The first starter set is intentionally explicit:

- best practices: engineering, AI-assisted delivery, Azure, Sekiban
- model registry: aggregate, read-model, API, auth-model

Use these as the child-repo knowledge base for `generate-from-current best-practice`. They are bounded repo-local inputs, not a replacement for parent intent refs or runtime command logic.

## Packaged invocation

The CLI is packaged as a .NET tool with:

- package id: `intent-cli`
- command name: `intent-cli`

Local package smoke path:

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

## Private-preview install (G367 / G369)

The `private-preview-pack` GitHub Actions workflow runs on every merge to
`main` and uploads a self-contained install bundle as a workflow artifact
named `intent-cli-private-preview-<version>`. The bundle contains:

| File | Purpose |
| --- | --- |
| `intent-cli.<version>.nupkg` | The NuGet package consumed by `dotnet tool install`. |
| `intent-cli.<version>.nupkg.sha256` | SHA-256 checksum sidecar; verify before installing (G369). |
| `preview-metadata.json` | Machine-readable build provenance (channel, version, build/expiry timestamps, commit, CI run identifiers). |
| `INSTALL.md` | Per-build install / update / verify / uninstall guide with this build's exact version, expiry, and commit pre-filled (G369). |

The package version pattern is `0.2.0-preview.<run_number>.<run_attempt>`,
so every CI run produces a distinct version. No PAT, source checkout, or
public NuGet feed is required to install -- only a compatible .NET SDK /
runtime and the unzipped bundle.

Install or update from a downloaded artifact:

```bash
# 1. Download and unzip the workflow artifact from the GitHub Actions
#    run page, e.g. into ./private-preview-package. Then `cd` into it.
cd ./private-preview-package

# 2. Verify the checksum (macOS: shasum; Linux: sha256sum). Prints
#    `intent-cli.<version>.nupkg: OK` on success. Do not install if
#    verification fails.
shasum -a 256 -c intent-cli.*.nupkg.sha256

# 3. Install (or update) the .NET tool from this local folder:
dotnet tool install --global --add-source . \
  --version 0.2.0-preview.<run_number>.<run_attempt> intent-cli
# Or for an upgrade-in-place:
dotnet tool update --global --add-source . \
  --version 0.2.0-preview.<run_number>.<run_attempt> intent-cli
```

To uninstall:

```bash
dotnet tool uninstall --global intent-cli
```

The installed binary exposes the preview metadata via `intent-cli --version`:

```text
intent-cli 0.2.0-preview.<run_number>.<run_attempt>-<short-sha>-G<unit>
channel=private-preview built=<iso-utc> expires=<iso-utc> commit=<full-sha>
```

The `channel=private-preview` trailer is the confirmation that the
embedded preview metadata loaded successfully; missing trailer means the
wrong package was installed.

CI-built private-preview packages expire 14 days after their build
timestamp; refresh the install from a newer workflow run when the
`expires=` line moves into the past. After expiry the installed tool
exits with code `78` (G368 private-preview expiry gate). Local source
builds (`dotnet pack` without the CI properties) carry no expiry trailer
and remain unrestricted. Each bundle's `INSTALL.md` contains the full
copy-pasteable step list with this build's exact version, expiry, and
commit pre-filled, so a tester who only received the zip via a private
share can install without referring back to this README.

## CLI command roles

The accepted production automation boundary lives in the parent host-side
review/next-slice loop, which uses provider-neutral GitHub labels
(`intent-target`, `intent-pr-reviewing`, `intent-pr-request-update`, etc.),
durable parent state, and explicit handoff artifacts. The child CLI is a tasking
companion to that loop, not a replacement.

| Surface | Role |
|---------|------|
| `intent-cli status brief` / `context collect` | Compact / richer AI-thread inputs |
| `intent-cli clarify draft` / `clarify record` | Owner clarification flow |
| `intent-cli issue validate-body` | Standalone Child Issue Contract enforcement |
| `intent-cli issue prepare` / `issue publish-reviewed` | Reviewed issue body publish boundary (never applies `intent-target`) |
| `intent-cli next-slice classify` | Local read-only continuation classifier |
| `intent-cli automation summary` | Provider-neutral label-driven automation contract emitter |
| `intent-cli safety nested-provider-handoff` | Artifact-only nested-provider safety guard (never spawns providers) |
| `intent-cli run …` | **Integration smoke, deterministic replay, and local dogfooding only** — not the primary production orchestrator |

For ongoing production automation, drive work through the host-side
review/next-slice loop and the provider-neutral label set described by
`intent-cli automation summary`. For nested-provider handoff steps, use
`intent-cli safety nested-provider-handoff` to emit a deterministic artifact
instead of recursively launching providers from inside `run`.

## Local coding automation prompt templates

Operator-dogfooding prompt templates that drive a local Claude/Codex coding
automation loop entirely through the deterministic `intent-cli` worker and
metadata commands (G202–G208) live under
[`docs/automation-templates/`](./docs/automation-templates/README.md). They
make explicit that:

- target selection runs through `intent-cli worker next-action`; prompts
  never reimplement label-walking;
- post-run outcomes go through `intent-cli worker result-summary`;
- parent-host metadata is touched only via `metadata validate` and the
  bounded `metadata update` transition modes;
- `intent-cli` is deterministic support tooling — it MUST NOT launch
  Claude, Codex, or any AI provider, and prompts must NOT call
  `intent-cli run` from this local coding-automation path.
