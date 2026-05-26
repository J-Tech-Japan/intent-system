# intent-cli ドキュメント（日本語）

> English version: [`../en/index.md`](../en/index.md)

`intent-cli` は、GitHub 上で intent 駆動の開発ワークフローを回すための
**決定論的なサポートツール** です。これらのページは、内部設計ノートを全部読まなくても
[ルート README](../../README.md) より少しだけ構造化された案内を提供します。

## intent-cli の使い方

`intent-cli` は AI agent — Claude、Codex、Copilot など、リポジトリアクセスを持つ
有能なコーディングアシスタント — に動かしてもらうことを前提に設計されています。
コマンドを自分で記憶・実行する必要はありません。

**人間の典型的な操作手順:**

1. `intent-cli` をインストールし、`intent-cli --version` で確認する。
2. AI agent のデザインスレッドを開く。
3. 次のようなプロンプトを貼り付ける:

> `<owner>/<repo>` の domain `<name>` について、intent-cli に現在のフェーズを
> 聞いてください。`intent-cli guide start` と `intent-cli intent status` を実行し、
> 次に私が決断すべきことを教えてください。

agent が内部で `intent-cli` を実行し、質問や結果を返します。
あなたは意図・優先度・承認の判断に集中するだけです。コマンドシーケンスを
記憶する必要はありません。

**プロンプトの背後にある唯一のルール:** label/metadata を変更する前に、AI agent は
ファイルを手編集したり GitHub label を手動で適用したりするのではなく、適切な
`intent-cli` コマンドを実行すべきです。以下のすべてのガイドと自動化ページが
このルールを強制しています。

agent が代わりに使うコマンドの全リストは
[README コマンドリファレンス](../../README.md#command-reference-agent-facing--power-users)
を参照してください。

## ページ一覧

1. [インストール](01-install.md)
2. [プロジェクト開始](02-project-start.md)
3. [intent の整理・保守](03-intents.md)
4. [packet 作成と issue 公開](04-packets-issues.md)
5. [実装ループの設定](05-implementation-loop.md)
6. [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)
7. [ループがおかしいときの復旧](07-recovery.md)

## 2 つの agent ロール（最初に一度だけ読む）

| ロール | source of truth | 責務 |
| --- | --- | --- |
| **Host / review agent** | 親 host の `.intent-cli/` 状態 + intent tree | issue 公開、`intent-target` 付与、review/approve/merge、next slice 切り出し、`intent-cli automation` 経由の label 遷移 |
| **Child implementation agent** | **GitHub の issue/PR + repo ローカルのコード**（host metadata ではない） | issue 契約の実装、PR の作成/更新、`intent-cli worker` での結果記録 |

Child implementation agent は **GitHub-contract-only**: host の `.intent-cli/`、
queue-state、metadata branch、`intents/**` を読んだり変更したりしない。
Host/review agent は metadata を扱ってよいが、手編集の前に `intent-cli` へ現在の
コマンドを尋ね、その遷移を優先する。

host は **別の host リポジトリ** に置くこともできますし、**同じリポジトリの専用 metadata ブランチ**
（例: `main-metadata`）に置くこともできます。どちらのトポロジーも完全にサポートされています。
どちらを選ぶかは [プロジェクト開始 → リポジトリトポロジーの選択](02-project-start.md#リポジトリトポロジーの選択)
を参照してください。
