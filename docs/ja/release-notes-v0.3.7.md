# リリースノート — intent-cli v0.3.7

> **メンテナ向けリリースチェックリスト:** [v0.3.7 GitHub リリースの作成](#v037-github-リリースの作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g475) を通過するまでタグを打たないこと。**

## v0.3.7 の内容

v0.3.7 は automation 安全性のリリースです。`v0.3.6` 後に完了した loop-prompt /
review-closeout / next-slice の信頼性修正をまとめ、非デフォルト base branch 上での
実装/レビュー loop を正しく動作させ、`issue-published` の queue-state 行が review
closeout を阻害しないようにし、インストール済み `intent-cli guide` の出力を古いローカル
rule docs より優先させ、absorbed/superseded パケットが重複 issue として再発行されるのを
防ぎます。package id・ライセンス・ワークフロー semantics の変更はありません。package id
は `JTechJapan.IntentSystem.Cli` のままです。

### 非デフォルト実装 base branch を loop prompt で first-class に (G471)

- loop-prompt の本文と review ガイダンスが、`main` を前提とせず非デフォルトの実装 base
  branch を first-class なケースとして扱うようになりました。生成される child/host loop
  prompt と base-branch ポリシー ガイダンスが実際に設定された base を保持するため、child
  実装エージェントは host メタデータを読まずに正しい PR base を選択でき、review ガイダンスも
  正しい base の PR を誤判定しません。

### `issue-published` の queue 行が review closeout を阻害しない (G472)

- review-closeout の parsing が、state が `issue-published` の queue-state 行を許容する
  ようになり、publish 済みで未 PR の行が closeout 読み取りを中断しなくなりました。host
  review loop ガイダンスは skill-free（closeout はインストール済み `intent-cli` surface
  のみを経由）で、CI-pending の結果は stop ではなく defer 条件として扱います。

### インストール済み `intent-cli guide` を古いローカル rule docs より優先 (G473)

- loop prompt 生成時、インストール済み `intent-cli guide` の出力がローカルの
  `intents/rules/automations/*.md` rule docs より優先されます。child/host loop と
  one-shot ガイダンスで hard rule が明示されました: operator がローカル rule docs を
  名指ししても読まない — インストール済み guide が source of truth であり、古い
  checked-in rule ファイルが出荷済みガイダンスを暗黙に上書きできなくなりました。

### absorbed / superseded パケット lifecycle retirement の安全性 (G474)

- 新しい machine-readable なパケット lifecycle retirement: `lifecycle.yaml` サイドカー
  （`lifecycle: ready|absorbed|retired|superseded` と任意の `absorbed_by` /
  `superseded_by` / `retired_reason` / `retired_at`）が、パケットディレクトリが
  next-slice 候補ではなく design history であることを記録します。
- 新 `intent-cli packet retire --execution-unit <id> (--absorbed-by <unit> |
  --superseded-by <unit> | --retired) --reason <text> [--write]` がサイドカーを記録し
  `packet-retired` run イベントを追加します。冪等（同じ retirement の再実行はサイドカーを
  書き換えず run イベントを重複させず `already-retired` を報告）で、パケットファイルを削除
  しません。
- `intent-cli intent next-slice` は両方の scan pass で machine-retired パケットを
  issue-cut-ready 選択から除外します。古い human marker（例: `STATUS: ABSORBED`）のみを
  持つパケットは除外され、`legacy-retirement-marker-needs-machine-metadata` 警告と
  修復ノートで surface されるため、absorbed パケットが盲目的に publish されることはありません。
  host loop ガイダンスは、こうしたパケットを operator に publish 可否を尋ねるのではなく
  `intent-cli packet retire` で retire するよう指示します。

> バージョンメタデータ注記: `eng/version.json` は既に `stableVersion: 0.3.6`,
> `nextVersion: 0.3.7` を記録しており、G475（本パケット）はリリース準備です。v0.3.7 後の
> メタデータ前進（`stableVersion → 0.3.7`, `nextVersion → 0.3.8`）は operator のリリース後
> 手順であり、本パケットの対象外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.7
```

または
[v0.3.7 GitHub リリース](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.7)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを
検証してください。

## v0.3.6 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.7
```

v0.3.6 からの破壊的変更はありません。

## リリース準備ゲート (G475)

以下が **すべて** 満たされるまで `v0.3.7` タグ/リリースを作成しないこと
（このゲートは fail-closed — 1 つでも未達なら停止しタグを打たない）:

- [ ] リリース対象パケットがすべて **完了し PR が `main` にマージ済み**:
      G471, G472, G473, G474（および本準備 G475）。host/review 側で host queue-state /
      GitHub PR state により確認すること — child 実装 loop は親 queue-state を読まないため、
      これは host 所有の前提条件です。
- [ ] `eng/version.json` の `nextVersion` が `0.3.7`（意図したリリースバージョン）で、
      作成するタグ（`v0.3.7`）と一致すること。release ワークフローはタグからパッケージ
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

## v0.3.7 GitHub リリースの作成

1. [リリース準備ゲート](#リリース準備ゲート-g475) を確認 — 未達項目があれば進めない。
2. release コミットにタグ: `git tag v0.3.7 && git push origin v0.3.7`。
3. `release.yml` ワークフローが発火し、バイナリ・`.nupkg`・チェックサムをビルド
   （バージョンはタグから導出）。green 完了を待つ。
4. ワークフローが GitHub Release draft を作成。確認し、本ファイルの内容を release body
   として貼り付け、publish。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.7` を push したことを確認。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）が
         アクセス可能。
   - [ ] `.sha256` チェックサムがダウンロードしたアーティファクトと一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.7`）の後、
         `intent-cli --version` が `0.3.7` を報告する。
   - [ ] バイナリアーティファクトの smoke check: プラットフォームアーカイブをダウンロードし、
         `.sha256` を検証、展開して `./intent-cli --version` → `0.3.7`。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.7` 後の次の開発ラインを
         使う（[Version flow](09-developer-reference.md#version-flow) のリリース後手順に
         従い `eng/version.json` を bump）: `stableVersion → 0.3.7`, `nextVersion → 0.3.8`。
