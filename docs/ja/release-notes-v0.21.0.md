# リリースノート — intent-cli v0.21.0

> **Released / stable（公開済み）。** operator が承認した release transaction により
> intent-cli v0.21.0 は公開済みです。下の evidence はこの notes とともに freeze され、
> このファイルは preparation stub ではありません。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.21.0`。
clean install の出力は `intent-cli 0.21.0-c77c92f-G691` です。公開済み Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.21.0 です。
直前の出荷範囲は [v0.20.0 notes](release-notes-v0.20.0.md) を参照し、earlier release notes は
link して、ここでは重複記載しません。

## Publication evidence (frozen)

- `v0.21.0` tag と GitHub Release は commit
  `c77c92fe8e5c9e62fc15b1ba96754b2acb35691c`（`c77c92fe`）を target にします。
- [31766364883](https://github.com/J-Tech-Japan/intent-system/actions/runs/31766364883) の release workflow は success で完了し、`NuGet package`、`Self-contained linux-x64`、`Self-contained osx-arm64`、`Self-contained win-x64` の4 job が成功しました。
- 八つの release asset（linux-x64、osx-arm64、win-x64 の各 archive と `.sha256`、NuGet package とその `.sha256`）が存在し、四つの checksum verification が pass しました。
- NuGet.org は `JTechJapan.IntentSystem.Cli` version `0.21.0` を index 済みです。
- clean install は正確に `intent-cli 0.21.0-c77c92f-G691` を出力します。

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

- この release が記録するのは version、notes、release-readiness evidence です。code と runtime
  behaviour は変更しません。
- feature list は G689–G692 に正確に限定し、G693 を暗黙に含めず、earlier release notes を再掲しません。
- 上に記録した operator release transaction は完了済みです。この post-release documentation roll
  は追加の GitHub Release / tag を作成せず、package を再 publish せず、二度目の release transaction
  を実行しません。

## Publication evidence と compatibility boundary

operator の Release action 前に確認した boundary は次のとおりです。

- stable v0.20.0 の後の additive な v0.21.0 line を release-to-be-cut としました。
- 上記四 PR と full merge commit が `main` に解決し、`git log v0.20.0..main --first-parent` の全
  commit が range table で説明されていること。
- preview statement が feature description より前にあり、1.0 compatibility promise を link
  していること。
- EN/JA notes が G613 terminology policy の parity を保ち、release-notes guard、bilingual count
  guard、version/readiness guard、full Release suite、`git diff --check` が green であること。
- Release、tag、package index evidence、四つの checksum result は上の frozen publication evidence
  に記録されています。

## 公開済み v0.21.0

operator が承認した publication は完了しており、上の evidence から参照できます。post-release roll
は development line を v0.21.1 に進めますが、この released note を変更せず、release transaction を
繰り返しません。
