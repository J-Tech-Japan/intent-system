# リリースノート — intent-cli v0.17.0

> **prepare-only / 未リリース。** この準備では version state、release notes、
> readiness documentation だけを変更します。GitHub Release、tag、package publish、
> release workflow は作成・実行せず、Release 作成は operator の手順として残します。

Install verification: `JTechJapan.IntentSystem.Cli --version 0.17.0`。
承認後の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.17.0 に公開されます。
直前の scope は [v0.16.1 notes](release-notes-v0.16.1.md) と
[v0.16.0 notes](release-notes-v0.16.0.md) を参照し、ここでは重複記載しません。

## feature description の前に読む preview lane

以下の surface は `preview-through-1.x` です。[1.0 compatibility promise](1.0-compatibility-promise.md)
の対象外であり、1.x の間に変更または撤回される可能性があり、1.0 の compatibility commitment
ではありません。

## supervised team の operating contract

三つの team が Codex を design seat として動かし、同じ週に失敗しました。原因は model capability
ではなく、書かれていない operating contract でした。三件の field report と 45-unit remote-herdr
report がこの release の measured basis です。G654 が contract を明文化し、周辺 unit が watcher に
durable activity/delivery evidence、OS-owned lifetime、escalation ladder、coherence work の recency signal
を与えます。

中心となる formula は、**各 team = 四つの judgment-bearing thread — design、orchestration、
implementation、review — + 一つの supervision process** です。supervision process は watcher
infrastructure であり、観測、記録、authorized thread の wake は行いますが、judgment や recovery
authority は持ちません。

## v0.17.0 の内容

この minor release は contract 順で正確に十一件の merged unit を対象にします。一覧は
`git log v0.16.1..main` を実行して導出し、その範囲の全二十 commit を以下で説明しています。
記載したすべての merge が `main` に解決することも確認しています。

- G656 — PR #1410、merge commit `853b48ab`（`main` で確認）: JSON guide fragment を明示宣言し、
  rendering guard が JSON と Markdown の到達可能な missing-count headline をすべて検証します。
- G652 — PR #1412、merge commit `542133f7`（`main` で確認）: `notify status` と supervision が
  durable activity sequence/time evidence を使って `working` と `live-idle` を区別し、interval 未満の
  bound は書き換えず warning にします。
- G653 — PR #1414、merge commit `83c5feea`（`main` で確認）: report は transport より前に
  generation-aware outbox へ永続化されます。undelivered report は task の再 delegate ではなく
  `notify collect` で回収します。
- G655 — PR #1416、merge commit `c06e16d3`（`main` で確認）: orchestration が delegate 前に
  workspace prerequisite を準備し、intent-cli に git を実行させず permission failure の
  prepare-and-resume path を示します。
- G654 — PR #1418、merge commit `eae66f05`（`main` で確認）: agent-kind-neutral な
  design-thread guide が four-outcome wake、provenance vocabulary、transaction-scoped approval、
  merge-authority comparison、monitoring separation を定義します。
- G657 — PR #1420、merge commit `7ab3e297`（`main` で確認）: escalation ladder に owner-role
  subject fallback、settled-red CI finding、declared-label green fallback を加え、single-rung wake を保ちます。
- G658 — PR #1422、merge commit `39d7cf42`（`main` で確認）:
  `notify supervise install` が team ごとの launchd、Task Scheduler、systemd artifact と正確な管理 command
  を出力します。Install は emit するだけで、register は決して行いません。
- G659 — PR #1424、merge commit `5331ec11`（`main` で確認）: opt-in event mode は一つの
  supervisor process 内で recorded `herdr agent wait` を保持し、失敗した wait を re-arm して数秒で wake
  します。Event mode は interval floor を維持します。
- G660 — PR #1426、merge commit `bdc5b5b1`（`main` で確認）: status、escalate、supervise が
  一つの residency-resolved delivery judgment を共有し、durable external-reader append と pane wake の
  異なる basis を保ちます。
- G661 — PR #1428、merge commit `b06dac5d`（`main` で確認）: writeback commit clarity、retire
  reactivation、placeholder exclusion、reachability scaffolding、multi-checkout host defaults という五つの
  field friction を修正します。
- G662 — PR #1430、merge commit `f2e53c03`（`main` で確認）: improve run を durable record にし、
  `guide next` が realignment recency を使い、facet-check の `no_facet_data: true` は lexical check が
  実行されなかった意味だと正直に示します。

## 二十 commit の derivation

この範囲は post-release roll、三つの squash merge、八つの direct-commit/merge pair から成ります。
次の一覧が `git log v0.16.1..main` のすべての commit を説明します。

| account | commits |
|---|---|
| post-release roll | `f3165a5c` |
| G656 | `853b48ab` |
| G652 | `542133f7` |
| G653 | `83c5feea` |
| G655 | `f6f2b6f0`, `c06e16d3` |
| G654 | `d1ec27d8`, `eae66f05` |
| G657 | `970eb671`, `7ab3e297` |
| G658 | `f9b5ff96`, `39d7cf42` |
| G659 | `30931bd2`, `5331ec11` |
| G660 | `99f5f2b2`, `bdc5b5b1` |
| G661 | `28a68cd0`, `b06dac5d` |
| G662 | `234a7058`, `f2e53c03` |

## 意図的な boundary

- `notify supervise install` は artifact と正確な command を emit しますが、OS scheduler の register、
  unregister、start、stop は決して行いません。
- Event mode は同じ supervisor process に秒単位の evidence を加えますが、独立した interval cycle は
  safety floor として残ります。
- Realignment-window recency は paste-ready な improve action を recommend しますが、realignment work を
  schedule、実行、grade することは決してありません。
- owner-role 自身が subject の場合、escalation ladder は design を wake できますが、design が受け取るのは
  一件の escalation-class wake だけで、recovery authority は一切付与されません。

## minor release の根拠

`supervise install`、event mode、`packet retire --reactivate`、design-thread guide surface、durable
improve-run record はすべて v0.16.1 には存在しませんでした。既存 v0.16.1 contract の patch-only
修正ではなく、新しい preview surface です。

## リリース準備ゲート (Release-readiness gate)

v0.17.0 Release を作成する前に operator が確認します。

- `eng/version.json` が stable `0.16.1` / next `0.17.0` を記録していること。
- 上記十一件の PR merge がすべて `main` に解決し、`git log v0.16.1..main` の全二十 commit が表で
  説明されていること。
- preview statement が feature description より前にあり、
  [1.0 compatibility promise](1.0-compatibility-promise.md) を link していること。
- bilingual release-notes guard と count guard、full Release suite、`git diff --check`、exact-head CI が
  green であること。
- [v0.16.1 notes](release-notes-v0.16.1.md) と [v0.16.0 notes](release-notes-v0.16.0.md) は
  preceding scope への link のままで、ここでは重複記載しないこと。
- prepare-only の境界を守り、operator が別手順を明示的に実行するまで GitHub Release、tag、
  package publish を作成しないこと。

## v0.17.0 の publish

ゲートが green になり operator が承認した後にのみ maintainer が GitHub Release と package を
publish できます。この preparation PR 自体はその操作を行いません。
