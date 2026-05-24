# 実装ループの設定

> **まず intent-cli に聞く:** `intent-cli guide start` →
> `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`。
> ← [ドキュメント索引](index.md)

これは **child-implementation** 作業で、**GitHub-contract-only かつ
metadata-free**: issue/PR と repo ローカルのコードのみが source of truth。
host の `.intent-cli/`、queue-state、metadata branch、`intents/**` を読んだり
変更したりしない。

## ループ（1 wake で 1 アクション）

oneshot prompt が正本。概要:

```bash
# 1. 対象を 1 つだけ選ぶ（手動の label-walking はしない）
intent-cli worker next-action --repo <owner>/<repo> --github-only --format json

# 2. issue-to-pr: claim → 最小実装 → ready-for-review の PR を作成
intent-cli worker claim --kind issue --number <n> --repo <owner>/<repo> --github-only --write --format json
#    PR 本文に `Closes #<n>` を必ず含める。origin/main から開始する。
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> --issue <n> --pr <pr> --outcome <outcome> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> --github-only --outcome <outcome> --pr <pr> --write --format json
```

`pr-comment-fix` の対象も、既存 PR ブランチ上で claim → 修正 → result-summary →
complete という同じ形に従う。

## ask-intent-cli プロンプトテンプレート

> このワークツリーから `<owner>/<repo>` の child implementation ループを回す。
> prompt は `intent-cli guide oneshot --kind child-implement-or-update` で取得。
> 作業の選択は `intent-cli worker next-action` のみ。1 wake で最大 1 アクション。
> label 遷移はすべて intent-cli worker 経由で、raw `gh` は使わない。

## metadata / label の安全境界

- ワークフロー label 遷移はすべて `intent-cli worker` 経由 — raw `gh ... --add-label`
  は使わない。
- child agent は PR に `intent-target`（host 所有）や `intent-pr-created`
  （issue 側マーカー）を付けない。
- `worker complete` の `linked_pr_synced: false` は child-cwd で想定される警告 —
  記録して先に進む。

## 次へ

[レビュー / next-slice ループの設定](06-review-next-slice-loop.md)。
