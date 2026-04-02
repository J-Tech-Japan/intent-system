実装差分とテスト契約を照合して、AI特有の観点でブロッキング有無を判定します。まずは変更ファイル全体・テスト・ポリシー全文を読み切ります。

実装内容とテストをファイル単位で突き合わせ、`REJECT` 基準（未使用コード・フォールバック乱用・公開API漏洩など）を機械的に検証します。次に `rg` で使用箇所確認をかけて事実ベースで判定します。

要件取りこぼし（スコープ縮小）がないかを確定するため、`context/knowledge` と `implement` ポリシーを確認して、実装済みスキーマ項目と突合します。

判定: **REJECT**（blocking 3件、すべて `new`）

`finding_id: ai-review-001`  
`src/schema/projection-input.ts:7-23` — 入力スキーマがスコープ縮小です。`target_path / review_mode / completion_action / landing_policy` が未定義で、`source_concepts` も `min(1)` で必須化されています。実行履歴 `reports/plan.md.20260402T073753Z:111-124` と不整合。  
修正: 仕様どおり入力必須/任意を再定義し、対応テストを追加。

`finding_id: ai-review-002`  
`src/schema/implementation-issue-packet.ts:7-20`, `src/schema/review-context-packet.ts:5-11` — 出力スキーマが不足。履歴では output が implementation 17項目 / review 7項目 (`reports/plan.md:17`) なのに実装は 12/5。  
修正: spec準拠で不足フィールドを schema と projector に追加し、必須契約を固定。

`finding_id: ai-review-003`  
`src/mapping/field-resolvers.ts:76-82`, `src/mapping/project-to-review-context-packet.ts:19` — `parent_intent_root` を `''` にフォールバックしており、`order.md:47-48` の「review packet から parent Intent root に戻れる」を満たしません。  
修正: `parent_intent_root` を必須化し、欠落時は validation error にしてください。