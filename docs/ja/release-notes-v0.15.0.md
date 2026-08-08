# リリースノート — intent-cli v0.15.0

> **prepare-only / 未リリース。** この準備では version state と documentation だけを
> 変更します。GitHub Release、tag、package publish、release workflow は作成・実行せず、
> Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.15.0`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.15.0 に公開されます。
直前の scope は [v0.14.0 notes](release-notes-v0.14.0.md) と
[v0.13.1 notes](release-notes-v0.13.1.md) を参照し、ここでは重複記載しません。

## feature list の前に読む preview lane

G644 の supervision setup discoverability と G645 の guide reachability は
`preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md) の対象外であり、
1.x の間に変更または撤回される可能性があり、1.0 の compatibility commitment ではありません。

## v0.15.0 の内容

この minor release は **G644 と G645 の正確に二件の merged unit** を対象にします。一覧は
`git log v0.14.0..main` を実行して導出し、その範囲の 6 commit はすべて、post-release roll
`96ab9947`、G644 implementation `605f377a`、G644 repair `9aa377a1`、G644 merge `0031bfb1`、
G645 implementation `d6b64785`、G645 merge `5ee849a8` として説明できます。両方の merge commit が
`main` に解決することを確認しています。

- G644 — PR #1392、merge commit `0031bfb1`（`main` で確認）: role が読む guide が supervision setup を
  名指しし、supervision cycle の記録がない team には setup を行うよう伝えます。
- G645 — PR #1394、merge commit `5ee849a8`（`main` で確認）: packet が追加する role-facing surface ごとに、
  どの guide がどの role を route するかを宣言し、未記録の declaration を
  `guide-reachability-pending` debt として報告します。

## minor release の根拠

minor bump は推測ではなく検証できます。G645 は packet の guide-reachability declaration と
`guide-reachability-pending` stall class を追加しました。どちらも v0.14.0 には存在しません。
これは既存 contract の patch-only 修正ではなく、新しい packet と closeout の surface です。

## この二件が testable にするもの

intent-cli は、feature を一つ追加するときにも intent 全体を視野に置くことを、人が覚えておくものではなく
process 内の mechanism にします。guidance は人が後から読む reference のためだけにあるのではありません。
keyword を渡された thread が guide と対話し、surface を理解し、action することが意図された path です。
reference に何かを書くだけでは completion ではありません。G644 は role が読む guide に supervision setup の
route を置き、G645 は今後の各 slice に guide route を宣言させ、未記録を debt として報告します。

## 測定したことと guidance の区別

この作業を強制した測定は具体的です。supervision loop が ship された直後、installed v0.14.0 build の
`review-next-slice-loop`、`implementation-loop`、`init-host`、`guide next` の四つの guide は
`supervise` を **0 回**しか mention しませんでした。これは discoverability gap の observed evidence であり、
deployment process が実行中だったかどうかの主張ではありません。

G644 の deployment facts は guidance です。`guide next` は cycle が記録されていないときに
`supervision-setup` を recommend でき、host-init と design-side loop の guide は deployment step を説明します。
一方で `next` は read-only のままで、background process を start・manage しません。したがって notes は guidance が
存在するだけで recorded cycle や running deployment が測定済みだとは扱いません。readiness で operator がその事実を確認します。

## boundary と意図した non-behaviour

- G644 は recorded cycle がないことを surface して setup を示しますが、background process を start・supervise しません。
- G645 は filename、keyword、guide wording から route を推測せず、guide が良いかどうかも判定しません。
- `guide-reachability-pending` は closeout debt であり merge または closeout gate ではありません。明示的な
  no-role-facing-surface declaration は silent です。

## リリース準備ゲート (Release-readiness gate)

v0.15.0 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.14.0` / next `0.15.0` を記録していること。
- PR #1392 が merge `0031bfb1` で `main` に、PR #1394 が merge `5ee849a8` で `main` に解決し、
  `git log v0.14.0..main` の 6 commit が上記の通りすべて説明されること。
- preview statement が feature description より前にあり、[1.0 compatibility promise](1.0-compatibility-promise.md) は
  重複記載せず link されていること。
- bilingual release-notes guard と count guard、full Release suite、exact-head CI が green であること。
  supervision deployment を ready と扱う前に measured-versus-guidance の区別も確認すること。
- [v0.14.0 notes](release-notes-v0.14.0.md) と [v0.13.1 notes](release-notes-v0.13.1.md) は preceding scope として
  link され、ここでは重複記載しないこと。
- prepare-only の境界を守り、operator が明示承認するまで GitHub Release、tag、package publish を作成しないこと。

## v0.15.0 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を publish できます。
この preparation PR 自体はその操作を行いません。
