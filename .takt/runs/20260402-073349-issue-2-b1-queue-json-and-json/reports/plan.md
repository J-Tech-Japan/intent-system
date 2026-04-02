# タスク計画

## 元の要求

Issue #2: [B1] Queue JSON And JSONL Schema — `.intent-cli/queue-state.json` と `.intent-cli/runs.jsonl` の最小 schema を、selective block と run trace が復元できる形で固定する。

## 分析結果

### 目的

新規リポジトリ (intent-system、現状 README.md のみ) に、queue current state (`queue-state.json`) と run history (`runs.jsonl`) のスキーマを TypeScript モジュールとして実装し、後続の B2 Queue Manager が利用できる型定義と操作関数を提供する。

### 参照資料の調査結果

| 参照資料 | 調査結果 |
|---------|---------|
| [03-queue-json-and-jsonl-schema.md](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md) | queue-state.json の最小構造（schema_version, updated_at, items[]）、runs.jsonl の最小構造（ts, execution_unit, event, by）、state 値 7 つ（queued/active/review/fixing/clarify-blocked/blocked/completed）、更新ルール、制約を定義。これがスキーマ実装のソース・オブ・トゥルース |
| [08-config-and-run-model.md](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/specs/08-config-and-run-model.md) | config.toml の責務定義。config は policy/baseline、queue-state/runs は runtime state として責務分離することを確認。B1 スコープ外だが設計境界の裏取りに使用 |
| [03-bootstrap-manual-operation.md](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/rules/03-bootstrap-manual-operation.md) | bootstrap phase での手動更新許可ルール。スキーマが手動編集にも耐える形（JSON/JSONL テキスト）であることを確認 |
| [05-persistence-strategy.md](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/intent-tree/means/05-persistence-strategy.md) | JSON for current state, JSONL for append-only log, file artifact として commit 対象可の方針を確認 |

現在の実装との差異: リポジトリが空のため差異なし。全て新規作成。

### スコープ

| 対象 | 内容 |
|------|------|
| 新規作成 | プロジェクト初期設定（package.json, tsconfig.json, vitest.config.ts） |
| 新規作成 | `src/supervisor/state.ts` — state 値の Zod enum 定義 |
| 新規作成 | `src/supervisor/queue-state.ts` — QueueState スキーマ + パース/クエリ操作 |
| 新規作成 | `src/supervisor/run-log.ts` — RunEvent スキーマ + JSONL パース/追記操作 |
| 新規作成 | `src/supervisor/index.ts` — パブリック API エクスポート |
| 新規作成 | `tests/supervisor/` 配下のテストファイル |

### 検討したアプローチ

| アプローチ | 採否 | 理由 |
|-----------|------|------|
| TypeScript + Zod | **採用** | ランタイム検証 + 型推論の両立。ナレッジで Zod スキーマに言及あり。CLI ツーリングに適合 |
| TypeScript + 手動型ガード | 不採用 | バリデーションコードが冗長になり、スキーマ変更時の追従コストが高い |
| JSON Schema ファイルのみ | 不採用 | 型安全性なし。操作関数を提供できない。後続 B2 が利用しにくい |

| 設計判断 | 採否 | 理由 |
|---------|------|------|
| ファイル I/O をモジュールに含めない | **採用** | このモジュールは「supervisor state model」。ファイル読み書きは上位層 B2 Queue Manager の責務。文字列 in/out で責務を分離 |
| Zod スキーマをパブリック API に含めない | **採用** | ナレッジ「パブリック API の公開範囲」: インフラ実装詳細を公開しない。消費者はドメイン型と操作関数のみ使用 |
| `linked_issue` をオプショナル | **採用** | スペック: 「実 Issue を起票した場合は linked_issue を queue item から引けるようにして**よい**」= optional |
| `event` に `"resumed"` を追加 | **採用** | スペック更新ルール: 「queue resume したら runs.jsonl に明示 event を残す」。state 値とは別のイベント種別が必要 |

### 実装アプローチ

**ディレクトリ構造:**

```
intent-system/
├── package.json
├── tsconfig.json
├── vitest.config.ts
├── src/
│   └── supervisor/
│       ├── index.ts              # パブリック API
│       ├── queue-state.ts        # queue-state.json スキーマ + 操作
│       ├── run-log.ts            # runs.jsonl スキーマ + 操作
│       └── state.ts              # QueueItemState enum
└── tests/
    └── supervisor/
        ├── queue-state.test.ts
        ├── run-log.test.ts
        └── state.test.ts
```

