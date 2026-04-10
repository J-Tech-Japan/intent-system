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
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -o .artifacts/packages
mkdir -p .artifacts/smoke-repo/.intent-cli
cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'
default_domain = "intent-cli"
artifact_root = ".intent-cli"
worktree_root = ".intent-cli/worktrees"
EOF
(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version 0.1.0 intent-cli project status)
```

Equivalent `dnx` path:

```bash
(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version 0.1.0 intent-cli project status)
```
