# リリースノート — intent-cli v0.16.0

> **prepare-only / 未リリース。** この準備では version state と documentation だけを
> 変更します。GitHub Release、tag、package publish、release workflow は作成・実行せず、
> Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.16.0`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.16.0 に公開されます。
直前の scope は [v0.15.0 notes](release-notes-v0.15.0.md) と
[v0.14.0 notes](release-notes-v0.14.0.md) を参照し、ここでは重複記載しません。

## feature list の前に読む preview lane

G647 の per-kind launch-recipe registry と G648 の registration-loss
corroboration は `preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md)
の対象外であり、1.x の間に変更または撤回される可能性があり、1.0 の compatibility commitment ではありません。

## v0.16.0 の内容

この minor release は **G647 と G648 の正確に二件の merged unit** を対象にします。一覧は
`git log v0.15.0..main` を実行して導出し、その範囲の 3 commit はすべて post-release roll
`e3c2e432`、G647 merge `532b01b9`、G648 merge `e3200aeb` として説明できます。両方の merge commit が
`main` に解決することを確認しています。

- G647 — PR #1398、merge commit `532b01b9`（`main` で確認）: recorded seat kind は human の現在の wish で、
  human が要求した kind switch は one step、recovery は unattended に kind を変更しません。per-kind registry は
  実測済み target launch recipe または明示的な absent notice を表示し、`topology update-kind` が command や model を
  silently 推測しないようにします。
- G648 — PR #1400、merge commit `e3200aeb`（`main` で確認）: liveness、supervision、delivery は
  `registration-lost-process-present` と genuine `lost` を区別し、corroborated state では
  `resend_permitted: true` を返し、recorded pane ごと cycle ごとに最大一件の finding を返します。kill、restart、
  automatic re-registration は行いません。

## minor release の根拠

G647 は v0.15.0 に存在しなかった per-kind recipe/update-kind surface と実測 Codex recipe registry を追加します。
G648 は registration-loss と process-presence の distinct state、および delivery/supervision の corroboration を追加します。
これは既存 v0.15.0 contract の patch-only 修正ではなく、新しい preview surface です。

## Operator principle

記録された seat kind は human operator の現在の wish です。human が要求した switch は one step で target recipe を記録し、
target が unknown なら invented default を与えず operator に尋ねます。recovery は unattended に kind を変更しません。

## 測定したことと guidance の区別

Codex recipe は universal claim ではなく実測値です。**MyIntentHost** で **2026-08-07** に
Codex **v0.144.1 / macOS** を次の bounded invocation で観測しました。

```text
herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write --ask-for-approval never --add-dir <role-work-root> [--add-dir <host-routing-root>]
```

観測した envelope fact は asymmetric でした。宣言した root の外への write は拒否されましたが、外への read は拒否されませんでした。
これはこの host と date の measured evidence であり、すべての platform/version に同じ挙動を約束するものではありません。
未実測の target kind は明示的に absent のままにし、flags や post-start answer を推測しません。

## G648 incident と fail-closed boundary

G648 incident では、herdr registration が flicker したため、healthy orchestrator が `lost` を 6 回報告しました。
old-process stop は **fail-closed** のまま保持され、process corroboration が kill/restart を防ぎ、automatic re-registration も行いませんでした。
registration と recorded-pane process の両方が absent の場合だけ genuine absence を `lost` とします。foreground process が残る
registration loss は `registration-lost-process-present` と命名し、pane ごと cycle ごとに一件だけ報告し、operator が pane を再登録できるよう
`resend_permitted: true` を維持します。

## リリース準備ゲート (Release-readiness gate)

v0.16.0 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.15.0` / next `0.16.0` を記録していること。
- PR #1398 が merge `532b01b9` で `main` に、PR #1400 が merge `e3200aeb` で `main` に解決し、
  `git log v0.15.0..main` の 3 commit が上記の通りすべて説明されること。
- preview statement が feature description より前にあり、[1.0 compatibility promise](1.0-compatibility-promise.md) は
  重複記載せず link されていること。
- bilingual release-notes guard と count guard、full Release suite、exact-head CI が green であること。Codex の measured attribution と
  G648 fail-closed incident boundary も確認すること。
- [v0.15.0 notes](release-notes-v0.15.0.md) と [v0.14.0 notes](release-notes-v0.14.0.md) は preceding scope として
  link され、ここでは重複記載しないこと。
- prepare-only の境界を守り、operator が明示承認するまで GitHub Release、tag、package publish を作成しないこと。

## v0.16.0 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を publish できます。
この preparation PR 自体はその操作を行いません。
