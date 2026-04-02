計画レポートを作成しました。以下が概要です:

## 計画サマリー

### タスク
グリーンフィールドの `intent-system` リポジトリに、execution の sub-slice データから2種類のパケット（`implementation_issue_packet` / `review_context_packet`）を生成するための **projection schema** を TypeScript + Zod で実装する。

### 技術選定
- **TypeScript + Zod**: スキーマ定義とランタイムバリデーションを型安全に両立
- **Vitest**: テストランナー
- **ESM モジュール**

### ファイル構成 (10ファイル)
- `src/schema/` — 型定義層（4ファイル + index）: 入力スキーマ、2つの出力パケットスキーマ、IssueKind enum
- `src/mapping/` — 変換ロジック層（3ファイル + index）: フィールド解決、2つの変換関数
- `src/index.ts` — パブリック API
- プロジェクト設定: `package.json`, `tsconfig.json`, `vitest.config.ts`

### 設計の要点
- **純粋関数による deterministic 変換**: 同一入力 → 同一出力を保証
- **spec 準拠のフィールド優先順位**: `depends_on_subslices` > `depends_on` 等
- **Scope 補完ルール**: `in_scope`/`out_of_scope` 未指定時のデフォルト生成
- **Markdown/YAML レンダリングは Out of Scope**（A2 担当）