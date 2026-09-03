# リリースノート — intent-cli v0.31.0

> **PREPARED / NOT PUBLISHED。** これは測定済み G788–G791 chain の prepare-only
> notes です。tag / GitHub Release / package publish、workflow または publish
> configuration、consumer comment、product source の変更は行いません。

v0.31.0 の GitHub Release はまだ存在せず、この notes は preparation evidence だけです。
matching install query は `JTechJapan.IntentSystem.Cli --version 0.31.0` です。
この preparation 後の policy は次のとおりです:

```json
{
  "stableVersion": "0.31.0",
  "nextVersion": "0.31.1"
}
```

`0.31.1` は replaceable development placeholder であり、次の real release number の決定では
ありません。EN/JA の v0.31.1 file は planning scaffold であり、changelog ではありません。

## 独自に測定した minor justification

named product base は `79a245c655e17ac654ac440fda31709ee38e28b8` です。installed tagged
v0.30.0 tool を explicit release version で測定しました:

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

この tagged surface には `inspect` route がありません。v0.28.0 の auditable rule は
**a command-route addition is a minor bump; option-level additions do not count as command
routes.** です。named base が read-only `session-layer inspect` command route を一つ追加した
ため v0.31.0 と判断します。G788 evidence source/informational output、G789 guide block、
G791 nested-pointer-drift classification は列挙しますが extra route とは数えません。

## 測定した version identities

policy roll 後の named base を clean Release build で確認しました:

```text
$ git rev-parse HEAD
79a245c655e17ac654ac440fda31709ee38e28b8
$ dotnet clean
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet build IntentSystem.sln --configuration Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.31.1-79a245c-G791
```

この normal identity は `nextVersion` placeholder であり、**v0.31.0 ではありません**。
同じ base に explicit property を指定した測定:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.31.0
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.31.0-79a245c-G791
```

published version は local policy file ではなく `release.yml` が導出する third identity です:

```text
$ RAW=v0.31.0; VERSION="${RAW#v}"; printf 'RAW=%s\nVERSION=%s\n' "$RAW" "$VERSION"
RAW=v0.31.0
VERSION=0.31.0
```

release workflow は `RAW` から `-p:Version=<tag>` を供給し、`eng/version.json` は local builds と dry runs だけを管理します。この prepare-only slice は tag を作成していません (no tag)。

## Release inventory: 正確に四つの first-parent unit

exact first-parent range から inventory を導出しました。Git は四つの commit を測定し、各
commit に operator-observable outcome を一つ記録します:

- G788 — PR #1723 / issue #1722; merge commit `cfdacb4a657d9a60ab82fea3faa435ff732f389f`。
  **Operator-observable outcome:** matching downstream delegation、child report、または
  queue transition が execution evidence を持つ場合だけ delivered parent を clear し、
  true stall は見え続けます。
- G789 — PR #1725 / issue #1724; merge commit `9d03309a155dc5f714be8a99bb3c2234724bf589`。
  **Operator-observable outcome:** design-thread guide は additive Orca operating block を
  保ち、mixed-kind review seat を矛盾なく解決し、topology fallback guidance も含みます。
- G791 — PR #1728 / issue #1727; merge commit `aa5c49f51bffa634ca7a96a08f1245e53a372904`。
  **Operator-observable outcome:** すべての nested checkout が clean のとき、別 domain の
  nested pointer drift をその domain の submodule に書き込まず分類します。
- G790 — PR #1729 / issue #1726; merge commit `79a245c655e17ac654ac440fda31709ee38e28b8`。
  **Operator-observable outcome:** `session-layer inspect` は recorded role state と optional
  bounded pane tail を focus、prompt、key send、process management なしで報告します。

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.30.0..79a245c655e17ac654ac440fda31709ee38e28b8
cfdacb4a657d9a60ab82fea3faa435ff732f389f
9d03309a155dc5f714be8a99bb3c2234724bf589
aa5c49f51bffa634ca7a96a08f1245e53a372904
79a245c655e17ac654ac440fda31709ee38e28b8
$ git rev-list --first-parent --count v0.30.0..79a245c655e17ac654ac440fda31709ee38e28b8
4
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `cfdacb4a657d9a60ab82fea3faa435ff732f389f` | G788 / PR #1723 / issue #1722 | included |
| `9d03309a155dc5f714be8a99bb3c2234724bf589` | G789 / PR #1725 / issue #1724 | included |
| `aa5c49f51bffa634ca7a96a08f1245e53a372904` | G791 / PR #1728 / issue #1727 | included |
| `79a245c655e17ac654ac440fda31709ee38e28b8` | G790 / PR #1729 / issue #1726 | included |

この range はこの四つの merge commit だけで、second-parent commit の changelog ではありません。

## Publish 後の consumer follow-up

GitHub Release が存在するまで operator report issue は open のままです。この prepare-only
slice は consumer comment を投稿しません。publish 後に design が released version を投稿して
close します。

| consumer issue | linked arc unit | post-publish follow-up |
| --- | --- | --- |
| (#1721) | G788 | cite v0.31.0 and close after the consumer report |

## Truthfulness boundaries

- G788 の delivered-never-executed finding は downstream delegation、同じ execution-unit token
  を持つ child report、queue transition を checked evidence として数えます。true stall では
  発火し続け、absence を断定せず checked list を示します。
- `session-layer inspect` は read-only で、recorded topology または明示的 `--role` からだけ
  target を解決し、focus default を持たず、session layer unavailable でも exit 0 です (exit 0)。dialog
  には回答せず、その path は `notify adjudicate` のままです。
- G791 host guard は別 domain の nested pointer drift に対して、すべての nested checkout が
  clean の場合だけ進みます。uncommitted nested content は拒否し、他 domain の submodule へ
  書き込みません。
- G789 design-thread guide の Orca block は non-normative であり、intent-cli は Orca を
  launch も manage もしません。

## Prepare-only verification

`ReleaseNotesV0310DocsTests` は EN/JA の unit/PR/issue/merge tuple と consumer row を比較し、
mirror の一フィールド mutation で意図的に fail します。PR には各 new test の parent
absence/failure actual output、focused release-note validation (13 passed)、full CLI Release
validation (5655 passed, 1 skipped, 0 failed)、all-project Release validation (5985 passed,
1 skipped, 0 failed)、三つの identity、`git diff --check`、exact-head CI を貼っています。diff は
release notes、version policy、placeholder、developer reference readiness、test に限定され、
tag / GitHub Release / package publish / workflow または publish-config / consumer-comment /
product-source change は含みません。
