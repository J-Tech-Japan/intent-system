# packet 作成と issue 公開

← [ドキュメント索引](index.md) | → [実装ループの設定](05-implementation-loop.md)

これは **host/design** 作業です。packet が正本ファイルを scaffold し、公開境界がレビュー済みの
Standalone Child Issue Contract を GitHub issue にします。以下のプロンプトを AI agent のデザインスレッドに
貼り付けてください。agent が intent-cli コマンドを実行し、結果を返します。

## agent が実行するコマンド（メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。通常、ユーザーが直接実行する必要はありません。メンテナンスやトラブルシューティングの際に参照してください。

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
> intent-cli に次に行うべきことを聞いてください。

## metadata / label の安全境界

- **`intent-target` は公開境界コマンドが付与する。手作業では付けない**。
  child implementation agent も付けない。
- issue 本文は **standalone contract** であること — child agent はそれを唯一の
  source of truth として扱う（host metadata にはアクセスしない）。

## 次へ

[実装ループの設定](05-implementation-loop.md)。
