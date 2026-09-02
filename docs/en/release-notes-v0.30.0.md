# Release Notes — intent-cli v0.30.0

> **PREPARED / NOT PUBLISHED.** This is the prepare-only release-note set for
> the measured G779–G786 arc. It does not create a tag or GitHub Release,
> publish packages, change a workflow trigger or publish configuration, post a
> consumer comment, or change product source.

No GitHub Release exists yet for v0.30.0; these notes are preparation evidence
only. The matching install query is
`JTechJapan.IntentSystem.Cli --version 0.30.0`.

The policy after this preparation is:

```json
{
  "stableVersion": "0.30.0",
  "nextVersion": "0.30.1"
}
```

`0.30.1` is a replaceable development placeholder, not a decision about the
next real release number. The EN and JA v0.30.1 files are planning scaffolds,
not changelogs.

## Same-repo claim targets (G780)

Claims now honor the existing same-repository topology declaration. When the
invoking checkout's `.intent-cli/config.toml` has both
`same_repo_topology = true` and a non-empty `metadata_write_branch`, claim
acquire, release, takeover, verification, and the worker store probe all use
`refs/heads/<metadata_write_branch>`. Hosts without that declaration continue
to use the G747 remote default branch unchanged.

The resolver fails closed: a missing or unsafe metadata write branch is an
error, not permission to fall back to the remote default or current checkout
branch. `claim stranded` also supports the reverse G763 migration direction:
on a declared host, records on the remote default are migrated transactionally
to `metadata_write_branch`, with dry-run, receipt verification, and an
unchanged source branch.

G779's rejected-push classification and fields remain intact on either target.
This lets an unprotected `intent-metadata` branch accept claims while a
protected `main` still reports an honest `push-rejected` result when the
same-repo declaration is absent.

## Independently measured minor justification

The named product base revision is
`d9dc053dd81f53c3a8be420ee7c6798b808f4521`. G780 is the deciding minor
contract change: on a declared same-repository host, the canonical claim
location moves to `metadata_write_branch`. That externally observable location
change enables a host whose product branch is protected; it is broader than a
patch correction while preserving undeclared-host default behavior.

The v0.28.0 release rule remains the auditable distinction: **a command-route
addition is a minor bump; option-level additions do not count as command
routes.** This arc's `--verify`, `--accept-evidence-gap`, and `--shell-policy`
options, plus G784 `update-field` values, are explicitly not counted as new
command routes. The minor decision is G780's claim-location contract change,
not those option-level additions.

The tagged v0.29.0 tool was measured before treating G781 as a release fact:

```text
$ intent-cli --version
intent-cli 0.29.0-8d019f8-G772
$ intent-cli notify supervise install
invalid-supervision-install: --domain, --team, and --owner-role are required safe identity values.
Usage: intent-cli notify supervise install --domain <d> --team <t> --repo <owner/repo> --owner-role <role> --bound <seconds> --interval <seconds> [--startup-bound <seconds>; default 30] [--persistence persistent] [--event-mode] [--platform macos|windows|linux] [--output <path>] [--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]
```

That actual tagged usage has no `--verify`. The named-base head build exposes
the new re-proof path:

```text
Usage: intent-cli notify supervise install --domain <d> --team <t> --repo <owner/repo> --owner-role <role> --bound <seconds> --interval <seconds> [--startup-bound <seconds>; default 120 for --write, 1 for --verify] [--persistence persistent] [--event-mode] [--pre-approve <agent-kind>:<prompt-class>]... [--pre-escalate <agent-kind>:<prompt-class>]... [--shell-policy <json>]... [--platform macos|windows|linux] [--output <path>] [--routing-root <host-root>] [--verify|--dry-run|--write] [--format markdown|json]
```

## Measured version identities

The product base stayed at `d9dc053dd81f53c3a8be420ee7c6798b808f4521` while
the prepare-only policy was rolled. After `dotnet clean`, the normal clean
Release build was re-measured with the rolled policy:

```text
$ dotnet build IntentSystem.sln --configuration Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.30.1-d9dc053-G772
```

