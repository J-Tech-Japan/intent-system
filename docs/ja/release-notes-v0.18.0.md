# リリースノート — intent-cli v0.18.0

> **prepare-only / 未リリース。** この変更は version state、release notes、
> readiness documentation だけを準備します。GitHub Release や tag の作成、
> package publish、release workflow の実行、post-release roll は行いません。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.18.0`。
operator が別手順を実行した後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.18.0 に公開されます。
直前の出荷範囲は [v0.17.0 notes](release-notes-v0.17.0.md) を参照し、
ここでは重複記載しません。

## feature description より先に読む preview lane

`guide bootstrap` と advisor の `bootstrap-resume` action は
`preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md)
の対象外であり、1.x の間に変更または撤回される可能性があり、1.0 の
compatibility commitment には含まれません。

## application conversation は front door

v0.18.0 は正確に一件の merged unit を含みます。一覧は
`git log v0.17.0..main` を独立に実行して導出しました。その三 commit をすべて
以下で説明し、記載した merge が `main` に解決することも確認しています。

- G664 — PR #1435、merge commit `40081137`（`main` で確認）: application-front-door
  bootstrap は一つの request を guided herdr-only team genesis に変え、最初の task を
  その team へ委譲します。

guide は次の exact trigger phrase のどちらでも開始できます。

- English: `Start this work in a herdr-only team.`
- Japanese: `herdr-only で起動して。`

rendered pass は次の六 step をこの順序で保ちます。

1. design、orchestration、implementation、review の各 seat が使う CLI と model を
   人間へ質問します。回答がなければ named gap とし、default は置きません。
2. installed per-kind recipe と link した G637 layout guide から herdr workspace、
   pane、typed-seat command を出力します。
3. operator-supplied topology を canonical topology writer で記録し、roster を
   validate / show します。
4. `notify supervise install` を出力し、人間が scheduler artifact を確認して
   register できるようにします。
5. application agent kind と inbound app monitor の有無を人間へ質問してから、
   link した G654 design-seat placement rule を適用します。CLI はどちらの回答も
   推測しません。
6. fresh task id と result nonce で最初の task を orchestration へ委譲します。
   application conversation 自身ではその task を実行しません。

rendered output の末尾は次の explicit statement です。

> **HANDOFF:** どの recorded thread が design seat になったかを明示します。
> application conversation は新しい request を受ける operator's front door のままで、
> design、orchestration、implementation、review、supervision の loop seat ではありません。

recorded topology がある場合は idempotent な `join-and-delegate` path を選びます。
workspace や記録済み seat を再作成せず、`topology-recorded-seats-missing`、
`topology-recorded-supervision-and-handoff-missing` などの partial state を命名し、
不足 step だけを出力します。`guide next` は topology が記録済みで supervision-cycle /
front-door handoff が未完了なら `bootstrap-resume` を推奨します。topology がなければ
bootstrap 未開始なので silent で、completed cycle は推奨を解除します。

## merged tree での design verification

merged head `40081137` で design は CLI を build し、この host の real data に対して
Markdown / JSON、`--team` あり / なしの guide を render しました。連続八回が exit 0
でした。rendered question は seat の CLI/model と application kind を人間へ尋ね、join
path と named partial state が存在し、最終出力は HANDOFF statement でした。さらに
`guide next` は recorded lifecycle に沿って `bootstrap-resume` を表示し、完了後に解除
しました。この検証は diff の読解だけでなく merged tree と real host data を使いました。
これにより shipped tree 上で one-keyword claim を検証しました。source audit でも、
command 内の唯一の `Process` は string field name であり、guide の背後に execution path
がないことを確認しました。

## 三 commit の derivation

次の表が `git log v0.17.0..main` の全 commit を説明します。post-release roll は
range context であり、release execution unit ではありません。

| account | commit | release-unit treatment |
|---|---|---|
| 0.17.1 への post-release roll | `c2746f26` | 説明済み。unit ではない |
| G664 implementation | `229e5522` | G664 の implementation commit |
| G664 merge | `40081137` | 一件の merged execution unit |

## 意図的な boundary

- この準備は version policy、notes、readiness documentation、release guard だけを
  変更し、product code や runtime behavior は変更しません。
- guide は question と command text を出力するだけです。intent-cli executes nothing:
  herdr の呼び出し、provider / seat の起動、OS scheduler artifact の register /
  unregister は行いません。
- application-side integration code は追加しません。application conversation は guide を
  読み、operator's front door のままです。
- 既存 per-kind recipe、G637 layout、G654 design placement、topology、
  supervision-install、delegation contract は link して compose し、ここで再定義や変更を
  しません。
- join は idempotent で partial state を保存し、recorded team を不用意に fork / recreate
  しません。

## minor release の根拠

`guide bootstrap` と advisor の `bootstrap-resume` lifecycle は v0.17.0 には存在しません
でした。frozen v0.17.0 contract の patch-only correction ではなく、新しい preview surface
です。

## リリース準備ゲート (Release-readiness gate)

operator が別の Release 手順を実行する前に確認します。

- `eng/version.json` が stable `0.17.0` / next `0.18.0` を記録していること。
- PR #1435 / merge `40081137` が `main` に解決し、`git log v0.17.0..main` の全三 commit
  が表で説明されていること。
- preview statement が feature description より前にあり、1.0 compatibility promise を
  link していること。
- bilingual release-notes / count guard、full Release suite、`git diff --check`、
  exact-head CI が green であること。
- [v0.17.0 notes](release-notes-v0.17.0.md) は preceding shipped scope への link のままで、
  内容をここに重複記載しないこと。
- prepare-only を保ち、この PR が Release / tag を作成せず、package publish と
  post-release roll を行わないこと。

## v0.18.0 の publish

Release 作成は、この準備が merge され readiness evidence が green になった後の別の
operator action です。その condition 成立後の別 action には conditional approval
`v0180-preapproved-001` が記録済みです。この approval が preparation PR を release action
に変えることはなく、この implementation PR はその操作を行いません。
