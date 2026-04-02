# 決定ログ

## 1. `issue_kind` の契約文字列を単一モジュールに集約する
- **背景**: `issue_kind` は入力 schema、出力 schema、resolver のデフォルト値で共通に使われる契約文字列であり、ムーブメント指示でも新規導入する契約文字列は 1 箇所で定義することが求められていた。
- **検討した選択肢**: schema ごとに enum を重複定義する / resolver にだけデフォルト文字列を書く / `src/schema/issue-kind.ts` に値と schema を集約する
- **理由**: 重複を避けつつ、入力・出力・mapping の全てで同じ契約を参照できるため。

## 2. projector 関数で入力と出力の両方を schema 検証する
- **背景**: 既存テストでは不正な入力時に validation error が表に出ることと、生成された packet が対応 schema を満たすことの両方が契約になっていた。
- **検討した選択肢**: 入力だけ検証する / 出力だけ検証する / projector 内で入力と出力の両方を検証する
- **理由**: fail-fast を守りつつ、deterministic な変換結果が公開 schema と一致していることを実装側で固定できるため。