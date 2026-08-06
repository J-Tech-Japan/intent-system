# packet 作成と issue 公開

← [intent の整理・保守](03-intents.md) | [ドキュメント索引](README.md) | → [agent メッセージオーケストレーション](12-agent-message-orchestration.md)

これは **host/design** 作業です。intent が十分に固まったら、デザインスレッドがそれを **packet**（実行可能な実装単位）に分割し、1つずつ GitHub Issue として公開します。子実装 agent がその issue を受け取って実装します。

## packet とは

**packet** は、intent から切り出された焦点の絞られた実装スライスです。デザインスレッドが正本ファイル一式（`packet.yaml`、`implementation.md`、`review-context.md`、`github-body.md`）を scaffold します。これにより、何を作るかが明確に定義されます。`review-context.md` には、その packet の `intent_references` と overlap する G529 semantic-facet node（vocabulary/invariant/decider/acceptance-property）を一覧化した、生成済みの **Facet context** セクションが含まれます — 詳細は [facet を意識した context 供給 (G530)](09-developer-reference.md#facet-を意識した-context-供給-g530) を参照してください。

**issue 公開**はレビュー済みの packet を GitHub Issue に変換します。この issue は **Standalone Child Issue Contract** であり、子実装 agent が実装に必要な唯一の情報源です。子 agent は issue 本文とリポジトリのコードを参照するだけです。ホストメタデータにはアクセスしません。

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
  子実装 agent も付けない。
- issue 本文は **standalone contract** であること — 子 agent はそれを唯一の
  正本となる定義として扱う（ホストメタデータにはアクセスしない）。

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。通常、ユーザーが直接実行する必要はありません。ワークフローのデバッグや host automation のメンテナンスを行う場合に参照してください。

```bash
# packet を scaffold（packet.yaml / implementation.md / review-context.md / github-body.md）
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --format markdown

# Standalone Child Issue Contract を検証してから公開
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
# 記録済み unit または issue 番号で intent-target を付与
intent-cli automation issue-publish --execution-unit <id> --write --format json
# issue 番号が既知の場合の同等な代替:
intent-cli automation issue-publish --issue <n> --write --format json
```

## 代替: timer-loop のセットアップ

timer-loop の alternative を選ぶときだけ、[実装ループの設定](05-implementation-loop.md)、続けて
[レビュー / next-slice ループの設定](06-review-next-slice-loop.md) を使います。

## 次へ

[agent メッセージオーケストレーション](12-agent-message-orchestration.md)。
