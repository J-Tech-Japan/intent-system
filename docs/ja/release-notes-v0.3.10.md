# リリースノート — intent-cli v0.3.10

> **メンテナ向けリリースチェックリスト:** [v0.3.10 GitHub リリースの作成](#v0310-github-リリースの作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g486) を通過するまでタグを打たないこと。**

## v0.3.10 の内容

v0.3.10 は dogfooding 安定性のパッチリリースです。`v0.3.9` 後に完了した 2 つの修正を出荷し、
オリジナルの開発環境外で public/NuGet 版 intent-cli を使う実ユーザー（Estivo）の障害を解消
します: 日本語 Windows での `gh` JSON デコードと、same-repo メタデータブランチの publish-flow
信頼性です。package id・ライセンス・ワークフロー semantics の変更はありません。package id は
`JTechJapan.IntentSystem.Cli` のままです。

### Windows 日本語 `gh` JSON デコード (G484)

- すべての `gh` サブプロセスが stdout/stderr を **周囲の Windows コンソールのコードページに
  依存せず UTF-8 として** デコードするようになりました（cp932/932）。日本語 issue/PR の
  タイトル・本文が valid な JSON のまま保たれるため、`worker next-action --github-only
  --format json`・`worker issue-preflight`・`worker pr-comment-preflight`・host/review preflight
  の各経路が日本語 Windows コンソールで invalid-JSON parse エラーにより壊れなくなりました。
- `chcp 65001` の実行や `$OutputEncoding` / `[Console]::OutputEncoding` の手動設定は **不要**
  です。`gh` のエラー出力も同じく UTF-8 でデコードされ診断が読めます。macOS/Linux の挙動は
  不変です（既に UTF-8）。

### same-repo メタデータの publish-flow 信頼性 (G485)

- `automation queue-seed-from-packet` がドメインの `execution_unit_regex` を、乖離し得る
  重複パーサではなく `automation summary` と host loop が使う **同じ共有 resolver** で解決する
  ようになりました。valid な same-repo packet（コードブランチ `main`、メタデータブランチ
  `main-metadata`）が `missing-domain-binding-regex` で拒否されず、正規の
  `queue-seed-from-packet` → `issue publish-flow` → `automation issue-publish` 経路で
  seed/publish できます。
- 拒否時の診断は、参照した bindings ソースを明示し `automation summary --domain <d>`（同一
  ソース）へ誘導するため、bindings ファイル欠落か空の regex フィールドかを手動 queue-state 編集
  なしで判別できます。
- サポートされる same-repo の `[project]` 設定キー（`same_repo_topology`,
  `metadata_source_branch`, `metadata_write_branch`）と seed → publish 経路を developer
  reference に文書化しました。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.9`,
> `nextVersion: 0.3.10` を記録しており、G486（本パケット）はリリース準備です。v0.3.10 後の
> メタデータ前進（`stableVersion → 0.3.10`, `nextVersion → 0.3.11`）は operator のリリース後
> 手順であり、本パケットの対象外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.10
```

または
[v0.3.10 GitHub リリース](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.10)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを
検証してください。

## v0.3.9 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.10
```

v0.3.9 からの破壊的変更はありません。

## リリース準備ゲート (G486)

以下が **すべて** 満たされるまで `v0.3.10` タグ/リリースを作成しないこと
（このゲートは fail-closed — 1 つでも未達なら停止しタグを打たない）:

- [ ] リリース対象パケットがすべて **完了し PR が `main` にマージ済み**:
      G484, G485（および本準備 G486）。host/review 側で host queue-state /
      GitHub PR state により確認すること — child 実装 loop は親 queue-state を読まないため、
      これは host 所有の前提条件です。
- [ ] 本リリース対象の open な intent-system PR や WIP packet を取りこぼしていないこと
      （タグ付け前に host queue / open PR 一覧を確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.10`（意図したリリースバージョン）で、
      作成するタグ（`v0.3.10`）と一致すること。release ワークフローはタグからパッケージ
      バージョンを導出し、`-p:Version=` が
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` のポリシー導出デフォルトを上書きします。
- [ ] パッケージメタデータが正しいこと: `PackageId = JTechJapan.IntentSystem.Cli`,
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system` を指す,
      `PackageLicenseExpression = Apache-2.0`, README/docs リンクが解決し,
      公式サービスサイト `https://www.intent-driven-development.com/` が README から
      リンクされていること。
- [ ] release コミットで **main CI が green**（`Build and test (source contract)`）で、
      **preview-pack** ワークフローが green であること。

## v0.3.10 GitHub リリースの作成

1. [リリース準備ゲート](#リリース準備ゲート-g486) を確認 — 未達項目があれば進めない。
2. release コミットにタグ: `git tag v0.3.10 && git push origin v0.3.10`。
3. `release.yml` ワークフローが発火し、バイナリ・`.nupkg`・チェックサムをビルド
   （バージョンはタグから導出）。green 完了を待つ。
4. ワークフローが GitHub Release draft を作成。確認し、本ファイルの内容を release body
   として貼り付け、publish。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.10` を push したことを確認。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）が
         アクセス可能。
   - [ ] `.sha256` チェックサムがダウンロードしたアーティファクトと一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.10`）の後、
         `intent-cli --version` が `0.3.10` を報告する。
   - [ ] バイナリアーティファクトの smoke check: プラットフォームアーカイブをダウンロードし、
         `.sha256` を検証、展開して `./intent-cli --version` → `0.3.10`。
   - [ ] **G484 Windows 日本語 smoke**: 日本語 Windows コンソール（cp932）で
         `intent-cli worker next-action --repo <repo> --github-only --format json` が
         日本語タイトルの issue を JSON エラーなく解析する。
   - [ ] **G485 same-repo smoke**: `[project] same_repo_topology = true` ＋
         `metadata_source_branch`/`metadata_write_branch` 設定で、valid な packet が
         `automation queue-seed-from-packet` 後 `issue publish-flow` を通過する。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.10` 後の次の開発ラインを
         使う（[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後手順に
         従い `eng/version.json` を bump）: `stableVersion → 0.3.10`, `nextVersion → 0.3.11`。
