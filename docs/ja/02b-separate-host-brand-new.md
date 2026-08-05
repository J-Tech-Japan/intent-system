<a id="pattern-separate-host-brand-new-project"></a>
# パターン: 別ホスト × 新規プロジェクト

← [導入パターンを選ぶ](02a-getting-started-orchestration.md) | [ドキュメント索引](README.md)

## この setup

プロダクトコード用の空の `<owner>/<implementation-repo>` とホストメタデータ用の空の `<owner>/<intents-host-repo>` を作成します。**空のホストリポジトリだけを** checkout します。実装リポジトリはプロンプトで指定し、この最初のホストセッションでは checkout しません。

<!-- G608 field-tested wording: only the empty host repository; host repository だけ。 -->

## 最初のプロンプト — ちょうど 1 つを選ぶ

### Herdr-only

> 新しい対象実装リポジトリ `<owner>/<implementation-repo>` 用に intent-cli を設定します。空の intent 用ホストリポジトリだけを開いています。まずインストール済みのガイドで intent-cli を理解し、初期化して同居する単一マシンの 4 スレッドチーム用に `herdr-only` を記録してください。

### Agmsg + herdr

> 新しい対象実装リポジトリ `<owner>/<implementation-repo>` 用に intent-cli を設定します。空の intent 用ホストリポジトリだけを開いています。まずインストール済みのガイドで intent-cli を理解し、初期化して分散チームまたは既存の agmsg 投資があるチーム用に `agmsg` を記録してください。

## agent が行うこと

インストール済み skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` を dry-run と `--write` で実行し、`host-check: ok` を確認します。観測した v0.11.0 の write は 9 個のファイルを生成します。選んだセッションレイヤーを `intent-cli session-layer set` で記録してから、現在のガイドで 4 スレッドチームを準備します。2 つのプロンプトの違いは最初の選択だけです。以降は記録済みの mode とインストール済みガイドに従います。

<!-- G608 observed write count: 9 files. -->

## 残る human decision

base-branch policy、トランスポートの選択（同居では herdr-only を最初に、分散 / 既存 agmsg のチームでは agmsg + herdr）、design・orchestration・implementation・review 各ロールの agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
