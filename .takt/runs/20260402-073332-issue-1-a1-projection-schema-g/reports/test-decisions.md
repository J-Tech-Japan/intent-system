# 決定ログ

## 1. Public API 経由の統合テストを追加する
- **背景**: 今回の projection は `schema`、`mapping`、`src/index.ts` の3モジュール以上を横断するデータフローを持つ。
- **検討した選択肢**: resolver 単体テストのみにする / projector 単体までにする / public API 経由の統合テストも追加する
- **理由**: ムーブメント指示の統合テスト条件を満たしており、packet 生成の最終契約を `src/index.ts` から固定する必要があるため。

## 2. Output schema テストは projector から得た packet を基準に検証する
- **背景**: 今回の主仕様は `order.md` と `plan.md` にあり、出力 packet の完全な静的サンプルよりも projection 後の契約整合性が重要だった。
- **検討した選択肢**: 手書きの packet fixture を使う / projector の出力を schema で再検証する
- **理由**: 実装前段階で過剰に未確定フィールドを固定せず、計画で明示された mapping 規則と schema 妥当性を一緒に担保できるため。