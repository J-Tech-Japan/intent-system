# Release Notes — intent-cli v0.29.0

> **PREPARED / NOT PUBLISHED.** This is the prepare-only release-note set for
> the independently measured G773–G777 corrective chain. This unit does not
> create a tag or GitHub Release, publish packages, change a workflow trigger
> or configuration, or change product source.

No GitHub Release exists yet for v0.29.0; these notes are preparation evidence
only. The matching install query is
`JTechJapan.IntentSystem.Cli --version 0.29.0`.

The policy after this preparation is:

```json
{
  "stableVersion": "0.29.0",
  "nextVersion": "0.29.1"
}
```

`0.29.1` is a replaceable development placeholder only. It is not a choice of
the next real release number; a later release-prep packet must measure and
decide that number. The EN and JA v0.29.1 files are DRAFT planning scaffolds,
not changelogs.

## Independently measured minor justification

The version decision comes from Release builds and tagged behavior, not from an
inference from `eng/version.json`. The named base revision is
`65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf`.

```bash
# normal clean Release build of the named base revision
dotnet build IntentSystem.sln --configuration Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.28.1-65e02d8-G772

# explicit release-prep identity on the same named base revision
dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.29.0
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.29.0-65e02d8-G772
```

The first banner is the current `nextVersion` placeholder identity and is
**not** v0.29.0. The second banner is only the explicit `-p:Version=0.29.0`
measurement. A published v0.29.0 release derives its version in `release.yml`:
on a release event `RAW` is the `v0.29.0` tag and `VERSION="${RAW#v}"` is
`0.29.0`; `eng/version.json` governs local builds and dry runs. No v0.29.0 tag
was created by this preparation.

The tagged v0.28.0 Release build rejects the new G773 route:

```text
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll notify supervise repair-unreadable --format json
invalid-notification: Unknown argument 'repair-unreadable'.
```

The named base provides the route:

```text
Usage: intent-cli notify supervise repair-unreadable --domain <d> --team <t> [--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]
```

The v0.28.0 release policy's auditable rule is **a command-route addition is a
minor bump; option-level additions do not count as command routes**. G773 adds
this one route, so the operator chose v0.29.0. G776's `--wake-command` is an
option-level declaration, explicitly not counted as a second route.

## Release inventory: exactly five units

The exact first-parent range is
`v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf`. Git measured five
commits, and every one is listed below with an operator-observable outcome.

- G773 — PR #1686 / issue #1685; merge commit `370cfd3ad6b008503fc38d11822a31617949c372`.
  **Operator-observable outcome:** `notify supervise repair-unreadable` first
  previews and then quarantines unreadable supervision evidence without
  reconstructing it.
- G774 — PR #1690 / issue #1687; merge commit `9f124d86b0cc76366d2bb8cfcdcffed17a9eca66`.
  **Operator-observable outcome:** rendered design guidance exposes a bounded
  `packet_authoring_check` before a packet is handed to implementation.
- G775 — PR #1691 / issue #1688; merge commit `75216283875b08ade3d100de7ddabe3fad0bd21c`.
  **Operator-observable outcome:** external frontend relabel guidance keeps
  residence, reader, and routing root distinct from an update-residence move.
- G776 — PR #1692 / issue #1689; merge commit `b766f2d0961c665a2d6216c7ed24755556560626`.
  **Operator-observable outcome:** a declared external wake command is shown
  as a courtesy after the durable canonical report, while intent-cli never
  executes or manages it and undeclared output remains unchanged.
- G777 — PR #1694 / issue #1693; merge commit `65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf`.
  **Operator-observable outcome:** a non-zero unreadable count routes an
  operator to repair-unreadable dry-run first and `--write` second.

## First-parent accounting

```bash
git rev-list --first-parent --reverse v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf
git rev-list --first-parent --count v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf
# 5
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `370cfd3ad6b008503fc38d11822a31617949c372` | G773 / PR #1686 / issue #1685 | included |
| `9f124d86b0cc76366d2bb8cfcdcffed17a9eca66` | G774 / PR #1690 / issue #1687 | included |
| `75216283875b08ade3d100de7ddabe3fad0bd21c` | G775 / PR #1691 / issue #1688 | included |
| `b766f2d0961c665a2d6216c7ed24755556560626` | G776 / PR #1692 / issue #1689 | included |
| `65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf` | G777 / PR #1694 / issue #1693 | included |

## Truthfulness boundaries

- `repair-unreadable` quarantines unreadable lines **verbatim** as evidence. It
  makes no reconstruction claim, is never automatic, and is never performed on
  read.
- Teams without a declared wake channel receive **zero changed bytes** in
  delegate results and task envelopes. A declared wake remains courtesy-only
  and never replaces the durable canonical record.
- The real-host repair of **9 records** with audit transaction `6279ad14` is
  evidence that the repair path worked in that observed transaction. It is not
  a fleet-cleanliness claim.

## Prepare-only verification

The PR records the exact parent-absence, focused, adjacent, G613, full Release,
build, `git diff --check`, and CI evidence. This change is restricted to
release notes, version policy, readiness documentation, and test guards; it
does not include a tag, GitHub Release, package publish, workflow change, or
product-source change.
