# リリースノート — intent-cli v0.14.0

> **prepare-only / 未リリース。** この準備では version state と documentation だけを
> 変更します。GitHub Release、tag、package publish、release workflow は作成・実行せず、
> Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.14.0`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.14.0 に公開されます。
直前の範囲は [v0.13.1 notes](release-notes-v0.13.1.md) と
[v0.13.0 notes](release-notes-v0.13.0.md) を参照し、ここでは重複記載しません。

## feature list の前に読む preview lane

v0.14.0 の measured recovery supervision surface は `preview-through-1.x` です。
[1.0 compatibility promise](1.0-compatibility-promise.md) の対象外であり、1.x の間に
変更または撤回される可能性があり、1.0 の compatibility commitment ではありません。

## v0.14.0 の内容

この minor release は **G641 の正確に一件の merged unit** を対象にします。一覧は
`git log v0.13.1..main` を実行して導出し、その範囲の 5 commit はすべて、post-release
roll `e08a6a7`、G641 の implementation `3330ec0`、blocker repair `de0a142`、
cadence/headroom repair `5cc8a2e`、merge `7524c305` として説明できます。G641 は PR #1388、
merge commit `7524c305` であり、`main` に解決することを確認しています。

- G641 — PR #1388、merge commit `7524c305`: 既知の stall class を一回の measured
  supervision pass で確認し、loop 自身が declared detection bound を検査し、各 stall の
  detectable / surfaced / cleared 時刻を per-stall record として残します。

## minor bump の根拠

minor bump は推測ではなく検証できます。`notify supervise` は declared-bound と
duration-record behaviour、新しい option を得ます。これは既存 command の単なる修正ではなく、
追加された surface です。

## この release が扱う問題

2026-08-06 と 07、この team の loop は異なる四つの原因で四回 silent に停止しました。
recipient が task の途中で終了し、check が wake する相手のいないまま完了し、completion が
sleeping seat に届き、durable に記録された escalation が誰も wake しなかったためです。
測定した最長 gap は約2時間15分でした。G641 は detection interval を declared かつ self-checked
な property にし、各 stall の duration を読み返せる数値にします。

## honesty と boundary

- unknown start は first observation から開始したことにせず unknown として記録します。unknown
  start に都合のよい duration は作りません。
- supervisor は直前に記録された cycle 以後の自分の absence を報告し、cycle がないことを
  healthy と表示しません。
- loop は記録済み transport で owning role を wake し、recovery evidence を record しますが、
  owed transition は取りません。agent kind も固定しません。

bounded recovery time があるからこそ、より多くの agent kind を現実的に support できます。
Copilot などの seat が停止しても、silent な無期限の human investigation にはしません。

## リリース準備ゲート (Release-readiness gate)

v0.14.0 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.13.1` / next `0.14.0` を記録していること。
- PR #1388 が merge commit `7524c305` で `main` に解決し、`git log v0.13.1..main` の 5 commit
  が G641 の三つの commit、merge、post-release roll としてすべて説明されること。
- bilingual release-notes guard と count guard、full Release suite、exact-head CI が green で
  あること。
- 直前の scope は [v0.13.1 notes](release-notes-v0.13.1.md) と
  [v0.13.0 notes](release-notes-v0.13.0.md) をリンクして参照し、ここでは重複記載しないこと。
- prepare-only の境界を守り、operator が明示承認するまで GitHub Release、tag、package publish
  を作成しないこと。

## v0.14.0 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を
publish できます。この preparation PR 自体はその操作を行いません。
