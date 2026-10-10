# コマンドリファレンス（agent 向け / パワーユーザー向け）

> 日本語版。English version: [`../en/08-command-reference.md`](../en/08-command-reference.md)

このページは AI agent やパワーユーザーが代理で実行する `intent-cli` コマンド群を示します。
通常の利用では記憶する必要はありません。
[ルート README](../../README.md) のクイックスタートと `intent-cli guide start` が
典型的なパスをカバーします。

以下のコマンドは AI agent が内部で実行するものです。現在の全カタログは
`intent-cli guide commands list --format json` を実行してください。

---

## 2 つの agent ロール

| ロール | source of truth | 責務 |
| --- | --- | --- |
| **Host / review agent** | 親 host の `.intent-cli/` 状態 + intent tree | issue 公開、`intent-target` 付与、review/approve/merge、next slice 切り出し、`intent-cli automation` 経由の label 遷移 |
| **Child implementation agent** | **GitHub の issue/PR + repo ローカルのコード**（host metadata ではない） | issue 契約の実装、PR の作成/更新、`intent-cli worker` での結果記録 |

Child implementation agent は **GitHub-contract-only** です: host の `.intent-cli/`、
queue-state、metadata branch、`intents/**` を読んだり変更したりしません。

host は **別の host リポジトリ** にも、**同じリポジトリの専用 metadata ブランチ**
（例: `main-metadata`）にも置くことができます。
詳しくは [プロジェクト開始 → リポジトリトポロジーの選択](02-project-start.md#リポジトリトポロジーの選択) を参照してください。

## seat の command-form guidance (G696)

インストール済み CLI から、seat kind ごとの実測済み command-form rule を参照できます。
これは read-only registry です。form と代替手段を表示しますが、seat settings、allowlist の判断、
command の approve は行いません。

```text
intent-cli guide seat-commands --kind claude --format markdown
intent-cli guide seat-commands --kind codex --format json
```

各 action は、operator が認めた literal な prefix と引数順を保ちます。prefix matching は、quoted
arguments、path より前に置いた flags、異なる prefix の command を `&&` で chain した形、`$VAR` の
expansion、`for` loop による wrapping で壊れることがあります。合成した形で prefix が変わる場合は、
sanctioned な各 step を分けて実行します。

実測された denied surface の代替は次です。

- `gh pr comment` → `gh pr review --body-file <review.md> --comment`。
- `git checkout <branch>` → `git fetch origin <branch>` の後に `git diff --check <base>...HEAD`。
- local `npm` または package-manager build → exact PR head SHA に紐づく CI evidence。

review seat は same-account verdict convention も使います。reviewer と PR author が同じ account の場合、
GitHub は `gh pr review --approve` を拒否します。body-file form で `COMMENTED` review を送信し、その後
canonical な `intent-cli notify report` command を実行します。workflow verdict は report が担います。
構造化された表示は `intent-cli guide review --format json` で確認できます。

role-facing route も構造化され、テストされています。`guide review`、`guide next --role review`、
`guide orchestrator-thread` は、それぞれ review seat 向けに `guide seat-commands` を明示します。
さらに、意図的な topology rebuild 用のインストール済み `guide topology-workspace-move` recipe も
これらの surface から到達できます。

## Orca mailbox lifecycle guidance（G853 — preview-through-1.x）

`guide bootstrap`、`guide onboarding`、`guide design-thread`、
`guide solo-conductor`、`guide steward-thread` は、共通の
`orca-mailbox-lifecycle/v1` contract を JSON / Markdown で公開します。記録済み team shape と
G837 binding health のみを projection し、installed capability / caller check、意図的な create/adopt、
canonical `record-orca-run` の dry-run/CAS write、recipient discovery、durable enqueue、bounded FIFO
receive/ACK、exact-request recovery を扱います。手順の詳細は
[agent-message orchestration lifecycle](12-agent-message-orchestration.md)
を参照してください。

Orca command を実行するのは agent です。`intent-cli` は command を render して host metadata を読むだけで、
Orca 起動、provider launch、binding mutation、mail transport、receive policy の schedule は行いません。
既存の `orca-push` / `inbox-pull`、binding-health cause、CAS behavior、canonical notify authority は維持します。

## topology workspace move (G697)

記録済み team を新しい herdr workspace へ意図的に rebuild するときは、最初にインストール済み
recipe を render します。これは read-only で、inspect → preview → apply → validate →
notify-preflight の正確な順序を示します。

```bash
intent-cli guide topology-workspace-move --domain <domain> --team <team> --format markdown
intent-cli session-layer topology show --domain <domain> --team <team> --format json
intent-cli session-layer topology move --domain <domain> --team <team> \
  --workspace-id <new-workspace-id> \
  --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... \
  --dry-run --format json
intent-cli session-layer topology move --domain <domain> --team <team> \
  --workspace-id <new-workspace-id> \
  --pane-map <old-pane>=<new-pane> [--pane-map <old-pane>=<new-pane>]... \
  [--current-digest <digest>] --write --format json
intent-cli session-layer topology validate --domain <domain> --team <team> [--live] --format json
intent-cli notify delegate --domain <domain> --team <team> --from <sender-role> \
  --to <recipient-role> --report-to <orchestrator-role> --task-id <task-id> \
  --objective <bounded-outcome> --input <reference> --expected-artifact <artifact> \
  --result-nonce <nonce> --dry-run --format json
```

move は、記録済み herdr role ごとに完全な old-to-new pane map を明示的に必要とし、team と role の
workspace/pane id を一つの atomic operation で更新します。role membership、cwd、kind、delivery method、
reader、profile、その他すべての field は維持します。複数の logical role が一つの old pane を共有している
場合、その old pane が一つの new pane に対応すれば role は一緒に移動します。一方、異なる old pane を
一つの new pane に集約する map は ambiguity として拒否します。herdr query、workspace の discover、pane の作成、
per-role refusal の repair は行いません。writer は CAS lock を保持し、置換前に topology digest を比較します。
stale な `--current-digest` は拒否されます。既存の per-role `topology record` workspace mismatch message は、
動作する sanctioned な whole-team transition としてこの command を示します。machine-local JSON を手編集しないでください。

## topology の host-state declaration (G736)

host-state work を担当する role と、その work に使う envelope を記録します。この command は明示された権限を記録する
だけで、residence、agent kind、external placement、co-location から推測しません:

```bash
intent-cli session-layer topology record-host-state \
  --domain <domain> --team <team> --role <role> \
  --envelope <named-host-state-envelope> --write --format json
intent-cli session-layer topology validate \
  --domain <domain> --team <team> --format json
intent-cli guide orchestrator-thread \
  --domain <domain> --team <team> --target-repo <owner/repo> \
  --agent <agent> --format markdown
```

validate result は declaration を、guide は探索された route を出力します。`host_state` のない legacy topology は valid のまま
migrate されませんが、publish 前に informational な `host-state-role-missing` finding が出ます。その finding は team が
実行できない host-state workflow work を示し、declaration だけでは non-sandboxed participant が供給されないことも伝えます。
design role は明示的に declaration された場合は正当です。禁止されるのは undeclared / ad-hoc な routine request だけです。

---

## プロジェクトセットアップ

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work setup --format json
```

### host-local model-resolution ledger（G685 — preview-through-1.x）

```bash
intent-cli session-layer model-resolution query --routing-root <absolute-host-root> \
  --domain <domain> --team <team> --role <logical-role> --kind <codex|claude> \
  --informal-name <mapping-or-attribution> --requested-effort <effort> \
  [--requested-model <explicit-id>] [--candidate-invocation <full-invocation>] --format json
intent-cli session-layer model-resolution record --routing-root <absolute-host-root> \
  --domain <domain> --team <team> --role <logical-role> --kind <codex|claude> \
  --informal-name <mapping-or-attribution> --requested-effort <effort> \
  [--requested-model <explicit-id>] --outcome verified --invocation <full-invocation> \
  --evidence <READY-proof> --capture-target-evidence --write --format json
intent-cli session-layer model-resolution record --routing-root <absolute-host-root> \
  --domain <domain> --team <team> --role <logical-role> --kind <codex|claude> \
  --informal-name <mapping-or-attribution> --requested-effort <effort> \
  [--requested-model <explicit-id>] --outcome refused --invocation <raw-invocation> \
  --error <captured-error> --write --format json
```

query では absolute routing root、domain、team、logical role、kind、informal name、requested
effort が必須です。`--requested-model` と candidate invocation だけは optional です。必須項目の
欠落は invalid argument として nonzero になります。informal request は mapping name を使い、
explicit-model request では `--requested-model` を追加します（この場合 informal name は attribution
のみです）。
read-only query は最も新しい matching scoped verified baseline を completeness 判定より先に選び、
exact selected target の full argv、local PID と実際の UTC process start time、host、選択された
routing/role digest が bounded observation で一致した場合だけ解決します。reader ごとの deadline は
5 秒です。query は別 pane を scan せず、ledger に書き込まず、model を置き換えません。
scoped baseline 不足、refusal、identity/argv の読取失敗、process generation の変更、request/topology
mismatch は human が exact invocation を許可するまで unresolved です。妥当な query で scoped baseline
が無い場合は `resolved=false` と `human_required=true` を返して exit `0` になります。不正引数と
unreadable ledger は nonzero です。

既存 workflow が許可した各 launch attempt の後、retry / 続行より前に matching READY/refusal evidence を
必ず記録します。verified capture は READY を条件とし、selected target の identity と current argv
だけを取得します。refusal は raw invocation と error を保存し、target capture はしません。
`--dry-run` の verified operation は bounded observation を実行できますが ledger と ignore file を
書き込みません。permission、cwd、`--add-dir` はコピーしません。provider の起動・検証も行いません。
legacy の unscoped row は migration なしで読める diagnostic history として残り、モデルの自動解決を許可しません。legacy query は `resolved=false`、`human_required=true`、
`next_step=query-recorded-target-or-ask-human`、resolution order
`[recorded-target-scoped-query, ask-human]` を返します。baseline を自動置換しません。

### Operator-recorded envelope profile（G686 — preview-through-1.x）

current-digest CAS を使って named typed comparator baseline を記録します。profile の write surface は
この専用 command だけであり、`update-kind`、`update-field`、generic JSON editing は profile を記録しません。

```bash
intent-cli session-layer topology record-profile \
  --domain <domain> --team <team> --profile-name <name> --kind <kind> \
  --sandbox-mode <mode> --approval-mode <mode> --roots-policy <policy> \
  [--writable-root <path>]... --network-access <value> \
  --transport-mode <mode> --evidence <text> \
  [--permission-option <flag>]... [--network-url <url>]... \
  [--role <role> [--role-override]] --current-digest <digest|absent> \
  --confirm-record-profile --write --format json
```

profile は operator が記録する fact で、observed argv から学習しません。role の `envelope_profile`
reference または typed override は、その role の G684 kind registry より優先されます。profile がない場合は
registry comparator を byte-for-byte で維持します。dangling reference または kind mismatch は machine-readable
な `profile-invalid` finding になり、registry に暗黙 fallback しません。command は confirmation、kind、digest
で guard され、seat を launch/recover せず、seat の topology 以外を変更しません。profile comparison は
detection-only で、G684 の security field、cadence、model/reasoning exclusion と preview-through-1.x status を維持します。

## デザイン / intent

```bash
intent-cli interview next-question
intent-cli interview record-answer ...
intent-cli interview compile
intent-cli guide workflow
```

## デザインスレッド improve / 再整合

デザインスレッドで定期的に一歩引いて、最近の作業が当初の mission / vision /
values・ADR / design note・intent tree とまだ整合しているかを確認するための
リフレクション工程です。デザインスレッドでは次の自然言語リクエストをそのまま
貼り付ければ、agent が内部で guide を実行します:

```text
intent-cli で improve プロセスを実行してください。
```

agent は現在のガイダンスを取得し、構造化レポートを生成します。`improve` は
first-class の top-level コマンドで、`guide improve` も同等の guide 名前空間形式
です:

```bash
intent-cli improve --domain <domain> --format markdown
intent-cli guide improve --domain <domain> --format markdown
```

両形式は同一のガイダンスを返し、`intent-cli --help` と
`intent-cli guide commands list` から discoverable です。installed CLI に
`improve` サーフェスが無い場合は `improve guidance unavailable` を報告して CLI を
更新します。`bug-to-intent-repair`・host-loop recovery・`state-doctor`・
dirty-state repair で**代替しない**でください。

`improve` はデフォルトで **implementation-aware** です。evidence が利用可能なら
関連 GitHub issue/PR・実装 diff・テスト・レビュー所見・product evidence も点検し、
現在の最上位 blocker を特定して corrective backlog を提案します
（`Implementation Reality Check` / `Blocker Cluster Analysis` /
`Corrective Backlog Candidates`）。packet 履歴が未解決の product blocker を示す場合、
intent-tree の整理だけでは不十分です。素早い intent-only リフレクションには
`--light` を付けます:

```bash
intent-cli improve --domain <domain> --light --format markdown
```

team の realignment window を supervision bound と同様に独立して宣言し、その後 human /
agent がレビューを実施した時に明示的な durable run record を append します（G662、
`preview-through-1.x`）。run record は domain、mode、timestamp、実際に touch した artifact
を保持します:

```bash
intent-cli improve window --domain <domain> --days <days> --write --format json
intent-cli improve record --domain <domain> --mode implementation-aware \
  --artifact <touched-path> \
  [--artifact <touched-path> ...] --write --format json
```

semantic review は human / agent の作業です。intent-cli は実施された事実を記録し、
timestamp を recency にだけ使います。review の quality を score / grade しません。この
record は scheduler、cron、auto-run、stalled-work debt class を追加しません。

operator 承認後、agent は提案した corrective packet を作成し、最初の GitHub issue を
**最大1件**だけ publish できます（明示的に依頼された場合を除く）。

`improve` は first line of defense ではなく **safety net** です。通常パスは
**packet-time intent maintenance**（G461）です。packet draft 時点で agent は intent
placement・ADR candidate・diagram candidate・docs update・closeout knowledge writeback
を検討するよう促されます（`intent-cli guide workflow task packet-draft` 参照）。この
metadata は optional かつ backward-compatible で（metadata を持たない legacy packet も
有効のまま）、設計コンテキストが新鮮なうちに記録することで intent tree・ADR・diagram が
packet 履歴から遅れて drift するのを最初の段階で防ぎます。`improve` は packet-time
チェックが見逃した drift を後から拾います。

`guide improve` はデザインスレッドのリフレクション工程であり、スケジューラでも
provider 起動でも、host-loop / worker-loop の通常の復旧診断でもありません。
metadata / label / queue の復旧は既存の運用サーフェス
（`automation reconcile` / `automation publish-recovery` /
`review closeout-plan`）に残します。MVV・ADR / design note・intent tree・直近の
packet 履歴・clarification 履歴・短期ループの兆候を点検し、結果を `aligned` /
`intent-strengthening-recommended` / `clarification-recommended` /
`corrective-packet-recommended` / `adr-update-recommended` /
`short-term-loop-detected` / `operator-policy-required` のいずれかに分類します。
変更はまず提案し、operator の同意後にサポートされた intent-cli / repo 経路でのみ
適用します。

## Grill — 永続インタビューモード

ユーザー向けの**永続インタビューモード**（G463）です。トピックを grill する
よう依頼すると、デザインスレッドは grill モードに留まり、現在の intent
コンテキスト（intents・packets・ADR / design note・docs・関連する実装
evidence）から open-question backlog を生成し、**一度に1つずつ質問**を続けます。
各回答のあとも `grill` を再入力させることなく自動的に継続し、構造化された
停止条件に達するまで質問します。

```bash
intent-cli grill --domain <domain> --format markdown
intent-cli guide grill --domain <domain> --format markdown
```

両形式は同一のガイダンスを返し、`intent-cli --help` と
`intent-cli guide commands list` から discoverable です。grill は既存の
`interview` artifact の上に構築されており（回答は `intent-cli interview
record-answer` で記録し、保留中の質問は `intent-cli interview next-question`
で読み出します）、`clarification`（blocker 解決）でも `improve`（遡及的な
再整合）でもなく、packet / issue を自動 publish しません。

停止条件: `no-more-questions`（backlog が空で rediscovery でも新しい質問が
見つからない場合にのみ `今のところ追加質問はありません` を返す）・
`packet-ready`・`intent-update-ready`・`clarification-needed`・
`blocked-by-user-decision`・`too-broad-split-needed`。packet / issue /
intent-update のアクションは停止条件で提案し、operator の明示的な同意後にのみ
適用します。

## Inspect — evidence-backed 観測

タスクを切る前に**実際のプロダクトを観測する**ための名前付きプロセスです（G466）。
`inspect` は agent に、実際の app / CLI / UI / logs / tests を動かし、観測した
evidence と inference を厳密に分離し、expected intent と比較し、その gap を packet
candidate に変換するよう導きます。

```bash
intent-cli inspect --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide inspect --domain <domain> --target-repo <owner/repo> --format markdown
```

**Inspect Report** は `observed_behavior`・`expected_intent`・`evidence`・`gaps`・
`risk_severity`・`recommended_next_action`・`packet_candidates` を分離します。最初の
パスはデフォルトで **read-only** で、破壊的な操作や自動 publish を行わず、
browser / computer-use / log / test ツールを**置き換えるのではなく使い方を導きます**。
観測結果に応じて inspect パスは **stack**（gap を packet 化）・**grill**（不明確な
intent を抽出）・**improve**（systemic drift）・**recovery**（壊れた運用状態）・
**no-action**（intent と一致）へルーティングします。grill・stack・improve とは
区別されます。

## Next — design-side アクションアドバイザー

デザインスレッドで最もシンプルな問い「次に何をしたらいいか」に答えるのが
`intent-cli next`（G465）です。design-side プロセスのカタログを提示し、その中から
1つを推奨するので、ユーザーは全コマンド名を覚える必要がありません。

```bash
intent-cli next --domain <domain> --team <team> --target-repo <owner/repo> --format markdown
intent-cli guide next --domain <domain> --team <team> --target-repo <owner/repo> --format markdown
```

自然言語で `intent-cli に聞いて、次に何をしたらいいか教えてください。` と尋ねれば、
agent が evidence（現在の intents・open question・packet backlog・open PR / review
状態・CLI / queue health）を確認し、**grill**（open question 抽出）・**stack**
（packet backlog 作成 + 最初の issue publish）・**improve**（遡及的再整合）・
**inspect**（実際の app/CLI/UI/log/test 挙動の evidence-backed 観測。単なる status 確認ではない）・**issue-publish**（ready な packet の publish）・
**review**（open PR のレビュー）・**recovery**（stale CLI / queue の修復）・**idle**
（着手可能な作業なし）のいずれか1つを推奨します。出力には recommended action・
reason・確認した evidence・paste 可能な suggested prompt・safety boundary が含まれ
ます。`next` はデフォルトで **read-only** で、選択したアクションを自動実行しません。
実行するかはユーザーが判断します。

`--domain` と `--team` を指定すると、`next` は team に記録された topology と supervision cycle
も読み取ります。recorded topology があり completed cycle / front-door handoff がなければ
`bootstrap-resume` と render-only の `intent-cli guide bootstrap` を示し、topology がない場合と
cycle 完了後は silent です。supervision は opt-in です(G828)。`.intent-cli/config.toml` に
`[supervision] opt_in_teams = ["<domain>/<team>"]` と宣言した team だけが、cycle 未記録のときに
`supervision-setup` の推奨を受け、bootstrap の完了条件にも cycle が含まれます。残っている `bound.json`・
`installed-supervisor.json`・cycle は opt-in の根拠になりません。JSON は `supervision.opted_in` と
`supervision.opt_in_source` を返し、cycle があれば推奨は静かなままです。host-init と design-side loop の guide が deployment の手順を
示し、[オーケストレーションのリファレンス](12-agent-message-orchestration.md)へ
リンクします。この command は未記録を検出するだけで、background process を start・
manage しません。
bootstrap trigger phrase は `Start this work in a herdr-only team.` と
`herdr-only で起動して。` です。出力は CLI / model と app-kind の選択を人間へ質問し、
executes nothing です。

`--domain` を指定すると、`next` は独立して宣言された realignment window と最新の
append-only improve-run record を読みます。その window 内に run がない場合だけ、
improve の実行と完了後の record を含む paste-ready な `realignment` action を追加します。
fresh record の直後は
推奨が silent になり、window 宣言がなければ cadence を推測しません。これは timestamp
recency だけの判定で quality judgment ではなく、scheduler、cron、auto-run、
stalled-work debt class を追加しません。

### GitHub API quota の可視化（G673 — preview-through-1.x）

GitHub を参照する command は、成功した empty result と read unavailable を区別します。
quota exhaustion では machine-readable な `cause: github-api-quota-exhausted`、`resource`、
`remaining`、`reset_at`（`degraded_state` 内にも同じ値）を emit します。`worker next-action` は
`action: unavailable`、`host-loop-next-action` と host review / reconcile surface は
`detection-unavailable` を返します。これは stderr の quota 文言ではなく、structured な
`gh api rate_limit` response から認識します。

`automation stalled-work` は local state だけで計算できる finding を保持し、`partial: true` と
`detection_available: false` を返します。その状態で `items` が空でも healthy とは扱いません。
`automation heartbeat` も同じ state と verdict を運びます。`automation doctor` は観測した全 resource の
`remaining` と `reset` / `reset_at` を報告し、quota により GitHub-consulting surface が使えない間は
`ok` 以外の verdict を返します。reset を待つかどうかは caller が判断します。G673 は retry、sleep、
reset scheduling、request budgeting、transport change、cache、batching を追加しません。

### host-state report の checkout freshness（G727）

`automation stalled-work` は、answer を計算した checkout が current であると
安全に言えない場合、その事実も報告します。local `HEAD` と、
`git ls-remote --symref origin HEAD` が返す実際の default branch の `HEAD` を比較します。

- `checkout_freshness: stale` は local と remote の commit ID を示し、sync して
  report を再実行するよう案内します。
- 本当に current な checkout では `checkout_freshness` を省略し、freshness banner も
  出しません。notice を稀に保つことで signal としての意味を維持します。
- remote を問い合わせられない場合（offline、remote 不在、応答不完全）は理由つきで
  `checkout_freshness: unknown` を出します。unknown を current と解釈してはいけません。
- remote probe は 3 秒で bounded です。stdout と stderr の read も同じ bound に含め、
  expiry では Git process tree を終了させ、stdin を閉じ、terminal/SSH prompt を無効にします。
  timeout は wake を止めず、理由つきの actionable な `unknown` になります。

この probe は read-only です。`fetch`、`pull`、`reset`、その他の sync operation を
実行せず、既存の stalled-work の finding logic も変更しません。`automation heartbeat` は
`stalled-work` を wrapper するため同じ warning を運びます。兄弟をすべて unaffected とは
分類していません。同じ stale clone で `intent status` が stale な local queue state を
checkout provenance なしに返すことを独立に実証しました。source survey でも
`automation summary`、`automation state-doctor`、`host-loop-next-action`、
`automation heartbeat` は `context.RepoRoot` を読むため、unstated checkout provenance の
property を共有します（heartbeat はこの slice の warning を継承し、他は follow-up です）。
この slice の scope は `stalled-work` と heartbeat inheritance に限定します。これらの
RepoRoot-reading sibling には、freshness/provenance contract と test を追加する follow-up
が必要です。`status brief` と host-review diagnostics はこの answer で `RepoRoot` を読まず、
この特定の unstated-checkout path には unaffected です。ただし全体が current だと証明した
わけではありません。この survey を理由に G727 の scope をここで広げません。

G672 は invoking role の pointer を optional に追加します（preview-through-1.x）。

```bash
intent-cli guide next --role design --format markdown
intent-cli guide next --role orchestration --format markdown
intent-cli guide onboarding --role implementation --format markdown
```

contract を持つ role では、この pointer が `guide next` と onboarding の最初の
read-before-acting instruction になります。design は `intent-cli guide design-thread`、
orchestration は `intent-cli guide orchestrator-thread`、implementation は
`intent-cli guide worker issue-to-pr`、review は `intent-cli guide review` を読みます。
contract がない role には invented pointer を出しません。既存の procedure と
first-call ordering は変更せず、wake ごとの reread も要求しません。CLI version または
session-layer configuration が変わったときに再読します。同じ output には、issue #1441
sections D/B-1 の operator-filed feedback に帰属する measured remote-herdr incident（48 units、session-scoped
nohup process が unnoticed のまま二度 died）も記録します。

setup は `intent-cli notify supervise install` を通し、current session 用の launchd、Task
Scheduler、または systemd artifact と operator 用の正確な registration / unregistration
command を生成しますが lifecycle command は実行しません。G712 の GUI-session fallback は
artifact を `~/Library/LaunchAgents` の外に置き、macOS `RunAtLoad` を省くため login / reboot の
auto-load がありません。`intent-cli notify supervise reconcile --write`（または `uninstall --write`）は
loaded job の before/after を表示し、managed job を bootout し、legacy login-persistent plist を含む
artifact を removal して path を示します。継続的な health は team の `cycles.jsonl` record の
age と declared bound を比較します。process-name grep は、実測で team を混同し、一方の
supervisor を強制終了しながら別 team の process を残したアンチパターンです。supervision と
optional `notify supervise --event-mode` は同じ process 内で seat ごとの blocking `herdr agent wait` を保持し、
implementation / review の settle を数秒単位で wake します。これは normative SECOND wake source である
herdr `pane.agent_status_changed` の concrete implementation です。独立した interval cycle は safety floor
として残り、両 source は recorded seat transition で de-dup します。install artifact は invocation を
埋め込むため、event mode の adoption には `supervise install --event-mode` で artifact を再生成して
明示的に re-register する必要があり、既存 artifact は interval-only のままです。この path は macOS の
herdr 0.8.0 で実測し、他 version / platform は unverified です。
install emission は compatibility promise 上 1.x までの preview です。

## Notify — pending delegation の明示的な disposition（G671 — preview-through-1.x）

role 間の message は notify lifecycle command を使います。matching report がなくても
open delegation の outcome が supersede された、または別の場所で適用済みになった場合は、
次の command で明示的に記録します。

```bash
intent-cli notify dispose --domain <domain> --team <team> \
  --task-id <task-id> --kind superseded|applied-elsewhere \
  --actor <actor> --reason <reason> \
  [--superseding-task-id <task-id>] \
  [--applied-outcome-evidence <evidence>] --write --format json
```

`superseded` には superseding task id、`applied-elsewhere` には outcome evidence が必要です。
record には kind、actor、timestamp、reason、および該当する evidence を保存します。
`notify status` は `settlement_basis: disposition` を表示し、report settlement と区別します。
disposed record は `notify supervise` と `stalled-work` の open 集計から外れます。disposition は
automatic や時間経過で作られず、unknown / 既に settled の task id は拒否します。disposed task の
遅い `notify report` も配信し、disposition を保持したまま disagreement を表示します。この post-freeze
surface は compatibility promise 上 1.x まで preview です。

`automation stalled-work` も、open notify record が設定された stale threshold を超えた場合に
informational な `pending-delegation-open` item を返し、未処理の `open_pending_delegations` count を
表示します。report-settled と disposition-settled は除外され、scan は read-only のままで disposition を
推測・選択・write しません。

## Stack — packet backlog 作成 + 最初の issue publish

名前付きの**前方計画**プロセス（G464）です。`stack` は現在の intents を読み、
いま着手可能な packet（しばしば10件程度）を依存順に backlog として作成し、その
durable state を commit / push してから、デフォルトでは**最初の1件だけ** GitHub
issue を publish します。残りは deferred backlog として残します。

```bash
intent-cli stack --domain <domain> --target-repo <owner/repo> --format markdown
intent-cli guide stack --domain <domain> --target-repo <owner/repo> --format markdown
```

`stack` は「タスクを積む」に対応します。`improve`（drift / loop 危機からの遡及的
再整合）・`grill`（永続的な open-question インタビュー）・`clarification`（blocker
解決）・runtime `queue` 遷移とは区別されます。open question・WIP・host-only packet
境界を尊重し、issue-publish の前に durable な packet state を commit / push し、
`intent-target` を手で付けません（host の publish 境界が付与）。出力 shape は
`created_packets`・`recommended_first_issue`・`published_issue`・`deferred_items`
を列挙します。

## Packet / issue

```bash
intent-cli packet ...
intent-cli issue validate-body ...
intent-cli issue prepare ...
intent-cli issue publish-reviewed ...
intent-cli issue publish-flow <id> --repo <owner>/<repo> --write --format json
intent-cli automation issue-publish --write
```

### bug implementation-repair の link（G782）

child repair の handoff は、command 自身が受け付ける link flag を使って durable な bug
projection に記録します:

```bash
intent-cli bug implementation-repair <bug-id> \
  [--execution-unit <unit>] [--issue-number <n>] [--issue-url <url>] \
  [--actor <name>] [--note <text>]
```

指定した値は `repair_execution_unit`、`repair_issue_number`、`repair_issue_url`、
`recorded_by`、`note`、`recorded_at` として保存されます。link flag なしの再実行は既存の
recorded link を維持し、新しい値を渡した再実行は置換して結果に以前の値を表示します。
`--issue-number` と `--issue-url` を両方指定する場合、URL の最終 URI path segment はその
number と完全に一致する必要があり、違う場合は command が両方の指定値を示して拒否します。
query string や fragment は最終 path segment を変えないため、issue number `1706` に対する
`.../issues/1705?repair=1706` は拒否されます。

`intent-cli bug implementation-issue <bug-id>` は記録済みの
`repair_execution_unit` を優先し、target として
`.intent-cli/issues/<unit>/packet.yaml` だけを使います。G337 の
`implementation_issue_packet` schema を root に持つ packet は、この handoff が期待する
legacy `ProjectionPacketRuntimeReader` の `execution_unit` schema ではありません。先に
`intent-cli issue publish-flow <unit> --repo <owner/repo> --write` で publish してから再試行してください。

## 実装・レビューループ

```bash
# AI agent 向けループプロンプトを取得:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --domain <name>
```

worker/metadata コマンドだけでループを回す operator dogfooding 向けプロンプトテンプレートは
[`docs/automation-templates/`](../automation-templates/README.md) にあります。

### unit ごとの evidence status（G855 — preview-through-1.x）

```bash
intent-cli unit status --execution-unit <unit> [--domain <domain>] [--team <team>] --format json|markdown
```

この read-only report は、既存の local packet、queue、publish、runs、claim/history、closeout、review record と、
上限を設けた read-only GitHub snapshot をまとめます。JSON は `schema_version: 1` を使い、両 format で
solo-conductor の 10 phase と subcheck、evidence pointer、cause、repair 可否、freshness、count を表示します。
固定 state は `done`、`missing`、`not-applicable`、`unavailable` です。evidence の欠落は、read failure や
過去 provenance の未対応とは区別されます。

applicability は `.intent-cli/team-mode.json` の現在の domain/team に完全一致する entry だけを使います。
mode の欠落/default と team unresolved は unavailable、exit 1 で、GitHub call は行いません。記録済みの
non-solo mode は solo-conductor phase を not-applicable にして GitHub read を省略します。
solo-conductor では、API read を local で照合した issue/PR と観測 PR head に結び付けます。pagination は
20 page まで完全に読み、head が変わった場合の retry は最大 1 回です。report は観測した check-run と
commit-status を示します。check-run は GitHub の `filter=all` を使い、古い row や superseded row も保持します。
API の `run_attempt` は現在の Actions run の attempt であり、個別 check の attempt を示すものではありません。
2 以上でも check を特定の attempt に結び付けられない場合、raw check を保持し、CI は既知の `provenance-limit` により
`unavailable` とします。この report は branch protection や required-check rule を評価しません。skipped または neutral の
check は `missing` として扱います。別 job の失敗後に条件付きで skip された場合も同じです。正常に読めたことは成功を意味しません。
GitHub の pending review は evidence として保持しますが、posted-review または delta-review を満たしません。観測を読むときは
state、cause、unavailable class の count を合わせて確認してください。

publication が `done` になるのは、publish record の `execution_unit` が requested unit、created issue URL/number が解決済みの
repository/issue に一致し、`publish_status` が `issue-created` または `published`、`lifecycle_state` が未記録/null（legacy baseline）または
issue 作成以降の既知 state（`issue-created`、`published`、
`pr-created`、`closed-out`）である場合です。cause は `publish_status` に従い、`issue-created-observed` または
`publication-issue-published` です。未対応の lifecycle value は unavailable のままです。lifecycle state は PR linkage や
run/closeout evidence を確立しません。別 fact の `issue-published-run` は一致する lifecycle event がある場合だけ `done` となり、
unit/repository/issue が一致する `issue-created` event は `issue-created-run-recorded`、`issue-published` event は
`issue-published-run-recorded` が cause です。
issue artifact の identity だけでは run event を満たしません。これらの observation は automation の `issue-publish` command が
実行されたことや `intent-target` が付与されたことを証明しません。host PR linkage には `queue-state.json` の一致する
`linked_pr` が必要です。publish record に PR URL があるだけでは足りません。issue が解決済みで PR が link されていない場合、
command はその issue の label だけを読み、PR に結び付く fact は `pr-not-linked` を cause とする `missing` にします。

claim evidence は、設定済みでローカルに存在する metadata snapshot を使います。metadata branch が未設定なら claim fact は
既知の provenance limit として示し、canonical/default branch の推測、fetch、ownership 解決は行いません。選択した local ref と
object ID は読んだ snapshot を特定しますが、remote canonical claim branch の保証ではありません。同一 repository の構成で
source/write branch が異なる場合や `same_repo_topology = false` の場合、選択 snapshot に別 branch の claim が無いことがあります。
同じ check context の Actions run が複数あり、各 row を区別できない場合は identity conflict を維持します。

明示された source issue URL では、対応する正確な routing field により requested issue と unit のどちらにも関係しないと証明できる
report だけを、上限付き inventory から除外できます。関連する report や scope を確定できない entry は、別に有効な chain があっても
表示され、source を unavailable にすることがあります。legacy `observed_in` routing は除外専用で、canonical completion evidence
にはしません。packet の明示 report path は従来どおり厳密に検証します。既知の 6 件の provenance limit は変更しません。

exit 0 は observation が完了したことを示し、evidence の欠落や既知の `provenance-limit` だけで unavailable
となる場合も含みます。exit 1 は不正な request または provenance 以外の observation failure を示します。
どちらも完了や merge readiness の判断ではありません。過去の exact-head approval receipt や
worker-completion receipt が存在しないなどの安定した provenance limit は、retry や再承認を促す指示ではありません。

### solo-conductor PR の posted approval（G856 — preview-through-1.x）

`automation pr-transition --transition approved` では、現在記録されている solo-conductor team に対し、
完全な `--head-sha` と同じ現在の PR head に対する posted independent approval が必要です。
`cross_runtime_review.teams` 宣言がない team も対象です。generic route では fresh reviewer の実際の verdict を
PR review として投稿します:

```bash
gh pr review <pr> --repo <owner/repo> --comment --body-file <review-body.md>
intent-cli automation pr-transition --repo <owner/repo> --pr <pr> \
  --transition approved --head-sha <full-head-sha> --format json
intent-cli automation pr-transition --repo <owner/repo> --pr <pr> \
  --transition approved --head-sha <full-head-sha> --write --format json
gh pr merge <pr> --repo <owner/repo> --squash \
  --match-head-commit <full-head-sha>
```

fresh な independent reviewer を使い、以下の generic body をそのまま使います。placeholder は実際の unit、full head、
reviewer の notes に置き換えます。verdict を捏造または書き換えてはいけません。

```markdown
## Independent subagent review: approve

- reviewer: independent subagent review
- execution unit: <unit>
- kind: implementation
- head SHA: <full-head-sha>
- verdict: approve

### Blocking findings

- none

### Notes

<actual reviewer notes>
```

blocking verdict では両方の `approve` を `request-changes` に置き換え、実際の finding を残します。投稿 review の REST
`commit_id`、body head、expected current head は一致しなければなりません。COMMENTED または APPROVED state でも
request-changes body は blocker です。trusted envelope を持つ malformed または race した review は、同じ GitHub login が
厳密に後から current-head の有効な approval を投稿した場合だけ修復できます。無関係な approval は修復しません。
PENDING/DISMISSED は obligation を満たさず、消去もしません。trusted login、正の review ID、submitted time のない named evidence は
unscopable です。API の利用可能性を直すか、通常の review と worker linkage による replacement PR が必要です。再投稿で修復できない場合は command が示します。

既存 G834 local gate は変えず、repo 全体に適用される `IsGatedRepo(repo)` と、解決済み domain/team の declaration の両方が
成立する場合だけ適用します。この route は local gate と、現在 deciding な canonical relation slot 2 つの posted comment を要求し、
generic blocker も引き続き評価します。それ以外の solo team は generic approval を使います。他 repository だけに宣言された team も同じです。
solo entry が 1 件でもある host で PR identity が解決できない場合は保守的に拒否します。既存 claim resolver は従来どおり `git fetch` を
実行することがあり、remote-tracking ref、`FETCH_HEAD`、取得済み object が変化しますが、refusal は label と CI wait を変更しません。
command は review 読み取り前と label 直前に head を読みます。GitHub に atomic な label/SHA compare はないため、
`--match-head-commit` を使って merge します。G856 自体は override や approval receipt を追加しません。eligible な implementation claim release は G857 が別途 gate します。gate は honest seat を前提とします。

### solo-conductor implementation claim release（G857 — preview-through-1.x）

この gate は `execution-unit:<unit>` の `claim release` に限り、actor が builder（`implementation`/`builder`）へ正規化され、完全な actor/team の組が現在の holder の場合に適用されます。既存の完全一致 holder check は維持します。canonical unit packet の明示 domain で held team が記録済み solo-conductor mode に解決される場合だけ gate を有効化します。solo entry がある host で applicability evidence が欠落または曖昧なら保守的に拒否し、mode がない場合や解決済み non-solo team では従来の claim 動作を維持します。

write attempt ごとに canonical claim branch（設定済み `metadata_write_branch`、なければ解決した origin default branch）を ff-only の fresh clone で評価します。holder 確認後、history 書込み、claim 削除、stage、commit、push より前に評価し、retry は新しい canonical snapshot を読み直します。candidate preview は同じ evidence 判定のため bounded な一時 clone を 1 つ使って cleanup します。clone/pull で canonical remote を read する場合がありますが、preview の書込みはその一時 clone 内だけです。invoking checkout は fetch せず、stale transaction root sweep も行いません。canonical branch に既に publish 済みの evidence だけを使います。mixed host では、どの domain/team に対しても canonical solo entry があり packet/domain が欠落する場合、held team に solo entry が見えなくても推測せず拒否します。

必須の closeout evidence は unit が一つだけ Completed の queue item、packet の `target_repo` 内の安全な linked PR、その repository/PR に一致する canonical `pr-merged` と `closeout-recorded` event です。required knowledge write-back には architect と orchestrator の各 role に帰属する record が 1 件必要です。受理される role alias は正規化されます。target は任意で、記録された target list を報告しますが path coverage は確認しません。全 false の明示宣言が `not-applicable` になるのは、既存 G855 predicate が `knowledge_updates.*.required` または `closeout_learning.write_back_required` の宣言 key を検出した場合だけです。宣言の欠落は missing、malformed declaration は unavailable です。宣言された guide route には architect に帰属する record が 1 件必要です。`record.roles` は recipient を示すため、recorder attribution の代用にはなりません。明示的な `no_role_facing_surface: true` は `not-applicable` です。

```bash
intent-cli claim release --scope execution-unit:<unit> --actor implementation \
  --team <team> --reason <text> --format json
intent-cli claim release --scope execution-unit:<unit> --actor implementation \
  --team <team> --reason <text> --write --format json
```

duty が未完了または読めない場合、`completion-blocked`、exit 1、`push_succeeded: false` と、canonical OID/ref、duty state、evidence path、recovery guidance を含む `solo_conductor_completion` snapshot を返します。active claim と holder/team は保持されます。missing receipt は対応する local writer で記録し、厳密な owned receipt path と未公開の closeout path だけを stage、commit し、表示された canonical target ref に plain push します。canonical 上の公開を確認してから release を retry します。既存 writer が retry で修復できない unreadable、conflicting、duplicate、immutable evidence では `repair_unavailable_reason` を返します。`--reason` は gate の override になりません。この release check は lifecycle、queue、run-log、PR-transition writer を追加しません。

### solo-conductor stalled-work adoption window（G858 — preview-through-1.x）

`automation stalled-work` は、既存の knowledge-writeback debt（recorded-uncommitted knowledge を含む）と
guide-reachability debt だけを、解決済み team entry の `solo-conductor` への最初の記録済み遷移に基づいて絞ります。
live CI、claims、operator attention、review/repair、delegation、backlog は絞りません。

```bash
intent-cli automation stalled-work --domain <domain> --repo <owner/repo> \
  [--team <team>] --format json
intent-cli automation stalled-work --domain <domain> --repo <owner/repo> \
  [--team <team>] --since <ISO-8601> --format json
```

開始時刻は、正確な identity が確認された最も早い `issue-created` / `issue-published`、または active/history claim の取得時刻です。
cutoff より前の一致する closeout は unit が既に存在した証拠になりますが、後の closeout だけでは開始を証明できません。
`--since` はどの mode でも明示的な query-wide debt window を有効化し、adoption cutoff を上書きします。
既存 lane flag は closeout-time filter のままなので、active な start window と交差します。start window がなければ、固定 August cutoff と
legacy result shape を維持します。
solo 遷移が利用できない場合や team-scoped unit に corroborating な claim-team provenance がない場合、active window は August floor に戻りません。
August より古い debt が unavailable / unknown reason とともに表示されることがあります。この保守的な保持は、adoption 前の finding がすべて消えることを保証しません。
明示的な lane cutoff で closeout date をさらに絞れます。

read は invoking checkout の既存 legacy debt run log と claim file を使います。scoped runtime log へ移行せず、state も書きません。
関連する identity/team evidence が欠落、malformed、conflicting の場合は pending debt を unknown/foreign diagnostic とともに表示します。
後の closeout activity だけで unit を clean な historical exclusion にはしません。inactive 時は `debt_window` を省略し、active 時は
distinct-unit count と historical exclusion evidence を JSON / Markdown の両方に表示します。

### claim release の監査付き missing-receipt 例外（G860 — preview-through-1.x）

`claim release --override-loop-evidence` は、architect/orchestrator の
knowledge receipt と architect guide receipt の missing だけを対象にする、理由を記録する明示的な例外です。
記録済み solo-conductor mode の builder claim を現在の holder が正確に解放し、canonical closeout identity、Completed queue item、merge、
closeout-recorded evidence がすべて満たされている場合に限ります。通常の `--reason` だけでは completion gate を迂回できません。
approval、ownership、closeout、declaration、その他の unavailable/conflicting evidence は免除できません。

```bash
intent-cli claim release --scope execution-unit:<unit> --actor implementation \
  --team <team> --reason "specific missing-receipt exception" \
  --override-loop-evidence --format json
intent-cli claim release --scope execution-unit:<unit> --actor implementation \
  --team <team> --reason "specific missing-receipt exception" \
  --override-loop-evidence --write --format json
```

preview は書き込みなしで例外 eligibility を表示します。解放時は既存の `release history`、immutable な
`.intent-cli/loop-evidence-overrides/` audit、既に選択済みの canonical run log への
`loop-evidence-override` event 1 件を、同じ claim transaction で書き込みます。
completion snapshot 上の対象 receipt は引き続き `missing` と表示し、audit には skipped duty と理由を記録します。
bounded retry 中に receipt がすべて満たされた場合、通常の claim 解放は override audit/event なしで進められます。
この flag は receipt の publish、PR closeout、approval evidence、claim/run-log schema の変更を行いません。
CLI が確認するのは現在 holder の actor/team が完全一致すること、明示的な flag、空でない reason だけです。
外部承認の有無や reason の妥当性は検証しません。`canonical_snapshot_oid` は評価対象の canonical host commit の OID であり、child PR head の OID ではありません。
この flag は他の operation、`execution-unit` 以外の scope、solo-conductor 以外の mode では使用できません。

### 貼り付け evidence gate（G785）

Acceptance Criteria の bullet に `actual output pasted` または `actual counts pasted`
を**そのまま**書くと、収集済み PR body evidence がコントラクトになります。worker が読むのは
`## Acceptance Criteria` 配下の unordered bullet だけで、Verification などに同じ語があっても
要件にはなりません。該当する各 criterion には、直前の Markdown heading または non-empty line、
または fence の最初の行で `AC <ordinal>`、`Criterion <ordinal>`、`Criteria <ordinal>`、あるいは
その criterion 由来の識別可能な 4 語以上の phrase を名前として示した、収集済み output の
fenced block が必要です。aggregate count、要約、expected value は代わりになりません。
Acceptance Criteria section 内で番号が一意なら、criterion text の先頭にある `AC<n>`、`AC <n>`、
`AC #<n>` は label `AC<n>` になります。Rule L により、その番号の `AC<n>` reference は対応する
label 付き criterion だけを指し、ordinal `<n>` も同時には指しません。ほかの `AC` reference は
ordinal として扱われ、`Criterion <ordinal>` と `Criteria <ordinal>` は常に ordinal を指します。
`AC<n>` と `AC <n>` は同じ表記として扱います。refusal と measurement では
label 付き criterion を `AC<n> (Criterion <ordinal>)` と表示します。

```bash
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> \
  --issue <n> --pr <n> --outcome pr-created --pr-body-file <pr-body.md> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> \
  --outcome pr-created --pr <n> --github-only --write --format json
```

`result-summary` は `evidence_required`（ordinal、criterion text、および存在する場合の `label`）、
`evidence_blocks_present`、`evidence_gap` を出力します。`pr-created` completion gate は
gap が空でなければ label を適用せずに拒否します。例外を明示するには、空でない記録理由を
付けます。

```bash
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> \
  --outcome pr-created --pr <n> --github-only \
  --accept-evidence-gap "<recorded reason>" --write --format json
```

結果には gap を残したまま `evidence_gap_accepted` が記録され、欠けた collected output が
存在したとは扱いません。両方の worker guide は同じ rule を表示し、packet-draft guide は
worker が認識する二つの phrase を著者に示します。

## セッションスコープの supervision セットアップ（G712）

宣言された supervision setup route は、`.intent-cli/config.toml` や host metadata
のない bare directory から実行できます。

```bash
intent-cli guide workflow task supervision-setup --format json
intent-cli guide workflow task supervision-setup --format markdown
```

この route は shipped の session-scoped contract を表示します。`notify supervise install`
は artifact を作成して first-cycle proof を確認するだけで process を登録せず、表示された
`launchctl bootstrap gui/$(id -u) '<artifact-path>'` は現在の GUI session で operator が明示的に
実行する action です。`notify supervise reconcile --write` / `uninstall --write` は before/after
を報告し、managed drift だけを削除します。route 自体は read-only で、これらの lifecycle command
を実行しません。

G781 では通常の install startup proof の既定値を 120 秒にします。`--startup-bound` を省略した
bare の `install --verify` は 1 回だけ読む 1 秒の short re-proof であり、より長い bounded wait は
`--startup-bound` を明示します。`--routing-root` を指定した場合、その supervision root は default
artifact、runtime log、cycle、installed evidence の path も支配します。explicit registration command の後、
guide は既存 artifact を書き換えず再証明する `install --verify` を表示します。timeout contract は
`no-post-install-process` と `post-install-process-wrote-no-cycle` を区別します。前者は存在しない
log path を作らず registration action を示し、後者は実在する runtime log だけを示します。3 つ目の
timeout `post-install-process-missing-writer` は post-install cycle に writer identity がない状態を
記録します。guide は scheduler job を読み込みも照会も行いません。

## 復旧

```bash
intent-cli worker issue-preflight       --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight  --repo <owner>/<repo> --pr <n>    --format json
intent-cli automation doctor --format json
intent-cli automation doctor --domain <domain> --team <team> --format json
```

引数なしの doctor は空の anonymous root を unjudged のままにします。named team に shared
record-first session-layer preflight を必須にするには `--domain` と `--team` を一緒に指定します。
mode 未記録は configuration-incomplete であり、not-required にはなりません。

---

## コマンドグループ概要

`intent-cli guide commands list` は **role ベースのカタログ**（G467）です。各
command group が operator-role カテゴリ — **design**（improve / grill / stack /
next / inspect / intent / interview / packet / clarify）・**host-review**（review /
closeout / automation / issue）・**child-implementation**（worker）・
**recovery-diagnostics**（automation doctor / metadata / queue）・
**advanced-developer**（task）— を `primary`/`support` lifecycle classification
とともに持ちます。`intent-cli guide help` も同じ role バケットを説明し、loop-prompt
生成（`guide workflow task implementation-loop` / `review-next-slice-loop`）を案内
します。

| Surface | 役割 |
|---------|------|
| `intent-cli guide …` | Ask-first ガイダンス: コラボレーションモデル、ワークフロー、プロンプトテンプレートカタログ、one-shot プロンプト |
| `intent-cli status brief` | コンパクトな AI スレッドコンテキスト入力 |
| `intent-cli clarify draft` / `clarify record` | オーナー clarification フロー |
| `intent-cli issue validate-body` | Child Issue Contract 単独強制 |
| `intent-cli issue prepare` / `issue publish-reviewed` | レビュー済み issue body 公開境界（`intent-target` は付与しない） |
| `intent-cli worker next-action` / `claim` / `result-summary` / `complete` | Child 実装ループセレクター + 境界付き label 遷移 |
| `intent-cli automation summary` | プロバイダー中立 label 駆動自動化コントラクトエミッター |
| `intent-cli safety nested-provider-handoff` | アーティファクトのみのネストされたプロバイダー安全ガード（プロバイダーを起動しない） |

---

## ルール

- **`intent-cli` 遷移コマンドを使い、直接編集はしない。** `intent-cli automation` /
  `intent-cli worker` が所有する遷移（queue-state、ワークフロー label、
  packet publish metadata など）は手編集しない。label は必ずこれらのコマンド経由で付与し、
  `gh ... edit --add-label` を直接使わない。
- **読んで推測するより聞く。** ローカルのルールファイルを読むより `intent-cli guide ...`
  を優先する; ガイダンスはインストール済み CLI の現行コントラクトを反映している。
- **`intent-cli` は AI プロバイダーを起動しない。** 決定論的なガイダンスを出力し、
  コントラクトを検証し、bounded な GitHub/metadata 遷移を行うだけ。AI agent がドライバーシートに留まる。

## G795 role consumer inventory（role 利用箇所一覧）

`role-consumer-inventory-entries: 24` は role vocabulary slice で測定した
consumer 一覧の件数です。`LogicalRoleNormalizer` が、3 つの role-scoped
closeout command と record の入力を通る唯一の境界です。role は責務であり
runtime や model ではありません。したがって `opencode` は自由な runtime 値のままです。
queue-state の `worker_role` と `review_role` は runtime/action verb の利用者として
記載するだけで、この slice では意味も field も変更しません。

| # | File と symbol | 読む値 | 分岐・永続化の責務 |
|---:|---|---|---|
| 1 | `Commands/LogicalRoleNormalizer.cs:LogicalRoleNormalizer` | role | canonical 5 種、alias 4 種、fail-closed 入力 |
| 2 | `Commands/CloseoutRecordRole.cs:CloseoutRecordRole.TryNormalize/TryResolve` | role | closeout attribution の共有境界 |
| 3 | `Commands/CloseoutRecordRole.cs:RoleScopedCloseoutRecordStore` | role | canonical role sidecar path |
| 4 | `Commands/KnowledgeWriteBackRecord.cs:Deserialize` | role | legacy read と canonical 再出力 |
| 5 | `Commands/GuideReachabilityRecord.cs:Deserialize` | role | legacy read と canonical 再出力 |
| 6 | `Commands/AutomationKnowledgeWriteBackRecordCommand` | role | `knowledge-writeback-record --role` |
| 7 | `Commands/AutomationGuideReachabilityRecordCommand` | role | `guide-reachability-record --role` |
| 8 | `Commands/AutomationStalledWorkCommand` | role | `stalled-work --role` filter と recommendation |
| 9 | `Commands/SessionLayerTopologyCommand` | role | topology map key（operator-supplied のまま） |
| 10 | `Commands/NotifyRoleTopology.cs:ResolveRecordedRole` | role | recorded delivery-role lookup |
| 11 | `Commands/NotifyCommand` | role | delegate/report/collect role 引数 |
| 12 | `Commands/NotifySupervisor` | role | delegation sender/recipient/report role |
| 13 | `Commands/GuideRoleContractGuidance` | role | 既存 guide-route pointer（route は不変） |
| 14 | `Commands/GuideReachabilityDeclaration` | role | 宣言された guide route role |
| 15 | `Commands/ReviewSeatSelectionGuidance` | role | design/review seat selection |
| 16 | `Commands/NextSlicePacketProvenance` | role | provenance writer/readback contract |
| 17 | `Commands/HostOwnershipModel` | role | host-role ownership resolution |
| 18 | `Commands/HerdrStandardLayoutRegistry` | role | pane/layout seat label |
| 19 | `Commands/GuideWorkspaceLayoutCommand` | role | workspace seat label と順序 |
| 20 | `Commands/GuideOrchestratorThreadCommand` | role | prompt と scheduled-seat routing |
| 21 | `Commands/CliRuntimeContracts` / `Infrastructure/CliConfigLoader` | runtime | configured implement/review runtime name |
| 22 | `Commands/TeamModeCapabilityMatrix` | runtime/action verb | implementation/review capability mode |
| 23 | `Commands/IntentExplainCommand` | runtime/action verb | queue-state `worker_role` / `review_role` 表示 |
| 24 | `Commands/QueueTransitionCommand`, `DuplicateQueueItemRules`, `ClarifyDraftAnalyzer` | action verb | queue-state transition vocabulary（ここでは不変） |

この一覧は説明用であり、queue-state の移行、Steward route の追加、vendor enum の導入、
インストール済み guide route の rename は行いません。