**実装順序:** `state.ts` → `queue-state.ts` → `run-log.ts` → `index.ts`

**型の概要:**

- `QueueItemState`: `"queued" | "active" | "review" | "fixing" | "clarify-blocked" | "blocked" | "completed"`
- `PacketPaths`: `{ implementation: string, review_context: string, yaml: string }`
- `LinkedIssue`: `{ repo: string, number: number, url: string }`（optional フィールド）
- `QueueItem`: `{ execution_unit, title, state, dependencies, blocked_by, clarification_return_path, packet_paths, linked_issue?, worker_role, review_role, priority }`
- `QueueState`: `{ schema_version: "1", updated_at: string (ISO-8601), items: QueueItem[] }`
- `RunEvent`: `{ ts: string (ISO-8601), execution_unit: string, event: QueueItemState | "resumed", by: string }`

**操作関数:**

| モジュール | 関数 | 責務 |
|-----------|------|------|
| queue-state | `parseQueueState(json: string): QueueState` | JSON パース + Zod バリデーション |
| queue-state | `serializeQueueState(state: QueueState): string` | 整形 JSON 出力（2 スペース + 末尾改行、diff-friendly） |
| queue-state | `findItemByUnit(state: QueueState, unit: string): QueueItem \| undefined` | execution_unit で検索 |
| queue-state | `findItemsByState(state: QueueState, s: QueueItemState): QueueItem[]` | state でフィルタ |
| queue-state | `getBlockedItems(state: QueueState): QueueItem[]` | blocked_by が空でないアイテム取得 |
| queue-state | `resolvePacketPaths(item: QueueItem): PacketPaths` | packet path 取得 |
| run-log | `parseRunLog(jsonl: string): RunEvent[]` | JSONL パース + 行ごと Zod バリデーション |
| run-log | `serializeRunEvent(event: RunEvent): string` | 1 イベントを JSON 行に変換 |
| run-log | `appendRunEvent(existingJsonl: string, event: RunEvent): string` | JSONL に追記 |
| run-log | `filterByUnit(events: RunEvent[], unit: string): RunEvent[]` | execution_unit フィルタ |
| run-log | `getTransitionHistory(events: RunEvent[], unit: string): RunEvent[]` | 特定ユニットの遷移履歴 |

## 実装ガイドライン

- **プロジェクト設定**: `package.json` に `"type": "module"`、Zod (`zod`) と Vitest (`vitest`) を依存追加。`tsconfig.json` は `strict: true`、`module: "NodeNext"`、`target: "ES2022"`
- **Zod スキーマは各ファイル内に閉じる**: `queue-state.ts` と `run-log.ts` の内部で定義し、`index.ts` からはエクスポートしない。型のみ `z.infer<>` で推論してエクスポート
- **イミュータブル操作**: 配列の `push`/`splice` は使用禁止。スプレッド演算子や `filter`/`map` を使用
- **エラーハンドリング**: パース失敗時は Zod の `ZodError` をそのままスロー。握りつぶし禁止
- **バリデーション詳細**:
  - `schema_version` は `z.literal("1")` で固定
  - `updated_at` / `ts` は `z.string().datetime()` で ISO-8601 検証
  - `priority`, `worker_role`, `review_role` は `z.string()` （enum 制限なし、値は config.toml 側の責務）
  - `clarification_return_path` は `z.string()`（パス形式の検証は不要）
- **シリアライズ**: `serializeQueueState` は `JSON.stringify(state, null, 2) + "\n"`。`serializeRunEvent` は `JSON.stringify(event)`（末尾改行なし、`appendRunEvent` 側で改行追加）

## スコープ外

| 項目 | 除外理由 |
|------|---------|
| ファイル I/O（fs.readFile/writeFile） | 上位層 B2 Queue Manager の責務。モデル層は文字列 in/out |
| config.toml のスキーマ | 別タスク（スペック 08 で定義済み、B1 スコープ外） |
| dependency update ロジック | Issue order.md の Out Of Scope に明記 |
| workflow engine 実行 | Issue order.md の Out Of Scope に明記 |
| clarify/interview artifact の詳細 schema | Issue order.md の Out Of Scope に明記 |