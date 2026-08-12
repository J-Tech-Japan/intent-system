# packet 作成と issue 公開

← [intent の整理・保守](03-intents.md) | [ドキュメント索引](README.md) | → [agent メッセージオーケストレーション](12-agent-message-orchestration.md)

これは **host/design** 作業です。intent が十分に固まったら、デザインスレッドがそれを **packet**（実行可能な実装単位）に分割し、1つずつ GitHub Issue として公開します。子実装 agent がその issue を受け取って実装します。

## packet とは

**packet** は、intent から切り出された焦点の絞られた実装スライスです。デザインスレッドが正本ファイル一式（`packet.yaml`、`implementation.md`、`review-context.md`、`github-body.md`）を scaffold します。これにより、何を作るかが明確に定義されます。`review-context.md` には、その packet の `intent_references` と overlap する G529 semantic-facet node（vocabulary/invariant/decider/acceptance-property）を一覧化した、生成済みの **Facet context** セクションが含まれます — 詳細は [facet を意識した context 供給 (G530)](09-developer-reference.md#facet-を意識した-context-供給-g530) を参照してください。

**issue 公開**はレビュー済みの packet を GitHub Issue に変換します。この issue は **Standalone Child Issue Contract** であり、子実装 agent が実装に必要な唯一の情報源です。子 agent は issue 本文とリポジトリのコードを参照するだけです。ホストメタデータにはアクセスしません。

## デザインスレッドプロンプト

AI agent のデザインスレッドに貼り付けてください:

> domain `<name>` の次の packet を作成し、その issue を `<owner>/<repo>` に公開したい。
> intent-cli に次に行うべきことを聞いてください。

AI agent が行うこと:
1. intent-cli で現在の intent と未完了作業を確認する
2. 次の packet を draft する（正本ファイルを scaffold）
3. Standalone Child Issue Contract のレビューを支援する
4. 正しいワークフローラベルで issue を公開する

公開後、issue はターゲットリポジトリに `intent-target` 付きで現れ、child implementation agent が受け取れる状態になります。

## ask-intent-cli プロンプトテンプレート

> packet `<id>` を作成し、その issue を `<owner>/<repo>` に公開する。
> intent-cli に次に行うべきことを聞いてください。

## metadata / label の安全境界

- **`intent-target` は公開境界コマンドが付与する。手作業では付けない**。
  子実装 agent も付けない。
- issue 本文は **standalone contract** であること — 子 agent はそれを唯一の
  正本となる定義として扱う（ホストメタデータにはアクセスしない）。

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。通常、ユーザーが直接実行する必要はありません。ワークフローのデバッグや host automation のメンテナンスを行う場合に参照してください。

```bash
# packet を scaffold（packet.yaml / implementation.md / review-context.md / github-body.md）
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --format markdown

# publish 前に必須: lexical facet-check の実際の結果を保存
intent-cli intent facet-check --domain <domain> --packet <id> --format json

# Standalone Child Issue Contract を検証してから公開
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
# 記録済み unit または issue 番号で intent-target を付与
intent-cli automation issue-publish --execution-unit <id> --write --format json
# issue 番号が既知の場合の同等な代替:
intent-cli automation issue-publish --issue <n> --write --format json
```

facet check は publish 前に必須ですが、結果は正直に記述します。
`no_facet_data: true` は facet annotation を持つ intent node が無いため lexical check が
**実行されなかった**ことを意味し、packet が pass した意味には決してなりません。
現在の intent-cli domain は facet node が無い実測例なので、human / agent による semantic
alignment review が引き続き必要です。この slice で green result を作るためだけに facet node
を author してはいけません。

## 新しい packet draft での有効な PR base branch

`packet draft` は、新しく作る `github-body.md` の `Expected PR base branch` を、
automation の各サーフェスと同じ effective-branch の判定で埋めます:

- `[project] implementation_base_branch` が設定されていれば、そのブランチを使う;
- 設定されていなければ、`base_branch_policy` の default branch を使う
  （`direct-main` → `main`、`main-ai` → `main-ai`）。

この挙動は新しく scaffold する packet body にだけ適用されます。`packet draft` は既存の
packet や公開済み issue 本文を書き換えず、`implementation_base_branch` が無い場合は従来の
scaffold 出力を byte 単位で維持します。

## 名前付き branch lane (G668 — preview-through-1.x)

host は domain ごとに `[project.branch_lanes.<domain>]` の下へ、名前付き
branch lane の registry を宣言できます:

```toml
[project.branch_lanes."intent-cli"]
default_lane = "continuous"
definition_revision = "registry-r1"

[project.branch_lanes."intent-cli".continuous]
start_branch = "develop"
pr_base_branch = "develop"
landing_mode = "direct"

[project.branch_lanes."intent-cli".hotfix]
start_branch = "main"
pr_base_branch = "main"
landing_mode = "direct"

[project.branch_lanes."sekiban-as-a-service"]
default_lane = "release"
definition_revision = "sekiban-r1"

[project.branch_lanes."sekiban-as-a-service".release]
start_branch = "release"
pr_base_branch = "main"
landing_mode = "integration-batch"
```

`packet draft --lane hotfix` で lane を明示的に選べます。`--lane` を省略すると設定された
`default_lane` を選び、`branch_lane_source: domain-default` として記録します。明示選択は
`branch_lane_source: explicit` です。draft は lane id、definition revision、start branch、
PR base branch、landing mode を含む `branch_lane` と `routing_snapshot` を `packet.yaml`
および `github-body.md` に materialize します。

この snapshot が accepted packet の routing の事実です。queue seed、projection regeneration、
review guidance、worker の base-branch check は materialize 済み snapshot を使い、後から registry
を編集しても既存 packet の宛先は変わりません。domain 選択は一致する registry だけを解決し、
一致する registry がない host は従来の `direct-main` / `main-ai` policy 名・field・出力と packet
draft の byte 単位互換性を維持します。以前の単一形 `[project.branch_lanes]` も互換性のため
読み込めますが、設定された project domain だけに scope されます。
名前付き lane は preview-through-1.x であり、branch の作成・管理は行いません。

## lane decision record と publish gate (G669 — preview-through-1.x)

lane の宣言は routing の事実であり、judgment そのものではありません。
`branch_lane` を持つ packet が publish boundary を越えるには、design が lane id、
解決済み branch、rationale、actor、timestamp、evidence、definition revision、
fingerprint を含む propose record を記録します:

```text
intent-cli automation branch-lane-propose-record \
  --execution-unit G669 --actor design --rationale "..." --evidence "..." --write
```

orchestration はその propose を独立に検証し、固有の actor、timestamp、evidence と
同じ routing fingerprint を持つ別の confirm record を記録します。`packet.yaml` や
`github-body.md` の prose は、どちらの record の代わりにもなりません:

```text
intent-cli automation branch-lane-confirm-record \
  --execution-unit G669 --actor orchestration --evidence "..." --write
```

record は `.intent-cli/branch-lane-decisions/<execution-unit>/propose.json` と
`confirm.json` に保存します。propose が無い confirm は拒否され、publish は GitHub
operation の前に missing、mismatch、malformed、または同一 actor の record を拒否します。
`branch_lane` が無い legacy packet は従来の publish path をそのまま維持します。

`automation stalled-work` は、confirmation が無い queued lane item が stale threshold を
越えたときだけ `branch-lane-decision-pending` を出します。packet、issue body、queue
snapshot、観測した PR base branch が食い違う場合は、PR が closed でも直ちに
`branch-routing-conflict` を出し、観測した全 value を列挙します。legacy packet には
どちらの classification も出しません。

## 代替: timer-loop のセットアップ

timer-loop の alternative を選ぶときだけ、[実装ループの設定](05-implementation-loop.md)、続けて
[レビュー / next-slice ループの設定](06-review-next-slice-loop.md) を使います。

## 次へ

[agent メッセージオーケストレーション](12-agent-message-orchestration.md)。
