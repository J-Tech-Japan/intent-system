# プロジェクト開始

> **まず intent-cli に聞く:** `intent-cli guide start` → 案内される
> `design-and-intent` / プロジェクト開始ガイド。 ← [ドキュメント索引](index.md)

これは **host/design** 作業（metadata を触ってよいが、手編集の前に intent-cli へ
現在のコマンドを尋ねる）。

## 初期化と確認

```bash
# host domain を初期化（--write なしは read-only）
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write

# 現在の baseline / WIP / キュー済み packet を確認（read-only）
intent-cli intent status

# 作業サーフェスが期待する内容を尋ねる
intent-cli guide intent-work --format json
```

## ask-intent-cli プロンプトテンプレート

> `<owner>/<repo>` の domain `<name>` を始める前に、`intent-cli guide start` と
> `intent-cli intent status` を実行し、フェーズに対応する guide コマンドに従う。
> label/metadata の変更は intent-cli の遷移を使い、手編集しない。

## metadata / label の安全境界

- `intent-target` や `intent-pr-*` などのワークフロー label は
  `intent-cli automation` / `intent-cli worker` が付与する。手作業では行わない。
- 正本の状態は host repo の `.intent-cli/` にある。intent-cli のサーフェス経由で読み、
  `queue-state.json` を直接編集しない。

## 次へ

[intent の整理・保守](03-intents.md)。
