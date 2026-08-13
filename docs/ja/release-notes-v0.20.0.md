# リリースノート — intent-cli v0.20.0

> **prepare-only / 未リリース。** この PR は version state、release notes、readiness
> documentation、guard だけを準備します。GitHub Release や tag の作成、package
> publish、release automation の実行、post-release roll は行いません。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.20.0`。
operator が別途承認して release action を実行した後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.20.0 に公開されます。
直前の出荷範囲は [v0.19.0 notes](release-notes-v0.19.0.md) を参照し、ここでは重複記載しません。

## feature description より先に読む preview lane

G678–G686 の surface は `preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md)
の対象外であり、1.x の間に変更または撤回される可能性があり、1.0 の compatibility
commitment には含まれません。

## day-scale で閉じた feedback loop

v0.20.0 は G678 から G686 まで、正確に九件の merged feature unit を含みます。この scope は
`git log v0.19.0..main --first-parent` を実行して導出しました。その range の全 commit を
post-release roll または九件の merge commit として以下で説明しています。各 PR は MERGED であり、
各 full merge commit が `main` に解決することを確認しています。

- G678 — [PR #1468](https://github.com/J-Tech-Japan/intent-system/pull/1468)、merge commit `3671ba062cd1a4e4b54d634e7160da381fdd3ceb`（`main` で確認）: per-lane `operator-merge` の landing authority を visible / patient にし、その lane を `intent-cli` の path が merge しません。
- G679 — [PR #1471](https://github.com/J-Tech-Japan/intent-system/pull/1471)、merge commit `42789d6d8b1e4ac0d7133a277decd6ebcddeaf6b`（`main` で確認）: git push-CAS claim を shared claim verification の multi-user work ownership として使います。
- G680 — [PR #1473](https://github.com/J-Tech-Japan/intent-system/pull/1473)、merge commit `46836e83098c6dd1192beeffe7daf6a32c529d89`（`main` で確認）: packet draft、queue seed、publish flow、worker next-action、next-slice が claim verification を共有し、claim-before-scaffold の numbering を行います。
- G681 — [PR #1475](https://github.com/J-Tech-Japan/intent-system/pull/1475)、merge commit `7540932f61ee34cb2941405d13964b5aa90affb1`（`main` で確認）: event stream を domain と team で scope し、legacy team-file fallback を読み取り可能なまま保ちます。
- G682 — [PR #1477](https://github.com/J-Tech-Japan/intent-system/pull/1477)、merge commit `bbcc360255ecc01fefbf30f4ea06687b763208e6`（`main` で確認）: prompt-class producer が無いとき pre-approval / pre-escalation record が inapplicability を明示し、coverage が実在するまで fail closed にします。
- G683 — [PR #1479](https://github.com/J-Tech-Japan/intent-system/pull/1479)、merge commit `358d8b83b3ea53ae62a5f8323a9b2a26db34235e`（`main` で確認）: literal prompt class は kind recipe から来て、matched answer は bounded / audited にし、unknown / unmatched prompt は escalate-only のままです。
- G684 — [PR #1481](https://github.com/J-Tech-Japan/intent-system/pull/1481)、merge commit `23a90d36ec9907541b1b3aa6aec789cf3ea00df7`（`main` で確認）: security-envelope recipe drift を detect-only とし、observed / recorded shape を示します。model と reasoning effort は human-selected wish field のままです。
- G685 — [PR #1483](https://github.com/J-Tech-Japan/intent-system/pull/1483)、merge commit `5e6bf6b6f1ffa3e882c8445960881ed85cc415d7`（`main` で確認）: grammar-only model / effort resolution は host-local の positive / negative evidence と live same-kind argv を使い、shipped model list は持ちません。
- G686 — [PR #1485](https://github.com/J-Tech-Japan/intent-system/pull/1485)、merge commit `e759bc04eeb4e4a56ac5334401b130fd749cb084`（`main` で確認）: typed host-recorded envelope profile に明示的 precedence を持たせ、invalid profile shape は registry fallback の前に fail closed にします。

### full first-parent range の会計

first-parent range は十 commit です。上の九 merge row が feature unit を説明し、残る一つは
post-release context であり、release execution unit ではありません。

| account | full commit | treatment |
| --- | --- | --- |
| 0.19.1 への post-release roll | `32fefec52ae353dbbe10b827020047c57ddfa279` | 説明済み。context であり unit ではない |
| G678 merge | `3671ba062cd1a4e4b54d634e7160da381fdd3ceb` | 上記の merged unit |
| G679 merge | `42789d6d8b1e4ac0d7133a277decd6ebcddeaf6b` | 上記の merged unit |
| G680 merge | `46836e83098c6dd1192beeffe7daf6a32c529d89` | 上記の merged unit |
| G681 merge | `7540932f61ee34cb2941405d13964b5aa90affb1` | 上記の merged unit |
| G682 merge | `bbcc360255ecc01fefbf30f4ea06687b763208e6` | 上記の merged unit |
| G683 merge | `358d8b83b3ea53ae62a5f8323a9b2a26db34235e` | 上記の merged unit |
| G684 merge | `23a90d36ec9907541b1b3aa6aec789cf3ea00df7` | 上記の merged unit |
| G685 merge | `5e6bf6b6f1ffa3e882c8445960881ed85cc415d7` | 上記の merged unit |
| G686 merge | `e759bc04eeb4e4a56ac5334401b130fd749cb084` | 上記の merged unit |

### 四つの attribution された origin

この unit 群は一つの day-scale feedback loop ですが、G625 に従い measured fact の attribution は
分けて保持します。

- operator の landing-authority と multi-user request が G678–G681 を生みました。これは
  operator-request origin であり、別 team の measurement を借りたものではありません。
- operator-filed の [#1469 audit](https://github.com/J-Tech-Japan/intent-system/issues/1469) が
  G682–G684 の configured-looking-but-inert policy / envelope observation を生みました。その
  audit attribution は、この host の後続 corroboration とは別に保持します。
- neighboring domain の `--model sol` incident（2026-08-12）が G685 の model-resolution evidence
  を生みました。`btx-mvc` の launch は account-shaped HTTP 400 を返し、live same-kind argv が
  recovery evidence になりました。この incident は neighboring domain に属し、この team の
  measurement ではありません。
- この team 自身の first-cycle drift finding（2026-08-12）が G686 を生みました。これはこの host
  の envelope-profile observation であり、#1469 audit や neighboring-domain incident とは別です。

minor bump の根拠は検証可能です。operator-controlled landing / work ownership、domain-scoped
event stream、prompt-policy applicability と bounded audit、detect-only envelope drift、host-local
model resolution、typed envelope profile は v0.19.0 line にはありませんでした。これは additive な
preview capability であり、patch-only correction ではありません。

## 意図的な boundary

- この準備が変更するのは version policy、release notes、readiness documentation、release guard
  だけです。code と runtime behavior は変更しません。
- earlier release notes は link し、重複記載しません。feature list は G678–G686 に正確に限定し、
  G687 や earlier unit を暗黙に追加しません。
- prepare-only を保ち、この PR は GitHub Release / tag を作成せず、package publish も release
  automation の実行も行いません。

## リリース準備ゲート (Release-readiness gate)

operator が別の Release 手順を実行する前に確認します。

- `eng/version.json` が stable `0.19.0` / next `0.20.0` を記録していること。
- 上記九 PR と full merge commit が `main` に解決し、`git log v0.19.0..main --first-parent` の全
  commit が range table で説明されていること。
- preview statement が feature description より前にあり、1.0 compatibility promise を link
  していること。
- EN/JA notes が G613 terminology policy の parity を保ち、release-notes count guard、
  version/readiness guard、full suite、`git diff --check` が green であること。
- prepare-only を保つこと。Release creation、tagging、package publication は operator の別 action
  です。

## v0.20.0 の publish

この準備が merge され readiness evidence が green になった後も、Release 作成は別の operator action
です。その後に限り authorized maintainer が `v0.20.0` の GitHub Release を作成・公開でき、
`release.yml`（`on: release: published`）が NuGet package と platform artifact の build / publish
を起動します。その別 Release 後に `eng/version.json` を stable `0.20.0` / next `0.20.1` へ roll し、
同じ commit に次の DRAFT note stub を加え、両方の readiness mirror を更新し、post-release roll を
完了とする前に child-main CI を確認します。
