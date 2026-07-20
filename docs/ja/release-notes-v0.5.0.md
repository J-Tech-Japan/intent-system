# リリースノート — intent-cli v0.5.0

> **リリースモデル:** メンテナ/オペレーター(または外部のリリース automation)が `v0.5.0` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、version メタデータと docs をバンプするだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲート-g538) と
> [v0.5.0 の publish](#v050-の-publish) を参照。

## v0.5.0 の内容

v0.5.0 は **minor リリース** で、`v0.4.0` 以降に完了した 9 件のスライス(G529–G537)を
カバーします。patch ではなく minor バンプなのは、**2 つの新しいコマンド**
(`intent facet-check`、`queue reprioritize`)、**新しい intent-tree schema サーフェス**
(`facets`)、**新しい stalled-work kind**、**新しい transition target**(`retired`)を
出荷するためで、どれも通常のメンテナンス以上のものです。package id は
`JTechJapan.IntentSystem.Cli` のままで、package id・ライセンス・workflow セマンティクスの
変更はありません。

> **orchestrator モードは引き続き preview/experimental です。** オプトインで、まだ hardening 中で
> あり、デフォルトの workflow ではありません。intent-cli と GitHub が権威 source of truth であり
> 続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### semantic facets(G529–G531)

オペレーター issue #1159 に端を発するこの 3 スライスのイニシアチブは、intent-tree の
ノードに、それが担う semantic な重みの種類を表す closed かつ 4 値の語彙を与え、その分類を
tooling とレビュアーの双方が消費できるようにします。

- **`facets:` frontmatter**(G529)— ノードは `vocabulary`(event/command 語彙:
  何が fact としてカウントされるか)、`invariant`(不変条件と一貫性の境界)、
  `decider`(decider の判断: コマンドが何を決定するか)、`acceptance-property`
  (壊してはいけないもの)のうち 0 個以上を宣言できます。facets はオプションかつ
  加算的です — どれも annotate されていない tree には影響しません。
- **facet を意識した context 供給**(G530)— `intent-cli context collect` は、
  分類されていない queue-state/clarification/automation-bindings context より前に
  レンダリングされる `## Facet context` セクションを持つようになりました(これは
  semantic な核であり、おまけではありません)。canonical な順序 `vocabulary →
  invariant → decider → acceptance-property` でグループ化されます。`--facets` は
  表示するグループを制限し、`--scope` は指定されたパスに overlap するノードへと
  絞り込みます(双方向対称にチェック)。`intent-cli packet draft` は、scaffold された
  `review-context.md` の中に同じセクションを、packet 自身の `intent_references` に
  スコープして生成します。再実行のたびに、生成ブロックの周囲にある手書きの文章には
  一切触れない fail-closed な marker-pair プロトコルによって最新に保たれます。
- **`intent facet-check`**(G531)— change proposal を、G530 が到達可能にした facet
  ノードに照らす read-only な lexical scaffold です: proposal の候補となる
  command/event term を既存の `vocabulary`/`invariant` ノードと突き合わせ、
  レビュアーが命名衝突やカバレッジのギャップを早期に把握できるようにします。
  明示的に semantic verifier では **ありません**し、決して gate にも **なりません**
  — matching は lexical であり、false negative は想定内で、コマンドは findings に
  関わらず常に exit `0` します。

### stalled-work の正確性(G532–G533)

downstream adopter に対するフィールドデータ(2026-07-15、2026-07-18)は、
`automation stalled-work` の execution-unit 識別ロジック自体が誤っている場合に
候補を誤分類することを示していました。

- **execution-unit と domain の識別**(G532)— candidate execution unit は今や、
  issue/PR タイトルの LEADING ID token であり、必須の right boundary を持ちます。
  実在の `.intent-cli/issues/<token>/packet.yaml` が裏付ける場合にのみ信頼され、
  それ以外の場合は candidate は各 packet 自身が宣言する `source_execution_unit`
  と照合されます。domain の確認は、他のあらゆる execution-unit を解決するサーフェスと
  同じ `--domain` > packet-declared > fail-loud の順序を適用しますが、これは
  execution unit 自体が実際の packet/queue linkage によって裏付けられている
  candidate に対してのみです — 裏付けの無い candidate は、黙って推測されるのではなく
  除外されます(`domain-underivable`)。
- **informational な stalled-work kind**(G533)— 3 つの新しい kind
  (`repair-pending`、`rereview-pending`、`claimed-but-silent`)がそれぞれ
  `is_informational: true` を持ち、`recommended_action` は記述的なプローズです
  (transition コマンドでは決してありません)。修理中の PR が誤った
  `review-start` 推奨とともに `pr-created-not-reviewing` として誤報告された
  正確なフィールドインシデントを修正します。`claimed-but-silent` は、使用不能な
  活動タイムスタンプデータに対して `createdAt` から推測するのではなく、
  `excluded[]` へ fail closed します。

### queue の頑健性(G534)

実際の hand-authored な packet と queue state に対する 3 つの関連したフィールド発見を
まとめて修正しました:

- **`queue enqueue` は両方の YAML list-item 慣習を受け入れます** — 4 スペース +
  `"- "` という renderer の慣習と、より一般的な 2 スペースの慣習の両方が、
  カラム数ではなく内容によって認識されます。
- **`queue transition --to retired` が queue-state エントリを backfill します** —
  新しい guarded かつ idempotent、terminal な transition で、`automation
  issue-retire` の拒否セマンティクスと整合する独自の entry point
  (`QueueManager.Retire`)を持ちます: `Completed` 以外のあらゆる state から合法;
  mutate する前に紐づく PR の実際の GitHub 状態を検証(merge または close が
  確認された場合は拒否); 既に `Retired` であれば idempotent; そして terminal —
  一度 retire された item は二度と他の state に transition できません。
- **publish selector(`intent next-slice`)は queue-state と packet-lifecycle の
  エビデンスを明示的に組み合わせます** — どちらか一方のシグナルが retirement を
  記録していればその unit は除外されます。両者の矛盾は、どちらか一方向に黙って
  解決されるのではなく、実行可能な `lifecycle-metadata-diagnostic` 警告として
  表面化するようになりました。

### label supersession(G535)

フィールド発見 #5(SKS-G824 / PR #1760): `automation pr-transition
--transition request-update` は修理ラベルを追加しましたが、既存の
`intent-pr-rereview-ready` をそのままにしていました — `worker claim` は
rereview-ready な PR を正しく拒否するため、2 つの canonical なルールの間で
デッドロックが発生し、インストール済みのどのコマンドも先に進めませんでした。
`request-update` は今や、`intent-pr-request-update` を追加するのと **同じ** write
の中で `intent-pr-rereview-ready`(とそのレガシー文字列形式)をクリアします —
repair request は常に pending な rereview-readiness に優先します。

### publish の信頼性(G536)

`issue publish-flow` の idempotent な再実行は、GitHub issue・queue-state
エントリ・`runs.jsonl` イベントという 3 つの durable artifact すべてを、1 つの
シグナル(例えば GitHub issue が存在すること)を信頼して他の 2 つも無傷だと
みなすのではなく、独立に検証・復元するようになりました。issue は作成されたが
queue-state エントリが欠落しているような部分的な過去の失敗は、re-run 時に
silently に完了扱いされるのではなく検出・修復されます。本当に回復不能な矛盾は
覆い隠されるのではなく fail loud します。

### priority override(G537)

新しい `queue reprioritize <execution-unit> --priority <high|normal|low>
--reason <text> [--write]` コマンドは、queued かつ未 publish の execution unit の
priority を、durable かつ concurrency-safe な audit protocol の下で設定します:

- `intent next-slice` は今や、eligible な publish candidate を
  **priority class を優先**(high > normal > low)して選択し、class 内では
  authoring order による安定した tiebreak を行います — dependency/WIP/
  clarification/lifecycle の gate は常に priority に優先します。高 priority
  だが gate でブロックされている candidate が、eligible なより低い priority の
  candidate より優先して選ばれることはありません。
- 各成功した `--write` は、queue item 上に記録される durable かつ単調増加する
  `priority_revision` カウンタによって識別されます(wall-clock time でも
  content fingerprint でもありません — どちらも試みられましたが、それぞれ
  clock の非単調性と genuine な state の再訪によって破綻しました)。これにより
  retry は常に「自分自身の以前の試み(再追記をスキップして安全)」と
  「本当に新しい mutation」と「矛盾する claim」を区別でき、後者 2 つに対しては
  fail closed します。
- `--write` は、authoritative な read の前に non-blocking な OS-level exclusive
  lock(sibling file `queue-state.reprioritize.lock` に対する `FileShare.None`)
  を取得し、write の critical section 全体にわたってそれを保持します —
  同じ lock を取得できなかった concurrent な invocation は、race するのではなく
  即座に fail closed します。dry-run は決して mutate せず、決して lock を
  取得しません。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.5.0
```

または [v0.5.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.5.0)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.4.0 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.5.0
```

本リリースは **compatibility-conscious かつ non-breaking な意図** で作られていますが、
すべての変更が純粋に加算的というわけではありません — いくつかのスライスは既に
出荷済みのコマンドの挙動を修正しており、その修正済みの挙動に依存しているコンシューマーは
注意して読んでください:

- **加算的で対応不要**: 2 つの新しいコマンド(`intent facet-check`、
  `queue reprioritize`)はオプトインのサーフェスであり、`facets:` frontmatter は
  オプションの annotation であり、G533 の新しい stalled-work kind は既存の
  read-only なサーフェスへより細かい分類を追加するだけです。
- **修正的 — 既存コマンドの挙動が変わります**:
  - **G535** — `automation pr-transition --transition request-update` は今や、
    同じ write の中で古い `intent-pr-rereview-ready` ラベルもクリアします。
    以前は `request-update` がそのラベルに触れないことを期待していた呼び出し元
    (例えば事後に手動で再適用していた場合)は、そのラベルが消えていることに
    気づくはずです。
  - **G536** — `issue publish-flow` の idempotent な再実行は、1 つのシグナルを
    信頼して 3 つすべてを推測するのではなく、3 つの durable artifact すべてを
    独立に検証・復元するようになりました。以前は部分的に失敗した先行実行に対して
    黙って no-op していた再実行が、今後は追加の書き込みを行う(あるいは
    fail loud する)ことがあります。
  - **G532/G534** — `automation stalled-work` の execution-unit/domain 識別は
    より厳格になりました(裏付けの無い candidate は、誤って帰属させられる
    可能性があった従来と異なり、今後は除外されます)。`intent next-slice` の
    retirement エビデンスの組み合わせもより厳格になりました(以前は一方向に
    黙って解決されていた lifecycle/queue-state の矛盾が、今後は candidate を
    除外し診断を発生させます)。以前は緩く一致していた candidate が、今後は
    除外またはフラグ付けされる可能性があります。

package id・ライセンス・CLI の引数/フラグの形状に変更はありません。上記の
修正的な変更はいずれも、新しいコマンドサーフェスや廃止されたサーフェスではなく、
各コマンド自身の documented intent に挙動を合わせるためのバグ修正です。

**sekiban-as-a-service-orch のフィールドワークアラウンド一式**(SKS-G8xx:
title-convention workaround、重複したトップレベルの `domain:` フィールド、
queue-state hand-edit recovery)を運用しているコンシューマーは、本リリースを
インストール後、3 つすべてを廃止できます — G532 の execution-unit 識別は
もはや title-convention workaround を必要とせず、G534 の `queue transition
--to retired` は hand-edit recovery パスを置き換え、重複したトップレベルの
`domain:` フィールドの互換エイリアス(以前のリリースから既にサポート済み)が
残るケースを今後もきれいにカバーします。

## リリース準備ゲート(G538)

次の項目は **`v0.5.0` の GitHub Release が publish される前** に成り立っている必要があります。
このゲートは fail closed です — 1 つでも未充足なら、まだ Release を publish しないでください。

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G529、G530、
      G531、G532、G533、G534、G535、G536、G537(および release-notes 準備の G538)。
      host queue-state / GitHub PR 状態で host/review 側から確認する(child 実装
      ループは parent queue-state を読まないため、これは host 所有の前提条件)。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされて
      いない(publish 前に host queue / open PR リストを確認)。
- [ ] `eng/version.json` の `nextVersion` が `0.5.0`(意図したリリースバージョン)。
      `release.yml` は publish された Release/タグから package バージョンをビルドし、
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` も同じポリシーからローカル
      デフォルトを導出する。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされている。
- [ ] リリースノート / README が **orchestrator モードは preview/experimental** かつオプトインで
      あること、timer-loop モードが不変であることを保っている。
- [ ] リリースコミットで **Main CI が green**(`Build and test (source contract)`)であり、
      **preview-pack** workflow も green。
- [ ] マージコミットでの **post-merge build + pack のエビデンス** が PR に記録されている
      (G528 の readiness gate を踏襲)。

## v0.5.0 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。version-bump マージ自体は
GitHub Release やタグを作成しません。

1. この version bump がマージされ上記の準備ゲートが成り立った後、**メンテナ/オペレーター(または
   外部のリリース automation)が `v0.5.0` の GitHub Release を作成・publish** します
   (リリースコミットにタグ付け)。これはマージ後の host/operator/外部リリースアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を
   発火させ、NuGet package とプラットフォームごとのバイナリアーカイブ(`.sha256` チェックサム付き)を
   build・publish し、トリガーとなった Release に添付します。

リリース後の検証(GitHub Release が publish され `release.yml` が実行された後):

- [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
- [ ] GitHub release アセットリンク(`.tar.gz`, `.zip`, `.exe`, `.nupkg`)にアクセスできる。
- [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`(または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.5.0`)後、
      `intent-cli --version` が `0.5.0` を報告する。
- [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
      展開して `./intent-cli --version` → `0.5.0`。
- [ ] **新コマンドスモーク**: `intent-cli intent facet-check --domain <d>
      --terms Example --format json` と `intent-cli queue reprioritize --help` が
      実リポジトリに対してクラッシュせず実行できる。
- [ ] **Facet context スモーク**(G530): `intent-cli context collect --domain <d>
      --format json` が `## Facet context` セクション(または annotate されたノードが
      無い tree に対する `facet_context_note` の graceful-degradation ノート)を
      レンダリングする。
- [ ] **stalled-work classification スモーク**(G532/G533): `intent-cli
      automation stalled-work --domain <d> --repo <r> --format json` が該当する
      場合に新しい `repair-pending` / `rereview-pending` / `claimed-but-silent`
      kind を、それぞれ `is_informational: true` 付きで報告する。
- [ ] **queue retirement スモーク**(G534): `intent-cli queue transition
      <execution-unit> retired --help` と `intent-cli queue enqueue --help` が
      実リポジトリに対してクラッシュせず実行できる。
- [ ] **priority override スモーク**(G537): `intent-cli queue reprioritize
      --help` と `intent-cli intent next-slice --help` がクラッシュせず実行できる。
      priority-class-first の順序は
      [09-developer-reference.md](09-developer-reference.md#canonical-な-publish-order-override--queue-priority-g537)
      に記載。
- [ ] `v0.5.0` の GitHub Release を publish するようオペレーターに通知し、`v0.5.0`
      インストール後に sekiban-as-a-service-orch へ 3 つの documented workaround を
      廃止するよう通知する。
- [ ] ローカル preview/dry-run のバージョンメタデータが `0.5.0` の次の開発ラインを使う
      ([バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
      `eng/version.json` をバンプ): `stableVersion → 0.5.0`, `nextVersion → 0.5.1`。
      このバンプは今回のパケットではなく **次の** リリース準備パケットに委ねられます。
