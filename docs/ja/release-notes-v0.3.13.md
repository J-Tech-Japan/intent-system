# リリースノート — intent-cli v0.3.13

> **メンテナ向けリリースチェックリスト:** [v0.3.13 GitHub Release の作成](#v0313-github-release-の作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g506) を通過するまでタグ付けしないでください。**

## v0.3.13 の内容

v0.3.13 は **パッチリリース** で、`v0.3.12` 以降に完了した design-thread agmsg receiver
ガイダンス（G505）を出荷します。package id・ライセンス・workflow セマンティクスの変更はなく、
既存の timer-loop モードは完全サポート・不変です。package id は `JTechJapan.IntentSystem.Cli`
のままです。

> **orchestrator モードは引き続き preview/experimental です。** オプトインで、まだ hardening 中で
> あり、デフォルトの workflow ではありません。intent-cli と GitHub が権威 source of truth であり
> 続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### design-thread agmsg receiver ガイダンス（G505）

- orchestrator-thread ガイド（`intent-cli guide orchestrator-thread`）が、任意の **4 つ目の
  論理ロール** である **design / human receiver** を文書化し、人間が必要なエスカレーションを
  agmsg で配信できるようにしました。4 つのロールは: orchestrator、implementation receiver、
  review receiver、そして任意の design/human receiver です。
- **ルーチンな進捗は** orchestrator / implementation / review の **内部に留まり**、**人間が
  必要な判断のみ** が design スレッドにルーティングされます（clarification、product 曖昧さ、
  permission/認証情報、破壊的操作、繰り返し no-progress、未解決の canonical state、
  リリース/publish、明示的ポリシー）。
- design receiver はルーチン運用には **任意** ですが、確実なエスカレーション配信には **推奨** され、
  かつ **loopless** です — 人間がオンデマンドで読めます。ガイドは貼り付け可能な登録/宛先指定テキスト、
  design スレッド向けの最小の手動 inbox トリガープロンプト、design monitor が起動する前に送られた
  メッセージは手動 `inbox.sh` 確認が必要になる旨の注記を提供します。
- implementation/review receiver は loopless のまま、agmsg はシグナル層のみです。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.12`,
> `nextVersion: 0.3.13` を記録します。G506（本パケット）はリリース準備です。
> v0.3.13 リリース後のメタデータ前進（`stableVersion → 0.3.13`, `nextVersion → 0.3.14`）は
> オペレーターのリリース後ステップであり、本パケットのスコープ外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.13
```

または [v0.3.13 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.13)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.3.12 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.13
```

v0.3.12 からの破壊的変更はありません。orchestrator モードはオプトインのままで、既存の timer-loop
セットアップには影響しません。

## リリース準備ゲート (G506)

次のすべてが成り立つまで `v0.3.13` タグ/リリースを作成しないでください
（このゲートは fail closed です — 1 つでも未充足なら停止し、タグ付けしない）:

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G505
      （および本準備の G506）。host queue-state / GitHub PR 状態で host/review 側から確認する
      （child 実装ループは parent queue-state を読まないため、これは host 所有の前提条件）。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされていない
      （タグ付け前に host queue / open PR リストを確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.13`（意図したリリースバージョン）で、作成する
      タグ（`v0.3.13`）と一致する。リリースワークフローは package バージョンをタグから導出し、
      `-p:Version=` が `src/IntentSystem.Cli/IntentSystem.Cli.csproj` のポリシー由来デフォルトを上書きする。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされている。
- [ ] リリースノート / README が **orchestrator モードは preview/experimental** かつオプトインで
      あること、timer-loop モードが不変であることを保っている。
- [ ] リリースコミットで **Main CI が green**（`Build and test (source contract)`）であり、
      **preview-pack** workflow も green。

## v0.3.13 GitHub Release の作成

1. [リリース準備ゲート](#リリース準備ゲート-g506) を確認 — 未充足項目があれば進めない。
2. リリースコミットにタグ付け: `git tag v0.3.13 && git push origin v0.3.13`。
3. `release.yml` workflow が発火し、バイナリ・`.nupkg`・チェックサムをビルドする（バージョンは
   タグから導出）。green 完了を待つ。
4. workflow が GitHub Release draft を作成する。レビューし、本ファイルの内容をリリース本文として
   貼り付け、publish する。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.13` を push したことを確認する。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）にアクセスできる。
   - [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.13`）後、
         `intent-cli --version` が `0.3.13` を報告する。
   - [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
         展開して `./intent-cli --version` → `0.3.13`。
   - [ ] **orchestrator ガイドスモーク**（G505）: `intent-cli guide orchestrator-thread
         --domain <d> --target-repo <repo> --agent <agent> --format markdown` が 4 つの論理ロールと
         任意の design/human receiver セクションをレンダリングする。
   - [ ] **design receiver inbox スモーク**（G505）: レンダリングされたガイドが design スレッド向けの
         最小の手動 inbox トリガープロンプトと pre-start `inbox.sh` 注記を含む。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.13` の次の開発ラインを使う
         （[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
         `eng/version.json` をバンプ）: `stableVersion → 0.3.13`, `nextVersion → 0.3.14`。
