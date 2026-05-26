# レビュー / next-slice ループの設定

> ← [ドキュメント索引](index.md)

これは **host/review** 作業。PR を packet/intent 契約に照らしてレビューし、更新を
要求し、approve/merge し、next slice を切り出す。host metadata を扱ってよいが、
常に `intent-cli` がサポートする遷移を使う。

## デザインスレッドプロンプト

AI agent（Claude、Codex、Copilot など）に貼り付けてください:

> domain `<name>` / `<owner>/<repo>` の host review / next-slice ループを回してください。
> まず以下を実行してフルループの prompt を取得し、そのとおりに従ってください:
> `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>`
> label 遷移はすべて `intent-cli automation` 経由で行い、手動で label を付けないでください。
> approve は green テストだけでなく packet/intent の証跡に紐づけてください。

## agent が実行するコマンド（リファレンス）

```bash
# レビュー / next-slice の正本 prompt を取得
intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>

# PR 固有のレビューガイド（チェックリスト、packet 参照、approve/request-update 要件）
intent-cli guide review --pr <n> --repo <owner>/<repo> --format json

# label 遷移（review-start、request-update、approve …）— 手作業では行わない
intent-cli automation pr-transition --transition <name> --write --format json
```

## metadata / label の安全境界

- レビュー label 遷移（`intent-pr-reviewing`、`intent-pr-request-update`、
  `intent-pr-approved` …）は `intent-cli automation` が付与する。手作業では行わない。
- テスト通過は **必要だが十分ではない** — approve には packet/intent への
  適合証跡が必要（`guide review` 参照）。
- 現在 PR の受け入れ基準ブロッカーは、request-update/clarification として完了する前に
  永続的な PR コメントを残す（[復旧](07-recovery.md) 参照）。

## 次へ

[ループがおかしいときの復旧](07-recovery.md)。
