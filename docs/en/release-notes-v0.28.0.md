# Release Notes — intent-cli v0.28.0

> **PREPARED / NOT PUBLISHED.** This is the prepare-only release-note set for
> the independently measured v0.28.0 line. This unit does not tag, publish,
> create a GitHub Release, push packages, or perform the post-release roll.

No GitHub Release exists yet for v0.28.0; these notes are preparation evidence
only. The matching install query is
`JTechJapan.IntentSystem.Cli --version 0.28.0`.

The policy after this release-prep is:

```json
{
  "stableVersion": "0.28.0",
  "nextVersion": "0.28.1"
}
```

The `0.28.1` value is a replaceable development placeholder only. It is not a
choice of the next real release number; a later release-prep packet must measure
and decide that number.

## Independently measured command-surface difference

The version decision uses separate Release builds of the tagged v0.27.0
baseline and this prepared head, not an inference from `eng/version.json`:

```bash
# tagged baseline
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.27.0-f43fbd1-G753

# normal clean build of named revision 565530e5c965d55335790c9446ef0686988d14c8
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.27.1-565530e-G769
# release-prep build with explicit -p:Version=0.28.0
dotnet build IntentSystem.sln --configuration Release -p:Version=0.28.0
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.28.0-565530e-G769
```

The normal clean build of the named revision reproducibly reports
`intent-cli 0.27.1-565530e-G769`; the separate `0.28.0` identity above is the
release-prep build with its explicit version-policy input. The published release
version is derived by `release.yml` from the `v0.28.0` tag (`VERSION` from the
`RAW` tag), while `eng/version.json` governs local builds and dry runs.

The child also observed the separately installed baseline
`intent-cli 0.27.1-5d553b7-G756`; it is reported as environment evidence, not
substituted for the tagged v0.27.0 comparison. A programmatic sweep invoked
help for all 32 command groups and every direct subcommand. The tagged
v0.27.0 build counted 32 group descriptors plus 72 direct-help usage lines,
or **104 usages**; the prepared build counted 32 plus 74, or **106 usages**.
There was exactly one route addition in this comparison and no removal.

| measured surface | tagged v0.27.0 Release build | prepared v0.28.0 Release build |
| --- | ---: | ---: |
| group descriptors | 32 | 32 |
| direct-help usage lines | 72 | 74 |
| total usages | 104 | 106 |
| `claim stranded` | absent/unimplemented | present |
| `notify supervise liveness` | absent (`invalid-notification: Unknown argument 'liveness'.`) | present |

The two new command routes are:

```text
Usage: intent-cli claim stranded [list] [--format json|markdown] (reports metadata-branch records absent from canonical branch)
Usage: intent-cli notify supervise liveness --domain <d> --team <t> [--routing-root <host-root>] [--format markdown|json]
```

The existing `notify supervise repair-cycle-history` route is already included
in the v0.27.0 baseline. Its usage, plus the `automation`, `worker`,
`state-doctor`, and `closeout-drift-check` surfaces, remained byte-identical
where the route was unchanged. Option-level additions are not counted as new
command routes. The measured two-route addition, not the policy file alone, is
the auditable reason for this minor bump.

## Release inventory: exactly 18 units

The range is exactly `v0.27.0..565530e5c965d55335790c9446ef0686988d14c8`.
Every first-parent commit in that range was read from git and is accounted for
below. Each entry names an operator-observable outcome.

- G754 — PR #1641 / issue #1640; merge commit `6ea81ac85e5fc104d5cd954766c916445f751183`.
  **Operator-observable outcome:** the post-v0.27.0 version policy rolls to the
  measured v0.28.0 preparation line without choosing a later real number.
- G755 — PR #1643 / issue #1642; merge commit `9a30e95accc9d92d56ba0bdb62b1974ec7ab8302`.
  **Operator-observable outcome:** canonical claim readers use the remote
  default branch and no longer mistake a current checkout branch for authority.
- G756 — PR #1646 / issue #1644; merge commit `ec261ec4c16454d122a3baec0d48393a4245f513`.
  **Operator-observable outcome:** external-resident roles show their effective
  reader and advisory validation distinguishes recorded from effective paths.
- G757 — PR #1648 / issue #1645; merge commit `071ccf2c988e6244633c0971c8098fbd31b17093`.
  **Operator-observable outcome:** an external role can collect its own events
  with a caller-held cursor and a bounded, explicit wait result.
- G758 — PR #1652 / issue #1647; merge commit `145c5a43c031353a5e5ad4d7ea9eb3fb7365304c`.
  **Operator-observable outcome:** checkout freshness is a read-only local
  containment question, so an ancestor default tip is current even when HEAD
  is not textually identical.
- G759 — PR #1650 / issue #1649; merge commit `c6e6922e8ca89520465adfa8f69375eefd5d4fa6`.
  **Operator-observable outcome:** file-backed delegation tells the recipient
  to read and execute the absolute task envelope while inline delivery stays
  unchanged.
- G760 — PR #1653 / issue #1651; merge commit `5d553b7a0aeecf8d9939080eada9772963fe35c8`.
  **Operator-observable outcome:** task-envelope reports persist at the
  recipient topology cwd, with a safe explicit placeholder when no cwd exists.
- G761 — PR #1660 / issue #1655; merge commit `5a6e850412beb5cd515991b3486022e457726f6a`.
  **Operator-observable outcome:** an operator can confirm a guarded
  external-to-herdr or herdr-to-external residence transition with CAS safety.
- G762 — PR #1659 / issue #1657; merge commit `ff11a355377fe2b1698cce1e14f39d8c79c20bd5`.
  **Operator-observable outcome:** rendered design guidance gives an
  external-resident seat the role-scoped collect receive contract.
