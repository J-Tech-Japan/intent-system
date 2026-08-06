# リリースノート — intent-cli v0.12.1

> **prepare-only / 未リリース。** このノートは operator review 用に準備されたものです。
> この PR は GitHub Release、tag、package、version state を作成・変更しません。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.12.1`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.12.1 に公開されます。
直前のリリース内容は [v0.12.0 notes](release-notes-v0.12.0.md) を参照し、ここでは重複記載しません。

## v0.12.1 patch scope

下記の unit 一覧は `git log v0.12.0..main` を実行して導出しました。この範囲の
8 commit は、G631 の修正系列、G632 の修正系列、post-release version roll
`e209d03` としてすべて説明できます。

- G631 — PR #1368、merge commit `4c4ef22`（`main` に存在）: すべての redirected
  child-process stream を UTF-8 で decode し、helper を `ProcessOutputEncoding` に変更しました。
  spawn site が declaration を省略すると suite を fail させる guard もあります。
- G632 — PR #1367、merge commit `77a57f2`（`main` に存在）: `worker issue-preflight` の
  target classification を宣言された `Repository:` と `Target paths:` から導出し、
  prose の言及は advisory note にしました。

これは minor ではなく patch です。どちらも command や flag を追加していません。G631 は
既存 spawn site に encoding を宣言して internal helper を rename し、G632 は既存 classification
の導出方法を変更しました。両方とも merge commit と `git log` の列挙から検証できます。

### operator impact: Windows child-stream decoding（G631）

UTF-8 ではない code page の console で child output に非 ASCII が含まれると JSON が壊れ、
すべての transport operation が同時に失敗することがありました。症状は間欠的・環境依存の
total loop stall で、別 workspace の pane title などの ambient bytes が trigger になり得ます。
v0.12.0 で案内した pane title や path の非 ASCII 回避 workaround は、現在は不要です。
将来の spawn site が UTF-8 declaration を省略すると test suite を fail させる source guard もあります。

### author impact: declaration-based preflight（G632）

child repository を target として宣言した Issue は、prose に何が書かれていても actionable のままです。
その言及は advisory note として表示されます。submodule 内を宣言した target を外側の working
directory で実行する場合は引き続き block し、読めない target declaration は prose から推測せず
fail closed します。

## リリース準備ゲート (Release-readiness gate)

v0.12.1 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.12.0` / next `0.12.1` のままであること。
- PR #1368 と #1367 が `main` の merge commit `4c4ef22` と `77a57f2` に解決し、
  `git log v0.12.0..main` の8 commit がすべて説明されること。
- bilingual release-notes guard と G634 count guard、full Release suite、exact-head CI が green であること。
- prepare-only の境界を守り、operator が明示承認するまで Release、tag、package publish を作成しないこと。

## v0.12.1 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を
publish できます。この prepare PR 自体はその操作を行いません。
