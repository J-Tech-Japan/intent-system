# リリースノート — intent-cli v0.13.1

> **prepare-only / 未リリース。** post-release の stub を v0.13.1 の
> 準備内容に置き換えます。この PR は GitHub Release、tag、package publish、
> release workflow を作成・実行せず、Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.13.1`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.13.1 に公開されます。
直前のリリースは [v0.13.0 notes](release-notes-v0.13.0.md) を参照し、ここでは
重複記載しません。

## v0.13.1 の内容

この patch release は **G640 の正確に一件の merged unit** だけを対象にします。
一覧は `git log v0.13.0..main` を実行して導出し、その範囲の 4 commit はすべて、
G640 の実装 commit `a69430f` と `3d9b793`、merge commit `b206075`、post-release
version roll `9d1705d` として説明できます。

- G640 — PR #1384、merge commit `b206075`（`main` に存在）: open な pending delegation
  に一致しない task id の report を拒否せず advisory とともに配信します。

## v0.13.0 で壊れたこと

v0.13.0 では、task id が open な pending delegation に一致しない report を
`delivered: false`、`cause: unknown-task-id` で拒否していました。待っている人には
visible error ではなく沈黙として見えました。delegation ではなく escalation に回答する
role（この host では design thread）は pending record を持たないため、その reporting
channel が完全に閉じていました。同じ拒否は unsolicited report、correction、out-of-band
answer も失わせました。これは recipient が依頼を知らなかった情報を運ぶ message でした。

## 保持されること

- unmatched report は advisory とともに配信しますが、pending record を create も resolve
  もしません。
- 同じ work を指す二つの conflicting identifier に対する refusal は引き続き発火します。
- matching task id は従来どおり pending record を resolve します。

これは新しい command surface ではなく patch です。**command も flag も追加しません**。
既存の report path の一つの拒否を狭めつつ、state mutation の保護は維持します。

## upgrade advice

v0.13.0 を使用している場合は v0.13.1 に upgrade してください。upgrade までの間に
blocked report を送る場合は、既存の `intent-cli notify escalate` path を使い、拒否される
report path に頼らず design boundary へ届けてください。

## リリース準備ゲート (Release-readiness gate)

v0.13.1 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.13.0` / next `0.13.1` のままであること。
- PR #1384 が merge commit `b206075` で `main` に解決し、
  `git log v0.13.0..main` の 4 commit がすべて説明されること。
- bilingual release-notes guard と count guard、full Release suite、exact-head CI が
  green であること。
- prepare-only の境界を守り、operator が明示承認するまで GitHub Release、tag、package
  publish を作成しないこと。

## v0.13.1 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package
を publish できます。この preparation PR 自体はその操作を行いません。
