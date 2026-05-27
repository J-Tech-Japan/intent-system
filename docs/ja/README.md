# intent-cli ドキュメント（日本語）

> English version: [`../en/README.md`](../en/README.md)

`intent-cli` は、AI agent に Intent System の正規手順を確認させながら Intent-Driven Development を進めるための **決定論的なサポートツール** です。

`intent-cli` をインストールしたら、AI agent のデザインスレッドで次のように依頼します:

> `<owner>/<repo>` で intent-cli を使い始めたいです。
> intent-cli に現在のフェーズと次に決断すべきことを聞いてください。

agent が内部で `intent-cli` を実行し、質問や結果を返します。コマンドを覚える必要はありません。

## ページ一覧

1. [インストール](01-install.md)
2. [プロジェクト開始](02-project-start.md)
3. [Intent Storming と intent の整理](03-intents.md)
4. [packet 作成と issue 公開](04-packets-issues.md)
4a. [GitHub ワークフローラベルで見る現在地](04a-workflow-labels.md) — ラベルの意味と読み方
5. [実装ループの設定](05-implementation-loop.md)
6. [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)
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

コミュニティのディスカッションや質問には [J-Tech JAPAN OSS Discord](https://discord.gg/kMdv978X) にご参加ください。

再現可能なバグやアクションにつながる機能要望は [GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) として報告してください。セキュリティに関する報告は [SECURITY.md](../../SECURITY.md) へ。
