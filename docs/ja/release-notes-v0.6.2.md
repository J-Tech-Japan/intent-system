# リリースノート — intent-cli v0.6.2

> **リリースモデル:** メンテナ/オペレーター(または外部のリリース automation)が `v0.6.2` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、ノートを author するだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲートg558) と
> [v0.6.2 の publish](#v062-の-publish) を参照。

## v0.6.2 の内容

v0.6.2 は **patch リリース** で、`v0.6.1` 以降にマージされた 3 スライス — **G555**、
**G556**、**G557** — のみをカバーします。minor ではなく patch なのは、CLI サーフェスを
変えるものが何もないためです: G555 と G556 は guide の追記であり、G557 は
テスト/リリースフローの hotfix です。新コマンドはなく、引数の削除・改名もありません。
package id は `JTechJapan.IntentSystem.Cli` のままで、package id・ライセンス・workflow
セマンティクスの変更はありません。

3 つのうち 2 つは同じ日のフィールドインシデントを閉じるもので、残る 1 つは、その修正自体が
生んだ失敗を閉じるものです。

### 共有マシン上での cross-project isolation(G555)

provisioning と supervision のガイダンスは **1 つの** チームの構築と維持を説明していましたが、
同じマシン上で他チームも動いていることには触れていませんでした。オペレーターインシデント
(2026-07-29): 複数のプロジェクトチームが同時に動いている状況で、あるプロジェクトの設計
スレッドが別プロジェクトのリソースを破壊し、オペレーターが手で介入する必要がありました。
同種のニアミスは同じ週の前半にも起きており、回避できたのは「kill の前に pid ごとの cwd を
確認する」という場当たり的な規律のおかげでした — その規律は 1 つのセッション記録の中にしか
存在していませんでした。

`guide orchestrator-thread` に cross-project isolation セクションが追加されます。監督
スレッドが行動できる **オブジェクト** を自チームのものに絞るだけで、**何をしてよいか** は
変えないため、監督の権限境界は不変です。

- **mutation の前に attribution** — pane へのキー入力、プロセスの kill、ワークスペースの
  クローズ/再構成、state ファイルの削除・書き換えの前に、**workspace label**、**pane cwd**、
  **process cwd**(pid ごとに読む — プロセス *名* だけで絞った pid 一覧は何も attribute
  しない)、**agmsg の `(team, role)` ファイル命名** の 4 キーで所有を確定します。attribution は
  積極的な確認であって「他人のものだという証拠が無い」ことではなく、**attribution できない
  場合は read-only** です: 見て、報告し、エスカレーションする — 推測で mutate しない。
- **team ごとに 1 ワークスペース**(チーム名でラベル付けし、再利用・借用しない)と
  **チーム専用ロールフォルダー**。folder-scoping の理由も明記: agmsg identity と codex
  bridge はフォルダースコープなので、他チームのフォルダーで起動した agent は *相手の*
  identity と delivery を乗っ取ります。
- **共有 substrate の所有テーブル** — ワークスペースマネージャーのサーバー(ワークスペース
  単位。サーバーには触れない)、agmsg run ディレクトリ(`(team, role)` ファイル単位。
  ディレクトリごと消さない)、codex app-server(フォルダー単位。cwd で確認)、host repo
  (domain パス単位)について、共有の単位と所有ルールを明示します。
- **非破壊的な復旧** — 他プロジェクトの破損した成果物は保全して脇に置き(壊れた成果物も
  所有者にとっては証拠)、自分のものは作り直します。**復旧の既定は cleanup ではなく
  recreate です。**

### verified liveness — startup report は readiness ではない(G556)

フィールドインシデント(2026-07-29): 2 体の codex agent が startup-complete を report した
**数秒後** に、共有していたリモート app-server が websocket の transport reset で失われ、
両方の TUI が shell プロンプトへ落ちました。それでも監督している設計スレッドは
「startup report を待っている」と言い続け、その時点で全 agent は既に死んでいました。
provisioning のフローは readiness ping で終わっており、report の **後** の再検証を要求して
おらず、supervision の pane スキャンは blocking ダイアログは列挙していたものの、agent が
いるべき場所に shell プロンプトが出ている pane は対象にしていませんでした。

- **verified liveness。** ロールが provisioned となるのは、startup report が届き **かつ**
  **settle delay** の後に次の 3 つがまだ通る場合です: pane が依然 agent の TUI をホストして
  いる(pane を読む — pane が ground truth であり、メッセージは過去についての主張)、
  agmsg の ping-pong 往復が **今** 成功する、そして codex では bridge が armed で app-server
  attachment が安定している。settle delay は load-bearing です: この失敗は report の
  **数秒後** に起きるため、即座に検証しても report が述べたのと同じ瞬間を再観測するだけです。
- **early death は normal mode** であり、シグネチャも命名されました — **transport reset** が
  app-server 接続を落とした結果、TUI が resume ヒントを残して shell プロンプトへ抜けます。
  ただの端末に見える pane になるため、ダイアログだけを探すスキャンでは見逃します。チェックが
  失敗したら **再チェックして復旧し、次の report を待ちません**: 死んだ agent は何も送らない
  ので、待つことは永遠に待つことです。
- **`agent-absent`** が supervision の pane スキャン一覧に blocking ダイアログと同等の
  stuck state として加わり、dialog handling ではなく state に応じてルーティングされます —
  答えるべきダイアログが存在しないからです。復旧は **shim 経由の relaunch**(pane の対話
  シェルにタイプし、死んだのが app-server ならそれを再作成)に続いて **verified-liveness の
  全手順** をやり直すことです。permission mode は起動後の切り替えではなく **launch フラグ**
  で設定します: 合成キー注入は mode 切り替えに使えません — shift+tab のような modifier
  chord は忠実に届かないためです(複数チームで観測)。
- **共有 app-server の death mode** — app-server を kill すると **attach しているすべての
  TUI が一斉に落ちます**。kill と無関係な他チームの agent も含めてです。予防策は G555 の
  attribution ルールで、これは attribution 違反の二次被害です: 被害者は kill したプロセス
  ではなく、それに attach していたすべてです。

### リリースフローの堅牢化(G557)

v0.6.1 のリリース後 version roll が初めて実運用されたとき、child main は 4 つのチェックで
red になりました: 3 つのテストが `stableVersion` / `nextVersion` の組を **値で** 固定して
おり、G475 のガードは `nextVersion` が指すバージョンのリリースノートの存在を要求するため
です。無関係な PR が red な main を継承して凍結され、この修正が着地するまで解除されません
でした。

- **version-agnostic な assertion。** バージョンの組をリテラルで固定するのは、*必須の
  繰り返しステップ* が変更することになっているフィールドに対する assertion としては誤りです —
  固定すれば、正しい roll が必ずテストを壊します。3 つの assertion は共有ソースを介して
  `eng/version.json` から導出するようになり、あらゆる roll をまたいで成立する property を
  assert します: policy がパースでき、release-to-be-cut が published stable より厳密に前で
  あること。**roll シミュレーション fixture**(次 patch / minor ロールオーバー / major
  ロールオーバー)が green のままであることを証明するため、この回帰は「直した」だけでなく
  「表現できる」状態になりました。
- **DRAFT stub の仕組み。** roll は同じ commit で、明確に **DRAFT** と記した
  `release-notes-v<nextVersion>.md` stub を作成するようになりました。G475 のガードは
  **意味を変えずに** 満たされ(存在要求はそのままで、roll 時点で満たせるようになっただけ)、
  stub 自身の契約により release-prep が埋めるまで Release をブロックします。
  *(本ノートが v0.6.2 の release-prep です。)*
- **roll ルールの完成。** リリース closeout チェックリストに、version bump と同じ commit で
  stub を作ること **および** push 後に child main の CI が green であることを roller が検証
  する最終ステップが加わりました: red な main はそれを継承するすべての無関係な PR を
  ブロックするため、roll は CI が green になって初めて完了です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.2
```

または
[v0.6.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.2)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.6.1 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.6.2
```

本リリースは **guide サーフェスへの追加** と **リリースフローの是正** です。新コマンドはなく、
引数/フラグの変更もありません。

- **追加のみ — ガイダンスであり対応不要。** G555 と G556 は
  `intent-cli guide orchestrator-thread` の出力(および ja/en のオーケストレーションドキュメント)に
  新しいセクションとフィールドを追加します。従来 guide を利用していたものの挙動は変わらず、
  単に内容が増えるだけです。共有マシンを監督している、またはチームを provisioning する場合は
  **新しい 2 セクションを読んでください** — 他チームに障害を出したインシデントが記録されています。
- **是正的 — リリースフローのみ。** G557 は本リポジトリ自身のリリースツーリングの assert 方法と
  リリース後 roll の実施方法を変更します。影響を受けるのはリリースをカットするメンテナであり、
  CLI の利用者ではありません:
  - version policy の assertion がリテラルのバージョン組を固定しなくなったため、正しい roll が
    それらを壊すことはなくなりました;
  - リリース後の roll は DRAFT ノート stub を作成し、完了とみなす前に green CI の確認を
    要求するようになりました。

package id・ライセンス・CLI の引数/フラグ形の変更はありません。

## リリース準備ゲート(G558)

以下は `v0.6.2` の **GitHub Release を publish する前に** 成立していなければなりません。
このゲートは fail-closed です — 1 つでも満たされないなら Release を publish しないでください。

- [ ] リリース対象の全 packet が **完了し、その PR が `main` にマージ済み**:
      G555(PR #1214)、G556(PR #1218)、G557(PR #1216)、および本 G558 release-prep。
      host/review 側で host queue-state / GitHub PR 状態を用いて確認します — child
      implementation loop は parent queue-state を読めないため、これは host 所有の前提条件です。
- [ ] **本ノートが draft でなくなっていること。** G557 の stub 契約は、DRAFT バナーが残っている
      間は Release をブロックします。本ファイルがそれを置き換えることがブロック解除です。
- [ ] 本リリース向けの open PR / WIP packet が誤って漏れていないこと(publish 前に host queue /
      open PR 一覧を確認)。
- [ ] `eng/version.json` が `stableVersion` `0.6.1`、`nextVersion` `0.6.2`(リリース対象)を
      示すこと。**本パケットでは変更していません。**
- [ ] package メタデータが正しいこと: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system` を指し、
      `PackageLicenseExpression = Apache-2.0`、README/docs のリンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされていること。
- [ ] **Main CI が green**(`Build and test (source contract)`)であること、および
      **preview-pack** ワークフローが green であること。
- [ ] **マージ後の build + pack 証跡** がマージコミットに対して PR に記録されていること
      (G528/G538/G551/G554 の準備ゲートに準拠)。

## v0.6.2 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。本ノートのマージ自体は
GitHub Release もタグも作成しません。

1. 本パケットがマージされ上記の準備ゲートが成立した後、**メンテナ/オペレーター(または外部の
   リリース automation)が `v0.6.2` の GitHub Release を作成・publish** します(リリース
   コミットにタグ付け)。これはマージ後の host/オペレーター/外部のアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`(`on: release: published`)を
   発火させ、NuGet package とプラットフォーム別バイナリアーカイブ(`.sha256` チェックサム付き)を
   build・publish し、トリガーとなった Release に添付します。

publish 後の検証(GitHub Release が publish され `release.yml` が実行された後):

- [ ] NuGet.org の package ページのリンクがすべて正しく解決すること。
- [ ] GitHub release の成果物リンク(`.tar.gz`、`.zip`、`.exe`、`.nupkg`)にアクセスできること。
- [ ] `.sha256` チェックサムがダウンロードした成果物と一致すること。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`(または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.2`)の後、
      `intent-cli --version` が `0.6.2` を報告すること。
- [ ] バイナリ成果物のスモークチェック: プラットフォームアーカイブをダウンロードし `.sha256` を
      検証、展開して `./intent-cli --version` → `0.6.2`。
- [ ] **guide スモーク**(G555/G556): `intent-cli guide orchestrator-thread --format markdown` が
      `Cross-project isolation on a shared machine` セクションと `Verified liveness` サブ
      セクションの両方をレンダリングすること。
- [ ] **`eng/version.json` を今すぐ ROLL する** — G554 のルール(G557 による改訂版)に従い、
      `stableVersion → 0.6.2`、`nextVersion → 0.6.3` を、**新しい DRAFT
      `release-notes-v0.6.3.md` stub(EN/JA)と同じ commit で** 適用し、その後
      **child main の CI が green であることを検証** してから roll 完了とすること。
      [バージョンフロー](09-developer-reference.md#バージョンフロー) を参照。
- [ ] `v0.6.2` の publish **および** 検証が完了したことを、オペレーターと下流の利用者へ通知
      すること。(publish の依頼自体は上記のリリース前フェーズに属します。この時点では
      Release は既に publish 済みです。)
