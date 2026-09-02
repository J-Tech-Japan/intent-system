# リリースノート — intent-cli v0.30.0

> **PREPARED / NOT PUBLISHED。** これは測定済み G779–G786 arc の prepare-only
> release-note set です。tag / GitHub Release / package publish、workflow trigger
> または publish configuration、consumer comment、product source の変更は行いません。

v0.30.0 の GitHub Release はまだ存在せず、この notes は preparation evidence
だけです。matching install query は
`JTechJapan.IntentSystem.Cli --version 0.30.0` です。

この preparation 後の policy は次のとおりです:

```json
{
  "stableVersion": "0.30.0",
  "nextVersion": "0.30.1"
}
```

`0.30.1` は replaceable development placeholder であり、次の real release number
の決定ではありません。EN/JA の v0.30.1 file は planning scaffold であり、changelog
ではありません。

## same-repo の claim target (G780)

claim は既存の same-repository topology declaration を尊重するようになりました。呼び出し元
checkout の `.intent-cli/config.toml` に `same_repo_topology = true` と空でない
`metadata_write_branch` の両方がある場合、claim acquire、release、takeover、verify、worker
store probe はすべて `refs/heads/<metadata_write_branch>` を使います。この declaration がない
host は従来どおり G747 の remote default branch を使います。

resolver は fail closed です。metadata write branch が存在しない、または unsafe な場合は
error であり、remote default/current checkout branch へ fallback する許可にはなりません。
`claim stranded` は reverse G763 migration direction も扱います。declared host では remote
default 上の record を `metadata_write_branch` へ transactionally に migrate し、dry-run、
receipt verification、変更されない source branch を保ちます。

G779 の rejected-push classification と fields はどちらの target でも維持されます。このため、
unprotected な `intent-metadata` branch は claim を受け入れ、same-repo declaration がない場合の
protected `main` は正直な `push-rejected` result を返します。

## 独自に測定した minor justification

named product base revision は
`d9dc053dd81f53c3a8be420ee7c6798b808f4521` です。G780 が minor を決める contract
change です。declared same-repository host では canonical claim location が
`metadata_write_branch` に移り、protected product branch の host を有効にします。
undeclared host の default behavior を維持しつつ externally observable な location
を変えるため、patch correction より広い変更です。

v0.28.0 release rule の auditable distinction はそのままです: **a command-route
addition is a minor bump; option-level additions do not count as command
routes.** この arc の `--verify`、`--accept-evidence-gap`、`--shell-policy` option と
G784 の `update-field` value は new command route として明示的に数えません。minor
decision は option-level addition ではなく G780 の claim-location contract change です。

G781 を release fact とする前に tagged v0.29.0 tool を測定しました:

```text
$ intent-cli --version
intent-cli 0.29.0-8d019f8-G772
$ intent-cli notify supervise install
invalid-supervision-install: --domain, --team, and --owner-role are required safe identity values.
Usage: intent-cli notify supervise install --domain <d> --team <t> --repo <owner/repo> --owner-role <role> --bound <seconds> --interval <seconds> [--startup-bound <seconds>; default 30] [--persistence persistent] [--event-mode] [--platform macos|windows|linux] [--output <path>] [--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]
```

この actual tagged usage には `--verify` がありません。named-base head build は新しい
re-proof path を表示します:

```text
Usage: intent-cli notify supervise install --domain <d> --team <t> --repo <owner/repo> --owner-role <role> --bound <seconds> --interval <seconds> [--startup-bound <seconds>; default 120 for --write, 1 for --verify] [--persistence persistent] [--event-mode] [--pre-approve <agent-kind>:<prompt-class>]... [--pre-escalate <agent-kind>:<prompt-class>]... [--shell-policy <json>]... [--platform macos|windows|linux] [--output <path>] [--routing-root <host-root>] [--verify|--dry-run|--write] [--format markdown|json]
```

## 測定した version identities

product base は `d9dc053dd81f53c3a8be420ee7c6798b808f4521` のまま、prepare-only
policy を roll しました。`dotnet clean` 後、rolled policy による normal clean Release
build を再測定しました:

```text
$ dotnet build IntentSystem.sln --configuration Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.30.1-d9dc053-G772
```

