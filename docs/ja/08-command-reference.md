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
reader、profile、その他すべての field は維持します。複数の recorded role が一つの old pane を共有して
いる場合、それらを一つの new pane へ向ける map は許容されます — role は pane と共に移動するため、
これは曖昧ではありません（G735）。`--pane-map maps more than one recorded role to new pane
'<pane>'; refusing an ambiguous workspace move.` という拒否は、二つ以上の異なる old pane が同じ
new pane（どの role も現在占めていない pane）へ向かう真に曖昧な map 専用で、従来どおり拒否されます。
writer は CAS lock を保持し、置換前に topology digest を比較します。
stale な `--current-digest` は拒否されます。既存の per-role mismatch message は sanctioned な whole-team
transition としてこの command を示します。

---

## プロジェクトセットアップ

```bash
intent-cli intent init --domain <name> [--target-repo <owner>/<repo>] --write
intent-cli intent status
intent-cli guide intent-work setup --format json
```

### host-local model-resolution ledger（G685 — preview-through-1.x）

```bash
intent-cli session-layer model-resolution query --kind <codex|claude> \
  --informal-name <name> [--candidate-invocation <full-invocation>] --format json
intent-cli session-layer model-resolution record --kind <codex|claude> \
  --informal-name <name> --outcome verified --invocation <full-invocation> \
  --evidence <banner-or-argv-evidence> --write --format json
intent-cli session-layer model-resolution record --kind <codex|claude> \
  --informal-name <name> --outcome refused --invocation <refused-invocation> \
  --error <error-text> --write --format json
```

ledger hit、currently-running same-kind seat argv、human への質問の順で解決します。
miss の場合は `herdr agent list` を実行し、`result.agents[].agent` が resolved kind と
完全一致する running entry を選択します。各 entry に
`herdr pane process-info --pane <selected-pane-id>` を実行し、
`result.process_info.foreground_processes[].argv` を読みます。選択した同一 kind の seat
すべてで full invocation が一致するときだけ再利用し、不一致なら human に質問します。

表示された launch attempt ごとに、retry または続行の前に対応する記録 step が必須です。
READY の後は取得した exact invocation と banner / running argv evidence を `verified` として、
refusal の後は取得した exact invocation と error text を `refused` として記録します。
bare id を推測せず、shipped list を参照しません。追記専用 ledger は host-local です。
これらの command は provider を起動せず provider 側の検証もしません。

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
cycle 完了後は silent です。cycle が未記録なら独立して `supervision-setup` を推奨し、cycle があれば
その推奨を静かにします。host-init と design-side loop の guide が deployment の手順を
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

## 実装・レビューループ

```bash
# AI agent 向けループプロンプトを取得:
intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>
intent-cli guide oneshot --kind host-review-next-slice    --domain <name>
```

worker/metadata コマンドだけでループを回す operator dogfooding 向けプロンプトテンプレートは
[`docs/automation-templates/`](../automation-templates/README.md) にあります。

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
