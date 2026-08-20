# はじめに: 最初の packet までの道のり

← [プロジェクト開始](02-project-start.md) | [ドキュメント索引](README.md) | → [intent の整理・保守](03-intents.md)

<a id="最初の-decision-2×2-onboarding-pattern-を選ぶ"></a>
## 最初の選択: 2×2 の導入パターンを選ぶ

ホストメタデータの置き場とプロジェクトが新規 / 既存のどちらかを最初に選びます。
各リンク先は意図的に自己完結しており、2 つのパターンの手順を混ぜる必要はありません。

| ホストメタデータ | 新規プロジェクト | 既存プロジェクトに intent-cli を追加 |
| --- | --- | --- |
| 別ホストリポジトリ | [別ホスト × 新規](02b-separate-host-brand-new.md) | [別ホスト × 既存](02c-separate-host-existing.md) |
| 同一リポジトリ、メタデータ用ブランチ | [同一リポジトリ × 新規](02d-same-repo-brand-new.md) | [同一リポジトリ × 既存](02e-same-repo-existing.md) |

<!-- G608 chooser identities: Separate host / Same repo; brand-new / existing. -->

各パターンは共存する貼り付け可能な最初のプロンプトをちょうど 2 つ提示します。4 agent 全員が
1 台に同居する場合は、依存関係が少ない `herdr-only` を優先します。分散チームまたは既存の
agmsg 投資があるチームには、サポート対象で廃止されない `agmsg` + herdr を選びます。どちらも
`session-layer set` で記録します。primary なのはトランスポートではなく
**4 スレッドモデル**です。最初のプロンプトの後はトランスポート固有の手順を混ぜず、記録された
mode と現在のインストール済みガイドに従います。

このページは最小開始から最初の公開 packet までの **orchestration-first** 経路です。
[プロジェクト開始](02-project-start.md) はトポロジーの正本となる定義であり、[agent メッセージ
オーケストレーション contract](12-agent-message-orchestration.md) はセッションレイヤーの意味の
正本となる定義として残ります。

pane を配置する前に、[チームのワークスペース配置（G637、preview）](12-agent-message-orchestration.md)
を読んでください。実際の workspace と pane ID が分かったら、operator が観測した shape を
`intent-cli guide workspace-layout` に渡します。同じ label vocabulary と 40% / 60% 均等分割を
導入時から確認できます。この guide は command を表示するだけで、herdr を駆動しません。

## これから設定するもの

4 スレッドモデルが **primary** です。design は intent を作成し、orchestration は
調整し、implementation は子 PR を届け、review は確認します。1 台の
マシンに同居するチームには、この経路では依存関係が少ない `herdr-only` トランスポートを優先します。
分散 / 既存 agmsg のチーム向けの `agmsg` + herdr はサポート対象で廃止されません。primary は
4 スレッドモデルだけであり、どちらのトランスポートでもありません。

## 1. repository と folder を選ぶ

