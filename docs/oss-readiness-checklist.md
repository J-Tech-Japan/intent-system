# OSS Readiness Checklist

Run this checklist before promoting a branch or release to public OSS audiences.

## Secrets and credentials

- [ ] No private tokens, API keys, or passwords in tracked files
  ```bash
  grep -rn "token\|secret\|password\|api.key" --include="*.md" --include="*.yml" docs/ .github/ README.md
  ```
- [ ] Workflow secrets are accessed only via `${{ secrets.* }}` and guarded with `if [ -z "${VAR:-}" ]` skips

## Personal and internal paths

- [ ] No personal machine paths (e.g. `/Users/<name>/`, `/home/<name>/`) in user-facing docs or source
  ```bash
  grep -rn "/Users/\|/home/" --include="*.md" docs/ README.md
  ```
- [ ] Test fixtures using local paths are isolated to test files and not referenced from docs

## Private-preview and expiry language

- [ ] No user-facing "private-preview", "internal tester", or "社内テスター" wording in docs
  ```bash
  grep -rni "private.preview\|internal tester\|社内テスター" docs/ README.md
  ```
- [ ] No stale expiry or build-time gating claims in user-facing docs
- [ ] Preview channel docs link to `#preview-install` (not `#private-preview-install`)

## Install and first-use docs

- [ ] README leads with NuGet install (`dotnet tool install -g JTechJapan.IntentSystem.Cli`)
- [ ] README includes self-contained binary install instructions with checksum verification
- [ ] `docs/en/01-install.md` and `docs/ja/01-install.md` are reachable from the docs index
- [ ] `intent-cli --version` is documented as a post-install verification step
- [ ] `intent-cli guide start` is recommended before first workflow use

## Community files

- [ ] `README.md` exists and is public-audience-friendly
- [ ] `CONTRIBUTING.md` exists with ask-intent-cli-first rule and dev setup
- [ ] `CODE_OF_CONDUCT.md` exists
- [ ] `SECURITY.md` exists with private reporting instructions
- [ ] `SUPPORT.md` exists
- [ ] `.github/ISSUE_TEMPLATE/` contains bug report and feature request templates
- [ ] `.github/PULL_REQUEST_TEMPLATE.md` exists

## Release docs

- [ ] Released version `0.3.0` release notes exist and are accurate
- [ ] `eng/version.json` `nextVersion` reflects the correct post-release version
- [ ] No stale release candidate version strings in user-facing docs

## Static checks

- [ ] `git diff --check` passes (no trailing whitespace or mixed line endings)
- [ ] `dotnet test IntentSystem.sln --configuration Release` passes

## Source code

- [ ] No hardcoded private tokens or credentials in source
- [ ] `PrivatePreviewExpiryGate` is wired only in non-OSS build paths (check `#if` or build props)
  ```bash
  grep -rn "PrivatePreviewExpiryGate\|private.preview" src/ --include="*.cs"
  ```
