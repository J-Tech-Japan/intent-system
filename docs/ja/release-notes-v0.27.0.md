# リリースノート — intent-cli v0.27.0

> **PREPARED / NOT PUBLISHED。** これは測定した v0.27.0 line の
> prepare-only release-note set です。この unit では tag、GitHub Release、
> package publish、post-release roll を行いません。

v0.27.0 の GitHub Release はまだ存在せず (no GitHub Release exists yet)、この file は preparation evidence
だけです。この prepare-only line は UNRELEASED で、no tag、no publish、
no GitHub Release、package release はありません。matching install query は
`JTechJapan.IntentSystem.Cli --version 0.27.0` です。

以前の v0.26.1 は v0.26.0 後の roll が置いた placeholder だけでした。
v0.26.1 release は選ばれず、publish されていません。この preparation は
測定した次の release line に置き換えます。

```json
{
  "stableVersion": "0.26.0",
  "nextVersion": "0.27.0"
}
```

## 測定した command-surface difference

この minor bump は `eng/version.json` からの推測ではなく、正確な prepared
functional head を clean Release build した independent measurement に基づきます。

```bash
intent-cli --version
# intent-cli 0.26.0-93f07f8-G749
dotnet build IntentSystem.sln --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.26.0-bb97548-G751
```

installed CLI は `intent-cli 0.26.0-93f07f8-G749` です。prepared functional
head `bb9754859ac8055adbd504f294145b7494668c1a` の clean Release build は
`intent-cli 0.26.0-bb97548-G751` を表示しました。この identity はその
revision から生成したものです。

programmatic sweep は 32 個すべての command group の help と、各 direct
subcommand の help を呼び出しました。installed CLI は group descriptor
32 + direct-help usage 71 = **103 usages**、prepared Release build は
32 + 72 = **104 usages** です。

| command-surface measurement | installed 0.26.0 | prepared Release build |
| --- | ---: | ---: |
| total usages | 103 | 104 |
| `notify supervise repair-cycle-history` | absent (`invalid-notification: Unknown argument 'repair-cycle-history'.`) | present |

prepared usage は次のとおりです。

```text
Usage: intent-cli notify supervise repair-cycle-history --domain <d> --team <t> [--dry-run|--write] [--format markdown|json]
```

追加は `notify supervise repair-cycle-history` の一つだけで、removal は
ありません。`automation`、`claim`、`worker` の help は installed CLI と
prepared build で byte-identical でした。`state-doctor` と
`closeout-drift-check` の usage も byte-identical でした。この測定した
operator surface が version file ではなく minor bump を監査可能にする reason です。

## v0.27.0 の内容

release inventory は、operator が観測できる outcome を持つ、正確に二つの
functional unit です。

- G750 — PR #1634; merge commit
  `b525191a24e361419b03f77e15e659110a22c395`。
  **Operator-observable outcome:** supervision cycle history を git に
  持たなくなり、100MB の cycle-history file で shared state が block
  された host も push できます。すでに file を tracking している host
  には `notify supervise repair-cycle-history` の supported migration が
  あり、file を preserve したまま delete しません。
- G751 — PR #1635; merge commit
  `bb9754859ac8055adbd504f294145b7494668c1a`。
  **Operator-observable outcome:** observation のない成功した event-mode
  wait は durable cycle record を作らず、genuine observation と interval
  safety-floor record は durable のままです。そのため running supervisor
  は空の wait ごとの event-wait record を書かず、宣言した
  one-record-per-interval に settle します。

## First-parent range と release inventory

正確な prepared-head accounting は次で測定しました。

```bash
git rev-list --first-parent --reverse v0.26.0..086344540d70a052555502971fa968aff6a252ac
git rev-list --first-parent --count v0.26.0..086344540d70a052555502971fa968aff6a252ac
# 3
```

三つすべての first-parent commit を下表で説明します。G752 row は
classification のためだけのもので、その post-v0.26.0 version roll は
release unit ではありません。

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `b525191a24e361419b03f77e15e659110a22c395` | G750 release unit; PR #1634 | included |
| `bb9754859ac8055adbd504f294145b7494668c1a` | G751 release unit; PR #1635 | included |
| `086344540d70a052555502971fa968aff6a252ac` | G752 post-v0.26.0 version roll to the 0.26.1 placeholder; not a release unit | classified only |

したがって release inventory は G750、G751 の二つだけです。G752 roll は
classification table に残り、unit として数えたり黙って落としたりしません。

## honest な三-unit supervision chain

v0.26.0 の G744 entry は reduction in write volume ではなく、bound を説明
したものでした。live file は bounded になりましたが supervisor が書く
量は減っていません。v0.26.0 に upgrade して history growth が止まると
期待した operator がその outcome を得たのは、この release の G751 まで
です。これは二つの release にまたがる一つの problem です。

- G744 は live history を bounded にしました。
- G750 は runtime-local cycle history を git から外し、すでに tracking
  していた host 向けに file を削除しない migration を提供しました。
- G751 は genuine observation と interval floor を残したまま、成功した
  no-observation event wait を durable にしないことで write rate を減らしました。

測定値は形容ではなく、source を付けた measurement です。G750 の記録では
`cycles.jsonl` は **111.5MB**、GitHub の **100MB** tracking limit に達しました。
G751 の running-supervisor measurement は change 前が **3.6 records/second**、
後が **12.00/hour** でした。最初の数値は git blockage を、後の二つは
durable-record rate の before/after を示します。

## prepare-only verification

正確な verification count は次のとおりです。
- focused release/doc/version guard: 14 passed, 0 failed, 0 skipped (14 total)。
- adjacent release/readiness guard: 51 passed, 0 failed, 0 skipped (51 total)。
- dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total)。
- Full Release suite: 5332 passed, 0 failed, 1 skipped (5333 total)。
`git diff --check` は clean です。tracked な EN/JA v0.26.0 shipped-note は
byte-identical のままです。tag、GitHub Release、package publish、post-release
roll、source runtime change はこの preparation の範囲外です。