That normal identity is the `nextVersion` placeholder and is **not** v0.30.0.
The same named product base measures v0.30.0 only when explicitly overridden:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.30.0
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.30.0-d9dc053-G772
```

Published versioning is separately derived by `release.yml`, not by
`eng/version.json`: on a release event it assigns `RAW` from the release tag
and `VERSION="${RAW#v}"`. The measured tag transformation is:

```text
$ RAW=v0.30.0; VERSION="${RAW#v}"; printf 'RAW=%s\nVERSION=%s\n' "$RAW" "$VERSION"
RAW=v0.30.0
VERSION=0.30.0
```

Thus a published v0.30.0 derives from the `v0.30.0` tag; `eng/version.json`
governs local builds and dry runs only. This preparation created no tag.

## Release inventory: exactly eight units

The exact first-parent range is
`v0.29.0..d9dc053dd81f53c3a8be420ee7c6798b808f4521`. Git measured eight
commits, and every one is listed with one operator-observable outcome.

- G779 — PR #1705 / issue #1699; merge commit `1057923311a0819d994c5180c1a58adff1e2fd8c`.
  **Operator-observable outcome:** a rejected claim push reports its real
  rejected-push cause instead of misreporting an unrelated remote advance.
- G780 — PR #1713 / issue #1703; merge commit `a16af04342d4dbe05c73a36699fc9b570c9eba69`.
  **Operator-observable outcome:** declared same-repository claims consistently
  use `metadata_write_branch`, including explicit stranded migration.
- G781 — PR #1711 / issue #1704; merge commit `d4bcdfcf3db347b887986ebd9beec75c57a8708c`.
  **Operator-observable outcome:** `notify supervise install --verify` can
  re-prove a completed first cycle without claiming a supervisor started when it did not.
- G782 — PR #1714 / issue #1706; merge commit `c09caab877ebaf3a5fc2c1fe6e42a4cfb6709c58`.
  **Operator-observable outcome:** bug-to-intent repair links accept their
  documented flags and expose the repair issue to implementation readers.
- G783 — PR #1715 / issue #1707; merge commit `14888e49288c1c4e826717e485fd6243ff16fcf6`.
  **Operator-observable outcome:** issue-publish summaries say what write mode
  did rather than what it would do.
- G784 — PR #1716 / issue #1708; merge commit `e26faca0c5ee4e58f71257d08f0601c2934409f6`.
  **Operator-observable outcome:** the sanctioned external-role frontend and
  wake-command fields can be updated with CAS, confirmation, and dry-run behavior.
- G785 — PR #1717 / issue #1709; merge commit `140bfc65a744ac7dbf14886a315b40f865d8001e`.
  **Operator-observable outcome:** worker completion refuses a missing
  required pasted-evidence fence unless a reasoned gap override is recorded.
- G786 — PR #1718 / issue #1712; merge commit `d9dc053dd81f53c3a8be420ee7c6798b808f4521`.
  **Operator-observable outcome:** the shell approval recognizer passes only
  the command to AST verification and supervise install carries shell policy.

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.29.0..d9dc053dd81f53c3a8be420ee7c6798b808f4521
1057923311a0819d994c5180c1a58adff1e2fd8c
a16af04342d4dbe05c73a36699fc9b570c9eba69
d4bcdfcf3db347b887986ebd9beec75c57a8708c
c09caab877ebaf3a5fc2c1fe6e42a4cfb6709c58
14888e49288c1c4e826717e485fd6243ff16fcf6
e26faca0c5ee4e58f71257d08f0601c2934409f6
140bfc65a744ac7dbf14886a315b40f865d8001e
d9dc053dd81f53c3a8be420ee7c6798b808f4521
$ git rev-list --first-parent --count v0.29.0..d9dc053dd81f53c3a8be420ee7c6798b808f4521
8
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1057923311a0819d994c5180c1a58adff1e2fd8c` | G779 / PR #1705 / issue #1699 | included |
| `a16af04342d4dbe05c73a36699fc9b570c9eba69` | G780 / PR #1713 / issue #1703 | included |
| `d4bcdfcf3db347b887986ebd9beec75c57a8708c` | G781 / PR #1711 / issue #1704 | included |
| `c09caab877ebaf3a5fc2c1fe6e42a4cfb6709c58` | G782 / PR #1714 / issue #1706 | included |
| `14888e49288c1c4e826717e485fd6243ff16fcf6` | G783 / PR #1715 / issue #1707 | included |
| `e26faca0c5ee4e58f71257d08f0601c2934409f6` | G784 / PR #1716 / issue #1708 | included |
| `140bfc65a744ac7dbf14886a315b40f865d8001e` | G785 / PR #1717 / issue #1709 | included |
| `d9dc053dd81f53c3a8be420ee7c6798b808f4521` | G786 / PR #1718 / issue #1712 | included |

## Consumer follow-ups after publication

These remain open until a GitHub Release exists; this preparation posts no
consumer comments. The table is deliberately tied to units in the inventory.

| consumer issue | linked arc unit | post-publish follow-up |
| --- | --- | --- |
| #1697 | G779 | cite v0.30.0 |
| #1658 | G780 | cite v0.30.0 |
| #1700 | G784 | cite v0.30.0 |
| #1701 | G781 | cite v0.30.0 |

## Truthfulness boundaries

- Undeclared hosts retain byte-identical claim behavior. A declared host moves
  default-branch records only through explicit `claim stranded migrate`, never
  automatically.
- `install --verify` re-proves without rewriting the artifact; intent-cli never
  loads, manages, or queries the scheduler job. On 2026-09-02, the real
  `intent-cli-dev` supervisor returned `first-cycle-verified` in one attempt
  without an artifact rewrite. That is evidence of this path, not a fleet claim.
- G786 changed only what reaches the AST verifier. The AST verifier's rules are
  unchanged; ShellCommandPolicy, G689 ledger identity, and G690 CAS are unchanged.
- `--accept-evidence-gap` records its reason. Issues whose packets contain no
  pasted-output phrases are unaffected.

## Prepare-only verification

The PR records parent absence, the intentional EN/JA mutation failure,
focused release-note guards, the full Release suite, build identities,
`git diff --check`, and CI. The diff is limited to release notes, version
policy, placeholders, and tests; it includes no tag, GitHub Release, publish,
workflow, consumer-comment, or product-source change.
