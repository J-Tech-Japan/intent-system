# リリースノート — intent-cli v0.31.0

> **PREPARED / NOT PUBLISHED。** これは測定済み G788–G793 chain の prepare-only
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

named product base は `fed2bbc74449b389565b8241732fe376b7a1c421` です。installed tagged
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
G791 nested-pointer-drift classification と G793 settled-outcome/disposal
classification は列挙しますが extra route とは数えません。

## 測定した version identities

policy roll 後の named base を clean Release build で確認しました:

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

この normal identity は `nextVersion` placeholder であり、**v0.31.0 ではありません**。
同じ base に explicit property を指定した測定:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.31.0
Build succeeded.
    0 Warning(s)
    0 Error(s)
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.31.0-fed2bbc-G793
```

歴史的な G790 merge SHA `79a245c655e17ac654ac440fda31709ee38e28b8` は inventory にだけ
残り、測定した identity banner にこの stale base fragment は含まれません。

published version は local policy file ではなく `release.yml` が導出する third identity です:

```text
$ RAW=v0.31.0; VERSION="${RAW#v}"; printf 'RAW=%s\nVERSION=%s\n' "$RAW" "$VERSION"
RAW=v0.31.0
VERSION=0.31.0
```

release workflow は `RAW` から `-p:Version=<tag>` を供給し、`eng/version.json` は local builds と dry runs だけを管理します。この prepare-only slice は tag を作成していません (no tag)。

## Release inventory: 正確に六つの first-parent unit

exact first-parent range から inventory を導出しました。Git は六つの commit を測定し、各
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
- G792 — PR #1732 / issue #1730; merge commit `26f0edf85cc6371c66ede5383de6543e11acd1fb`。
  **Operator-observable outcome:** この release 自身の preparation unit として、測定済み
  v0.31.0 notes、identity banner、version-policy roll を記録します。
- G793 — PR #1733 / issue #1731; merge commit `fed2bbc74449b389565b8241732fe376b7a1c421`。
  **Operator-observable outcome:** `automation stalled-work` は自身の unit に merged PR と
  closed issue の両方がある場合だけ delegation を outstanding から外し、merge SHA と
  issue evidence 付きの settled classification を記録し、still-open row ごとに
  `notify dispose --kind applied-elsewhere` を案内します。

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

この range はこの六つの merge commit だけで、second-parent commit の changelog ではありません。

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
- G793 の settled outcome は自身の unit に対する merged linked PR と closed linked issue の
  両方を要求します。still-open row は pending のままで、空でない
  `notify dispose --kind applied-elsewhere` recommendation を持ちます。

## Prepare-only verification

`ReleaseNotesV0310DocsTests` と G794 amendment guard は EN/JA の unit/PR/issue/merge tuple と
consumer row を比較し、六つの測定済み commit を検査し、mirror の一フィールド mutation で
意図的に fail します。PR には各 new test の parent absence/failure actual output、focused
release-note validation (20 passed, 0 skipped, 0 failed)、full CLI Release validation (5665
passed, 1 skipped, 0 failed)、all-project Release validation (5995 passed, 1 skipped, 0 failed)、
三つの identity、`git diff --check`、exact-head CI を貼ります。
diff は二つの release notes と test だけに限定され、tag / GitHub Release / package publish /
workflow または publish-config / consumer-comment / version-policy / product-source change は
含みません。
