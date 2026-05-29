# 外部アーティファクト取り込み (G438)

← [docs index](README.md) | → [リカバリー](07-recovery.md)

このページでは、通常の intent-packet パブリッシュフロー以外から届く **外部 GitHub Issue や PR** を AI エージェントとホストオペレーターがどのように扱うかを説明します。想定するユーザー体験は次のとおりです：

> この外部 Issue / PR を intent-cli に聞いて、正規の方法で扱ってください。

AI エージェントが intent-cli にガイダンスを問い合わせ、返されたステップに従います。人間が intent-cli コマンドを直接覚える必要はありません。

## なぜメタデータが必要か

外部アーティファクトにコメントを付けてワークフローラベルを貼るだけでは**正規の取り込みになりません**。intent ワークフローに取り込む価値は永続的なトレーサビリティにあります。ホストは「なぜこのアーティファクトを受け入れたか」「どの intent を支えるか」「どのパケットと関連するか」「レビューで何を検証するか」を記録する必要があります。

このメタデータがなければ、後続ループのエージェントはパケット対応のレビューやクローズアウトを手動修復なしに実行できません。

## 3 つのレーン

intent-cli は 3 つの取り込みシナリオを区別します：

| レーン | トリガー | ゲート |
|---|---|---|
| `external-issue` | 外部 GitHub Issue を intent キューに追加したい | `intent-target` 適用前に軽量パケットメタデータが必要 |
| `external-pr-review` | 外部 PR を intent ワークフローの一部としてレビューしたい | パケット/レビューコンテキストメタデータ + リンク Issue が必要 |
| `external-pr-adopt` | 外部 PR をホストが正式に intent ワークフローに採用したい | 完全な来歴 + シャドウ Issue + オペレーターの明示的な確認が必要 |

## intent-cli に聞くプロンプトテンプレート

### 外部 Issue 取り込み

> `<url>` にある外部 Issue を intent ワークフローに正式に取り込む方法を intent-cli に聞いてください。

AI エージェントは次を実行します：

```
intent-cli guide artifact-intake --lane external-issue --repo <owner/repo> --format markdown
```

### 外部 PR レビュー

> `<url>` にある外部 PR を intent ワークフローでレビューする方法を intent-cli に聞いてください。

AI エージェントは次を実行します：

```
intent-cli guide artifact-intake --lane external-pr-review --repo <owner/repo> --format markdown
```

### 外部 PR 採用

> `<url>` の PR を intent ワークフローに正式採用する手順を intent-cli に聞いてください。

AI エージェントは次を実行します：

```
intent-cli guide artifact-intake --lane external-pr-adopt --repo <owner/repo> --format markdown
```

## メタデータフィールド

各レーンでは、ラベル変更の前にホストが特定のメタデータを記録する必要があります。`artifact-intake` ガイドコマンドは要求レーンの必須フィールドを返します。全レーン共通の主要フィールド：

- **source_artifact** — 外部 GitHub Issue/PR の URL と番号
- **relevant_intents** — このアーティファクトが支える既存 intent ドキュメントへのリンク
- **related_packets** — 関連パケットの実行ユニット
- **expected_outcome** — 承認または実装が達成すべきこと
- **constraints** — スコープ・互換性・順序の制約

PR 固有の追加フィールド：

- **linked_issue** — intent コンテキストを固定する適切なリンク Issue（またはシャドウ Issue）
- **review_focus** — ホストレビューで検証すべきこと
- **provenance**（採用のみ）— 元の作者、元リポジトリ、過去の議論への参照
- **operator_confirmation**（採用のみ）— ホストオペレーターの明示的な承認

## シャドウ Issue

外部 PR に適切なリンク Issue がない場合、ホストはレビュートランジションや採用ステップの前に**シャドウ Issue** を作成する必要があります。シャドウ Issue は：

- 取り込みレーンで必要なメタデータフィールドを記録する
- 後続のレビュー/クローズアウトステップで PR の intent アンカーになる
- ホストオペレーターが手動で作成する（自動作成禁止）

PR に未解決のプロダクト質問や技術的な intent 質問がある場合は、シャドウ Issue を作成する**前**にインタビュー/クラリフィケーションフローを実行します：

```
intent-cli guide workflow task intent-interview --format markdown
```

## コントリビューターが知る必要があること

何もありません。外部コントリビューターは intent ラベル、queue-state、パケット YAML、クローズアウトの仕組みを知る必要はありません。ホストエージェントが intent-cli を通じてすべてのメタデータ作成とラベルトランジションを処理します。

## ガードレール

- `intent-target` はコメントやラベル操作だけでは適用されない
- `intent-pr-reviewing` はメタデータとリンク Issue が揃うまで開始されない
- マッピングが曖昧な場合はオペレーターの確認待ちで停止し、推測しない
- AI エージェントはオペレーター確認ゲートを明示的な意思決定なしに通過してはならない

## コマンドリファレンス

```
intent-cli guide artifact-intake --lane external-issue [--repo <owner/repo>] [--format markdown|json]
intent-cli guide artifact-intake --lane external-pr-review [--repo <owner/repo>] [--format markdown|json]
intent-cli guide artifact-intake --lane external-pr-adopt [--repo <owner/repo>] [--format markdown|json]
```
