# intent の整理・保守

> **まず intent-cli に聞く:** `intent-cli guide start` →
> `intent-cli guide workflow task intent-interview --format json`。 ← [ドキュメント索引](index.md)

**host/design** 作業: slice を切り出す前に、永続的な intent を収集・コンパイルする。

```bash
# domain ごとの永続 Q/A アーティファクト
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile

# 推奨フロー / 準備状況
intent-cli guide workflow --format json
intent-cli intent status --format json
```

## ask-intent-cli プロンプトテンプレート

> domain `<name>` の intent を整理する。`intent-cli guide start` の後に
> `intent-interview` ワークフローガイドを実行し、`intent-cli interview …` で
> 回答を記録する。ルールを発明せず、現在のガイドを intent-cli に尋ねる。

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