この normal identity は `nextVersion` placeholder であり、**v0.30.0 ではありません**。
同じ named product base が v0.30.0 を測定するのは explicit override の場合だけです:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.30.0
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.30.0-d9dc053-G772
```

published versioning は `eng/version.json` ではなく `release.yml` が別に導出します。
release event では release tag から `RAW` を取り、`VERSION="${RAW#v}"` を設定します。
measured tag transformation は次のとおりです:

```text
$ RAW=v0.30.0; VERSION="${RAW#v}"; printf 'RAW=%s\nVERSION=%s\n' "$RAW" "$VERSION"
RAW=v0.30.0
VERSION=0.30.0
```

したがって published v0.30.0 は `v0.30.0` tag から導出され、`eng/version.json` は
local builds と dry runs だけを扱います。この preparation は tag を作成していません。

## Release inventory: 正確に八 units

exact first-parent range は
`v0.29.0..d9dc053dd81f53c3a8be420ee7c6798b808f4521` です。git は八 commit を測定し、
すべてを operator-observable outcome とともに記録します。

- G779 — PR #1705 / issue #1699; merge commit `1057923311a0819d994c5180c1a58adff1e2fd8c`。
  **Operator-observable outcome:** rejected claim push は unrelated remote advance ではなく
  real rejected-push cause を報告します。
- G780 — PR #1713 / issue #1703; merge commit `a16af04342d4dbe05c73a36699fc9b570c9eba69`。
  **Operator-observable outcome:** declared same-repository claim は explicit stranded
  migration を含めて一貫して `metadata_write_branch` を使います。
- G781 — PR #1711 / issue #1704; merge commit `d4bcdfcf3db347b887986ebd9beec75c57a8708c`。
  **Operator-observable outcome:** `notify supervise install --verify` は、存在しない
  supervisor start を claim せず completed first cycle を re-prove できます。
- G782 — PR #1714 / issue #1706; merge commit `c09caab877ebaf3a5fc2c1fe6e42a4cfb6709c58`。
  **Operator-observable outcome:** bug-to-intent repair link は documented flag を受け入れ、
  implementation reader に repair issue を表示します。
- G783 — PR #1715 / issue #1707; merge commit `14888e49288c1c4e826717e485fd6243ff16fcf6`。
  **Operator-observable outcome:** issue-publish summary は write mode が行ったことを
  would-do ではなく報告します。
- G784 — PR #1716 / issue #1708; merge commit `e26faca0c5ee4e58f71257d08f0601c2934409f6`。
  **Operator-observable outcome:** sanctioned external-role frontend と wake-command field は
  CAS / confirmation / dry-run behavior 付きで update できます。
- G785 — PR #1717 / issue #1709; merge commit `140bfc65a744ac7dbf14886a315b40f865d8001e`。
  **Operator-observable outcome:** required pasted-evidence fence がない worker completion は
  reasoned gap override を記録しない限り拒否されます。
- G786 — PR #1718 / issue #1712; merge commit `d9dc053dd81f53c3a8be420ee7c6798b808f4521`。
  **Operator-observable outcome:** shell approval recognizer は command だけを AST verifier
  へ渡し、supervise install は shell policy を carry します。

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

## Publish 後の consumer follow-ups

GitHub Release が存在するまでこれらは open のままで、この preparation は consumer
comment を投稿しません。table は inventory 内の unit に意図的に結び付けています。

| consumer issue | linked arc unit | post-publish follow-up |
| --- | --- | --- |
| #1697 | G779 | cite v0.30.0 |
| #1658 | G780 | cite v0.30.0 |
| #1700 | G784 | cite v0.30.0 |
| #1701 | G781 | cite v0.30.0 |

## Truthfulness boundaries

- undeclared host は byte-identical claim behavior を維持します。declared host が
  default-branch record を移すのは explicit `claim stranded migrate` のみで、automatic
  には行いません。
- `install --verify` は artifact を rewrite せず re-prove します。intent-cli は scheduler
  job を never loads, manages, or queries します。2026-09-02 に real `intent-cli-dev`
  supervisor は一 attempt で artifact rewrite なしに `first-cycle-verified` を返しました。
  これは path の evidence であり、fleet claim ではありません。
- G786 は AST verifier に届くものだけを変更しました。AST verifier の rules は unchanged
  であり、ShellCommandPolicy、G689 ledger identity、G690 CAS も unchanged です。
- `--accept-evidence-gap` は reason を記録します。packet に pasted-output phrase がない
  issue は unaffected です。

## Prepare-only verification

PR には parent absence、意図的な EN/JA mutation failure、focused release-note guard、
full Release suite、build identities、`git diff --check`、CI の evidence を記録します。
diff は release notes、version policy、placeholder、test に限定され、tag / GitHub Release /
publish / workflow / consumer-comment / product-source change を含みません。
