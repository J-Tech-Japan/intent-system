計画レポートを作成しました。

## サマリ

**タスク:** B1 - Queue JSON And JSONL Schema（supervisor state model の初期実装）

**概要:** 新規リポジトリ (intent-system) に、`queue-state.json` と `runs.jsonl` のスキーマを TypeScript + Zod モジュールとして実装する。

**主な成果物:**

| ファイル | 責務 |
|---------|------|
| `src/supervisor/state.ts` | 7つの state 値（queued〜completed）の Zod enum 定義 |
| `src/supervisor/queue-state.ts` | QueueState/QueueItem 型定義 + パース/クエリ操作 |
| `src/supervisor/run-log.ts` | RunEvent 型定義 + JSONL パース/追記操作 |
| `src/supervisor/index.ts` | パブリック API エクスポート（型+操作関数のみ、Zod スキーマは非公開） |

**設計判断のポイント:**
- ファイル I/O はスコープ外（上位層 B2 の責務）。文字列 in → 文字列 out
- `linked_issue` はオプショナル（スペックの「してよい」に準拠）
- `event` フィールドに `"resumed"` を追加（スペックの resume イベント要件）
- Zod スキーマはパブリック API に含めない（インフラ詳細の隠蔽）