- G763 — PR #1667 / issue #1663; merge commit `6cc2b05127f7dc8c9080e425eb5af8e0e099ace7`.
  **Operator-observable outcome:** stranded metadata-branch claims can be
  reported and migrated with remote-receipt confirmation.
- G764 — PR #1666 / issue #1664; merge commit `642a86626f95fe271be663fca9d79240a58e6fd7`.
  **Operator-observable outcome:** request-update guidance includes the same
  wake for a loopless receiver while preserving the G524 cap and timer loop.
- G765 — PR #1670 / issue #1665; merge commit `db5394d75e267e17606f9a5fb96b3607ec58b435`.
  **Operator-observable outcome:** persistence metadata, keep-versus-legacy
  reconciliation, and read-only liveness remain observable without lifecycle
  execution.
- G766 — PR #1671 / issue #1669; merge commit `7adb2b5cac8090865d19c864842dbed48ffab7d2`.
  **Operator-observable outcome:** metadata-branch claims no longer make an
  empty canonical branch look like a configured ownership store.
- G767 — PR #1673 / issue #1672; merge commit `4dcf1916a94dfb871a1249fd60a3a4569b0a032c`.
  **Operator-observable outcome:** one malformed supervision JSONL record is
  reported while valid records remain readable instead of becoming clean absence.
- G768 — PR #1676 / issue #1674; merge commit `af8b82c37c27ff319c7468084b8ac59590f887fb`.
  **Operator-observable outcome:** real concurrent writers append complete
  records atomically without dropping cycle, stall, or prompt-audit evidence.
- G769 — PR #1677 / issue #1675; merge commit `a92a53fda2f8901e49b0e60d5d7c00d5c2a6c324`.
  **Operator-observable outcome:** an explicit routing root is honored and a
  missing store is distinguishable from a real store with empty history.
- G770 — PR #1680 / issue #1678; merge commit `b111fc644dfca24b911c26eef6bad9c784ad6cd4`.
  **Operator-observable outcome:** every successful liveness response exposes
  supervision state, including no-flag not-found versus empty-history results.
- G771 — PR #1682 / issue #1681; merge commit `565530e5c965d55335790c9446ef0686988d14c8`.
  **Operator-observable outcome:** every claim outcome tolerates bounded cleanup
  failure, preserves the real cause, warns with the leftover, and sweeps only
  stale transaction roots.

## First-parent accounting

The exact accounting was measured with:

```bash
git rev-list --first-parent --reverse v0.27.0..565530e5c965d55335790c9446ef0686988d14c8
git rev-list --first-parent --count v0.27.0..565530e5c965d55335790c9446ef0686988d14c8
# 18
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `6ea81ac85e5fc104d5cd954766c916445f751183` | G754 / PR #1641 | included |
| `9a30e95accc9d92d56ba0bdb62b1974ec7ab8302` | G755 / PR #1643 | included |
| `ec261ec4c16454d122a3baec0d48393a4245f513` | G756 / PR #1646 | included |
| `071ccf2c988e6244633c0971c8098fbd31b17093` | G757 / PR #1648 | included |
| `145c5a43c031353a5e5ad4d7ea9eb3fb7365304c` | G758 / PR #1652 | included |
| `c6e6922e8ca89520465adfa8f69375eefd5d4fa6` | G759 / PR #1650 | included |
| `5d553b7a0aeecf8d9939080eada9772963fe35c8` | G760 / PR #1653 | included |
| `5a6e850412beb5cd515991b3486022e457726f6a` | G761 / PR #1660 | included |
| `ff11a355377fe2b1698cce1e14f39d8c79c20bd5` | G762 / PR #1659 | included |
| `6cc2b05127f7dc8c9080e425eb5af8e0e099ace7` | G763 / PR #1667 | included |
| `642a86626f95fe271be663fca9d79240a58e6fd7` | G764 / PR #1666 | included |
| `db5394d75e267e17606f9a5fb96b3607ec58b435` | G765 / PR #1670 | included |
| `7adb2b5cac8090865d19c864842dbed48ffab7d2` | G766 / PR #1671 | included |
| `4dcf1916a94dfb871a1249fd60a3a4569b0a032c` | G767 / PR #1673 | included |
| `af8b82c37c27ff319c7468084b8ac59590f887fb` | G768 / PR #1676 | included |
| `a92a53fda2f8901e49b0e60d5d7c00d5c2a6c324` | G769 / PR #1677 | included |
| `b111fc644dfca24b911c26eef6bad9c784ad6cd4` | G770 / PR #1680 | included |
| `565530e5c965d55335790c9446ef0686988d14c8` | G771 / PR #1682 | included |

The inventory is exactly G754 through G771; no roll commit is silently dropped
or counted as a separate release unit.

## Truthful supervision-history chain

G768 stops new corruption from concurrent partial writes, but it does not repair
existing damage. The real host still has **9 unreadable records**. G771 makes a
post-commit deletion failure harmless and keeps the primary cause visible, but
does not change the **250 ms × 3** cleanup bound or make deletion more reliable;
the real deletion remains about **1.8 s**. These are measured limitations,
not claims that the incidents disappeared. The G768 and G771 release entries
and this test guard pin both statements.

Issues **#1679** and **#1662** are closed by the G771 and G765/G770 work,
respectively; **#1661** was already fixed before this range. The release
notes retain those references so operators can connect the release evidence to
the reports without treating an unrelated follow-up as a new release unit.

## Prepare-only verification

Final child Release verification was `Failed: 0, Passed: 5445, Skipped: 1,
Total: 5446` at the prepared source range above; the one skipped test is the
existing environment-gated test. `git diff --check` is clean. Focused and
adjacent guard counts are reported with the PR evidence. No product source
change, tag, GitHub Release, publish, or post-release action is included.
