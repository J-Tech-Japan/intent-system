依頼内容に従って、まず `.takt/.../context/task` の指示、最新版レビューレポート、その直近履歴、そして policy/knowledge の原本を確認します。指摘の `family_tag` と対象ファイルを特定してから、必要な修正と再発防止テストを入れます。
タスク指示と固定ルールは確認できました。次に、レポートの配置と最新/直近履歴を特定して、`new` と `reopened` の対象を絞ります。
レビュー履歴の起点は `reports/` 側です。いまから各レポート名ごとに最新と直近履歴を見て、`persists / reopened` の傾向と今回必須の修正対象を確定します。
今回の修正対象はほぼ固まりました。実装前に、実コードと `plan/coder-decisions` を突き合わせて、消すべき公開APIと維持すべき関数契約を確認します。
実装方針は明確です。`F-DUPLICATE-METHODS-RUNLOG` は最新の監督レポートで overreach 扱いなので触らず、`api-surface` と `comment-style` を直接修正し、対応する再発防止テストを追加します。
編集前に最後の確認として、既存のスクリプトは `npm run build` / `npm run test` で足ります。これから公開APIの絞り込み、既存テストコメントの削除、再発防止テストの追加をまとめて入れます。
`api-surface` と `comment-style` を対象に直接編集します。あわせて、公開API契約と禁止コメントの再発防止を検証するテストを追加します。
直接修正は入りました。ここで禁止コメントの残りと公開API露出の残りを機械的に確認してから、ビルドとテストを実行します。
ビルドとテストは通りました。最後に差分と行番号を確認して、レビューファミリーごとの収束状況を整理します。
## 作業結果
- 最新のレビュー履歴は [ai-review.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/ai-review.md)、[architect-review.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/architect-review.md)、[supervisor-validation.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/supervisor-validation.md) を確認し、最新評価に従って `api-surface` と `comment-style` を修正しました。
- `F-DUPLICATE-METHODS-RUNLOG` は最新の [supervisor-validation.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/supervisor-validation.md) で overreach / resolved 扱いだったため、実装は変更していません。

## 変更内容
- 公開 API から内部定数の re-export を削除しました。[index.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/src/supervisor/index.ts#L1)
- 既存の supervisor テスト 4 ファイルから `Given/When/Then` コメントを全削除しました。[index.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/index.test.ts) [queue-state.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/queue-state.test.ts) [run-log.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/run-log.test.ts) [state.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/state.test.ts)
- 再発防止として、barrel の公開 API 契約と禁止コメント不在を検証するテストを追加しました。[contracts.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/contracts.test.ts#L1)
- `?raw` import 用の型宣言を追加しました。[raw-modules.d.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/raw-modules.d.ts#L1)

## ビルド結果
- `npm run build` 成功
- 実行内容: `tsc --noEmit`

## テスト結果
- `npm run test` 成功
- 実行内容: `vitest run`
- 結果: 5 test files, 24 tests passed

## 収束ゲート
| 指標 | 件数 |
|------|------|
| new（今回修正） | 0 |
| reopened（再発修正） | 0 |
| persists（前回から継続、今回未着手） | 0 |

## 証拠
- 最新レビューで未解消扱いだった `F-API-INTERNAL-EXPORTS` と `F-COMMENT-WHAT-HOW` を対象化しました。[architect-review.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/architect-review.md) [supervisor-validation.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/supervisor-validation.md)
- barrel から `queueStateSchemaVersion` / `resumedRunEvent` が消えていることをコードとテストで確認しました。[index.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/src/supervisor/index.ts#L1) [contracts.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/contracts.test.ts#L11)
- supervisor テスト群に `Given/When/Then` コメントが残っていないことを契約テストで固定しました。[contracts.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/contracts.test.ts#L17)