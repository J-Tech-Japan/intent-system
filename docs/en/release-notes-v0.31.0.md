# Release Notes — intent-cli v0.31.0

> **PREPARED / NOT PUBLISHED.** This prepare-only note set records the measured
> G788–G793 chain. It does not create a tag or GitHub Release, publish a package,
> change a workflow or publish configuration, post a consumer comment, or change
> product source.

No GitHub Release exists for v0.31.0; these notes are preparation evidence only.
The matching install query is `JTechJapan.IntentSystem.Cli --version 0.31.0`.
The policy after this preparation is:

```json
{
  "stableVersion": "0.31.0",
  "nextVersion": "0.31.1"
}
```

`0.31.1` is a replaceable development placeholder, not a decision about the
next real release. The EN and JA v0.31.1 files are planning scaffolds, not
changelogs.

## Independently measured minor justification

The named product base is `fed2bbc74449b389565b8241732fe376b7a1c421`. The
installed tagged v0.30.0 tool was measured with an explicit release version:

```text
$ dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release --no-restore -p:Version=0.30.0 -p:IntentSystemLatestExecutionUnit=G772
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.30.0-f4b01c2-G772
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll session-layer --help
intent-cli session-layer — group help
Usage: intent-cli session-layer <subcommand> [--help]

Subcommands (run with --help for details):
- marker
- model-resolution
- set
- show
- team-mode
- topology
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll session-layer inspect --help
Command 'session-layer inspect' is not yet implemented.
EXIT:1
```

The tagged surface has no `inspect` route. The v0.28.0 release rule is the
auditable distinction: **a command-route addition is a minor bump; option-level
additions do not count as command routes.** The named base adds exactly one
command route, the read-only `session-layer inspect`; that decides v0.31.0.
G788 evidence sources and informational output, G789 guide blocks, G791's
nested-pointer-drift classification, and G793's settled-outcome/disposal
classification are listed but explicitly not counted as additional routes.

## Measured version identities

The named base was checked with a clean Release build after the policy roll:

```text
$ git rev-parse HEAD
fed2bbc74449b389565b8241732fe376b7a1c421
$ dotnet clean
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet build IntentSystem.sln --configuration Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.31.1-fed2bbc-G793
```

