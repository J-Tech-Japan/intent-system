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

> `<owner>/<repo>` で intent-cli を使い始めたいです。
> intent-cli に現在のフェーズと次に決断すべきことを聞いてください。

agent が内部で `intent-cli` を実行し、質問や結果を返します。
あなたは意図・優先度・承認の判断に集中するだけです。コマンドシーケンスを
記憶する必要はありません。

## intent deepening とは

**intent deepening（意図の深化）** は、AI agent と一緒に「何を作りたいか・なぜ作るか」を整理していくプロセスです。

チャット文脈があるときは一文でも始められます:

> `<やりたいこと>`。intent-cli に聞いて正規の仕方で扱って。

新しいプロダクトやドメインを始めるときは、もう少し詳しいプロンプトを貼るとより充実した質問が得られます:

> intent-cli に聞いて、このプロジェクトの intent を一緒に整理してください。
>
> やりたいことは、`<作りたいプロダクトや機能>` です。
> 大事にしたい方向性は、`<ユーザー価値、事業目的、品質、運用方針>` です。
> 技術的には、`<使いたい言語、クラウド、アーキテクチャ>` を考えています。
> まだ決めきれていない点もあるので、背景・選択肢・メリット/デメリット・推奨理由つきで質問してください。
> 最終的に intent tree と packet に進められる形に整理してください。

AI agent は intent-cli のガイダンスを使い、背景・選択肢・メリット/デメリット・推奨理由つきの構造化された質問を投げかけます。あなたの回答は **intent tree**（発見しやすいフォルダ構造）に整理され、packet（実装タスク）と GitHub issue の土台になります。

技術的な専門知識がなくても始められます。どの選択肢がよいか分からない場合は、AI agent に提案を求めてトレードオフを比較しながら決められます。

詳細は [intent の整理・保守](03-intents.md) を参照してください。

**プロンプトの背後にある唯一のルール:** label/metadata を変更する前に、AI agent は
ファイルを手編集したり GitHub label を手動で適用したりするのではなく、適切な
`intent-cli` コマンドを実行すべきです。以下のすべてのガイドと自動化ページが
このルールを強制しています。

agent が代わりに使うコマンドの全リストは
[コマンドリファレンス](08-command-reference.md)
を参照してください。

## ページ一覧

1. [インストール](01-install.md)
2. [プロジェクト開始](02-project-start.md)
3. [intent の整理・保守](03-intents.md)
4. [packet 作成と issue 公開](04-packets-issues.md)
5. [実装ループの設定](05-implementation-loop.md)
6. [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)
7. [ループがおかしいときの復旧](07-recovery.md)
8. [コマンドリファレンス](08-command-reference.md) — agent 向け・パワーユーザー向けコマンド一覧
9. [開発者リファレンス](09-developer-reference.md) — パッケージ化された実行、preview チャンネル、バージョンフロー

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

## コミュニティ

コミュニティのディスカッションや質問には [J-Tech Japan Discord](https://discord.gg/kMdv978X)
にご参加ください。Discord はカジュアルなサポート窓口であり、正式なサポート SLA はありません。

再現可能なバグやアクションにつながる機能要望は Discord ではなく
[GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) として報告してください。
セキュリティに関する報告は Discord ではなく [SECURITY.md](../../SECURITY.md) へ。
