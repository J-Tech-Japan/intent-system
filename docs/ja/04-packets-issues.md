# packet 作成と issue 公開

> **まず intent-cli に聞く:** `intent-cli guide start` →
> `intent-cli guide workflow task packet-draft --format json` と
> `… task issue-publish --format json`。 ← [ドキュメント索引](index.md)

**host/design** 作業。packet が正本ファイルを scaffold し、公開境界がレビュー済みの
Standalone Child Issue Contract を GitHub issue にする。

```bash
# packet を scaffold（packet.yaml / implementation.md / review-context.md / github-body.md）
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --format markdown

# Standalone Child Issue Contract を検証してから公開
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --issue <n> --write --format json
```

## ask-intent-cli プロンプトテンプレート

> packet `<id>` を作成し、その issue を `<owner>/<repo>` に公開する。
> `intent-cli guide start` の後に `packet-draft` と `issue-publish` のワークフロー
> ガイドを実行し、`intent-target` は公開/automation コマンド経由でのみ付与する。

## metadata / label の安全境界

- **`intent-target` は公開境界コマンドが付与する。手作業では付けない**。
  child implementation agent も付けない。
- issue 本文は **standalone contract** であること — child agent はそれを唯一の
  source of truth として扱う（host metadata にはアクセスしない）。

## 次へ

[実装ループの設定](05-implementation-loop.md)。
