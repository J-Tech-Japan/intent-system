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
