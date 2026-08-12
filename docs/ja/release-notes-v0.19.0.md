# リリースノート — intent-cli v0.19.0

> **prepare-only / 未リリース。** この変更は version state、release notes、
> readiness documentation だけを準備します。GitHub Release や tag の作成、
> package publish、release workflow の実行、post-release roll は行いません。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.19.0`。
operator が別手順を実行した後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.19.0 に公開されます。
直前の出荷範囲は [v0.18.0 notes](release-notes-v0.18.0.md) を参照し、ここでは
重複記載しません。

## feature description より先に読む preview lane

G666–G676 の surface は `preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md)
の対象外であり、1.x の間に変更または撤回される可能性があり、1.0 の compatibility
commitment には含まれません。

## day-scale で閉じた feedback loop

v0.19.0 は G666 から G676 まで、正確に十一件の merged unit を含みます。一覧は
`git log v0.18.0..main --first-parent` を実行して導出しました。その range の全 commit を
post-release roll または十一件の merge commit として以下で説明しています。各 PR は
MERGED であり、各 full merge commit が `main` に解決することを確認しています。

- G666 — PR #1440、merge commit `1b7f8b718d9c22cfe67707ee9ca23a9a9e6f0b7b`（`main` で確認）: approval layer は recipe による eliminate-first、recorded policy の adjudication、design が relay しないことを実現します。
- G667 — PR #1444、merge commit `2c253a01ea3b7d3836ad044eb5e9ffac38d46f77`（`main` で確認）: packet draft が shared judgment を通して effective base branch を解決します。
- G668 — PR #1446、merge commit `e9d125ea45a163636323a7a0420476b7267cf94e`（`main` で確認）: named branch lane が registry、明示的 membership、immutable routing snapshot を持ちます。
- G669 — PR #1448、merge commit `e1924405e6d0fcdfdccf8665abc7263dc9a0ee96`（`main` で確認）: lane propose/confirm decision record が二つの routing stall class を区別します。
- G670 — PR #1450、merge commit `8a85262cd1e42f73d9ba1f438f783e394f8a3828`（`main` で確認）: placeholder scaffold は gate の judgment 後に issue-cut-ready pool から外れます。
- G671 — PR #1452、merge commit `c4f2d66af72c278d0de1d38b0c2c4ea508b1be5f`（`main` で確認）: pending-delegation disposition は expectation を終え、carriage を保持します。
- G672 — PR #1454、merge commit `cc60fc7ae94ddba7746caf2acdef53ecb29becaf`（`main` で確認）: role contract を guide next の先頭に置き、event mode を offer と duty の両方で説明します。
- G673 — PR #1456、merge commit `e6762a5151dc8f489dd5ba108a63adca4ee8c0a6`（`main` で確認）: GitHub API quota exhaustion を named degraded state とし、doctor が resource ごとの quota を報告します。
- G674 — PR #1458、merge commit `44c4a27befe458399777743ed5c8e16c0d5f3fe1`（`main` で確認）: field equivalence を確認した GitHub read に REST を使い、未確認の read は GraphQL-bound のままです。
- G675 — PR #1460、merge commit `1c7cace56fdf29a834ee2de61df768e3b083a796`（`main` で確認）: scheduler artifact が environment を持ち、transport start failure を lost ではなく degraded とします。
- G676 — PR #1462、merge commit `85a4d451d9a91daaf936e3997cf36f67b73766f1`（`main` で確認）: writer identity で duplicate supervisor を検出し、election は行いません。

### full first-parent range の会計

first-parent range は十二 commit です。上の十一 merge row が feature unit を説明し、残る
一つは range の context であり、release execution unit ではありません。

