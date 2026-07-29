# リリースノート — intent-cli v0.6.0

> **リリースモデル:** メンテナ/オペレーター(または外部のリリース automation)が `v0.6.0` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、version メタデータと docs をバンプするだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲートg551) と
> [v0.6.0 の publish](#v060-の-publish) を参照。

## v0.6.0 の内容

v0.6.0 は **minor リリース** で、`v0.5.0` 以降にマージされた 11 件のスライス —
**G539, G540, G541, G542, G543, G544, G545, G546, G548, G549, G550** — をカバーします。
patch ではなく minor バンプなのは v0.5.0 と同じ理由です: **新しい stalled-work 検出 kind**
(`backlog-ready-idle`、`blocked-label-drift`、`repair-stalled`)、**新しいコマンドサーフェス**
(`automation runs-audit`、`queue priority-drift`、`automation issue-block`)、既出コマンドの
**可視な挙動変更**、そして **4 スレッド agmsg オーケストレーションの primary モデルへの再配置**
を出荷するためです。package id は `JTechJapan.IntentSystem.Cli` のままで、package id・
ライセンス・workflow セマンティクスの変更はありません。

> **G547 は上記のスライス一覧に意図的に含まれていません。** 本サイクルの当初の release-prep
> ユニット(G547)は、G549/G550 をリリーススコープに加えるというオペレーター指示を受けて
> `automation issue-retire` により canonical に **retire** されました。retire はユニット ID に
> 対して terminal であるため、継続は G547 の republish ではなく、新しいユニットである本パケット
> **G551** になります。G547 はコードもドキュメントも出荷しておらず、その packet は監査証跡として
> 残ります。したがってユニット範囲 `G539–G550` は 12 個のユニット ID にまたがりますが、
> **本リリースでマージ・出荷されるのはそのうち 11 件** です。

> **4 スレッド agmsg オーケストレーションが PRIMARY な文書化モデルになりました**
> ([G540/G541](#primary-モデルへの再配置と-provisioningg540g541g549g550) を参照)。
> v0.5.0 が持っていた preview/experimental の位置づけは置き換えられます。timer-loop モードは
> よりシンプルな代替として完全にサポートされ続けます。intent-cli と GitHub が権威 source of
> truth であり続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### primary モデルへの再配置と provisioning(G540/G541、G549、G550)

本サイクルは 4 スレッドオーケストレーションを「preview」から、実践され文書化された既定へ
移します。そのうえで、オペレーターが実際に必要とする 2 つの半分 — チームの作り方と、
チームを動かし続ける方法 — を供給します。

- **4 スレッドオーケストレーションが PRIMARY モデル**(G540) — `guide orchestrator-thread`、
  `guide model`、`guide onboarding`、`guide prompt-matrix`、`guide help` のすべてが
  orchestrator-message モードを primary、timer-loop モードを完全にサポートされたよりシンプルな
  ALTERNATIVE として再構成し、オーケストレーションモードに付いていた preview/experimental/
  opt-in の限定語はすべて除去されました。role-boundary セクションには
  **design↔orchestrator の double-check ルール** が追加され、協議対象となる 4 つの判断カテゴリ
  (intent shaping、packet 内容と受け入れ基準、リリーススコープ、優先度の裁定)と双方向の
  非バイパス規則 — orchestrator は design 内容を単独で author せず、design は workflow 遷移で
  orchestrator をバイパスしない — を明示します。
  [ADR 0001](../adr/0001-four-thread-orchestration-primary-model.md) に記録されています。
- **再配置は外向きの全サーフェスに到達**(G541) — README の quickstart は 4 スレッド
  オーケストレーション経路を先頭に置き(implementation-loop プロンプトは「timer-loop
  alternative」に改称)、NuGet の `Description` は primary モデルを明記し、`PackageTags` に
  `agmsg`/`orchestration`/`ai-agent` を追加。ja/en の docs インデックスは agent-message
  orchestration を timer-loop 系エントリより前に置き、オーケストレーションページの導入と
  driver-mode 表から残っていた "optional"/"opt-in" 表現を除去しました。package id・ツール
  コマンド・ライセンスは不変です。
- **ターミナルワークスペースのチーム provisioning**(G549) — `guide orchestrator-thread` に、
  設計スレッドがプレースホルダーだけで end-to-end 実行できる provisioning セクションを追加。
  **存在しない専用ロールフォルダーの作成から始まります**: host 側ロール(orchestrator、review)は
  host メタデータリポジトリのクローン、implementation ロールは target repo のクローン。
  never-share ルールはその理由とともに明記されます — agmsg identity と codex monitor bridge は
  `(project, type)` スコープであり、1 フォルダーに 2 ロールがいると同一 identity に解決され、
  片方が静かに受信を停止します。さらに 1 workspace / 1 タブ / ロールごとに 1 pane のトポロジー、
  codex の shim-safe なタイプ起動(canonical な実行ファイルを直接 exec するワークスペース
  マネージャーは shim を bypass し bridge が arm されません)、CLI ごとの actas 形式、そして
  混同してはいけない 3 レイヤーに分割された readiness — delivery **設定**、**live attachment**
  (agent 別: Claude Code の Monitor マーカー / `Codex bridge: … alive (pid N)` マーカー)、
  **end-to-end**(ping/ack が唯一の end-to-end 証明)— を pin します。herdr は参照ワークスペース
  マネージャーとして surface を列挙しつつ internals はリンクアウトされ、同等のマネージャーで
  置き換え可能です。
- **設計スレッドによるワークスペース監督と権限境界**(G550) — もう半分です。**オペレーターが
  付与した** 権限のもとで、設計スレッドはチームの **セッション層**(pane、プロセス、ロール保持、
  ブロッキングダイアログ)を運用します。**workflow 層** — label、queue-state、publish、委譲、
  CI/review ゲート、closeout — は **付与対象ではなく動きません**: intent-cli・GitHub・
  orchestrator が所有し続け、セッションの監督が workflow 遷移を authorize することはありません。
  本セクションはセッションのライフサイクル(死亡判定の前に pane を読む。置き換えは 1 ロール 1
  保持者を守る graceful drop のみで、drop の確認はオペレーターに可視)、**ケイデンス付きの 3 監督
  レイヤー**(リアルタイム message monitor / サブ分オーダーの blocking-UI pane スキャン —
  メッセージを一切出さない failure mode / 数十分オーダーの state watchdog)、そして
  **re-arm ルール** — 監督スケジューラーはセッションスコープで設計セッションとともに静かに死ぬため、
  各レイヤーは再起動を生き延びるか新セッションの最初の行動として re-arm される — を文書化します。
  ブロッキングダイアログは **verified-read ルール** に加え、closed な 4 項目の MAY リスト
  (各項目に検証条件付き)と closed な 4 カテゴリの MUST-ESCALATE リストに支配され、credential・
  security・permission の待ちは事前承認の有無にかかわらず **決して** 回答できません。境界文:
  *セッションの詰まりを解くことは、そのセッションの代わりに決定することではない。*

### stall 検出の完成(G543、G544、G545、G546)

v0.5.0 は最初の informational な stalled-work kind を出荷し、「フィールドデータが出るまで」
しきい値を意図的に持たせませんでした。フィールドデータが到着し、本サイクルは残る死角を塞ぎます。

- **`backlog-ready-idle`**(G544) — 対象ドメインの WIP が空で、`issue publish-flow` の preflight
  自身が使う **同じ canonical セレクター** が publish 可能(`issue-cut-ready`)な候補を報告し、かつ
  `runs.jsonl` に少なくとも `--backlog-idle-minutes`(既定 45)の間 activity が記録されていない
  ときに fire します。新しいヒューリスティクスはありません — publish 可否の判断はゲートを含む
  既存セレクターそのものです。まったく corroborate できない execution unit は除外ではなく
  WIP-empty チェックを **保守的にブロック** します(ここでは誤った "idle" 報告こそが危険な方向
  だからです)。`runs.jsonl` の欠落・パース不能は年齢を推測せず `excluded[]` へ fail-closed。
  G524 の wake contract も拡張され、`backlog-ready-idle` が actionable な間は wake を終了できません。
- **canonical な issue-level blocked 表現 + `claimed-but-silent` の除外**(G545) —
  `claimed-but-silent` は `queue-state.json` を参照するようになり、queue item が `state=blocked` の
  ユニットを決して flag しません。GitHub の label がまだ reconcile されていない blocked ユニットは
  代わりに新しい informational kind **`blocked-label-drift`** として reconcile コマンド名とともに
  報告されます。新しい canonical コマンド `automation issue-block --repo <r> --issue <n> --reason
  <text> [--write]`(および `--clear`)が `intent-issue-blocked` label を適用します — queue-state の
  `blocked` 遷移に GitHub を一致させる唯一のサポート手段で、生の `gh` label 編集はしません。
  この label は canonical palette に加わり、`label-palette-audit`/`-sync` も provision・検証します。
  claimed かつ非 blocked でしきい値超過の silent なユニットは引き続き `claimed-but-silent` を fire
  することを、no-weakening fixture が証明します。
- **`repair-stalled`**(G546) — `intent-pr-request-update`、`intent-pr-update-in-progress`、
  `intent-pr-rereview-ready` のいずれかを持つ PR が `--repair-silent-minutes`(既定 180)を超えて
  観測可能な activity を持たない場合、informational から **actionable**(`stalled: true`)へ昇格
  します。推奨は常に **責任スレッドへの status check** — 2 つの repair label は `implement`、
  `rereview-ready` は `review-dispatch` — であって、遷移や再割り当てでは決してありません。沈黙だけ
  では repair が成功した・失敗した・所有者から取り上げるべきだ、のいずれも成立しないからです。
  draft PR は独立した収集経路を持つため、PR が二重に報告されることはなく、repair 中の draft が
  不可視になることもなくなりました。フィールド証拠: 2026-07-23 に claim された repair が implement
  セッションの死後 **4 日間** 沈黙し続けたにもかかわらず、`stalled-work` は `stalled=false` を報告
  し続けました。
- **priority enum の整合**(G543) — 共有される `QueuePriorityClassification` が、文書化された
  `high|normal|low` enum と、候補セレクターが遭遇しうるあらゆる値(フィールドで観測された
  `medium` のような legacy な enum 外の値を含む)のランク付けの、単一の source of truth になりました。
  ランク規則自体は不変(未知の値は `normal` と同じランク)で、`intent next-slice` の内部に private に
  重複していたものが共有・文書化・fixture で pin されるようになりました。新しい read-only な
  `queue priority-drift` は priority 値ごとの item 数を報告し、安定した形のため `high`/`normal`/`low`
  を常に列挙し、enum 外の値を flag します。`queue-state.json` も `runs.jsonl` も変更しません。
  `queue transition` が既存の enum 外 priority を副作用で書き換えないことも回帰テストで固定しました。

### durable state の完全性(G542、G548)

- **`automation runs-audit` + ドメインスコープの publish 検証**(G542) — 新コマンド
  `automation runs-audit [--repo <r>] [--domain <d>] [--write] [--apply-inferred]
  --format json|markdown` が、`runs.jsonl` の不正な行を 1 パスで全件報告します: 行番号、欠落必須
  フィールド、所有ドメイン、そして無損失に導出できる場合の record 内修復。`--write` は
  **record 内** の修復のみを適用し(`ts` はその record 自身の `timestamp` から、`execution_unit` は
  文書化された 2 つの行形における `wip[0].eu` / `stage1.eu` から)、修復ごとに `runs-repair` 監査
  イベントを 1 件追記し、無関係なバイトをすべて保存します。record 内に導出元が無いフィールドは常に
  `non_derivable` であり、別個の明示的な `--apply-inferred` フラグのみが peer-convention による
  推測を適用し `derivation: inferred-peer-convention` として記録します — 2 つの導出クラスが混ざる
  ことはありません。publish 検証は **ドメインスコープ** になりました: `issue publish-flow` は
  `runs.jsonl` を 1 行ずつパースし、**別ドメイン** に属する不正行は hard block ではなく
  `runs-audit` を名指しする warning になります。同一ドメインまたはドメイン解決不能な行は従来どおり
  fail-closed です。
- **queue-state の no-item-loss 不変条件と stale-base 再適用**(G548) — `queue-state.json` は
  マルチドメイン host において全ドメインが共有する 1 ファイルであり、複数のチェックアウトから並行に
  書き込まれます。従来はどの canonical writer もファイル全体を deserialize し、メモリ上で変更し、
  全体を再 serialize していたため、read-modify-write の競合は単に衝突するのではなく、stale な
  メモリ上のコピーに欠けていたものを **静かに消去** していました。いまや **19 件** の canonical
  な変更がすべて 1 つの共有永続化層を通り、3 つの保証を提供します: **stale-base 検出と再適用**
  (呼び出し側が読んだ base を persist 時点のディスク上の状態と同じ serializer ラウンドトリップで
  比較するため、単なる整形ゆらぎを並行書き込みと誤認しません。不一致時は呼び出し側の item 単位の
  delta を fresh state に再適用し、その再適用を呼び出し側へ報告するので不可視になりません)、
  **no-item-loss 不変条件**(ディスク上に存在するが outgoing state に無く、expected removal として
  名指しもされていない execution unit があれば書き込みを中止し、該当ユニットと canonical な復旧
  経路を名指ししたうえでファイルを一切変更しません)、**item スコープの再適用**(再適用は delta が
  カバーするユニットと `updated_at` のみに触れ、無関係な item は fresh state の順序のまま
  バイト同一で引き継がれます)。retire や completed-item ライフサイクルなど正当な削除は対象ユニットを
  expected removal として渡すため、不変条件が対象とするのは **要求されていない** 損失のみです。
  2026-07-23 の、クロスドメインの書き込みが 1 時間前に seed された item を落とし、4 日間不可視の
  まま循環的な復旧デッドロックを生んだインシデントを閉じます。

### 運用ガイダンス(G539)

- **design-thread watchdog が RECOMMENDED な既定のセーフティネット**(G539) — 設計スレッドから
  30 分オーダーの間隔で回す watchdog ループが `automation heartbeat` を呼び、`stale=true` のときに
  canonical な nudge を最大 1 通送り、それ以外は沈黙します。**orchestrator 側の長間隔 automation**
  (同じ heartbeat 呼び出しを orchestrator 自身のスレッドから 30〜60 分オーダーで回す)は選択可能な
  ALTERNATIVE として文書化され、loopless か hop が 1 つ少ないかのトレードオフが明示されます。
  `automation heartbeat` / `automation stalled-work` の挙動は不変でスケジューラー非依存のままです。
  watchdog の安全ルールと legacy な 5 分の orchestrator フォールバックタイマーはそのまま保存されます。

> #### v0.5.0 の外部スケジューラー推奨を置き換えます
>
> v0.5.0 時点のガイダンスは **外部 cron/launchd の OS スケジューラーによる heartbeat runner** を
> 推奨していました。G539 はこの推奨を **retire** します。理由はフィールド証拠とともに記録されて
> います: cron コンテキストからは credential store / keychain に到達できないため、runner は
> **5 日間連続で毎回 silent に失敗** し(2026-07-15..07-20)、`automation stalled-work` が正しく
> 検出していたにもかかわらず 105 分の stall が復旧されず、人間の ping でようやく表面化しました。
> 外部スケジューラーはそもそも agmsg モデルの外側にも位置します。ときどき再起動するが
> **壊れたときに可視** な watchdog のほうが、誰かがログを見に行くまで不可視に走り続けるものより
> 強い保証です。現行ガイダンス:
> [agent メッセージオーケストレーション — design-thread watchdog](12-agent-message-orchestration.md#design-thread-watchdog推奨されるセーフティネット)。

## 本リリースで retire される workaround

フィールドの利用者が運用してきた文書化済みの workaround を、本リリースは不要にします。
`v0.6.0` インストール後:

- **title-convention workaround** — 不要になります。execution unit の識別は title 単独を信用せず
  実際の packet/queue linkage で corroborate されます(v0.5.0 の G532 で出荷済み。本リリースは
  周辺の検出カバレッジを完成させ、この workaround に残っていたフォールバック的役割もなくします)。
- **top-level `domain:` フィールドの重複** — 不要になります。ドメイン解決とドメインスコープの
  publish 検証(G542)が文書化された解決順で所有ドメインを読むため、別ドメインの不正行で
  publish-flow の検証が転ばないようにするための互換フィールド重複は必要ありません。
- **queue-state の手編集による復旧** — retire されます。G548 の no-item-loss 不変条件と stale-base
  再適用が、手編集を必要にしていた silent な損失のクラスを塞ぎ、`automation runs-audit`(G542)と
  canonical な queue 遷移が、従来手段の無かった修復経路をカバーします。
- **repair stall の手動 ping** — retire されます。`repair-stalled`(G546)が、`--repair-silent-minutes`
  経過後の silent な repair claim を status-check 推奨付きの actionable な item に昇格させるため、
  stall した repair が人間の気づきに依存しなくなります。`blocked-label-drift`(G545)も同様に、
  blocked ユニットの GitHub label の手動 reconcile を canonical コマンド名の提示に置き換えます。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.0
```

または
[v0.6.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.0)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.5.0 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.6.0
```

本リリースは **互換性に配慮しており意図として非破壊** ですが、いくつかのスライスは既出コマンドの
挙動を変更します。従来の挙動に依存している場合は以下を必ず確認してください:

- **追加のみ・対応不要**: 新コマンド(`automation runs-audit`、`queue priority-drift`、
  `automation issue-block`)はオプトインのサーフェスであり、新しい stalled-work kind
  (`backlog-ready-idle`、`blocked-label-drift`、`repair-stalled`)は既存の read-only サーフェスに
  より細かい分類を追加するだけです。
- **是正的 — 既出コマンドの挙動変更**:
  - **G545** — `automation stalled-work` は queue item が `state=blocked` のユニットについて
    `claimed-but-silent` を報告しなくなります。すべての `claimed-but-silent` を要 nudge として
    扱っていた呼び出し側は item が減り、blocked だが未 reconcile のユニットは代わりに
    `blocked-label-drift` として現れます。
  - **G546** — repair/rereview label を持つ PR が `--repair-silent-minutes` を超えると、
    informational ではなく **actionable**(`stalled: true`)になります。`stalled == true` で
    フィルタしていた呼び出し側にはこれらの item が新たに見えます。
  - **G542** — `issue publish-flow` は **別ドメイン** に属する不正な `runs.jsonl` 行で hard block
    しなくなり、warning を出して `runs-audit` を名指しします。同一ドメイン・ドメイン解決不能な行は
    従来どおり fail-closed です。
  - **G548** — canonical な queue-state 書き込みは、落とすことになるユニットを名指しした
    no-item-loss エラーで **中止** されうるようになりました(従来は成功して静かに失っていました)。
    base が stale な書き込みは fresh state に対して再適用され、その再適用が **報告** されます。
    これは意図した是正ですが、queue-state 書き込みが常に成功すると仮定していた呼び出し側は
    中止を扱う必要があります。
  - **G543** — priority の順序付け自体は挙動不変ですが、共有・文書化されました。
    `queue priority-drift` は従来見えなかった legacy な enum 外の値(例: `medium`)を可視化します。
- **ドキュメント上の位置づけ変更**: orchestrator モードはどこにおいても preview/experimental とは
  記述されなくなりました(G540/G541)。timer-loop モードは不変で、引き続き完全にサポートされます。

package id・ライセンス・CLI の引数/フラグ形の変更はありません。上記の是正的変更はいずれも、
各コマンド自身の文書化された意図に挙動を一致させるものです。

## リリース準備ゲート(G551)

以下は `v0.6.0` の **GitHub Release を publish する前に** 成立していなければなりません。
このゲートは fail-closed です — 1 つでも満たされないなら Release を publish しないでください。

- [ ] リリース対象の全 packet が **完了し、その PR が `main` にマージ済み**:
      G539, G540, G541, G542, G543, G544, G545, G546, G548, G549, G550(および本 G551
      release-notes prep)。host/review 側で host queue-state / GitHub PR 状態を用いて確認します —
      child implementation loop は parent queue-state を読めないため、これは host 所有の前提条件です。
- [ ] **G547 が terminally retired であることの確認** と、本リリースに何も寄与しないこと。
      その後継が本 G551 パケットです。
- [ ] 本リリース向けの open PR / WIP packet が誤って漏れていないこと(publish 前に host queue /
      open PR 一覧を確認)。
- [ ] `eng/version.json` が `stableVersion` `0.5.0`、`nextVersion` `0.6.0`(リリース対象)を示すこと。
      `release.yml` は publish された Release/タグから package version を組み立て、
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` は同じポリシーからローカル既定を導出します。
- [ ] package メタデータが正しいこと: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system` を指し、
      `PackageLicenseExpression = Apache-2.0`、README/docs のリンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされていること。
- [ ] リリースノート / README が 4 スレッドオーケストレーションを **PRIMARY** モデル、timer-loop
      モードを完全にサポートされた代替として記述し、preview/experimental の表現が **残っていない**
      こと(G540/G541)。
- [ ] **Main CI が green**(`Build and test (source contract)`)であること、および **preview-pack**
      ワークフローが green であること。
- [ ] **マージ後の build + pack 証跡** がマージコミットに対して PR に記録されていること
      (G528/G538 の準備ゲートに準拠)。

## v0.6.0 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。version-bump マージ自体は
GitHub Release もタグも作成しません。

1. 本 version bump がマージされ上記の準備ゲートが成立した後、**メンテナ/オペレーター(または外部の
   リリース automation)が `v0.6.0` の GitHub Release を作成・publish** します(リリースコミットに
   タグ付け)。これはマージ後の host/オペレーター/外部のアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を
   発火させ、NuGet package とプラットフォーム別バイナリアーカイブ(`.sha256` チェックサム付き)を
   build・publish し、トリガーとなった Release に添付します。

publish 後の検証(GitHub Release が publish され `release.yml` が実行された後):

- [ ] NuGet.org の package ページのリンクがすべて正しく解決すること。
- [ ] GitHub release の成果物リンク(`.tar.gz`、`.zip`、`.exe`、`.nupkg`)にアクセスできること。
- [ ] `.sha256` チェックサムがダウンロードした成果物と一致すること。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`(または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.0`)の後、
      `intent-cli --version` が `0.6.0` を報告すること。
- [ ] バイナリ成果物のスモークチェック: プラットフォームアーカイブをダウンロードし `.sha256` を
      検証、展開して `./intent-cli --version` → `0.6.0`。
- [ ] **新コマンドのスモーク**: `intent-cli automation runs-audit --domain <d> --format json`、
      `intent-cli queue priority-drift --format json`、`intent-cli automation issue-block --help`
      がいずれも実リポジトリに対してクラッシュせず実行できること。
- [ ] **stall 検出のスモーク**(G544/G545/G546): `intent-cli automation stalled-work --domain <d>
      --repo <r> --format json` が該当時に新 kind `backlog-ready-idle` / `blocked-label-drift` /
      `repair-stalled` を報告し、`repair-stalled` が `stalled: true` を持つこと。
- [ ] **queue-state 完全性のスモーク**(G548): ディスク上の state に余分なユニットを含む fixture に
      対する canonical な queue 変更が、そのユニットを名指しした no-item-loss エラーで中止し、
      ファイルを一切変更しないこと。
- [ ] **provisioning / supervision ガイダンスのスモーク**(G549/G550): `intent-cli guide
      orchestrator-thread --format markdown` が `Terminal-workspace provisioning` と
      `Design-thread workspace supervision` の両セクションをレンダリングすること。
- [ ] オペレーターに `v0.6.0` GitHub Release の publish を通知し、続いて
      sekiban-as-a-service-orch に対して
      [本リリースで retire される workaround](#本リリースで-retire-される-workaround)
      に列挙した workaround を `v0.6.0` インストール後に外すよう通知すること。
- [ ] ローカル preview/dry-run の version メタデータを `0.6.0` の次の開発ラインにすること
      ([バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後手順に従い
      `eng/version.json` をバンプ): `stableVersion → 0.6.0`、`nextVersion → 0.6.1`。この bump は
      本パケットではなく **次の** release-prep パケットに委ねられます。
