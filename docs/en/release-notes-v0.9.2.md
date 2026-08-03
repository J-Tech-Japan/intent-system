# Release Notes — intent-cli v0.9.2 (DRAFT — UNRELEASED)

> **⚠️ DRAFT / UNRELEASED.** This is a **stub**, not release notes. It exists
> because `eng/version.json` names `0.9.2` as the release-to-be-cut, and the
> G475 guard requires notes to exist for that version before it can be
> published. **The v0.9.2 release-prep packet authors the real content**;
> nothing here describes shipped behavior, and this file must not be treated as
> a changelog.

## Status

Created by the post-release version roll (G554 rule as amended by G557) at the
moment `nextVersion` became `0.9.2`. Until the release-prep packet fills it in:

- **No slices are listed yet.** What ships in `v0.9.2` is decided by the
  release-prep packet, not by this stub.
- **No bump rationale yet** (patch vs minor is a release-prep decision).
- **No readiness gate yet.** Do **not** publish a `v0.9.2` GitHub Release while
  this file is still a draft — an unfilled stub means release-prep has not run.

## What the release-prep packet must replace this with

Follow the shape of the previous notes
([v0.9.1](release-notes-v0.9.1.md), [v0.7.1](release-notes-v0.7.1.md)):

- what shipped, grouped by theme, covering exactly the merged slices;
- the bump rationale (patch vs minor) stated, not just labelled;
- the prepare-only publishing section and the release-readiness gate;
- an upgrade section separating additive surfaces from corrective behavior
  changes;
- the post-release roll reminder.

## Install (placeholder — the version below is what the guard checks)

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.9.2
```

Once published, the self-contained binaries will be attached to the
[v0.9.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.9.2).
Verify the `.sha256` sidecar before use.
