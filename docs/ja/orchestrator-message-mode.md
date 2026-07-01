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

## Windows ガイダンス

- Windows では monitor モードの Claude Code receiver を **Git Bash** から起動してください。
  dogfooding では PowerShell / ネイティブ Windows 起動だと agmsg Monitor が確実に attach
  されない場合が確認されました（SessionStart の `watch.sh` ディレクティブは bash 環境を
  前提とします）。そのため receiver が `mode=monitor` と報告してもストリーミングしない
  ことがあります。
- Git Bash が使えない、または Windows で Monitor が依然として attach されない場合は、
  `turn` 配信または手動の `inbox.sh` ポーリングへフォールバックしてください（下記
  フォールバックの段階手順を参照）。`mode=monitor` だけを根拠に receiver を ready と
  報告しないでください。

## フォールバックの段階手順 — realtime Monitor なしでも orchestrator モードは使える

realtime Monitor 配信は利便性であり、orchestrator モードの **必須要件ではありません**。
success marker が欠けている場合は、この bounded な段階手順を実施し、その後は明示的な
フォールバックで作業を続けてください。ライブ monitor だと無言で主張しないこと。

1. receiver の Claude Code セッションを再起動し、SessionStart ディレクティブが新しい turn で
   `watch.sh`/Monitor を再起動するようにしてから、success marker を再確認する。
2. project trust / セッションを検証: exact-cwd の `~/.claude.json` project キーが trust
   accepted である必要がある（trust 修復 runbook を参照）。そのセッションで
   `ToolSearch select:Monitor` が汎用 Monitor ツールを解決することを確認する。
3. Windows では、PowerShell / ネイティブシェルではなく Git Bash から receiver を再起動する
   （Windows ガイダンスを参照）。
4. 既知の正常な receiver プロジェクト（すでに `1 monitor` / `Monitor event` を表示している
   もの）と比較し、破損がこの cwd の設定か環境かを切り分ける。
5. それでも attach しない場合は、`turn` 配信または手動 `inbox.sh` ポーリングへフォールバック
   してその旨を明示するか、operator にエスカレーションする。Bash/バックグラウンドの
   `watch.sh`（`1 shell`）は診断/フォールバック専用であり、Claude Code Monitor の代替には
   決してならず、receiver を live-monitored と報告する根拠にもなりません。

backend 固有の配信/watch の詳細は
[agmsg monitor-delivery ドキュメント](https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md)
を参照してください。intent-cli は agmsg 内部や Claude Code のツール可用性を所有・変更しません。

## Monitor が見つからない場合の project-settings 診断

上記の trust 修復 runbook とフォールバック段階手順は、`Monitor` ツールが *存在するが* attach
されていないことを前提とします。より優先度の高い別の失敗は、**`ToolSearch select:Monitor` が
`Monitor` ツールを一切見つけられない**場合です — `1 shell` と `1 monitor` の違いではなく、
ツール自体が存在しません。これは `delivery.sh status` `mode=monitor` が何を報告していても、
**agmsg 配信の問題である前に、まず Claude Code の tool-surface の問題**です。Claude Code が
露出していない Monitor ツールを通じて agmsg はストリーミングできないため、ここで agmsg を
デバッグするのは無駄になります。

**既知の正常環境との比較チェックリスト** — `1 monitor` がすでに機能するフォルダと、この
プロジェクトの Claude Code 設定を diff します:

- `.claude/settings.json`
- `.claude/settings.local.json`
- `~/.claude.json` の project trust / onboarding フラグ
- 有効/無効な MCP サーバーのリスト
- project レベルの `env` 設定

**疑わしい project レベルの `env` オーバーライド**（dogfooding で `.claude/settings.json` の
`env` 配下に観測）— tool surface を抑制し `Monitor` が現れなくなる原因:

- `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`
- `CLAUDE_CODE_ENABLE_TELEMETRY=false`
- `DISABLE_ERROR_REPORTING=true`
- `DISABLE_TELEMETRY=true`

これらの project `env` オーバーライドを削除または隔離する（agmsg hooks は保持）ことで、
該当フォルダで `ToolSearch select:Monitor` が復旧しました。

**安全な修復手順**（operator のアクション。agmsg には触れない）:

1. Claude Code セッションを閉じる。
2. 疑わしい project レベルの `env` 設定を削除または隔離する（**agmsg SessionStart hooks は
   保持**）。
3. Claude Code を再度開く。
4. `ToolSearch select:Monitor` を実行する。
5. inbox メッセージ到着時に `Monitor(agmsg inbox stream)`、フッター `1 monitor`、
   `Monitor event: "agmsg inbox stream"` を検証する。

これは agmsg の変更ではなく Claude Code の project-config 修復です。全体を通じて G516 の区別を
保ちます: `1 monitor` は成功、`1 shell` はフォールバック/失敗です。

このページは agmsg スクリプト（`watch.sh` / `delivery.sh`）を変更せず、intent-cli は
`.claude/settings.json` や `~/.claude.json` を編集しません。trust の修復と project-settings の
修復は operator のアクションのみです。
