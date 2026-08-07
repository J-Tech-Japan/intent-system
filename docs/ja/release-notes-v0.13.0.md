# リリースノート — intent-cli v0.13.0

> **prepare-only / 未リリース。** この準備では version state と documentation だけを
> 変更します。GitHub Release、tag、package publish、release workflow は作成・実行せず、
> Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.13.0`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.13.0 に公開されます。
直前の範囲は [v0.12.1 notes](release-notes-v0.12.1.md) と
[v0.12.0 notes](release-notes-v0.12.0.md) を参照し、ここでは重複記載しません。

## feature list の前に読む preview lane

v0.13.0 のすべての surface は `preview-through-1.x` です。
[1.0 compatibility promise](1.0-compatibility-promise.md) の対象外であり、1.x の間に
変更または撤回される可能性があり、1.0 の compatibility commitment ではありません。
freeze 後にこの preview lane が実際の surface に適用される最初の Release です。

## v0.13.0 の内容

この minor release は G636、G629、G630、G637、G638 の正確に五件の unit を対象にします。
一覧は `git log v0.12.1..main` を実行して導出し、その範囲の 16 commit をすべて説明しています。
post-release roll は `ca24b94` です。G636 の系列は `75e7a3a`、`8b4d0c1`、`d7cc4a5`、
`844e3e7`、`c58aa9b`、merge `861f1978`、G629 は `140b520` と merge `98e8805`、G630 は
`8ee090d` と merge `fa39c857`、G637 は `aa86ba8` と merge `26c2b465`、G638 は `2bd744a`、
`1d0f81e`、merge `b45f675` です。

- G636 — PR #1372、merge commit `861f1978`（`main` に存在）: launch recipe が post-start interaction を記録し、Copilot permission dialog の既定値が unbounded answer であることを扱います。
- G629 — PR #1374、merge commit `98e8805`（`main` に存在）: dispatch を永続状態にし、pending record と `notify status` の live / settled / lost を提供します。
- G630 — PR #1376、merge commit `fa39c857`（`main` に存在）: `notify supervise` が healthy seat では黙り、recovery と loss を確認し、re-dispatch を opt-in にします。
- G637 — PR #1378、merge commit `26c2b465`（`main` に存在）: workspace layout convention と `guide workspace-layout` で記録済み topology を再現できます。
- G638 — PR #1380、merge commit `b45f675`（`main` に存在）: `automation ci-wait`、`ci-all-green-not-transitioned` stall class、recipient warning で exact-head wait を永続化します。

## minor bump の根拠

minor bump は推測ではなく検証できます。`notify status`、`notify supervise`、
`automation ci-wait`、`guide workspace-layout` は v0.12.1 には存在せず、この line で初めて
追加されました。これらの surface はすべて上記の preview lane に属します。

## 新しい surface が意図的にしないこと

- `notify status` は永続化された delegation state を読むだけで、process には作用しません。
- `notify supervise` は healthy seat では黙り、re-dispatch を既定で off にします。沈黙だけから recovery action を作りません。
- `automation ci-wait` は exact-head wait を記録しますが、polling や background timer を開始しません。
- `guide workspace-layout` は必要な command と convention を表示するだけで、command を実行しません。

## 運用上の目的と operator への開示

2026-08-06、このチームの loop は異なる原因で三回 silent に停止しました。recipient process が task の途中で終了し、
CI run が wake する相手のいないまま完了し、completion report が sleeping seat に届いたためです。五件の unit はこれらの
state を可視化し、delegation、supervision、CI-wait、report の記録済み経路で recovery state を明示します。観測された
stall のうち二つは、記録された workflow から recovery できるようになりました。

G636 には、見落としてはいけない authority disclosure があります。正しい command line で seat を起動しても、operator
が許可していない authority を保持する可能性があります。agent の startup dialog は既定で全 permissions を有効にするためです。
launch recipe は、宣言した envelope を保つ回答を記録するようになりました。

## リリース準備ゲート (Release-readiness gate)

v0.13.0 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.12.1` / next `0.13.0` を記録していること。
- PR #1372、#1374、#1376、#1378、#1380 が `main` の merge commit `861f1978`、`98e8805`、
  `fa39c857`、`26c2b465`、`b45f675` に解決し、`git log v0.12.1..main` の 16 commit がすべて説明されること。
- bilingual release-notes guard と G634 count guard、full Release suite、exact-head CI が green であること。
- prepare-only の境界を守り、operator が明示承認するまで GitHub Release、tag、package publish を作成しないこと。

## v0.13.0 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を publish できます。
この preparation PR 自体はその操作を行いません。
