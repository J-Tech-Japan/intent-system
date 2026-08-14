# agent メッセージオーケストレーション（single-domain と multi-domain）

← [packet 作成と issue 公開](04-packets-issues.md) | [docs インデックス](README.md)

このページは **主要な 4 スレッドモデル**（design / orchestrator / implementation /
review）と、特に 1 つのホストリポジトリが **複数の intent ドメイン** を保持する場合に
それを安全に保つ方法を説明します。1 台のマシンに同居するチームは、依存関係が少ない
`herdr-only` トランスポートを優先します。分散したチームまたは既存の agmsg 投資があるチームには、
サポート対象で廃止されない `agmsg` + herdr を選びます。選択は
`session-layer set` で記録し、どちらのトランスポートも主要ではありません。正本となる
貼り付け可能なプロンプトはインストール済みの intent-cli ガイダンスから生成され、このページの
プロンプトを手で写してはいけません。現在のプロンプトは次で生成します:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

## team shape: delivery と authoring-only（G691 — preview-through-1.x）

team shape は session-layer transport とは独立です。永続 record は次の
canonical command だけで書き込みます:

```text
intent-cli team-mode show --domain <domain> --team <team> --format json
intent-cli team-mode set --domain <domain> --team <team> --mode delivery|authoring-only --write --format json
intent-cli team-mode validate --domain <domain> --team <team> --format json
```

`--team` が optional な read / consumer command では、domain-wide record を優先します。
domain-wide record がなく、その domain に team-scoped record が 1 件だけある場合は、
intent-cli がその team context を一意に解決し、authoring-only の audit / handoff record
にも effective team を保持します。team-scoped record が複数ある場合は、名前付きの
`team-mode-ambiguous` outcome で安全側に停止し、呼び出し側は `--team` を明示しなければ
なりません。したがって `--team` の省略によって recorded authoring-only team が
delivery に silently fallback することはありません。

record がない場合は `delivery` で、既存の behavior を byte-for-byte で保ちます。
`authoring-only` では operator-facing front door が intent を `shape/interview` し、standalone
packet を `author` し、issue を `publish` します。bootstrap が確認するのは front door と
repository/claim/publish prerequisite だけで、delivery topology や delivery seat の起動を
要求しません。`guide next` が提示するのは shape/interview、packet authoring、publish、improve、
inspect、idle だけです。

測定された bootstrap state は、永続的な `team_mode=authoring-only` record と front-door shape
を確認した時点で `authoring-only-complete` になります。repository、claim、publish command
は明示的な operator prerequisite として表示されますが、delivery-topology の missing fact
ではありません。

`notify supervise`、`notify supervise install`、`notify adjudicate`、delivery-topology command は
`not-applicable-team-mode` という名前付き outcome を返します。`notify adjudicate` は、
authoring-only team に adjudicate 対象となる delivery seat または adjudication dialog が存在しないため、
applicable ではありません。G691 の gate は `notify report`、`notify escalate`、`notify status`、
`notify dispose` を無効化せず、これらの report / settlement surface は利用できます。この slice は
publish、delegation、handoff の新しい behavior を追加しません。supervisor、worker lifecycle、transport
migration は発生しません。`delivery` が default で、このページの 4-thread / supervision contract は
変更されません。[ADR 0005](../adr/0005-team-mode-authoring-only.md) を参照してください。parent intent
の ADR-014 は host-side successor link が書かれるまで変更しません。

## authoring-only の publish audit と共有 diagnostics（G692 — preview-through-1.x）

authoring-only の front door が issue を publish できるのは、既存の readiness、claim、repository、
content、duplicate、branch-lane の全 gate を通過した後だけです。publish command は design actor、明示的な
operator-acceptance evidence、destination ownership を記録し、作成した issue に対応する永続的な
`published-external-handoff` record を書きます。この record は handoff を観測可能にしますが、worker を
認可したり publish gate を迂回したりしません。delivery-mode の issue content、issue creation、run-event
の byte は変更されません。

lane を宣言した packet では、authoring-only は design の proposal を記録し、異なる operator による
confirmation を要求します。この operator lane は orchestration の impersonation ではありません。
`orchestration` を名乗る confirmation は拒否され、named worker role への `notify delegate` は outbox や
transport に触れず、nonzero の `not-applicable-team-mode` refusal を返します。`notify delegate` は domain に一意な
team-scoped record がある場合だけ `--team` を省略でき、intent-cli は
その team を refusal / delivery judgment に引き継ぎます。一意な team context がなければ outbox より前に
安全側で停止します。`automation stalled-work` は
matching handoff record が現れるまで `published-not-delegated` を表示し、exact な destination/issue evidence
がある場合だけこの observation を抑制します。

`automation stalled-work`、`automation state-doctor`、`intent status`、`status brief` は 1 つの shared
team-mode capability matrix を使います。authoring-only では worker、review、CI、delegation、supervisor
class を明示的に not applicable とし、authoring、contract/readiness、branch-lane、branch-routing、
publish の永続状態 drift、knowledge/guide-writeback は active のままです。delivery は全 class と既存の
output を保持します。これは diagnostic judgment だけであり、publish、claim、ownership gate を弱めません。

## completion continuation chain の永続記録（G695 — preview-through-1.x）

G695 は、誰が実行権限を持つかを変更せずに completion から次の action までの境界を観測可能にします。
delivery 済みの `notify report` と、herdr が観測した `working→done|blocked|idle` transition はそれぞれ
次の append-only chain を開始します:

```text
.intent-cli/continuation-chains/<domain>/<team>/chains.jsonl
```

chain の順序は次です:

```text
report-received
  → orchestration-wake-attempted
  → wake-delivered-or-observed
  → canonical-state-classified
  → required-continuation-started | named-blocker-recorded
```

各 link は timestamp 付きで、record は正確な次の missing link を返します。全 chain を読むか、1 つの completion
signal で絞り込む read-only surface は次です:

`canonical-state-classified` があるのに terminal link がどちらもない場合、`next_missing_link` には
`required-continuation-started|named-blocker-recorded` が出力されます。したがって classified-then-stop の chain が
complete に見えたり、何も owed でないと表示されたりしません。

```text
intent-cli automation continuation-chain --domain <domain> --team <team> \
  [--task-id <task-id>|--completion-signal-id <signal-id>|--chain-id <chain-id>] \
  [--routing-root <host-root>] --format json|markdown
```

report と supervision の writer は observation だけを記録します。orchestration wake は signal を
`automation host-loop-wake` の `--write` に渡すことで後続 link を追加できます。安全な continuation は
`required-continuation-started` を記録し、refusal または judgment-gated path は
`named-blocker-recorded` を記録しなければなりません。terminal link がない classification は done と扱わず、
silent stop として query できます。

measured supervisor は #1491 の 3 つの canonical owed-transition shape も命名します:
exact-head と all-green evidence を持つ `approved-direct-lane-merge-closeout-owed`、declared write-back target
を持つ `merged-pr-knowledge-writeback-dispatch-owed`、empty WIP と issue-cut-ready evidence を持つ
`actionable-queue-next-slice-publication-owed` です。`owed_transition` と `evidence` は diagnostic finding であり、
chain と supervision は merge、publish、key relay、その他の transition 実行を行いません。

> **1.x を通じた preview (G695)。** chain file、query surface、observed transition seed、terminal-link evidence、
> 3 つの named finding は additive observability です。権限境界、message transport、既存の
> G654/G657/G659/G685 wake contract は変更しません。

## application front door からの bootstrap（G664 — preview-through-1.x）

desktop app conversation から **`Start this work in a herdr-only team.`** または
**`herdr-only で起動して。`** と依頼し、現在の guided pass を次で表示します。

```text
intent-cli guide bootstrap --domain <domain> --team <team> --target-repo <owner/repo> --routing-root <host-root> --format markdown
```

6 step の順序は固定です。最初に design / orchestration / implementation /
review の各 seat が使う CLI と model を人間へ質問し、default は置きません。次に
installed recipe と G637 layout guide に従う herdr workspace / pane / typed-seat
command を出力し、operator-supplied topology を記録し、`notify supervise
install` を出力します。その後 application kind と inbound app monitor の有無を
人間へ質問してから G654 の design placement rule を適用し、最初の task を
orchestration へ委譲します。最後の出力は、どの thread が新しい design seat
なのか、application conversation は loop seat ではなく operator's front door のまま
であることを明示します。

recorded topology があれば `join-and-delegate` となり、workspace や seat を再作成
しません。partial state は `topology-recorded-seats-missing`、
`topology-recorded-supervision-and-handoff-missing` などの名前で示し、missing command
だけを出力します。`guide next --domain <domain> --team <team>` は recorded topology
があり completed supervision cycle / application-front-door handoff がない場合に
`bootstrap-resume` を推奨し、cycle 完了後は直ちに silent になります。topology が
なければ bootstrap は未開始なので silent です。

この surface executes nothing です。intent-cli は herdr を呼び出さず、provider を
起動せず、OS scheduler artifact を登録 / 解除せず、application-side
integration を追加しません。既存 recipe、deployment rule、4 judgment thread + 1
supervision process formula、preview-through-1.x boundary を変更せず構成します。

### host-local model resolution（G685 — preview-through-1.x）

bootstrap、seat recovery、kind switch では、operator の informal な model / effort 名を
必ず次の順序で解決します。

1. `intent-cli session-layer model-resolution query` で host-local measured ledger を検索する。
2. miss の場合は `herdr agent list` を実行し、`result.agents[].agent` が resolved kind と
   完全一致する running entry を残し、workspace と pane の順に並べる。選択した pane ごとに
   `herdr pane process-info --pane <selected-pane-id>` を実行する。
3. `result.process_info.foreground_processes[].argv` を読み、選択した同一 kind の seat
   すべてで full invocation が一致するときだけ再利用する。
4. 読み取り可能な一致 argv が無い場合は human に質問する。

bare model id を推測せず、shipped list を参照しません。intent-cli が出荷するのは実測済みの
stable flag grammar だけです。Codex は `--model <id> -c
model_reasoning_effort=<level>`、Claude は `--model <id> --effort <level>` を使います。
他 kind の grammar は発明しません。

表示された launch attempt ごとに、retry または続行の前に対応する記録 step が必須です。
READY の後は、取得した informal name、kind、exact launched invocation、banner / running argv
evidence を含む、表示済みの `model-resolution record --outcome verified` command を実行します。
refusal の後は、取得した exact invocation と error text を含む、表示済みの
`--outcome refused` command を実行します。次回 query は negative evidence を明示し、
同一 invocation の retry を許可しません。JSONL ledger は machine-local な
`.intent-cli/model-resolution/ledger.jsonl` にあり、configuration ではなく measurement です。
sharing mechanism も catalogue もありません。

2026-08-12 の btx-mvc setup で、`--model sol` は account-shaped HTTP 400 になりました。
別 workspace の running Codex argv を読むことで working full invocation を回復しました。その
provider id は host-local evidence のままで、intent-cli とこの guide には意図的に収録しません。

これらの command は provider を起動せず、provider API に対して id を検証しません。
G647 envelope field と G684 の model / effort を wish とする drift semantics は不変です。

## design thread の運用 contract（G654 — preview-through-1.x）

agent kind に依存しない contract は `intent-cli guide design-thread` で表示します。
`guide commands list` が catalog に載せ、`guide next` が design role 向けにこの guide を示します。
内容は `agmsg` / `herdr-only` と team 指定の有無で変わりません。guide を再読するのは
CLI version または session-layer configuration が変わったときで、wake ごとではありません。

### role contract の precedence（G672 — preview-through-1.x）

`guide next` または `guide onboarding` を `--role` 付きで呼ぶと、contract を持つ role の
installed operating contract が最初の read-before-acting instruction になります。`design` は
`intent-cli guide design-thread`、`orchestration` は `intent-cli guide orchestrator-thread`、
`implementation` は `intent-cli guide worker issue-to-pr`、`review` は `intent-cli guide review` です。
contract がない role には invented pointer を出しません。これは新しい pointer の順序だけを
追加し、既存 procedure の削除・reorder、contract text、wake ごとの reread を変更しません。
CLI version または session-layer configuration の変更後に再読します。

**Measured incident record（attributed field evidence）。** issue #1441 sections D/B-1 の
operator-filed feedback（remote-herdr、48 units）では、数日間 design seat が自分の contract を読まず、
rule に反する parallel detector を作り、undeclared bound、default interval、no event mode、
session-scoped nohup process の二度の unnoticed death を含む supervision を運用し、seven findings が
mis-filed になったと記録されています。これは incident の記録であり、substantive B-1 question の
解決ではありません。

### GitHub quota を named な last-net blind spot として扱う（G673 — preview-through-1.x）

periodic な `automation stalled-work` check は引き続き last net であり、wake / supervision
class は変更しません。GitHub API quota failure は healthy な empty result ではありません。
影響した surface は `cause: github-api-quota-exhausted`、exhausted な `resource`、その
`reset` / `reset_at` を出力します。`automation heartbeat` と `stalled-work` は
`detection_available: false` を返し、`stalled-work` は local-only findings を
`partial: true` で保持します。reset を記録し、wait するかどうかは orchestration が
意図的に判断します。この slice は automatic retry、sleep、reset scheduling、request budgeting、
transport migration、cache を追加しません。

これは attribution を分けた measured incident record です。issue #1442 は remote-herdr の
measurement（`graphql.remaining == 0`、5,046 requests/hour）であり、この host が同日 G667
publish cycle で GraphQL refusal（REST core は 4999/5000）を観測したことは host corroboration
です。#1442 の再帰属ではありません。

### GraphQL remainder を明示した REST read (G674 — preview-through-1.x)

#1442 の surface inventory は issue-list read の正確な field mapping を記録します。
これは `core` 上の `GET /repos/{owner}/{repo}/issues` で、`number`、`title`、
`html_url`、`created_at`、`body`、`updated_at`、`labels[].name`、`state` を既存の
candidate shape に対応させます。`pull_request` marker は adapter の filter だけに使います。
PR read は caller が `closingIssuesReferences` を消費する箇所では引き続き
`graphql-bound` です。stalled-work とその heartbeat も、check-runs だけでは
field-complete ではない GraphQL の `CheckRun`/`StatusContext` `statusCheckRollup`
remainder を保持します。

degraded state がその read によって発生したとき、`dependency` は `rest-core` または
`graphql-bound` を示し、`unverified_fields` は migration を妨げた field を示します。
これは transport/quota の attribution だけであり、wake / supervision semantics、caller
output、認証、mutation path は変わりません。この preview slice に cache、batch、budget、
retry、sleep、reset scheduler はありません。

design wake の有効な outcome は 4 種類だけです。canonical workflow を進める、新しい実進捗の
evidence を確認する、次の actionable な design / packet / issue candidate を見つけて provenance
付きで orchestration に渡す、人間だけが解決できる blocker を報告する、のいずれかです。
unfinished project では `no-actionable`、`running=true`、liveness、unchanged status、`no change`
は outcome ではありません。report は変化の evidence を示します。人間の action が必要なら、
最小の具体的 operation と自動化できない理由を示します。

provenance state は candidate、accepted design、packet、queued unit、published unit、WIP を区別します。
canonical host state に存在する前に execution-unit number を使いません。external handoff を優先する前に、
source kind、reference、timestamp、requesting party、acceptance state を記録します。read-only inspection
に approval は不要です。operator が merge-only と明示しない限り、merge instruction は merge、merge
commit 検証、linked issue close、queue transition、runs append、host state write-back、host state push からなる
完全な closeout transaction を 1 回で許可します。piecemeal な approval は求めません。publication、contract、
priority、release の変更には引き続き明示的 acceptance が必要です。

GitHub `reviewDecision` だけでは blocker を証明できません。intent-cli workflow label、exact PR head、
GitHub check、GitHub mergeability、canonical queue state を source ごとに帰属させて比較します。
delegation verification は canonical workflow status、recorded session-layer agent state と G652 activity
sub-verdict、file / commit / pull request という実 artifact の 3 layer です。`running=true` だけでは
progress を証明できず、terminal content を workflow evidence として `parse` しません。

team formula は judgment を担う 4 thread と 1 supervision process です。watcher infrastructure は
第 5 role ではなく、conversation、judgment、model token を持ちません。supervision は design
conversation の外で動き、design wake あたり最大 1 回だけ参照します。すべての stall class（review
wedge を含む）の detection、classification、authorized recovery は orchestration が所有します。
design は event-driven で、guide が示す escalation set だけを受け取ります。残る duty は、最後に完了した
supervision-cycle record の age を declared supervision-liveness bound と低頻度で比較することであり、
conversation の heartbeat ではありません。detection bound は wake interval と scheduling jitter の和より
大きくします。

inbound app monitor を持たない agent kind の design seat は routing root を cwd とする recorded resident
herdr seat にし、persistent AGENTS rule を適用します。inbound app monitor を持つ kind は external reader
を利用できます。これは recommendation ではなく deployment rule であり、stall recovery を design に
移したり、model-backed monitoring role を追加したりしません。

## host state の誠実さと不足のない scaffold (G661 — 1.x を通じた preview)

host 側の 5 つの境界には「surface は evidence より強い完了を主張しない」という同じ規則を
適用します。`automation knowledge-writeback-record --write` はローカル record を作りますが、
その path をコミットしてプッシュするまで別 checkout からは観測できないことを常に表示します。
intent-cli 自身が自動でコミットすることはありません。このため `automation stalled-work` は record の不在
(`knowledge-writeback-pending`)と、ローカルだけに記録済みの path
(`knowledge-writeback-recorded-uncommitted`)を区別し、commit と push が必要な正確な path を示します。

reactivation の経路は `packet retire --reactivate --evidence <text> --write` だけです。evidence を
必須とし、`lifecycle: ready` と変更前の lifecycle、evidence、timestamp を書き、
`packet-reactivated` を追記します。closeout はこの transition を推測しません。出荷済みの work に
non-publishable な lifecycle が残っていれば `shipped-while-retired-contradiction` を出し、sidecar は
変更しません。

issue-cut readiness は既存の publish validator の判定をそのまま使います。placeholder だけの
Related Links を含む TODO scaffold は validator の理由付きで not ready と表示され、
`issue-cut-ready` として提示されません。`packet draft` の guide reachability も、作者が次の accepted
form のどちらかを選ぶまで comment のままです。declaration 欠落 warning は両 fragment をそのまま表示します。

```yaml
guide_reachability:
  no_role_facing_surface: false
  routes:
    - guide_surface: guide workflow task implementation-loop
      role: implementation
      target_surface: <role-facing-surface>
```

```yaml
guide_reachability:
  no_role_facing_surface: true
  routes: []
```

新規 host では `intent init --write` が既存内容を保持したまま、次の repository default を追記します。

```gitattributes
.intent-cli/runs.jsonl merge=union
.intent-cli/**/*.jsonl merge=union
```

```gitignore
.intent-cli/supervision/**/cycles.jsonl
.intent-cli/supervision/**/stalls.jsonl
```

前者は append-only JSONL store に union merge を適用し、後者は team ごとの supervision telemetry を
git の対象外にします。初期化済みの host に対して `intent init` はこの正確な行を guidance として
表示するだけで、`.gitattributes` と `.gitignore` を変更しません。migration は常に operator が明示的に
行います。

## canonical notify workflow

role 間の workflow message はすべて `intent-cli notify` を使い、agent 自身は
agmsg/herdr の配信方法を選択・直接実行しません。CLI が team に記録された session-layer
mode（未記録時は `agmsg`）を内部で解決し、送信前に logical role を検証するため、team が
transport を切り替えても command shape は変わりません。

```bash
# 1 件の bounded task を委譲。--input / --expected-artifact は必要に応じて反復する。
intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> \
  --to <receiver-role> --report-to <orchestrator-role> --task-id <task-id> \
  --objective <one-bounded-outcome> --input <canonical-reference> \
  --expected-artifact <inspectable-artifact> --result-nonce <fresh-nonce> \
  --write --format json

# receiver の final step（delegate payload がこの command を供給する）。
intent-cli notify report --domain <domain> --team <team> --from <receiver-role> \
  --to <orchestrator-role> --task-id <task-id> \
  --status completed|blocked|question --artifact <artifact> \
  --summary <one-line-summary> --write --format json

# design decision を既存 events.jsonl boundary へ送る。
intent-cli notify escalate --domain <domain> --team <team> --from <sender-role> \
  --task-id <task-id> --artifact <decision-input> \
  --summary <one-line-summary> --write --format json
```

```bash
# supervision / re-dispatch をせずに 1 件の委譲を確認する。
intent-cli notify status --task-id <task-id> [--domain <domain> --team <team>] \
  [--routing-root <host-root>] --format json

# outcome が別の場所で適用済みになった open delegation を明示的に settle する。
intent-cli notify dispose --domain <domain> --team <team> \
  --task-id <task-id> --kind superseded|applied-elsewhere --actor <actor> \
  --reason <reason> [--superseding-task-id <task-id>] \
  [--applied-outcome-evidence <evidence>] --write --format json
```

delivery は recipient の recorded residency から 1 度だけ判定し、同じ judgment を
`notify status`、`notify escalate`、`notify supervise` が利用します。

| recorded residency | delivery contract | output basis |
| --- | --- | --- |
| recorded reader を持つ `external` | reader への永続追記が delivery であり、pane wake は適用しません。 | `recorded-reader-append` |
| recorded pane を持つ `herdr` | recorded pane の wake が delivery に必要であり、event の append だけでは delivery になりません。 | `recorded-pane-wake` |

したがって external-reader への escalation の append が成功すれば `delivered: true` を返し、
supervision は `undelivered-escalation` を開きません。次の cycle では対応する open false-positive
record を 1 回だけ解消し、6-field event history は書き換えません。reader append が失敗した場合は
`delivered: false` のまま、書けなかった reader の外側に genuine append-failure finding を保持するため、
移行済みの false positive として解消しません。pane delivery と G641/G657 の wake / escalation
ladder は変わりません。

> **1.x を通じた preview (G660)。** shared residency-resolved judgment、`delivery_basis` output、
> append-failure finding、false-positive reconciliation は freeze 後の preview behavior であり、
> 1.0 compatibility promise の対象外です。

`notify delegate --write` は最初に team-scoped な pending snapshot を
`<routing-root>/.intent-cli/notify/<domain>/<team>/pending.jsonl` へ追記します。
snapshot は task id、recorded recipient identity、expected artifact、dispatch timestamp を
持ちます。write に失敗した場合は pane prompt や external reader event を試みません。
一致する `notify report` は resolution を追記します。open な pending delegation に一致しない
task id でも report は配信し、human-readable と machine output の両方に supplied id と
「open な pending delegation に一致しなかった」ことを示す advisory を出します。この場合は
pending record を作成も解決もしません。既に settled の task id、または team store 間で
identifier が衝突する lookup は従来どおり拒否し、supplied id と known open ids を示します。
`notify status` は delegate と同じ
recorded identity / liveness judgment を読みます。herdr では recorded workspace/pane の
正確な `agent_running` flag を使い、status string は使いません。agmsg では recorded roster を
使います。report がなく recipient が running なら `live`、一致する report 後は現在の liveness
によらず `settled`、recipient が not running、report がなく、記録された pane に foreground
process の corroboration がない場合だけ `lost` です。herdr の registration が失われても
process が残っている場合は `registration-lost-process-present` という別の state を返し、
process が消えたとは推測しません。経過時間だけで verdict を変えることはありません。

