# agent メッセージオーケストレーション（single-domain と multi-domain）

← [packet 作成と issue 公開](04-packets-issues.md) | [docs インデックス](README.md)

このページは **primary な 4 スレッドモデル**（design / orchestrator / implementation /
review）と、特に 1 つの host リポジトリが **複数の intent ドメイン** を保持する場合に
それを安全に保つ方法を説明します。1 台に collocate する team は supported な
`herdr-only` transport を最初に選びます（**PREVIEW** は maturity note）。distributed team
または既存 agmsg investment には supported な `agmsg` + herdr を選びます。選択は
`session-layer set` で record し、どちらの transport も primary ではありません。権威ある
貼り付け可能なプロンプトはインストール済みの intent-cli ガイダンスから生成され、このページの
プロンプトを手で写してはいけません。現在のプロンプトは次で生成します:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

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

`notify delegate` は task id、expected artifact、fresh marker nonce、isolated child
checkout から必要な transport-neutral `--routing-root` を含む完全な canonical report
command を配信 task に埋め込みます。receiver は他のすべての作業後、その report
command を final step として実行するため、herdr-only の完了が receiver pane に表示される
だけで終わらず orchestration role を能動的に wake します。herdr-only mode の source of truth は
`<routing-root>/.intent-cli/topology/<domain>/<team>.json` です。sender、recipient、delegate の
`report-to` はすべてその team の recorded roster に存在する必要がありますが、**only the
recipient must be deliverable** です。このため `resident: external` role は pane なしで sender
および `report-to` になれます。その external role が recipient の場合、`delegate` / `report`
は安全な routing-root-relative の recorded `reader` へ変更されていない 6-field event を正確に
1 件 append して配信します（`delegate` は `question`、`report` は status
`completed|blocked|question` を event kind `completion|blocked|question` へ map します）。
そして `eventAppended: true` を返します。herdr-resident recipient は team の recorded workspace
内にある明示的な recorded pane を target にします。他 workspace にだけ存在する agent は
決して eligible ではありません。

`--dry-run` は `--write` と同じ topology、team-workspace、recipient-state、reader resolution を
実行し、prompt / append の副作用なしで同じ refusal verdict と cause を返します。unknown-role
failure は実際に参照した source、team/workspace scope、その scope で見つかった role、corrective
action を明示します。resolution はすべて fail closed で、foreign workspace や別 transport への
fallback はありません。`notify escalate` は同じ 6-field event schema を引き続き append します。
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
   policy、ロールごとの agent、agmsg team 名、delivery mode。
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
   leave/despawn し、inbox watcher を停止する。

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
ワークスペースマネージャーはこれを bypass し、セッションは健全に見えるのにメッセージが
一切配信されません。claude は **オペレーター** が選んだ permission mode で起動します。
各 pane の初回起動には **必ず立ち会って** ください: trust 画面と permission プロンプトは
回答されるまでセッションをブロックします。設計スレッドが回答を認可されている場合、その回答は
次の wake で再プロンプトされる 1 回限りの承認ではなく **durable な** allowlist を生む必要が
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
semantics を記します。後から Cursor や opencode のレシピを追加する場合も、同じ field を
持つ entry を追加するだけで、以下の central rule を再定義・弱化しません。

> **central autopilot supervision rule。** unattended autopilot seat では、launch allowlist
> の外にある action は G550 の supervision dialog として表示されず、静かに自動拒否されます。
> allowlist は role need から導出して **記録** します。READY では、期待される許可済み action、
> その role の canonical reporting surface への到達可能性、out-of-scope action の拒否を証明
> しなければなりません。review evidence は command output と transcript で拒否を調べます。
> liveness は拒否された step が実行された証拠ではありません。これは supervision evidence
> だけを変えるものです。G556 の liveness と notify/delivery semantics は変わりません。

#### Copilot — 実測済みの最初のレシピ

```text
herdr agent start <logical-role> --kind copilot --pane <pane-id> -- --model claude-opus-5 --mode autopilot --allow-all-tools --add-dir <role-work-root> [--add-dir <host-routing-root>] --max-autopilot-continues 10
```

- **role-derived root。** 各 role には checkout または worktree 用の境界付き
  `--add-dir <role-work-root>` を 1 つ与えます。reviewer には canonical reporting surface
  である `intent-cli notify report` のため、さらに `--add-dir <host-routing-root>` が必要です。
  developer-machine の無関係な root は追加しません。
- **継続上限。** `--max-autopilot-continues 10` を明示したままにします。別の上限は、レシピと
  ともに記録する operator の判断です。
- **inline-payload の advisory。** `copilot-autopilot-observed-paste-risk` profile は
  `inline_payload_warning_chars: 4096` を宣言します。これは advisory にすぎません。これを
  超える payload は type ではなく paste されやすいという目安であり、下回れば安全という保証には
  なりません。実際の限界は terminal と agent に依存します。
- **startup gate。** folder trust と autopilot-enable は operator provisioning gate であり、
  launch flag ではどちらも bypass できません。`--mode autopilot` を launch 時に渡しても、
  autopilot-enable dialog は **最初の task** で現れます。`--allow-all-tools` と境界付き root
  を使う場合は `Continue with limited permissions` を選びます。境界を捨てる
  `Enable all permissions` は選びません。
- **禁止する包括権限。** developer machine の unattended seat では `--yolo` と
  `--allow-all-paths` は **禁止** です。代わりに境界付き `--add-dir` root を使います。

