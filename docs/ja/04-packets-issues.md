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
intent-cli packet draft --execution-unit <id> --target-repo <owner>/<repo> --team <team> --format markdown

# publish 前に必須: lexical facet-check の実際の結果を保存
intent-cli intent facet-check --domain <domain> --packet <id> --format json

# 同じ claim judgment で検証済み packet を queue に投影
intent-cli automation queue-seed-from-packet --execution-unit <id> --target-repo <owner>/<repo> --team <team> --format json

# Standalone Child Issue Contract を検証してから公開
intent-cli issue validate-body ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --team <team> --write --format json
# 記録済み unit または issue 番号で intent-target を付与
intent-cli automation issue-publish --execution-unit <id> --write --format json
# issue 番号が既知の場合の同等な代替:
intent-cli automation issue-publish --issue <n> --write --format json
```

claims-enabled host では最初の scaffold より前に plain push 成功で
`execution-unit:<id>` を acquire し、draft、queue seed、publish、next-slice、worker
selection に invoking team を渡します。すべて同じ read-only claim judgment を参照します。
unheld scope または別 team の record は scope、holder、holder team を名指して拒否します。
`.intent-cli/claims/` が存在しない場合、これらの surface は legacy single-team 出力を
byte 単位で維持します。

番号割り当ては claim-then-draft です。N を計算し `execution-unit:<N>` を claim し、
勝者だけが scaffold します。敗者は fast-forward 後に次番号を再計算し、その新しい scope を
exactly once retry します。2 回目も失敗したら停止します。GitHub label は visibility と
defence in depth のままで、ownership fact ではありません。

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
landing_mode = "operator-merge"

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

### 人が landing する権限 (G678 — preview-through-1.x)

`landing_mode = "operator-merge"` は review と approval を従来どおり実行し、不可逆な
landing step の実行者だけを変更します。lane の PR が approved かつ exact-head checks green
になると、automation は PR、lane、approval evidence を持つ `awaiting-operator-merge` を
表示します。これは review debt や stall ではなく patient state です。entry 時に design へ
1 回だけ通知し、supervision は待機を urge、remind、age-escalate しません。

その PR を merge するのは人だけです。intent-cli のどの path も operator-merge lane を
merge しません。GitHub で人の merge を検知すると、patient state を即時の closeout-only
continuation に置き換え、通常の closeout flow を自動的に再開します。`direct`、
`integration-batch`、registry 未設定 lane の既存 behavior と output は変わりません。

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

## JSON payload の issue 本文 gate (G847)

`issue create`、`queue dispatch`、`bug implementation-issue` は
`{title, body}` を JSON document に serialize して `gh api --input` で送信
します。共有 `IssueBodySizeLimits` は `HardLimitBytes = 65536` と
`WarningThresholdBytes = 58000` を宣言します。hard boundary は submitted
body content に対して inclusive です。65,535 と 65,536 bytes は受理し、
65,536 bytes を超える body は exit code 1 と次の plain message で拒否します:

```text
Issue body is <n> bytes, which exceeds the 65536-byte limit.
```

この gate が測るのは `CreateIssue` に渡す decoded string そのもので、
`Encoding.UTF8.GetByteCount` による submitted body content です。wire 上の
bytes ではありません。JSON payload には title も含まれ、body は escape
されるため、JSON overhead は content-dependent です。この overhead の
byte range は contract にしません。**65,536 は submitted body content に
対する intent-cli 自身の conservative limit です。** GitHub の boundary、
inclusive かどうか、unit、JSON payload の扱いは verify していません。利用
できる remote datum は、2026-09-16 に約 96,000 characters の body の publish
が失敗したという記録だけです。

この JSON-payload route の body を供給する全 read は、共有
`StrictUtf8FileReader.ReadText(path)` helper を使います:
`IssueCreateCommand.cs:88`、`QueueDispatchCommand.cs:124`、
`BugImplementationIssueCommand.cs:72`、`:152`、`:445`、`:599`、`:607`、
`:615`。bytes を strict UTF-8 として decode し、malformed input は size check
より前に exit code 1 で拒否します:

```text
Issue body is not valid UTF-8 at byte offset <k> in <file>.
```

この JSON-payload mechanism に限り、decode 後の先頭にある UTF-8 BOM を
ちょうど 1 つ取り除き、UTF-8 file の submitted content を従来と同じに
します。UTF-16 または UTF-32 BOM で始まる file は拒否します。この BOM
behavior は JSON-payload mechanism に scope されます。姉妹 G845 の
`--body-file` route は file bytes を `gh` に渡すため、counting rule は
異なります。この unit は `--format` option や result surface を追加しません。
この 3 command の ledger にある generic な `--format json` row は、既存の
不正確さを記録したものであり、この unit では修正しません。

contract の hedge は次のとおりです: **65,536 is intent-cli's own
conservative limit on the submitted body content; GitHub's boundary,
inclusivity, unit and treatment of a JSON payload were not verified.**

## early issue-body size の reporting と refusal (G846)

issue-body workflow は `github-body.md` 自体の raw bytes を数えます。UTF-8
BOM も bytes に含め、decoded characters は数えません。共有する
`IssueBodySizeLimits` は `HardLimitBytes = 65536` と
`WarningThresholdBytes = 58000` です。warning band は 58,000 から 65,536
bytes まで inclusive で、65,536 bytes を超える body は local create-path
gate が拒否します。

`issue validate-body` は `body_bytes`、`body_too_large`、
`body_size_warning`、`body_size_reason` を返します。`packet draft` は常に
top-level `warnings` array を返し、default と `--dry-run` の両方で
`issue-body-too-large` を理由に拒否しますが scaffolding は保持します。
`issue publish-flow` は unpublished create の oversized body を creator または
durable write の前に拒否します。already-published body は idempotent に扱い、
size warning として `issue-body-too-large` を返します。`issue sync-body` は
`warnings` を返し remote read の前に `reason_code: body-too-large` で拒否します。
4 つすべての command surface で warning literal `issue-body-size-warning` を
保持します。

**65,536 is intent-cli's own conservative limit. GitHub's boundary, inclusivity and unit were not verified. The only remote datum is the roughly 96,000-character failure reported on 2026-09-16.**

## 代替: timer-loop のセットアップ

timer-loop の alternative を選ぶときだけ、[実装ループの設定](05-implementation-loop.md)、続けて
[レビュー / next-slice ループの設定](06-review-next-slice-loop.md) を使います。

## 次へ

[agent メッセージオーケストレーション](12-agent-message-orchestration.md)。
