# リリースノート — intent-cli v0.32.0

> **PREPARED / NOT PUBLISHED。** これは測定済み G795–G801 chain の
> prepare-only notes です。tag / GitHub Release / package publish、workflow または
> publish configuration、consumer follow-up、product source の変更は行いません。

v0.32.0 の GitHub Release はまだ存在せず、この notes は preparation evidence だけです。
matching install query は `JTechJapan.IntentSystem.Cli --version 0.32.0` です。
この preparation 後の policy は次のとおりです:

```json
{
  "stableVersion": "0.32.0",
  "nextVersion": "0.32.1"
}
```

`0.32.1` は replaceable development placeholder であり、次の real release number の決定では
ありません。EN/JA の v0.32.1 file は planning scaffold であり、changelog ではありません。
normal identity は placeholder であり **not** v0.32.0（v0.32.0 ではありません）。この
prepare-only slice は no tag、no GitHub Release、no workflow change、no product source change です。

## 独自に測定した minor justification

named product base は `2a833a976688b3139678e4954162a9c00d32d0f4` です。minor の判断は
v0.28.0 の auditable rule、**a command-route addition is a minor bump; option-level additions
do not count as command routes.** に従います。G796 は新しい role への event-kind routing、G800 は
research-delegation route を追加し、この二つの command-surface route additions が minor の測定済み
理由です。alias table と config repair、guide rendering、G801 npm dist-tag behavior は列挙しますが
routes としては **not counted** です。

merged history から route の判断を再現できます: G796 は six-kind event routing addition、G800 は
first-class research delegation route です。他の変更は command route として数えていません。

## 測定した version identities

policy roll 後の named base を clean Release build で確認しました:

```text
$ git rev-parse HEAD
2a833a976688b3139678e4954162a9c00d32d0f4
$ dotnet build IntentSystem.sln --configuration Release --no-restore; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.1-2a833a9-G801
```

この normal identity は `nextVersion` placeholder であり、**v0.32.0 ではありません**。
同じ base を explicit release property で測定しました:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.32.0; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.0-2a833a9-G801
```

published version の third identity は local policy file ではなく `release.yml` が tag から導出します:

```text
$ raw=v0.32.0; version="${raw#v}"; printf 'RAW=%s\nVERSION=%s\n' "$raw" "$version"
RAW=v0.32.0
VERSION=0.32.0
```

release workflow は `RAW` から `-p:Version=<tag>` を供給し、`eng/version.json` は local builds と
dry runs だけを管理します。この prepare-only slice は no tag（tag を作成していません）です。

## Release inventory: 正確に六つの first-parent unit

exact first-parent range から inventory を導出しました。Git は merge order で六つの commit を測定し、
各 commit に一つの operator-observable outcome を記録します:

- G795 — PR #1740 / issue #1737; merge commit `1b3c7229cfe8c8f8565034a7e2220a94ac14785b`。
  **Operator-observable outcome:** canonical Architect, Orchestrator, Builder, Reviewer, Steward の
  role values は四つの legacy aliases を受け入れ、unknown role は拒否します。
- G798 — PR #1742 / issue #1741; merge commit `09b1f4edca51f3acbbe3e901356866996f4be29f`。
  **Operator-observable outcome:** recorded role configuration は canonical normalizer 経由で
  load され、queue-state role fields は runtime semantics なしに read/display されます。
- G796 — PR #1743 / issue #1738; merge commit `67c8578090f1a53e8894aeff88abd6cd8b83ff15`。
  **Operator-observable outcome:** six event kinds は Steward または Architect に route され、opaque
  ruling payload は digest と origin の境界を保ったまま byte-identically relay されます。
- G800 — PR #1747 / issue #1745; merge commit `6e0bff220e2bf51308596c19ee258835ce509dd8`。
  **Operator-observable outcome:** Architect または Reviewer は sourced research を Orchestrator
  または Steward へ delegate でき、ruling-bearing research は judgement seat で拒否されます。
  direct research は ungated です。
- G797 — PR #1746 / issue #1739; merge commit `11457187ad0f9c2c269b80de84b0fd9ea278dfe5`。
  **Operator-observable outcome:** guides は canonical roles と Steward を説明し、retired-name
  glossary を保持し、vendor/runtime role coupling なしに installed route names を保存します。
- G801 — PR #1749 / issue #1748; merge commit `2a833a976688b3139678e4954162a9c00d32d0f4`。
  **Operator-observable outcome:** npm publish calls は stable version では `latest`、preview/rc/beta/
  alpha SemVer では non-default prerelease dist-tag を導出します。

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.31.0..2a833a976688b3139678e4954162a9c00d32d0f4
1b3c7229cfe8c8f8565034a7e2220a94ac14785b
09b1f4edca51f3acbbe3e901356866996f4be29f
67c8578090f1a53e8894aeff88abd6cd8b83ff15
6e0bff220e2bf51308596c19ee258835ce509dd8
11457187ad0f9c2c269b80de84b0fd9ea278dfe5
2a833a976688b3139678e4954162a9c00d32d0f4
$ git rev-list --first-parent --count v0.31.0..2a833a976688b3139678e4954162a9c00d32d0f4
6
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1b3c7229cfe8c8f8565034a7e2220a94ac14785b` | G795 / PR #1740 / issue #1737 | included |
| `09b1f4edca51f3acbbe3e901356866996f4be29f` | G798 / PR #1742 / issue #1741 | included |
| `67c8578090f1a53e8894aeff88abd6cd8b83ff15` | G796 / PR #1743 / issue #1738 | included |
| `6e0bff220e2bf51308596c19ee258835ce509dd8` | G800 / PR #1747 / issue #1745 | included |
| `11457187ad0f9c2c269b80de84b0fd9ea278dfe5` | G797 / PR #1746 / issue #1739 | included |
| `2a833a976688b3139678e4954162a9c00d32d0f4` | G801 / PR #1749 / issue #1748 | included |

