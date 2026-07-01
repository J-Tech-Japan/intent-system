# agent メッセージオーケストレーション（single-domain と multi-domain）

← [レビュー standing-policy](11-review-standing-policy.md) | [docs インデックス](README.md)

このページは **オプションの** agmsg ベースの orchestrator スレッドと、特に
1 つの host リポジトリが **複数の intent ドメイン** を保持する場合に、それを
どう安全に保つかを説明します。権威ある貼り付け可能なプロンプトはインストール済み
の intent-cli ガイダンスから生成され、このページのプロンプトを手で写してはいけません。
現在のプロンプトは次で生成します:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

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
  開始前に停止する（または `--existing-loop-policy will-stop` を渡す）。orchestrator のみが
  スケジュールされる。

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
   （推奨される低頻度のセーフティネットは [design-side watchdog](#design-side-watchdog任意のセーフティネット) を参照）。
8. **クリーンアップ** — 終了時は agmsg スクリプト（`leave.sh` / `despawn.sh`）でロールを
   leave/despawn し、inbox watcher を停止する。

> **警告:** agmsg のデータベースや team ファイルを直接編集しないでください — 登録・送信・
> クリーンアップはすべて agmsg スクリプト経由で行います。agmsg state の手編集は delivery を
> 壊します。

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
| **timer-loop モード** | 定期タイマー | 既存・完全サポート。実装/レビュースレッドが自己スケジュールし、`worker next-action` / host review-next-slice を読む。orchestrator は不要。 |
| **orchestrator-message モード** | 4 つ目の orchestrator スレッド | オプトイン。orchestrator が agmsg 経由で実装/レビュースレッドをペース配分する。定常状態はメッセージ駆動で、任意の低頻度 design-side watchdog をセーフティネットとする。明示的な orchestrator タイマーは fallback/legacy オプションとして引き続きサポートされる。 |

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
起動しません。メッセージ駆動の定常状態に推奨されるセーフティネットは、高速な
orchestrator ループではなく、低頻度の
[design-side watchdog](#design-side-watchdog任意のセーフティネット) です。

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
- この wake の単一アクションを決定する: 次の slice/PR を委譲、1 通の repair メッセージ送信、
  または 1 件のオペレーター判断にエスカレーション。

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
ありません。GitHub checks が権威です。各 wake で必須チェックを再確認します。pending な CI
はそれ単独では request-update label、repair メッセージ、オペレーターへの質問を引き起こしません。
review / merge / closeout を委譲する直前には必ず必須チェックを再検証してください — 以前読んだ
green は古くなっている可能性があります。

- **pending / running** — 次の wake で待って再確認する。メッセージなし、request-update なし、
  オペレーター質問なし。PR を in-flight として追跡し、先へ進む。
- **green** — すべての必須チェックが通過。intent-cli review surface 経由で review/closeout を
  委譲する。委譲時に green を再検証する。
- **red** — 必須チェックが失敗。所有権でルーティング: 実装スレッドが直せる test/build/lint の
  失敗には 1 通の repair メッセージ。プロダクト/設計や canonical 判断が必要なものはエスカレーション。
  必須チェックが red の間は merge/closeout を委譲しない。
- **stuck / ambiguous** — チェックが開始されない、妥当な時間を大きく超えてハングする、または
  矛盾/不明なステータスを報告する。1 件のオペレーター判断にエスカレーション（fail closed）。
  green を推測しない。

## next-slice の publish

ルーチンな next-slice issue の publish は **orchestrator の責務** であり、オペレーターへの
質問ではありません。intent-cli が候補を `issue-cut-ready` と報告し、すべての安全ゲートを
通過したら、orchestrator はオペレーターに GitHub issue 作成を依頼して止まるのではなく、
canonical な intent-cli コマンドで自分で publish します。**1 wake につき最大 1 件** です。

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
反映していることを検証し、その後 agmsg で実装を委譲します。実装 receiver は依然として
`intent-cli worker next-action` からターゲットを得ます（agmsg テキストからではありません）。

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
- **dependency-waiting** — 依存が in flight（例: PR の CI が pending）→ 次の wake まで待って
  再確認。依存元は hold のまま。
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
- CI 待ち（pending チェックはアクティブな待ち状態）;
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

- orchestrator = Claude
- implementer = Claude
- reviewer = Codex
- design = manual-inbox Codex
- runtime / implementation / review receivers = monitor（サポートされる場合）

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

## design-side watchdog（任意のセーフティネット）

メッセージ駆動の定常状態では、implementation/review の返信がすでに orchestrator を
起こしているため、高速な orchestrator ループは冗長です。代わりに推奨されるセーフティ
ネットは、**design** スレッドから実行する **任意**・**低頻度** の watchdog です:
HITL（human-in-the-loop）メッセージが届いているか、orchestrator が停滞していないかを
確認し、**最大 1 通** の canonical な repair/status リクエストを送ります — ルーチンな
オーケストレーションそのものは駆動しません。

- **頻度** — 低頻度のみ（例: 数十分〜数時間ごと、5 分ごとではない）。高速な watchdog
  ループはメッセージ駆動モデルが取り除いたのと同じチャーンを再現してしまいます。
- **チェック内容** — design/HITL inbox で未読の人間向けエスカレーションを確認
  （design ロールの `inbox.sh`）、および read-only な intent-cli/GitHub の事実
  （`worker next-action --github-only`、open PR/CI/label 状態）を最後に確認した
  orchestrator の活動と比較して orchestrator の停滞を確認します。
- **アクション** — 停滞または未回答の HITL メッセージを検知したら、orchestrator へ
  canonical な repair/status リクエストを最大 1 通だけ送ります:

  ```json
  {"type":"status-request","to":"orchestrator","from":"design-watchdog","ask":"non-destructive liveness check: reply with current state and next action, or confirm idle"}
  ```

- **停止条件** — backlog と human-decision（HITL）キューの両方がなくなったら watchdog を
  停止またはアーカイブします。

watchdog の安全ルールは次を **禁止** します:

- 重複した delegation — watchdog は自分で delegation を再送・再作成しません。
  delegation するのは orchestrator だけです。
- permission プロンプトのクリア — `waiting-permission` は引き続きオペレーター向けの
  通知であり、watchdog が自動でクリアすることはありません。
- 進行中の作業のキャンセルやリセット。
- issue/PR やその他の終端アクションの強制クローズ。
- 推測的な durable-state の手術 — label、queue-state、host metadata の手編集は
  行いません。

明示的な orchestrator タイマー（Codex automation 5 分ごと、または Claude 同一スレッド
`/loop 5m`）は、オペレーターがメッセージ駆動の定常状態の代わりにスケジュールされた
ポーリングを明示的に望む場合の fallback/legacy ポーリングとして引き続きサポートされ
ます — design-side watchdog と orchestrator の fallback タイマーは代替のセーフティ
ネットであり、両方を同時に必要とするわけではありません。

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
