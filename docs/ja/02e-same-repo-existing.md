<a id="pattern-same-repository-metadata-branch-existing-project"></a>
# パターン: 同一リポジトリのメタデータ用ブランチ × 既存プロジェクト

← [導入パターンを選ぶ](02a-getting-started-orchestration.md) | [ドキュメント索引](README.md)

## この setup

既存の `<owner>/<implementation-repo>` を維持し、初期化前に想定する実装ベースブランチからメタデータ用ブランチ（例: `main-metadata`）を作成します。最初のホストセッションでは **メタデータ用ブランチの checkout だけを**開きます。実装ブランチと既存コードはホストメタデータの作業から分けます。

## 最初のプロンプト — ちょうど 1 つを選ぶ

### Herdr-only

> 既存リポジトリ `<owner>/<implementation-repo>` に intent-cli を追加します。メタデータ用ブランチの checkout だけを開いています。まずインストール済みのガイドで intent-cli を理解し、このホストを初期化して同居する単一マシンの 4 スレッドチーム用に `herdr-only` を記録してください。

### Agmsg + herdr

> 既存リポジトリ `<owner>/<implementation-repo>` に intent-cli を追加します。メタデータ用ブランチの checkout だけを開いています。まずインストール済みのガイドで intent-cli を理解し、このホストを初期化して分散チームまたは既存の agmsg 投資があるチーム用に `agmsg` を記録してください。

## agent が行うこと

インストール済み skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` dry-run を実行し、`init --write` を適用してホストを確認します。その後、選んだセッションレイヤーを `intent-cli session-layer set` で記録し、現在のガイドで 4 スレッドチームを準備します。新規 v0.11.0 の write は 9 個のファイルを作ります。2 つのプロンプトは最初のトランスポートだけを選び、以降は記録済みの mode を使います。

<!-- G608 observed write count: 9 files. -->

## 残る human decision

子 PR 用の base-branch policy、トランスポートの選択（同居では herdr-only を最初に、分散 / 既存 agmsg のチームでは agmsg + herdr）、各ロールの agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
