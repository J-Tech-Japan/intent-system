# intent-cli ドキュメント（日本語）

> English version: [`../en/README.md`](../en/README.md)

> **公式サイト:** [intent-driven-development.com（日本語）](https://www.intent-driven-development.com/jp) — J-Tech Japan が運営する Intent-Driven Development のコンセプト・intent-system サービスサイトです。Intent-Driven Development の考え方や intent-system の概要を扱います。GitHub リポジトリは引き続きコード・リリース・インストール・詳細ドキュメントの提供元です。

`intent-cli` は、AI agent に Intent System の正規手順を確認させながら Intent-Driven Development を進めるための **決定論的なサポートツール** です。

新しい project では implementation repository と intents host repository を作り、host だけを
checkout します。そこで AI agent を開き、次を貼り付けます:

> target implementation repository `<owner>/<implementation-repo>` 用に intent-cli を
> 設定します。空の intents host repository を開いています。まずインストール済みの
> guidance で intent-cli を理解し、それから初期化を案内してください。1 回に 1 つずつ
> decision を聞いてください。

agent が内部で `intent-cli` を実行し、質問や結果を返します。1 台に collocate する team は
`herdr-only` を最初の supported choice とします（PREVIEW は maturity note）。distributed team
または既存の agmsg investment には `agmsg` + herdr を選びます。primary なのは transport
ではなく 4 スレッドモデルです。

## ページ一覧

1. [インストール](01-install.md)
2. [プロジェクト開始](02-project-start.md)
2a. [はじめに: 最初の packet までの道のり](02a-getting-started-orchestration.md) — minimal start と primary な 4 スレッドモデル。collocate する `herdr-only` は supported（PREVIEW は maturity note）
3. [Intent Storming と intent の整理](03-intents.md)
4. [packet 作成と issue 公開](04-packets-issues.md)
4a. [GitHub ワークフローラベルで見る現在地](04a-workflow-labels.md) — ラベルの意味と読み方
12. [agent メッセージオーケストレーション](12-agent-message-orchestration.md) — 4 スレッドの contract reference。single-domain と multi-domain の routing

### 代替経路

5. [実装ループの設定](05-implementation-loop.md) — timer-loop の **alternative** セットアップ
6. [レビュー / next-slice ループの設定](06-review-next-slice-loop.md) — timer-loop の **alternative** セットアップ

### リファレンスと復旧

7. [ループがおかしいときの復旧](07-recovery.md)
8. [コマンドリファレンス](08-command-reference.md) — agent 向け・パワーユーザー向けコマンド一覧
9. [開発者リファレンス](09-developer-reference.md) — パッケージ化された実行、preview チャンネル、バージョンフロー

## Intent Storming とは

**Intent Storming** は、コードを書く前に「何を作るか・なぜ作るか・どの制約を受け入れるか」を AI agent と整理し、構造化された intent tree に残す作業です。AI agent は背景・選択肢・メリット/デメリット・推奨理由つきで構造化された質問を投げかけ、あなたの回答を packet（実装タスク）と GitHub issue の土台となる intent tree に整理します。

詳しくは [Intent Storming と intent の整理](03-intents.md) を参照してください。

**プロンプトの背後にある唯一のルール:** label/metadata を変更する前に、AI agent は適切な `intent-cli` コマンドを実行すべきです。ファイルを手編集したり GitHub label を手動で適用しません。[コマンドリファレンス](08-command-reference.md) を参照してください。

## 2 つの agent ロール（最初に一度だけ読む）

| ロール | source of truth | 責務 |
| --- | --- | --- |
| **Host / review agent** | 親 host の `.intent-cli/` 状態 + intent tree | issue 公開、`intent-target` 付与、review/approve/merge、next slice 切り出し、`intent-cli automation` 経由の label 遷移 |
| **Child implementation agent** | **GitHub の issue/PR + repo ローカルのコード**（host metadata ではない） | issue 契約の実装、PR の作成/更新、`intent-cli worker` での結果記録 |

Child implementation agent は **GitHub-contract-only**: host の `.intent-cli/`、queue-state、metadata branch、`intents/**` を読んだり変更したりしない。

host は **別の host リポジトリ** に置くこともできますし、**同じリポジトリの専用 metadata ブランチ**（例: `main-metadata`）に置くこともできます。詳しくは [プロジェクト開始 → リポジトリトポロジーの選択](02-project-start.md#リポジトリトポロジーの選択) を参照してください。

## コミュニティ

コミュニティのディスカッションや質問には [J-Tech JAPAN OSS Discord](https://discord.gg/z9FnEgm6mp) にご参加ください。

再現可能なバグやアクションにつながる機能要望は [GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) として報告してください。セキュリティに関する報告は [SECURITY.md](../../SECURITY.md) へ。
