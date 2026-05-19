# intent-system

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

## Private-preview install (G367)

The `private-preview-pack` GitHub Actions workflow runs on every merge to
`main` and uploads an `intent-cli` `.nupkg` plus a `preview-metadata.json`
descriptor as a workflow artifact named
`intent-cli-private-preview-<version>`. The package version pattern is
`0.2.0-preview.<run_number>.<run_attempt>`, so every CI run produces a
distinct version.

Install or update from a downloaded artifact:

```bash
# 1. Download and unzip the workflow artifact from the GitHub Actions
#    run page, e.g. into ./private-preview-package.
# 2. Install (or update) the .NET tool from that local folder:
dotnet tool install --global --add-source ./private-preview-package \
  --version 0.2.0-preview.<run_number>.<run_attempt> intent-cli
# Or for an upgrade-in-place:
dotnet tool update --global --add-source ./private-preview-package \
  --version 0.2.0-preview.<run_number>.<run_attempt> intent-cli
```

The installed binary exposes the preview metadata via `intent-cli --version`:

```text
intent-cli 0.2.0-preview.<run_number>.<run_attempt>-<short-sha>-G<unit>
channel=private-preview built=<iso-utc> expires=<iso-utc> commit=<full-sha>
```

CI-built private-preview packages expire 14 days after their build
timestamp; refresh the install from a newer workflow run when the
`expires=` line moves into the past. Local source builds
(`dotnet pack` without the CI properties) carry no expiry trailer and
remain unrestricted.

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
