# Intent ナレッジツリーレイアウト (tree-v1)

> **まず intent-cli に聞く。** ← [intent の整理](03-intents.md)

このページでは、新規ドメイン向けの **tree-v1** フレキシブル intent ナレッジツリーレイアウトについて説明します。
既存のフラットファイルドメインはすぐに移行する必要はありません。tree-v1 は新規ドメインへの推奨デフォルトであり、既存ドメインへの強制要件ではありません。

## intent deepening の会話とツリーの関係

[intent deepening の会話](03-intents.md#intent-deepening-とは)で得られた回答は、このツリーの各フォルダに整理されます。

| 会話で決まった内容 | ツリーの格納先 |
|---|---|
| プロダクトの目標・ユーザー・非目標 | `product/` |
| ミッション/バリュー/ビジョン・原則 | `identity/` |
| 機能要件・ユーザーストーリー | `features/<slug>/` |
| 技術選択・アーキテクチャ・ライブラリ | `technology/` |
| ADR スタイルの決定事項 | `decisions/` |
| 未解決の問い | `clarifications/open.md` |
| 実装ループ・リリース方針 | `operations/` |
| 実行可能スライス | `packets/` → GitHub issue |

1回の会話ですべてのフォルダが埋まる必要はありません。intent deepening は何度でも繰り返せます。

## なぜ tree-v1 か

フラット intent ファイルは小規模・初期段階のプロジェクトで機能します。ドメインが成長すると、単一ファイルは検索・レビュー・リンク・分析が困難になります。
tree-v1 は intent を発見しやすいフォルダに整理し、ミッション/ビジョン/バリュー、機能要件、技術的選択、ループ運用、決定事項、明確化事項をパスで参照・相互リンクできるようにします。

## マニフェスト

tree-v1 に従う各ドメインは `intents/<domain>/manifest.yaml` にマニフェストを提供します:

```yaml
version: "1"
layout_version: tree-v1
project_type: product-app   # product-app | library-tool | infrastructure | research-prototype
target_repo: <owner/repo>
branch_policy: direct-main
metadata_policy: host-metadata
entrypoints:
  - identity/mission.md
  - README.md
categories:
  identity: identity/
  product: product/
  features: features/
  technology: technology/
  operations: operations/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
  links: links/
```

`categories:` 配下のカテゴリパスは設定可能です。プロジェクトタイプに合わせて任意のカテゴリを名前変更・省略できます（[プロジェクトタイプの例](#プロジェクトタイプの例)参照）。

## 推奨カテゴリ

| カテゴリ | パス | 用途 |
|---|---|---|
| `identity` | `identity/` | ミッション、ビジョン、バリュー、原則、用語集 |
| `product` | `product/` | 概要、ユーザー、ジャーニー、非目標 |
| `features` | `features/` | 機能ごとのサブフォルダ — 概要、要件、受け入れ基準、決定事項、未解決の質問、パケット、リンク |
| `technology` | `technology/` | アーキテクチャ、言語、ライブラリ、フロントエンド、バックエンド、データ、クラウド、セキュリティ、テスト、オブザーバビリティ、デプロイ |
| `operations` | `operations/` | 実装ループ、レビューループ、リリース、リカバリ |
| `decisions` | `decisions/` | ADR スタイルの記録（`<NNNN>-<slug>.md`） |
| `clarifications` | `clarifications/` | `open.md`、`answered.md`、`log.md` |
| `packets` | `packets/` | ロードマップ、バックログ、ウェーブ |
| `links` | `links/` | GitHub リポジトリ、外部ドキュメント、関連プロジェクト |

すべてのカテゴリ名はオプションかつ設定可能です。

## 機能フォルダレイアウト

各機能は `features/<feature-slug>/` に配置します:

```
features/
  auth/
    overview.md          # ゴール、動機、受け入れ基準サマリー
    requirements.md      # 詳細要件
    acceptance.md        # 受け入れ基準
    decisions.md         # 機能固有の設計決定
    open-questions.md    # 未解決の質問（clarifications/ へのリンク）
    packets.md           # 実行ユニット一覧（packets/ または GitHub issues へのリンク）
    links.md             # 参照リンク
```

## 相互リンクのルール

相互リンクはツリーをナビゲートしやすくし、コンテンツの重複を防ぎます:

- **機能概要ページ** は、関連する決定事項、明確化事項、パケット、GitHub issues にリンクする必要があります。
- **決定事項レコード** は、それを動機づけた機能と明確化事項にリンクする必要があります。
- **明確化エントリ** は、それがブロックしている機能または決定事項にリンクバックする必要があります。
- **パケットページ** は、公開後に GitHub issue にリンクする必要があります。
- ファイル間でコンテンツを重複させず、相対 Markdown リンクを使用します。

## プロジェクトタイプの例

### `product-app`

推奨カテゴリをすべて使用します。マニフェストのデフォルトから変更不要です。

### `library-tool`

`product/` を `api/` と `users/` に置き換え、不要な場合は `links/` を省略します:

```yaml
categories:
  identity: identity/
  api: api/
  users: users/
  features: features/
  technology: technology/
  operations: operations/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
```

### `infrastructure`

`product/` を `environments/` に置き換え、operations 配下に `runbooks/` を追加します:

```yaml
categories:
  identity: identity/
  environments: environments/
  features: features/
  technology: technology/
  operations: operations/
  runbooks: runbooks/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
```

### `research-prototype`

`product/` を `hypothesis/` に、`operations/` を `experiments/` に置き換えます:

```yaml
categories:
  identity: identity/
  hypothesis: hypothesis/
  features: features/
  technology: technology/
  experiments: experiments/
  decisions: decisions/
  clarifications: clarifications/
  packets: packets/
```

## エージェントへのガイダンス

intent コンテンツを作成・更新するエージェントは以下を行う必要があります:

1. `intent-cli guide intent-work setup --kind tree-layout --domain <name> --target-repo <owner/repo>` を実行して最新ガイダンスを確認する。
2. フラットファイルへの追記より**ツリー配置を優先**する — 新しいコンテンツは適切なカテゴリフォルダに配置する。
3. 機能ページ、決定事項、明確化事項を作成・更新する際に**相互リンクを追加**する。
4. マニフェストの書き込みや既存フラットファイルの移行前に**オペレーターに変更を提示**する。
5. tree-layout 作業と同じウェイクで GitHub issue を公開しない。

## 関連ドキュメント

- [intent の整理・保守](03-intents.md)
- [packet 作成と issue 公開](04-packets-issues.md)