| account | full commit | treatment |
| --- | --- | --- |
| 0.18.1 への post-release roll | `478dd57b5de609e47dbe678c82f714fd0e463dd8` | 説明済み。context であり unit ではない |
| G666 merge | `1b7f8b718d9c22cfe67707ee9ca23a9a9e6f0b7b` | 上記の merged unit |
| G667 merge | `2c253a01ea3b7d3836ad044eb5e9ffac38d46f77` | 上記の merged unit |
| G668 merge | `e9d125ea45a163636323a7a0420476b7267cf94e` | 上記の merged unit |
| G669 merge | `e1924405e6d0fcdfdccf8665abc7263dc9a0ee96` | 上記の merged unit |
| G670 merge | `8a85262cd1e42f73d9ba1f438f783e394f8a3828` | 上記の merged unit |
| G671 merge | `c4f2d66af72c278d0de1d38b0c2c4ea508b1be5f` | 上記の merged unit |
| G672 merge | `cc60fc7ae94ddba7746caf2acdef53ecb29becaf` | 上記の merged unit |
| G673 merge | `e6762a5151dc8f489dd5ba108a63adca4ee8c0a6` | 上記の merged unit |
| G674 merge | `44c4a27befe458399777743ed5c8e16c0d5f3fe1` | 上記の merged unit |
| G675 merge | `1c7cace56fdf29a834ee2de61df768e3b083a796` | 上記の merged unit |
| G676 merge | `85a4d451d9a91daaf936e3997cf36f67b73766f1` | 上記の merged unit |

### 四つの attribution された origin

この unit 群は day-scale で閉じる一つの feedback loop ですが、G625 に従い measured fact
の attribution は分けて保持します。

- operator の branch-lane request が G667–G669 を生みました。これは operator-request origin
  であり、remote-herdr team の measurement ではありません。
- operator-filed feedback issue [#1441](https://github.com/J-Tech-Japan/intent-system/issues/1441)
  が G670–G672 を生みました。remote-herdr domain の design thread (Claude) が report し、
  operator は Tomohisa Takaoka、期間は 2026-08-04–2026-08-11、48 packet、tier-2 E2E 21 round
  です。これらの measurement はその reporting team と期間に属します。
- operator-filed feedback issue [#1442](https://github.com/J-Tech-Japan/intent-system/issues/1442)
  が G673–G674 を生みました。remote-herdr の four-thread team が 2026-08-12T02:05–02:08Z
  に outage を測定し、GraphQL remaining 0（5,046 request 後）、REST remaining 4,948 を
  記録しました。この report の team と timestamp を保持し、この repository 自身の measurement
  と混ぜません。
- same-day の host incident が G675–G676 を生みました。G675 は 2026-08-12 に scheduler の
  exit-127 loop と一 cycle の ten false loss をこの host で測定し、G676 は同じ machine の
  Sekiban workers team、workspace `w2H` で四つの concurrent supervisor を 2026-08-12 に
  測定しました。host incident と sibling-team observation は team と date を含めて区別します。

minor bump の根拠は検証可能です。branch-lane registry / routing snapshot、pending-delegation
disposition record、named quota-degraded state / doctor quota report、duplicate-supervisor
detection は v0.18.0 には存在しませんでした。これは additive な preview capability であり、
patch-only correction ではありません。

## 意図的な boundary

- この準備が変更するのは version policy、release notes、readiness documentation、release
  guard だけです。code と runtime behavior は変更しません。
- earlier release notes は link し、重複記載しません。上の table は G666–G676 に正確に限定し、
  earlier unit を追加しません。
- prepare-only を保ち、この PR は GitHub Release / tag を作成せず、package publish も release
  automation の実行も行いません。

## リリース準備ゲート (Release-readiness gate)

operator が別の Release 手順を実行する前に確認します。

- `eng/version.json` が stable `0.18.0` / next `0.19.0` を記録していること。
- 上記十一 PR と full merge commit が `main` に解決し、`git log v0.18.0..main --first-parent`
  の全 commit が range table で説明されていること。
- preview statement が feature description より前にあり、1.0 compatibility promise を link
  していること。
- EN/JA notes が G613 terminology policy の parity を保ち、release-notes count guard、
  version/readiness guard、full suite、`git diff --check` が green であること。
- prepare-only を保つこと。Release creation、tagging、package publication は operator の別 action
  です。

## v0.19.0 の publish

この準備が merge され readiness evidence が green になった後も、Release 作成は別の operator
action です。その後に限り authorized maintainer が `v0.19.0` の GitHub Release を作成・公開でき、
`release.yml`（`on: release: published`）が NuGet package と platform artifact の build / publish
を起動します。その別 Release 後に `eng/version.json` を stable `0.19.0` / next `0.19.1` へ roll し、
同じ commit に次の DRAFT note stub を加え、両方の readiness mirror を更新し、post-release roll を
完了とする前に child-main CI を確認します。
