# 変更スコープ宣言

## タスク
projection schema の公開 API と deterministic な packet 変換を実装して既存テストを通す

## 変更予定
| 種別 | ファイル |
|------|---------|
| 作成 | `src/schema/issue-kind.ts` |
| 作成 | `src/schema/projection-input.ts` |
| 作成 | `src/schema/implementation-issue-packet.ts` |
| 作成 | `src/schema/review-context-packet.ts` |
| 作成 | `src/schema/index.ts` |
| 作成 | `src/mapping/field-resolvers.ts` |
| 作成 | `src/mapping/project-to-implementation-packet.ts` |
| 作成 | `src/mapping/project-to-review-context-packet.ts` |
| 作成 | `src/mapping/index.ts` |
| 作成 | `src/index.ts` |
| 作成 | `package.json` |
| 作成 | `tsconfig.json` |
| 作成 | `vitest.config.ts` |

## 推定規模
Medium

## 影響範囲
- projection input schema
- implementation issue packet schema
- review context packet schema
- field mapping / public API
- build / test configuration