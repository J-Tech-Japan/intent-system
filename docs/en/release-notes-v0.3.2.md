# Release Notes — intent-cli v0.3.2

> **Release checklist for maintainers:** see [Creating the v0.3.2 GitHub Release](#creating-the-v032-github-release).

## What's in v0.3.2

v0.3.2 is a documentation and packaging quality release focused on validating
that all links on the NuGet.org package page resolve correctly after the
absolute-URL cleanup introduced in G432. No new product commands are added.

### NuGet package README absolute-link cleanup (G432)

- All repository-relative links (`./docs/...`, `./SECURITY.md`, etc.) in
  `README.md` converted to absolute GitHub blob URLs so they render correctly
  on the NuGet.org package page.
- Verified that install/upgrade commands, documentation links, community
  links, and license/notice references all point to the correct stable paths.

### Contract validation consistency (G433)

- `intent next-slice --dry-run` now uses the same required Child Issue
  Contract section list as `issue publish-flow`, eliminating a mismatch where
  `next-slice` could report `issue-cut-ready` for a packet that `publish-flow`
  would reject for missing sections (e.g. `Base Branch Policy`).
- `automation host-review-diagnostics --candidate <unit>` now validates the
  candidate's packet contract before reporting `issue-publish-ready`, and
  emits `missing_contract_sections` in JSON output when sections are absent.
- Regression tests added for both surfaces covering `Base Branch Policy`.

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.2
```

Or download the self-contained binary from the
[v0.3.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.2).
Verify the `.sha256` sidecar before use.

## Upgrade from v0.3.1

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.2
```

There are no breaking changes from v0.3.1.

## Creating the v0.3.2 GitHub Release

1. Tag the release commit: `git tag v0.3.2 && git push origin v0.3.2`
2. The `release.yml` workflow fires and builds binaries, `.nupkg`, and
   checksums. Wait for it to complete.
3. The workflow creates the GitHub Release draft. Review it, paste the
   content of this file as the release body, and publish.
4. Post-release verification checklist:
   - [ ] NuGet.org package page links all resolve correctly (absolute URLs
         from G432 are visible and functional).
   - [ ] GitHub release asset links (`.tar.gz`, `.zip`, `.exe`, `.nupkg`)
         are accessible.
   - [ ] `.sha256` checksums match the downloaded artifacts.
   - [ ] `intent-cli --version` reports `0.3.2`.
   - [ ] Local preview/dry-run version metadata uses `0.3.3` as the next
         development line.
