# プロジェクト開始

> ← [ドキュメント索引](index.md)

これは **host/design** 作業です。以下のプロンプトを AI agent のデザインスレッドに
貼り付けてください。agent が intent-cli コマンドを実行し、質問や結果を返します。

## デザインスレッドプロンプト

AI agent（Claude、Codex、Copilot など）に貼り付けてください:

> `<owner>/<repo>` の domain `<name>` のプロジェクトを開始または継続したいです。
> intent-cli に現在のフェーズと次に決断すべきことを聞いてください。

## agent が実行するコマンド（リファレンス）

```bash
# host domain を初期化（--write なしは read-only）
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write

# 現在の baseline / WIP / キュー済み packet を確認（read-only）
intent-cli intent status

# 作業サーフェスが期待する内容を尋ねる
intent-cli guide intent-work --format json
```

## metadata / label の安全境界

- `intent-target` や `intent-pr-*` などのワークフロー label は
  `intent-cli automation` / `intent-cli worker` が付与する。手作業では行わない。
- 正本の状態は host repo の `.intent-cli/` にある。intent-cli のサーフェス経由で読み、
  `queue-state.json` を直接編集しない。

## リポジトリトポロジーの選択

初期化の前に、host metadata をどこに置くかを選択します。2 つのトポロジーが完全にサポートされています。

### トポロジー A — 別の host リポジトリ

host のオーケストレーションリポジトリを専用リポジトリ（例: `myorg/my-project-host`）として用意します。
child 実装作業は 1 つ以上の別のターゲットリポジトリ（例: `myorg/my-project`）で行います。

```
myorg/my-project-host      ← host リポジトリ
  .intent-cli/             ← queue-state、config、intent tree
  intents/
  AGENTS.md

myorg/my-project           ← child 実装リポジトリ
  <ソースコード>
  (.intent-cli/ なし)
```

- `intent-cli intent init` は **host リポジトリ** のチェックアウトから実行します。
- host agent は host チェックアウトから `intent-cli automation` と `intent-cli review` コマンドを呼び出します。
- child 実装 agent は実装リポジトリをクローンまたは使用します。host リポジトリへはアクセスしません。

### トポロジー B — metadata ブランチを使う同一リポジトリ

host と child 実装が **同じリポジトリ** に共存します。host metadata は専用ブランチ（一般的に `main-metadata`）に置き、
実装 PR が実装ベースブランチ（`main`）を対象にしながら metadata を含まないようにします。

```
myorg/my-project           ← 単一リポジトリ
  branch: main             ← 実装コード、child の PR はここを対象
  branch: main-metadata    ← .intent-cli/、intents/、AGENTS.md（host 専用）
```

- `intent-cli intent init` は **metadata ブランチのチェックアウト** から実行します。
- 実装 PR は `main` を対象にします。metadata は `main-metadata` に留まります。
- child 実装 agent は両方のトポロジーで **GitHub-contract-only** です。
  metadata ブランチを読んだり変更したりしません。

### どちらのトポロジーを選ぶか

| 考慮点 | 別の host リポジトリ | 同一リポジトリ + metadata ブランチ |
|---|---|---|
| チームが intent オーケストレーションと実装を明確に分離したい | ✓ 自然な境界 | より多くの規律が必要 |
| 管理するリポジトリ数を減らしたい | リポジトリが増える | ✓ 単一リポジトリ |
| コントリビューターに実装のみ見せたい OSS プロジェクト | ✓ host リポジトリを非公開にできる | metadata ブランチは全員に見える |
| すでに単一リポジトリがあり intent-cli を追加したい | 移行コストが発生 | ✓ 移行コストが低い |

どちらのトポロジーも有効です。チームの既存の慣習に合う方を選んでください。

## 次へ

[intent の整理・保守](03-intents.md)。
