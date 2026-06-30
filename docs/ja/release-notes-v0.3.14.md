# リリースノート — intent-cli v0.3.14

> **リリースモデル:** メンテナ/オペレーター（または外部のリリース automation）が `v0.3.14` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`（`on: release: published`）を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、version メタデータと docs をバンプするだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲート-g512) と
> [v0.3.14 の publish](#v0314-の-publish) を参照。

## v0.3.14 の内容

v0.3.14 は **パッチリリース** で、`v0.3.13` 以降に完了した orchestrator モードガイダンス作業
（G508–G511）、orchestrator モードのロール境界（G513）、same-repo bootstrap config ロード修正
（G514）を出荷します。package id・ライセンス・workflow セマンティクスの変更はなく、
既存の timer-loop モードは完全サポート・不変です。package id は `JTechJapan.IntentSystem.Cli`
のままです。

> **orchestrator モードは引き続き preview/experimental です。** オプトインで、まだ hardening 中で
> あり、デフォルトの workflow ではありません。intent-cli と GitHub が権威 source of truth であり
> 続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### セットアップガイドの具体的な agmsg 起動手順（G508）

- orchestrator-thread ガイド（`intent-cli guide orchestrator-thread`）が、**具体的な agmsg
  起動手順** — preflight と貼り付け可能な登録/delivery コマンド、各ロールの最初のプロンプト —
  を生成し、オペレーターが順序を推測せずに orchestrator・implementation・review の各 receiver を
  立ち上げられるようにしました。

### design-thread ハンドオフと monitor recovery（G509）

- ガイドが orchestrator モード向けの **design-thread ハンドオフ** と **monitor recovery**
  チェックリストを文書化しました: receiver の monitor が起動しなかった、メッセージが見えない、
  メッセージ送信後に receiver が起動した場合の対処（`inbox.sh` で読む、登録/delivery を再確認、
  ack 後に再送）。

### セットアップ intake フォームと traffic-controller プレイブック（G510）

- ガイドが **セットアップ intake フォーム**（`missing-inputs` / `setup-ready` / `blocked` の結果）と
  **design traffic-controller プレイブック** をレンダリングし、orchestrator モードを求める design
  スレッドを必要な入力とルーティング/エスカレーションのルールに沿って案内します。

### Monitor ツールと agmsg delivery-mode の区別（G511）

- ガイドと新しい `orchestrator-message-mode` ドキュメントが、Claude Code の汎用 **`Monitor`
  ツール**（agmsg が SessionStart ディレクティブから `watch.sh` を起動して attach する、実際の
  inbox ストリーム配信の仕組み）を、agmsg の `delivery.sh status` `mode=monitor` 設定（Monitor が
  attach されストリーミングしている **証明にはならない**）と区別します。**ライブ attach の
  success-marker** リスト（`ToolSearch select:Monitor` で Monitor が解決される；
  `Monitor(agmsg inbox stream)`；フッター `1 monitor`；`Monitor event`）、**failure-marker**
  リスト（Bash/バックグラウンド `watch.sh` への fallback；フッター `1 shell`；Azure Monitor /
  MCP monitor との混同）、および **project-trust 修復 runbook**（exact-cwd の `~/.claude.json`
  `hasTrustDialogAccepted=false` が Monitor を抑制 → Claude project trust を修復して再起動し、
  再検証）を追加しました。

### orchestrator モードのロール境界（G513）

- orchestrator-thread ガイドが **ロール境界** を encode しました: **design スレッドが packet
  authoring を所有** します — intent shaping、clarification、ADR/設計判断、リリーススコープと
  バージョン選択、packet 内容/受け入れ基準。**orchestrator は ready な packet を coordinate**
  します — canonical な intent-cli/GitHub state を検査し、1 wake につき既に authoring 済みの
  `issue-cut-ready` packet を 1 件だけ publish、implementation/review へ委譲、CI/review を待ち、
  closeout し、durable な設計/リリース成果物を自分で合成せず **blocker と不足 packet を design に
  報告** します。
- 必要な packet が不在、またはプロダクト/リリース/設計判断を要する場合（release-prep を含む:
  design がバージョン/スコープを決め release-prep packet を author する）、orchestrator は packet を
  でっち上げず、構造化された `packet-needed` メッセージを design に送って待ちます。

### same-repo bootstrap config ロード（G514）

- host 側の bootstrap 経由 automation コマンドが、host の `.intent-cli/config.toml` を存在時に
  ロードするようになり、`automation summary`、`automation same-repo-metadata-preflight`、
  `automation queue-seed-from-packet` が effective な same-repo トポロジと metadata/base ブランチ
  設定（`same_repo_topology`、`metadata_source_branch`、`metadata_write_branch`、
  `implementation_base_branch`、`base_branch_policy`）で一致します。以前は bootstrap context が常に
  default config を使っていたため、キーが設定されていても `same-repo-metadata-preflight` が
  `not-configured` を返すことがありました。`.intent-cli/config.toml` を持たない repo は安全な
  default の child/standalone metadata-free bootstrap を保ちます。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.13`,
