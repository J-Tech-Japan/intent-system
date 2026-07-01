# リリースノート — intent-cli v0.3.15

> **リリースモデル:** メンテナ/オペレーター（または外部のリリース automation）が `v0.3.15` の
> **GitHub Release を作成・publish** します — version-bump マージ自体は Release やタグを作成しません。
> GitHub Release の publish が `.github/workflows/release.yml`（`on: release: published`）を発火させ、
> NuGet package とプラットフォームバイナリ成果物を build・publish します。本パケットは
> **prepare-only** で、version メタデータと docs をバンプするだけで publish ステップを **追加しません**。
> [マージ前 リリース準備ゲート](#リリース準備ゲート-g519) と
> [v0.3.15 の publish](#v0315-の-publish) を参照。

## v0.3.15 の内容

v0.3.15 は **パッチリリース** で、`v0.3.14` 以降に完了した 2 件の orchestrator/agmsg 運用修正、
agmsg `Monitor` ツールが欠落した場合の Claude project-settings 診断（G517）と、orchestrator モードの
タイマーをメッセージ駆動の定常状態＋任意の design-side watchdog へシフトする変更（G518）を出荷します。
package id・ライセンス・workflow セマンティクスの変更はなく、既存の timer-loop モードは完全サポート・
不変です。package id は `JTechJapan.IntentSystem.Cli` のままです。

> **orchestrator モードは引き続き preview/experimental です。** オプトインで、まだ hardening 中で
> あり、デフォルトの workflow ではありません。intent-cli と GitHub が権威 source of truth であり
> 続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### agmsg Monitor 欠落時の Claude project-settings 診断（G517）

- dogfooding により、`ToolSearch select:Monitor` が Claude Code の `Monitor` ツールを **まったく**
  見つけられない失敗モードが判明しました — これは G511/G516 が扱う「`1 shell` vs `1 monitor`」の
  delivery-mode 混同とは異なる、より手前の失敗です。ツール自体が存在しない場合、orchestrator-thread
  ガイドはまず（`delivery.sh status` が何を報告していても）**Claude Code のツールサーフェス問題**
  として扱うようになりました。agmsg delivery のデバッグはその後です。
- ガイドに **known-good 比較チェックリスト**（`1 monitor` が既に動いているフォルダーと、
  `.claude/settings.json`、`.claude/settings.local.json`、`~/.claude.json` の project trust/onboarding
  フラグ、有効/無効な MCP server リスト、project-level `env` 設定を diff する）、dogfooding で
  観測された **疑わしい project-level `env` override**（`CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`、
  `CLAUDE_CODE_ENABLE_TELEMETRY`、`DISABLE_ERROR_REPORTING`、`DISABLE_TELEMETRY`）、および
  **安全な修復手順**（オペレーターアクション: セッションを閉じ、agmsg の SessionStart フックを
  保ったまま疑わしい `env` override を削除/隔離し、再度開いて Monitor の success marker を再検証する）
  を追加しました。intent-cli 自身は `.claude/settings.json` や `~/.claude.json` を編集しません。

### orchestrator モードのタイマーを design-side watchdog へシフト（G518）

- orchestrator-thread ガイドは、**通常の定常状態をメッセージ駆動** として枠組みし直しました:
  implementation/review receiver はすでに accepted/progress/completed/blocked の返信を orchestrator に
  送っており、その返信が orchestrator を起こすため、既定では高速な定期 orchestrator ループは不要に
  なりました。明示的な orchestrator タイマー（Codex automation 5 分ごと、または Claude 同一スレッド
  `/loop 5m`）は引き続き **サポート** されますが、オプトインの **fallback/legacy ポーリング**
  オプションとしてのみです。
- メッセージ駆動の定常状態に推奨されるセーフティネットとして、新しい **任意・低頻度の
  design-side watchdog** を追加しました: design/HITL（human-in-the-loop）inbox と orchestrator の
  停滞を確認し、canonical な repair/status リクエストを **最大 1 通** 送り、backlog と
  human-decision キューの両方がなくなったら停止/アーカイブします。watchdog の安全ルールは、
  重複した delegation、permission プロンプトのクリア、進行中作業のキャンセル/リセット、
  issue/PR の強制クローズ、推測的な durable-state の手術を明示的に **禁止** します — watchdog は
  メッセージを送り read-only な事実を読むだけです。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.14`,
> `nextVersion: 0.3.15` を記録します。G519（本パケット）はリリース準備のメタデータバンプです。
> v0.3.15 リリース後のメタデータ前進（`stableVersion → 0.3.15`, `nextVersion → 0.3.16`）は
> オペレーターのリリース後ステップであり、本パケットのスコープ外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.15
```

または [v0.3.15 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.15)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.3.14 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.15
```

v0.3.14 からの破壊的変更はありません。orchestrator モードはオプトインのままで、既存の timer-loop
セットアップには影響しません。明示的な orchestrator タイマーは引き続き fallback/legacy ポーリング
オプションとして利用できます。

## リリース準備ゲート (G519)

次の項目は **`v0.3.15` の GitHub Release が publish される前** に成り立っている必要があります。
このゲートは fail closed です — 1 つでも未充足なら、まだ Release を publish しないでください。

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G517、G518（および
      release-notes 準備の G519）。host queue-state / GitHub PR 状態で host/review 側から確認する
      （child 実装ループは parent queue-state を読まないため、これは host 所有の前提条件）。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされていない
      （publish 前に host queue / open PR リストを確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.15`（意図したリリースバージョン）。`release.yml`
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

## v0.3.15 の publish

本パケットはリリースを publish せず、publish ステップを **追加しません**。version-bump マージ自体は
GitHub Release やタグを作成しません。

1. この version bump がマージされ上記の準備ゲートが成り立った後、**メンテナ/オペレーター（または
   外部のリリース automation）が `v0.3.15` の GitHub Release を作成・publish** します
   （リリースコミットにタグ付け）。これはマージ後の host/operator/外部リリースアクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`（`on: release: published`）を
   発火させ、NuGet package とプラットフォームごとのバイナリアーカイブ（`.sha256` チェックサム付き）を
   build・publish し、トリガーとなった Release に添付します。

リリース後の検証（GitHub Release が publish され `release.yml` が実行された後）:

- [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
- [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）にアクセスできる。
- [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.15`）後、
      `intent-cli --version` が `0.3.15` を報告する。
- [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
      展開して `./intent-cli --version` → `0.3.15`。
- [ ] **Missing-Monitor 診断スモーク**（G517）: `intent-cli guide orchestrator-thread
      --domain <d> --target-repo <repo> --agent <agent> --format markdown` が、**Monitor tool vs
      delivery-mode** の下に **Missing-Monitor project-settings diagnosis** サブセクション
      （known-good 比較チェックリスト、疑わしい `env` override、安全な修復手順）をレンダリングする。
- [ ] **design-side watchdog スモーク**（G518）: 同じガイド出力が、メッセージ駆動の定常状態
      （orchestrator タイマーは fallback/legacy のみ）として枠組みされた **Scheduled orchestrator
      cadence** セクションと、新しい **Design-side watchdog (optional safety net)** セクション
      （頻度、HITL/停滞チェック、repair/status リクエスト最大 1 通、停止条件、重複 delegation・
      permission プロンプトのクリア・キャンセル・強制クローズ・durable-state の手術を禁止する
      安全ルール）をレンダリングする。
- [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.15` の次の開発ラインを使う
      （[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
      `eng/version.json` をバンプ）: `stableVersion → 0.3.15`, `nextVersion → 0.3.16`。
