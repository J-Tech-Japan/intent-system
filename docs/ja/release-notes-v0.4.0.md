# リリースノート — intent-cli v0.4.0

> **リリースモデル:** メンテナ/オペレーター(または外部のリリース automation)が `v0.4.0` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、version メタデータと docs をバンプするだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲート-g528) と
> [v0.4.0 の publish](#v040-の-publish) を参照。

## v0.4.0 の内容

v0.4.0 は **minor リリース** で、`v0.3.15` 以降に完了した 8 件のスライス(G520–G527)を
カバーします。patch ではなく minor バンプなのは、**3 つの新しい automation コマンド**と、
domain 解決における目に見える **fail-loud な挙動変更**を出荷するためで、どちらも通常の
メンテナンス以上のものです。package id は `JTechJapan.IntentSystem.Cli` のままで、
package id・ライセンス・workflow セマンティクスの変更はありません。

> **orchestrator モードは引き続き preview/experimental です。** オプトインで、まだ hardening 中で
> あり、デフォルトの workflow ではありません。intent-cli と GitHub が権威 source of truth であり
> 続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### 新しい automation コマンド: stalled-work, heartbeat, issue-retire

これら 3 つのコマンドは、orchestrator モードのパイロット(sekiban-as-a-service-orch、
2026-06-28..07-14)が、検知もリカバリもできないサイレントなパイプラインスタールに
大きな時間を失ったことから生まれました。

- **`automation stalled-work`**(G523)— 保留中のパイプライン transition を年齢付きで
  一覧する read-only な棚卸しコマンドで、3 つのカテゴリがあります: `published-not-
  delegated`(OPEN issue が `intent-target` を持つが claim も PR もまだ無い)、
  `pr-created-not-reviewing`(closing PR に `review-start` transition が欠けている)、
  `merged-not-closed-out`(MERGED PR の紐づく queue item がまだ `Completed` でない)。
  各項目は execution unit・年齢・実行すべき正確な canonical な次のコマンドを報告します。
  GitHub mutation も queue-state/runs.jsonl への書き込みもありません。
- **`automation heartbeat`**(G526)— `stalled-work` をラップし、何かがスタールしている
  場合はスタールした各項目とその推奨アクションを示す送信可能な reconcile メッセージを
  返します。健全な場合は沈黙した JSON(`stale: false`、`message_body` なし)を返します。
  自分でメッセージを送ったり agent を起動したりすることは一切ありません — orchestrator-
  thread ガイドはこれを **推奨セーフティネット** として文書化しています: セッションに
  依存しない外部スケジューラ(cron/launchd、60 分クラス)を fail-closed なコピペ用
  ラッパーとともに使い、design-side watchdog と in-session の fallback タイマー
  (どちらも同じフィールドトライアルで実測された弱点があります — 詳細な比較はガイドの
  「External heartbeat」セクションを参照)よりも優先します。
- **`automation issue-retire`**(G525)— authored 通りには決して開始できない
  published `intent-target` issue(例: slice を decompose する必要が判明した場合)の
  ための canonical かつ atomic な transition です。issue を「not planned」として
  close し、workflow label を除去し、queue-state エントリを `retired` としてマーク
  します — エントリが存在しなければ新規作成するため、正当に retire されたユニットに
  ついて `metadata validate` が queue entry の欠落を報告することはありません。open な
  紐づき PR、アクティブな claim、既に `Completed` な作業に対しては fail closed し、
  partial write のリトライは行き詰まるのではなく、durable な provenance marker を
  通じて安全に収束します。

### fail-loud な domain 解決 — 移行に関する注記

**(G522、G523/G525 でさらに強化)** execution-unit を解決するサーフェス —
`automation queue-seed-from-packet`、`review closeout-plan`、
`automation publish-recovery`、`automation stalled-work`、`automation
issue-retire` — は、次の厳密な順序で domain を解決するようになりました:

1. 明示的な `--domain` が優先されます(解決対象の packet.yaml の `domain:` フィールドと
   矛盾する場合はエラー);
2. それ以外の場合は packet-declared な `domain:` フィールドが使われます;
3. どちらも無い場合、サーフェスは **fail loud** し、candidate domains(`intents/*/`
   からスキャン)と正確な `--domain` re-invocation を示します。

**これは目に見える挙動変更です。** 以前は、これらのサーフェスの一部は、どちらの
シグナルも得られない場合に host の config-default domain binding へ黙って
フォールバックしていました — マルチドメインの host では、これにより execution unit が
誤った domain に帰属してしまう可能性がありました。**その黙ったフォールバックに
依存していたスクリプトや automation がある場合**、それらは今後、誤った domain に
対して黙って成功するのではなく、fail loud するようになります。`--domain` を明示的に
渡すか、該当する packet.yaml が `domain:` フィールドを宣言していることを確認して
対応してください。

### orchestrator wake contract(G524)

フィールドデータは約 60 時間の「publish してから sleep する」スタールと、88 分間の
サイレントな completion のギャップを示しました。orchestrator-thread ガイドは、
すべての wake で次を要求するようになりました:

- **同じ wake 内で publish と delegate を行う** — delegation を「次の wake」に
  持ち越すことはもうありません(他に何も、確実に orchestrator を起こして delegation を
  送らせる仕組みはありません);
- メッセージ上限は、一律の「1 通のみ」ルールではなく **「wake ごと・receiver ごとに
  最大 1 通の delegation」** と再定義されました — publish + その delegation +
  repair メッセージ + エスカレーション + receiver report の処理はすべて 1 回の
  wake 内で許可されます;
- 新しい **end-of-wake の `automation stalled-work` チェック**(never-defer・
  escalate-instead-of-defer ルール付き);
- receiver の completion/blocked レポートは、すべての delegation の
  **REQUIRED FINAL STEP** になり、期待される正確な JSON 形状とともに明記されました;
- 送信のたびに **dispatch roster を検証**(`team.sh`)し、死んだ `review` vs
  `reviewer` アドレスへのフィールドで観測されたメッセージ損失を解消します。

### managed review worktree + design-alignment チェック(G520)

review worktree は managed root(`.intent-cli/worktrees/review-<unit>`)配下で
強制されるようになり — 生の `/tmp/...` パスはもう使われません — stale/dirty/
未登録の worktree は、オペレーターへの `rm -rf` 承認プロンプトではなく、構造化された
blocker 返信になります。review completed 返信は、design alignment(packet、
review-context、intent tree、ADR/decision note)が実際に確認されたエビデンスを
示さない限り、incomplete 扱いになります。

### Codex monitor(beta)ガイダンス(G521)

agmsg Codex bridge 向けの Codex monitor setup preflight と、3 つの
troubleshooting エントリ(silent launcher — 複数 identity、static TUI — 古い
app-server thread とその完全なリカバリ手順、doubled responses — bridge の二重化)を
追加しました。

### packet-yaml parser 修正(G527)

`PreparedPacketYamlScalarParser` の quote-balance チェックが、値中のすべての
quote 文字を数える方式ではなく、delimiter-aware になりました — double-quoted
scalar の balance は double quote のみに依存し(内部のアポストロフィは通常の
コンテンツ)、single-quoted scalar の balance は single quote のみに依存します
(YAML の `''` エスケープを尊重)。これにより、`automation queue-seed-from-packet`
が、アポストロフィだけを含む double-quoted 値を持つ有効な packet を 2 回誤って
拒否したフィールドインシデントを修正しました。本当に unterminated あるいは
曖昧な quoting は引き続き fail closed します。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.15`,
> `nextVersion: 0.4.0` を記録します。G528(本パケット)はリリース準備のメタデータ
> バンプです。v0.4.0 リリース後のメタデータ前進(`stableVersion → 0.4.0`,
> `nextVersion → 0.4.1`)はオペレーターのリリース後ステップであり、本パケットの
> スコープ外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.4.0
```

または [v0.4.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.4.0)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.3.15 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.4.0
```

**アップグレード前に、上記の fail-loud な domain 解決の移行に関する注記を、これらの
コマンドをスクリプト化している automation について必ず確認してください。** それ以外は
すべて加算的な変更です: 3 つの新しいコマンドはオプトインのサーフェスであり、
orchestrator/review ガイドの変更は orchestrator モードをオプトインしたオペレーターに
のみ影響します。既存の timer-loop セットアップには影響しません。

## リリース準備ゲート (G528)

次の項目は **`v0.4.0` の GitHub Release が publish される前** に成り立っている必要があります。
このゲートは fail closed です — 1 つでも未充足なら、まだ Release を publish しないでください。

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G520、G521、
      G522、G523、G524、G525、G526、G527(および release-notes 準備の G528)。
      host queue-state / GitHub PR 状態で host/review 側から確認する(child 実装
      ループは parent queue-state を読まないため、これは host 所有の前提条件)。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされて
      いない(publish 前に host queue / open PR リストを確認)。
- [ ] `eng/version.json` の `nextVersion` が `0.4.0`(意図したリリースバージョン)。
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

## v0.4.0 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。version-bump マージ自体は
GitHub Release やタグを作成しません。

1. この version bump がマージされ上記の準備ゲートが成り立った後、**メンテナ/オペレーター(または
   外部のリリース automation)が `v0.4.0` の GitHub Release を作成・publish** します
   (リリースコミットにタグ付け)。これはマージ後の host/operator/外部リリースアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を
   発火させ、NuGet package とプラットフォームごとのバイナリアーカイブ(`.sha256` チェックサム付き)を
   build・publish し、トリガーとなった Release に添付します。

リリース後の検証(GitHub Release が publish され `release.yml` が実行された後):

- [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
- [ ] GitHub release アセットリンク(`.tar.gz`, `.zip`, `.exe`, `.nupkg`)にアクセスできる。
- [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`(または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.4.0`)後、
      `intent-cli --version` が `0.4.0` を報告する。
- [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
      展開して `./intent-cli --version` → `0.4.0`。
- [ ] **新コマンドスモーク**: `intent-cli automation stalled-work --domain <d>
      --repo <r> --format json`、`intent-cli automation heartbeat --domain <d>
      --repo <r> --format json`、`intent-cli automation issue-retire --help` が
      実リポジトリに対してクラッシュせず実行できる。
- [ ] **fail-loud な domain 移行スモーク**(G522): `domain:` を宣言していない packet に
      対して、対象サーフェスを `--domain` なしで実行すると、host の config-default
      domain へ黙って解決するのではなく、candidate domains を示して fail loud する。
- [ ] **External heartbeat ガイドスモーク**(G526): `intent-cli guide
      orchestrator-thread --domain <d> --target-repo <repo> --agent <agent>
      --format markdown` が、**Design-side watchdog (alternative safety net)**
      セクションより前に **External heartbeat (recommended safety net)**
      セクション(頻度、コマンド、コピペ用ラッパー、1 回の実行につき最大 1 通の
      ルール)をレンダリングする。
- [ ] **wake contract ガイドスモーク**(G524): 同じガイド出力が、end-of-wake の
      `automation stalled-work` チェック、「wake ごと・receiver ごとに最大 1 通の
      delegation」という枠組み、dispatch roster 検証ステップをレンダリングする。
- [ ] ローカル preview/dry-run のバージョンメタデータが `0.4.0` の次の開発ラインを使う
      ([バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
      `eng/version.json` をバンプ): `stableVersion → 0.4.0`, `nextVersion → 0.4.1`。
