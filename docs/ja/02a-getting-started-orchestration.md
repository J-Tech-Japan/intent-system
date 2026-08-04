# はじめに: 最初の packet までの道のり

← [プロジェクト開始](02-project-start.md) | [ドキュメント索引](README.md) | → [intent の整理・保守](03-intents.md)

## Minimal start: 空の 2 repository と 1 つの prompt

空の implementation repository と空の intents host repository を作成します。**host だけを**
checkout し、そこで AI agent を開いて次を貼り付けます:

> target implementation repository `<owner>/<implementation-repo>` 用に intent-cli を
> 設定します。空の intents host repository を開いています。まずインストール済みの
> guidance で intent-cli を理解し、それから初期化を案内してください。1 回に 1 つずつ
> decision を聞いてください。

1 repository の選択肢では、先に [topology B](02-project-start.md#トポロジー-b--metadata-ブランチを使う同一リポジトリ)
を選び、2 つ目の repository は作りません。agent は shipped skill から `guide onboarding` に進み、
`intent-cli --version` を確認し、`guide model` を読み、`intent init` を dry-run と `--write` で
各 1 回実行して host を確認します。観測した v0.11.0 の write は **9 files** を生成し、
`host-check` は `"classification": "ok"` を返しました。human が決めるのは repository topology、
base-branch policy、transport、role ごとの agent kind の 4 点です。

4 agent 全員が 1 台に collocate する場合は `herdr-only` を最初に選びます。**PREVIEW** は
maturity note であり、非推奨という意味ではありません。distributed team または既存の agmsg
investment がある場合は `agmsg` + herdr を選びます。どちらも supported choice であり、
`session-layer set` で record します。primary なのは transport ではなく **4 スレッドモデル**です。

このページは minimal start から最初の公開 packet までの **orchestration-first** 経路です。
[プロジェクト開始](02-project-start.md) は topology の authority、[agent メッセージ
オーケストレーション contract](12-agent-message-orchestration.md) は session-layer semantics の
authority として残ります。

## これから設定するもの

4 スレッドモデルが **primary** です。design は intent を author し、orchestration は
coordinate し、implementation は child PR を deliver し、review は確認します。1 台の
machine に collocate する team には、この経路では `herdr-only` transport を推奨します。
`herdr-only` が **PREVIEW** なのは transport だけで、4 スレッドモデルではありません。

## 1. repository と folder を選ぶ

まず [02 の repository topology 比較](02-project-start.md#リポジトリトポロジーの選択)で
topology A/B を選びます。ここには table を複写しません。durable host state の置き場は
02 が決めます。

別 host repository を使う場合の実用的な 4-role layout は次のとおりです。

```text
~/work/my-project-host/          # design と orchestration がこの host checkout を開く
~/work/my-project/               # implementation がこの target-repo checkout を開く
~/work/my-project-review/        # review がこの分離した target-repo checkout を開く
```

design と orchestration は host-side intent / workflow decision を所有するため host checkout を
共有します。implementation は implementation checkout だけを開き、GitHub issue/PR contract に
従います。review は inspection、test artifact、必要になった repair が active な implementation
worktree を乱さないよう独立 checkout を使います。

## 2. host を install・initialize する

[インストール](01-install.md) に従い、host checkout から [02 の初期化フロー](02-project-start.md#agent-が実行するコマンドメンテナトラブルシューティング向け)
を実行します。host check が済んだら、**新しい** team を次の順で stand-up します。

> **新規 team 専用。** この順番は new truth を表示する前に record します。transport を変更する
> 既存 team は、`session-layer set --write` が final canonical step になる
> [doc 12 の Session-layer switch checklist](12-agent-message-orchestration.md#session-layer-switch-checklist)
> に従います。新規 team の順番を transport switch の手順として使わないでください。

### 2.1 transport を record する

design/orchestration agent に次の prompt を貼り付けます。

> host checkout で domain `<domain>` の新規 team `<team>` に `herdr-only` を record してください。
> `intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only --write --format json`
> を実行し、返った JSON を表示してください。residue や workspace から mode を infer してはいけません。

隔離 scratch host で shipped `intent-cli 0.11.0-7b3800e-G606` を実行したときの success shape は
次のとおりです（dynamic timestamp と migration item は省略）。

```json
{
  "domain": "onboarding",
  "team": "docs-team",
  "mode": "herdr-only",
  "source": "recorded",
  "command_mode": "write",
  "applied": true,
  "changed": true,
  "summary": "team `docs-team` in domain `onboarding`: session layer is herdr-only (PREVIEW — session transport only) (recorded)."
}
```

この初回 record は `migration_plan` array も返しました。これは mode change の出力です。既存 team の
migration procedure はこのページではなく doc 12 を使用してください。

### 2.2 すべての role の topology を record する

実際の herdr workspace/pane ID が分かった後、design/orchestration agent に次の prompt を貼り付けます。

> domain `<domain>`、team `<team>` に `design`、`orchestration`、`implementation`、`review`
> role を record してください。各 role に対して `intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> --write --format json` を実行してください。明示的な domain、operator が
> supplied した workspace/pane ID、上記 layout の folder を渡し、ID を推測したり topology file を
> 手編集したりしてはいけません。

scratch host の各 run は、record した role を `role` に持つ同じ success shape を返しました。

```json
{
  "team": "docs-team",
  "role": "design",
  "resident": "herdr",
  "mode": "write",
  "record_path": ".intent-cli/topology/onboarding/docs-team.json",
  "applied": true,
  "changed": true,
  "already_recorded": false,
  "conflict": false,
  "summary": "Recorded operator-supplied role 'design' for team 'docs-team'."
}
```

観測では継続前に 4 role すべてを record しました。record command は controlled writer です。
residency variant と validation rule の詳細は [doc 12](12-agent-message-orchestration.md) に残します。

### 2.3 visible marker を generate する

generate の前に、domain/team 用の空の managed marker block を `AGENTS.md` または `CLAUDE.md` に
[doc 12 の generated marker 節](12-agent-message-orchestration.md#可視な生成済み-mode-marker)の
指定どおり 1 つ置きます。続けて次の prompt を貼り付けます。

> recorded domain `<domain>`、team `<team>` の marker を generate してください。
> `intent-cli session-layer marker generate --domain <domain> --team <team> --file AGENTS.md --write --format json`
> を実行して JSON を表示してください。managed marker block だけを変更してください。

scratch-host run の success shape は次のとおりです（record hash は dynamic）。

```json
{
  "written": true,
  "file": "AGENTS.md",
  "domain": "onboarding",
  "team": "docs-team",
  "mode": "herdr-only",
  "verify_command": "intent-cli session-layer show --domain onboarding --team docs-team",
  "summary": "Generated the managed session-layer marker for team 'docs-team' in 'AGENTS.md'."
}
```

### 2.4 structural readiness を確認する

design/orchestration agent に次の prompt を貼り付けます。

> `intent-cli automation doctor --domain <domain> --team <team> --format json` で新しい team の
> shared preflight を確認してください。`session_layer_preflight` result を報告し、doctor だけから
> delivery readiness を主張しないでください。

scratch-host run は structural ready でした。

```json
{
  "status": "ok",
  "topology_health": { "status": "valid", "required": true },
  "session_layer_preflight": {
    "verdict": "ready",
    "ready": true,
    "passive_phase": { "status": "ready", "contacted_receiver": false },
    "active_phase": { "status": "skipped", "contacted_receiver": false }
  }
}
```

ここでの `ready` は shared **passive structural** verdict です。delivery surface は delivery を
claim する前に自身の bounded receiver check を実行します。

## 3. 最初の packet を作る

session layer は record され visibility も得ました。[intent の整理・保守](03-intents.md)、続けて
[packet 作成と issue 公開](04-packets-issues.md) に進んでください。最初の packet が公開可能になった
時点から、それらのページが引き継ぎます。

## 代替経路

- **agmsg + herdr:** distributed または既存 agmsg の選択では [agent message
  orchestration contract](12-agent-message-orchestration.md) を使います。
- **timer-loop:** [実装ループの設定](05-implementation-loop.md) と
  [レビュー / next-slice ループの設定](06-review-next-slice-loop.md) を使います。上の
  orchestration-first route の alternative です。

## 次へ

[intent の整理・保守](03-intents.md)。
