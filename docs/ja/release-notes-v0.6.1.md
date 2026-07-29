# リリースノート — intent-cli v0.6.1

> **リリースモデル:** メンテナ/オペレーター(または外部のリリース automation)が `v0.6.1` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、version メタデータと docs をバンプするだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲートg554) と
> [v0.6.1 の publish](#v061-の-publish) を参照。

## v0.6.1 の内容

v0.6.1 は **patch リリース** で、`v0.6.0` 以降にマージされた 2 スライス — **G552** と
**G553** — のみをカバーします。minor ではなく patch なのは、どちらのスライスも新しい
コマンドサーフェスを出荷しないためです: G553 は gate のバグ修正で、G552 は既存の
`automation stalled-work` / `automation heartbeat` の検出サーフェスと orchestrator-thread
guide を拡張するものです。minor バンプは新コマンドサーフェスと広範な挙動変更のために
留保されています。package id は `JTechJapan.IntentSystem.Cli` のままで、package id・
ライセンス・workflow セマンティクスの変更はありません。

どちらのスライスも実測されたフィールドインシデントを閉じるもので、フィールドの利用者は
両方を待っています。

### design 判断による hold が可視かつ bounded になりました(G552)

design スレッドの不在がパイプラインを不可視に stall させうる状態でした。field incident、
2026-07-28 16:11 → 07-29 01:29: 技術チェックがすべて green のまま、1 行の wording 判断の
ために review が final verdict を **9 時間** 保留しました。保留項目は機械的に事実確認可能で
両スレッドとも答えを知っていましたが、権限委譲が存在せず、しかも hold は agmsg メッセージ上に
しか存在しなかったため、`automation stalled-work` はその間ずっと `stalled: false` を報告して
いました(field record で 4 件目の design 不在 stall)。

- **clarification-backed hold。** design の判断でブロックされた orchestrator/reviewer の
  hold は、canonical な clarify surface を通じて clarification artifact として記録されます。
  `clarify open` に任意の明示入力が追加され、OPEN artifact が **実際の** 内容を運べるように
  なりました: `--question` は artifact の `QuestionText` に入り packet 由来の synthesis より
  常に優先され、`--recommended-answer` と `--evidence` は既存の serialized field である
  `Reason` にラベル付きで格納されます。**clarification の schema 変更はなく**、3 つとも省略
  すれば従来の packet 由来挙動そのままです。agmsg だけの hold は **contract violation** です:
  メッセージ上にしか存在しないブロックは、あらゆる supervision レイヤーから見えません。
- **`design-decision-pending` の検出。** `automation stalled-work` が domain の OPEN な
  clarification artifact を読み、age(artifact 自身の `createdAt` 由来)・ブロックされている
  execution unit・質問サマリつきで報告します。`automation heartbeat` は他の kind と同様に
  `message_body` でそれを運びます。推奨アクションは回答すべき clarification を正確に名指しし、
  オペレーターへのエスカレーション経路も併記します。**自動回答は決してしません**。これは
  自分自身の GitHub エンティティを持たない唯一の kind であり、まさにそれが検出対象の stall が
  不可視だった理由です。両方向に fail closed: 読めない artifact は「回答済み」として飛ばさず
  パス付きで除外し、packet が別 domain を宣言する clarification は帰属させず除外します。
  回答(または applied / cancelled)で item は消えます。
- **bounded default authority。** オペレーターは、判断ではなく *リポジトリの事実を確認する*
  ことで決着する 4 つの列挙された判断クラス — 件数・列挙の訂正、引用された事実から導かれる
  wording 訂正、相互参照の訂正、名指しした canonical source との識別子不一致 — を事前委譲
  できます(各クラスに検証条件つき)。あらゆる方向に bounded です: **付与される**(前提では
  ない)、**列挙されている**(その一覧がスコープのすべて)、既存の `clarify record --from-file`
  sink による **証拠ログ**(Question / Decision / Rationale が `## Recently Resolved` に入り、
  post-hoc amendment のために読める形で残る)、design による **後からの修正が可能**、そして
  **決してセマンティックではない** — intent shaping・packet 内容・リリーススコープ・優先度は
  常に design へ行きます。
- **定期的な design リマインダーループ。** clarification が open である間、orchestrator が
  既存の長間隔 automation からリマインダーを再送します — 新しいスケジューラーは不要 —
  30〜60 分オーダーの間隔で、open な clarification 1 件につき 1 間隔あたり最大 1 通、回答
  されたら停止します。
- **refined reviewer hold ルール。** 技術チェックが green かつ保留項目が非セマンティックで
  事実確認可能なら、付与された権限のもとで証拠をログして解決します。それ以外は記録された
  clarification と可視な pending state になります。reviewer が単に待つ、という第 3 の選択肢は
  ありません。

### queue で blocked のユニットが WIP gate を枯渇させなくなりました(G553)

`automation host-review-preflight` は OPEN な `intent-target` issue を blocked かどうかに
関わらずすべて `in_flight_issues` にカウントしていました。field finding
(sekiban-as-a-service、2026-07-26、0.5.0 上): gate が issue #1783 を挙げて
`skip-next-slice-due-to-wip` を返しましたが、そのユニット SKS-G818 は claim を保持したままの
supported な block transition で parked されていました。blocked のユニットは設計上 parked で
あり unblock されるまで進行できないため、それをカウントすることは、オペレーターが意図的に
work を脇に置いたまさにそのときに publish を枯渇させます。G545 は blocked ユニットを
`claimed-but-silent` から除外していましたが、それは stalled-work 側だけで、この gate は
カバーされていませんでした。

- queue item が **converged blocked state**(queue `state=blocked` **かつ** `blocked_by` が
  非空)にある issue は `in_flight_issues` から除外されます。in-flight が blocked のものだけに
  なった時点で next-slice candidate は `skip-next-slice-due-to-wip` から `candidate-ready` へ
  切り替わります。
- **convergence は必須で two-sided です。** `state=blocked` なのに `blocked_by` が空、または
  blocked でない item に理由がある状態は G545 の言う **drift** であって exemption では
  ありません。half-converged な item はカウントされ続け(fail-closed)、各方向とも canonical な
  converging コマンド(`automation issue-block … --reason "<why>" --write`、または同コマンドの
  `--clear --write` によるユニット解放)を名指しする warning を出します。
- **exemption が silent になることはありません。** 除外された各ユニットは新しい
  `wip_exempt_blocked_units` diagnostics フィールドに execution unit・issue 番号・
  `blocked_by` の理由つきで現れます(JSON / text 両方の出力)。
- **linkage は queue item 自身の `linked_issue`** — repo **と** number の両方です。blank や
  missing の repo は wildcard ではなく(issue 番号は repository 内でのみ一意)、そのような
  item は exemption をスキップし理由を述べます。
- **読めない host state では fail-closed。** queue-state が存在しない場合は何も exempt せず、
  パースできない場合も何も exempt せず warning を出します。unblock した場合は次の呼び出しから
  即座にカウントが戻ります。
- `intent next-slice` は元々 `active`/`review`/`fixing` の item だけを WIP として数えており
  blocked はカウントしていなかったため、このルールの divergent copy は追加していません。

## リリース後の version roll(新しいフロールール)

2026-07-29 に version flow のギャップがフィールドで問題を起こしました。`v0.6.0` の Release を
publish した後、`eng/version.json` が roll されなかったため preview は `0.6.0-preview.N` の
まま build され続け、これは SemVer 上リリース済みの `0.6.0` より **下** にソートされます。
`dotnet tool update` は新しい build を拒否し、手動での uninstall/install が必要になりました。

修正はフロールールで、リリース closeout チェックリストに組み込まれました:
**GitHub Release を publish して検証した直後に、follow-up commit で `eng/version.json` を
roll する** — `stableVersion` = 今リリースしたバージョン、`nextVersion` = 次の patch。
[バージョンフロー](09-developer-reference.md#バージョンフロー) を参照。

**preview チャンネルを追っている場合**、この新しい規律が始まるのは本リリースからです:
`v0.6.1` の publish 後、オペレーターが follow-up commit で `version.json` を
`stableVersion 0.6.1 / nextVersion 0.6.2` に roll し、以降の preview は `0.6.2-preview.N`
として build されます — リリースより上にソートされるため、`dotnet tool update` が uninstall
なしで再び機能します。このルール以前に生成された preview 成果物は **遡って番号を振り直しません**。
現在 `0.6.0-preview.N` の build に固定されている場合は、Release publish 後に `0.6.1` を
明示的にインストールしてください。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.1
```

または
[v0.6.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.1)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.6.0 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.6.1
```

本リリースは **既存サーフェス内での追加と是正** です。新コマンドはなく、引数/フラグの削除も
ありません。

- **追加のみ・対応不要**: `clarify open` の `--question` / `--recommended-answer` /
  `--evidence` は任意で、省略すれば従来の packet 由来挙動そのままです。新しい
  `design-decision-pending` kind と `wip_exempt_blocked_units` diagnostics フィールドは
  既存出力を変更するのではなく出力を追加します。
- **是正的 — 既出コマンドの挙動変更**:
  - **G553** — `automation host-review-preflight` は converged-blocked の issue を
    `in_flight_issues` にカウントしなくなります。意図的に parked されたユニットが open な間
    `skip-next-slice-due-to-wip` を期待していた呼び出し側は `candidate-ready` を見ることに
    なり、除外されたユニットは `wip_exempt_blocked_units` に名指しされます。half-converged な
    item は不変(引き続きカウント)ですが、修復 warning を出すようになります。
  - **G552** — `automation stalled-work` が新しい actionable kind を報告するため、
    `stalled == true` でフィルタしていた呼び出し側には、open な clarification artifact を持つ
    domain で `design-decision-pending` item が新たに見えます。

package id・ライセンス・CLI の引数/フラグ形の変更はありません。

## リリース準備ゲート(G554)

以下は `v0.6.1` の **GitHub Release を publish する前に** 成立していなければなりません。
このゲートは fail-closed です — 1 つでも満たされないなら Release を publish しないでください。

- [ ] リリース対象の全 packet が **完了し、その PR が `main` にマージ済み**:
      G552(PR #1208)と G553(PR #1210)、および本 G554 release-notes prep。host/review 側で
      host queue-state / GitHub PR 状態を用いて確認します — child implementation loop は
      parent queue-state を読めないため、これは host 所有の前提条件です。
- [ ] 本リリース向けの open PR / WIP packet が誤って漏れていないこと(publish 前に host queue /
      open PR 一覧を確認)。
- [ ] `eng/version.json` が `stableVersion` `0.6.0`、`nextVersion` `0.6.1`(リリース対象)を
      示すこと。`release.yml` は publish された Release/タグから package version を組み立て、
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` は同じポリシーからローカル既定を導出します。
- [ ] package メタデータが正しいこと: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system` を指し、
      `PackageLicenseExpression = Apache-2.0`、README/docs のリンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされていること。
- [ ] **Main CI が green**(`Build and test (source contract)`)であること、および
      **preview-pack** ワークフローが green であること。
- [ ] **マージ後の build + pack 証跡** がマージコミットに対して PR に記録されていること
      (G528/G538/G551 の準備ゲートに準拠)。

## v0.6.1 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。version-bump マージ
自体は GitHub Release もタグも作成しません。

1. 本 version bump がマージされ上記の準備ゲートが成立した後、**メンテナ/オペレーター(または
   外部のリリース automation)が `v0.6.1` の GitHub Release を作成・publish** します
   (リリースコミットにタグ付け)。これはマージ後の host/オペレーター/外部のアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を
   発火させ、NuGet package とプラットフォーム別バイナリアーカイブ(`.sha256` チェックサム付き)を
   build・publish し、トリガーとなった Release に添付します。

publish 後の検証(GitHub Release が publish され `release.yml` が実行された後):

- [ ] NuGet.org の package ページのリンクがすべて正しく解決すること。
- [ ] GitHub release の成果物リンク(`.tar.gz`、`.zip`、`.exe`、`.nupkg`)にアクセスできること。
- [ ] `.sha256` チェックサムがダウンロードした成果物と一致すること。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`(または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.1`)の後、
      `intent-cli --version` が `0.6.1` を報告すること。
- [ ] バイナリ成果物のスモークチェック: プラットフォームアーカイブをダウンロードし `.sha256` を
      検証、展開して `./intent-cli --version` → `0.6.1`。
- [ ] **design 判断の可視化スモーク**(G552): `intent-cli clarify open --help` が
      `--question` / `--recommended-answer` / `--evidence` を表示し、
      `intent-cli automation stalled-work --domain <d> --repo <r> --format json` が
      open な clarification のある domain で `design-decision-pending` を報告すること。
- [ ] **WIP gate スモーク**(G553): `intent-cli automation host-review-preflight --repo <r>
      --format json` が `wip_exempt_blocked_units` フィールドをレンダリングすること
      (何も parked されていなければ空)。
- [ ] **`eng/version.json` を今すぐ ROLL する** — 新しいリリース後ルールに従い、follow-up
      commit で直ちに進めます: `stableVersion → 0.6.1`、`nextVersion → 0.6.2`。これにより
      preview は `0.6.2-preview.N` として build され、リリースより上にソートされます。これを
      飛ばすことが、本リリースが記録している 2026-07-29 の不具合そのものです。
      [バージョンフロー](09-developer-reference.md#バージョンフロー) を参照。
- [ ] オペレーターに `v0.6.1` GitHub Release の publish を通知し、続いて
      sekiban-as-a-service-orch に対して `#1783` クラスの WIP 枯渇が `v0.6.1` インストールで
      解消されることを通知すること。
