## arch-review
## Architecture Review Result: **REJECT**

### 前回指摘の追跡

| # | finding_id | 状態 | 根拠 |
|---|------------|------|------|
| 1 | F-API-INTERNAL-EXPORTS | **persists** | `src/supervisor/index.ts:6,17` で `queueStateSchemaVersion` / `resumedRunEvent` が依然として re-export されている。grep 確認済み: どの外部ファイルからも import されていない |
| 2 | F-COMMENT-WHAT-HOW | **persists** | 全4テストファイルに Given/When/Then コメントが合計66箇所残存 |
| 3 | F-DUPLICATE-METHODS-RUNLOG | **persists** | `src/supervisor/run-log.ts:42-44` の `getTransitionHistory` が `filterByUnit` と完全同一実装のまま |

---

### 詳細

#### F-API-INTERNAL-EXPORTS (persists) — ブロッキング

**箇所:** `src/supervisor/queue-state.ts:5`, `src/supervisor/run-log.ts:5`, `src/supervisor/index.ts:6,17`

**問題:** `queueStateSchemaVersion` と `resumedRunEvent` はスキーマ定義で内部的に使用される定数であり、ドメイン操作関数でも型でもない。Plan にも公開 API として記載されておらず（Plan の「操作関数」テーブルに含まれていない）、`coder-decisions.md` の設計判断「複合 schema は内部に閉じる」とも不整合。grep で確認済み: 外部からの import は 0 件。

ナレッジ判定基準: 「内部実装の関数が外部から直接呼び出し可能になっている → REJECT」

**修正案:**
1. `src/supervisor/queue-state.ts:5` — `export const` → `const` に変更
2. `src/supervisor/run-log.ts:5` — `export const` → `const` に変更
3. `src/supervisor/index.ts:6` — `queueStateSchemaVersion,` の行を削除
4. `src/supervisor/index.ts:17` — `resumedRunEvent,` の行を削除

---

#### F-COMMENT-WHAT-HOW (persists) — ブロッキング

**箇所:** `tests/supervisor/state.test.ts`, `queue-state.test.ts`, `run-log.test.ts`, `index.test.ts` の全テスト内（66箇所）

**問題:** `// Given:`, `// When:`, `// Then:` コメントがコードの動作をそのまま自然言語で言い換えている。例:

- `state.test.ts:7` `// Given: the documented supervisor states` — テスト名 `should expose the documented queue item states in order` と重複
- `state.test.ts:18` `// When: the exported values are read` — 次の行 `const actualStates = queueItemStateValues` の言い換え
- `queue-state.test.ts:16` `// Given: a queue snapshot that can restore selective blocking state` — テスト名で表現済み

ナレッジ判定基準: 「コードの動作をそのまま自然言語で言い換えている → REJECT」

**修正案:** 全テストファイルから `// Given:`, `// When:`, `// Then:` コメント行を削除する。テストの意図はテスト名で十分表現されている。

---

#### F-DUPLICATE-METHODS-RUNLOG (persists) — ブロッキング

**箇所:** `src/supervisor/run-log.ts:42-44`

```typescript
export function getTransitionHistory(events: RunEvent[], unit: string): RunEvent[] {
  return filterByUnit(events, unit)
}
```

**問題:** `getTransitionHistory` は `filterByUnit` と完全に同一の実装。Plan に両方記載されているが、現時点で振る舞いに差がなく、ポリシー「同じことをするメソッドの増殖（構成の違いで吸収すべき）→ REJECT」に該当。将来の差別化を理由とした未使用コードの先行配置は YAGNI 違反。

**修正案:** `getTransitionHistory` を削除し、呼び出し側（`index.ts` の re-export、`run-log.test.ts:115-132` のテスト、`index.test.ts:8,44` の import/使用）を `filterByUnit` に統一する。将来「遷移イベントのみフィルタ」等の差別化が必要になった時点で、その振る舞いとともに関数を導入する。

---

### 構造・設計の評価（新規問題なし）

| 観点 | 結果 | 備考 |
|------|------|------|
| ファイル分割 | ✅ | 全ファイル67行以下。1ファイル1責務 |
| モジュール構成 | ✅ | `state.ts` ← `queue-state.ts`, `run-log.ts` → `index.ts`。循環依存なし |
| 依存方向 | ✅ | 下位層（state）→ 上位層（queue-state/run-log）→ バレル（index）。逆方向なし |
| 操作の一覧性 | ✅ | `queue-state.ts` にキュー操作、`run-log.ts` にログ操作が集約。モジュールを読めば全体像がわかる |
| イミュータブル操作 | ✅ | `filter`/`find`/`map`/スプレッド演算子のみ使用。直接変更なし |
| エラーハンドリング | ✅ | Zod の `ZodError` をそのままスロー。握りつぶしなし |
| テストカバレッジ | ✅ | 全公開関数にテストあり。正常系・異常系・境界値を網羅 |
| 仕様準拠 | ✅ | state 値7種、スキーマ構造、resumed イベント、packet_paths — 全て仕様準拠 |
| 変更スコープ | Small（約350行） | 論理的にまとまっており、無関係な変更なし |
| デッドコード | — | 上記 F-API-INTERNAL-EXPORTS, F-DUPLICATE-METHODS-RUNLOG で指摘済み |

### REJECT 判定条件

`persists` が 3 件あるため **REJECT**。全3件が解消されれば APPROVE 可能。

---

## supervise
## 最終検証結果: **REJECT**

### 要件充足: 全10要件 ✅
タスク指示書の Acceptance Criteria 3件 + Review Context 3件を細分化した10要件すべてが実コードで充足確認済み。機能的な問題はありません。

### REJECT理由: 未対応の前段レビュー finding 2件

1. **F-COMMENT-WHAT-HOW（妥当）**: テスト全4ファイルの Given/When/Then コメントがポリシーの「説明コメント（What/How）禁止」に該当。テスト名がすでに十分に説明的なため、コメント削除のみで解消可能。

2. **F-API-INTERNAL-EXPORTS（部分的妥当）**: `index.ts` からの `queueStateSchemaVersion` / `resumedRunEvent` の re-export が plan の公開 API テーブルに含まれておらず、plan 超過。re-export を削除してモジュール内部定数に閉じるべき。

### 前段 finding の overreach 判定 1件

- **F-DUPLICATE-METHODS-RUNLOG**: `getTransitionHistory` と `filterByUnit` は plan が明示的に異なるセマンティクスで設計した API。DRY違反ポリシーの「plan にない追加抽象化を要求」条項に照らし overreach。**対応不要**。

### テスト/ビルド証跡
implement ムーブメントの自己レポート（22 tests passed, tsc 成功）のみ。独立した実行ログはなし。