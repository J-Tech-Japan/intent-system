# Intent Storming と intent の整理

← [ドキュメント索引](index.md) | → [packet 作成と issue 公開](04-packets-issues.md)

このページは **host/design** 作業です。packet を切り出す前に、AI agent とのデザインスレッドでプロダクト・技術の intent を整理します。

## Intent Storming とは

**Intent Storming** は、コードを書く前に「何を作るか・なぜ作るか・どの制約を受け入れるか」を AI agent と整理し、構造化された intent tree に残す作業です。

最初は大まかな説明から始まります。AI agent は intent-cli のガイダンスを使って、背景・選択肢・メリット/デメリット・推奨理由つきで構造化された質問を投げかけます。あなたが答えるたびに、プロジェクトの方向性・技術的選択・未解決の課題が明確になっていきます。その結果は **intent tree** という発見しやすいフォルダ構造に整理され、packet（実装タスク）と GitHub issue の土台になります。

詳細は [Intent-Driven Development サイトの Intent Storming 提案](https://www.intent-driven-development.com/jp/our-proposals/intent-storming) も参照してください。

技術的な専門知識がなくても始められます。どの選択肢がよいか分からない場合は、AI agent に提案を求めてトレードオフを比較しながら決められます。

## デザインスレッドに貼るプロンプト

AI agent のデザインスレッドに貼り付けてください:

**実用的な短いパターン（チャット文脈があるとき）:**

> `<やりたいこと>`。intent-cli に聞いて正規の仕方で扱って。

「やりたいこと」と目的が AI agent に見えているときは、この一文で十分です。agent が intent-cli のガイダンスを参照し、適切な次のステップを提案します。

**起動プロンプト（すでに intent を持っている場合）:**

> intent-cli に聞いて次に行うべきことを教えてください。

**新しいプロダクト・ドメインを始める場合の詳しいプロンプト:**

> intent-cli に聞いて、このプロジェクトの intent を一緒に整理してください。
>
> やりたいことは、`<作りたいプロダクトや機能>` です。
> 大事にしたい方向性は、`<ユーザー価値、事業目的、品質、運用方針>` です。
> 技術的には、`<使いたい言語、クラウド、アーキテクチャ、イベントソーシングなど>` を考えています。
> まだ決めきれていない点もあるので、背景・選択肢・メリット/デメリット・推奨理由つきで質問してください。
> 最終的に intent tree と packet に進められる形に整理してください。

プロンプトに含められる情報の例:

- プロダクトの目標: 何を作り、誰の役に立つか
- ミッション/バリュー/ビジョン: このプロジェクトが大切にしたいトレードオフの方向性
- 機能要件: システムが行うべきこと
- 非目標: 今回含めないこと
- 技術的な好み: 言語、フレームワーク、データベース、クラウド、イベントソーシング、テストスタイル
- 制約: 予算、チームスキル、デプロイ環境、コンプライアンス、パフォーマンス
- 不確実な部分: AI に提案してほしい選択、トレードオフを知りたい決定
- 判断の理由: なぜその選択が重要かの背景

## AI agent が行うこと

デザインスレッドでプロンプトを貼ると、AI agent は:

1. `intent-cli guide workflow` や `intent-cli intent status` を内部で実行し、現在の状態を確認する
2. 未解決の決定事項について、構造化された質問を投げかける
3. あなたの回答を `intent-cli interview record-answer` で永続化する
4. 整理された内容を intent tree の適切なフォルダに分類する（詳細は後述）

## 構造化された質問のスタイル

AI agent の質問はこの形をとります:

- **現在の理解**: 現時点でわかっていること
- **背景 / なぜ重要か**: この決定が後続のパケット・実装にどう影響するか
- **質問**: 1つに絞った明確な問い
- **選択肢**: 2〜4つの具体的な選択肢
- **メリット/デメリット**: 各選択肢のトレードオフ
- **推奨**: agent の推奨する選択
- **推奨理由**: なぜその選択を推奨するか
- **決まること**: この答えが intent tree またはパケットの何を確定させるか

## なぜアドホックなチャットより優れているか

- 会話の結果が**永続的な intent tree**に残り、後から参照・更新できる
- 構造化された質問形式で、見落としがちな観点（セキュリティ、運用、移行制約など）をカバーできる
- packet → GitHub issue → 実装ループという一貫したトレーサビリティが生まれる
- 複数のセッションや担当者をまたいでも、意思決定の文脈が失われない

## 生まれるアーティファクト

| 会話の内容 | 生成されるアーティファクト |
|---|---|
| 技術的な選択・決定事項 | `decisions/` ADR スタイルのノート、`technology/` |
| 未解決の問い | `clarifications/open.md` |
| 機能要件・ユーザーストーリー | `features/<slug>/` |
| 実行可能なスライス | `packets/` → GitHub issue |
| ミッション/バリュー/ビジョン | `identity/` |

## ask-intent-cli プロンプトテンプレート

> domain `<name>` の intent を整理する。intent-cli に次に行うべきことを聞いてください。

## metadata / label の安全境界

- interview/draft アーティファクトは `intent-cli interview …` 経由で書き込む
  （ここでの変更は `record-answer` のみ）。永続 Q/A ファイルを手編集しない。
- child implementation agent は intent tree（`intents/**`）や host metadata を
  **読まない** — これは host/design の領域。

## Intent ナレッジツリーレイアウト (tree-v1)

新規ドメインは、単一のフラットファイルではなく発見しやすいフォルダに intent を整理することを推奨します。
**tree-v1** レイアウトは推奨カテゴリ（`identity`、`product`、`features`、`technology`、`operations`、`decisions`、`clarifications`、`packets`、`links`）と、カスタムフォルダ名およびプロジェクトタイプをサポートするマニフェストスキーマを定義します。

```bash
# ツリーレイアウト作成の現在のガイダンスを取得
intent-cli guide intent-work setup \
  --kind tree-layout \
  --domain <name> \
  --target-repo <owner/repo> \
  --format markdown
```

完全な仕様、マニフェストスキーマ、プロジェクトタイプの例、相互リンクのルールは [Intent ナレッジツリーレイアウト (tree-v1)](03a-intent-tree-layout.md) を参照してください。

## 次へ

[packet 作成と issue 公開](04-packets-issues.md)。