report がなくても outcome が確定している open delegation は、`notify dispose --write` で
明示的な disposition を記録できます。この command は actor、timestamp、reason と disposition kind を要求します。
`superseded` には superseding task id、`applied-elsewhere` には適用済み outcome の evidence が必要です。
`notify status` は `settled` と `settlement_basis: disposition` を返し、report による settled と区別して
disposition を表示します。disposed record は `notify supervise` と `stalled-work` の open 集計に入りません。
automatic disposition はなく、経過時間だけで状態は終了しません。unknown または既に settled の task id は
拒否し、supplied id と state を示します。

disposed task に遅れて届いた `notify report` も通常の message-carriage rule で配信します。disposition を
消去したり record を再オープンしたりせず、status と report advisory は late-report disagreement を明示します。
message を黙らせず、2 つの outcome を operator が reconcile できるようにします。

**活動証拠 (G652 — 1.x を通じた preview)。** 実行中 process は作業の証拠ではありません。herdr では status が `agent_status`、`state_change_seq`、最後の状態変更時刻も示します。`working` には working agent と進行する活動が必要です。sequence baseline がまだない場合、status は `live-idle` を断定せず `activity-unknown` を返します。dispatch 後の状態変更時刻は cold-start の `working` の十分な証拠です。supervision は最初の baseline を live-idle finding なしで記録し、その後に変化しない live-idle recipient に report がなければ一度だけ表示して terminal の確認を remedy として示します。この finding のために terminal content を読んだり recovery に入ったりしません。declared bound が configured interval より小さい場合は、supervise start と各 cycle で structural false alarm warning を出し、CLI が黙って補正する値ではありません。

**report outbox (G653 — 1.x を通じた preview)。** `notify report --write` は transport を試す前に sender-side outbox entry を保存します。entry は task id、result nonce、status、artifact、summary、delivery timestamp を保持し、delivery failure は完了した作業を失わず `undelivered` として残ります。supervision は entry と `notify collect` の remedy を表示するだけで自動送信しません。recipient-side terminal の `ORCH_RESULT` は人間向けの record のままで、intent-cli は terminal を `parse` しません。visible result に arrived report がないときは、recipient を `re-delegate` したり task を `redo` したりせず persisted outbox entry を `collect` します。collection は同じ task id の original report だけを一度送信し、already-delivered entry は拒否します。entry は dispatch generation（result nonce）単位なので、再委譲された task id も新しい report を運べ、unmatched report も message として継続します。undelivered の current generation に対する二回目の report は fail-closed で拒否し、正確な `notify collect` recovery command を示します。

新しい委譲を開始する前に、`notify delegate --write` は undelivered report entry がある task を拒否し、supervision finding と同じ `notify collect` command を示します。これにより finding の対象と collect できる entry は一致します。また、report が settled になった task id/result nonce の組も作業開始前に拒否し、fresh `--result-nonce` または新しい task id を要求します。outbox がない open generation は idempotent に再送できます。

report は bookkeeping entry ではなく message です。fail-closed の保護は message を運ぶことではなく
pending state の mutation に置きます。認識されない identifier を理由に配信を拒否すると、recipient が
依頼したことを知らなかった unsolicited report や escalation への回答という、情報を運ぶ message を
黙らせてしまいます。

> **1.x を通じた preview (G629/G671)。** pending-delegation record、explicit disposition、
> `notify status` は v0.12.0 freeze 後に追加されました。1.0 compatibility promise の対象外であり、
> 1.x の間に変更・撤回できます。後続 MAJOR release でのみ正式化します。[compatibility ledger]
> (1.0-compatibility-ledger.md) の preview row を参照してください。

### role-scoped closeout evidence (G698)

orchestration は mechanical closeout の evidence を `--role orchestration` で記録し、design は intent-tree の
lesson と guide update を `--role design` で記録します。

```text
intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role design --write
intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role orchestration --write
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --role design --write
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --role orchestration --write
```

`automation stalled-work --role design|orchestration` は一つの role の debt だけを検証します。different role は
`records/<role>.json` の下で併存し、既存の legacy `record.json` は unattributed として readable のままです。
自動 migration はありません。

### guide reachability (G645 — preview-through-1.x)

keyword-to-guide standard は workflow の一部です。thread に keyword を渡せば、その thread は named guide に
到達し、surface を理解して action できなければなりません。packet は role-facing な追加ごとに
guide_surface / role / target_surface を宣言するか、no_role_facing_surface を明示します。declaration の
欠落を no-surface と解釈せず、process は route を推測せず guide wording も判定しません。

closeout では design が host update を
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --write
で記録します。記録されるまで automation stalled-work は execution unit、guide、role を含む
guide-reachability-pending を出します。この debt は merge/closeout を阻害せず、explicit no-surface は
silent です。これは 1.0 promise の対象外の preview surface です。compatibility ledger を参照してください。

### scoped adjudication 権限、live CAS、recipe drift（G666/G682/G683/G689/G690 — preview-through-1.x）

unattended approval model は正確に 3 層です。第 1 に agent kind ごとの recorded
recipe の G636 fields に agent-side allow configuration を記録し、既知 dialog を除去します。
第 2 に G683 supervision は dialog-blocked pane の bottom detection snapshot を読み、kind ごとの
recipe entry にある ordered literal fragment が current trailing dialog を構成する場合だけ agent kind、
pane、observed text、stable class を出力します。古い known dialog の後に新しい未分類 text がある場合は
class `unknown` で、曖昧な分類を行わず escalate-only です。
第 3 に検証済みの recorded pre-approve が正確に一致した場合だけ、canonical adjudication surface に
rule、observed dialog、recipe の exact answer scope を渡します。orchestration は authorization と
execution-pending transition を実行前に永続記録し、その recorded key sequence だけを実行して
terminal outcome を記録します。未解決の pending transition は answer retry を抑止し、
reconciliation-required になります。

G690 は「design は決して回答しない」という絶対的な shortcut を、宣言された権限境界に
置き換えます。prompt class と一致した shell scope は `answerable_by` を持ち、実効 capability は
両者の intersection です。design actor が使えるのは、exact class が design-answerable と宣言され、
scope が一致し、hard risk floor tag がなく、記録した pane、state-change sequence、observed-text の
SHA-256 が live dialog と一致する場合に限る `intent-cli notify adjudicate` だけです。command は
bounded な `herdr agent send-keys` の直前に CAS を再読し、pane / sequence / text の mutation があれば
回答を拒否して `stale-dialog-cas-refused` を記録します。decision actor と mechanical executor は
監査上の別 field です。direct relay、direct `send-keys`、fuzzy classification、unscoped forwarding は
design の権限経路ではありません。この slice では shipped class / scope に design-answerable なものを
追加しません。将来の明示的な class のための capability boundary だけを実装します。hard floor は
`destructive`、`credential`、`permission-change`、`security`、`product-decision`、`unverifiable` で、
常に escalation です。

standing supervisor に repeatable な
`--pre-approve <agent-kind>:<prompt-class>` と
`--pre-escalate <agent-kind>:<prompt-class>` を `--write` とともに指定して policy を
記録します。2 つの list は同時に宣言します。各 pair は reviewable recipe vocabulary に対して
検証され、unknown pair は拒否されて既知の `kind:class` 値がすべて表示されます。同じ pair を
両方の list に記録することは拒否され、legacy conflicting record では escalation が fail-closed で
優先されます。
producer-covered kind は G682 の inapplicability を自動的に解消し、uncovered kind は
`inapplicable-no-prompt-class-producer` を維持します。matched pre-escalate、unmatched、
`unknown` はすべて escalate-only として監査記録され、answer を実行しません。最初の vocabulary は
`codex:github-comment-post`、別途記録済みの codex launch-hook trust dialog、G636 launch recipe の
`copilot:launch-limited-permissions` です。

G689 はこの vocabulary に measured な `codex:shell-command` class を追加します。
class producer は dialog を認識して command payload を抽出しますが、payload を answerable にはしません。
shell policy instance は `--shell-policy <json>` で記録し、shipped inventory は
`intent-cli prompt-class list` または `intent-cli prompt-class describe codex:shell-command` で読み取ります。
shell answer が有効なのは、compound shell-AST のすべての segment が scoped policy に覆われ、監査に
matched scope が記録される場合だけです。`project-test` は recorded `dotnet test` argv prefix を
recorded cwd/root/path constraint に束縛し、read-only ではなく test execution です。
`owned-scratch-delete` は同じ wake の scratch ledger にある exact path だけを要求し、bare `/tmp` を
認可しません。`exact-command-once` は normalized AST digest と current dialog hash を束ね、bounded answer
1 回の後に消費されます。unknown syntax、command substitution、redirect、uncovered または root 外の
segment はエスカレーション対象です。persistent allowance は operator-only のままです。G689 の shipped
shell scope（`owned-scratch-delete` を含む）は orchestration-only で、同じ wake の scratch-ledger identity
要件も維持します。G690 はこの contract を弱めません。将来 design-answerable な class が追加されても、
canonical adjudication surface と live CAS が必須です。

各 supervision cycle は running recorded seat の structured argv と kind ごとの recorded
recipe も比較します。比較対象は security envelope、すなわち sandbox mode、approval mode、
writable roots/add-dirs、network access だけです。bound の欠落、extra root、より広い envelope は
`recipe-envelope-alarming`、より狭い envelope は informational な
`recipe-envelope-narrower` です。model と reasoning effort は operator-choice の wish field として
設計上比較から除外されるため、wish だけの差分は無出力です。argument order と whitespace は
同値です。mismatch は observed shape と recorded shape の両方を示す `recipe-drift` finding を
seat ごと、cycle ごとに 1 回出し、conforming seat では無出力です。finding は誰にも通知せず、
watcher は seat の restart/correction を行いません。input 操作は
一致する policy、永続的な実行前監査、orchestration-only 通知の後に registry の exact bounded
`agent send-keys` sequence を実行する場合だけです。generic keystroke relay と design answer path は
ありません。prompt の監査記録は既存の `cycles.jsonl` stream を共有し、seat、pane、class、rule、actor、
timestamp、exact scope、outcome を記録します。

G686 は G684 の比較 algorithm を変えず、operator-owned な第 2 の truth layer を追加します。
confirmation と digest の検査がある専用 command
`intent-cli session-layer topology record-profile` で、team topology に named typed envelope profile を
記録します。profile は kind、sandbox mode、approval mode、roots policy と concrete writable roots、
network access、transport mode、evidence、`recorded_at`（optional digest、Copilot permission options、
network URLs を含められます）を持ちます。この command は `update-kind` / `update-field` とは別であり、
generic な topology JSON 編集は profile 記録経路ではありません。role は named profile を reference するか、
typed override を持てます。profile がある role ではそれを comparator baseline とし、profile がない場合の
G684 registry behavior は byte-identical です。missing profile、dangling reference、kind mismatch の
reference/override は distinct な `profile-invalid` finding とし、registry へ暗黙に戻りません。
profile は記録済み fact だけであり、supervision は observed argv から学習せず、seat の launch/repair も
行いません。G684 の cadence、wish field 除外、no-action boundary は不変です。profile command と finding は
G628 の preview-through-1.x です。

これにより **judgment を担う 4 thread と 1 supervision process** を維持し、第 5 の
approval seat を追加しません。2026-08-11 の workspace wK では Claude app safety が
design-thread keystroke relay を正しく拒み、存在しない `/approvals` surface の助言でも
復旧できませんでした。別の attribution として、operator-filed #1469 は 0.19.0 cycle の
47 keys に prompt/dialog/class/adjudication producer key がないこと、review seat が 1 日に
3 回停止したこと、orchestration が class 捏造を正しく拒否したことを実測しました。これは
#1465 の configured-looking-but-inert failure shape と共通です。interim の in-contract remedy は
上記 recipe layer の agent-side allow configuration を G636 fields に記録することであり、answer
path ではありません。同じ contract は `intent-cli guide orchestrator-thread` と
`intent-cli guide design-thread` の両方で、Markdown/JSON、team の有無を問わず表示します。

### bounded な recipient supervision (G630)

```bash
intent-cli notify supervise --domain <domain> --team <team> \
  [--interval <seconds>] [--auto-redispatch] [--once] [--dry-run|--write] \
  [--routing-root <host-root>] --format json
```

`notify supervise` は open pending delegation を対象に bounded な wake を繰り返します。
recipient が `live` の場合と record が settled の場合は、意図的に action も output も出しません。
`lost` の場合だけ、次の順序で recovery を実行します。recorded pane が mid-exit ではなく本当に
消えたことを確認し、foreground process が記録された role の cwd に属することを検証してから
旧 process が消えたことを再確認し、記録された unattended 起動レシピで `start` し、register
用 prompt を送り、response line に nonce が完全一致することを検証します。pane 内のどこかに
nonce があるだけの場合（composer に未送信の echo が残る場合を含む）は readiness の証拠にしません。

この gate を通過した後だけ、supervisor は delegating role へ 1 件の loss notification を送ります。
そこには task id、recipient が recovered であること、in-flight task は lost なので再 dispatch が
必要であることを含めます。`--auto-redispatch` の既定値は off です。指定した場合だけ readiness
確認後に通常の `notify delegate` 経路で再送し、その結果も通知に含めます。recipe が不明、process
の帰属が曖昧、旧 process が残存、response line の証拠が得られない場合は、その recipient の
処理を fail-closed で停止し、未確認の readiness を報告しません。`--once` は bounded な診断 pass
に使い、`--dry-run` は kill、start、prompt、再 dispatch、追記を一切行いません。

> **1.x を通じた preview (G630)。** supervision、recovery、response-line による readiness の証拠、
> 任意の再 dispatch は v0.12.0 freeze 後に追加されました。1.0 compatibility promise の対象外であり、
> 1.x の間に変更・撤回できます。後続 MAJOR release でのみ正式化します。[compatibility ledger]
> (1.0-compatibility-ledger.md) の preview row を参照してください。

### measured recovery supervision (G641 — preview-through-1.x)

```bash
intent-cli notify supervise --domain <domain> --team <team> \
  --repo <owner/repo> --owner-role <logical-role> --bound <seconds> \
  [--interval <seconds>] [--once] [--dry-run|--write] \
  [--routing-root <host-root>] --format json
```

同じ bounded invocation が、既存の recipient-lost、terminal CI wait、queue/write-back、recorded
seat の inventory をまとめて消費します。healthy team では finding を出しません。condition を検出したら
team に記録された transport で owner の logical role を起こします。supervisor は transport を発明せず、
agent kind を仮定せず、owed transition を適用しません。`--owner-role` は recipient 以外の finding の owner
を指定し、G630 の recipient recovery は記録済みの delegating role を owner として使います。

`--bound` は team の最大 detection interval を
`.intent-cli/supervision/<domain>/<team>/bound.json` に記録します。各 cycle は `cycles.jsonl` に永続的な記録として残り、直前 cycle からの実測 gap と bound を満たしたかを含みます。restart 後の gap が bound を超えた場合は
`supervisor-not-running` と `absent_since_last_cycle: true` を報告します。直前 cycle がない場合は healthy と推測せず
unknown とします。

`--bound` を省略した場合、declared bound は推測も記録もせず、cycle に保存した `interval_seconds` を loop の実測 cadence
として扱います。fallback の self-absence threshold は `max(2 * cadence, cadence + 60s)` とし、通常の cycle 作業時間や
scheduler の揺らぎに headroom を持たせます。この場合 `bound_met` は null のままで、liveness summary も detection bound が
未宣言であることを示します。`started_at` は cycle 開始時刻、`completed_at` は実際の完了時刻であり、`gap_seconds` は直前の
完了から現在の開始までを測るため、通常の cycle 作業を downtime と誤認しません。

各 finding は `stalls.jsonl` に append-only recovery record として残り、`detectable_at`、`surfaced_at`、
`cleared_at` を持ちます。supervision restart 後に初めて見つかった condition は `detectable_at: null` と
`detectable_at_unknown: true` になり、都合のよい duration を作りません。既知の start を持つ record の clear は
実測 duration を記録します。pane-resident または append-failure の `notify escalate` delivery も finding として扱い、bounded `--once` result には
loop 自身の liveness を返します。dry-run は同じ class と bound を解決して確認しますが、wake、record、clear はしません。
undelivered escalation の wake が Delivered になった後は recovery record を acknowledgement として消去し、append-only
event 自体は残したまま、その key を後続 cycle の finding から除外して healthy silence に戻します。

> **1.x を通じた preview (G641)。** measured supervision、bound と永続 recovery record、undelivered-escalation
> finding、self-liveness は post-freeze の preview であり、1.0 compatibility promise の対象外です。1.x の間に変更・
> 撤回でき、後続 MAJOR release でのみ正式化します。[compatibility ledger](1.0-compatibility-ledger.md) を参照してください。

### supervisor の永続 setup (G658 — preview-through-1.x)

standing loop の setup は ad-hoc な background shell ではなく出力専用の installer を使います。

```bash
intent-cli notify supervise install --domain <domain> --team <team> \
  --repo <owner/repo> --owner-role <logical-role> --bound <seconds> \
  --interval <seconds> [--platform macos|windows|linux] \
  [--output <path>] [--routing-root <host-root>] --write --format json
```

`--platform` を省略すると current platform の scheduler definition、すなわち macOS の launchd plist、
Windows の `schtasks` compatible Task Scheduler XML、または Linux の systemd user unit を生成します。
`--platform` は明示的な cross-authoring override です。すべての artifact は
`intent-cli.supervise.<domain>.<team>` という team 固有の名前と label を持ち、domain、team、repo、
owner role、bound、interval を含む完全な `notify supervise` invocation を埋め込みます。output は write 先と
registration / unregistration の正確な command を表示します。これらは operator action であり、intent-cli は
実行しません。supervisor の register、unregister、start、stop、強制終了は一切行いません。macOS から生成した
Windows / Linux artifact は `emitted-but-unverified` と明示します。

agent seat の外に team ごと 1 つだけ artifact を配置します。loop、cycle measurement、detection bound、
finding ごとの 1 wake、escalation ownership は既存の G630/G641/G657 semantics のままです。この配置によって
CLI の lifecycle 権限や recovery 権限は増えません。

team ごとの canonical liveness check は、その team の
`.intent-cli/supervision/<domain>/<team>/cycles.jsonl` record の age を declared bound と比較することです。
process-name grep は team identity を証明できないアンチパターンです。2026-08-08 の実測では process-name grep が
team を混同し、design team 自身の supervisor を強制終了し、別 team の process を残しました。この absence は約 47 時間
検知されず、永続 record が `absent_since_last_cycle=true`、`gap_seconds=169796`、bound failure を示して初めて
判明しました。supervisor health は共有 process name ではなく record identity と age で判定します。

> **1.x を通じた preview (G658)。** scheduler artifact emission と output schema は post-freeze preview で、
> 1.0 compatibility promise の対象外です。生成は registration や runtime verification ではなく、この surface は
> release decision を行いません。

### event-driven supervision (G659 — preview-through-1.x)

1 つの standing `notify supervise` invocation に `--event-mode` を追加して有効化します。
同じ supervisor process が recorded seat ごとに blocking `herdr agent wait` を 1 つ保持し、
implementation または review seat が `working` から `done`、`blocked`、`idle` へ変わると
数秒以内に owner role を起こします。transition、source、`state_change_seq`、observed latency、
wake result は `cycles.jsonl` に記録します。wait の death / error は `event-wait` と
`rearm_attempted: true` を記録してから再設定します。terminal output は解析しません。

event wait と interval cycle は 1 process 内の独立した observation source です。wait が死んでも
interval loop は safety floor として残ります。両 source は同じ workspace / pane / sequence の
transition key を使うため、同時に観測しても 1 transition / 1 wake です。既存の wake target、stall class、
owner から design への escalation、recovery の権限、team ごと exactly one supervisor の rule は
変わらず、event mode は第 2 の standing loop を作りません。実測 evidence は macOS 上の
`herdr 0.8.0` です。他の herdr version と platform は unverified です。
concrete な wake-source flag は `--event-mode` で、対応する herdr observation は
`pane.agent_status_changed`（normative SECOND wake source）です。

G659 が hand-written transition watcher に取って代わるのは operator が event mode を採用
した場合だけです。intent-cli は watcher の探索、強制終了、強制置換を行いません。G658 scheduler artifact は
invocation を埋め込むため、既存の interval-only artifact は interval-only のままです。adoption には
`notify supervise install ... --event-mode` を再実行し、新しい artifact を確認して、表示された
operator command で明示的に登録解除 / 再登録する必要があります。

> **1.x を通じた preview (G659)。** event wait、transition / wait record、event / interval de-dup は
> post-freeze preview です。release decision は行わず、1.x の間に変更・撤回できます。

### scheduler の自己完結性と transport degradation の正直な分類 (G675 — preview-through-1.x)

`notify supervise install` は scheduler artifact の emission 時に
`intent-cli` executable を絶対パスへ解決します。loop が使う runtime transport
binary も解決できる場合は絶対 executable として埋め込み、解決できない
binary は emission result に名前を出し、残る command name を覆う記録済み
PATH も artifact に残します。この surface は emit-only のままであり、
intent-cli は scheduler job の register、start、stop、replace、manage を
行いません。

operator は live PID と最初の cycle record である `cycles.jsonl` の両方を
確認します。loaded PID だけでは loop が生きている証拠になりません。
guidance は loaded-but-silently-exiting / exit-127 という形を明示します。
G675 の実測は 2026-08-12、host macOS node 08 における別 attribution の
二つの act です。act one は launchd の minimal PATH で
`/usr/bin/env intent-cli` が loaded だが silently exiting、exit 127 になる
ことを確認し、同じ machine の audit は accumulated supervisor が 4 件ある
ことを確認しました。act two は `herdr` 欠落により、recipient が alive で
mid-task のままなのに 1 cycle で false recipient loss が 10 件出る形を確認
しました。これは recipient absence ではなく scheduler / transport の事実です。

transport process を start できない場合は recipient liveness を一度も判定
する前に、cycle-level の `supervision-degraded` finding 一件として分類します。
cause は `transport-unavailable` とし、binary と start error を含めます。
`recipient-lost` にはせず、open delegation ごとに繰り返しません。healthy
recipient は従来どおり silent であり、G648 の genuine absence と
foreground-process corroboration rule は byte/semantic とも変更しません。

optional な第 2 wake source の具体的な flag は `--event-mode` です。
`pane.agent_status_changed` のための blocking herdr wait を保持しますが、
interval cycle が safety floor として残ります。G675 は duplicate-supervisor
detection、recovery sequence、新しい verdict name、wake transport、emission
target を追加しません。

> **1.x を通じた preview (G675)。** scheduler executable resolution、記録済み
> PATH diagnostics、cycle-level transport degradation、rendered verification
> guidance は post-freeze preview であり、1.0 compatibility promise の対象外です。
> 1.x の間に変更または撤回できます。

### duplicate supervisor の検出 (G676 — preview-through-1.x)

新しい supervision cycle は `cycles.jsonl` に nullable な `writer` object を
追加し、`pid`、`process_start_time`、`host` を記録します。reader はその object を
持たない legacy cycle も読み続けます。legacy record は duplicate の証拠にはなりません。
cycle 開始時、loop は直近の cycle と自分の writer identity を比較します。別の writer
であり、その process が live で、cycle age が同じ declared bound または cadence-based
の liveness 用 recent threshold 内にある場合だけ duplicate とします。このとき cycle
ごとに exactly one の `duplicate-supervisor` finding を出力し、current / other の
両 writer、other cycle の age、同じ stall への duplicate wake cost、そして remedy として
G658 の team ごとの scheduler label `intent-cli.supervise.<domain>.<team>` を明示します。

