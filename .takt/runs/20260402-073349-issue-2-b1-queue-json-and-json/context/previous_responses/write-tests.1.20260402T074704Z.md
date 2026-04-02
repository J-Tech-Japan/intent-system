依頼は `write_tests` ムーブメントとして理解しています。まずタスク仕様、ポリシー全文、今回のレポート履歴、既存のテスト配置を確認して、変更スコープを確定します。
仕様とポリシーは確認しました。レポートファイルはまだ存在せず、リポジトリ本体もほぼ空なので、次に task コンテキストと隠しファイル込みの構成を見て、テスト配置を決めます。
レポート履歴は未生成なので、今回は `order.md` とリポジトリ実態だけで契約を固定します。既存コード・既存テストは見当たらないため、実装予定モジュールに沿った新規テスト配置で進めます。
`reports/plan.md` は存在するので、これを今回の一次履歴として参照します。加えて、ポリシーとナレッジは全文確認が必要なので残りを読み、そこからテスト対象を確定します。
これから編集に入ります。先に `test-scope.md` を作成して変更範囲を固定し、その後に `tests/supervisor/` 配下の契約テストを追加します。
テストは `tests/supervisor/` に揃えます。単体は `state` / `queue-state` / `run-log` ごとに分け、状態が 3 モジュール以上を跨ぐため `index.ts` 経由の統合テストも 1 本追加します。
テスト本体は追加しました。次に内容を見直して、脆いアサーションや不要な前提がないかを詰め、必要なら決定ログを残します。
`state.test.ts` のエラー検証だけは Zod の文言差分に引きずられやすいので、挙動中心に寄せます。あわせて統合テストは `findItemByUnit` の結果存在確認を先に置いて前提を明示します。
必要なテストと決定ログは揃いました。最後に差分を確認して、スコープ宣言との整合だけ見ます。
## 作業結果
- `queue-state.json` と `runs.jsonl` の契約を固定するテストを追加しました。単体テストは state / queue-state / run-log ごとに分離し、公開 API 経由で snapshot と history を復元する統合テストも追加しています。
- 変更スコープ宣言を [test-scope.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/test-scope.md) に作成し、統合テスト追加の判断を [test-decisions.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/test-decisions.md) に記録しました。
- テスト実行は未実施です。現時点ではプロジェクト設定やプロダクションコードが未作成のため、このムーブメントでは契約テストの追加までに留めています。

## 変更内容
- [tests/supervisor/fixtures.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/fixtures.ts)
- [tests/supervisor/state.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/state.test.ts)
- [tests/supervisor/queue-state.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/queue-state.test.ts)
- [tests/supervisor/run-log.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/run-log.test.ts)
- [tests/supervisor/index.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/tests/supervisor/index.test.ts)
- [test-scope.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/test-scope.md)
- [test-decisions.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-2-issue-2-b1-queue-json-and-json/.takt/runs/20260402-073349-issue-2-b1-queue-json-and-json/reports/test-decisions.md)