> `nextVersion: 0.3.14` を記録します。G512（本パケット）はリリース準備のメタデータバンプです。
> v0.3.14 リリース後のメタデータ前進（`stableVersion → 0.3.14`, `nextVersion → 0.3.15`）は
> オペレーターのリリース後ステップであり、本パケットのスコープ外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.14
```

または [v0.3.14 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.14)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.3.13 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.14
```

v0.3.13 からの破壊的変更はありません。orchestrator モードはオプトインのままで、既存の timer-loop
セットアップには影響しません。

## リリース準備ゲート (G512)

次の項目は **`v0.3.14` の GitHub Release が publish される前** に成り立っている必要があります。
このゲートは fail closed です — 1 つでも未充足なら、まだ Release を publish しないでください。

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G508、G509、G510、G511、
      G513、G514（および release-notes 準備の G512/G515）。host queue-state / GitHub PR 状態で
      host/review 側から確認する（child 実装ループは parent queue-state を読まないため、これは
      host 所有の前提条件）。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされていない
      （publish 前に host queue / open PR リストを確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.14`（意図したリリースバージョン）。`release.yml`
      は publish された Release/タグから package バージョンをビルドし、
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` も同じポリシーからローカルデフォルトを導出する。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされている。
- [ ] リリースノート / README が **orchestrator モードは preview/experimental** かつオプトインで
      あること、timer-loop モードが不変であることを保っている。
- [ ] リリースコミットで **Main CI が green**（`Build and test (source contract)`）であり、
      **preview-pack** workflow も green。

## v0.3.14 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。version-bump マージ自体は
GitHub Release やタグを作成しません。

1. この version bump がマージされ上記の準備ゲートが成り立った後、**メンテナ/オペレーター（または
   外部のリリース automation）が `v0.3.14` の GitHub Release を作成・publish** します
   （リリースコミットにタグ付け）。これはマージ後の host/operator/外部リリースアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`（`on: release: published`）を
   発火させ、NuGet package とプラットフォームごとのバイナリアーカイブ（`.sha256` チェックサム付き）を
   build・publish し、トリガーとなった Release に添付します。

リリース後の検証（GitHub Release が publish され `release.yml` が実行された後）:

- [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
- [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）にアクセスできる。
- [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.14`）後、
      `intent-cli --version` が `0.3.14` を報告する。
- [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
      展開して `./intent-cli --version` → `0.3.14`。
- [ ] **orchestrator ガイドスモーク**（G508–G511, G513）: `intent-cli guide orchestrator-thread
      --domain <d> --target-repo <repo> --agent <agent> --format markdown` が、具体的な agmsg
      起動手順、design-thread ハンドオフ / monitor recovery、セットアップ intake フォームと
      traffic-controller プレイブック、success/failure marker と trust 修復 runbook を含む
      **Monitor tool vs delivery-mode** セクション、そして **ロール境界**（design が packet を author、
      orchestrator は coordinate）をレンダリングする。
- [ ] **same-repo bootstrap スモーク**（G514）: same-repo host repo で
      `intent-cli automation same-repo-metadata-preflight` が設定済みの `[project]` metadata ブランチを
      尊重する（`not-configured` ではない）。`automation summary` / `automation queue-seed-from-packet`
      が同じ effective config で一致する。
- [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.14` の次の開発ラインを使う
      （[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
      `eng/version.json` をバンプ）: `stableVersion → 0.3.14`, `nextVersion → 0.3.15`。