dead writer、stale cycle、同じ writer、legacy cycle では duplicate finding を出しません。
これは detection のみです。intent-cli は supervisor を終了、停止、順位付け、選出、ロック、
リースせず、duplicate seat process もこの slice の範囲外です。scheduler artifact を
register する前に、operator は session restart 後も残った stale hand-run supervisor を
確認して停止しますが、intent-cli はその停止を実行しません。G676 の実測 incident は
この machine の 2026-08-12 に、1 team に 4 concurrent loops が存在し、同じ stall に
duplicate wake が出ることを確認しました。この attribution は team ごとの G658 artifact
を remedy とする理由であり、supervision に新しい recovery 権限を与えるものでは
ありません。

> **1.x を通じた preview (G676)。** additive writer identity、duplicate-supervisor
> detection、stale hand-run cleanup guidance は post-freeze preview であり、1.0
> compatibility promise の対象外です。1.x の間に変更または撤回でき、後続 MAJOR でのみ
> 正式化します。

### escalation ladder と CI fallback (G657 — preview-through-1.x)

完全な ladder は次のとおりです。各 seat は割り当てられた作業を行い、orchestration は通常の stall を検知して
既存の権限内だけで recovery を行います。measured supervision は担当範囲の working seat を orchestration も含めて
監視します。design は operator の rung であり、supervision の監視対象ではありません。finding の `subject_role` が configured owner role の場合だけ narrow fallback を使い、design の
recorded pane または event-reader transport に `wake_class: escalation` の wake を 1 件だけ送ります。それ以外の
subject は通常どおり `owner_role` を起こします。同じ cycle で両方の rung へ一斉送信しません。
永続 finding は provenance として `subject_role`、`wake_target_role`、`wake_class` を記録します。

design は escalation を裁定しますが、recovery の権限は受け取りません。supervision liveness に対する最後の
resort であり、supervision が design を監視または recover することもありません。health evidence、G630 recovery
sequence、delivery、settlement、lifecycle transition の ownership は変更しません。

terminal CI も action の形で分類します。exact-head の `ci-wait` が failed で、その head が current のまま、repair
の担当表明がない場合だけ actionable です。repair/update label または new head は通常の repair が進行している
証拠なので、supervision は silent のままです。green check には declared state の fallback もあります。
`intent-pr-created` label の issue に紐づく open non-draft PR が all green で review-routing label を持たない場合、
`ci-wait` record がなくても `ci-all-green-not-transitioned` を返します。このとき
`ci_classification_source` は `declared-label-fallback` で、永続 wait はより豊かな `ci-wait-record` path です。

> **preview-through-1.x (G657)。** subject-based owner escalation と settled-CI classification fallback は
> v0.12.0 freeze 後の preview behavior で、1.0 compatibility promise の対象外です。release decision は行いません。

### registration は失われたが process は存在する (G648 — preview-through-1.x)

herdr の registration は process liveness ではありません。`notify status`、delivery、supervision が
recipient を `lost` と判定する前に、CLI は正確な recorded workspace/pane の foreground process を
確認します。registration も process もない場合だけが genuine な `lost` であり、G630 の fail-closed
recovery gate は変わりません。registration がなく process が残る場合は
`registration-lost-process-present` と命名します。liveness は recipient が生きている可能性を示し、
supervision は G630 recovery の外側で cycle ごとに recorded workspace/pane あたり最大 1 件の finding を
返し、delivery は同じ cause と `resend_permitted: true` を返します。対応は recorded pane で agent を
再登録することであり、kill、restart、automatic re-registration は行いません。`pane-absent` や
`agent_not_found` のような prompt 文言が process corroboration を上書きすることもありません。
これは 1.x を通じた preview で、1.0 compatibility promise の対象外です。

`notify delegate` は task id、expected artifact、fresh marker nonce、isolated child
checkout から必要な transport-neutral `--routing-root` を含む完全な canonical report
command を配信 task に埋め込みます。receiver は他のすべての作業後、その report
command を final step として実行するため、herdr-only の完了が receiver pane に表示される
だけで終わらず orchestration role を能動的に呼び起こします。herdr-only mode の source of truth は
`<routing-root>/.intent-cli/topology/<domain>/<team>.json` です。sender、recipient、delegate の
`report-to` はすべてその team の recorded roster に存在する必要がありますが、**only the
recipient must be deliverable** です。このため `resident: external` role は pane なしで sender
および `report-to` になれます。その external role が recipient の場合、`delegate` / `report`
は安全な routing-root-relative の recorded `reader` へ変更されていない 6-field event を正確に
1 件追記して配信します（`delegate` は `question`、`report` は status
`completed|blocked|question` を event kind `completion|blocked|question` へ対応付けします）。
そして `eventAppended: true` を返します。herdr-resident recipient は team の recorded workspace
内にある明示的な recorded pane を target にします。他 workspace にだけ存在する agent は
決して eligible ではありません。

`--dry-run` は `--write` と同じ topology、team-workspace、recipient-state、reader resolution を
実行し、prompt / append の副作用なしで同じ refusal verdict と cause を返します。unknown-role
failure は実際に参照した source、team/workspace scope、その scope で見つかった role、corrective
action を明示します。resolution はすべて fail closed で、foreign workspace や別 transport への
fallback はありません。`notify escalate` は同じ 6-field event schema を引き続き追記します。
いずれも merge / label / publish / queue mutation を行いません。direct transport command は
provisioning/readiness diagnostics に限り、workflow send instruction には使いません。

## orchestrator モードの開始（設計スレッドのセットアップ）

オーケストレーションを動かしたい設計スレッドは intent-cli に直接尋ねられます —
`intent-cli guide workflow suggest --goal "I want to start agmsg orchestrator mode"`
（および `orchestrator を使いたい` / `新しい intent-cli オーケストレーションを使ってみたい`
のような自然言語の言い回し）が orchestrator setup ガイダンスへルーティングします。

`guide orchestrator-thread` はまず **setup intake** をレンダリングし、その可視 outcome は
`missing-inputs` / `setup-ready` / `blocked` のいずれかです:

- **missing-inputs** — domain、target repo、orchestrator/implementation/review フォルダー、
  orchestrator/implementer/reviewer agent、agmsg team 名、delivery mode、existing-loop stop
  policy のうち、不足しているフィールドだけを補う。
- **setup-ready** — intake が貼り付け可能な agmsg `join.sh` / `delivery.sh` コマンドと 3 ロール
  分の最初のプロンプト、最初の検証（existing-loop 競合チェック、read-only first wake、ping/inbox
  テスト）を emit する。
- **blocked** — 同じ domain/repo の既存の実装/レビュー timer loop が orchestrator と競合する。
  開始前に停止する（または `--existing-loop-policy will-stop` を渡す）。receiver は決してスケジュール
  されない — 明示的な fallback/legacy タイマーを使う場合（既定はメッセージ駆動の wake）のみ、
  orchestrator が唯一スケジュールされるスレッドになる。

```bash
intent-cli guide orchestrator-thread --domain <d> --target-repo <owner/repo> \
  --orchestrator-path <o> --implementation-path <i> --review-path <r> \
  --orchestrator-agent <a> --implementer-agent <a> --reviewer-agent <a> \
  --team <team> --delivery-mode <mode> --existing-loop-policy none --format markdown
```

intake の後に、完全なリファレンスチェックリストが続きます:

1. **決定 / 記録** — domain と target repo、host / orchestrator / implementation /
   review のパス（各ロールは自分のフォルダー・クローン・worktree から実行）、base branch
   policy、ロールごとの agent、agmsg team 名、delivery mode。herdr-only では各 seat
   （`design`、`orchestrator`、`implementation`、`review`）でどの CLI と model を使うか human に
   尋ね、各回答をその seat の `kind` として記録します。silent に default を選びません。
2. **ロール登録** — orchestrator・implementation・review を 1 つの agmsg team に登録
   （`join.sh`）。
3. **delivery 設定** — 各ロールがメッセージを受け取れるようにする。例: ストリーミングの
   inbox watch（`delivery.sh` / `watch.sh`）。
4. **ロールプロンプトを貼る** — `guide orchestrator-thread` の orchestrator /
   implementation / review プロンプトを対応するスレッドへコピーする。
5. **最初の read-only wake** — 確認のみの orchestrator wake を 1 回実行し、何も送らない。
6. **ping テスト** — agmsg メッセージを 1 通送り、実際の委譲の前に対象ロールの inbox に
   届くことを確認する。
