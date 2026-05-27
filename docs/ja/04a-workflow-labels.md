# GitHub ワークフローラベルで見る現在地

← [ドキュメント索引](index.md) | → [実装ループの設定](05-implementation-loop.md)

packet が GitHub Issue になると、Intent System の状態は GitHub ラベルとして見えるようになります。
ラベルは人間が現在地を把握するための可視状態です。**通常は手で付け外ししません。**
workflow label の変更は intent-cli と automation が行います。

## コアラベル一覧

| ラベル | 意味 |
|---|---|
| `intent-target` | この item は現在のワークフロー対象として選択されている |
| `intent-issue-in-progress` | 実装 worker が issue を claim して作業中 |
| `intent-pr-created` | issue が PR を生み出した（issue 側に付く） |
| `intent-pr-reviewing` | host review loop が PR をレビュー中 |
| `intent-pr-request-update` | レビュアーが変更を要求した |
| `intent-pr-update-in-progress` | 実装 worker が PR の修正を claim して作業中 |
| `intent-pr-rereview-ready` | 修正が push 済みで、review loop の再レビュー待ち |
| `intent-pr-approved` | host review が PR を承認した。マージまたは closeout を待つ |

## ラベルの組み合わせで読む現在地

```text
intent-target + intent-issue-in-progress
→ この issue は現在の実装対象で、実装 worker が作業中です。

intent-target + intent-pr-created（issue 側）
→ PR が作成済みで、review loop が拾うのを待っています。

intent-target + intent-pr-reviewing（PR 側）
→ host review loop が PR をレビュー中です。

intent-target + intent-pr-request-update（PR 側）
→ PR に修正依頼があり、実装 worker の再対応待ちです。

intent-target + intent-pr-rereview-ready（PR 側）
→ 修正が push 済みで、review loop の再レビュー待ちです。

intent-target + intent-pr-approved（PR 側）
→ PR が承認済みで、マージを待っています。
```

## ラベルについての注意

- **手でラベルを付け外ししない**: 通常の運用では、workflow label は intent-cli/automation が管理します。手動で変更すると、ループが誤った状態に入る可能性があります。
- **状態がおかしいと感じたら**: ラベルを手で直すのではなく、AI agent に症状を伝えて intent-cli への修復依頼を任せてください（[ループがおかしいときの復旧](07-recovery.md) を参照）。

## 次へ

[実装ループの設定](05-implementation-loop.md) | [packet 作成と issue 公開](04-packets-issues.md) | [ドキュメント索引](index.md)
