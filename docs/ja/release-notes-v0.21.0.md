# リリースノート — intent-cli v0.21.0

> **prepare-only / 未リリース。** この準備が変更するのは version state、release
> notes、readiness documentation、release guard だけです。GitHub Release や tag を
> 作成せず、package を publish せず、release automation と post-release roll を実行しません。
> code や runtime behaviour の変更もありません。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.21.0`。
operator が別途承認して release action を実行した後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.21.0 に公開されます。
直前の出荷範囲は [v0.20.0 notes](release-notes-v0.20.0.md) を参照し、earlier release notes は
link して、ここでは重複記載しません。

## feature description より先に読む preview lane

G689–G692 の surface は `preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md)
の対象外であり、1.x の間に変更または撤回される可能性があり、1.0 の compatibility commitment
には含まれません。

## 四つの unit で閉じた feedback loop

v0.21.0 は G689 から G692 まで、正確に四件の merged feature unit を含みます。この scope は
`git log v0.20.0..main --first-parent` を実行して導出しました。その range の全 commit を
post-release roll または四つの merge commit として以下で説明しています。各 PR は MERGED であり、
各 full merge commit が `main` に解決することを確認しています。

- G689 — [PR #1492](https://github.com/J-Tech-Japan/intent-system/pull/1492)、merge commit `b80d358913be6375741fe95ef93113159b2e0087`（`main` で確認）: shell approval に prompt-class と scope の二層モデルを追加し、class は dialog を認識し、scoped policy が answer 対象を命名します。bare-class による wholesale approval は構造的に不可能です。
- G690 — [PR #1494](https://github.com/J-Tech-Japan/intent-system/pull/1494)、merge commit `bf9ca28b670362c24d439c847e477dfd55598440`（`main` で確認）: design adjudication は non-overridable な hard risk floor の下で `answerable_by` により scope され、live-dialog CAS と decision-actor / executor の監査役割分離を持ちます。
- G691 — [PR #1496](https://github.com/J-Tech-Japan/intent-system/pull/1496)、merge commit `d305987bc6580e2bd137a17e1764e77bc6b219aa`（`main` で確認）: `team_mode` が delivery と authoring-only を記録します。issue-authoring team は front door だけで bootstrap でき、supervise は named not-applicable verdict を返し、delivery は byte-identical のままです。
- G692 — [PR #1498](https://github.com/J-Tech-Japan/intent-system/pull/1498)、merge commit `05b0aa575fb3fb160a6f0035de6c5aaab0aa8bd9`（`main` で確認）: authoring-only publish は design front-door audit と operator acceptance を記録し、publish gate を維持します。distinct operator lane を確認し、mode-capability matrix を共有し、named worker への delegation なしで published external handoff を記録します。

### full first-parent range の会計

first-parent range は五 commit です。上の四 merge row が feature unit を説明し、残る post-release
context も説明済みですが、release execution unit ではありません。

| account | full commit | treatment |
| --- | --- | --- |
| 0.20.1 への post-release roll | `a73fea1c54fb544645074cf0edf038158f539332` | 説明済み。context であり unit ではない |
| G689 merge | `b80d358913be6375741fe95ef93113159b2e0087` | 上記の merged unit |
| G690 merge | `bf9ca28b670362c24d439c847e477dfd55598440` | 上記の merged unit |
| G691 merge | `d305987bc6580e2bd137a17e1764e77bc6b219aa` | 上記の merged unit |
| G692 merge | `05b0aa575fb3fb160a6f0035de6c5aaab0aa8bd9` | 上記の merged unit |

### 二つの origin と minor rationale

G625 に従い、二つの origin は分けて保持します。operator-filed の [#1489 audit](https://github.com/J-Tech-Japan/intent-system/issues/1489)
は vocabulary が work より厳格だったことを見つけ、G689–G690 が end to end で答えました。operator の
authoring-only team use-case request は別の origin であり、G691–G692 が答えました。一方の origin の
measurement と shipped surface はそれぞれの origin に separately attributed のままとし、一方の
origin の measurement を他方の attribution に付け替えません。

hard risk floor は non-overridable です。#1489 の `rm`-containing compound command は
`rm-containing compound` であり、design-unanswerable by design のままです。minor rationale は checkable です: shell prompt-class scope
registry と `prompt-class list/describe`、`answerable_by` と hard risk floor を持つ canonical
`adjudicate` surface、記録された `team_mode`、mode-capability matrix は v0.20.0 にはありませんでした。
これは patch-only correction ではなく additive な preview surface です。

## 意図的な boundary

- この準備が変更するのは version policy、release notes、readiness documentation、release guard
  だけです。code と runtime behaviour は変更しません。
- feature list は G689–G692 に正確に限定し、G693 を暗黙に含めず、earlier release notes を再掲しません。
- prepare-only を保ち、この child は GitHub Release / tag を作成せず、package publish と post-roll を
  行いません。readiness が green になった後の Release 作成は operator の別 action です。

## リリース準備ゲート (Release-readiness gate)

operator が別の Release 手順を実行する前に確認します。

- `eng/version.json` が stable `0.20.0` / next `0.21.0` を記録していること。
- 上記四 PR と full merge commit が `main` に解決し、`git log v0.20.0..main --first-parent` の全
  commit が range table で説明されていること。
- preview statement が feature description より前にあり、1.0 compatibility promise を link
  していること。
- EN/JA notes が G613 terminology policy の parity を保ち、release-notes guard、bilingual count
  guard、version/readiness guard、full Release suite、`git diff --check` が green であること。
- prepare-only を保つこと。Release creation、tagging、package publication、post-release rolling
  は operator の別 action です。

## v0.21.0 の publish

この準備が merge され readiness evidence が green になった後、operator は Release 作成を明示的に
承認しなければなりません。その後に限り authorized maintainer が `v0.21.0` の GitHub Release を
作成・公開できます。downstream の release automation はこの child PR の範囲外で、post-release
version roll もここでは実行しません。
