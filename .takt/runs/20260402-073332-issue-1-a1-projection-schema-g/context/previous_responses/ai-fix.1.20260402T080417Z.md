依頼の一次情報として、`task` と `Report Directory`、加えてこのムーブメントの `policy` / `knowledge` の元ファイルを先に確認します。指摘対象の実在確認をしたうえで、必要な修正とテストまで進めます。
レビューで何を指摘されたかを固定するため、次に `Report Directory` と実際の対象コードを突き合わせます。ここで指摘ファイルと検索結果を確定させます。
指摘3件は `reports/ai-review.md` に固定されていました。これから対象ファイルを直接開いて、レビュー文言どおりの欠落とフォールバックが現状コードに残っているかを確認します。
ここで仕様の穴が1つあります。レポートには不足フィールドの総数はありますが、出力 packet 側の具体名は書かれていません。まず既存の実装履歴に、どの field を packet に残す想定だったかがないかを追加で確認します。
修正内容は固まりました。入力契約を reviewer 指摘に合わせて広げ、`parent_intent_root` の必須化に合わせて resolver / projector / テストを同時に更新します。加えて不足扱いだった packet field も schema と projector に明示します。
編集は入りました。次に reviewer 指摘どおり、実在確認用の `rg` を回してフォールバックが消えたことと、新しい field が schema / projector に到達していることを確認してから、`npm test` と `npm run build` を実行します。
型エラーはテスト側だけでした。`parent_intent_root` を必須にしたので、欠落ケースは実行時バリデーション確認のために明示的に型を落とす必要があります。そこだけ修正して再実行します。
`parent_intent_root` の必須化に伴う型落としをテストへ反映しました。もう一度 `build` と `test` を通して、修正が schema / runtime の両方で整合しているかを確認します。
確認と検証は通りました。最後に報告フォーマットに合わせて、実際に確認した行番号と修正済みの箇所を採番できる形で取り直します。
## 確認したファイル
- [reports/ai-review.md:16](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/.takt/runs/20260402-073332-issue-1-a1-projection-schema-g/reports/ai-review.md#L16)
- [reports/plan.md:17](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/.takt/runs/20260402-073332-issue-1-a1-projection-schema-g/reports/plan.md#L17)
- [src/schema/projection-input.ts:7](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/projection-input.ts#L7)
- [src/schema/implementation-issue-packet.ts:7](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/implementation-issue-packet.ts#L7)
- [src/schema/review-context-packet.ts:5](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/review-context-packet.ts#L5)
- [src/mapping/field-resolvers.ts:13](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/field-resolvers.ts#L13)
- [src/mapping/project-to-implementation-packet.ts:19](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-implementation-packet.ts#L19)
- [src/mapping/project-to-review-context-packet.ts:13](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-review-context-packet.ts#L13)
- [src/schema/projection-input.test.ts:5](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/projection-input.test.ts#L5)
- [src/schema/implementation-issue-packet.test.ts:28](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/implementation-issue-packet.test.ts#L28)
- [src/schema/review-context-packet.test.ts:27](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/review-context-packet.test.ts#L27)
- [src/mapping/field-resolvers.test.ts:40](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/field-resolvers.test.ts#L40)
- [src/mapping/project-to-implementation-packet.test.ts:30](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-implementation-packet.test.ts#L30)
- [src/mapping/project-to-review-context-packet.test.ts:28](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-review-context-packet.test.ts#L28)
- [src/index.test.ts:34](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/index.test.ts#L34)

## 実行した検索
- `rg -n "REJECT|target_path|review_mode|completion_action|landing_policy|parent_intent_root" .takt/runs/20260402-073332-issue-1-a1-projection-schema-g/reports src`
  - review 指摘の対象ファイルと不足 field 名を実在確認。
- `rg -n "parent_intent_root|target_path|review_mode|completion_action|landing_policy" src`
  - schema / projector / test まで新フィールドが到達していることを確認。
- `rg -n "return ''|\\?\\? ''|parent_intent_root: ''" src`
  - `parent_intent_root` の空文字フォールバックが消えていることを確認。結果は 0 件。

## 修正内容
- [src/schema/projection-input.ts:7](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/projection-input.ts#L7) を確認した結果、`target_path / review_mode / completion_action / landing_policy` が存在せず、`source_concepts` が必須だったため、4 field を追加し、`source_concepts` を optional に変更し、`parent_intent_root` を必須化しました。
- [src/schema/implementation-issue-packet.ts:7](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/implementation-issue-packet.ts#L7) を確認した結果、implementation packet が不足していたため、`target_path / review_mode / completion_action / landing_policy / parent_intent_root` を追加して 17 field 契約に修正しました。
- [src/schema/review-context-packet.ts:5](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/review-context-packet.ts#L5) を確認した結果、review packet が不足していたため、`target_path / review_mode` を追加して 7 field 契約に修正しました。
- [src/mapping/field-resolvers.ts:13](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/field-resolvers.ts#L13) を確認した結果、`source_concepts` optional 化に追従できておらず、`parent_intent_root` もフォールバックしていたため、`source_concepts` 未指定時は `[]` を扱うようにし、[src/mapping/field-resolvers.ts:84](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/field-resolvers.ts#L84) で必須値をそのまま返す実装に修正しました。
- [src/mapping/project-to-implementation-packet.ts:22](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-implementation-packet.ts#L22) と [src/mapping/project-to-review-context-packet.ts:16](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-review-context-packet.ts#L16) を確認した結果、新規必須 field が packet に写されていなかったため、schema 追加分を projector に配線しました。
- テストは [src/schema/projection-input.test.ts:57](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/projection-input.test.ts#L57)、[src/schema/implementation-issue-packet.test.ts:46](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/implementation-issue-packet.test.ts#L46)、[src/schema/review-context-packet.test.ts:42](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/schema/review-context-packet.test.ts#L42)、[src/mapping/project-to-review-context-packet.test.ts:52](/Users/tomohisa/dev/GitHub/takt-worktrees/20260402T0733-1-issue-1-a1-projection-schema-g/src/mapping/project-to-review-context-packet.test.ts#L52) などを更新し、field 数と `parent_intent_root` 欠落時の validation error を固定しました。

## テスト結果
- `npm test`
  - 成功。`7` files / `26` tests passed。
- `npm run build`
  - 成功。`tsc --noEmit` 通過。