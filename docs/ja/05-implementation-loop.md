# 実装ループの設定

← [ドキュメント索引](README.md) | → [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)

実装ループは、このページの手順を直接コピーして作るものではありません。
正確な条件は installed intent-cli guidance が source of truth です。
設計スレッドで AI agent に依頼し、現在のループ作成プロンプトを生成してもらいます。

## フォルダー分離（最初に理解する）

実装ループを始める前に、3 つのフォルダーの役割を理解してください:

| フォルダー | 役割 |
|---|---|
| **設計/host フォルダー** | intent metadata・packet を保管。設計スレッドがここで動く |
| **実装フォルダー** | child implementation ループがコードを編集し PR を作成/更新する |
| **レビューフォルダー** | host review/next-slice ループが PR をレビューし次の issue を公開する |

> **注意 — cwd の誤りはよくある失敗パターンです。**
> 実装ループを設計/host フォルダーで起動したり、レビューループを実装フォルダーで起動したりすると
> 誤動作します。ループを開始する前に作業ディレクトリを必ず確認してください。

**同一リポジトリ metadata トポロジー**（`main-metadata` ブランチ使用）の場合も、
設計・実装・レビューの各ループは**同じリポジトリの別フォルダー/クローン/ワークツリー**
で動かすことを強く推奨します。3 つのロールが同じフォルダーを共有すると、
互いのブランチ操作や metadata 変更が干渉します。

## ループ作成の手順

1. **設計スレッドで** AI agent に intent-cli への問い合わせを依頼し、実装ループ作成プロンプトを生成する
2. domain、target repo、**実装フォルダーのパス**、PR の base branch を伝える
3. 生成されたプロンプトを **実装フォルダー** で開いた別スレッドに貼り付ける

## 設計スレッドプロンプト（ループ作成依頼用）

設計スレッド（設計/host フォルダーで動いている AI agent）に貼り付けてください:

> intent-cli に聞いて、`<owner>/<repo>` の child implementation loop を
> Claude Code の `/loop 5m` で作成するための依頼文を作ってください。
> domain は `<domain>`、作業場所は `<implementation-folder>`、
> 実装 PR の base は `<branch>` です。
> 詳細条件は intent-cli guidance に従う形にしてください。

生成されたプロンプトを**実装フォルダーを開いた別スレッド**に貼り付けます。
ループの詳細条件は intent-cli guidance から取得されるため、
このドキュメントに長いループ本体をコピーする必要はありません。

## child implementation ループの原則

- **GitHub-contract-only かつ metadata-free**: issue/PR とリポジトリローカルのコードのみが source of truth
- host の `.intent-cli/`、queue-state、metadata branch、`intents/**` を読んだり変更したりしない
- 作業の選択は `intent-cli worker next-action` のみ。1 wake で最大 1 アクション
- label 遷移はすべて `intent-cli worker` 経由 — raw `gh ... --add-label` は使わない

## metadata / label の安全境界

- child agent は PR に `intent-target`（host 所有）や `intent-pr-created`（issue 側マーカー）を付けない
- `worker complete` の `linked_pr_synced: false` は child-cwd で想定される警告 — 記録して先に進む

## Preview: Git-backed cross-clone scope claim (G679)

この decision と boundary は
[ADR 0003](../adr/0003-git-push-cas-work-ownership.md) に記録しています。

`worker claim` は上記の GitHub issue/PR lifecycle transition のままです。別の preview
`claim` group は server なしで host clone 間の named work unit を調整します。

```bash
intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
intent-cli claim acquire --scope release-prep:<owner/repo>:<version> --actor <actor> --team <team> --write --format json
intent-cli claim verify --scope <scope> --team <team> --format json
```

acquire は厳密に `git pull --ff-only` → `.intent-cli/claims/` 配下に immutable record を
作成 → commit → plain push です。push 成功だけが acquire の事実です。push reject 後に
同じ scope が現れた場合は holder を名指す `held`、無関係な advance なら fresh base から
bounded retry で再適用します。release / takeover は actor/team/reason の明示的 attribution が
必要で、takeover は displaced holder を名指します。age で claim を expire / transfer しません。
`automation stalled-work` は actor、team、scope、age、last evidence を持つ `claim-stale` を
detect-only で報告します。

fresh host は `.gitattributes` に次の exact line をこの順序で置きます。

```gitattributes
.intent-cli/runs.jsonl merge=union
.intent-cli/**/*.jsonl merge=union
.intent-cli/claims/** -merge
```

existing host は自動 migrate しません。最後の specific line は broad union rule の後へ、
明示的に review した commit でのみ追加します。

### Preview: claim-aware start surface (G680)

packet draft、queue seed/publish-flow、worker next-action、release-prep は同じ
`claim verify` judgment を使います。store が設定されている場合、invoking team が matching
scope を保持している必要があります。unheld / other-team の拒否は scope、holder、holder team
を名指します。next-slice は同じ judgment を recommendation mode で使い、unheld と own-team
unit は candidate のまま、claimed-elsewhere unit は holder evidence 付きで除外します。
これにより start が拒否する作業を recommendation が促しません。

番号は claim-then-draft です。scaffold 前に `execution-unit:<N>` を claim し、N に負けたら
fast-forward、次番号を再計算し、その番号を exactly once retry します。GitHub lifecycle label
は visible な defence in depth のままで acquisition fact ではありません。review/closeout gate と
`worker complete` は変更しません。

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。ループの詳細条件は
> `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`
> が source of truth です。通常、ユーザーが直接実行する必要はありません。

```bash
# 対象を 1 つだけ選ぶ（手動の label-walking はしない）
intent-cli worker next-action --repo <owner>/<repo> --team <team> --github-only --format json

# issue-to-pr: claim → 最小実装 → ready-for-review の PR を作成
intent-cli worker claim --kind issue --number <n> --repo <owner>/<repo> --github-only --write --format json
#    PR 本文に `Closes #<n>` を必ず含める。origin/main から開始する。
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> --issue <n> --pr <pr> --outcome <outcome> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> --github-only --outcome <outcome> --pr <pr> --write --format json
```

## 次へ

[レビュー / next-slice ループの設定](06-review-next-slice-loop.md) | [ドキュメント索引](README.md)
