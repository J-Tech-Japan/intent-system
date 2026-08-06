<a id="pattern-same-repository-metadata-branch-brand-new-project"></a>
# パターン: 同一リポジトリのメタデータ用ブランチ × 新規プロジェクト

← [導入パターンを選ぶ](02a-getting-started-orchestration.md) | [ドキュメント索引](README.md)

## この setup

新しい `<owner>/<implementation-repo>` に想定する実装ベースブランチを作り、初期化前にそこからメタデータ用ブランチ（例: `main-metadata`）を作成します。このセッションでは **そのメタデータ用ブランチの checkout だけを**開きます。プロダクトコードと子 PR は実装ベースブランチ、ホストメタデータはメタデータ用ブランチに置きます。

## 最初のプロンプト — ちょうど 1 つを選ぶ

### Herdr-only

> 新しいリポジトリ `<owner>/<implementation-repo>` 用に intent-cli を設定します。メタデータ用ブランチの checkout だけを開いています。まずインストール済みのガイドで intent-cli を理解し、このホストを初期化して同居する単一マシンの 4 スレッドチーム用に `herdr-only` を記録してください。

### Agmsg + herdr

> 新しいリポジトリ `<owner>/<implementation-repo>` 用に intent-cli を設定します。メタデータ用ブランチの checkout だけを開いています。まずインストール済みのガイドで intent-cli を理解し、このホストを初期化して分散チームまたは既存の agmsg 投資があるチーム用に `agmsg` を記録してください。

## agent が行うこと

インストール済み skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` を dry-run し、`init --write` を適用してホストが ok か確認します。それからセッションレイヤーを `intent-cli session-layer set` で記録し、現在のガイドで 4 スレッドチームを準備します。新規 v0.11.0 の write は 9 個のファイルを作ります。違うのは最初のプロンプトだけで、以降は記録済みの mode に従います。

<!-- G608 observed write count: 9 files. -->

## 残る human decision

base-branch policy、トランスポートの選択（同居では依存関係が少ないため herdr-only を優先し、分散 / 既存 agmsg のチームではサポート対象で廃止されない agmsg + herdr を選ぶ）、各ロールの agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
