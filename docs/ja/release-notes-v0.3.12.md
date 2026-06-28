# リリースノート — intent-cli v0.3.12

> **メンテナ向けリリースチェックリスト:** [v0.3.12 GitHub Release の作成](#v0312-github-release-の作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g504) を通過するまでタグ付けしないでください。**

## v0.3.12 の内容

v0.3.12 は **パッチリリース** で、`v0.3.11` 以降に完了した 2 つの orchestrator モードプレビュー
修正を出荷します: agmsg receiver の起動順序（G502）と approved-PR ラベルクリーンアップ（G503）。
package id・ライセンス・workflow セマンティクスの変更はなく、既存の timer-loop モードは完全
サポート・不変です。package id は `JTechJapan.IntentSystem.Cli` のままです。

> **orchestrator モードは引き続き preview/experimental です。** オプトインで、まだ hardening 中で
> あり、デフォルトの workflow ではありません。intent-cli と GitHub が権威 source of truth であり
> 続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### agmsg receiver の起動順序（G502）

- orchestrator setup ガイダンス（`intent-cli guide orchestrator-thread`）が、実際の委譲前に
  厳密な起動順序と **ping/ack ハンドシェイク** を要求するようになりました: ロールを join →
  delivery mode 設定 → receiver の CLI セッション起動/再起動 → monitor/bridge のアタッチを待つ →
  セッションがアクティブになった後に ping → ack を要求（または `inbox.sh` で確認）→ その後にのみ委譲。
- receiver が ready になる前に送ったメッセージは agmsg history に保存されても、新しく起動/再起動
  したセッションには **可視に delivery されない** こと（ack のない送信は receiver-not-ready であり
  成功した委譲ではない）を警告し、receiver が initial メッセージ送信後に launch された場合に
  オペレーターが送る貼り付け可能な復旧メッセージを提供します。

### approved PR の label クリーンアップ（G503）

- `approved` PR 遷移が、`intent-pr-reviewing` に加え stale な `intent-pr-rereview-ready`
  （および他の in-flight review ラベル `intent-pr-request-update` / `intent-pr-update-in-progress`）
  を除去するようになり、approved の PR が `intent-pr-approved` と「再レビュー待ち」ラベルを同時に
  可視に持つことがなくなります。これらが不在のときも遷移はべき等です。
- `intent-cli automation reconcile` が、すでに `intent-pr-approved` と stale な in-flight review
  ラベルの両方を持つ PR を検出し、high-confidence な intent-cli 所有のラベルクリーンアップとして
  修復します（生の `gh label` 編集ではありません）。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.11`,
> `nextVersion: 0.3.12` を記録します。G504（本パケット）はリリース準備です。
> v0.3.12 リリース後のメタデータ前進（`stableVersion → 0.3.12`, `nextVersion → 0.3.13`）は
> オペレーターのリリース後ステップであり、本パケットのスコープ外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.12
```

または [v0.3.12 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.12)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.3.11 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.12
```

v0.3.11 からの破壊的変更はありません。orchestrator モードはオプトインのままで、既存の timer-loop
セットアップには影響しません。

## リリース準備ゲート (G504)

次のすべてが成り立つまで `v0.3.12` タグ/リリースを作成しないでください
（このゲートは fail closed です — 1 つでも未充足なら停止し、タグ付けしない）:

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G502・G503
      （および本準備の G504）。host queue-state / GitHub PR 状態で host/review 側から確認する
      （child 実装ループは parent queue-state を読まないため、これは host 所有の前提条件）。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされていない
      （タグ付け前に host queue / open PR リストを確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.12`（意図したリリースバージョン）で、作成する
      タグ（`v0.3.12`）と一致する。リリースワークフローは package バージョンをタグから導出し、
      `-p:Version=` が `src/IntentSystem.Cli/IntentSystem.Cli.csproj` のポリシー由来デフォルトを上書きする。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされている。
- [ ] リリースノート / README が **orchestrator モードは preview/experimental** かつオプトインで
      あること、timer-loop モードが不変であることを保っている。
- [ ] リリースコミットで **Main CI が green**（`Build and test (source contract)`）であり、
      **preview-pack** workflow も green。

## v0.3.12 GitHub Release の作成

1. [リリース準備ゲート](#リリース準備ゲート-g504) を確認 — 未充足項目があれば進めない。
2. リリースコミットにタグ付け: `git tag v0.3.12 && git push origin v0.3.12`。
3. `release.yml` workflow が発火し、バイナリ・`.nupkg`・チェックサムをビルドする（バージョンは
   タグから導出）。green 完了を待つ。
4. workflow が GitHub Release draft を作成する。レビューし、本ファイルの内容をリリース本文として
   貼り付け、publish する。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.12` を push したことを確認する。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）にアクセスできる。
   - [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.12`）後、
         `intent-cli --version` が `0.3.12` を報告する。
   - [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
         展開して `./intent-cli --version` → `0.3.12`。
   - [ ] **orchestrator setup ガイドスモーク**（G502）: `intent-cli guide orchestrator-thread
         --domain <d> --target-repo <repo> --agent <agent> --format markdown` が numbered な
         起動順序・ping/ack 要件・貼り付け可能な復旧メッセージをレンダリングする。
   - [ ] **approved-label 遷移スモーク**（G503）: `intent-cli automation pr-transition
         --transition approved --pr <n> --repo <repo> --format json` が `intent-pr-approved` と
         ともに `intent-pr-rereview-ready` の除去を計画する。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.12` の次の開発ラインを使う
         （[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
         `eng/version.json` をバンプ）: `stableVersion → 0.3.12`, `nextVersion → 0.3.13`。
