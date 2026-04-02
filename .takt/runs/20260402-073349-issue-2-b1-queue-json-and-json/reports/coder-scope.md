# 変更スコープ宣言

## タスク
Queue JSON / JSONL supervisor state model を実装し既存テストを通す

## 変更予定
| 種別 | ファイル |
|------|---------|
| 作成 | `.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/coder-scope.md` |
| 作成 | `package.json` |
| 作成 | `package-lock.json` |
| 作成 | `tsconfig.json` |
| 作成 | `vitest.config.ts` |
| 作成 | `src/supervisor/state.ts` |
| 作成 | `src/supervisor/queue-state.ts` |
| 作成 | `src/supervisor/run-log.ts` |
| 作成 | `src/supervisor/index.ts` |

## 推定規模
Medium

## 影響範囲
- `tests/supervisor/` が固定している supervisor state model 契約
- `src/supervisor/` の公開 API
- プロジェクトのビルド / テスト実行設定