まず [02 のリポジトリトポロジー比較](02-project-start.md#リポジトリトポロジーの選択)で
トポロジー A/B を選びます。ここには表を複写しません。永続化されたホスト状態の置き場は
02 が決めます。

別ホストリポジトリを使う場合の実用的な 4 ロール構成は次のとおりです。

```text
~/work/my-project-host/          # design と orchestration がこの host checkout を開く
~/work/my-project/               # implementation がこの target-repo checkout を開く
~/work/my-project-review/        # review がこの分離した target-repo checkout を開く
```

design と orchestration はホスト側の intent / workflow の決定を所有するためホストの checkout を
共有します。implementation は実装用 checkout だけを開き、GitHub issue/PR contract に
従います。review は検査、テスト成果物、必要になった修復が進行中の implementation
worktree を乱さないよう独立した checkout を使います。

## 2. ホストをインストール・初期化する

[インストール](01-install.md) に従い、ホストの checkout から [02 の初期化フロー](02-project-start.md#agent-が実行するコマンドメンテナトラブルシューティング向け)
を実行します。ホストの確認が済んだら、**新しい**チームを次の順で立ち上げます。

> **新規チーム専用。** この順番は新しい状態を表示する前に記録します。トランスポートを変更する
> 既存チームは、`session-layer set --write` が最終の正本となる手順になる
> [doc 12 のセッションレイヤー切替チェックリスト](12-agent-message-orchestration.md#session-layer-switch-checklist)
> に従います。新規チームの順番をトランスポート切替の手順として使わないでください。

### 2.1 トランスポートを記録する

design/orchestration agent に次のプロンプトを貼り付けます。

> ホストの checkout で domain `<domain>` の新規チーム `<team>` に `herdr-only` を記録してください。
> `intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only --write --format json`
> を実行し、返った JSON を表示してください。残留物やワークスペースから mode を推測してはいけません。

隔離した scratch host で出荷済みの `intent-cli 0.11.0-7b3800e-G606` を実行したときの成功時の形は
次のとおりです（動的な timestamp と移行項目は省略）。

```json
{
  "domain": "onboarding",
  "team": "docs-team",
  "mode": "herdr-only",
  "source": "recorded",
  "command_mode": "write",
  "applied": true,
  "changed": true,
  "summary": "team `docs-team` in domain `onboarding`: session layer is herdr-only (preferred — fewer dependencies) (recorded)."
}
```

この初回の記録は `migration_plan` array も返しました。これは mode change の出力です。既存チームの
移行手順はこのページではなく doc 12 を使用してください。

### 2.2 すべてのロールのトポロジーを記録する

実際の herdr ワークスペース/pane ID が分かった後、design/orchestration agent に次のプロンプトを貼り付けます。

> domain `<domain>`、team `<team>` に `design`、`orchestration`、`implementation`、`review`
> ロールを記録してください。各ロールに対して `intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> --write --format json` を実行してください。明示的な domain、operator が
> 与えた workspace/pane ID、上記の構成のフォルダを渡し、ID を推測したりトポロジーファイルを
> 手編集したりしてはいけません。

scratch host の各実行は、記録したロールを `role` に持つ同じ成功時の形を返しました。

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

観測では継続前に 4 ロールすべてを記録しました。記録コマンドは制御された writer です。
residency の選択肢と検証規則の詳細は [doc 12](12-agent-message-orchestration.md) に残します。

### 2.3 可視の marker を生成する

`AGENTS.md` または `CLAUDE.md` に別 domain の valid な managed marker block がすでにある場合は、
手編集したり placeholder を追加したりしません。canonical writer が不足している `(domain, team)` block
を append します。managed block が 1 つもない file だけは、[doc 12 の生成済み marker 節](12-agent-message-orchestration.md#可視な生成済み-mode-marker)
の指定どおり空の placeholder を 1 つ置きます。続けて次のプロンプトを貼り付けます。

> 記録済み domain `<domain>`、team `<team>` の marker を生成してください。
> `intent-cli session-layer marker generate --domain <domain> --team <team> --file AGENTS.md --write --format json`
> を実行して JSON を表示してください。別の managed block がある場合はこの domain の block を append し、他の block をすべて保持してください。

scratch-host の実行時の成功形は次のとおりです（記録 hash は動的）。

```json
{
  "written": true,
  "file": "AGENTS.md",
  "domain": "onboarding",
  "team": "docs-team",
  "mode": "herdr-only",
  "verify_command": "intent-cli session-layer show --domain onboarding --team docs-team",
  "marker_action": "appended",
  "preserved_existing_blocks": 1,
  "summary": "Appended the managed session-layer marker for domain 'onboarding' and team 'docs-team' in 'AGENTS.md'; preserved 1 existing managed block(s)."
}
```

### 2.4 構造上の準備状態を確認する

design/orchestration agent に次のプロンプトを貼り付けます。

> `intent-cli automation doctor --domain <domain> --team <team> --format json` で新しいチームの
> 共有 preflight を確認してください。`session_layer_preflight` の結果を報告し、doctor だけから
> delivery の準備状態を主張しないでください。

scratch-host の実行では構造上 ready でした。

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

ここでの `ready` は共有の **passive structural** verdict です。delivery surface は delivery を
claim する前に自身の bounded receiver check を実行します。

## 3. 最初の packet を作る

セッションレイヤーは記録され、可視性も得ました。[intent の整理・保守](03-intents.md)、続けて
[packet 作成と issue 公開](04-packets-issues.md) に進んでください。最初の packet が公開可能になった
時点から、それらのページが引き継ぎます。

## 代替経路

- **agmsg + herdr:** 分散または既存 agmsg の選択では [agent メッセージ
  オーケストレーション contract](12-agent-message-orchestration.md) を使います。
- **timer-loop:** [実装ループの設定](05-implementation-loop.md) と
  [レビュー / next-slice ループの設定](06-review-next-slice-loop.md) を使います。上の
  orchestration-first 経路の alternative です。

## 次へ

[intent の整理・保守](03-intents.md)。
