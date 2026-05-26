# Release Notes — intent-cli v0.3.0

> **Release checklist for maintainers:** see [Creating the v0.3.0 GitHub Release](#creating-the-v030-github-release).

## What's in v0.3.0

v0.3.0 is the first OSS-oriented stable release of `intent-cli`. It ships the
full set of commands that support the intent-driven development workflow on top
of GitHub, plus the first public NuGet and self-contained binary distribution.

### New commands in this release

| Command group | Commands added |
|---|---|
| `intent-cli intent` | `init-tree`, `add-feature`, `analyze-tree`, `lint-layout` |
| `intent-cli guide intent-work setup` | `--kind restructure` |

### Intent knowledge tree (G403/G404/G405)

- **`intent init-tree`** — bootstrap a domain into the `tree-v1` layout
  (`manifest.yaml` + category folders). Supports four project types:
  `product-app`, `library-tool`, `infrastructure`, `research-prototype`.
- **`intent add-feature`** — add a feature folder with seven starter files
  and auto-update `features/index.md`.
- **`intent analyze-tree`** — dry-run or write-mode analysis of flat
  intent files: heading extraction, keyword-based category suggestions,
  reference detection (markdown links, anchors, execution-unit IDs, packet
  paths, GitHub URLs), migration reference map, and `.restructure-backup/`
  copies.
- **`intent lint-layout`** — layout health check for flat and tree-v1
  domains. Emits seven lint codes (`MISSING-DOMAIN`, `MISSING-MANIFEST`,
  `MISSING-CATEGORY-FOLDER`, `LARGE-FLAT-FILE`, `BROKEN-RELATIVE-LINK`,
  `MISSING-FEATURES-INDEX`, `MISSING-FEATURE-OVERVIEW`) in Markdown or JSON.
- **`guide intent-work setup --kind restructure`** — emits a design-AI
  prompt that drives the flat-to-tree redesign workflow, asking `intent-cli`
  for the deterministic analysis and leaving semantic grouping to the
  operator + AI pair.

### Distribution (G386/G387)

- NuGet stable package: `intent-cli` on NuGet.org (Apache-2.0).
- Self-contained binaries attached to the GitHub Release:
  `osx-arm64`, `win-x64`, `linux-x64`.
- Preview/main builds continue using `nextVersion` from `eng/version.json`
  (now `0.3.1-preview.*` post-release).

---

## Install / update

### With the .NET SDK (recommended)

```bash
# New install
dotnet tool install -g intent-cli

# Upgrade from an older version
dotnet tool update -g intent-cli
```

Requires **.NET 10 SDK** (`dotnet --version` → `10.x`).

### Without the .NET SDK (self-contained binary)

Download the archive for your platform from the
[v0.3.0 GitHub Release assets](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.0),
extract, and place the `intent-cli` binary somewhere on your `PATH`.

| Platform | Archive name |
|---|---|
| macOS (Apple Silicon) | `intent-cli-0.3.0-osx-arm64.tar.gz` |
| Windows (x64) | `intent-cli-0.3.0-win-x64.zip` |
| Linux (x64) | `intent-cli-0.3.0-linux-x64.tar.gz` |

Verify with `sha256sum` against the matching `.sha256` sidecar file.

### Verify the install

```bash
intent-cli --version
# Expected: 0.3.0
```

---

## License

Apache-2.0 — see [LICENSE](../../LICENSE) in the repository root.

---

## Creating the v0.3.0 GitHub Release

> **For maintainers only.** This checklist is the authoritative sequence for
> cutting a stable release. Publishing the GitHub Release automatically triggers
> the release workflow, which builds and attaches the NuGet package and
> self-contained binaries.

### Pre-release checklist

- [ ] All intended PRs for this release are merged into `main`.
- [ ] `eng/version.json` has `"stableVersion": "0.3.0"` and
  `"nextVersion": "0.3.1"` (the post-release bump is already committed as
  part of G406 — do **not** change it before tagging; it represents the
  next development line, not this release).
- [ ] The release workflow (`release.yml`) is correct on `main`:
  - Derives the stable version from the release tag (not `eng/version.json`).
  - Does not set `PrivatePreview*` or expiry properties for the stable pack step.
  - Binaries dry-run version falls back to `nextVersion` from `eng/version.json`.
- [ ] `intent-cli --version` reports the expected version after a local build.
- [ ] `git diff --check` passes (no whitespace errors).
- [ ] CI is green on `main`.

### Release steps

1. **Create a GitHub Release** from the GitHub UI (or `gh release create`):
   - Tag: `v0.3.0` (create from `main`).
   - Title: `intent-cli v0.3.0 — first OSS-oriented stable release`.
   - Body: paste the content of this file from "What's in v0.3.0" through
     "License" (omit this checklist section).
   - **Publish** (not draft) — publishing triggers the release workflow.

2. **Monitor the release workflow** in the Actions tab:
   - `nupkg` job: builds `intent-cli.0.3.0.nupkg` and pushes to NuGet.org
     (if `NUGET_API_KEY` is set); attaches `.nupkg` + `.sha256` to the release.
   - `binaries` jobs (3×): build `osx-arm64`, `win-x64`, `linux-x64`
     self-contained archives; smoke-test `intent-cli --version`; attach to the
     release.

3. **Verify the release assets** on the GitHub Release page:
   - `intent-cli.0.3.0.nupkg` + `.sha256`
   - `intent-cli-0.3.0-osx-arm64.tar.gz` + `.sha256`
   - `intent-cli-0.3.0-win-x64.zip` + `.sha256`
   - `intent-cli-0.3.0-linux-x64.tar.gz` + `.sha256`

4. **Verify NuGet.org** (allow up to 15 minutes for indexing):
   ```bash
   dotnet tool install -g intent-cli --version 0.3.0
   intent-cli --version
   # Expected: 0.3.0
   ```

5. **Announce** if applicable (e.g., update internal docs, notify teams).

### Post-release version policy

After the `v0.3.0` release, `main` preview builds derive their version from
`eng/version.json`'s `nextVersion`, which is now `0.3.1`. Preview builds will
appear as `0.3.1-preview.<build>.<commit>`.

When preparing the next stable release (`v0.3.1` or higher), update
`eng/version.json` **in the release-prep PR** (just like this G406 PR did),
setting `stableVersion` to the new stable version and `nextVersion` to the
following patch line.

> **Rule:** never leave `nextVersion` equal to the just-published stable
> version after a release. The version bump in `eng/version.json` is the
> commit evidence that the release boundary was crossed.
