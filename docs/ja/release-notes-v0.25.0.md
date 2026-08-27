# リリースノート — intent-cli v0.25.0

> **PREPARED / NOT PUBLISHED。** これは測定した v0.25.0 line の
> prepare-only release-note set です。この unit では tag、GitHub Release、
> package publish、post-release roll を実行しません。

v0.25.0 の GitHub Release はまだ存在せず、この file は preparation evidence
この line は未リリースで、no tag、no publish、no package release です。
matching install query は `JTechJapan.IntentSystem.Cli --version 0.25.0` です。
v0.25.0 の GitHub Release はまだ存在せず (no GitHub Release)、この file は preparation evidence だけです。

以前の 0.24.1 は v0.24.0 後に置かれた placeholder にすぎません。
v0.24.1 の release は選択も publish もされていません。その placeholder を、
測定した次の line に置き換えます。

```json
{
  "stableVersion": "0.24.0",
  "nextVersion": "0.25.0"
}
```

この理由は prepared functional head の測定で確認できます。
5c4af5d88ddcfa47335bad4df56ad3e40dae9140 の Release build は
intent-cli 0.24.1-5c4af5d-G741 を表示し、installed baseline は
intent-cli 0.24.0-df472fe-G737 を表示しました。prepared build には
二つの command option が増えたため、minor-version policy を適用します。

## 測定した command-surface difference

比較には installed CLI と、正確な prepared head を独自に build した結果を
使いました。

```bash
intent-cli --version
# intent-cli 0.24.0-df472fe-G737
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.24.1-5c4af5d-G741
```

この minor bump を支える新しい surface は次の全てです。

| command surface | installed 0.24.0 | prepared Release build |
| --- | --- | --- |
| session-layer topology record --model <text> | absent | present |
| session-layer topology record --reasoning-effort <text> | absent | present |
| notify supervise --delegation-execution-window-seconds <seconds> | absent | present; default 300 |

build した usage は次のとおりです。

```text
intent-cli session-layer topology record ... [--model <text>] [--reasoning-effort <text>]
intent-cli notify supervise ... [--delegation-execution-window-seconds <seconds>; default 300]
```

model と reasoning_effort は optional な free-form operator declaration です。
enumerated model list や measurement ではありません。supervision option は
表示された default を持つ bounded execution window です。

## v0.25.0 の内容

release inventory は三つの functional unit だけです。各項目は operator が
観測できる outcome を記録します。

- G738 — PR #1609; merge commit f0a30f08de6281b34b6fd4a5e8732243ad176053。
  **Operator-observable outcome:** claim state が commit、push された後の
  teardown は best-effort かつ bounded です。commit 済み claim は teardown
  で fail や hang せず、Windows user は cleanup 待ちを避けるため command を
  background にする必要がありません。
- G739 — PR #1611; merge commit f0ea90fd3df65de3f1b95bd38f6f8c79b011d171。
  **Operator-observable outcome:** topology show と validate は optional
  な model と reasoning-effort declaration を、absence も含めて render します。
  誰がこの work をしたかは recorded topology から答えられ、値は measurement
  ではなく operator declaration です。
- G741 — PR #1614; merge commit 5c4af5d88ddcfa47335bad4df56ad3e40dae9140。
  **Operator-observable outcome:** delivery succeeded、recipient idle、
  configured window elapsed、canonical report absent、expected artifact absent、
  durable target-entity transition absent の全条件がそろったときだけ、
  delivered delegation が never observably starts したことを finding として
  surface します。slow-but-started は finding ではなく、classifier は observe と
  report だけを行い、seat への prompt、restart、mutation は行いません。六つの
  motivating incident をこの wording に反映しましたが、seat は名指ししません。

## First-parent range と release inventory

prepared head の accounting は次で測定しました。

```bash
git rev-list --first-parent --reverse v0.24.0..5c4af5d88ddcfa47335bad4df56ad3e40dae9140
git rev-list --first-parent --count v0.24.0..5c4af5d88ddcfa47335bad4df56ad3e40dae9140
# 4
```

四つの first-parent commit を下表ですべて分類します。G740 の row は分類用
だけです。その version roll を落とさず、release unit には数えません。

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| f0a30f08de6281b34b6fd4a5e8732243ad176053 | G738 release unit; PR #1609 | included |
| f0ea90fd3df65de3f1b95bd38f6f8c79b011d171 | G739 release unit; PR #1611 | included |
| 8bcab9766412e3c946f3299274f969277135eb03 | G740 post-release version roll to the 0.24.1 placeholder; not a release unit | classified only |
| 5c4af5d88ddcfa47335bad4df56ad3e40dae9140 | G741 release unit; PR #1614 | included |

従って release inventory は G738、G739、G741 の三つだけです。

## Release-prep verification

v0.24.0 の shipped baseline は intent-cli 0.24.0-df472fe-G737 で表され、
tracked な EN/JA release-notes-v0.24.0.md は変更していません。以前の
v0.24.1 DRAFT stub はこの preparation で削除し、新しい EN/JA v0.25.0 notes
だけを unpublished line として作成します。

最終 focused documentation/version guard: 40 passed, 0 failed, 0 skipped (40 total)。
Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total)。
Adjacent G739 topology + G741 supervision tests: 14 passed, 0 failed, 0 skipped (14 total)。
Full Release suite: 5261 passed, 0 failed, 1 skipped (5262 total)。
git diff --check: clean。
installed G725 detector command `intent-cli automation stalled-work --domain intent-cli --repo J-Tech-Japan/intent-system --format json` は checkout commit 5c4af5d88ddcfa47335bad4df56ad3e40dae9140 から実行し、origin/main も同じ commit でした。結果は `stalled: true`、`version-roll-required`（released/expected stable 0.24.0、expected next 0.24.1）で、この preparation が未 merge の間は silent proof ではありません。
Host-duty request: orchestration は PR merge 後、final PR head の synced main checkout から同じ installed read-only detector を再実行し、silent result と checkout commit を記録してください。この child は host repository に入りません。
この unit が変更するのは eng/version.json、release-note file、EN/JA developer-reference の readiness section、release-note/version test だけです。

## Prepare-only boundary

この preparation では tag、GitHub Release、package publish、workflow change、
credential action、post-release roll、source runtime change を行いません。
実際の release 後の次の roll は別の operator-owned action であり、古い
0.24.1 placeholder が real release number を選ぶことはありません。
