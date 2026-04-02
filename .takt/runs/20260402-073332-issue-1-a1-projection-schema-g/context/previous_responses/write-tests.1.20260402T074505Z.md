要求に沿って、このムーブメントでは `.takt/.../context/task` と指定された Report Directory を基点に仕様と履歴を確認し、既存のテスト構成を把握したうえでテストだけを追加します。まず `order.md`、ポリシー/ナレッジの元ファイル、既存のテスト配置を読みます。
仕様とポリシーは確認できました。次に計画レポートとリポジトリの現在地を読み、既存コードの有無とテスト配置の前提を固めます。
ここまでで前提は固まりました。リポジトリは実質空で既存テストパターンがないため、計画レポートのインターフェースを唯一の実装前提にして、単体テストと統合テストの両方を先に置きます。

1. Report Directory に `test-scope.md` を先に作成します。
2. その後、`schema` と `mapping` の責務ごとにテストを分け、`src/index.ts` 経由の統合テストも追加します。
3. 実装を先回りしすぎないよう、`order.md` と `plan.md` に明示された契約だけを検証対象にします。
これから Report Directory にスコープ宣言を書き、その後にテストファイルを追加します。プロダクションコードには触れません。
テスト本体を追加します。計画レポートにある公開 API 名と変換規則だけを前提にし、resolver 単体と projector/public API の統合の両方を固定します。
テストは一通り置けました。ここからは型面で詰まりそうな箇所を見直し、必要なら軽く整えてから decision レポートを書きます。
型で先に弾かれてしまうと「ランタイム検証が表に出ること」を確認できないので、そのケースだけテスト側で入力型を明示的に落とします。あわせて今回の判断を decisions に残します。
作成物の確認に入ります。テストは実行環境がまだない前提なので、ここでは追加ファイルと差分状態を確認します。
## 作業結果
- `order.md` と Report Directory の `plan.md` を基準に、projection schema の実装前テストを追加しました。
- resolver 単体、projector 単体、`src/index.ts` 経由の統合フローを分けて固定し、`depends_on_subslices` 優先、`rules_and_specs` 抽出、scope 補完、`parent_intent_root` 補完、入力バリデーション伝搬を検証対象にしました。
- スコープ宣言は [test-scope.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/.takt/runs/20260402-073332-issue-1-a1-projection-schema-g/reports/test-scope.md)、決定ログは [test-decisions.md](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/.takt/runs/20260402-073332-issue-1-a1-projection-schema-g/reports/test-decisions.md) に記録しました。
- テスト実行はしていません。現時点ではプロダクションコードと実行設定が未作成です。

## 変更内容
- [src/schema/projection-input.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/projection-input.test.ts)
- [src/schema/implementation-issue-packet.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/implementation-issue-packet.test.ts)
- [src/schema/review-context-packet.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/review-context-packet.test.ts)
- [src/mapping/field-resolvers.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/field-resolvers.test.ts)
- [src/mapping/project-to-implementation-packet.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-implementation-packet.test.ts)
- [src/mapping/project-to-review-context-packet.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-review-context-packet.test.ts)
- [src/index.test.ts](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/index.test.ts)