That normal identity is the `nextVersion` placeholder and is **not** v0.31.0.
The same base with the explicit release property was measured separately:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.31.0
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.31.0-fed2bbc-G793
```

The historical G790 merge SHA `79a245c655e17ac654ac440fda31709ee38e28b8`
remains only in the inventory; no measured identity banner contains that stale
base fragment.

Published versioning is a third identity and is derived by `release.yml`, not
by the local policy file:

```text
$ RAW=v0.31.0; VERSION="${RAW#v}"; printf 'RAW=%s\nVERSION=%s\n' "$RAW" "$VERSION"
RAW=v0.31.0
VERSION=0.31.0
```

The release workflow supplies `-p:Version=<tag>` from `RAW`; `eng/version.json`
governs local builds and dry runs only. This prepare-only slice created no tag.

## Release inventory: exactly six first-parent units

The inventory is derived from the exact first-parent range. Git measured six
commits, and every commit has one operator-observable outcome:

- G788 — PR #1723 / issue #1722; merge commit `cfdacb4a657d9a60ab82fea3faa435ff732f389f`.
  **Operator-observable outcome:** a delivered parent is cleared only when a
  matching downstream delegation, child report, or queue transition carries
  execution evidence; a true stall remains visible.
- G789 — PR #1725 / issue #1724; merge commit `9d03309a155dc5f714be8a99bb3c2234724bf589`.
  **Operator-observable outcome:** design-thread guides retain the additive
  Orca operating block and resolve mixed-kind review seats without contradiction,
  including topology fallback guidance.
- G791 — PR #1728 / issue #1727; merge commit `aa5c49f51bffa634ca7a96a08f1245e53a372904`.
  **Operator-observable outcome:** a nested pointer drift in another domain is
  classified without writing that domain's submodule when every nested checkout
  is clean.
- G790 — PR #1729 / issue #1726; merge commit `79a245c655e17ac654ac440fda31709ee38e28b8`.
  **Operator-observable outcome:** `session-layer inspect` reports recorded role
  state and an optional bounded pane tail read without focus, prompts, key sends,
  or process management.
- G792 — PR #1732 / issue #1730; merge commit `26f0edf85cc6371c66ede5383de6543e11acd1fb`.
  **Operator-observable outcome:** this release's own preparation unit records
  the measured v0.31.0 notes, identity banners, and version-policy roll.
- G793 — PR #1733 / issue #1731; merge commit `fed2bbc74449b389565b8241732fe376b7a1c421`.
  **Operator-observable outcome:** `automation stalled-work` stops reporting a
  delegation as outstanding only after its own unit has a merged PR and closed
  issue, records the settled classification with merge SHA and issue evidence,
  and names `notify dispose --kind applied-elsewhere` on every still-open row.

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.30.0..fed2bbc74449b389565b8241732fe376b7a1c421
cfdacb4a657d9a60ab82fea3faa435ff732f389f
9d03309a155dc5f714be8a99bb3c2234724bf589
aa5c49f51bffa634ca7a96a08f1245e53a372904
79a245c655e17ac654ac440fda31709ee38e28b8
26f0edf85cc6371c66ede5383de6543e11acd1fb
fed2bbc74449b389565b8241732fe376b7a1c421
$ git rev-list --first-parent --count v0.30.0..fed2bbc74449b389565b8241732fe376b7a1c421
6
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `cfdacb4a657d9a60ab82fea3faa435ff732f389f` | G788 / PR #1723 / issue #1722 | included |
| `9d03309a155dc5f714be8a99bb3c2234724bf589` | G789 / PR #1725 / issue #1724 | included |
| `aa5c49f51bffa634ca7a96a08f1245e53a372904` | G791 / PR #1728 / issue #1727 | included |
| `79a245c655e17ac654ac440fda31709ee38e28b8` | G790 / PR #1729 / issue #1726 | included |
| `26f0edf85cc6371c66ede5383de6543e11acd1fb` | G792 / PR #1732 / issue #1730 | included — this release's own preparation unit |
| `fed2bbc74449b389565b8241732fe376b7a1c421` | G793 / PR #1733 / issue #1731 | included |

The first-parent range contains exactly these six merge commits and nothing
else; the table is not a changelog of second-parent commits.

## Consumer follow-up after publication

The operator report issue remains open until a GitHub Release exists. This
prepare-only slice posts no consumer comment; after publication, design posts
the released version and closes it.

| consumer issue | linked arc unit | post-publish follow-up |
| --- | --- | --- |
| (#1721) | G788 | cite v0.31.0 and close after the consumer report |

## Truthfulness boundaries

- G788's delivered-never-executed finding checks the downstream delegation,
  child report carrying the same execution-unit token, and queue transition
  evidence. It still fires on a true stall and lists what it checked rather
  than asserting absence.
- `session-layer inspect` is read-only, resolves a target only from recorded
  topology or an explicit `--role`, has no focus default, exits 0 when the
  session layer is unavailable (exit 0), and does not answer dialogs. `notify adjudicate`
  remains the dialog path.
- The G791 host guard proceeds on another domain's nested pointer drift only
  when every nested checkout is clean; it refuses uncommitted nested content
  and writes to no other domain's submodule.
- G789's design-thread Orca block is non-normative; intent-cli neither launches
  nor manages Orca.
- G793's settled outcome requires both a merged linked PR and closed linked issue;
  still-open rows remain pending and carry the non-empty
  `notify dispose --kind applied-elsewhere` recommendation.

## Prepare-only verification

`ReleaseNotesV0310DocsTests` and the G794 amendment guards compare the EN/JA
unit/PR/issue/merge tuples and consumer row, assert the six measured commits,
and deliberately fail on a one-field mirror mutation. The PR pastes the actual
parent absence/failure output for each new test, focused release-note validation
(20 passed, 0 skipped, 0 failed), full CLI Release validation (5665 passed,
1 skipped, 0 failed), all-project Release validation (5995 passed, 1 skipped,
0 failed), all three identity outputs, `git diff --check`, and exact-head CI.
The diff is limited to the two release notes and tests; it has
no tag, GitHub Release, package publish, workflow/publish-config,
consumer-comment, version-policy, or product-source change.