7. **既定はメッセージ駆動の定常状態** — implementation/review が agmsg で返信し、
   orchestrator を起こすため、通常は高頻度のポーリングは不要。receiver は loopless の
   まま。orchestrator タイマー（Codex automation 5m または Claude `/loop 5m`）は
   明示的な fallback/legacy オプションとしてのみスケジュールする
   （RECOMMENDED なデフォルトのセーフティネットは
   [design-thread watchdog](#design-thread-watchdog推奨されるセーフティネット) を参照）。
8. **クリーンアップ** — 終了時は agmsg スクリプト（`leave.sh` / `despawn.sh`）でロールを
   離脱/終了し、inbox watcher を停止する。

> **警告:** agmsg のデータベースや team ファイルを直接編集しないでください — provision・
> diagnose・cleanup は agmsg script を使い、workflow notification は adapter を呼ぶ
> `intent-cli notify` で送ります。agmsg state の手編集は delivery を壊します。

## ターミナルワークスペースの provisioning（チームを構築する）

上記のセットアップチェックリストは、各ロールが **すでに** 専用フォルダーと稼働中の
ターミナルセッションを持っていることを前提にしています。そうでない場合 — 設計スレッドが
何もない状態から「このチームをセットアップして」と依頼された場合 —
`guide orchestrator-thread` は両方を作る **terminal-workspace provisioning**
セクションをレンダリングします。プレースホルダーだけで実行可能です
（`<Project>`、host メタデータリポジトリ `<owner/host-repo>`、target repo
`<owner/repo>`、agmsg team `<team>`、`<workspace-root>`）。同じコマンドで生成し、
そのチェックリストを上から実行してください。以下の要約はオリエンテーションであり、
代替ではありません。

**1. ロールフォルダー — 無ければ作る。** host 側のロール（orchestrator、review）は
**host メタデータリポジトリ** のクローンから、implementation ロールは **target repo**
のクローンから実行します（implementation は GitHub-contract-only であり、host の
`.intent-cli/` state を読みません）。2 つのロールが 1 つのフォルダーを共有しては
**いけません**: agmsg identity と codex monitor bridge は `(project, type)` スコープ
（G521）であり、同じ型の 2 ロールが 1 フォルダーにいると同一 identity に解決され、
片方が静かに受信を停止します。存在しない cwd で開かれた pane はシェルの既定
ディレクトリにフォールバックし、まさにこの衝突を起こします — 先にフォルダーを作成し、
その後で各フォルダーが別パスであること、`origin` が期待どおりであること、clean である
ことを検証してください。

**2. ワークスペーストポロジー。** team ごとに 1 ワークスペース、team 名を付けた
タブ 1 つ、ロールごとに pane 1 つ（そのロールのフォルダーを cwd として pane 作成時に
設定する — agent 起動後に `cd` しない）。**設計スレッドは** 自分が構築している
ワークスペースの **外に留まります**。

**3. 起動ルール。** すべての agent は pane の **対話シェルにタイプして**
（send-text + enter）起動します。codex では必須です: `codex()` シェル shim が agmsg
monitor bridge を arm する（G521）ため、canonical な実行ファイルを直接 exec する
ワークスペースマネージャーはこれを迂回し、セッションは健全に見えるのにメッセージが
一切配信されません。claude は **オペレーター** が選んだ permission mode で起動します。
各 pane の初回起動には **必ず立ち会って** ください: trust 画面と permission プロンプトは
回答されるまでセッションをブロックします。設計スレッドが回答を認可されている場合、その回答は
次の wake で再プロンプトされる 1 回限りの承認ではなく、**永続する** allowlist を生む必要が
あります。

> **権限境界 — 詰まりを解くことは決定することではない。** pane に立ち会うことは、設計
> スレッドが決定者になることを意味しません。設計スレッドは **実際に読んだ pane の内容に
> 対してのみ** 行動できます（レンダリングしていないダイアログへのブラインド入力は禁止）。
> オペレーターの認可が及ぶのは **読んだ pane の trust/allowlist ケースに限られます** —
> たとえば設計スレッド自身の hook-trust ケースです。credential プロンプト・security
> プロンプト・permission プロンプトを設計スレッドが回答することは **決してありません**:
> 事前認可の有無にかかわらず **常に** 未回答のまま **常に** オペレーターへ
> エスカレーションします — どんな認可もこれらを回答可能にはしません。回答がアクセス
> 付与・permission mode の拡大・security 警告の受諾になるなら、それはオペレーターの
> 判断です。

### 3a. unattended 起動レシピ（agent-neutral）(G617)

unattended 起動レシピは agent-neutral です。起動 invocation、各 role の実際の作業から
導く境界付き許可 root、自律継続の上限、operator が回答する startup gate、そして denial
semantics に加えて、起動後に agent が提示する post-start interaction、宣言した境界を
維持するための回答、default answer が安全かどうかを記録します。command line が正しくても
起動後に agent が権限を交渉できるため、command line で止まるレシピは不完全です。
この host で実測済みの registry には Copilot と Codex の entry があります。未実測の kind（Cursor や
opencode など）は名前だけの placeholder のままにし、推測した flag を追加してはいけません。

> **1.x を通じた preview (G636)。** post-start interaction field は v0.12.0 freeze 後に追加された
> preview surface です。1.0 compatibility promise の対象外であり、1.x の間に変更・撤回される
> 可能性があり、正式化は後続 MAJOR release でのみ行います。詳細は
> [compatibility ledger](1.0-compatibility-ledger.md) の preview row を参照してください。

> **central autopilot supervision rule。** unattended autopilot seat では、launch allowlist
> の外にある action は G550 の supervision dialog として表示されず、静かに自動拒否されます。
> allowlist は role need から導出して **記録** します。READY では、期待される許可済み action、
> その role の canonical reporting surface への到達可能性、out-of-scope action の拒否を証明
> しなければなりません。review evidence は command output と transcript で拒否を調べます。
> liveness は拒否された step が実行された証拠ではありません。これは supervision evidence
> だけを変えるものです。G556 の liveness と notify/delivery semantics は変わりません。

#### Copilot — 実測済みの最初のレシピ

```text
herdr agent start <logical-role> --kind copilot --pane <pane-id> -- --mode autopilot --allow-all-tools --add-dir <role-work-root> [--add-dir <host-routing-root>] --max-autopilot-continues 10
```

- **role-derived root。** 各 role には checkout または worktree 用の境界付き
  `--add-dir <role-work-root>` を 1 つ与えます。reviewer には canonical reporting surface
  である `intent-cli notify report` のため、さらに `--add-dir <host-routing-root>` が必要です。
  developer-machine の無関係な root は追加しません。delegation の前に orchestrator は workspace
  prerequisite をこの記録済み write envelope と比較し、その外側にあるものを orchestrator の権限
  で準備します (G655)。
- **継続上限。** `--max-autopilot-continues 10` を明示したままにします。別の上限は、レシピと
  ともに記録する operator の判断です。
- **inline-payload の advisory。** `copilot-autopilot-observed-paste-risk` profile は
  `inline_payload_warning_chars: 4096` を宣言します。これは advisory にすぎません。これを
  超える payload は type ではなく貼り付けられやすいという目安であり、下回れば安全という保証には
  なりません。実際の限界は terminal と agent に依存します。
- **reference-first の限界。** 繰り返す review の実体は committed canonical な
  `review-context.md` に置き、delegate には短い pointer だけを載せます。ただし、これを paste の
  remedy として扱ってはいけません。最小の canonical `notify delegate` envelope でも 842 文字・14 行で、
  これ自体が paste になります。これは重複を減らす discipline であり、paste-sensitive な wedge を防ぐ
  ものではありません。transport-layer の remedy は G619 が担当します。
- **task-envelope delivery method。** existing record を持つ paste-sensitive な herdr seat では、
  registry-limited topology field update で `delivery_method: file-backed` を宣言します。`notify` は unchanged な envelope を host の
  `.intent-cli/tasks/<domain>/<team>/<task-id>-<nonce>.md` に書いてから、pane には
  `Read task envelope: <path>` という 1 行だけを送ります。明示的に選ぶなら `inline` を宣言します。
  宣言がなければ既存の inline delivery をそのまま維持します。
- **post-start interaction (G636, preview-through-1.x)。** 最初の task で Copilot 1.0.78 は
  `1. Enable all permissions (recommended)` / `2. Continue with limited permissions` /
  `3. Cancel` を表示し、cursor は option 1 にあります。宣言した `--add-dir` 境界を維持するには
  `Continue with limited permissions` を選びます。default の `Enable all permissions` は unsafe です。
  recipe には `default_is_safe: false` と記録し、restart でこれを受け入れるのは shortcut ではなく
  supervision failure です。
- **startup gate。** folder trust と autopilot-enable は operator provisioning gate であり、
  launch flag ではどちらも bypass できません。`--mode autopilot` を launch 時に渡しても、
  autopilot-enable dialog は **最初の task** で現れます。`--allow-all-tools` と境界付き root
  を使う場合は `Continue with limited permissions` を選びます。境界を捨てる
  `Enable all permissions` は選びません。
- **禁止する包括権限。** developer machine の unattended seat では `--yolo` と
  `--allow-all-paths` は **禁止** です。代わりに境界付き `--add-dir` root を使います。

#### Codex — 実測済みの recipe (G647)

```text
herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write --ask-for-approval never --add-dir <role-work-root> [--add-dir <host-routing-root>]
```

- **role-derived root。** role の checkout/worktree には境界付きの
  `--add-dir <role-work-root>` を 1 つ使い、その role の canonical report surface に必要な場合だけ
  host routing root を追加します。delegation の前に orchestrator は workspace prerequisite をこの
  記録済み write envelope と比較し、その外側にあるものを orchestrator の権限で準備します (G655)。
- **実測した bounded invocation。** この invocation は **Codex v0.144.1 / macOS** で実測したものであり、
  未実測環境に対する universal な flag recipe ではありません。
- **実測した自己更新 behavior。** Codex は自己更新して **「Please restart Codex」** を表示し、
  pane の shell へ exit することがあります。記録済み pane で agent を再起動して READY/ping を再実行します。
  これは wedge ではなく restart condition であり、回避のために envelope を広げてはいけません。
- **実測した envelope asymmetry。** 宣言した root の外への write は拒否されましたが、外への read は拒否されませんでした。
  これは明示的な security fact として扱い、read permission の保証と解釈してはいけません。
- **post-start interaction (G636)。** **MyIntentHost** で **2026-08-07** に Codex の
  post-start interaction は観測されていません。したがって structured な
  `post_start_interaction` record は `status: unmeasured`、`observed: false`、prompt/answer/default safety
  を null、そして明示的な absence reason として保持し、Markdown でもその absence を表示します。実測した
  prompt・answer・default safety を推測してはいけません。
- **registry boundary。** `topology update-kind` は変更とともに実測済み target recipe を表示します。recipe が未記録の
  target kind では明示的に absent とし、launch flag を推測しません。recorded kind は human の現在の wish であり、
  human が要求した switch は one step、recovery は unattended に kind を変更しません。

**unattended READY branch。** 通常の G556 liveness check に加え、次の 3 点を証明します:
記録済み root 内の期待される action が成功すること、role が canonical reporting surface
（review なら host routing root 経由の `intent-cli notify report`）へ到達できること、意図的に
out-of-scope にした action が拒否されることです。その拒否を review 用に記録します。live pane
だけ、または許可済み action の成功だけでは **READY ではありません**。denial probe が予想に反して
成功した場合は、まず post-start interaction が default で回答されていないか確認します。unsafe な
default は宣言した境界を捨てます。

**4. ロール初期化。** pane の CLI に合った actas 形式をタイプします — claude は
`/agmsg actas <role>`、codex は `$agmsg actas <role>`。その後 readiness を
**混同してはいけない 3 つのレイヤー** で確認します:

1. **delivery 設定** — `delivery.sh status` がモード（例: `mode=monitor`）を報告することは、
   登録と設定を証明するだけです。watcher が生きていることも、セッションに接続されている
   ことも **証明しません**。`mode=monitor` を報告しながら何もストリームされていない receiver は
   ありえます。逆も成り立ちます: trust 画面のままの pane は **live-attached でも
   session-active でもありません** が、起動前に `delivery.sh` で設定した delivery 設定は
   影響を受けません。起動 UI の状態が設定を消すことはなく、設定が attachment を意味することも
   ありません。
2. **live attachment — agent ごとに異なる。** **claude** では、その receiver 自身の
   セッションに現れる Claude Code Monitor マーカーが証拠です: transcript の
   `Monitor(agmsg inbox stream)`、フッターの `1 monitor`（`1 shell` は **不可** —
   バックグラウンドの `watch.sh` は診断/フォールバック専用）、メッセージ到着時の
   `Monitor event` 行（[Monitor ツールと delivery-mode](#monitor-リカバリ) を参照）。
   **codex** では、bridge が適用される場合の bridge-alive マーカー:
   `delivery.sh status` の `Codex bridge: <team>/<role> alive (pid N)`。bridge は codex 起動時
   ではなくセッションへの **最初の turn** で有効化される点に注意してください。
3. **end-to-end** — [ping テスト](#receiver-の準備状態readiness) の ack が **唯一の**
   end-to-end の証明です。レイヤー 1・2 は前提条件であって代替ではありません。live マーカーが
   得られない場合は明示的にフォールバックし（`turn` delivery または手動 `inbox.sh`）、その旨を
   述べたうえで、それでも ack を必須にします。

**verified liveness — startup report は readiness ではない。** provisioning は
メッセージではなく verified liveness で完了します。ロールが provisioned となるのは、
startup report が届き **かつ**、**settle delay** の後に次の 3 つがいずれもまだ通る
場合です:

1. **pane が依然として agent の TUI をホストしている** — pane を読みます。shell
   プロンプトが出ていれば、どれほど直前に報告していても agent は終了しています。
   pane が ground truth であり、メッセージは過去についての主張にすぎません。
2. **agmsg の ping-pong 往復が成功する** — 今疎通確認し、今 pong を要求します。先ほどの
   readiness ack が証明するのは「その時点で」生きていたことだけです。
3. **codex では bridge が armed で app-server attachment が安定している** — codex の
   TUI は per-folder の app-server に `--remote` websocket で attach するため、pane も
   bridge も直前まで正常に見えていたのに attachment だけが死ぬことがあります。

> **startup report は readiness ではありません。** field incident(2026-07-29):
> 2 体の codex agent が startup-complete を報告した **数秒後** に、共有していた
> app-server の喪失で死亡しました。それでも監督スレッドは「startup report を待っている」
> と言い続け、その時点で全 agent は既に死んでいました。report だけで provisioning を
> 完了と結論してはいけません。

settle delay が重要です: この検査が捕まえる失敗は report の **数秒後** に起きるため、
即座に検証しても report が述べたのと同じ瞬間を再観測するだけで、新しいことは何も
証明できません。

**early death は normal mode です。** そのシグネチャは、websocket の **transport
reset** が app-server 接続を落とした結果、TUI が **shell プロンプトへ抜ける**(多くは
resume ヒントを画面に残す)ことです。ただの端末に見える pane になるため、ダイアログだけを
探すスキャンでは見逃します。チェックが失敗したら **再チェックして復旧します。次の report を
待ってはいけません** — 死んだ agent は何も送らないので、待つことは永遠に待つことです。

> **共有 app-server の death mode。** app-server を kill すると、**接続している
> すべての TUI が一斉に落ちます** — kill の理由と無関係な、他チームの agent も含めて。
> 2026-07-29 の 2 体同時死はまさにこれでした。予防策は下記の attribution ルールです:
> app-server を停止する前にプロセス自身の cwd を確認し、attribute できないプロセスには
> 手を出さないこと。これは attribution 違反の二次被害です — 被害者は終了したプロセス
> ではなく、それに接続していたすべてです。

**5. 排他性とハンドオーバー。** 1 つのロールを保持できる生きたセッションはちょうど 1 つで、
2 番目の actas は拒否されます — その拒否が正しい挙動です。セッションの置き換えは
**graceful drop** を通します: 現保持者が（オペレーター確認つきで）ロールを解放し、
その後にはじめて後継が取得し、readiness + ping テストを再実行します。

**6. 参照ワークスペースマネージャーは herdr。** 設計スレッドが駆動する surface は
`workspace create`、`pane split`、`pane send-text` / `send-keys`、`agent prompt`、
`agent wait` です。intent-cli は herdr を所有・同梱・ラップしません — internals は
agmsg internals と同じくリンクアウトし、herdr 自身のドキュメントを参照します。同じルール
（ロールごとの専用フォルダーを pane の cwd にする、shim-safe なタイプ起動、初回プロンプトに
立ち会う、ping テスト前の actas + readiness、1 ロール 1 保持者と handover 時の graceful
drop）が満たされるなら、**任意の** 同等なワークスペースマネージャーで置き換えられます。

## チームのワークスペース配置（G637、preview-through-1.x）

> **1.x を通じた preview。** このワークスペース配置ガイドは v0.12.0 の freeze 後に追加されました。
> 1.0 compatibility promise の対象外であり、1.x の間に変更または撤回される可能性があります。
> 正式化は後続 MAJOR release で行います。[compatibility ledger](1.0-compatibility-ledger.md) の
> preview entry を参照してください。

各 team workspace は、3 席を見渡せる 1 つの形にそろえます。
`orchestration` は左側で幅 40%、高さ全体を占めます。`implementation` は右上、`review` は右下です。
右側に残る 60% は上下で均等に分かれ、各 pane は 30% になります。label は記録済み topology の
role 名である `orchestration`、`implementation`、`review` を使います。3 席目が実際に design の席なら
その pane の label は `design` とします。slot の規約は seat の identity を変更しません。

この guide は operator が観測した shape と明示的な ID を入力にします。live workspace を一覧・照会
せず、herdr を実行もしません。

```text
intent-cli guide workspace-layout --workspace-id <workspace-id> --tab-id <tab-id> \
  --shape canonical|three-column|mirrored|unknown \
  --orchestration-pane <orchestration-pane> --implementation-pane <implementation-pane> \
  --review-pane <review-pane> --temporary-tab-id <temporary-tab-id> --format markdown
```

canonical shape と canonical label の workspace では、変更不要であることを表示します。別の shape なら、
一時 tab へ `herdr pane move` してから、本来その下に置く pane を target にして戻す往復、pane rename、
40% / 60%-均等分割を作る resize の呼び出しをこの順番で表示します。ratio が分かる場合は渡して、
resize amount を必要最小限の方向差分にします。表示された command を実行する直前に、明示的な ID を
record から解決してください。この guide は plan であり executor ではありません。

実測した herdr 0.8.0 on macOS では、同じ tab 内の `herdr pane move` は `changed: false` を返す no-op です。
一時 tab に移してから destination pane を target にして戻す往復も同じ herdr 0.8.0 on macOS で実測し、
pane は再作成ではなく付け替えられ、稼働中の 17 agent process がすべて残りました。まず scratch tab で
この往復を検証し、すべての agent が残っていることを確認してから、稼働中 agent を持つ workspace に適用し、
もう一度存在を確認します。これは測定した事実であり、未測定の herdr version や platform への主張では
ありません。single-pane workspace は標準化する配置を持たないため、この規約の対象外です。

## herdr-only の運用手順（preferred — fewer dependencies）

この節はチームが `herdr-only` を記録している場合だけ operative です。agmsg の
provisioning / receiver 節に対する具体的な counterpart です。依存関係が少ないため優先しますが、
agmsg + herdr はサポート対象で廃止されません。1 チームでは transport を 1 つだけ動かし、
agmsg と herdr の mixed delivery は contract violation です。

### human の seat-kind intake と実測 registry (G647)

herdr-only の seat を起動する前に、各 seat（`design`、`orchestrator`、`implementation`、`review`）で
どの CLI と model を使うか human に尋ね、各回答をその seat の `kind` として記録します。silent に
default を選びません。recorded kind は human の現在の wish であり、要求された switch は one step、
recovery は unattended に kind を変更しません。`topology update-kind` は変更とともに target の実測
recipe を表示し、未記録なら absent を明示します。

registry には実測済みの kind だけを置きます。Codex の実測 entry は次のとおりです。

```text
herdr agent start <logical-role> --kind codex --pane <pane-id> -- --sandbox workspace-write --ask-for-approval never --add-dir <role-work-root> [--add-dir <host-routing-root>]
```

**MyIntentHost で 2026-08-07 に実測した Codex v0.144.1 / macOS** の fact は、workspace-write・never-ask
approval・role-derived root の bounded invocation、自己更新が **「Please restart Codex」** を表示して pane を
shell に残す挙動（再起動と READY/ping を行い、wedge とは扱わない）、宣言 root 外の write は拒否される一方で
read は拒否されないという asymmetry です。rendered / structured の各 measurement には
`host: MyIntentHost` と `date: 2026-08-07` を付けます。Cursor と opencode は実測 entry がなく、名前だけの
placeholder に留めます。

> **Reachability discipline (G650)。** source に存在することは reachability ではありません。
> この G647 guidance が到達可能だと記録するのは、実際に role が動かす build で、記録済みの
> session layer の team-scoped guide を表示してからにします。typed fragment が欠けた場合は
> fail-closed の rendering defect であり、typing rule を弱める理由にはなりません。

### Provisioning と READY の証明

この topology を文字どおり使います: **1 チームにつき 1 workspace、team 名の 1 tab、role
ごとに 1 pane とし、各 pane はその role の folder を cwd にして開きます。** これにより、
すべての role を同時に operator から見える状態にし、G550 supervision pane scan が inactive
な tab の背後に隠れないようにします。

最初に workspace を作成します。

```text
herdr workspace create --cwd <host-repo> --label <team> --no-focus
```

この host で herdr 0.8.0 にて実測した `workspace_created` result は top-level の `workspace`、`tab`、`root_pane` を
返すため、`workspace.workspace_id`、`tab.tab_id`、`root_pane.pane_id` から mapping を初期化
し、`root_pane.cwd` を検証します。返された tab が team の通常唯一の tab です。その名前が
`<team>` であることを保証し、必要なら返された explicit tab id を使います。

```text
herdr tab rename <tab-id> <team>
```

root pane を host-repo role の 1 つに割り当てます。残る各 herdr-resident role では **pane
creation が default** です。記録済み mapping から空でない pane id を解決し、その
明示した pane から split して新 role の cwd を指定します。

```text
herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus
```

design / orchestrator には `<host-repo>`、implementation には child checkout、review には
隔離した review cwd/worktree を使い、各 pane creation result から mapping を更新します。
`herdr tab create --workspace <workspace-id> --cwd <role-cwd> --label <logical-role> --no-focus`
は primary path ではありません。tab-level lifecycle isolation を simultaneous visibility より
優先する場合など、文書化した理由で separate role tab を operator が明示的に認可した
ときだけ例外として使います。

同じ tab 内の `herdr pane move` は未サポートです。同じ tab の layout を変えるときは、影響する
pane を作り直して logical-role mapping を更新し、in-place の move で role の配置を保てるとは
考えません。

operator-visible な logical-role→pane-id/cwd mapping を記録し、workflow は pane/workspace id を
固定指定しません。initial workspace creation が最初の id を返した後、すべての provisioning /
mutation command は実行直前に記録済み mapping から明示的で空でない pane/workspace target id
を解決し、command にその id を必ず指定します。解決結果が missing または empty なら fail
closed とし、command を実行しません。そうしないと herdr が focus-default で他チームの
currently focused pane を mutate する可能性があります。既存 G555 cross-project attribution
rules が変わらず authoritative であり、別の attribution policy を再定義せずそれを参照
します。herdr 外の design frontend は架空 pane ではなく reader type として記録します。

この machine-scoped かつ team ごとの topology は
`<host-repo>/.intent-cli/topology/<domain>/<team>.json` に永続的に保存します。CLI は
`.intent-cli/topology` 内だけに directory-local ignore を書くため、pane id と absolute path
は machine local のままで root `.gitignore` を編集しません。各 record は `domain` と `team`
を自身に持ち、path と identity が食い違う copy はフェイルクローズします。
`session-layer-mode.json` はこれまで通り tracked な multi-team truth です。machine truth を
移す方法は値の copy ではなく destination machine での re-record です。team の
`workspace_id` を記録し、`roles` 配下で pane-backed role には `resident: herdr` と明示的な
`workspace_id` / `pane_id`、herdr 外の role には `resident: external` と routing-root-relative
な `reader`（通常は `.intent-cli/events/<domain>/<team>.jsonl`。legacy の
`.intent-cli/events/<team>.jsonl` record も有効）を記録します。すべての recorded role は
sender と delegate report target になれます。受信時は herdr resident に、その正確な team
workspace の recorded pane で running agent が必要です。external resident は recorded reader
を通して canonical delegate/report event を受け取ります。missing/unsafe reader、stale pane、
foreign-workspace-only name、ambiguous mapping は prompt / append なしで fail closed になります。

新しい per-team file が absent のときだけ legacy fixed file を互換性のために読み出し、
`topology record` を名指しする deprecation warning を出します。両方が存在して内容が
食い違う場合は、どちらも優先せずフェイルクローズします。

この artifact は手編集せず、canonical topology surface で記録・検査します。

```text
intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> [--kind <agent-kind>] --write
intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident external --reader <routing-root-relative-path> [--frontend <frontend>] --write
intent-cli session-layer topology update-kind --domain <domain> --team <team> --role <role> --current-kind <kind> --new-kind <kind> --confirm-update-kind --write
intent-cli session-layer topology update-field --domain <domain> --team <team> --role <role> --field delivery_method --current <absent|inline|file-backed> --new <inline|file-backed> --confirm-update-field --write
intent-cli session-layer topology retire-legacy --domain <domain> --team <team> --evidence <named-fleet-migration-evidence> --confirm-retire-legacy --write
intent-cli session-layer topology validate --domain <domain> --team <team> --format json
intent-cli session-layer topology show --domain <domain> --team <team> --format json
intent-cli guide topology-workspace-move --domain <domain> --team <team> --format markdown
intent-cli session-layer topology move --domain <domain> --team <team> --workspace-id <new-workspace-id> --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... --dry-run --format json
intent-cli session-layer topology move --domain <domain> --team <team> --workspace-id <new-workspace-id> --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... [--current-digest <digest>] --write --format json
```

G697 は意図的な workspace rebuild の path を追加します。インストール済みの
`guide topology-workspace-move` recipe は `guide review`、`guide next --role review`、
`guide orchestrator-thread` から到達でき、inspect → dry-run preview → explicit write →
validate → notify preflight の完全な順序を表示します。move は herdr role ごとの operator-supplied
old-pane から new-pane への完全な map を必要とし、CAS lock を保持して topology digest を比較します。
team と role の workspace/pane id だけを atomic に更新し、他の role field は維持します。herdr query、
pane 作成、membership 変更、per-role conflict の repair は行いません。既存の refusal は sanctioned な
whole-team transition としてこの move command を示します。

agent kind は herdr が起動できる任意の kind です。Claude、Codex、Copilot、Cursor、OpenCode などは
例であり、supported-set の制約ではありません。logical role の既定値は `implementation`、`review`、
`interview`、`clarify` です。legacy の product 名を含め、既存の明示的な role mapping はそのまま
有効です。3 つの update/retire mutation command は JSON を出力し、`--format json` だけをサポートします。
`update-kind` では明示した `--dry-run` が flag の順序にかかわらず `--write` より優先され、決して
書き込みません。

legacy fixed `role-pane-mapping.json` の compatibility read は削除済みです。この file が残り per-team record がない host では reader は安全側に停止し、`topology record --domain <domain> --team <team> ... --write` と `topology retire-legacy --domain <domain> --team <team> --evidence <evidence> --confirm-retire-legacy --write` を示します。reader は自動移行しません。`retire-legacy` が成功すると、CLI は ignored な machine-local topology directory の外にある
`<host-repo>/.intent-cli/legacy-topology-retirements.jsonl` へ fleet-wide decision から引用可能な
entry を 1 件追記します。定義済みの field は `timestamp_utc`、`host`、`domain`、`team`、
`retired_path`、名前付きの `evidence` です。これにより、現在の legacy reader disposition を
変更せずに、後続の ledger decision が累積した retirement を引用できます。

`record` が使う値は operator が供給したものだけです。herdr query、id の guess、resource の
provision、既存 conflict の repair は行いません。完全一致は idempotent no-op、異なる既存 role
は file を書き換えず拒否します。read-only の `validate` は `valid: true|false` と全 finding
を一度に返し、missing/unsupported residence、missing `pane_id`、unsafe reader、team-workspace
mismatch を含む各 finding に role、field、cause、message を記載します。`show` も read-only
であり、`notify` と同じ delivery-target 関数を通して各 pane / reader を解決し、prompt、append、
herdr query を行いません。mapping が存在するか herdr-only が必要とする場合、`automation doctor`
もこの health を載せ、notify の topology refusal は remedy として `topology validate` / `record`
を示します。不正状態は常に fail closed のままです。これらは knowledge と controlled writer を
追加するものであり、fallback は追加しません。

`update-field` は、recorded role が一度も持たなかった field を宣言する場合、または既に記録された値を
変更する場合のための狭い経路です。role、field 名、stated current value、新しい値、explicit confirmation、
`--dry-run` または `--write` が必要です。field が実際に absent の場合に限り `--current absent` と指定します。
古い認識にもとづく指定は両方向で拒否されます。registry が最初に許可するのは `delivery_method` だけなので、
unknown または dotted name は任意の JSON path を編集できないよう拒否されます。この command が
変更するのはその field だけです。`record` の conflict refusal は緩和されず、異なる shape の re-record は
引き続き拒否され、force flag もありません。

#### 可視な生成済み mode marker

mode record は唯一の source of truth のままですが、team が transport を知るために毎回 query を
思い出す必要はありません。agent-startup file（`AGENTS.md` または `CLAUDE.md`）に各
`(domain, team)` 用の明示的な空の managed block を 1 つ置き、recorded mode から display を生成します。

```text
<!-- intent-cli:session-layer-marker:start domain="<domain>" team="<team>" -->
<!-- intent-cli:session-layer-marker:end -->

intent-cli session-layer marker generate --domain <domain> --team <team> --file <AGENTS.md|CLAUDE.md> --write
```

生成済み block は domain、team、mode、canonical な `session-layer show` verification command、
resolved canonical record の hash を持ちます。host-global または bare な mode claim にはなりません。
writer は record だけを読み、その delimited block だけを更新します。unrecorded team（`session-layer
set ... --write` を明記）、absent block、malformed marker は拒否し、
`session-layer-mode.json` を書きません。

shared preflight は `AGENTS.md` / `CLAUDE.md` 内の managed marker を発見します。recorded team に
marker がなければ generating command を含む informational な `marker-not-generated` です。mode または
record hash が異なる marker は file、claim、canonical truth を明記する `marker-drift` となり、verdict を
`configuration-incomplete` にします。mode switch の後は再生成してください。marker は signpost であり、
canonical record の代わりにはなりません。

herdr workspace を provision するときは、recorded mode を workspace label に含めます（例:
`<team> · herdr-only`）。この label は human-facing かつ non-authoritative です。intent-cli は herdr
state を書かず、label を mode evidence として読みません。

#### mode switch 後の manual migration review

`session-layer set --write` が recorded mode を実際に変更したときは、順序付きの **manual migration
plan** を出力します。other mode の session hooks、inbox watchers / monitors をレビューし、続けて G601
visibility marker を再生成してください。各項目は operator action です。intent-cli は user
configuration を delete、rewrite、無効化しません。no-op の set は plan を出しません。

shared preflight は declared location だけで known other-mode residue を確認します。たとえば
herdr-only team 上の `.codex/hooks.json` にある project-level agmsg session hooks は報告対象です。
`other-mode-residue` finding は path と owning mode、one-mode exclusivity contract、removal guidance を
明記しますが advisory のままです。residue は active mixing の証明ではなく hazard であり、canonical
mode record を infer、flip、上書きしません。

#### shared record-first session-layer preflight

`automation doctor`、guide の READY definition、`notify` は、同じ production predicate が返す
1 つの machine-readable `session_layer_preflight` result を消費します。似た predicate を
3 個持つのではありません。passive structural phase は receiver に接触せず、active receiver
phase は別に報告されます。active phase の skip は、passing な passive verdict を無効にしません。

named team に mode record がない場合は `configuration-incomplete`、すなわち check-not-completed
であり、決して `not-required` ではありません。intended mode を明示的に記録してから検証します。

```text
intent-cli session-layer set --domain <domain> --team <team> --mode agmsg|herdr-only --write
intent-cli automation doctor --domain <domain> --team <team> --format json
```

bare anonymous root は expected domain/team を宣言するまで `unjudged` のままです。
`cannot-determine` は決して green ではありません。preflight は live herdr state から mode を infer
または修正しません。mode が record 済みなら、その transport だけを確認し、contradiction
evidence は diagnostic detail にとどめます。role-pane topology が記述する mode は `herdr-only`
です。team の recorded mode が `agmsg` なら、team、recorded mode、topology mode を明記した
mismatch を返します。このため agmsg team に herdr install は要求されません。

typed launch は次の surface です。

```text
herdr agent start <logical-role> --kind <agent-kind> --pane <pane-id> -- <operator-approved-permission-flags>
```

permission flag は launch に渡し、modifier chord を注入しません。approval は自動回答せず、
G550 の MAY/escalate 境界がそのまま支配します。approval は pane-visible で supervision boundary
において処理し、agmsg Codex bridge の headless auto-decline とは明確に異なります。READY は最初に
shared passive preflight の `ready` を要求し、その後 G556 active receiver proof を適用します。
startup report 後に settle delay を置き、期待する cwd/repo と agent kind、同一 pane detection を
再確認し、bounded unattended probe の working transition と fresh settled acknowledgement を
観測します。re-provision 後は record-first の settle-and-re-check sequence 全体を繰り返します。
workspace の存在、shell prompt、agent state だけ、または idle のままの unattended prompt は
READY ではありません。herdr-only の role identity は検証済み logical-role→pane mapping であり、
別の agmsg identity step はありません。

### Dispatch、wait、artifact 検証

[canonical notify workflow](#canonical-notify-workflow) の
`intent-cli notify delegate ...` を target logical role に対して実行します。CLI が
herdr-only を内部解決し、role mapping を検証して structured task block を生成します。
`herdr agent prompt` を手書きしてはいけません。

**reference-first dispatch。** review の実体は committed canonical な `review-context.md` に
置き、delegate にはその file への短い pointer だけを載せます。packet にない consideration は
`review-context.md` に追加してプッシュし、それを参照します。pane prompt に実体をインラインで
書いてはいけません。これは packet structure を変えるものではなく、意図どおりに使う discipline です。

測定済みの限界も重要です。最小の canonical `notify delegate` envelope でも 842 文字・14 行であり、
これ自体が paste になります。reference-first は重複する実体を減らしますが、paste-sensitive な seat の
wedge を防ぐものではありません。transport-layer の remedy は G619 が担当します。

貼り付けに弱い herdr seat の記録済みロールに `delivery_method` がない場合は、
`--field delivery_method --current absent --new file-backed` と明示的な確認を付けて
`topology update-field` を使います。後から許可された値を変更するときも、記録済みの現在値を指定して
同じ経路を使います。`notify` は変更しない envelope をアドレス可能で永続的な
`.intent-cli/tasks/<domain>/<team>/<task-id>-<nonce>.md` に書いてから、pane へ
`Read task envelope: <path>` という 1 行のポインターを送ります。file は削除しないため、再起動
した recipient も同じ task を読み直せます。明示的に選ぶ場合だけ `inline` を宣言し、宣言がなければ
確立済みの inline delivery は byte-identical のままです。

recipient recipe の `inline_payload_warning_chars` profile は **advisory** であり、universal な
safe-paste limit ではありません。delegate の inline payload が resolved threshold を超えると、
`notify` は payload size、threshold、reference-first remedy を human と machine の両方に warning
として出しますが、同じ payload の delivery は続行します。refuse も truncate もしません。別 team
での観測では、大きな paste が terminal に broken bracketed-paste state を残し、一部の agent process
を terminate することがあります。fresh agent start で復旧します。これは観測事実であり、
すべての terminal や agent に universal な size limit があるという主張ではありません。

settled pane では、notify は最初に bounded `agent prompt --wait --until working` を使い、続けて
idle/done/blocked を待つ別の bounded `agent wait` を実行します。観測された unattended working
transition が delivery verdict です。一度これを観測したら、後続の settle check が
`delivered: true` を否定することはありません。独立した acknowledgement は `settle_outcome` で
`observed`、`pending`、`not-applicable` と報告し、機械可読な retry verdict は
`resend_permitted` です。idle のままなら `receiver_state_outcome: idle-stays-idle`、
`working_transition: not-observed`、`settle_outcome: not-applicable` で未配達のため
`resend_permitted: true` です。working へ入った後、bounded settle observation の終了時にも
recipient が作業中なら、これは成功した non-terminal dispatch です:
`receiver_state_outcome: working-observed-in-progress`、`working_transition: observed`、
`settle_outcome: pending`、`resend_permitted: false` となります。recipient が作業中の間は再送しません。notify 開始時にすでに working の pane は
prompt submission 成功後に delivered としますが、`receiver_state_outcome: already-working`、
`working_transition: unobservable`、`settle_outcome: not-applicable`、`resend_permitted: false` と
報告し、active turn を新 prompt の transition と誤認しません。dry-run は active phase を `skipped`
のままにしてプロンプトしません。

`--to` は引き続き topology の logical role を指定しますが、logical role name は globally unique な
herdr agent name から独立しています。recipient identity は recorded workspace と pane の組です。
notify はその workspace 内のその pane に running agent がちょうど 1 件あることを要求し、agent
name は diagnostic detail にだけ使います。agent が 0 件、running agent が複数、または pane が
foreign workspace でのみ報告された場合は、team、recorded workspace、recorded pane を明記して
fail closed にし、agent-name match fallback は決して行いません。

dispatch ごとに fresh で予測不能な nonce を生成し、再利用や task id 単独での代用をしません。
`pane wait-output` は既存 output を即座に検索するため、task block 内の precomposed wait needle
がエコーされ、作業開始前に false match することがあります。生成された split field により、その literal を
配信対象から除外します。handoff は file、commit、PR、verification log などの検査可能な
artifact です。screen prose はそれを指す signal にすぎません。repair は現在の pane mapping を
解決した後、同じ logical role に task id と具体的 delta を添えて戻します。どの buffer 由来でも
marker match だけでは不十分で、named artifact の存在と verification が必要です。repair も
`intent-cli notify delegate` で同じ logical role に戻します。

### 2 つの normative wake source

herdr-only orchestration には 2 つの normative wake source があります。canonical な
`intent-cli notify report` は primary かつ最も情報量が多く、task id、status、artifact、
summary を運びますが、worker が協力して必須の final command を実行することに依存します。
normative な **SECOND wake source** は herdr が観測する
`pane.agent_status_changed` です。worker が report を省略しても、herdr による process 観測
だけで orchestration を wake できますが、task outcome は運びません。

この host で herdr 0.8.0 にて実測した socket API は `events.subscribe` を使います。
`pane.agent_status_changed` は `pane_id` を必須とするため、watched pane ごとに 1 つの
subscription entry を含めます。

```json
{"method":"events.subscribe","params":{"subscriptions":[{"type":"pane.agent_status_changed","pane_id":"<resolved-pane-id>"}]}}
```

各 `<resolved-pane-id>` は subscribe 時と re-provision 後に、記録済み
logical-role→pane mapping から解決し、pane id を固定指定してはいけません。event frame は
`agent`、`agent_status`、`pane_id`、`workspace_id` を運びます。

```json
{"event":"pane.agent_status_changed","data":{"agent":"<agent>","agent_status":"<working|idle|done|blocked|unknown>","pane_id":"<resolved-pane-id>","workspace_id":"<workspace-id>"}}
```

直前の status は logical role ごとに独立して追跡します。その role が `working` から
settled (`idle`、`done`、`blocked`) へ遷移した場合だけ起動を促します。最初から settled の
sample、`unknown`、settled→settled の変化では起動を促しません。wake 前に settle delay を置き、
per-role dedupe により burst から生じる wake を、その観測済み transition につき 1 回にします。
新しい `working` の観測で、その role を再有効化します。

**state change は何かが起きたことだけを意味し、task が成功したことを決して意味しません。**
どちらの source から起動を促した後も毎回、orchestration は現在の herdr state と pending
approval/question pause、正確で fresh な completion marker と status、検証済み named
artifact、fresh な canonical intent-cli/GitHub facts を確認します。2 つの source は相補的です:
notify report は最も情報量が多い一方で worker の協力に依存し、state change は herdr の観測
だけに依存する一方で outcome を運びません。periodic な
`intent-cli automation stalled-work ...` check が last net です。この実測 shape は、
non-informational な `approved-not-merged` kind により、`intent-pr-approved` を持つ
open かつ non-blocked な PR が configured stale threshold を超えたら、すべての immediate wake
source が失敗しても age と canonical な merge → `closeout pr` path 付きで actionable にします。
version-specific details について installed herdr help/schema を確認するという standing rule を
置き換えません。

wait は必ず bounded にします。

```text
herdr agent wait <logical-role> --until idle --until done --until blocked --timeout <milliseconds>
herdr pane wait-output --match "ORCH_RESULT <fresh-per-dispatch-nonce>" --source recent-unwrapped --timeout <milliseconds> <pane-id>
```

`idle`、`done`、`blocked`、marker match、timeout を含む EVERY wait return 後に
`herdr pane read --source recent-unwrapped <pane-id>` を実行し、pending approval / question を
確認します。`idle` は approval-paused の場合があります。結果を settled、
approval/question-paused、timeout に分類します。pause では pane から読んだ G550 MAY class
だけを回答し、それ以外はエスカレーションしてから wake に戻り、再度待機します。timeout も
re-entry point です。進捗を永続的に保存して制御を返し、後続 wake で再開します。長い flow には
cursor を永続化する deterministic script を推奨します。success は pending
approval/question のない settled state + 正確な fresh-nonce marker と status + 存在して検証済みの
artifact + fresh な canonical intent-cli/GitHub facts の合成判定です。artifact verification と
canonical facts が final gate であり、state 単独も marker 単独も success ではありません。

### Normative な `events.jsonl` design boundary

host root を実行時に解決し、`<host-repo>/.intent-cli/events/<domain>/<team>.jsonl`
へ追記します（G681、`preview-through-1.x`）。domain と team は検証済みの verbatim な
path segment なので、同じ team name を使う 2 domain も別 file へ書きます。reader は scoped
file を先に調べ、それが absent の場合に限り legacy
`<host-repo>/.intent-cli/events/<team>.jsonl` を代わりに参照します。新しい追記は legacy file を
使いません。path 構築前に、空文字、先頭 dot、`/` または `\`、任意の `..` sequence を
fail closed で拒否します。不正名を無害化してはいけません。

migration は operator 所有かつ optional で、intent-cli は host file を移動しません。host
repository root で placeholder を置換し、次を正確に実行します。

```sh
mkdir -p .intent-cli/events/<domain> && mv .intent-cli/events/<team>.jsonl .intent-cli/events/<domain>/<team>.jsonl
```

既存 external-reader topology は legacy reader value のままでよく、同じ scoped-first の
代替参照と scoped 配置が topology edit なしで適用されます。operator move 後も既存の永続
watermark と不変の file identity/replacement check を維持し、自動リセット／再読込
してはいけません。

canonical `intent-cli notify` surface だけが writer で、caller は手動追記しません。
通常は orchestrator が delegate/escalate event を書き、recorded recipient が external の場合は
receiver の canonical report も append できます。`O_APPEND` で開き、1 行に完全な JSON object
を 1 つ追記し、embedded newline を許さず、`summary` を 1 行へ正規化します。必須 schema:

```json
{"timestamp":"<RFC3339>","team":"<team>","kind":"completion|blocked|question|escalation","unit":"<execution-unit-or-task-id>","summary":"<one-line-summary>","artifact":"<repo-relative-path-or-URL>"}
```

recorded external reader 宛ての canonical notification と、design-relevant な completion /
blocked / question / escalation だけを書きます。external reader 宛て delegation は `question`、
external report は `completed|blocked|question` status を event kind
`completion|blocked|question` へ対応付けします。pane-resident dispatch、
routine progress、pane output、acknowledgement はここへミラーしません。この mode-independent
channel は explicit external-reader/design boundary のままで、fallback inter-agent bus ではなく、
`intent-cli notify`、GitHub、intent-cli workflow state の代替でもありません。

すべての reader は watcher restart をまたいで永続的な watermark を保持し、file identity、
byte offset、complete-line count を記録します。各 read 前に同じ file identity であることと、
byte / line count が逆戻りしていないことを検証します。
永続的な byte-offset watermark は必ず file identity と complete-line count と組にし、3 値のどれも
restart-local にしません。
rotation、truncation、backwards count、file replacement は operator recovery まで fail closed とします。replay が design
decision を重複させるため、先頭から silent リセットしてはいけません。

- Claude app watcher: 永続的な file-identity/byte-offset/complete-line-count watermark より後の
  完全な未読行だけを末尾追跡し、成功後にのみ進め、watcher restart をまたいで保持します。
  rotation、truncation、backwards byte/line count、file replacement で fail closed とし、先頭から再開しません。
- herdr pane の Codex CLI: 通常 coordination では `intent-cli notify delegate` / `report` を使い
  file ポーリングしません。design-boundary reader として動く場合は同じ永続的で restart-surviving な
  watermark を使い、rotation、truncation、backwards count、file replacement で fail closed とし、
  先頭へリセットしません。
- Codex Desktop: one-minute-class（約 1 分）cadence でポーリングし、永続的で restart-surviving な
  file-identity/byte-offset/complete-line-count watermark より後の完全な行だけを処理します。
  rotation、truncation、backwards byte/line count、file replacement、malformed JSON で fail closed とし、
  先頭からリセットしません。

### Recovery と mode switch

運用 baseline は macOS/Linux の latest stable herdr です。Windows support は beta であり、
この guide では仮定しません。`herdr --skill` は bundled herdr agent skill を見つけるためだけの
discovery pointer で、intent-cli guide の正本となる定義より下位です。version-specific detail は引き続き
installed herdr help/schema を参照します。

- live server update: running pane を保つ `herdr server live-handoff` を使います。
  `events.subscribe` consumer は stream EOF を error ではなく resubscribe trigger として扱います。
  handoff 近くで回答した approval は再提示され得るため、pane を再読し同じ dialog を
  再判断します。以前の回答が消費されたと仮定せず、blind re-answer もしません。pane PTY size
  は TUI client の reattach まで shrink することがあります。read は有効なままで、headless
  resize/zoom は PTY を復元せず、operator の TUI reattach が remedy です。

- modifier-chord launch corruption: shell へ戻すか再準備し、typed な
  `agent start ... -- <permission-flags>` を使います。
- reboot 後の dead pty wiring: stopped server の socket command は `server_not_running` を返します。
  headless server は TUI client を待たず restored agent session を再開します。undetected agent /
  shell-only pane では artifact を保全して re-provision、mapping 再構築、上記の自己完結した
  settle-and-re-check READY gate 再実行を行います。
- focus-default cross-team mutation: 明示的な pane/workspace id が missing / empty だと、他チームの
  currently focused pane を mutate する可能性があります。initial workspace creation 後の every
  provisioning/mutation command で記録済み logical-role mapping から non-empty id を解決・明示し、
  解決失敗時は実行しません。既存 G555 attribution rules を変更せず適用します。
- long-wait turn death: bounded wait、re-entry、永続化された deterministic loop を使います。
- dispatch-echo false match: composed wait needle を task block に入れず、return 後に pane を
  確認し、named artifact を独立に検証します。
- idle と報告された approval/question pause: every wait 後に pane を確認し、G550 の
  MAY/escalate 境界を適用して wake に戻ります。

### Session-layer switch checklist

**agmsg → herdr-only**: 作業を排出または保留し、role を **graceful drop** して watcher/bridge を停止、outgoing
transport の per-project agmsg hook configuration と delivery mode を turn off または削除し、delivery
不可を検証します。これは cosmetic ではありません。残存 hook が next-launch hook-trust screen を
発生させ、次の Codex launch を阻止した実測事象があります。herdr・mapping・検証済み events
path を provision、G556 と marker/artifact 検出を通し、最後に `intent-cli session-layer set
--domain <domain> --team <team> --mode herdr-only --write`。

**herdr-only → agmsg**: 作業を排出または保留して必要な final design event を append、operator
policy に従って workspace を停止または保持・終了し delivery を止め、agmsg role と承認済み
watcher/bridge を provision、G556 と end-to-end delivery を通し、最後に `intent-cli
session-layer set --domain <domain> --team <team> --mode agmsg --write`。両方向とも mode flip が
final canonical step です。

## 設計スレッドによるワークスペース監督（チームを動かし続ける）

provisioning はチームを作りますが、**チームを動かし続けるのは監督（supervision）** であり、
オペレーターが日常的に頼っているのはこちら半分です。オペレーターが **付与した** 権限のもとで、
設計スレッドはワークスペースマネージャー越しにチームの **セッション層** を運用します:
provisioning（上記）、セッションのライフサイクル、stall の監督。ブロッキングダイアログへの
回答は明示的な境界の内側に限られ、しかも **その内容を pane から読んだ後** に限ります。
これはセッション層のロールを追加するものであり、workflow の権限は **一切** 動きません。

**付与された権限 — セッション層のみ。** 層は 2 つ、所有者も 2 つです。**セッション** 層
（pane、プロセス、ロール保持、ブロッキングダイアログ）がオペレーターの付与対象です。
**workflow** 層 — label、queue-state、publish、委譲、CI/review ゲート、closeout — は
付与対象ではなく、決して動きません: intent-cli・GitHub・orchestrator が所有し続け、
[design↔orchestrator の double-check ルール](#ロール境界--design-が-authoringorchestrator-は-coordinate)
も従来どおり適用されます。セッションを監督することが workflow 遷移を authorize することは
決してなく、pane が詰まっていることは label を手で動かす理由になりません。権限は
**付与されるものであって前提ではありません**: 付与の外では、設計スレッドは行動せず観測と報告に
とどまります。

**セッションのライフサイクル。** 応答しないセッションはセッション層の障害であり、設計
スレッドが修復してよい対象です — ただし修復とは「正しく保持された生きたセッションを取り戻す」
ことであって、そのロールの仕事を肩代わりすることではありません。まず pane を読み（「応答なし」の
多くは死んでいるのではなくダイアログで止まっています）、delivery の問題とセッションの死を区別し、
ロールがまだそのセッションに保持されているか確認し、最も侵襲の少ない修復を優先します。置き換えは
最後の手段です。置き換えは **graceful drop** を通します — 現保持者がロールを解放し、その後に
後継が取得して readiness と ping テストを再実行 — 全過程で **1 ロール 1 保持者** を守ります。
drop の確認は **オペレーターに可視** です: 生きたセッションを退役させる判断はオペレーターのもので、
確認はそれを記録するものです。

**3 つの監督レイヤー。** 各レイヤーは他のレイヤーが構造上検知できないものを捕まえます:

| レイヤー | 目的 | ケイデンス |
| --- | --- | --- |
| リアルタイム message monitor | 受信する agmsg の返信・blocker・エスカレーション | 継続的（接続された live stream） |
| blocking-UI の pane スキャン | approval / selection / trust プロンプトで止まった pane、**および agent がいるべき場所に shell プロンプトが出ている pane**(`agent-absent`)— メッセージを一切出さない failure mode | サブ分オーダー |
| 定期 state watchdog | canonical な intent-cli/GitHub 状態と期待進捗の比較。既存の [design-thread watchdog](#design-thread-watchdog推奨されるセーフティネット) | 数十分オーダー |

**pane スキャンが探しているもの。** 2 つの stuck state が同列に並びます:

- **blocking dialog** — 入力待ちの approval / selection / trust プロンプト。下記の
  ダイアログルールに従って扱います。
- **`agent-absent`** — agent がいるべき場所に shell プロンプトが出ている状態。復旧は
  **shim 経由の relaunch** です: pane の対話シェルにタイプして起動し(実行ファイルの
  直接 spawn は禁止)、死んだのが app-server ならそれを先に再作成し、その後
  **verified-liveness の全手順** をやり直します。permission mode は起動後に切り替えるのでは
  なく **launch フラグ**(例: `--permission-mode`)で設定してください — ワークスペース
  マネージャーの合成キー注入は mode 切り替えに使えません: 平文キーは届きますが、
  shift+tab のような modifier chord は忠実に届きません(複数チームで観測)。

> **再起動をまたいだ re-arm。** 監督スケジューラーはセッションスコープです: `/loop`・automation・
> 接続された monitor は、それをホストしている設計セッションと一緒に死に、しかも停止したことを
> 誰も知らせません。各レイヤーは設計セッションの再起動を生き延びるか、**新しいセッションの最初の
> 行動として re-arm** されなければなりません。忘れた場合の実測コスト: セッション再起動の窓で
> claim が失われ、publish 済み issue が **5.5 時間** 停止しました — たまたま動いている監督
> レイヤーが 1 つも無かったためです。

**ブロッキングダイアログ — 境界。** ここでは **verified-read ルール** がすべてを支配します:
設計スレッドがダイアログに回答してよいのは、**その内容を pane から読み**、自分が何を承認しようと
しているかを述べられる場合に限ります。レンダリングしていないダイアログへのブラインド入力は、
どれほど定型に見えても禁止です。内容が読めない・検証できない場合、そのダイアログはエスカレーション
対象です。

**回答してよい（MAY）** のは次の 4 種のみで、いずれも上記の読み取りの後に限ります:

1. **自分自身が要求した作業の確認** — プロンプトが、この設計スレッドが直前に開始した操作と
   一致すること（同じ対象・同じ操作）。
2. **read-only であると検証済みのコマンド承認**（shell 以外の attended case のみ） — pane に表示された
   正確なコマンドを読み、read-only であると検証すること。書き込み・削除・インストール・publish・状態変更を
   伴うものはエスカレーション（「おそらく read-only」は検証ではありません）。G689 の
   `codex:shell-command` class は design-thread の権限対象外です。`project-test` も read-only
   ではなく、shell answer は scoped policy と audit を通じた orchestration-only です。
3. **自分自身がインストールした hook の trust 画面** — 自身の hook-trust ケース。自分が
   インストールしていないものの trust 画面は受諾対象ではありません。
4. **オペレーターが事前承認した mode 変更** — 事前承認は具体的かつ事前でなければならず、読んだ
   pane が同じ変更を示していること。セッション監督の一般的な付与から推論してはいけません。

**必ずエスカレーションする（MUST）** のは 4 カテゴリです: **読めない・検証できない** ダイアログ
（回答の根拠が無い）、**破壊的・不可逆** な承認（誤答のコストが回復不能）、**プロダクト/設計の判断を
含む** 選択（設計内容はオペレーターと double-check を通す）、そして **credential・security・
permission の待ち** — 事前承認の有無にかかわらず回答不可。

> **セッションの詰まりを解くことは、そのセッションの代わりに決定することではない。** 設計
> スレッドの仕事は、ロールが自分の仕事をできるようにセッション層を生かし続けることであって、
> ロールの選択を代行することでも、オペレーターの判断を代行することでもありません。

[watchdog の安全ルール](#design-thread-watchdog推奨されるセーフティネット) は監督全体にそのまま
適用されます: 委譲の重複禁止、permission プロンプトの自動クリア禁止、進行中作業の cancel/reset
禁止、issue/PR の force-close 禁止、durable state（永続状態）の投機的な手編集禁止。

## 共有マシン上での cross-project isolation

**このマシン上にいるのは自分だけではない、と前提してください。** 複数の
プロジェクトチームが同時に動いており、以下の substrate はすべてそれらの間で
共有されています。上記 2 セクションは **1 つの** チームの構築と維持を説明する
ものですが、本セクションはそのチームが **他チームを壊さない** ようにするための
ものです。行動できる **オブジェクト** を自チームのものに絞るだけで、**何をして
よいか** は変えないため、
[監督の権限境界](#設計スレッドによるワークスペース監督チームを動かし続ける)
はそのまま適用されます。

オペレーターインシデント(2026-07-29): 複数チームが同時に動いている状況で、ある
プロジェクトの設計スレッドが別プロジェクトのリソースを破壊し、オペレーターが手で
介入する必要がありました。同種のニアミスは同じ週の前半にも起きており、回避できたのは
「kill の前に pid ごとの cwd を確認する」という場当たり的な規律のおかげでした —
その規律は 1 つのセッション記録の中にしか存在せず、本ガイドにはありませんでした。

**mutation の前に attribution。** **pane へのキー入力**、**プロセスの kill**、
**ワークスペースのクローズや再構成**、**state ファイルの削除・書き換え** を行う前に、
その対象が *自分の* チームのものであることを確定させてください。attribution とは
以下 4 つのキーすべてによる積極的な確認であって、「他人のものだという証拠が無い」
ことではありません:

| キー | 確認方法 |
| --- | --- |
| workspace label | ワークスペースが **自分の** チーム/プロジェクト名を持つ。自分が作っておらず名前も言えないものは自分のものではない |
| pane cwd | pane の作業ディレクトリが **自分の** チーム専用ロールフォルダーのいずれかである |
| process cwd | kill の前に **pid ごとに** cwd を読む — プロセス *名* だけで絞った pid 一覧は何も帰属を確認しない |
| agmsg の `(team, role)` ファイル命名 | run ディレクトリの state ファイルは `(team, role)` 単位で命名される。team セグメントが自分のものでないファイルは、どれほど壊れて見えても他チームの bridge/watcher state である |

> **attribution できない場合は read-only。** 所有を積極的に確定できないなら、見ることと
> 報告することはできますが、mutate はできません。推測せずオペレーターへエスカレーション
> してください: ここでの誤った推測は他チームの障害であり、そのコストを払うのは自分では
> なく相手です。

**ワークスペースとフォルダーの排他性。** **team ごとに 1 ワークスペース** とし、
チーム/プロジェクト名でラベル付けします — 他チームのワークスペースや pane を
再利用・転用・借用してはいけません(アイドルに見えるものであっても)。
**1 フォルダーはちょうど 1 チームに属します** — 他チームのフォルダーで自分の agent を
起動してはいけません。これはチーム *内* で 2 ロールがフォルダーを共有することを禁じる
のと同じ folder-scoping の事実(G521)です: agmsg identity と codex bridge は
フォルダースコープなので、他チームのフォルダーで起動した agent は **相手の** identity と
delivery を乗っ取ります。

**共有 substrate と所有の単位:**

| substrate | 共有の単位 | 所有ルール |
| --- | --- | --- |
| ワークスペースマネージャーのサーバー(例: herdr server) | **すべての** ワークスペースを 1 つのサーバープロセスが提供 | 所有はサーバーではなく **ワークスペース** 単位 — 再起動・再設定・kill は決してしない |
| agmsg run ディレクトリ(`~/.agents/skills/agmsg/run`) | **全チーム** の bridge / watcher / app-server state を 1 ディレクトリが保持 | 所有は `(team, role)` **ファイル** 単位 — 自分の delivery を直すためにディレクトリごと消してはいけない |
| codex app-server | **フォルダー** ごとに 1 app-server、フォルダーはチームに属する | 所有はフォルダーに従う — 停止する前にプロセスの cwd を確認する |
| host repo | **すべての** domain のメタデータを 1 repo が保持 | 所有は **domain パス** 単位。queue-state は G548 の no-item-loss 不変条件で並行書き込みから保護されているが、それは安全網であって他 domain の state を手編集してよい免罪符ではない |

**非破壊的な復旧。** 破損を見つけたとき — 自分が壊した場合も含め — 他プロジェクトの
成果物は **保全して脇に置きます**。リネームする、脇へ移す、あるいはその場に残して報告する。
他チームのワークスペース・pane・フォルダー・プロセス state・ファイルを、どれほど壊れて
見えても削除してはいけません。壊れた成果物も、その所有者にとっては証拠です。そのうえで
**自分のものは作り直します** — その場で修理するのではなく、新しいワークスペース・pane・
ロールフォルダーを作り、provisioning をやり直します。

> **復旧の既定は cleanup ではなく recreate です。**

<a id="design-判断による-hold-と-bounded-authority"></a>

## design 判断による hold と限定された権限

**design の判断** で止まっている hold は、**可視** かつ **bounded** でなければ
なりません。どちらも欠けた場合の実測コスト: G551 のレビューは、技術チェックが
すべて green で、保留項目は機械的に事実確認可能で、両スレッドとも答えを知って
いたにもかかわらず、1 行の wording 判断のために final verdict を **9 時間**
保留しました。hold は agmsg メッセージ上にしか存在しなかったため、
`automation stalled-work` はその間ずっと `stalled: false` を報告していました
(field record で 4 件目の design 不在 stall)。

**clarification-backed hold。** orchestrator または reviewer が design の判断で
ブロックされたとき、agmsg メッセージに加えて canonical な clarify surface
(`intent-cli clarify open`)を通じて **clarification artifact を記録** します:
domain、ブロックされている execution unit、スレッドの外にいる人でも答えられる
形に書いた質問、そして — 質問側が答えを分かっていると考える場合は — 根拠となる
事実つきの推奨回答。artifact が hold を検出可能にするものであり、メッセージは
通知にすぎません。

> **agmsg だけの hold は contract violation です。** メッセージ上にしか存在しない
> ブロックは `stalled-work` にも `heartbeat` にも見えず、したがってあらゆる
> watchdog とオペレーターの目視からも見えません。design を待っているなら artifact が
> 存在するはずで、artifact が無いならそれは待っているのではなく停止しています。

その内容を運ぶのは OPEN artifact 自身です — agmsg メッセージは通知はできますが、
永続的な記録の代わりには決してなりません:

```bash
intent-cli clarify open <execution-unit> \
  --question "<スレッドの外にいる人でも答えられる形の、実際にブロックしている設計質問>" \
  --recommended-answer "<答えが分かっていると考える場合の推奨回答>" \
  --evidence "<推奨回答を支えるリポジトリ上の事実>"
```

質問は artifact の `QuestionText` に、推奨回答と根拠は artifact の `Reason` に
`Recommended answer:` / `Evidence:` ラベル付きで格納されます。3 つのフラグはすべて
任意で、省略すれば G552 以前の packet 由来挙動のままです。**clarification の schema
変更はありません。**

### design 判断待ちの記録義務

進行が design の判断で止まるとき、judgment-wait
record を開くことは任意ではなく義務です。その待ちが始まった時点で design を owner として
記録します。record は query でき、scrollback に埋もれず `heartbeat` / `stalled-work` に
現れます:

```bash
intent-cli judgment-wait open --record <design-wait-id> \
  --domain <domain> --team <team> --owner design \
  --blocking-reference <issue|pr|unit|release> \
  --action-needed "<必要な design judgment>" --evidence "<事実>" \
  --write --format json
intent-cli judgment-wait query --domain <domain> --team <team> --format json
```

判断を回答した人は、その回答と evidence を添えて同じ record を**必ず解決**します:

```bash
intent-cli judgment-wait resolve --record <design-wait-id> \
  --resolution-evidence "<回答と evidence>" --write --format json
```

回答済みで open のままの record は嘘です。既存 lifecycle は回答者が解決するまで完了
しません。これは design 所有の待ちを記録するもので、helper を追加せず、上記 clarification
lifecycle も変更しません。

**reviewer hold ルール(refined)。** 技術チェックが green で、保留項目が
非セマンティックかつ機械的に事実確認可能 → 限定された既定権限のもとで
解決し、検証事実をログに残して先へ進みます。それ以外 → clarification を記録し、
hold を **可視な pending state** として保ちます。reviewer が単に待ち、それを
メッセージで述べるだけ、という第 3 の選択肢はありません。

**限定された既定権限。** オペレーターは、判断ではなく *リポジトリの事実を
確認する* ことで決着する、少数の列挙された判断クラスを事前委譲できます:

| 判断クラス | 何が検証するか |
| --- | --- |
| 件数・列挙の訂正 | 両スレッドが読めるリポジトリの事実から件数が導出できる(例: マージ済み PR 一覧からのスライス数) |
| 引用された事実から導かれる wording 訂正 | wording がリポジトリの事実から論理的に導かれ、reviewer と orchestrator が事実と訂正の両方に合意している |
| 相互参照・リンクの訂正 | 参照先が記載どおり存在する(しない)ことを、読んで検証できる |
| canonical source との識別子・メタデータ不一致 | canonical source を名指しして読む。canonical source が勝ち、解決はそれを引用する |

これはあらゆる方向に bounded です: **付与される**(前提ではない — オペレーターの
付与が無ければ、すべての design 判断は従来どおり design へ)、**列挙されている**
(上表が MAY のスコープのすべて)、**証拠がログされる**(何を決めたか・どの事実が
それを entail するか・どのスレッドが合意したかを記録する。ログの無い解決は解決では
なく違反)、**修正可能**(design は後から証拠を確認して覆せる。この権限が買うのは
レイテンシであって finality ではない)。

**evidence の sink は `clarify record --from-file`** です — エントリは domain の
clarification return path(`intents/<domain>/clarifications/open.md`)の
`## Recently Resolved` セクションに入り、**Question** が保留項目を特定し、
**Decision** が決定値を記録し、**Rationale** が検証済みのリポジトリ事実と
reviewer/orchestrator の合意を記録します。エントリはそこに読める形で残り続け、
それが design による post-hoc amendment を可能にします。後からの amendment は
trail に追加されるだけで、修正対象を消すことはありません:

```bash
cat > /tmp/authority-decision.md <<'EOF'
## Question
<後から design が見つけられる形で特定した保留項目>

## Decision
<決定値>

## Rationale
<それを entail する検証済みのリポジトリ事実と、合意したスレッド>
EOF

intent-cli clarify record --domain <domain> --from-file /tmp/authority-decision.md
```

> **セマンティック・プロダクトの判断は絶対に除外されます。** intent shaping、
> packet 内容と受け入れ基準、リリーススコープ、優先度の裁定は、常に
> [design↔orchestrator の double-check ルール](#ロール境界--design-が-authoringorchestrator-は-coordinate)
> を通じて design へ行きます。本 contract はそのスコープに触れません。問いの決着に
> 「何が *真である* かを確認する」のではなく「何が *真であるべきか* を決める」ことが
> 必要なら、この権限は及びません。

**定期的な design リマインダーループ。** clarification が open である間、
**orchestrator** が既存の長間隔 automation からリマインダーを design に再送します
— 新しいスケジューラーは不要で、receiver は loopless のまま。間隔は
**30〜60 分オーダー**、**open な clarification 1 件につき 1 間隔あたり最大 1 通**、
**回答されたら停止** します。design スレッドは既定で **オペレーターアプリ** 上で
動くため、リマインダーはどちらの状態でも届きます: 開いているセッションは monitor
経由で即座に受け取り、閉じているセッションは再開時に inbox で見つけます。design が
チームのワークスペースに常駐している必要はありません。

**検出。** `automation stalled-work` は open な clarification を
`design-decision-pending` として age・ブロックされているユニット・質問サマリつきで
報告し、`automation heartbeat` は他の kind と同様に `message_body` でそれを運びます
— [09-developer-reference.md](09-developer-reference.md) を参照。hold が実在するのに
この kind が出ないなら、artifact が記録されていないということです。それは上記の
contract violation であって、detector のバグではありません。

## ロール境界 — design が authoring、orchestrator は coordinate

**design が packet を作成し、orchestrator は ready な packet を workflow に通します。**
orchestrator は黙ってプロダクト/リリース/設計の author になっては **いけません**。

- **design が所有** — intent shaping と clarification; ADR と設計判断; リリーススコープと
  バージョン選択; packet 内容と受け入れ基準（永続的な packet ファイル）。
- **orchestrator が所有** — canonical な intent-cli/GitHub state の検査; **1 wake につき
  既に authoring 済みの `issue-cut-ready` packet を 1 件だけ** publish; implementation/review
  への委譲; CI/review の待機; canonical review surface 経由の approved PR の closeout;
  blocker と不足 packet を design に報告。

必要な packet が不在・不完全、またはプロダクト/リリース/設計判断を要する場合、orchestrator は
それを **でっち上げません** — 構造化された `packet-needed` メッセージを design に送り、design が
packet を author/更新する（または明示的に指示する）のを **待ちます**:

```json
{"to":"design","type":"packet-needed","domain":"<domain>","need":"<what is needed>","reason":"<why the orchestrator cannot proceed>","blocking":"<the work that is waiting>"}
```

これは inbox だけに置く state ではなく design judgment の待ちです。開始時に orchestrator は
`--owner design` つきの judgment-wait record を開き、待機中に既存 record を照会し、
回答者が evidence とともに解決します。完全な lifecycle は
[design 判断待ちの記録義務](#design-判断待ちの記録義務)を参照してください。

**release-prep は design 所有:** design がリリースバージョンとスコープを決め、release-prep
packet を作成します。orchestrator はそれが存在し `issue-cut-ready` になった **後** にのみ
publish・coordinate できます — 曖昧な「リリースを準備して」という指示からバージョンを選んだり、
スコープを決めたり、リリースノート/packet を自分で作成してはいけません。

## agmsg とは（そして何ではないか）

agmsg は **メッセージ / 進捗 / 完了 / ブロッカーのシグナル層のみ** です。スレッド間で
自然言語の委譲・返信シグナルを運びます。

`intent-cli` と GitHub は queue-state、issue/PR の事実、label、CI、レビュー、closeout、
recovery について **権威** であり続けます。シグナルはワークフロー状態ではありません。
orchestrator はそれに従って行動する前に、すべての主張を intent-cli / GitHub に対して
**再検証** します。intent-cli は Claude/Codex などの AI プロバイダーを起動しません。

## 2 つのドライバーモード（domain/repo ごとに 1 つを選ぶ）

| モード | ドライバー | 備考 |
|---|---|---|
| **orchestrator-message モード** | 4 つ目の orchestrator スレッド | **PRIMARY な 4 スレッドモデル。** 実践され、メンテナンスされているモデル。記録済みトランスポートが agmsg + herdr の場合、orchestrator が agmsg 経由で実装/レビュースレッドをペース配分する。定常状態はメッセージ駆動で、30 分クラスの design-thread watchdog loop を RECOMMENDED なデフォルトのセーフティネットとする(orchestrator-side の長間隔 automation は選択可能な alternative)。明示的な 5 分の orchestrator タイマーは fallback/legacy オプションとして引き続きサポートされる。 |
| **timer-loop モード** | 定期タイマー | **ALTERNATIVE。** orchestrator スレッドを実行しない domain/repo 向けの、完全サポートされたよりシンプルなセットアップ。実装/レビュースレッドが自己スケジュールし、`worker next-action` / host review-next-slice を読む。orchestrator は不要。 |

同じ domain/repo に対して両モードを同時に実行しては **いけません**。
orchestrator-message モードでは、実装/レビューの定期タイマーループも起動しないでください。
2 つのドライバーが同じ GitHub 状態を奪い合ってしまいます。

## スケジュールされた orchestrator のケイデンス

orchestrator-message モードの通常の定常状態は **メッセージ駆動** です:
implementation/review receiver はすでに accepted/progress/completed/blocked の
返信を orchestrator に送っており、その返信が orchestrator を起こすため、高頻度の
ポーリングは **不要** です。orchestrator タイマーは引き続き **サポート** されますが、
メッセージ駆動の wake の代わりにスケジュールされたポーリングを明示的に望むオペレーター
向けの **fallback/legacy** ポーリングオプションとしてのみです。いずれの場合も、
実装/レビュースレッドは長命ですが **ループを持たない受信側（loopless receiver）** であり、
orchestrator が委譲したときだけ動作し、同じ domain/repo に対して自分の定期タイマーを
起動しません。メッセージ駆動の定常状態に RECOMMENDED なデフォルトのセーフティネットは、
高速な orchestrator ループではなく、30 分クラスの
[design-thread watchdog](#design-thread-watchdog推奨されるセーフティネット) です。

明示的な fallback/legacy タイマーを使う場合、orchestrator のスケジュール方法は次の
2 通りです:

- **Codex automation（5 分ごと・任意）** — 起動ごとに 1 回の orchestrator wake を実行:
  設計進捗と返信を確認し、intent-cli に状態を問い合わせ、GitHub の事実を検証し、
  最大 1 通だけメッセージを送って終了する。
- **Claude 同一スレッド `/loop 5m`（任意）** — orchestrator スレッドで `/loop 5m` を実行し、
  同じスレッドが 5 分ごとに 1 パスずつ再起動する。

実装/レビュースレッドでは `/loop` や Codex automation を **同時に実行しないでください** —
orchestrator がメッセージ駆動で動作する場合でも fallback/legacy タイマーを使う場合でも、
これらは loopless receiver です。

### 各 orchestrator wake

権威ある wake プロンプトは intent-cli から生成します。wake は implementation/review
からの agmsg 返信の到着（メッセージ駆動の定常状態）か、任意の fallback/legacy タイマーの
発火のいずれかによってトリガーされます — どちらのトリガーでも 1 パスだけ実行します:

- 設計側の進捗を確認（新しい packet/issue、intent status の変化）。
- 保留中の agmsg 返信を読む（シグナルのみ — intent-cli / GitHub に対して再検証）。
- intent-cli に worker 状態を問い合わせる（`worker next-action --github-only`）。
- host レビュー準備状況を確認（`automation host-review-preflight`）。
- GitHub の事実を直接検証: open PR、CI 結論、承認、マージ状態、closeout/label 状態。
- 停滞ブロッカーと無返信の receiver を検知する。
- **publish と delegate は SAME WAKE で行う（G524）。** この wake で next-slice issue を
  公開した場合、存在を検証したうえで、その **同じ wake の中で** implementation
  スレッドへ delegate する — delegate をスケジュールされていない「次の wake」に
  先送りしてはいけない。他に何もそれをトリガーしないためです（フィールドトレースでは
  4 slice にまたがり合計約 60 時間という、測定された中で最大の stall class でした）。
- **1 wake あたりの上限は「receiver ごとに最大 1 件の delegation」であり、
  「最大 1 メッセージ」ではありません（G524）。** 1 回の wake に、publish とその
  同一 wake 内 delegation、停滞している receiver ごとに 1 通の repair メッセージ、
  1 件のオペレーターエスカレーション、保留中の receiver report への対応が
  すべて含まれてよい。
- **notify が受信者を検証する（G524/G578）。** workflow message は
  `intent-cli notify` だけで送り、active transport の role source に無い id や
  unavailable receiver は named cause と non-zero で fail closed にする。role 名を
  推測したり、handwritten transport call で検証を迂回したりしない（旧経路では、登録済み
  `reviewer` に対して `review` と誤指定したメッセージが静かに失われました）。
- **stalled-work チェックで wake を終える（G523/G524）。**
  `intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json`
  を実行し、報告されたすべての actionable item を眠りにつく前に処理する — wake が
  actionable な transition をスケジュールされていない次の wake に残したまま終わる
  ことは決してない。オペレーターの判断に本当にブロックされている場合は、黙って
  先送りせず明示的にエスカレーションする。

### repair と escalate

- **repair**: ルーチンな脱線状態は、適切なスレッドへメッセージを送って公式の intent-cli
  ワークフローに戻すことで自分で修復する — 停滞した receiver、`worker complete` を飛ばした、
  label を手動適用した、返信がない、など。ルーチンな復旧は repair メッセージであり
  エスカレーションではない。
- **escalate**: オペレーターへのエスカレーションは次の場合のみ — プロダクト/設計判断、
  認証情報やセキュリティ、破壊的なローカル操作、または解決不能な canonical な曖昧さ
  （intent-cli/GitHub の事実が本当に矛盾するか欠落している）。

### CI 待ち状態

pending/running の CI を持つ PR は **アクティブな待ち状態** であり、ブロッカーでは
ありません。GitHub checks が権威です。以下の mode ごとに名前を付けた再確認 producer を使います。pending な CI
はそれ単独では request-update label、repair メッセージ、オペレーターへの質問を引き起こしません。
review / merge / closeout を委譲する直前には必ず必須チェックを再検証してください — 以前読んだ
green は古くなっている可能性があります。

- **timer-loop** — 設定済みの定期タイマーが exact-head CI の再確認を発生させます。timer-loop の
  挙動は変更しません。
- **herdr-only** — pending CI で yield する前に、
  `gh pr checks <pr> --repo <owner/repo> --watch` で exact-head の CI-completion watch を明示的に
  有効化します。intent-cli の外側の controller が watch を所有します。terminal に達したら、team の
  logical-role mapping から解決した pane ID の recorded orchestration role を起動を促します。pane ID を
  固定指定してはいけません。intent-cli はこの background process を起動も管理もしません。この
  wake が示すのは待ちが終わったことだけです。成功か失敗かを判定するため、`stalled-work` と
  exact-head の GitHub facts を再読します。
- **agmsg orchestrator-message** — 明示的に設定した fallback orchestrator timer が再確認を発生
  させられます。それがない場合は、同じ exact-head `gh pr checks ... --watch` surface を有効化します。
  receiver report だけでは CI 完了の証明になりません。

- **pending / running** — 上で名前を付けた producer を使って待つ。メッセージなし、request-update なし、
  オペレーター質問なし。PR を in-flight として追跡し、先へ進む。
- **green** — すべての必須チェックが通過。intent-cli review surface 経由で review/closeout を
  委譲する。委譲時に green を再検証する。
- **red** — 必須チェックが失敗。所有権でルーティング: 実装スレッドが直せる test/build/lint の
  失敗には 1 通の repair メッセージ。プロダクト/設計や canonical 判断が必要なものはエスカレーション。
  必須チェックが red の間は merge/closeout を委譲しない。
- **stuck / ambiguous** — チェックが開始されない、妥当な時間を大きく超えてハングする、または
  矛盾/不明なステータスを報告する。1 件のオペレーター判断にエスカレーション（fail closed）。
  green を推測しない。

`intent-cli automation stalled-work` は、同じ PR について exact-head のチェックが 1 つでも実行中なら
informational な `ci-pending`、すべて terminal で失敗がなければ actionable な
`ci-all-green-not-transitioned`、terminal の失敗が 1 つでもあれば actionable な
`ci-failed-not-transitioned` を報告します。各 CI-aware item は `pr_head_sha`、
pass/fail/skip/pending の breakdown、kind + PR + head SHA による安定した `dedupe_key` を含みます。
この inventory は厳密に read-only であり、delegate、relabel、queue-state write を行いません。

G638 は永続的な **preview-through-1.x** wait record を追加します。check が終了したら、観測した
exact head と owed transition を記録します:
`intent-cli automation ci-wait record --domain <d> --repo <owner/repo> --pr <n> --head <sha> --transition <t> --write`。
これは polling loop ではなく、次の message-driven wake が扱う obligation です。`automation ci-wait show`
で読み取り、canonical な `automation pr-transition` が transition の適用後に消去します。G638 は別の
current head を actionable な `ci-head-moved` と命名しましたが、G657 は new head を advancing-repair evidence
として silent に限定します。old head の green または red check を current の結果とはみなしません。

`notify report` が running でない role の recorded pane を解決した場合、role と observed liveness を示す
advisory `recipient_warning` を出力したうえで、その pane に report を届けます。report は sleeping role が
起きるまで unread のままです。liveness だけを理由に report を拒否してはいけません。

## next-slice の publish

ルーチンな next-slice issue の publish は **orchestrator の責務** であり、オペレーターへの
質問ではありません。intent-cli が候補を `issue-cut-ready` と報告し、すべての安全ゲートを
通過したら、orchestrator はオペレーターに GitHub issue 作成を依頼して止まるのではなく、
canonical な intent-cli コマンドで自分で公開します。**1 wake につき最大 1 件** で
公開し、検証したうえで、**同じ wake の中で** その issue を implementation へ
委譲します（G524）— publish と delegate は一緒に完了させ、delegate を
スケジュールされていない次の wake に先送りしてはいけません。

次の **すべて** が成り立つときのみ公開します:

- same-domain コンテキスト、または明示的にルーティングされた multi-domain 委譲
  （明示ルーティングなしに cross-domain 候補を公開しない）;
- packet contract が完全（必須セクションの欠落なし）;
- open な clarification や contract の曖昧さがない;
- 依存が満たされている — 未 cut の依存より先に公開しない;
- WIP 上限内;
- host-sync / preflight がクリーンで、対象 repo/domain が一意。

それ以外は **hold またはエスカレーション** — 必須セクションの欠落、open clarification、
依存の不一致、WIP 上限到達、host-sync ブロッカー、対象 repo/domain の曖昧さはすべて
ブロッカーです。

publish は canonical な surface のみ — `intent-cli issue publish-flow` と
`intent-cli automation issue-publish` — を使い、生の `gh issue create` や
`gh ... --add-label` は使いません。publish 後は intent-cli / GitHub（チャットではなく）で
issue が期待どおりの body と `intent-target` label を持つこと、永続状態がそれを
反映していることを検証し、**その同じ wake の中で** agmsg で実装を委譲します（G524）—
公開した後で止まって将来の wake を待つことはしません。実装 receiver は依然として
`intent-cli worker next-action` からターゲットを得ます（agmsg テキストからではありません）。

## end-of-wake チェック（G523/G524）

すべての orchestrator wake は read-only な stalled-work チェックで終わります —
wake は、何もトリガーしないスケジュールされていない「次の wake」に actionable な
pending transition を残したまま終わってはいけません。これにより、測定された
publish-then-sleep と silent-completion の stall class を、タイマーを追加することなく
解消します。

```text
intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --format json
```

- **先送りしない** — このチェックが報告するすべての actionable item を、眠りにつく前に
  この wake の中で処理する（delegate、repair、または closeout へのルーティング）。
  明示的な fallback/legacy タイマーが実際にそれを実行するようスケジュールされていない
  限り、将来の wake のために作業を告知しない。メッセージ駆動の wake には、先送りした
  作業を拾い直す他のトリガーがありません。
- **先送りせずエスカレーションする** — item が本当に外部/オペレーターの判断に
  ブロックされている場合は、design スレッドのエスカレーションフィルターを通じて
  今すぐ明示的にエスカレーションする。黙って先送りしたり未処理のまま残したりしない。

## dispatch 検証（G524）

workflow message は `intent-cli notify` だけで送ります。agmsg adapter は team roster を、
herdr-only adapter は logical-role mapping と running agent/pane を検証してから配信します。
unknown role / unavailable receiver は named cause と non-zero を返し、配信済みとは決して
報告しません。再試行前に role registration/mapping を直し、role 名を推測・近似したり、
handwritten transport invocation で検証を迂回したりしてはいけません。

フィールドで観測された損失: 登録済みロールが `reviewer` であったにもかかわらず
`review` 宛に送られた 8 件の dispatch が、静かに失われました — agmsg は配信もせず、
不一致を報告もしませんでした。

## 依存の計画（dependency planning）

未充足の依存は、明示的かつ解決可能であれば **通常のオーケストレーション作業** であり、
オペレーターへの停止ではありません。次の候補が未完了の作業に依存している場合、orchestrator は
オペレーター判断のために止まらず、チェーンを決定論的に計画します — 依存元の候補を保留し、
この wake のアクションを **最も早い未充足の** same-domain（または明示ルーティングされた）依存に
向けます。

依存ステータスによるルーティング:

- **dependency-publish-ready** — 最も早い未充足依存が `issue-cut-ready` で GitHub issue が
  ない → この wake で publish（1 wake 1 件、next-slice publication ゲートに従う）。依存元は
  hold のまま。
- **dependency-actionable** — 依存にすでに issue または PR があり進められる → intent-cli /
  GitHub の事実を使ってルーティング（実装・レビュー・closeout・repair）。
- **dependency-waiting** — 依存が in flight（例: PR の CI が pending）→ CI wait state で名前を付けた
  mode-specific re-check producer を使って待つ。依存元は hold のまま。
- **dependency-ambiguous** — 決定論的に解決できない（依存 packet 欠落、GitHub linkage の矛盾、
  ルートマッピングのない cross-domain）→ 1 件のオペレーター判断にエスカレーション。
- **dependency-cycle** — 依存が循環している → エスカレーション（fail closed）。

依存元の候補は、すべての依存が完了/切り出されるまで保留されます。**エスカレーションは次の場合
のみ**: 依存 packet の欠落、依存の循環、ルートマッピングのない cross-domain 依存、GitHub
linkage の矛盾、破壊的な復旧、認証情報/セキュリティ、または人間のプロダクト/設計判断。

## stale-thread ヘルスチェック

receiver は loopless なので、**沈黙は曖昧** です — receiver は working、CI 待ち、
permission プロンプト待ち、blocked、返信なしで completed、または本当に stale かもしれません。
receiver がしきい値（デフォルト **30 分**、設定可能）を超えて返信しない場合、orchestrator は
**安全な** liveness チェックを行います: 行動する前に尋ね、権威ある事実を検証し、作業の自動
キャンセル・permission プロンプトの自動クリア・タスクの重複を決して行いません。

手順:

1. **非破壊的な status-request を 1 通** 送る — 尋ねるだけで、retry/cancel/リセットしない。
2. read-only の intent-cli / GitHub 事実を確認（`worker next-action`、issue/PR 状態、CI、label）。
3. 事実が進捗を示すなら（新規コミット、PR 更新、CI 実行中）**監視を続ける** — 作業を再送しない。
4. receiver が `waiting-permission` と返したら、それは **オペレーター通知** — surface する。
   プロンプトを自動クリアしない。
5. 繰り返しの no-reply **かつ** 進捗なしの後にのみ、同じ issue/PR を参照する
   **冪等な re-entry を最大 1 通** 送る。
6. 進捗のない沈黙が続く場合、または安全でないケース（cancel/reset、破壊的 git、認証情報）は
   エスカレーションする。

status-request は receiver に次のいずれかで返信するよう求めます: `working`、`waiting-ci`、
`waiting-permission`、`blocked`、`completed`、`idle`。ヘルスチェックは permission プロンプトの
クリア、作業の cancel/reset、label の変更、破壊的 git を決して行いません。（timer-loop モードは
影響を受けません — これは orchestrator-message の receiver にのみ適用されます。）

## 設計スレッドへのエスカレーションフィルター

**設計スレッド** が人間との主なコミュニケーション surface です。人間は主に設計スレッドと
やり取りし、実装とレビューは orchestrator 経由で動きます。設計スレッドに戻すのは
**人間が必要な** 判断のみです。これは **ノイズフィルターであり、失敗フィルターではありません** —
人間が必要な失敗を決して隠しません。

デフォルトで内部に留める（設計スレッドへ送らない）:

- 通常の進捗 / accepted / in-flight な委譲;
- CI 待ち（pending チェックはアクティブな待ち状態）。exact head が terminal になったとき、待ちの
  終了は正当な orchestration wake signal であり、pending wait として重複排除せず green または
  failed に分類する;
- 成功した実装（PR open、CI green）;
- 成功したレビュー / 承認;
- 承認済み PR の closeout;
- 実行可能な変化のない idle wake。

設計スレッドへエスカレーションするのは次の場合のみ:

- clarification が必要（issue/packet contract が曖昧）;
- プロダクト intent の曖昧さ、または設計判断;
- permission / 認証情報 / セキュリティ;
- 破壊的な操作が必要;
- 安全な stale-thread ヘルスチェック後の繰り返し no-reply / 無進捗;
- 未解決の canonical state（intent-cli / GitHub の事実が矛盾または欠落）;
- リリース / 公開 publish の判断;
- オペレーターが所有する明示的なポリシー判断。

設計エスカレーションは、簡潔な reason、intent-cli/GitHub から読んだ **現在の authoritative
state**、それを裏付ける evidence、必要なときだけの options、そして必要な正確な判断を運びます —
人間が state を再導出せずに判断できるようにします:

```json
{"to":"design","type":"escalation","ref":"issue#<n>|pr#<n>","reason":"<clarification|product-ambiguity|permission|destructive|no-progress|canonical-conflict|release|policy>","current_state":"<intent-cli/GitHub から読んだ現在の AUTHORITATIVE state: labels, PR/CI/review/merge 状態, queue 位置>","evidence":"<その state を establish する intent-cli/GitHub の事実>","options":"<任意: 候補の選択肢。役立つときのみ>","decision_needed":"<人間に求める正確な判断またはアクション>"}
```

- `reason` — どの人間が必要なカテゴリがエスカレーションを引き起こしたか。
- `current_state` — 現在の **authoritative** state。intent-cli / GitHub から読む（labels、
  PR/CI/review/merge 状態、queue 位置）。**必須** — 受信側が再導出する必要がないようにする。
  汎用的な evidence の文言は明示的な state の代替にならない。
- `evidence` — 現在の state を establish する intent-cli / GitHub の事実。
- `options` — **任意** の候補の選択肢。役立つときのみ含める。
- `decision_needed` — 人間に求める正確な判断またはアクション。

## delegation 前の workspace prerequisite (G655)

> **1.x を通じた preview。** この v0.12.0 freeze 後の guidance surface は 1.0 compatibility
> promise の対象外であり、1.x の間に変更・撤回される可能性があります。

prerequisite は receiver の privilege ではなく delegation とともに運ばれます。delegation の前に
orchestrator は task が必要とするすべての workspace prerequisite を識別し、境界付き receiver が
記録済み write envelope の外で worktree、checkout state、directory を作れるとは想定しません。

1. 必要な worktree、checkout、checkout state、directory を識別する。
2. prerequisite の各 write を、選択した recipe の記録済み write envelope (role-derived root) と
   比較する。receiver が書けない path は receiver work ではなく orchestrator 側の準備です。
3. orchestrator の権限で prerequisite を作成または修復する。worktree が必要なら既存の
   managed-worktree と safe-cleanup policy に従う。
4. 準備した cwd、checkout/branch state、managed-worktree registration、必要な writable directory
   を検証する。
5. 検証後にのみ、準備済み path と state を同じ logical task とともに委譲する。

> **prepare and resume。** receiver の permission failure は **routing signal であり、retry target
> ではありません**。orchestrator が不足する prerequisite を準備・検証し、準備済み path から
> **同じ PR と同じ logical task** を再開します。receiver の envelope は境界付きのままにし、
> recovery が unattended に seat kind を変更しないという G630 の rule も変更しません。

> **避けるべき手順:** receiver の記録済み write envelope では実行できない同一の failing step を
> 再委譲すること。failure loop、envelope の拡大、replacement PR/task の作成、workaround としての
> seat kind switch は行いません。

これは guidance-first orchestration であり、新しい command は追加しません。intent-cli は worktree
を作成・検証せず、git operation も実行しません。human/orchestrator が既存の権限で準備します。
worktree metadata failure と retry loop は transcript 付きで **remote-herdr team が 2026-08-08 に報告**
しました。Codex write-envelope asymmetry は別の事実として **MyIntentHost での 2026-08-07 の実測**
という attribution を維持します。

## managed worktree のクリーンアップ

オーケストレーション作業は実装・レビュー用の一時 worktree を作成します。ワークスペース内の
**管理された allowlist 済みルート** の下に割り当て、`git worktree remove` でクリーンアップします
— 任意の `/tmp/intent-review-...` パスを生の `rm -rf` で削除しては **いけません**。承認を無効化
するのではなく安全なクリーンアップ設計が正しいデフォルトです: 破壊的な `rm -rf` 承認プロンプトは
管理されていないワークスペースの症状です。

- **管理ルート** — `[project] worktree_root`（デフォルト `.intent-cli/worktrees/`、git-ignored）の
  下に割り当てる。任意の `/tmp` パスではない。`git worktree add .intent-cli/worktrees/<role>-<unit>
  <branch>` で role/unit ごとに 1 つ作成する。
- **安全なクリーンアップ** — `git worktree remove` でのみ削除（dirty な worktree は拒否される）。
  ターゲットが allowlist 済みルート内であること、登録済み git worktree（`git worktree list`）で
  あること、clean であることを検証してから `git worktree prune`。
- **クリーンアップを拒否する** — ターゲットが allowlist 済みルートの外、repo root / `$HOME` /
  システムパス、登録されていない worktree、または uncommitted/untracked のユーザー作業がある
  場合は停止して surface する。ユーザー作業を決して削除しない。
- **承認ポリシー** — `approval_policy=never` / `danger-full-access` は安全なクリーンアップ設計の
  **代替ではありません**。最小権限の承認をデフォルトに保ちます。目標は破壊的な `rm -rf` プロンプトを
  抑制することではなく、そもそも必要としないことです。

## review 委譲 — managed worktree と design alignment

review の委譲は managed-worktree ポリシーと design-alignment のエビデンス要求を **あらかじめ**
含んでいる必要があります — reviewer に発見させてはいけません。dogfooding では、reviewer が生の
`/tmp/...review...` worktree を割り当て、Codex が破壊的な `rm -rf` の承認を正しく求めるという
事例が見つかりました — これは **正しい** 安全動作ですが、**間違った** workflow です。修正は
managed root であり、承認設定を弱めることでは **ありません**。

- **managed worktree root** — review worktree は他のオーケストレーション作業と **同じ** managed・
  workspace-local root を使います — `[project] worktree_root`（デフォルト `.intent-cli/worktrees/`）、
  例: `.intent-cli/worktrees/review-<unit>` — 任意の `/tmp/...review...` パスは **決して** 使いません。
- **禁止パターン** — 生の `/tmp/...` review worktree と `rm -rf /tmp/... && git worktree add ...`
  のクリーンアップチェーンは、通常のパスとして **禁止** されます。このパターンに手が伸びたら、
  それは停止して managed root の下に割り当て直す合図です — オペレーターに `rm -rf` の承認を
  求める合図ではありません。
- **クリーンアップルール** — クリーンアップは **登録済みで clean な** worktree に対してのみ
  `git worktree remove <managed-path>` を使います（`git worktree list` と clean な `git status`
  でまず確認する）。
- **unsafe/stale パスのルール** — 登録済み git worktree ではない、managed root の外にある、
  または dirty/unsafe な stale パスは **決して** オペレーターの `rm -rf` 承認プロンプトには
  なりません — それは orchestrator への **structured blocker** agmsg 返信（`status: blocked`）
  であり、orchestrator が repair としてルーティングできるようにします。reviewer が unmanaged な
  パスを強制削除して解決するものではありません。

review 委譲の例（orchestrator → review）:

```json
{"delegate":{"domain":"<domain>","execution_unit":"<unit>","target_repo":"<owner/repo>","pr":"<n>","review_cwd":"/review/<domain>","managed_worktree_policy":"required — allocate under [project] worktree_root (default .intent-cli/worktrees/), never /tmp","design_alignment_required":true,"destination_thread":"review@<domain>"}}
```

review の `completed` 返信は design-alignment のエビデンスを含む必要があります:

```json
{"status":"completed","thread":"review","ref":"pr#<n>","note":"approved; closeout done","design_alignment_checked":true,"design_alignment_sources_checked":["packet","review-context","intent-tree","adr-decision-notes","relevant-docs"],"managed_worktree_policy":"compliant — .intent-cli/worktrees/review-<unit>, removed after review"}
```

review 返信がチェック済みとして挙げられる design-alignment ソース: **packet**（内容と受け入れ基準）、
その PR/unit の **review-context** artifact、関連する **intent tree** のエントリ、リンクされた
**ADR / decision notes**、変更が触れる **関連 docs**。

**review-incomplete ルール:** `design_alignment_checked: true` とチェック済みソースのリストを
省いた review の `completed` 返信は **incomplete** です — orchestrator はその返信だけでは
merge/closeout をルーティングしません。唯一の例外は、権威ある **事前の** approval state が
すでに同等の design-alignment review を証明している場合です（orchestrator はその具体的な
事前エビデンスを指し示す必要があり、同等性を仮定してはいけません）。

## receiver の準備状態（readiness）

monitor の設定だけでは **不十分** です。team 登録 + delivery mode 設定があっても、receiver が
メッセージを見られるとは **限りません** — 新しく起動/再起動したセッションは、その monitor/watch
パスがアクティブになる前に送られたメッセージを拾わないことがあります。実際の作業を送る前に、
各 receiver が ping/ack で **ready** であることを確認してください。

### 起動順序（startup order）

次の順序を厳密に守ってください — send は delivery ではありません:

1. 3 つのロールを team に join する（`join.sh`）。
2. 各ロールの delivery mode を設定する（`delivery.sh set`）。
3. receiver の CLI セッション（implementation・review・orchestrator）を起動/再起動する。
4. 何かを送る前に、各 receiver セッションで monitor/bridge がアタッチされるのを待つ。
5. セッションがアクティブになった **後** にのみ各 receiver へ ping を送る。
6. 進む前に ack を要求する — または `inbox.sh` で受信を手動確認する。
7. その後にのみ最初の実際の委譲を送る。

> **send-before-ready:** receiver が ready になる前に送ったメッセージは agmsg history に保存されて
> いても、新しく起動/再起動したセッションには **可視に配信されない** ことがあります。ack の
> ないメッセージは **receiver-not-ready** であり、成功した委譲ではありません。ack 後に resend するか、
> receiver に `inbox.sh` で queue を読ませて復旧します。

receiver が initial メッセージ送信 **後** に起動された場合に送る、貼り付け可能なオペレーター
メッセージ:

```text
Heads up: your session started AFTER I sent earlier messages, so they may be in agmsg history but not visibly delivered to you. Read your queue now with `inbox.sh` to catch anything you missed. Any prior unacked message is receiver-not-ready (NOT a delegation you must act on) — reply `ack` to this ping and I will (re)send the current delegation.
```

readiness 状態:

- **registered** — ロールが team に参加した（`team.sh` に表示される）。
- **delivery-configured** — delivery mode が設定済み（`delivery.sh status`）。
- **watcher-alive** — そのロールの monitor/watch プロセスが動いている。
- **receiver-session-active** — 起動/再起動した receiver セッションが実際に monitor パスに
  アタッチされている（delivery がアクティブになる前に開始したセッションは earlier なメッセージを
  受け取らないことがある）。
- **ping-acknowledged** — receiver が ping に返信した。チャネルが end-to-end で動く唯一の証拠。

実際の委譲の前に orchestrator・implementer・reviewer に対して **ping/ack が必須** であり、
起動/再起動のたびに再実行します。ack がなければ **not-ready** — 実際の作業を送らない。receiver が
ready でなかった場合、earlier に送ったメッセージは missed の可能性があります: ack 後に resend するか、
`inbox.sh` で queue を読む。先に `team.sh` と `delivery.sh status` を再確認します。

境界:

- **`watch.sh`** はロールの inbox をライブにストリームしますが **ターミナルを占有** します —
  debug/fallback オプションであり、デフォルトのセットアップ要件ではありません。通常は monitor
  delivery hook が標準パスです。
- **Codex Desktop app のスレッドはデフォルトで agmsg monitor receiver ではありません** — CLI
  セッションとは別の実行 surface です。receiver には CLI セッションを使う（または `inbox.sh` で
  読む）。

診断は agmsg スクリプトのみ: `team.sh`（登録）、`delivery.sh status`（delivery）、`inbox.sh`
（queue 済みメッセージ）、`send.sh`（ping → ack）。

## design / human receiver（任意）

人間が必要なエスカレーションを agmsg で配信したい場合、**4 つ目の論理ロール** として
**design / human receiver** を追加します。ルーチンな進捗は orchestrator / implementation /
review の内部に留まり、人間が必要な判断のみが design スレッドに行きます（design-thread
エスカレーションフィルター参照）。design receiver はルーチン運用には **任意** ですが、
エスカレーションが確実に人間に届くよう **推奨** され、inbox を確認して手動で受け取れます。

design receiving を有効にしたときの 4 つの論理ロール:

- **orchestrator** — agmsg 経由で他のロールをペース配分する。既定はメッセージ駆動で、
  明示的なタイマーは fallback/legacy オプションとしてのみ使う。
- **implementation receiver** — loopless。委譲にのみ反応する。
- **review receiver** — loopless。委譲にのみ反応する。
- **design / human receiver** — 任意。人間が必要なエスカレーションのみを受け取り、これも
  loopless（人間がオンデマンドで、例えば `inbox.sh` で読む）。

セットアップ:

- design ロールを **同じ** agmsg team に登録する —
  `agmsg join.sh <team> design <agent> <design-folder>` — または既存の design スレッドへ
  エスカレーションメッセージを宛先指定する。
- 任意のストリーミング delivery: `agmsg delivery.sh set <mode> <agent> <design-folder>`。
  そうでなければ design スレッドは `inbox.sh` でオンデマンドに読む。
- design receiver は定期ループ不要 — implementation/review と同様 loopless で、人間が促されたときに読む。

最小の手動 inbox トリガープロンプト（design スレッドに貼り付け）:

```text
agmsg の inbox を確認してください。あなたは `<team>` の design です。 (Check your agmsg inbox — you are the `design` role of team `<team>`. Read pending escalations with `inbox.sh`; routine progress is intentionally not sent here.)
```

> **pre-start メッセージ:** design receiver の monitor が起動する前に送られたメッセージは
> agmsg history にあっても可視に配信されないことがあります — design スレッドは `inbox.sh`
> で inbox を読んで earlier なエスカレーションを拾うべきです。他の receiver と同様です
> （Receiver readiness / startup order 参照）。

## セットアップ intake フォーム

ユーザーが「orchestrator モードを使いたい」とだけ言った場合、セットアップ事実を引き出すか推論し、
その後に具体的なコマンド/メッセージを生成します。不足分を尋ね、残りは推奨デフォルトを適用します。

尋ねる / 推論する: domain と target repo、orchestrator の cwd + agent type、implementation
receiver の cwd + agent type、review receiver の cwd + agent type、design の cwd + agent type
（design が manual-inbox か monitored か）、ロールごとの delivery mode。

入力が不完全なときの推奨デフォルト:

- orchestrator = operator-chosen herdr-startable kind
- implementer = operator-chosen herdr-startable kind
- reviewer = operator-chosen herdr-startable kind
- design = manual-inbox または monitored。operator が選ぶ herdr-startable kind を使う
- runtime / implementation / review receivers = monitor（サポートされる場合）

これは product の組み合わせではなく logical role の既定値です。新しい configuration の既定値は
`implementation`、`review`、`interview`、`clarify` です。既存で明示した role mapping は、この
互換性を保つ migration の間もそのまま有効です。

design は manual-inbox receiver（オンデマンドに `inbox.sh` で読む）でも monitored receiver でも
構いませんが、いずれにせよ受け取るのは **人間が必要な** エスカレーションか明示的なサマリーのみで、
ルーチンな進捗は受け取りません。

ロール起動メッセージ — agmsg ロールを引き受け、その後そのロールのプロンプトを貼り付ける:

- **Claude**: `/agmsg actas <role>`（スラッシュコマンド）
- **Codex**: `$agmsg actas <role>`

## design ハンドオフ（start / resume）

セットアップはロール登録で終わりません。agmsg ロールが登録されて ready になった後、**design
スレッド** が orchestrator に **1 通** メッセージを送ってオーケストレーションを start（または
resume）します。その後 orchestrator がループを自律的に駆動し、人間が必要な判断のときだけ design に
戻ります。

最初のメッセージ — design → orchestrator（design スレッドに貼り付け）:

```json
{"to":"orchestrator","type":"start","domain":"<domain>","target_repo":"<owner/repo>","requested_action":"<例: 次の ready なスライスを publish して PR まで進める>","constraints":"one action per wake; escalate to design ONLY for human decisions (product/clarification, release/credentials/security, destructive actions, unresolved blockers)"}
```

- **autonomous publish** — `intent-cli` が次のスライスを `issue-cut-ready` と報告し、すべての
  publish ゲートが通れば、orchestrator は canonical な intent-cli コマンド（`issue publish-flow` /
  `automation issue-publish`）で **1 つ** の GitHub issue を自分で作成・公開します — 各ステップを
  design に頼みません。1 wake につき最大 1 件。委譲前に検証します。
- **escalation boundary** — ルーチンな委譲（publish、delegate、CI 待ち、review、closeout）は
  orchestrator↔receivers に留まります。**design** に戻すのは人間が必要な判断のときだけ（product/設計
  clarification、release/認証情報/セキュリティ、破壊的操作、未解決ブロッカー）。構造化された
  エスカレーションメッセージを使います。
- **design inbox workflow** — design スレッドは loopless receiver でオンデマンドに読みます。
  エスカレーションを拾うには `inbox.sh` で design inbox を確認します — 特に monitor delivery が
  ライブで現れなかった場合や、design セッションが orchestrator の送信後に開始した場合。

## design-thread watchdog（推奨されるセーフティネット）

メッセージ駆動の定常状態では、implementation/review の返信がすでに orchestrator を
起こしているため、高速な orchestrator ループは冗長です — しかし、メッセージ駆動の
経路自身が自己報告できないスタールは、依然として何かが検知しなければなりません。
**RECOMMENDED なデフォルト** のセーフティネット(G539。G526 の外部スケジューラ推奨を
置換します)は、**design** スレッドから実行する **30 分クラス** の間隔の
watchdog loop です: `intent-cli automation heartbeat` を唯一の scheduler-agnostic な
decision surface として呼び出します。各 valid result は `healthy-active-wait`、
`actionable-stall`、`operator-required`、`cannot-determine` のちょうど一つの verdict を持ちます。
外部 loop が cadence、watermark、dedupe persistence を所有し、intent-cli は schedule、sleep、send、
poll state の永続化をしません。生きた、人間が監視しているエージェント
セッションの **内側** で動作するため、見えない外部プロセスとは異なり、別途の
credential/keychain セットアップも不要で(セッションの他の部分と同じ方法で
認証します)、壊れた瞬間にオペレーターの画面上で可視化されます。

- **頻度** — 30 分クラス(例: 30 分ごと): 邪魔にならないほど静かで、フィールドトライアル
  で実測されたスタールを大きく下回る上限に収まるだけの頻度です。高速な watchdog
  ループはメッセージ駆動モデルが取り除いたのと同じチャーンを再現してしまいます。
- **loop setup プロンプト**(design スレッドに貼り付ける)— design スレッドで
  `/loop 30m`(Claude 同一スレッド)、または 30 分ごとに発火する Codex automation を
  実行します。プロンプトは各 wake で
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --team <team> --format json`
  を実行します。外部 dedupe は age や poll time ではなく返された `dedupe_key` を key にします。
  `healthy-active-wait` では何も送らず、result が示す awaited condition、observable end signal、有限の
  bound に従って signal または bound 超過時に再評価します。`actionable-stall` では返された canonical
  `intent-cli notify` command をその key に対して最大 **1 回** 実行します。`operator-required` は named
  record と action を operator に提示し、orchestration を促しません。`cannot-determine` は named
  monitor/routing failure を可視化して repair またはエスカレーションし、healthy/silent として扱いません。
  heartbeat コマンドの実行失敗や不正な/オブジェクトでない出力は
  **決して沈黙しません**: この wake 自身の turn 出力で、生きたこのセッションを
  監視しているオペレーターに見える形で、失敗を明示的に述べます — これこそが、
  廃止された見えない外部スケジューラに対して in-session の watchdog が持つ
  正確な優位性です(下記 Retired を参照)— その一方で、壊れた入力から notify の
  nudge を捏造・送信することは決してありません。送信する nudge は
  `actionable-stall` verdict とともに返された canonical notify command の場合だけです。
  `stale` や `message_body` から action を推論せず、closed verdict だけを decision trigger にします。
- **failure visibility** — 沈黙は `healthy-active-wait` の heartbeat 結果にのみ
  許されます。heartbeat コマンドの実行失敗や不正な/オブジェクトでない出力は、
  この wake の watchdog 自身の turn 出力で **可視的に** 表面化させなければ
  なりません — 決して黙って飲み込んだり、黙ってリトライしたりしません。沈黙した
  失敗こそが、このスライスが外部 OS スケジューラを retire する理由そのものだから
  です — その一方で、壊れた入力から notify の nudge を捏造・送信することは決して
  ありません。送信する nudge は `actionable-stall` verdict とともに返された
  canonical notify command の場合だけです。
- **チェック内容** — design/HITL inbox で未読の人間向けエスカレーションを確認
  (design ロールの `inbox.sh`)、read-only な intent-cli/GitHub の事実
  (`worker next-action --github-only`、open PR/CI/label 状態)を最後に確認した
  orchestrator の活動と比較して orchestrator の停滞を確認、そして RECOMMENDED な
  primary チェックとして `intent-cli automation heartbeat` 自体 — これは
  `automation stalled-work`(G523)、judgment-wait、recorded topology を一つの verdict、
  evidence/age basis、stable dedupe key、owner、canonical notify command に統合します。
- **アクション** — その一つの verdict だけに従います: named signal を bound 内で待ち、
  `actionable-stall` key には返された canonical notify command を最大 1 回実行し、
  `operator-required` は human に経路指定し、`cannot-determine` は可視化して repair/エスカレーションします。
  他の field から action を推論せず、hand-written transport request を送ってはいけません。

  ```json
  {"type":"status-request","to":"orchestrator","from":"design-watchdog","ask":"non-destructive liveness check: reply with current state and next action, or confirm idle"}
  ```

- **停止条件** — backlog と human-decision(HITL)キューの両方がなくなったら watchdog を
  停止またはアーカイブします。

watchdog の安全ルールは次を **禁止** します(変更なし・逐語的に維持):

- 重複した delegation — watchdog は自分で delegation を再送・再作成しません。
  delegation するのは orchestrator だけです。
- permission プロンプトのクリア — `waiting-permission` は引き続きオペレーター向けの
  通知であり、watchdog が自動でクリアすることはありません。
- 進行中の作業のキャンセルやリセット。
- issue/PR やその他の終端アクションの強制クローズ。
- 推測的な永続状態の手術 — label、queue-state、ホストメタデータの手編集は
  行いません。

明示的な orchestrator タイマー(Codex automation 5 分ごと、または Claude 同一スレッド
`/loop 5m`)は、オペレーターがメッセージ駆動の定常状態の代わりにスケジュールされた
ポーリングを明示的に望む場合の fallback/legacy ポーリングとして引き続き **サポート**
されます — 実測された弱点: これはオペレーターが明示的に望んでいない定常状態での高速
ポーリングであり、まさにこれが design-thread watchdog が推奨されるデフォルトになった
理由です。design-thread watchdog(推奨)、orchestrator-side の長間隔 automation
(alternative、下記参照)、5 分の orchestrator fallback タイマー(legacy/discouraged)
は代替のセーフティネットであり、すべてを同時に必要とするわけではありません。

**実測された弱点** — フィールドトライアル(2026-06-28..07-14): この watchdog が動作する
design session 自体が 16 日間で 8〜9 回死に、その monitor は手動で復旧するまで死んだ
ままでした。いくつかのスタールは、そのセッションがたまたま再起動したときにしか
発見されませんでした。これは引き続き考慮すべき既知の限界ですが、G539 のフィールド
エビデンス(2026-07-15..07-20)は、代替案である、セッションに依存しない外部 OS
スケジューラの方が **厳密に悪い** ことを示しました: 5 日間連続して **すべての実行が
サイレントに失敗** しました(credential-store access が原因。下記の Retired を参照)
— これは、目に見える形で死んで、オペレーターに再起動される session とは対照的です。
時折再起動するが壊れたときには可視である watchdog は、オペレーターがログをたまたま
確認するまで見えないまま動作するものより強い保証です。

## orchestrator-side の長間隔 automation(代替のセーフティネット)

design-thread watchdog に対する **選択可能な alternative** です: 同じ
`intent-cli automation heartbeat` の呼び出しを、design スレッドではなく
**orchestrator 自身のスレッド** の中で、長間隔の automation(Codex automation または
Claude 同一スレッド `/loop`)から直接実行します。各 wake で `automation heartbeat`
を自分自身で呼び出し、その closed verdict に従って **同じ** wake の中で行動します
— design から orchestrator へのメッセージのホップは発生しません。なぜなら
orchestrator 自身がそのチェックを実行しているからです。

- **頻度** — 30〜60 分クラス — 推奨される design-thread watchdog と同じ低頻度帯で
  あり、高速な 5 分の fallback タイマーとは異なります。
- **トレードオフ** — design-side(推奨)は、orchestrator を厳密に loopless に
  保ちます — inbound な agmsg メッセージからしか起きず、通常のメッセージ駆動モデルと
  一致しますが、その代償として 1 つの追加ホップ(design watchdog から orchestrator
  へ)が発生します。orchestrator-side の automation はそのホップを取り除きます
  (orchestrator が自身の heartbeat チェックに対して直接起きて行動する)が、
  orchestrator 自身が定期ループを実行する必要があります — これは orchestrator-message
  モードが定常状態で避けるよう設計されているまさにそのパターンです。orchestrator-side
  を選ぶのは、オペレーターが orchestrator を loopless に保つことよりも 1 ホップ少ない
  ことを優先する特定の理由がある場合だけにしてください。
- **コマンド** —
  `intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --team <team> --format json`
- **setup プロンプト**(orchestrator スレッドに貼り付ける)— orchestrator スレッドの
  中で、30〜60 分ごとに発火する Codex automation または Claude 同一スレッド `/loop`
  を実行します。各 wake は通常の orchestrator wake チェックに加えて
  `automation heartbeat` を実行します。`healthy-active-wait` は待機し、
  `actionable-stall` だけが返された canonical notify command を実行し、
  `operator-required` は operator に経路指定し、`cannot-determine` は可視化して
  repair/エスカレーションします（G524 の wake contract に従い、dedupe key ごとに最大 1 nudge）。
  `stale` や `message_body` から action を推論しません。

### Retired: 外部 OS スケジューラの heartbeat(G526 → G539)

**Retired。** G526 が追加した外部 cron/launchd の OS スケジューラ推奨は廃止
されました。理由:

1. **credential-store access** — ラッパーの `gh`/agmsg 認証は、多くの場合ログイン
   keychain に存在し、cron ジョブはそこにアクセスできません。そのため、ロジックでは
   なく認証情報のステップで失敗します。
2. **invisible failure** — 失敗した cron の実行は誰も見ない OS ログに書き込まれる
   だけなので、実際にはセーフティネットとして機能していません。
3. **outside the agmsg model** — intent-cli は agmsg を経由して調整し、
   自分自身のスレッドを持ちません。OS スケジューラはそのモデルの完全に外側に
   位置します。

**フィールドエビデンス**: インストール(2026-07-15)から 2026-07-20 まで — 5 日間
連続して — すべての実行がサイレントに失敗し、2026-07-20 の 105 分のスタール
(G538 / PR #1179)は、`automation stalled-work` が正しく検知した
(`pr-created-not-reviewing, age=105m`)にもかかわらず回復されませんでした。
人間による ping だけがそれを表面化させました。

`intent-cli automation heartbeat` 自体は **変更なし** であり、引き続き
scheduler-agnostic です — cron を含む任意のスケジューラが引き続き呼び出せます —
ガイドが外部 OS スケジューラをメカニズムとして **推奨** しなくなっただけです。

## monitor リカバリ

- **monitor が起動しなかった** — receiver セッションを再起動して monitor/watch hook を新しい turn で
  アタッチさせる。`delivery.sh status` と ping/ack で検証。それまでは `inbox.sh` で読む。
- **メッセージが可視でない** — queue 済みだがライブ配信されていない可能性。ロールの queue を
  `inbox.sh` で読み、`team.sh` / `delivery.sh status` を再確認し、ack 後に resend する。
- **メッセージ送信後に receiver が開始した** — earlier なメッセージは history にあるがライブ配信
  されない。`inbox.sh` で読むか、receiver の ack 後に resend する。
- **packet があるのに orchestrator が idle** — orchestrator が design の start/resume メッセージを
  受信したか（`inbox.sh`）、`worker next-action` / `intent status` が **この** domain/repo に対して
  実行可能項目を報告しているか（host repo に見える別ドメインではない）を確認する。issue-cut-ready で
  安全なら、orchestrator は待たずに自分で 1 つ issue を publish すべき。
- **`mode=monitor` だがライブストリームがない** — `delivery.sh status` `mode=monitor` は設定にすぎず、
  Claude Code `Monitor` が接続されている証明ではない。ライブ attach の success marker（`1 monitor` /
  `Monitor event`）を検証し、Windows では Git Bash 起動を確認し、bounded なフォールバック段階手順
  （再起動 → trust 検証 → Windows で Git Bash → 既知の正常環境と比較 → `turn`/手動 `inbox.sh` または
  エスカレーション）を実施する。完全なチェックリスト:
  [orchestrator-message モード — Monitor ツールと delivery-mode の違い](orchestrator-message-mode.md)。
- **`ToolSearch select:Monitor` が Monitor ツールを一切見つけられない** — これは `mode=monitor` の
  状態に関わらず、agmsg の問題である *前に* Claude Code の tool-surface の問題。`.claude/settings.json` /
  `.claude/settings.local.json` / `~/.claude.json` を既知の正常フォルダと比較し、疑わしい project レベルの
  `env` オーバーライド（例: `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`）を削除する（agmsg hooks は保持）。
  その後、再起動して再検証する。参照:
  [Monitor が見つからない場合の project-settings 診断](orchestrator-message-mode.md#monitor-が見つからない場合の-project-settings-診断)。

## Codex monitor（beta）の failure mode

agmsg の Codex monitor（beta）は、上記の Claude Code `Monitor` ツールとは別の
delivery backend であり、agmsg を Codex CLI セッションへブリッジします。
intent-cli は agmsg の内部実装を所有・変更しません。ここでは、オペレーターが
Codex receiver をセットアップする際に必要な情報と、フィールドで確認済みの
2 つの failure mode を認識・復旧する方法だけを扱います。内部実装については
[agmsg codex-monitor-beta doc](https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md)
を参照してください。

> agmsg 1.1.6 / Codex v0.144.1（macOS、`codex()` shim launch）で観測した内容です —
> 以下の setup preflight・healthy-state marker・トラブルシューティング項目は、
> その検証環境における観測にすぎず、永続的な bridge の契約ではありません。
> アップグレード後は、ここに書かれている具体的な挙動（リトライ間隔、thread の
> attach 順序など）を鵜呑みにする前に、インストール済みの agmsg/Codex の
> バージョンに対して再確認してください。

**setup preflight** — Codex receiver を起動する前に、(project, codex) のペアが
ちょうど 1 つの identity に解決されることを確認する: `whoami.sh <project> codex`
は `agent=` の行を 1 行だけ出力するはず。まず古い登録を掃除する（例えば別ロールを
そのプロジェクトに登録したままの `actas` の残骸）— identity が 2 つ以上あると
bridge launcher は無言でブロックされる。

**healthy-state marker:**

- `delivery.sh status` が `Codex bridge: <team>/<role> alive (pid N)` を表示する。
- bridge はセッション起動時ではなく、送信された **最初の turn** で arm する —
  その最初の turn より前に delivery を期待しない。
- 既に起動済みの Codex セッションは、bridge を有効化した後に再起動するまで
  監視対象外のままになる。

**トラブルシューティング:**

- **`mode: monitor` だが Codex bridge が一切起動しない** — (project, codex) の
  ペアが 2 つ以上の identity に解決されている。`codex-bridge-launcher.sh` は
  ちょうど 1 つのときのみ処理を進め、それ以外は 0.3 秒ごとに無言でリトライし
  続ける（例えば別ロールの `actas` の残骸）。`whoami.sh <project> codex` で
  identity 数を確認し、古い登録を削除してから再起動する。
- **bridge は alive（pid 表示あり）だが Codex の TUI が全く動かない/メッセージに
  反応しない** — 共有の Codex app-server はセッションをまたいで loaded thread を
  蓄積し、`codex-bridge.js` は `thread/loaded/list` の最初（最も古い）エントリに
  attach する — turn は古いバックグラウンド thread に注入され、可視の TUI は
  一切反応しない。復旧手順: TUI を終了し、app-server/bridge/launcher の
  プロセスを停止し、記録されている app-server/bridge の state ファイル
  （`codex-app-server.*.{pid,port,version}` と bridge の `{pid,appserver,meta}`
  ファイル）を削除し、codex を再起動してから 1 turn 送信して re-arm する。
- **再起動をまたいで 1 件のメッセージへの返信が二重に現れる** — bridge が
  二重になっている疑いがある。再起動前に `delivery.sh status` で bridge の
  pid が 1 つだけであることを確認する。

## design traffic-controller プレイブック

design スレッドは実装者ではなく **traffic controller**（交通整理役）として振る舞います。
orchestrator を通じて調整し、人間が必要な項目だけを表示します。

1. design inbox（`inbox.sh`）を確認し、orchestrator のエスカレーション/サマリーを読む。
2. intent-cli / GitHub の **read-only** state（`intent status`、`worker next-action`、PR/issue/
   labels）を確認して判断の根拠にする — agmsg メッセージを state として信用しない。
3. orchestrator に state 更新や nudge（start/resume）を送る。implementation/review を自分で
   駆動しない。
4. implementation/review の作業・labels・host メタデータを直接 **変更しない** — それは
   orchestrator/receivers が intent-cli 経由で行う仕事。
5. 人間が必要な項目 **だけ** を人間に要約する。ルーチンな進捗は内部に留める。
6. design judgment を待つことで進行が止まるなら、待つ前にその待ちを永続的に記録します:
   `--owner design` つきで judgment-wait を開き、record を照会し、回答を出したら
   evidence とともに解決します。回答済みで open のままの record は嘘であり、design handoff
   は完了していません。

**「orchestrator が idle に見える」診断**（エスカレーション前）: orchestrator がスケジュールされ
新しい turn にあるか確認。最後のメッセージを受信したか確認（`inbox.sh`）— pre-monitor の送信は
queue 済みでライブでない可能性があるので ack 後に resend。intent-cli が **この** domain/repo に
実行可能項目を報告しているか確認（idle が正しい場合もある）。その後にのみ人間にエスカレーション。

> **context-only:** design スレッドは receiver スレッドに context を送ってよいが、orchestrator が
> アクションを委譲していない限り `context-only: <text>` とマークしなければならない — receiver は
> design の context ではなく orchestrator の委譲にのみ反応する。

## preflight（3 つの cwd すべて）

何かを変更する前に、**3 つのチェックアウト全部**（orchestrator・implementation・review の cwd）を
事前確認します。receiver が誤った repo・誤ったブランチ・dirty なユーザー作業の上で動くのは、最も
よくあるオーケストレーション失敗です。

- 各 cwd で `git status` が clean であることを確認 — checkout/branch 切替で壊れる uncommitted/
  untracked 作業がないこと。
- 各 cwd の git remote がそのロールの **期待 repo** であることを確認（implementation/review receiver
  は委譲された target repo を指す必要がある）。
- 各 cwd が **期待ブランチ/base** にあることを確認 — stale なブランチ上の receiver は誤った base に
  対して実装する。
- チェックアウトが複数ドメインを露出している場合、orchestrator は publish/delegate の前に
  **要求された domain/target repo でフィルター** しなければならない（可視であることは権限ではない）。
- **既存ループ競合チェック** — この domain/repo に対して timer-loop が動いていないこと。
  orchestrator-message モードと timer-loop モードは同じルートで同時に動かしてはいけない。

## トラブルシューティング

- **receiver がメッセージを受信しない** — 登録（`team.sh`）と delivery（`delivery.sh status`）を確認。
  receiver が取りこぼした可能性がある（monitor 未アクティブ）— `inbox.sh` で queue を読むか、ping/ack 後に
  resend する。
- **monitor/delivery をセッション開始後に設定した** — monitor/watch パスがアクティブになる前に開始した
  セッションは earlier なメッセージをライブで拾わない。receiver セッションを再起動する（または `inbox.sh`
  で読む）。その後、委譲前に ping/ack で再確認する。
- **Codex Desktop app スレッドが receiver** — Codex Desktop app スレッドはデフォルトで agmsg monitor
  receiver ではない。手動でのみ受信する。CLI セッションを使うか、Desktop スレッドに `inbox.sh` で読ませる。
- **receiver の cwd が委譲と異なる repo/domain を見る** — 停止。取得しない。receiver の cwd/worktree・
  git remote・委譲された domain がルーティングと一致する必要がある。blocked を返して re-route する。
  execution-unit prefix の不一致だけでは signal にならない — packet/domain メタデータを比較する。
- **Codex が `rm -rf /tmp/...review...` の承認を求めてくる** — これは **正しい** 安全動作ですが
  **間違った** workflow です: review worktree が managed root ではなく unmanaged な `/tmp` パスに
  割り当てられています。修正は managed root（`.intent-cli/worktrees/review-<unit>`）であり、承認
  設定を弱めることでは **ありません** — managed root の下に再割り当てしてください
  （[review 委譲](#review-委譲--managed-worktree-と-design-alignment) 参照）。stale な `/tmp` パスの
  `rm -rf` は承認せず、代わりに orchestrator に blocked を返信して repair としてルーティングして
  もらいます。

## draft PR のレビュー可否

**draft PR は domain guidance によってはレビュー可能** です — domain の review policy が許す場合、
reviewer は draft に対してレビューフィードバックを行ってよいです。ただし reviewer は **canonical な
intent-cli review surface**（`review closeout-plan`、`guide review`、`automation pr-transition`、
`closeout pr`）を使わなければなりません。merge/approval はそれらの surface で判定され続けます。
draft が手作業や生の label 編集で approve/マージされることはなく、host メタデータ編集を経ることも
ありません。

## single-domain と multi-domain のオーケストレーション

host チェックアウトは正当に **複数** の intent ドメインを含み得ます（例:
`sekiban-as-a-service`、`sekiban-wasm-runtime`、`intent-cli`）。さらに
**複数のドメインが同じ GitHub リポジトリを対象** にすることもあります。可視であることは
権限ではありません。そのため orchestrator は次の 2 モードのいずれかで動作します。

### single-domain orchestrator

- 選択したドメインのみがスコープ内。
- 同じ host repo に **可視** な他ドメインの queue 項目は **スコープ外** — たとえ
  同じリポジトリを対象にしていても、publish / delegate / 修正してはいけません。
- 可視な他ドメイン項目を委譲可能と見なすのではなく、domain/mode を切り替えるよう
  オペレーターにエスカレーションします。

### multi-domain orchestrator

- 意図的に複数ドメインを調整します。
- publish / delegate / review / repair の前に、**各委譲ごとに明示的なルーティング
  メタデータ** を要求します。
- 各 execution unit を、そのドメインのチェックアウトを所有するスレッドにのみ
  ルーティングします。

すべての multi-domain 委譲は次を伴わなければなりません:

- domain
- execution unit
- target repo
- implementation cwd/worktree
- review cwd/worktree
- base branch policy
- destination thread

委譲ペイロードの例（1 つの repo が 2 つのドメインに供給している点に注意）:

```json
{"delegate":{"domain":"sekiban-as-a-service","execution_unit":"G491","target_repo":"J-Tech-Japan/intent-system","impl_cwd":"/work/sekiban-saas","review_cwd":"/review/sekiban-saas","base_branch_policy":"direct-main","destination_thread":"implementation@sekiban-as-a-service"}}
```

### execution-unit の prefix はルーティングシグナルではない

ドメイン名と異なる execution-unit ID の prefix（例: 番号がドメインを符号化していない
`G###` ユニット）は、それ **単独では** wrong-repo シグナルでは **ありません**。
所有権の判断には prefix 文字列ではなく **packet/domain メタデータ** と
**ルーティングコンテキスト** を比較してください。

## 実装スレッド: claim 前にチェックアウトを検証する

実装スレッドは orchestrator の委譲で駆動されますが、worker のターゲットは依然として
受信側の `intent-cli worker next-action --repo <owner/repo> --github-only` から来ます
— agmsg のテキストからでは **ありません**。claim する前に:

1. ローカルチェックアウトのコンテキスト — cwd/worktree、git remote repo、委譲された
   domain — が、渡されたルーティングと一致することを検証する。
2. チェックアウトが委譲された repo/domain と一致しない場合は、取得せずに
   **停止して blocked を返す**。
3. prefix の不一致だけでは wrong-repo シグナルにならないことを忘れない。所有権は
   packet/domain メタデータとルーティングコンテキストで確認する。

実装スレッドは **GitHub-contract-only** を維持します。host メタデータ
（`.intent-cli/**`、`intents/**`）を読んだり変更したりしません。すべての label 遷移は
`intent-cli worker` / `intent-cli automation` を経由します。

**orchestrator への completion または blocked の報告は、すべての delegation の
REQUIRED FINAL STEP です（G524）** — これは任意ではなく、orchestrator が自力で
silent completion を発見することはできません（orchestrator に報告が届かないまま
PR が開かれた場合、それは orchestrator の視点では失われた作業です — フィールドの
ある事例では、手動の GitHub チェックで発見されるまで 88 分間気づかれませんでした）。
完了時は正確に次の shape を送ってください:

```json
{"status":"completed","thread":"implementation","ref":"pr#<n>","note":"PR opened, Closes #<n>, CI green"}
```

または 1 件のオペレーターアクションを名指しする `blocked` の shape。同じ
required-final-step のルールは review スレッドにも適用され、その `completed` 返信は
さらに `design_alignment_checked` とチェック済みソースのリストを持ちます:

```json
{"status":"completed","thread":"review","ref":"pr#<n>","note":"approved; closeout done","design_alignment_checked":true,"design_alignment_sources_checked":["packet","review-context","intent-tree","adr-decision-notes","relevant-docs"]}
```

## セーフティ境界（まとめ）

- agmsg はシグナル層のみ。intent-cli と GitHub がすべてのワークフロー状態の権威。
- 生の label 変更は禁止。すべての遷移は intent-cli worker/automation を経由。
- queue-state、runs ログ、packet、host メタデータの手編集は禁止。
- agmsg はセマンティックレビューを置き換えず、マージを認可しない。
- ドメイン分離: 可視であることは権限ではない。single-domain orchestrator は他ドメイン
  項目を無視/エスカレーションし、multi-domain orchestrator は委譲ごとの明示的ルーティング
  を要求する。
- orchestrator の重複や、シグナルが intent-cli/GitHub の事実と矛盾する場合は fail closed —
  推測せず、停止してエスカレーションする。
- 1 wake あたりの上限は **receiver ごとに最大 1 件の delegation**（implementation、
  review）— 「最大 1 メッセージ」ではない。publish の同一 wake 内 delegation、repair
  メッセージ、1 件のエスカレーション、receiver report への対応は 1 回の wake に
  すべて含まれてよい（G524）。publish の delegation をスケジュールされていない
  将来の wake に先送りしない。
- workflow send は必ず `intent-cli notify` を使う。active transport の role source を
  検証し、unknown / unavailable recipient は fail closed にする（G524/G578）。
- すべての wake を stalled-work チェック（`automation stalled-work`、G523）で終え、
  眠りにつく前に actionable な item を処理する。黙って先送りせず、明示的に
  エスカレーションする。
