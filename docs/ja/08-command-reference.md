# コマンドリファレンス（agent 向け / パワーユーザー向け）

> 日本語版。English version: [`../en/08-command-reference.md`](../en/08-command-reference.md)

このページは AI agent やパワーユーザーが代理で実行する `intent-cli` コマンド群を示します。
通常の利用では記憶する必要はありません。
[ルート README](../../README.md) のクイックスタートと `intent-cli guide start` が
典型的なパスをカバーします。

以下のコマンドは AI agent が内部で実行するものです。現在の全カタログは
`intent-cli guide commands list --format json` を実行してください。

---

## 2 つの agent ロール

| ロール | source of truth | 責務 |
| --- | --- | --- |
| **Host / review agent** | 親 host の `.intent-cli/` 状態 + intent tree | issue 公開、`intent-target` 付与、review/approve/merge、next slice 切り出し、`intent-cli automation` 経由の label 遷移 |
| **Child implementation agent** | **GitHub の issue/PR + repo ローカルのコード**（host metadata ではない） | issue 契約の実装、PR の作成/更新、`intent-cli worker` での結果記録 |

Child implementation agent は **GitHub-contract-only** です: host の `.intent-cli/`、
queue-state、metadata branch、`intents/**` を読んだり変更したりしません。

host は **別の host リポジトリ** にも、**同じリポジトリの専用 metadata ブランチ**
（例: `main-metadata`）にも置くことができます。
詳しくは [プロジェクト開始 → リポジトリトポロジーの選択](02-project-start.md#リポジトリトポロジーの選択) を参照してください。

---

## プロジェクトセットアップ

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work setup --format json
```

## デザイン / intent

```bash
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile
intent-cli guide workflow
```

## デザインスレッド improve / 再整合

デザインスレッドで定期的に一歩引いて、最近の作業が当初の mission / vision /
values・ADR / design note・intent tree とまだ整合しているかを確認するための
リフレクション工程です。デザインスレッドでは次の自然言語リクエストをそのまま
貼り付ければ、agent が内部で guide を実行します:

```text
intent-cli で improve プロセスを実行してください。
```

agent は現在のガイダンスを取得し、構造化レポートを生成します。`improve` は
first-class の top-level コマンドで、`guide improve` も同等の guide 名前空間形式
です:

```bash
intent-cli improve --domain <domain> --format markdown
intent-cli guide improve --domain <domain> --format markdown
```

両形式は同一のガイダンスを返し、`intent-cli --help` と
`intent-cli guide commands list` から discoverable です。installed CLI に
`improve` サーフェスが無い場合は `improve guidance unavailable` を報告して CLI を
更新します。`bug-to-intent-repair`・host-loop recovery・`state-doctor`・
dirty-state repair で**代替しない**でください。

`improve` はデフォルトで **implementation-aware** です。evidence が利用可能なら
関連 GitHub issue/PR・実装 diff・テスト・レビュー所見・product evidence も点検し、
現在の最上位 blocker を特定して corrective backlog を提案します
（`Implementation Reality Check` / `Blocker Cluster Analysis` /
`Corrective Backlog Candidates`）。packet 履歴が未解決の product blocker を示す場合、
intent-tree の整理だけでは不十分です。素早い intent-only リフレクションには
`--light` を付けます:

```bash
intent-cli improve --domain <domain> --light --format markdown
```

operator 承認後、agent は提案した corrective packet を作成し、最初の GitHub issue を
**最大1件**だけ publish できます（明示的に依頼された場合を除く）。

`improve` は first line of defense ではなく **safety net** です。通常パスは
**packet-time intent maintenance**（G461）です。packet draft 時点で agent は intent
placement・ADR candidate・diagram candidate・docs update・closeout knowledge writeback
を検討するよう促されます（`intent-cli guide workflow task packet-draft` 参照）。この
metadata は optional かつ backward-compatible で（metadata を持たない legacy packet も
有効のまま）、設計コンテキストが新鮮なうちに記録することで intent tree・ADR・diagram が
packet 履歴から遅れて drift するのを最初の段階で防ぎます。`improve` は packet-time
チェックが見逃した drift を後から拾います。

`guide improve` はデザインスレッドのリフレクション工程であり、スケジューラでも
provider 起動でも、host-loop / worker-loop の通常の復旧診断でもありません。
metadata / label / queue の復旧は既存の運用サーフェス
（`automation reconcile` / `automation publish-recovery` /
`review closeout-plan`）に残します。MVV・ADR / design note・intent tree・直近の
packet 履歴・clarification 履歴・短期ループの兆候を点検し、結果を `aligned` /
`intent-strengthening-recommended` / `clarification-recommended` /
`corrective-packet-recommended` / `adr-update-recommended` /
`short-term-loop-detected` / `operator-policy-required` のいずれかに分類します。
変更はまず提案し、operator の同意後にサポートされた intent-cli / repo 経路でのみ
適用します。

## Grill — 永続インタビューモード

ユーザー向けの**永続インタビューモード**（G463）です。トピックを grill する
よう依頼すると、デザインスレッドは grill モードに留まり、現在の intent
コンテキスト（intents・packets・ADR / design note・docs・関連する実装
evidence）から open-question backlog を生成し、**一度に1つずつ質問**を続けます。
各回答のあとも `grill` を再入力させることなく自動的に継続し、構造化された
停止条件に達するまで質問します。

```bash
intent-cli grill --domain <domain> --format markdown
intent-cli guide grill --domain <domain> --format markdown
```

両形式は同一のガイダンスを返し、`intent-cli --help` と
`intent-cli guide commands list` から discoverable です。grill は既存の
`interview` artifact の上に構築されており（回答は `intent-cli interview
record-answer` で記録し、保留中の質問は `intent-cli interview next-question`
で読み出します）、`clarification`（blocker 解決）でも `improve`（遡及的な
再整合）でもなく、packet / issue を自動 publish しません。

停止条件: `no-more-questions`（backlog が空で rediscovery でも新しい質問が
見つからない場合にのみ `今のところ追加質問はありません` を返す）・
`packet-ready`・`intent-update-ready`・`clarification-needed`・
`blocked-by-user-decision`・`too-broad-split-needed`。packet / issue /
intent-update のアクションは停止条件で提案し、operator の明示的な同意後にのみ
適用します。

## Inspect — evidence-backed 観測

タスクを切る前に**実際のプロダクトを観測する**ための名前付きプロセスです（G466）。
`inspect` は agent に、実際の app / CLI / UI / logs / tests を動かし、観測した
evidence と inference を厳密に分離し、expected intent と比較し、その gap を packet
candidate に変換するよう導きます。

```bash
intent-cli inspect --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide inspect --domain <domain> --target-repo <owner/repo> --format markdown
```

**Inspect Report** は `observed_behavior`・`expected_intent`・`evidence`・`gaps`・
`risk_severity`・`recommended_next_action`・`packet_candidates` を分離します。最初の
パスはデフォルトで **read-only** で、破壊的な操作や自動 publish を行わず、
browser / computer-use / log / test ツールを**置き換えるのではなく使い方を導きます**。
観測結果に応じて inspect パスは **stack**（gap を packet 化）・**grill**（不明確な
intent を抽出）・**improve**（systemic drift）・**recovery**（壊れた運用状態）・
**no-action**（intent と一致）へルーティングします。grill・stack・improve とは
区別されます。

## Next — design-side アクションアドバイザー

デザインスレッドで最もシンプルな問い「次に何をしたらいいか」に答えるのが
`intent-cli next`（G465）です。design-side プロセスのカタログを提示し、その中から
1つを推奨するので、ユーザーは全コマンド名を覚える必要がありません。

```bash
intent-cli next --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide next --domain <domain> --target-repo <owner/repo> --format markdown
```

自然言語で `intent-cli に聞いて、次に何をしたらいいか教えてください。` と尋ねれば、
agent が evidence（現在の intents・open question・packet backlog・open PR / review
状態・CLI / queue health）を確認し、**grill**（open question 抽出）・**stack**
（packet backlog 作成 + 最初の issue publish）・**improve**（遡及的再整合）・
**inspect**（実際の app/CLI/UI/log/test 挙動の evidence-backed 観測。単なる status 確認ではない）・**issue-publish**（ready な packet の publish）・
**review**（open PR のレビュー）・**recovery**（stale CLI / queue の修復）・**idle**
（着手可能な作業なし）のいずれか1つを推奨します。出力には recommended action・
reason・確認した evidence・paste 可能な suggested prompt・safety boundary が含まれ
ます。`next` はデフォルトで **read-only** で、選択したアクションを自動実行しません。
実行するかはユーザーが判断します。

## Stack — packet backlog 作成 + 最初の issue publish

名前付きの**前方計画**プロセス（G464）です。`stack` は現在の intents を読み、
いま着手可能な packet（しばしば10件程度）を依存順に backlog として作成し、その
durable state を commit / push してから、デフォルトでは**最初の1件だけ** GitHub
issue を publish します。残りは deferred backlog として残します。

```bash
intent-cli stack --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide stack --domain <domain> --target-repo <owner/repo> --format markdown
```

`stack` は「タスクを積む」に対応します。`improve`（drift / loop 危機からの遡及的
再整合）・`grill`（永続的な open-question インタビュー）・`clarification`（blocker
解決）・runtime `queue` 遷移とは区別されます。open question・WIP・host-only packet
境界を尊重し、issue-publish の前に durable な packet state を commit / push し、
`intent-target` を手で付けません（host の publish 境界が付与）。出力 shape は
`created_packets`・`recommended_first_issue`・`published_issue`・`deferred_items`
を列挙します。

## Packet / issue

```bash
intent-cli packet ...
intent-cli issue validate-body ...
intent-cli issue prepare ...
intent-cli issue publish-reviewed ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --write
```

## 実装・レビューループ

```bash
# AI agent 向けループプロンプトを取得:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --domain <name>
```

worker/metadata コマンドだけでループを回す operator dogfooding 向けプロンプトテンプレートは
[`docs/automation-templates/`](../automation-templates/README.md) にあります。

## 復旧

```bash
intent-cli worker issue-preflight       --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight  --repo <owner>/<repo> --pr <n>    --format json
intent-cli automation doctor --format json
```

---

## コマンドグループ概要

`intent-cli guide commands list` は **role ベースのカタログ**（G467）です。各
command group が operator-role カテゴリ — **design**（improve / grill / stack /
next / inspect / intent / interview / packet / clarify）・**host-review**（review /
closeout / automation / issue）・**child-implementation**（worker）・
**recovery-diagnostics**（automation doctor / metadata / queue）・
**advanced-developer**（task）— を `primary`/`support` lifecycle classification
とともに持ちます。`intent-cli guide help` も同じ role バケットを説明し、loop-prompt
生成（`guide workflow task implementation-loop` / `review-next-slice-loop`）を案内
します。

| Surface | 役割 |
|---------|------|
| `intent-cli guide …` | Ask-first ガイダンス: コラボレーションモデル、ワークフロー、プロンプトテンプレートカタログ、one-shot プロンプト |
| `intent-cli status brief` | コンパクトな AI スレッドコンテキスト入力 |
| `intent-cli clarify draft` / `clarify record` | オーナー clarification フロー |
| `intent-cli issue validate-body` | Child Issue Contract 単独強制 |
| `intent-cli issue prepare` / `issue publish-reviewed` | レビュー済み issue body 公開境界（`intent-target` は付与しない） |
| `intent-cli worker next-action` / `claim` / `result-summary` / `complete` | Child 実装ループセレクター + 境界付き label 遷移 |
| `intent-cli automation summary` | プロバイダー中立 label 駆動自動化コントラクトエミッター |
| `intent-cli safety nested-provider-handoff` | アーティファクトのみのネストされたプロバイダー安全ガード（プロバイダーを起動しない） |

---

## ルール

- **`intent-cli` 遷移コマンドを使い、直接編集はしない。** `intent-cli automation` /
  `intent-cli worker` が所有する遷移（queue-state、ワークフロー label、
  packet publish metadata など）は手編集しない。label は必ずこれらのコマンド経由で付与し、
  `gh ... edit --add-label` を直接使わない。
- **読んで推測するより聞く。** ローカルのルールファイルを読むより `intent-cli guide ...`
  を優先する; ガイダンスはインストール済み CLI の現行コントラクトを反映している。
- **`intent-cli` は AI プロバイダーを起動しない。** 決定論的なガイダンスを出力し、
  コントラクトを検証し、bounded な GitHub/metadata 遷移を行うだけ。AI agent がドライバーシートに留まる。
