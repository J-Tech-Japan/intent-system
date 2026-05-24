# ループがおかしいときの復旧

> **まず intent-cli に聞く** — 状態を手で直さない。`intent-cli guide start` の後、
> 以下の read-only な preflight/doctor サーフェスを実行する。 ← [ドキュメント索引](index.md)

ループが詰まっている・おかしい・修正が scope 内か不明なときは、label や metadata を
直接編集する代わりに、`intent-cli` に分類させ、（あれば）どのコマンドが修復を
所有するかを尋ねる。

```bash
# この PR のレビュー指摘は安全で scope 内の child 修復か？
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr <n> --format json

# この issue を issue-to-pr として（再）claim して安全か？
intent-cli worker issue-preflight --repo <owner>/<repo> --issue <n> --format json

# CLI の鮮度 / host-state 解決
intent-cli automation doctor --format json
```

結果を読む: `actionable` / `safe_repair_available` / `repair_category` が、
child-loop 所有の修復が存在するかを示す。host 所有のカテゴリは
`host-artifact-repair-required` として現れ、host ループへ戻す。

## ask-intent-cli プロンプトテンプレート

> `<owner>/<repo>`（PR/issue `<n>`）でループがおかしい。何も触る前に、対応する
> `intent-cli worker …-preflight` と `intent-cli automation doctor` を実行し、CLI が
> 安全かつ scope 内と判断した修復のみ適用する。label/metadata を手編集しない。

## metadata / label の安全境界

- 復旧は `queue-state.json` や label の手編集を意味しない。preflight サーフェスは
  read-only で、所有コマンドを示す。
- child implementation agent が所有する修復は `child-selector-label-gap` のみ。
  それ以外は host/review 所有。
- 永続的な PR ブロッカーコメント（チャットだけにしない）が現在 PR の AC ブロッカーを
  記録する。より広い能力ギャップは follow-up issue/packet/signal に振り分ける。

## 索引へ戻る

[ドキュメント索引](index.md) に戻るか、`intent-cli guide start` を再実行する。
