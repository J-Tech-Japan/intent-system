# Intent ナレッジツリーレイアウト (tree-v1)

← [ドキュメント索引](README.md) | ← [intent の整理・保守](03-intents.md)

このページでは、新規ドメイン向けの **tree-v1** フレキシブル intent ナレッジツリーレイアウトについて説明します。
既存のフラットファイルドメインはすぐに移行する必要はありません。tree-v1 は新規ドメインへの推奨デフォルトであり、既存ドメインへの強制要件ではありません。

## Intent Storming の会話とツリーの関係

[Intent Storming の会話](03-intents.md#intent-storming-とは)で得られた回答は、このツリーの各フォルダに整理されます。

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

1回の会話ですべてのフォルダが埋まる必要はありません。Intent Storming は何度でも繰り返せます。

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

## facets(セマンティックファセット)

どの node ファイルも、任意の `facets:` frontmatter フィールドで、その node が
4 つの「人間が理解を保持し続けるべき」サーフェスのうちどれを文書化しているかを
示すことができます — これは、コーディングを AI に委譲する際に人間が理解し続け
なければならない最小限のサーフェスです(オペレーター入力、
[issue #1159](https://github.com/J-Tech-Japan/intent-system/issues/1159))。
projection や boilerplate は安全に委譲できますが、この 4 つは委譲できません:

- **`vocabulary`** — event/command vocabulary: 何が fact として扱われるか。
- **`invariant`** — invariant と consistency boundary。
- **`decider`** — decider judgment: コマンドが何を決定するか。
- **`acceptance-property`** — acceptance property: 何が壊れてはならないか。

この値セットは現時点では closed set です。拡張は将来の design 作業であり、
node の frontmatter がローカルに新しい値を作り出すことはできません。
`facets:` は完全に **任意** です — 付与されていない node は unannotated
であり、これは正当な状態であって error ではありません。`intent lint-layout`
は認識できない値のみを拒否します(node・不正な値・許可されている値セットを
明示)。`intent search --facet <value>` はその facet を持つ node に絞り込み、
`intent analyze-tree` は facet ごとのカウントを報告します。scaffold される
node ファイル(`init-tree`、`add-feature`、`draft-from-interview`)には、
4 つの値すべてを説明するコメント付きの例が含まれます — 該当行のコメントを
外して編集することで node に注釈を付けられます:

```markdown
---
# Optional semantic facets (G529) — closed set, one line each:
#   vocabulary            — event/command vocabulary: what counts as a fact
#   invariant              — invariants and consistency boundaries
#   decider                — decider judgments: what a command decides
#   acceptance-property    — what must not break
# Uncomment and edit to annotate this node, e.g.:
# facets: [vocabulary]
---

# Node title
...
```

facet ごとの例 node を 1 つずつ示します:

- **vocabulary** — ドメインの event/command vocabulary を定義する glossary
  node:

  ```markdown
  ---
  facets: [vocabulary]
  ---
  # Glossary
  **PaymentAuthorized** — 決済プロバイダが資金確保を確認した時点で記録される
  fact。その場で取り消されることはない(`PaymentRefunded` を参照)。
  ```

- **invariant** — 機能の consistency boundary を示す node:

  ```markdown
  ---
  facets: [invariant]
  ---
  # Order — consistency boundary
  注文の合計は常にその明細行の合計と一致しなければならない。この invariant は
  `Order` aggregate の境界内でのみ強制され、aggregate を横断しては強制しない。
  ```

- **decider** — コマンドが何を決定するかを文書化する node:

  ```markdown
  ---
  facets: [decider]
  ---
  # ApproveRefund — decision
  返金リクエストと注文の支払い履歴が与えられたとき、返金を承認・部分承認・
  却下のいずれにするかを決定する。この決定そのもの(通知/projection の副作用
  ではなく)が、人間によるレビューを維持すべき部分である。
  ```

- **acceptance-property** — 何が壊れてはならないかを述べる acceptance
  criteria node:

  ```markdown
  ---
  facets: [acceptance-property]
  ---
  # Checkout — acceptance properties
  - 完了した checkout はカートを空でない状態のまま残してはならない。
  - 同じ注文に対して決済が二重に capture されてはならない。
  ```

1 つの node が複数の facet を持つこともできます(例: `facets: [vocabulary,
invariant]`)。本当に両方を文書化している場合に限ります。

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
