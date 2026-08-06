<a id="pattern-separate-host-existing-project"></a>
# パターン: 別ホスト × 既存プロジェクト

← [導入パターンを選ぶ](02a-getting-started-orchestration.md) | [ドキュメント索引](README.md)

## この setup

既存の `<owner>/<implementation-repo>` は変更しません。ホストメタデータ用に空の `<owner>/<intents-host-repo>` を別に作り、最初のセッションでは **そのホストリポジトリだけを** checkout します。既存の実装リポジトリはプロンプトで指定し、ここでホストの checkout と混ぜません。

## 最初のプロンプト — ちょうど 1 つを選ぶ

### Herdr-only

> 既存の対象実装リポジトリ `<owner>/<implementation-repo>` に intent-cli を追加します。空の分離した intent 用ホストリポジトリだけを開いています。まずインストール済みのガイドで intent-cli を理解し、ホストを初期化して同居する単一マシンのチーム用に `herdr-only` を記録してください。

### Agmsg + herdr

> 既存の対象実装リポジトリ `<owner>/<implementation-repo>` に intent-cli を追加します。空の分離した intent 用ホストリポジトリだけを開いています。まずインストール済みのガイドで intent-cli を理解し、ホストを初期化して分散チームまたは既存の agmsg チーム用に `agmsg` を記録してください。

## agent が行うこと

インストール済み skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` を dry-run し、`init --write` を適用してホストが ok であることを確認します。その後セッションレイヤーを `intent-cli session-layer set` で記録し、現在のガイドで 4 スレッドチームを準備します。新規 v0.11.0 のホスト write は 9 個のファイルを作ります。プロンプトの違いは最初のトランスポートだけで、下流の手順を混ぜません。

<!-- G608 observed write count: 9 files. -->

## 残る human decision

子 PR 用の base-branch policy、トランスポートの選択（同居では依存関係が少ないため herdr-only を優先し、分散 / 既存 agmsg のチームではサポート対象で廃止されない agmsg + herdr を選ぶ）、各ロールの agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
