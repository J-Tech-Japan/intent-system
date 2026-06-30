# orchestrator-message モード — Monitor ツールと delivery-mode の違い

← [agent メッセージオーケストレーション](12-agent-message-orchestration.md) | [docs インデックス](README.md)

このページは orchestrator-message モードにおける運用上きわめて重要な 1 つの区別を
ドキュメント化します: **Claude Code の `Monitor` ツールこそが agmsg inbox のメッセージ
を receiver にストリーミングする実際の仕組みであり、agmsg の `delivery.sh status`
`mode=monitor` は設定にすぎず、Monitor が attach されてストリーミングしている証明には
ならない、という点です。** 権威ある貼り付け可能なガイダンスはインストール済みの
intent-cli が生成します。次で生成してください:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --format markdown
```

このページはそのガイドの **Monitor tool vs delivery-mode (G511)** セクションを反映し、
公開ドキュメントとガイドが同期し続けるようにします。以下の検証/修復チェックリストが、
健全な receiver と無言で壊れた receiver を見分ける手段です。

## なぜ "monitor" は紛らわしいのか

"monitor" という語は無関係な 3 つのものを指すため、区別が明示されるまで、operator や
agent は健全な receiver と無言で壊れた receiver を見分けられません:

1. Claude Code の汎用 `Monitor` ツール — inbox ストリーム配信の実際の仕組み。
2. agmsg の `delivery.sh` `mode=monitor` 設定。
3. 無関係な `Azure Monitor` / その他 MCP の `monitor` ツール。

Claude Code の `Monitor` は汎用の Claude Code ツールであり、agmsg inbox を receiver に
ストリーミングする実際の仕組みです。agmsg は Claude Code の SessionStart ディレクティブ
から `watch.sh` を起動して `Monitor` を attach します。実行中の Monitor タスクこそが、
到着する agmsg の行をライブの transcript イベントに変えます。

agmsg `delivery.sh status` `mode=monitor` は設定にすぎず、Monitor ツールが attach されて
ストリーミングしている **証明にはなりません**。receiver は `mode=monitor` と報告しつつ、
Claude Code の `Monitor` が一切実行されておらずライブ配信が何もない、という状態があり得ます。
ライブ attach は delivery mode だけでなく、以下の success marker で確認してください。

## ライブ attach の success marker

inbox ストリームがライブであることを確認するため、4 つすべてを検証します:

- `ToolSearch select:Monitor` resolves Monitor — receiver セッションで Monitor が解決される（ツールが利用可能）。
- transcript に `Monitor(agmsg inbox stream)` が表示される — Monitor ツールが inbox ストリームに attach されている。
- Claude Code のフッターに `1 monitor` が表示される（ライブの Monitor タスクが attach されている）。
- inbox メッセージ到着時に transcript へ `Monitor event` の行が表示される（ストリームがライブ）。

## failure marker

`mode=monitor` を報告していても receiver が無言で壊れていることがあります:

- 配信が attach された Monitor ではなく、素の `Bash` / バックグラウンドの `watch.sh` タスクへ fallback している — ライブストリームなし。
- フッターに `1 monitor` ではなく `1 shell` が表示される（Monitor ではなくバックグラウンドシェルが実行中）。
- `Azure Monitor` / その他 MCP の `monitor` ツールとの混同 — これらは agmsg inbox ストリーミングと無関係で、attach の証明にはなりません。

## trust 修復 runbook

success marker が欠けている場合:

- 根本原因: `~/.claude.json` の exact-cwd の project キーが `hasTrustDialogAccepted=false`
  だと、Monitor を起動する SessionStart ディレクティブが抑制され、Monitor が attach されず
  inbox がストリーミングされません（receiver は依然として `mode=monitor` を報告します）。
- 修復（operator のアクションのみ）: その exact cwd について Claude project trust を修復し、
  receiver セッションを再起動してから、上記の success marker を再検証します。intent-cli は
  `~/.claude.json` を自動検出も自動編集もしません。

このページは agmsg スクリプト（`watch.sh` / `delivery.sh`）を変更せず、intent-cli は
`~/.claude.json` を編集しません。trust の修復は operator のアクションのみです。
