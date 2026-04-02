# 変更スコープ宣言

## タスク
Queue JSON / JSONL supervisor state model の振る舞いを固定するテストを追加する

## 変更予定
| 種別 | ファイル |
|------|---------|
| 作成 | `.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/test-scope.md` |
| 作成 | `tests/supervisor/fixtures.ts` |
| 作成 | `tests/supervisor/state.test.ts` |
| 作成 | `tests/supervisor/queue-state.test.ts` |
| 作成 | `tests/supervisor/run-log.test.ts` |
| 作成 | `tests/supervisor/index.test.ts` |

## 推定規模
Medium

## 影響範囲
- `src/supervisor/state.ts` の state schema 契約
- `src/supervisor/queue-state.ts` の queue-state.json 契約
- `src/supervisor/run-log.ts` の runs.jsonl 契約
- `src/supervisor/index.ts` の公開 API 契約