# Release Notes — intent-cli v0.11.1

> Prepare-only: this PR creates no GitHub Release, tag, package publish,
> workflow run, merge, or version roll. The operator creates the Release only
> after the readiness gate below is satisfied.

## Scope

This patch release contains exactly these verified `main` merges:

- G607 — [PR #1318](https://github.com/J-Tech-Japan/intent-system/pull/1318), merge `764905194ee1`.
- G608 — [PR #1320](https://github.com/J-Tech-Japan/intent-system/pull/1320), merge `a138e32b82a7`.

Both commits resolve on `main`; no other slice is included. See
[v0.11.0](release-notes-v0.11.0.md) for the preceding release scope.

## Why PATCH

This is a verified patch release: it adds no command surface and changes no
behaviour. Since v0.11.0, the `src` delta is confined to presentation strings
in `GuideModelCommand`, `GuideOnboardingCommand`, and
`GuideCommandsListCommand`. The G608 review verified that the transport
operating contracts remain byte-unchanged. Installing v0.11.1 therefore aligns
the installed guide's transport presentation with the published docs chooser.

## Operational purpose

G607 added the orchestration-first 02a onboarding page and reordered the docs
index. G608 completes that front door: the default reading trail reaches
orchestration; the first decision is a 2×2 pattern chooser with four
self-contained pages and dual initial prompts; and `herdr-only` and
`agmsg` + herdr are conditionally recommended supported choices. The
four-thread model is primary; PREVIEW is only a transport maturity note.

## Install or upgrade

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.11.1
```

## Release-readiness gate

- [ ] `eng/version.json` remains `stableVersion` `0.11.0` / `nextVersion` `0.11.1`.
- [ ] These EN/JA notes name exactly G607 and G608 with the verified PRs and
      merge commits above.
- [ ] The patch comparison confirms no new command surface and only the three
      named guide presentation-string files in `src`.
- [ ] G475, focused release-note checks, full suite, diff check, and exact-head
      CI are green.
- [ ] The operator explicitly approves creating and publishing the v0.11.1 GitHub Release.

## Publishing v0.11.1

After this preparation is merged and every gate is green, the operator may
create [the v0.11.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.11.1).
This PR itself does not publish a package or create that Release.
