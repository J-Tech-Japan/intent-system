# プロジェクト開始

← [ドキュメント索引](README.md) | → [はじめに: 最初の packet までの道のり](02a-getting-started-orchestration.md)

これは **host/design** 作業です。以下のプロンプトを AI agent のデザインスレッドに
貼り付けてください。agent が intent-cli コマンドを実行し、質問や結果を返します。

## デザインスレッドプロンプト

AI agent（Claude、Codex、Copilot など）に貼り付けてください:

> `<owner>/<repo>` の domain `<name>` のプロジェクトを開始または継続したいです。
> intent-cli に現在のフェーズと次に決断すべきことを聞いてください。

## agent が実行するコマンド（メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。通常、ユーザーが直接実行する必要はありません。メンテナンスやトラブルシューティングの際に参照してください。

```bash
# host domain を初期化（--write なしは read-only）
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write

# intent tree と automation bindings を初期化
intent-cli intent init-tree --domain <name> [--target-repo <owner>/<repo>] --write

# host が初期化済みか確認（`partially-initialized` ではなく `ok` を期待）
intent-cli intent host-check --domain <name> --format json

# 現在の baseline / WIP / キュー済み packet を確認（read-only）
intent-cli intent status

# 最初の slice を計画（新規ドメインでは `design-needed` ガイダンスを期待。
# `missing-domain-bindings` のハードブロックにはならない）
intent-cli intent next-slice --domain <name> --dry-run --format json

# 作業サーフェスが期待する内容を尋ねる
intent-cli guide intent-work --format json
```

> **初回セットアップ補足（G441）:** `intent init` + `intent init-tree --write` は
> durable-state スケルトン（`.intent-cli/queue-state.json`・`.intent-cli/runs.jsonl`）と
> ドメインの automation bindings（`intents/<name>/automation/bindings.md`）を生成します。
> これにより初回の `host-check` は `ok` を返し、`next-slice` が
> `missing-domain-bindings` でブロックされません。コマンドが詰まって見えたら、
> **intent-cli に次のアクションを尋ねてください**（`host-check`・`next-slice --dry-run`・
> `automation summary`）。初回セットアップの復旧に intent-cli のソースコードを読む必要も、
> `bindings.md` を手書きする必要もありません。

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

[はじめに: 最初の packet までの道のり](02a-getting-started-orchestration.md)。
