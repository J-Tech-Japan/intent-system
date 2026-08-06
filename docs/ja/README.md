# intent-cli ドキュメント（日本語）

> 英語版: [`../en/README.md`](../en/README.md)

> **公式サイト:** [intent-driven-development.com（日本語）](https://www.intent-driven-development.com/jp) — J-Tech Japan が運営する Intent-Driven Development のコンセプト・intent-system サービスサイトです。Intent-Driven Development の考え方や intent-system の概要を扱います。GitHub リポジトリは引き続きコード・リリース・インストール・詳細ドキュメントの提供元です。

`intent-cli` は、AI agent に Intent System の正規手順を確認させながら Intent-Driven Development を進めるための **決定論的なサポートツール** です。

最初に [自己完結した導入パターン](02a-getting-started-orchestration.md) を選びます。
別ホストリポジトリ / 同一リポジトリのメタデータ用ブランチと、新規 / 既存プロジェクトを
掛け合わせます。各パターンには貼り付け可能な最初のプロンプトが 2 つあります。1 台に
同居するチームでは、依存関係が少ない `herdr-only` を優先します。分散チームまたは既存の
agmsg 投資があるチームには、サポート対象で廃止されない `agmsg` + herdr を選びます。primary
なのはトランスポートではなく 4 スレッドモデルです。

## ページ一覧

1. [インストール](01-install.md)
2. [プロジェクト開始](02-project-start.md)
2a. [はじめに: 最初の packet までの道のり](02a-getting-started-orchestration.md) — 最小開始と primary な 4 スレッドモデル。同居する `herdr-only` は依存関係が少ないため優先
   - [別ホスト × 新規](02b-separate-host-brand-new.md)
   - [別ホスト × 既存](02c-separate-host-existing.md)
   - [同一リポジトリ × 新規](02d-same-repo-brand-new.md)
   - [同一リポジトリ × 既存](02e-same-repo-existing.md)
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
10. [1.0 互換性の約束](1.0-compatibility-promise.md) — 対象となる機械向けサーフェス、廃止規則、ledger
11. [日本語ドキュメントの用語ポリシー](00-terminology-policy.md)

## Intent Storming とは

**Intent Storming** は、コードを書く前に「何を作るか・なぜ作るか・どの制約を受け入れるか」を AI agent と整理し、構造化された intent tree に残す作業です。AI agent は背景・選択肢・メリット/デメリット・推奨理由つきで構造化された質問を投げかけ、あなたの回答を packet（実装タスク）と GitHub issue の土台となる intent tree に整理します。

詳しくは [Intent Storming と intent の整理](03-intents.md) を参照してください。

**プロンプトの背後にある唯一のルール:** label/metadata を変更する前に、AI agent は適切な `intent-cli` コマンドを実行すべきです。ファイルを手編集したり GitHub label を手動で適用しません。[コマンドリファレンス](08-command-reference.md) を参照してください。

## 2 つの agent ロール（最初に一度だけ読む）

| ロール | 正本となる定義 | 責務 |
| --- | --- | --- |
| **host / review agent** | 親ホストの `.intent-cli/` 状態 + intent tree | issue 公開、`intent-target` 付与、review/approve/merge、次の作業スライスの切り出し、`intent-cli automation` 経由の label 遷移 |
| **子実装 agent** | **GitHub の issue/PR + リポジトリローカルのコード**（ホストメタデータではない） | issue 契約の実装、PR の作成/更新、`intent-cli worker` での結果記録 |

子実装 agent は **GitHub-contract-only**: ホストの `.intent-cli/`、queue-state、メタデータ用ブランチ、`intents/**` を読んだり変更したりしない。

ホストは **別のホストリポジトリ** に置くことも、**同じリポジトリの専用メタデータ用ブランチ**（例: `main-metadata`）に置くこともできます。詳しくは [プロジェクト開始 → リポジトリトポロジーの選択](02-project-start.md#リポジトリトポロジーの選択) を参照してください。

## コミュニティ

コミュニティのディスカッションや質問には [J-Tech JAPAN OSS Discord](https://discord.gg/z9FnEgm6mp) にご参加ください。

再現可能なバグやアクションにつながる機能要望は [GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) として報告してください。セキュリティに関する報告は [SECURITY.md](../../SECURITY.md) へ。