**unattended READY branch。** 通常の G556 liveness check に加え、次の 3 点を証明します:
記録済み root 内の期待される action が成功すること、role が canonical reporting surface
（review なら host routing root 経由の `intent-cli notify report`）へ到達できること、意図的に
out-of-scope にした action が拒否されることです。その拒否を review 用に記録します。live pane
だけ、または許可済み action の成功だけでは **READY ではありません**。

**4. ロール初期化。** pane の CLI に合った actas 形式をタイプします — claude は
`/agmsg actas <role>`、codex は `$agmsg actas <role>`。その後 readiness を
**混同してはいけない 3 つのレイヤー** で確認します:

1. **delivery 設定** — `delivery.sh status` がモード（例: `mode=monitor`）を報告することは、
   登録と設定を証明するだけです。watcher が生きていることも、セッションが attach されている
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
   ではなくセッションへの **最初の turn** で arm される点に注意してください。
3. **end-to-end** — [ping テスト](#receiver-の準備状態readiness) の ack が **唯一の**
   end-to-end の証明です。レイヤー 1・2 は前提条件であって代替ではありません。live マーカーが
   得られない場合は明示的にフォールバックし（`turn` delivery または手動 `inbox.sh`）、その旨を
   述べたうえで、それでも ack を必須にします。

**verified liveness — startup report は readiness ではない。** provisioning は
メッセージではなく verified liveness で完了します。ロールが provisioned となるのは、
startup report が届き **かつ**、**settle delay** の後に次の 3 つがいずれもまだ通る
場合です:

1. **pane が依然として agent の TUI をホストしている** — pane を読みます。shell
   プロンプトが出ていれば、どれほど直前に report していても agent は終了しています。
   pane が ground truth であり、メッセージは過去についての主張にすぎません。
2. **agmsg の ping-pong 往復が成功する** — 今 ping し、今 pong を要求します。先ほどの
   readiness ack が証明するのは「その時点で」生きていたことだけです。
3. **codex では bridge が armed で app-server attachment が安定している** — codex の
   TUI は per-folder の app-server に `--remote` websocket で attach するため、pane も
   bridge も直前まで正常に見えていたのに attachment だけが死ぬことがあります。

> **startup report は readiness ではありません。** field incident(2026-07-29):
> 2 体の codex agent が startup-complete を report した **数秒後** に、共有していた
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

> **共有 app-server の death mode。** app-server を kill すると、**attach している
> すべての TUI が一斉に落ちます** — kill の理由と無関係な、他チームの agent も含めて。
> 2026-07-29 の 2 体同時死はまさにこれでした。予防策は下記の attribution ルールです:
> app-server を停止する前にプロセス自身の cwd を確認し、attribute できないプロセスには
> 手を出さないこと。これは attribution 違反の二次被害です — 被害者は kill したプロセス
> ではなく、それに attach していたすべてです。

**5. 排他性とハンドオーバー。** 1 つのロールを保持できる生きたセッションはちょうど 1 つで、
2 番目の actas は拒否されます — その拒否が正しい挙動です。セッションの置き換えは
**graceful drop** を通します: 現保持者が（オペレーター確認つきで）ロールを drop し、
その後にはじめて後継が claim し、readiness + ping テストを再実行します。

**6. 参照ワークスペースマネージャーは herdr。** 設計スレッドが駆動する surface は
`workspace create`、`pane split`、`pane send-text` / `send-keys`、`agent prompt`、
`agent wait` です。intent-cli は herdr を所有・同梱・ラップしません — internals は
agmsg internals と同じくリンクアウトし、herdr 自身のドキュメントを参照します。同じルール
（ロールごとの専用フォルダーを pane の cwd にする、shim-safe なタイプ起動、初回プロンプトに
立ち会う、ping テスト前の actas + readiness、1 ロール 1 保持者と handover 時の graceful
drop）が満たされるなら、**任意の** 同等なワークスペースマネージャーで置き換えられます。

## herdr-only の運用手順（PREVIEW maturity）

この節はチームが `herdr-only` を記録している場合だけ operative です。agmsg の
provisioning / receiver 節に対する具体的な counterpart です。PREVIEW が限定するのは
transport であり、4 スレッドモデルではありません。1 チームでは transport を 1 つだけ
動かし、agmsg と herdr の mixed delivery は contract violation です。

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
返すため、`workspace.workspace_id`、`tab.tab_id`、`root_pane.pane_id` から mapping を seed
し、`root_pane.cwd` を検証します。返された tab が team の通常唯一の tab です。その名前が
`<team>` であることを保証し、必要なら返された explicit tab id を使います。

```text
herdr tab rename <tab-id> <team>
```

root pane を host-repo role の 1 つに割り当てます。残る各 herdr-resident role では **pane
creation が default** です。記録済み mapping から non-empty pane id を resolve し、その
explicit pane から split して新 role の cwd を指定します。

```text
herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus
```

design / orchestrator には `<host-repo>`、implementation には child checkout、review には
隔離した review cwd/worktree を使い、各 pane creation result から mapping を更新します。
`herdr tab create --workspace <workspace-id> --cwd <role-cwd> --label <logical-role> --no-focus`
は primary path ではありません。tab-level lifecycle isolation を simultaneous visibility より
優先する場合など、文書化した理由で separate role tab を operator が明示的に authorize した
ときだけ例外として使います。

同じ tab 内の `herdr pane move` は未サポートです。同じ tab の layout を変えるときは、影響する
pane を作り直して logical-role mapping を更新し、in-place の move で role の配置を保てるとは
考えません。

operator-visible な logical-role→pane-id/cwd mapping を記録し、workflow は pane/workspace id を
hard-code しません。initial workspace creation が最初の id を返した後、すべての provisioning /
mutation command は実行直前に記録済み mapping から明示的で空でない pane/workspace target id
を解決し、command にその id を必ず指定します。解決結果が missing または empty なら fail
closed とし、command を実行しません。そうしないと herdr が focus-default で他チームの
currently focused pane を mutate する可能性があります。既存 G555 cross-project attribution
rules が変わらず authoritative であり、別の attribution policy を再定義せずそれを reference
します。herdr 外の design frontend は架空 pane ではなく reader type として記録します。

この machine-scoped かつ team ごとの topology は
`<host-repo>/.intent-cli/topology/<domain>/<team>.json` に persist します。CLI は
`.intent-cli/topology` 内だけに directory-local ignore を書くため、pane id と absolute path
は machine local のままで root `.gitignore` を編集しません。各 record は `domain` と `team`
を自身に持ち、path と identity が食い違う copy は fail closed します。
`session-layer-mode.json` はこれまで通り tracked な multi-team truth です。machine truth を
移す方法は値の copy ではなく destination machine での re-record です。team の
`workspace_id` を記録し、`roles` 配下で pane-backed role には `resident: herdr` と明示的な
`workspace_id` / `pane_id`、herdr 外の role には `resident: external` と routing-root-relative
な `reader`（通常は `.intent-cli/events/<team>.jsonl`）を記録します。すべての recorded role は
sender と delegate report target になれます。受信時は herdr resident に、その正確な team
workspace の recorded pane で running agent が必要です。external resident は recorded reader
を通して canonical delegate/report event を受け取ります。missing/unsafe reader、stale pane、
foreign-workspace-only name、ambiguous mapping は prompt / append なしで fail closed になります。

新しい per-team file が absent のときだけ legacy fixed file を compatibility read し、
`topology record` を名指しする deprecation warning を出します。両方が存在して内容が
食い違う場合は、どちらも優先せず fail closed します。

この artifact は手編集せず、canonical topology surface で記録・検査します。

```text
intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> [--kind <agent-kind>] --write
intent-cli session-layer topology record --domain <domain> --team <team> --role <role> --resident external --reader <routing-root-relative-path> [--frontend <frontend>] --write
intent-cli session-layer topology update-kind --domain <domain> --team <team> --role <role> --current-kind <kind> --new-kind <kind> --confirm-update-kind --write
intent-cli session-layer topology retire-legacy --domain <domain> --team <team> --evidence <named-fleet-migration-evidence> --confirm-retire-legacy --write
intent-cli session-layer topology validate --domain <domain> --team <team> --format json
intent-cli session-layer topology show --domain <domain> --team <team> --format json
```

agent kind は herdr が起動できる任意の kind です。Claude、Codex、Copilot、Cursor、OpenCode などは
例であり、supported-set の制約ではありません。logical role の既定値は `implementation`、`review`、
`interview`、`clarify` です。legacy の product 名を含め、既存の明示的な role mapping はそのまま
有効です。新しい 2 つの mutation command は JSON を出力し、`--format json` だけをサポートします。
`update-kind` では明示した `--dry-run` が flag の順序にかかわらず `--write` より優先され、決して
書き込みません。

`retire-legacy` が成功すると、CLI は ignored な machine-local topology directory の外にある
`<host-repo>/.intent-cli/legacy-topology-retirements.jsonl` へ fleet-wide decision から引用可能な
entry を 1 件 append します。定義済みの field は `timestamp_utc`、`host`、`domain`、`team`、
`retired_path`、名前付きの `evidence` です。これにより、現在の legacy reader disposition を
変更せずに、後続の ledger decision が累積した retirement を引用できます。

`record` が使う値は operator が供給したものだけです。herdr query、id の guess、resource の
provision、既存 conflict の repair は行いません。完全一致は idempotent no-op、異なる既存 role
は file を書き換えず refuse します。read-only の `validate` は `valid: true|false` と全 finding
を一度に返し、missing/unsupported residence、missing `pane_id`、unsafe reader、team-workspace
mismatch を含む各 finding に role、field、cause、message を記載します。`show` も read-only
であり、`notify` と同じ delivery-target 関数を通して各 pane / reader を解決し、prompt、append、
herdr query を行いません。mapping が存在するか herdr-only が必要とする場合、`automation doctor`
もこの health を載せ、notify の topology refusal は remedy として `topology validate` / `record`
を示します。不正状態は常に fail closed のままです。これらは knowledge と controlled writer を
追加するものであり、fallback は追加しません。

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
set ... --write` を明記）、absent block、malformed marker は refuse し、
`session-layer-mode.json` を書きません。

shared preflight は `AGENTS.md` / `CLAUDE.md` 内の managed marker を発見します。recorded team に
marker がなければ generating command を含む informational な `marker-not-generated` です。mode または
record hash が異なる marker は file、claim、canonical truth を明記する `marker-drift` となり、verdict を
`configuration-incomplete` にします。mode switch の後は regenerate してください。marker は signpost であり、
canonical record の代わりにはなりません。

herdr workspace を provision するときは、recorded mode を workspace label に含めます（例:
`<team> · herdr-only`）。この label は human-facing かつ non-authoritative です。intent-cli は herdr
state を書かず、label を mode evidence として読みません。

#### mode switch 後の manual migration review

`session-layer set --write` が recorded mode を実際に変更したときは、順序付きの **manual migration
plan** を出力します。other mode の session hooks、inbox watchers / monitors を review し、続けて G601
visibility marker を regenerate してください。各項目は operator action です。intent-cli は user
configuration を delete、rewrite、disable しません。no-op の set は plan を出しません。

shared preflight は declared location だけで known other-mode residue を確認します。たとえば
herdr-only team 上の `.codex/hooks.json` にある project-level agmsg session hooks は報告対象です。
`other-mode-residue` finding は path と owning mode、one-mode exclusivity contract、removal guidance を
明記しますが advisory のままです。residue は active mixing の証明ではなく hazard であり、canonical
mode record を infer、flip、override しません。

#### shared record-first session-layer preflight

`automation doctor`、guide の READY definition、`notify` は、同じ production predicate が返す
1 つの machine-readable `session_layer_preflight` result を consume します。似た predicate を
3 個持つのではありません。passive structural phase は receiver に接触せず、active receiver
phase は別に報告されます。active phase の skip は、passing な passive verdict を無効にしません。

named team に mode record がない場合は `configuration-incomplete`、すなわち check-not-completed
であり、決して `not-required` ではありません。intended mode を明示的に record してから検証します。

```text
intent-cli session-layer set --domain <domain> --team <team> --mode agmsg|herdr-only --write
intent-cli automation doctor --domain <domain> --team <team> --format json
```

bare anonymous root は expected domain/team を宣言するまで `unjudged` のままです。
`cannot-determine` は決して green ではありません。preflight は live herdr state から mode を infer
または repair しません。mode が record 済みなら、その transport だけを probe し、contradiction
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
`review-context.md` に追加して push し、それを reference します。pane prompt に実体を inline
してはいけません。これは packet structure を変えるものではなく、意図どおりに使う discipline です。

recipient recipe の `inline_payload_warning_chars` profile は **advisory** であり、universal な
safe-paste limit ではありません。delegate の inline payload が resolved threshold を超えると、
`notify` は payload size、threshold、reference-first remedy を human と machine の両方に warning
として出しますが、同じ payload の delivery は続行します。refuse も truncate もしません。別 team
での観測では、大きな paste が terminal に broken bracketed-paste state を残し、一部の agent process
を terminate することがあります。fresh agent start で recovery します。これは観測事実であり、
すべての terminal や agent に universal な size limit があるという主張ではありません。

settled pane では、notify は最初に bounded `agent prompt --wait --until working` を使い、続けて
idle/done/blocked を待つ別の bounded `agent wait` を実行します。観測された unattended working
transition が delivery verdict です。一度これを観測したら、後続の settle check が
`delivered: true` を否定することはありません。独立した acknowledgement は `settle_outcome` で
`observed`、`not-observed-within-bound`、`not-applicable` と報告し、機械可読な retry verdict は
`resend_permitted` です。idle のままなら `receiver_state_outcome: idle-stays-idle`、
`working_transition: not-observed`、`settle_outcome: not-applicable` で未配達のため
`resend_permitted: true` です。working には入ったが bound 内に settle しない場合は
`receiver_state_outcome: working-did-not-settle`、配達済み、
`settle_outcome: not-observed-within-bound`、`resend_permitted: false` となります。receiver が
まだ作業中であり得るため automation は再送してはいけません。notify 開始時にすでに working の pane は
prompt submission 成功後に delivered としますが、`receiver_state_outcome: already-working`、
`working_transition: unobservable`、`settle_outcome: not-applicable`、`resend_permitted: false` と
報告し、active turn を新 prompt の transition と誤認しません。dry-run は active phase を `skipped`
のままにして prompt しません。

`--to` は引き続き topology の logical role を指定しますが、logical role name は globally unique な
herdr agent name から独立しています。recipient identity は recorded workspace と pane の組です。
notify はその workspace 内のその pane に running agent がちょうど 1 件あることを要求し、agent
name は diagnostic detail にだけ使います。agent が 0 件、running agent が複数、または pane が
foreign workspace でのみ報告された場合は、team、recorded workspace、recorded pane を明記して
fail closed にし、agent-name match fallback は決して行いません。

dispatch ごとに fresh で予測不能な nonce を生成し、再利用や task id 単独での代用をしません。
`pane wait-output` は既存 output を即座に検索するため、task block 内の precomposed wait needle
が echo され、作業開始前に false match することがあります。split field により、その literal を
生成された split field により、その literal を dispatch から除外します。handoff は file、commit、PR、verification log などの inspectable
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
logical-role→pane mapping から解決し、pane id を hard-code してはいけません。event frame は
`agent`、`agent_status`、`pane_id`、`workspace_id` を運びます。

```json
{"event":"pane.agent_status_changed","data":{"agent":"<agent>","agent_status":"<working|idle|done|blocked|unknown>","pane_id":"<resolved-pane-id>","workspace_id":"<workspace-id>"}}
```

直前の status は logical role ごとに独立して追跡します。その role が `working` から
settled (`idle`、`done`、`blocked`) へ遷移した場合だけ wake します。最初から settled の
sample、`unknown`、settled→settled の変化では wake しません。wake 前に settle delay を置き、
per-role dedupe により burst から生じる wake を、その観測済み transition につき 1 回にします。
新しい `working` の観測で、その role を re-arm します。

**state change は何かが起きたことだけを意味し、task が成功したことを決して意味しません。**
どちらの source から wake した後も毎回、orchestration は現在の herdr state と pending
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
inspect します。`idle` は approval-paused の場合があります。結果を settled、
approval/question-paused、timeout に分類します。pause では pane から読んだ G550 MAY class
だけを回答し、それ以外は escalate してから wake に re-enter し、再度 wait します。timeout も
re-entry point です。進捗を persist して制御を返し、後続 wake で再開します。長い flow には
cursor を永続化する deterministic script を推奨します。success は pending
approval/question のない settled state + 正確な fresh-nonce marker と status + 存在して検証済みの
artifact + fresh な canonical intent-cli/GitHub facts の合成判定です。artifact verification と
canonical facts が final gate であり、state 単独も marker 単独も success ではありません。

### Normative な `events.jsonl` design boundary

host root を実行時に解決し、`<host-repo>/.intent-cli/events/<team>.jsonl` を使います。
`<team>` は agmsg/herdr team name を verbatim にした flat filename（例:
`intent-cli-dev.jsonl`）で、team subdirectory と absolute path の hard-code は禁止です。
path 構築前に、空文字、先頭 dot、`/` または `\`、任意の `..` sequence を fail closed で
拒否します。不正名を sanitize してはいけません。

canonical `intent-cli notify` surface だけが writer で、caller は手動 append しません。
通常は orchestrator が delegate/escalate event を書き、recorded recipient が external の場合は
receiver の canonical report も append できます。`O_APPEND` で開き、1 行に完全な JSON object
を 1 つ append し、embedded newline を許さず、`summary` を 1 行へ normalize します。必須 schema:

```json
{"timestamp":"<RFC3339>","team":"<team>","kind":"completion|blocked|question|escalation","unit":"<execution-unit-or-task-id>","summary":"<one-line-summary>","artifact":"<repo-relative-path-or-URL>"}
```

recorded external reader 宛ての canonical notification と、design-relevant な completion /
blocked / question / escalation だけを書きます。external reader 宛て delegation は `question`、
external report は `completed|blocked|question` status を event kind
`completion|blocked|question` へ map します。pane-resident dispatch、
routine progress、pane output、acknowledgement はここへ mirror しません。この mode-independent
channel は explicit external-reader/design boundary のままで、fallback inter-agent bus ではなく、
`intent-cli notify`、GitHub、intent-cli workflow state の代替でもありません。

すべての reader は watcher restart をまたいで durable watermark を永続化し、file identity、
byte offset、complete-line count を保持します。各 read 前に同じ file identity であることと、
byte / line count が backwards になっていないことを検証します。
durable byte-offset watermark は必ず file identity と complete-line count と組にし、3 値のどれも
restart-local にしません。
rotation、truncation、backwards count、file replacement は operator recovery まで fail closed とします。replay が design
decision を重複させるため、先頭から silent reset してはいけません。

- Claude app watcher: durable な file-identity/byte-offset/complete-line-count watermark より後の
  完全な未読行だけを tail し、成功後にのみ進め、watcher restart をまたいで保持します。
  rotation、truncation、backwards byte/line count、file replacement で fail closed とし、先頭から再開しません。
- herdr pane の Codex CLI: 通常 coordination では `intent-cli notify delegate` / `report` を使い
  file poll しません。design-boundary reader として動く場合は同じ durable で restart-surviving
  watermark を使い、rotation、truncation、backwards count、file replacement で fail closed とし、
  先頭へ reset しません。
- Codex Desktop: one-minute-class（約 1 分）cadence で poll し、durable で restart-surviving な
  file-identity/byte-offset/complete-line-count watermark より後の完全な行だけを処理します。
  rotation、truncation、backwards byte/line count、file replacement、malformed JSON で fail closed とし、
  先頭から reset しません。

### Recovery と mode switch

運用 baseline は macOS/Linux の latest stable herdr です。Windows support は beta であり、
この guide では仮定しません。`herdr --skill` は bundled herdr agent skill を見つけるためだけの
discovery pointer で、intent-cli guide authority より下位です。version-specific detail は引き続き
installed herdr help/schema を参照します。

- live server update: running pane を保つ `herdr server live-handoff` を使います。
  `events.subscribe` consumer は stream EOF を error ではなく resubscribe trigger として扱います。
  handoff 近くで回答した approval は re-present され得るため、pane を re-read し同じ dialog を
  re-judge します。以前の回答が consumed されたと仮定せず、blind re-answer もしません。pane PTY size
  は TUI client の reattach まで shrink することがあります。read は有効なままで、headless
  resize/zoom は PTY を restore せず、operator の TUI reattach が remedy です。

- modifier-chord launch corruption: shell へ戻すか再 provision し、typed な
  `agent start ... -- <permission-flags>` を使います。
- reboot 後の dead pty wiring: stopped server の socket command は `server_not_running` を返します。
  headless server は TUI client を待たず restored agent session を resume します。undetected agent /
  shell-only pane では artifact を保全して re-provision、mapping 再構築、上記の自己完結した
  settle-and-re-check READY gate 再実行を行います。
- focus-default cross-team mutation: 明示的な pane/workspace id が missing / empty だと、他チームの
  currently focused pane を mutate する可能性があります。initial workspace creation 後の every
  provisioning/mutation command で記録済み logical-role mapping から non-empty id を解決・明示し、
  解決失敗時は実行しません。既存 G555 attribution rules を変更せず適用します。
- long-wait turn death: bounded wait、re-entry、persist された deterministic loop を使います。
- dispatch-echo false match: composed wait needle を task block に入れず、return 後に pane を
  inspect し、named artifact を独立に検証します。
- idle と報告された approval/question pause: every wait 後に pane を inspect し、G550 の
  MAY/escalate 境界を適用して wake に re-enter します。

### Session-layer switch checklist

**agmsg → herdr-only**: work を drain/park、role を graceful drop して watcher/bridge を停止、outgoing
transport の per-project agmsg hook configuration と delivery mode を turn off または remove し、delivery
不可を検証します。これは cosmetic ではありません。残存 hook が next-launch hook-trust screen を
発生させ、次の Codex launch を block した実測事象があります。herdr・mapping・検証済み events
path を provision、G556 と marker/artifact 検出を通し、最後に `intent-cli session-layer set
--domain <domain> --team <team> --mode herdr-only --write`。

**herdr-only → agmsg**: work を drain/park して必要な final design event を append、operator
policy に従って workspace を停止または retain/close し delivery を止め、agmsg role と承認済み
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
後継が claim して readiness と ping テストを再実行 — 全過程で **1 ロール 1 保持者** を守ります。
drop の確認は **オペレーターに可視** です: 生きたセッションを退役させる判断はオペレーターのもので、
確認はそれを記録するものです。

**3 つの監督レイヤー。** 各レイヤーは他のレイヤーが構造上検知できないものを捕まえます:

| レイヤー | 目的 | ケイデンス |
| --- | --- | --- |
| リアルタイム message monitor | 受信する agmsg の返信・blocker・エスカレーション | 継続的（attach された live stream） |
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
> attach された monitor は、それをホストしている設計セッションと一緒に死に、しかも停止したことを
> 誰も知らせません。各レイヤーは設計セッションの再起動を生き延びるか、**新しいセッションの最初の
> 行動として re-arm** されなければなりません。忘れた場合の実測コスト: セッション再起動の窓で
> claim が失われ、publish 済み issue が **5.5 時間** stall しました — たまたま動いている監督
> レイヤーが 1 つも無かったためです。

**ブロッキングダイアログ — 境界。** ここでは **verified-read ルール** がすべてを支配します:
設計スレッドがダイアログに回答してよいのは、**その内容を pane から読み**、自分が何を承認しようと
しているかを述べられる場合に限ります。レンダリングしていないダイアログへのブラインド入力は、
どれほど定型に見えても禁止です。内容が読めない・検証できない場合、そのダイアログはエスカレーション
対象です。

**回答してよい（MAY）** のは次の 4 種のみで、いずれも上記の読み取りの後に限ります:

1. **自分自身が要求した作業の確認** — プロンプトが、この設計スレッドが直前に開始した操作と
   一致すること（同じ対象・同じ操作）。
2. **read-only であると検証済みのコマンド承認** — pane に表示された正確なコマンドを読み、
   read-only であると検証すること。書き込み・削除・インストール・publish・状態変更を伴うものは
   エスカレーション（「おそらく read-only」は検証ではありません）。
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
禁止、issue/PR の force-close 禁止、durable state の投機的な手編集禁止。

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
| process cwd | kill の前に **pid ごとに** cwd を読む — プロセス *名* だけで絞った pid 一覧は何も attribute しない |
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

## design 判断による hold と bounded authority

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
> 存在するはずで、artifact が無いならそれは待っているのではなく stall しています。

その内容を運ぶのは OPEN artifact 自身です — agmsg メッセージは通知はできますが、
durable な記録の代わりには決してなりません:

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

進行が design の判断で止まるとき、operator-attention
record を開くことは任意ではなく義務です。その待ちが始まった時点で design を owner として
記録します。record は query でき、scrollback に埋もれず `heartbeat` / `stalled-work` に
現れます:

```bash
intent-cli operator-attention open --record <design-wait-id> \
  --domain <domain> --team <team> --owner design \
  --blocking-reference <issue|pr|unit|release> \
  --action-needed "<必要な design judgment>" --evidence "<事実>" \
  --write --format json
intent-cli operator-attention query --domain <domain> --team <team> --format json
```

判断を回答した人は、その回答と evidence を添えて同じ record を**必ず resolve**します:

```bash
intent-cli operator-attention resolve --record <design-wait-id> \
  --resolution-evidence "<回答と evidence>" --write --format json
```

回答済みで open のままの record は嘘です。既存 lifecycle は回答者が resolve するまで完了
しません。これは design 所有の待ちを記録するもので、helper を追加せず、上記 clarification
lifecycle も変更しません。

**reviewer hold ルール(refined)。** 技術チェックが green で、保留項目が
非セマンティックかつ機械的に事実確認可能 → bounded default authority のもとで
解決し、検証事実をログに残して先へ進みます。それ以外 → clarification を記録し、
hold を **可視な pending state** として保ちます。reviewer が単に待ち、それを
メッセージで述べるだけ、という第 3 の選択肢はありません。

**bounded default authority。** オペレーターは、判断ではなく *リポジトリの事実を
確認する* ことで決着する、少数の列挙された判断クラスを事前委譲できます:

| 判断クラス | 何が検証するか |
| --- | --- |
| 件数・列挙の訂正 | 両スレッドが読めるリポジトリの事実から件数が導出できる(例: マージ済み PR 一覧からのスライス数) |
| 引用された事実から導かれる wording 訂正 | wording がリポジトリの事実から entail され、reviewer と orchestrator が事実と訂正の両方に合意している |
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
  バージョン選択; packet 内容と受け入れ基準（durable な packet ファイル）。
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
`--owner design` つきの operator-attention record を open し、待機中に既存 record を query し、
回答者が evidence とともに resolve します。完全な lifecycle は
[design 判断待ちの記録義務](#design-判断待ちの記録義務)を参照してください。

**release-prep は design 所有:** design がリリースバージョンとスコープを決め、release-prep
packet を author します。orchestrator はそれが存在し `issue-cut-ready` になった **後** にのみ
publish・coordinate できます — 曖昧な「リリースを準備して」という指示からバージョンを選んだり、
スコープを決めたり、リリースノート/packet を自分で author してはいけません。

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
| **orchestrator-message モード** | 4 つ目の orchestrator スレッド | **PRIMARY。** 実践され、メンテナンスされているモデル。orchestrator が agmsg 経由で実装/レビュースレッドをペース配分する。定常状態はメッセージ駆動で、30 分クラスの design-thread watchdog loop を RECOMMENDED なデフォルトのセーフティネットとする(orchestrator-side の長間隔 automation は選択可能な alternative)。明示的な 5 分の orchestrator タイマーは fallback/legacy オプションとして引き続きサポートされる。 |
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
  publish した場合、存在を検証したうえで、その **同じ wake の中で** implementation
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
  arm します。intent-cli の外側の controller が watch を所有します。terminal に達したら、team の
  logical-role mapping から解決した pane ID の recorded orchestration role を wake します。pane ID を
  hard-code してはいけません。intent-cli はこの background process を起動も管理もしません。この
  wake が示すのは待ちが終わったことだけです。成功か失敗かを判定するため、`stalled-work` と
  exact-head の GitHub facts を再読します。
- **agmsg orchestrator-message** — 明示的に設定した fallback orchestrator timer が再確認を発生
  させられます。それがない場合は、同じ exact-head `gh pr checks ... --watch` surface を arm します。
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

## next-slice の publish

ルーチンな next-slice issue の publish は **orchestrator の責務** であり、オペレーターへの
質問ではありません。intent-cli が候補を `issue-cut-ready` と報告し、すべての安全ゲートを
通過したら、orchestrator はオペレーターに GitHub issue 作成を依頼して止まるのではなく、
canonical な intent-cli コマンドで自分で publish します。**1 wake につき最大 1 件** で
publish し、検証したうえで、**同じ wake の中で** その issue を implementation へ
delegate します（G524）— publish と delegate は一緒に完了させ、delegate を
スケジュールされていない次の wake に先送りしてはいけません。

次の **すべて** が成り立つときのみ publish します:

- same-domain コンテキスト、または明示的にルーティングされた multi-domain 委譲
  （明示ルーティングなしに cross-domain 候補を publish しない）;
- packet contract が完全（必須セクションの欠落なし）;
- open な clarification や contract の曖昧さがない;
- 依存が満たされている — 未 cut の依存より先に publish しない;
- WIP 上限内;
- host-sync / preflight がクリーンで、対象 repo/domain が一意。

それ以外は **hold またはエスカレーション** — 必須セクションの欠落、open clarification、
依存の不一致、WIP 上限到達、host-sync ブロッカー、対象 repo/domain の曖昧さはすべて
ブロッカーです。

publish は canonical な surface のみ — `intent-cli issue publish-flow` と
`intent-cli automation issue-publish` — を使い、生の `gh issue create` や
`gh ... --add-label` は使いません。publish 後は intent-cli / GitHub（チャットではなく）で
issue が期待どおりの body と `intent-target` label を持つこと、durable state がそれを
反映していることを検証し、**その同じ wake の中で** agmsg で実装を委譲します（G524）—
publish した後で止まって将来の wake を待つことはしません。実装 receiver は依然として
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
オペレーター判断のために止まらず、チェーンを決定論的に計画します — 依存元の候補を hold し、
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

依存元の候補は、すべての依存が完了/cut されるまで hold されます。**エスカレーションは次の場合
のみ**: 依存 packet の欠落、依存の循環、ルートマッピングのない cross-domain 依存、GitHub
linkage の矛盾、破壊的な復旧、認証情報/セキュリティ、または人間のプロダクト/設計判断。

## stale-thread ヘルスチェック

receiver は loopless なので、**沈黙は曖昧** です — receiver は working、CI 待ち、
permission プロンプト待ち、blocked、返信なしで completed、または本当に stale かもしれません。
receiver がしきい値（デフォルト **30 分**、設定可能）を超えて返信しない場合、orchestrator は
**安全な** liveness チェックを行います: 行動する前に尋ね、権威ある事実を検証し、作業の自動
キャンセル・permission プロンプトの自動クリア・タスクの重複を決して行いません。

手順:

1. **非破壊的な status-request を 1 通** 送る — 尋ねるだけで、retry/cancel/reset しない。
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
  終了は正当な orchestration wake signal であり、pending wait として dedupe せず green または
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
  パスを force-delete して解決するものではありません。

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
> いても、新しく起動/再起動したセッションには **可視に delivery されない** ことがあります。ack の
> ないメッセージは **receiver-not-ready** であり、成功した委譲ではありません。ack 後に resend するか、
> receiver に `inbox.sh` で queue を読ませて復旧します。

receiver が initial メッセージ送信 **後** に launch された場合に送る、貼り付け可能なオペレーター
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
> agmsg history にあっても可視に delivery されないことがあります — design スレッドは `inbox.sh`
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
  `automation issue-publish`）で **1 つ** の GitHub issue を自分で作成・publish します — 各ステップを
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
supersede します)は、**design** スレッドから実行する **30 分クラス** の間隔の
watchdog loop です: `intent-cli automation heartbeat` を唯一の scheduler-agnostic な
decision surface として呼び出します。各 valid result は `healthy-active-wait`、
`actionable-stall`、`operator-required`、`cannot-determine` のちょうど一つの verdict を持ちます。
外部 loop が cadence、watermark、dedupe persistence を所有し、intent-cli は schedule、sleep、send、
poll state の永続化をしません。生きた、人間が監視しているエージェント
セッションの **内側** で動作するため、見えない外部プロセスとは異なり、別途の
credential/keychain セットアップも不要で(セッションの他の部分と同じ方法で
authenticate します)、壊れた瞬間にオペレーターの画面上で可視化されます。

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
  record と action を operator に提示し、orchestration を nudge しません。`cannot-determine` は named
  monitor/routing failure を可視化して repair または escalate し、healthy/silent として扱いません。
  heartbeat コマンドの実行失敗や不正な/オブジェクトでない出力は
  **決して沈黙しません**: この wake 自身の turn 出力で、生きたこのセッションを
  監視しているオペレーターに見える形で、失敗を明示的に述べます — これこそが、
  retire された見えない外部スケジューラに対して in-session の watchdog が持つ
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
  `automation stalled-work`(G523)、operator-attention、recorded topology を一つの verdict、
  evidence/age basis、stable dedupe key、owner、canonical notify command に統合します。
- **アクション** — その一つの verdict だけに従います: named signal を bound 内で待ち、
  `actionable-stall` key には返された canonical notify command を最大 1 回実行し、
  `operator-required` は human に route し、`cannot-determine` は可視化して repair/escalate します。
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
- 推測的な durable-state の手術 — label、queue-state、host metadata の手編集は
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
  `operator-required` は operator に route し、`cannot-determine` は可視化して
  repair/escalate します（G524 の wake contract に従い、dedupe key ごとに最大 1 nudge）。
  `stale` や `message_body` から action を推論しません。

### Retired: 外部 OS スケジューラの heartbeat(G526 → G539)

**Retired。** G526 が追加した外部 cron/launchd の OS スケジューラ推奨は retire
されました。理由:

1. **credential-store access** — ラッパーの `gh`/agmsg 認証は、多くの場合ログイン
   keychain に存在し、cron ジョブはそこにアクセスできません。そのため、ロジックでは
   なく認証情報のステップで失敗します。
2. **invisible failure** — 失敗した cron の実行は誰も見ない OS ログに書き込まれる
   だけなので、実際にはセーフティネットとして機能していません。
3. **outside the agmsg model** — intent-cli は agmsg を経由して coordinate し、
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
- **メッセージが可視でない** — queue 済みだがライブ delivery されていない可能性。ロールの queue を
  `inbox.sh` で読み、`team.sh` / `delivery.sh status` を再確認し、ack 後に resend する。
- **メッセージ送信後に receiver が開始した** — earlier なメッセージは history にあるがライブ delivery
  されない。`inbox.sh` で読むか、receiver の ack 後に resend する。
- **packet があるのに orchestrator が idle** — orchestrator が design の start/resume メッセージを
  受信したか（`inbox.sh`）、`worker next-action` / `intent status` が **この** domain/repo に対して
  実行可能項目を報告しているか（host repo に見える別ドメインではない）を確認する。issue-cut-ready で
  安全なら、orchestrator は待たずに自分で 1 つ issue を publish すべき。
- **`mode=monitor` だがライブストリームがない** — `delivery.sh status` `mode=monitor` は設定にすぎず、
  Claude Code `Monitor` が attach されている証明ではない。ライブ attach の success marker（`1 monitor` /
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
orchestrator を通じて調整し、人間が必要な項目だけを surface します。

1. design inbox（`inbox.sh`）を確認し、orchestrator のエスカレーション/サマリーを読む。
2. intent-cli / GitHub の **read-only** state（`intent status`、`worker next-action`、PR/issue/
   labels）を確認して判断の根拠にする — agmsg メッセージを state として信用しない。
3. orchestrator に state 更新や nudge（start/resume）を送る。implementation/review を自分で
   駆動しない。
4. implementation/review の作業・labels・host メタデータを直接 **変更しない** — それは
   orchestrator/receivers が intent-cli 経由で行う仕事。
5. 人間が必要な項目 **だけ** を人間に要約する。ルーチンな進捗は内部に留める。
6. design judgment を待つことで進行が止まるなら、待つ前にその待ちを durable にします:
   `--owner design` つきで operator-attention を open し、record を query し、回答を出したら
   evidence とともに resolve します。回答済みで open のままの record は嘘であり、design handoff
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
preflight します。receiver が誤った repo・誤ったブランチ・dirty なユーザー作業の上で動くのは、最も
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
- **receiver の cwd が委譲と異なる repo/domain を見る** — 停止。claim しない。receiver の cwd/worktree・
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
`closeout pr`）を使わなければなりません。merge/approval はそれらの surface で gate され続けます。
draft が手作業や生の label 編集で approve/merge されることはなく、host メタデータ編集を経ることも
ありません。

## single-domain と multi-domain のオーケストレーション

host チェックアウトは正当に **複数** の intent ドメインを含み得ます（例:
`sekiban-as-a-service`、`sekiban-wasm-runtime`、`intent-cli`）。さらに
**複数のドメインが同じ GitHub リポジトリを対象** にすることもあります。可視であることは
権限ではありません。そのため orchestrator は次の 2 モードのいずれかで動作します。

### single-domain orchestrator

- 選択したドメインのみがスコープ内。
- 同じ host repo に **可視** な他ドメインの queue 項目は **スコープ外** — たとえ
  同じリポジトリを対象にしていても、publish / delegate / repair してはいけません。
- 可視な他ドメイン項目を delegate 可能と見なすのではなく、domain/mode を切り替えるよう
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
2. チェックアウトが委譲された repo/domain と一致しない場合は、claim せずに
   **停止して blocked を返す**。
3. prefix の不一致だけでは wrong-repo シグナルにならないことを忘れない。所有権は
   packet/domain メタデータとルーティングコンテキストで確認する。

実装スレッドは **GitHub-contract-only** を維持します。host メタデータ
（`.intent-cli/**`、`intents/**`）を読んだり変更したりしません。すべての label 遷移は
`intent-cli worker` / `intent-cli automation` を経由します。

**orchestrator への completion または blocked の報告は、すべての delegation の
REQUIRED FINAL STEP です（G524）** — これは任意ではなく、orchestrator が自力で
silent completion を発見することはできません（orchestrator に報告が届かないまま
PR が open された場合、それは orchestrator の視点では失われた作業です — フィールドの
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
