指定の `context/task` と Report Directory を一次情報として読み、既存テストが要求している公開契約を確認したうえで実装します。まず `order.md`、実装ポリシー全文、ナレッジ元ファイル、Report Directory のレポート、追加済みテストを順に確認します。
仕様とポリシーは確認しました。次に Report Directory の `plan.md` と追加済みテストを読み、実装すべき公開 API と具体的な変換規則を固定します。
テストが唯一の実装契約になっています。ここから実装用のスコープ宣言を Report Directory に追加し、その後 `schema`、`mapping`、公開 API、実行設定を最小構成で作ります。
実装ファイルと実行設定をまとめて追加します。テスト契約にない挙動は入れず、`issue_kind` と抽出ルールだけ定数化して 1 箇所に寄せます。
実装本体は入りました。ここから依存関係を入れて型チェック前の確認を行い、必要なら import/export や型のズレだけ詰めます。
`npm` のグローバルキャッシュ権限で止まったので、作業ディレクトリ内にキャッシュ先を切り替えて進めます。実装の変更対象には影響しません。
ビルド前の自己チェックを入れます。新規フィールドの到達経路と、禁止されているフォールバック構文が紛れていないかを `rg` で確認します。
ビルドとテストは通りました。最後に差分を確認し、今回の実装で余計な公開物や未使用の置き換えが残っていないかだけ締めます。
## 作業結果
- projection schema の実装を追加し、`sub-slice` 入力から `implementation_issue_packet` と `review_context_packet` を deterministic に生成できるようにしました。
- 既存テストが前提にしている公開 API を [src/index.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/index.ts)、変換ロジックを [src/mapping/field-resolvers.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/field-resolvers.ts)、[src/mapping/project-to-implementation-packet.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-implementation-packet.ts)、[src/mapping/project-to-review-context-packet.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-review-context-packet.ts) に実装しました。
- 実装開始時のスコープ宣言は [implement-scope.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/.takt/runs/20260402-073332-issue-1-a1-projection-schema-g/reports/implement-scope.md) に記録しました。計画から逸脱する追加判断はなかったため、決定ログの新規作成はしていません。

## 変更内容
- Zod ベースの入力/出力スキーマを [src/schema/projection-input.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/projection-input.ts)、[src/schema/implementation-issue-packet.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/implementation-issue-packet.ts)、[src/schema/review-context-packet.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/review-context-packet.ts) に追加しました。
- `issue_kind` の契約文字列を [src/schema/issue-kind.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/issue-kind.ts) に集約し、`depends_on_subslices` 優先、`rules/specs/designs` 抽出、`target_part` からの `in_scope` 補完、`parent_intent_root` の空文字補完を実装しました。
- 実行設定として [package.json](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/package.json)、[tsconfig.json](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/tsconfig.json)、[vitest.config.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/vitest.config.ts) を追加し、`package-lock.json` も生成しました。

## ビルド結果
- `npm run build`
- 成功。`tsc --noEmit` で型エラーは出ていません。

## テスト結果
- `npm test`
- 成功。`7` test files、`21` tests がすべて pass しました。