# リリースノート — intent-cli v0.16.1

> **prepare-only / 未リリース。** post-release の placeholder を、この patch の
> operator review 用の内容に置き換えます。この preparation は GitHub Release、tag、
> package publish、version state の変更を作成せず、Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.16.1`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.16.1 に公開されます。
直前のリリース内容は [v0.16.0 notes](release-notes-v0.16.0.md) を参照し、ここでは
重複記載しません。

## v0.16.1 patch scope

この patch release は **G650 の正確に一件の merged unit** を対象にします。一覧は
`git log v0.16.0..main` を実行して導出しました。その範囲の 2 commit は post-release roll
`428eea70` と G650 の merge `53ee440e` としてすべて説明できます。G650 の merge commit が
`main` に解決することを確認しています。

- G650 — PR #1405、merge commit `53ee440e`（`main` に解決）: team-scoped の
  `guide orchestrator-thread` が再び render されます。undeclared な Setup-intake fragment を
  declare し、every guide を every session-layer mode で `--team` の有無両方について render する
  guard が undeclared fragment を見つけると fail closed します。

## v0.16.0 で壊れていたこと

v0.16.0 では `guide orchestrator-thread --domain <d> --team <t>` が undeclared-fragment error で
exit 1 になりましたが、`--team` なしの同じ invocation は正常に render しました。この失敗形は
herdr-only team の通常の invocation です。そのため v0.16.0 が案内した setup sentence（各 seat が
どの CLI と model を実行するかを尋ねる内容）が、想定した読者には到達不能でした。

## この patch が維持すること

- エラーを生んだ fragment-typing rule は正しく、弱めていません。G650 は rule を bypass せず fragment
  を declare して修正します。
- **source presence is not reachability**。source fragment があるだけでは不十分で、shipped build 上で
  guide を render して検証します。
- guard は every guide × every session-layer mode × `--team` の有無を render し、undeclared fragment が
  あれば fail closed します。

これは新しい command surface ではなく patch です。**command も flag も追加していません**。v0.16.0 が
書かれた configuration では render できなかった guide を復元します。

## リリース準備ゲート (Release-readiness gate)

v0.16.1 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.16.0` / next `0.16.1` のままであること。
- `git log v0.16.0..main` の 2 commit が上記の通りすべて説明され、PR #1405 が merge
  `53ee440e` で `main` に解決すること。
- bilingual release-notes guard と count guard、full Release suite、exact-head CI が green であること。
  EN/JA の parity は G613 terminology policy に従うこと。
- 直前の scope として [v0.16.0 notes](release-notes-v0.16.0.md) を link し、ここでは重複記載しないこと。
- prepare-only の境界を守り、operator が明示承認するまで GitHub Release、tag、package publish を作成しないこと。

## v0.16.1 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を publish できます。
この preparation PR 自体はその操作を行いません。
