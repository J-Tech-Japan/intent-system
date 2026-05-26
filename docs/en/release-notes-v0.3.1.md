# Release Notes — intent-cli v0.3.1

> **Release checklist for maintainers:** see [Creating the v0.3.1 GitHub Release](#creating-the-v031-github-release).

## What's in v0.3.1

v0.3.1 is the first OSS-hardened follow-up to v0.3.0. It ships release
packaging improvements, repository cleanup, community files, and an OSS
readiness checklist. No new product commands are added.

### Release packaging (G409)

- Release workflow checksum sidecars now use the bare filename
  (`intent-cli-linux-x64.tar.gz.sha256`) instead of the full
  distribution-relative path, so `sha256sum -c` and `CertUtil -hashfile`
  work directly from the download directory without extracting the archive
  first.
- README verification instructions updated with per-platform
  (Linux / macOS / Windows) sections.

### Repository cleanup (G410)

- Removed `.takt/` runtime trace directory and its `.gitignore`.
- Moved `ops/` automation notes to `docs/automation-templates/` and
  `eng/`; deleted stale historical ops notes.
- `GuideRulesCommand` source reference updated from `ops/` to
  `docs/automation-templates/`.

### OSS community files (G411)

- Added `CONTRIBUTING.md` with ask-intent-cli-first rule, dev setup,
  PR expectations, and coding conventions.
- Added `CODE_OF_CONDUCT.md` (Contributor Covenant v2.1).
- Added `SECURITY.md` with private vulnerability reporting instructions.
- Added `SUPPORT.md`.
- Added `.github/FUNDING.yml`, issue templates, and PR template — all
  including intent-cli guidance prompts.

### OSS readiness (G412)

- Added `docs/oss-readiness-checklist.md` for pre-promotion audit.
- Fixed stale "internal testing channel" / "private-preview-install"
  wording in install docs and error messages.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.1
```

Or download the self-contained binary from the
[v0.3.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.1).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.0

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.1
```

There are no breaking changes from v0.3.0.

## Creating the v0.3.1 GitHub Release

1. Tag the release commit: `git tag v0.3.1 && git push origin v0.3.1`
2. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums. Wait for it to complete.
3. The workflow creates the GitHub Release draft. Review it, paste the
   content of this file as the release body, and publish.
4. Verify `dotnet tool install -g JTechJapan.IntentSystem.Cli` resolves
   the new version from NuGet.org within a few minutes of publish.
