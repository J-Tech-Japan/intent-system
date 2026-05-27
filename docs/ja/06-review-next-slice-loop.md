# レビュー / next-slice ループの設定

← [ドキュメント索引](README.md) | → [ループがおかしいときの復旧](07-recovery.md)

レビュー/next-slice ループも、このページの手順を直接コピーして作るものではありません。
正確な条件は installed intent-cli guidance が source of truth です。
設計スレッドで AI agent に依頼し、現在のループ作成プロンプトを生成してもらいます。

## フォルダー分離（最初に理解する）

レビューループは**レビュー専用フォルダー**で動かします。
実装ループや設計スレッドと同じフォルダーを共有しないでください。

| フォルダー | 役割 |
|---|---|
| **設計/host フォルダー** | intent metadata・packet を保管。設計スレッドがここで動く |
| **実装フォルダー** | child implementation ループがコードを編集し PR を作成/更新する |
| **レビューフォルダー** | host review/next-slice ループが PR をレビューし次の issue を公開する |

> **注意:** レビューフォルダーは通常運用では必須です。
> レビュー/next-slice 自動化が host metadata を取得・変更するとき、
> 設計スレッドや実装ワーカーのチェックアウトに干渉しないようにするためです。

**同一リポジトリ metadata トポロジー**（`main-metadata` ブランチ使用）の場合も、
設計・実装・レビューの各ループは同じリポジトリの**別フォルダー/クローン/ワークツリー**
で動かすことを強く推奨します。

## ループ作成の手順

1. **設計スレッドで** AI agent に intent-cli への問い合わせを依頼し、レビューループ作成プロンプトを生成する
2. domain、target repo、**レビューフォルダーのパス**、実装 PR の base branch を伝える
3. 生成されたプロンプトを **レビューフォルダー** で開いた別スレッドに貼り付ける

## 設計スレッドプロンプト（ループ作成依頼用）

設計スレッド（設計/host フォルダーで動いている AI agent）に貼り付けてください:

> intent-cli に聞いて、`<owner>/<repo>` の host review / next-slice loop を
> Codex の 5m automation で作成するための依頼文を作ってください。
> domain は `<domain>`、作業場所は `<review-folder>`、実装 PR の base は `<branch>` です。
> レビュー対象、next issue、workflow label、durable metadata は
> intent-cli guidance を source of truth にしてください。

生成されたプロンプトを**レビューフォルダーを開いた別スレッド**に貼り付けます。
ループの詳細条件は intent-cli guidance から取得されるため、
このドキュメントに長いループ本体をコピーする必要はありません。

## host review / next-slice ループの原則

- これは **host/review** 作業: PR を packet/intent 契約に照らしてレビューし、更新を要求し、approve/merge し、next slice を切り出す
- host metadata を扱ってよいが、常に `intent-cli` がサポートする遷移を使う
- approve はテスト通過だけでなく packet/intent の証跡に紐づける
- label 遷移はすべて `intent-cli automation` 経由 — 手作業では行わない

## metadata / label の安全境界

- レビュー label 遷移（`intent-pr-reviewing`、`intent-pr-request-update`、`intent-pr-approved` …）は `intent-cli automation` が付与する。手作業では行わない
- テスト通過は **必要だが十分ではない** — approve には packet/intent への適合証跡が必要（`guide review` 参照）
- 現在 PR の受け入れ基準ブロッカーは、request-update/clarification として完了する前に永続的な PR コメントを残す（[復旧](07-recovery.md) 参照）

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。ループの詳細条件は
> `intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>`
> が source of truth です。通常、ユーザーが直接実行する必要はありません。

```bash
# レビュー / next-slice の正本 prompt を取得
intent-cli guide oneshot --kind host-review-next-slice --repo <owner>/<repo>

# PR 固有のレビューガイド（チェックリスト、packet 参照、approve/request-update 要件）
intent-cli guide review --pr <n> --repo <owner>/<repo> --format json

# label 遷移（review-start、request-update、approve …）— 手作業では行わない
intent-cli automation pr-transition --transition <name> --write --format json
```

## 次へ

[ループがおかしいときの復旧](07-recovery.md) | [実装ループの設定](05-implementation-loop.md) | [ドキュメント索引](README.md)
