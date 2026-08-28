# リリースノート — intent-cli v0.26.0

> **PREPARED / NOT PUBLISHED。** これは測定した v0.26.0 line の
> prepare-only release-note set です。この unit では tag、GitHub Release、
> package publish、post-release roll を実行しません。

v0.26.0 の GitHub Release はまだ存在せず (no GitHub Release)、no tag、no publish、package release もないため、この file は preparation evidence
だけです。この prepare-only line は未リリースで、tag、publish、package release
はありません。matching install query は `JTechJapan.IntentSystem.Cli --version 0.26.0` です。

以前の 0.25.1 は v0.25.0 後に置かれた placeholder にすぎず、v0.25.1 の
release は選択も publish もされていません。この preparation では、その
placeholder を測定した次の line に置き換えます。

```json
{
  "stableVersion": "0.25.0",
  "nextVersion": "0.26.0"
}
```

## 測定した command-surface difference

この bump は `eng/version.json` から推測せず、正確な prepared head を
seat 自身で Release build して測定した結果に基づきます。

```bash
intent-cli --version
# intent-cli 0.25.0-74a1c72-G741
dotnet build IntentSystem.sln --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.25.1-a49ad93-G748
```

installed CLI は `intent-cli 0.25.0-74a1c72-G741`、prepared head
`a49ad93c36bd93d1ccc9317622d36fa01ea346b8` の Release build は
`intent-cli 0.25.1-a49ad93-G748` です。
正確な policy を metadata だけ変更した後の同じ Release build は
`intent-cli 0.26.0-a49ad93-G748` を表示します。これは final prepared identity
であり、minor bump を推測するための根拠ではありません。

測定で確認した新しい command surface は次の一つです。

| command surface | installed 0.25.0 | prepared Release build |
| --- | --- | --- |
| `notify supervise archive --domain <d> --team <t> [--live-window-days <days>] [--dry-run\|--write] [--format markdown\|json]` | absent (`Unknown argument 'archive'.`) | present |

build した usage は次のとおりです。

```text
Usage: intent-cli notify supervise archive --domain <d> --team <t> [--live-window-days <days>] [--dry-run|--write] [--format markdown|json]
```

`automation`、`claim`、`worker` の help surface は installed と prepared
build で byte-identical でした。`notify` の差分はこの新しい
`notify supervise archive` だけで、ほかの追加はありません。
`state-doctor` と `closeout-drift-check` の usage も byte-identical でした。
この一つの operator surface の測定が minor bump の根拠であり、version file
を理由として先に決めたものではありません。

## v0.26.0 の内容

release inventory は五つの functional unit だけです。各項目は operator が
観測できる outcome を記録します。G743 と G747 は v0.25.0 で shipped した
claim-transaction contract を finish、repair するものであり、新しい contract
だったと誤って記録しません。G748 は G741 detector を repair するもので、
修正前は qualifying incident が sixteen 件あっても finding が zero 回でした。

- G743 — PR #1620; merge commit `1ad68963b65a1fe4978d3a0e83d0812842a2de29`。
  **Operator-observable outcome:** real pre-commit failure を primary result
  として保ち、cleanup evidence を分けて報告します。claim state の commit
  後は teardown が best-effort かつ bounded で、cleanup warning があっても pushed claim を successful と報告します。
  v0.25.0 の commit boundary を operator がそのまま利用できます。
- G744 — PR #1621; merge commit `0e97529c64294677b41e49cd87a40920c1dd3d4e`。
  **Operator-observable outcome:** configurable な recent live window により
  cycles file を小さく保ち、古い record を period-addressable archive へ移します。
  既存の history reader は archive と live の両方を読み、live-safe な move は
  concurrent record を discard も duplicate もしません。
- G746 — PR #1626; merge commit `d112dd957826864124d4b8f0d8c1940d4145e1fe`。
  **Operator-observable outcome:** duplicate execution-unit queue row を
  closeout と state-doctor が crash せず報告します。strictly more-informative
  な duplicate だけを repair し、ambiguous な entry は competing information
  を示して安全に停止します。**Consumer report #1622:** duplicate
  `execution_unit` row により `closeout-drift-check` が duplicate-key crash
  しました。canonical command ではその state を recovery できず、reporter
  は unblock のため `.intent-cli/queue-state.json` を手動編集しました。
  新しい canonical finding/repair がその手動 recovery を置き換えます。
- G747 — PR #1627; merge commit `7e7d16e4639f22530843b19f065b5a101cf1b0b4`。
  **Operator-observable outcome:** claim transaction は実際の pre-commit cause
  を保持し、metadata から解決した remote default branch を対象にし、cleanup
  warning があっても JSON stdout を parseable に保ちます。v0.25.0 の commit
  boundary、cleanup bound、retry count、retry timing は変更しません。
- G748 — PR #1629; merge commit `a49ad93c36bd93d1ccc9317622d36fa01ea346b8`。
  **Operator-observable outcome:** delivery succeeded、configured window elapsed、
  report、artifact、durable target transition がすべて absent のとき、G741 の
  supervision finding は documented な closed recipient-state set `{idle, done}`
  を扱います。これは G741 detector が sixteen qualifying incidents で zero 回
  だった問題を repair したものです。blocked と unknown は除外し、障害中または
  観測できない seat を停止したと誤判定しません。

## First-parent range と release inventory

prepared head の accounting は次で測定しました。

```bash
git rev-list --first-parent --reverse v0.25.0..a49ad93c36bd93d1ccc9317622d36fa01ea346b8
git rev-list --first-parent --count v0.25.0..a49ad93c36bd93d1ccc9317622d36fa01ea346b8
# 6
```

六つの first-parent commit を下表ですべて分類します。G745 の row は分類用
だけです。その post-v0.25.0 version roll を落とさず、release unit には数えません。

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1ad68963b65a1fe4978d3a0e83d0812842a2de29` | G743 release unit; PR #1620 | included |
| `0e97529c64294677b41e49cd87a40920c1dd3d4e` | G744 release unit; PR #1621 | included |
| `b8f249e965cad2c3c2e19dda9dd99e726324485d` | G745 post-v0.25.0 version roll; not a release unit | classified only |
| `d112dd957826864124d4b8f0d8c1940d4145e1fe` | G746 release unit; PR #1626 | included |
| `7e7d16e4639f22530843b19f065b5a101cf1b0b4` | G747 release unit; PR #1627 | included |
| `a49ad93c36bd93d1ccc9317622d36fa01ea346b8` | G748 release unit; PR #1629 | included |

release inventory は G743、G744、G746、G747、G748 の五つだけです。
G745 の post-v0.25.0 roll は表で分類し、release unit には数えません。

## Release-prep verification

tracked な EN/JA v0.25.0 shipped-note file は変更していません。以前の
v0.25.1 DRAFT stub はこの preparation で削除し、ここで作成した v0.26.0
notes だけが unpublished line です。

Targeted release-prep docs/version guard: 40 passed, 0 failed, 0 skipped (40 total)。
Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total)。
Adjacent release/readiness suite: 59 passed, 0 failed, 0 skipped (59 total)。
Full Release suite: 5305 passed, 0 failed, 1 skipped (5306 total)。
git diff --check: clean。この unit では tag、GitHub Release、package publish、post-release roll を実行しません。

## Prepare-only boundary

この preparation が変更するのは `eng/version.json`、release-note file、EN/JA
developer-reference の readiness section、release-note/version test だけです。
source runtime、tag、Release、publish、post-release roll、shipped v0.25.0 note
file は変更しません。次の post-release roll は別の operator-owned action であり、
この preparation は real release number を別に選びません。