この first-parent range はこの六つの merge commit だけで、second-parent commit の changelog ではありません。

## Alias promise and compatibility boundary

role-renaming release は existing host と互換です。legacy names `design`、`orchestration`、
`implementation`、`review` の四つは Architect、Orchestrator、Builder、Reviewer の aliases として
still work します。Existing roles configuration keeps loading、existing queue-state keeps reading and
displaying、no installed guide route changed name です。これは compatibility promise であり、new route
claim ではありません。

## Truthfulness と prepare-only boundaries

- G795 の five canonical roles と four aliases は一つの normalizer で処理し、unknown role は黙って
  persist せず refusal します。
- G798 の roles configuration は loadable のまま、queue-state `worker_role` / `review_role` は
  runtime behavior ではなく read/display fields のままです。
- G796 の ruling payload は opaque で、bytes、digest、origin を保持し、指定された relay envelope
  だけを追加できます。
- G800 の research delegation は source-bearing finding を要求し、ruling-bearing report は finding
  を rule すべき judgement seat を名前にして拒否します。Direct Architect/Reviewer research は成功し、
  gate ではありません。Visibility counts は grading なしの measurement で、size threshold、model name、
  runtime condition は使いません。
- この prepare-only slice には tag、GitHub Release、package publish、workflow/publish configuration、
  consumer follow-up、product source の変更はありません。

## Prepare-only verification

`ReleaseNotesV0320G802Tests` は EN/JA の unit/PR/issue/merge tuples を比較し、両 mirror の四つの
alias statements、三つの measured identities、exact six-commit inventory を guard し、一フィールドの
mirror mutation で意図的に fail します。`ReleasePackageMetadataTests` は policy shape と demanded
next-version placeholder を引き続き guard します。PR には各 new test の parent absence/failure actual
output、criterion-named release-policy output、`git diff --check`、focused/full Release counts、exact-head
CI を貼ります。diff は EN/JA v0.32.0 notes、v0.32.1 planning placeholders、`eng/version.json`、tests に
限定され、tag / GitHub Release / package publish / workflow または publish-config / product source change
は含みません。
