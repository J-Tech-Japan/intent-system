# packet 作成と issue 公開

← [ドキュメント索引](index.md) | → [実装ループの設定](05-implementation-loop.md)

これは **host/design** 作業です。intent が十分に固まったら、デザインスレッドがそれを **packet**（実行可能な実装単位）に分割し、1つずつ GitHub Issue として公開します。child implementation agent がその issue を受け取って実装します。

## packet とは

**packet** は、intent から切り出された焦点の絞られた実装スライスです。デザインスレッドが正本ファイル一式（`packet.yaml`、`implementation.md`、`review-context.md`、`github-body.md`）を scaffold します。これにより、何を作るかが明確に定義されます。

**issue 公開**はレビュー済みの packet を GitHub Issue に変換します。この issue は **Standalone Child Issue Contract** であり、child implementation agent が実装に必要な唯一の情報源です。child agent は issue 本文とリポジトリのコードを参照するだけです。host metadata にはアクセスしません。

## デザインスレッドプロンプト

AI agent のデザインスレッドに貼り付けてください:

> domain `<name>` の次の packet を作成し、その issue を `<owner>/<repo>` に公開したい。
> intent-cli に次に行うべきことを聞いてください。

AI agent が行うこと:
1. intent-cli で現在の intent と未完了作業を確認する
2. 次の packet を draft する（正本ファイルを scaffold）
3. Standalone Child Issue Contract のレビューを支援する
4. 正しいワークフローラベルで issue を公開する

公開後、issue はターゲットリポジトリに `intent-target` 付きで現れ、child implementation agent が受け取れる状態になります。

## ask-intent-cli プロンプトテンプレート

> packet `<id>` を作成し、その issue を `<owner>/<repo>` に公開する。
> intent-cli に次に行うべきことを聞いてください。

## metadata / label の安全境界

- **`intent-target` は公開境界コマンドが付与する。手作業では付けない**。
  child implementation agent も付けない。
- issue 本文は **standalone contract** であること — child agent はそれを唯一の
  source of truth として扱う（host metadata にはアクセスしない）。

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。通常、ユーザーが直接実行する必要はありません。ワークフローのデバッグや host automation のメンテナンスを行う場合に参照してください。

```bash
# packet を scaffold（packet.yaml / implementation.md / review-context.md / github-body.md）
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --format markdown

# Standalone Child Issue Contract を検証してから公開
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --issue <n> --write --format json
```

## 次へ

[実装ループの設定](05-implementation-loop.md)。
