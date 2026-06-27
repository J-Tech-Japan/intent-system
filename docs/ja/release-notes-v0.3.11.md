# リリースノート — intent-cli v0.3.11

> **メンテナ向けリリースチェックリスト:** [v0.3.11 GitHub Release の作成](#v0311-github-release-の作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g497) を通過するまでタグ付けしないでください。**

## v0.3.11 の内容

v0.3.11 は、G487–G496 の intent パケットで設計された **agmsg orchestrator モード
（preview/experimental）** と、レビュー側の自動コメント triage ポリシーを導入します。
package id・ライセンス・workflow セマンティクスの変更はなく、既存の timer-loop モードは
完全サポート・不変です。package id は `JTechJapan.IntentSystem.Cli` のままです。

> **orchestrator モードは preview/experimental です。** オプトインで、まだ hardening 中であり、
> デフォルトの workflow ではありません。agmsg は最初のローカルメッセージバスの例であり、恒久的な
> アーキテクチャ境界ではありません。intent-cli と GitHub は queue・issue・PR・label・review・
> closeout の権威 source of truth であり続け、agmsg はメッセージ / 進捗 / 完了のシグナル層のみです。

### エージェントメッセージ（agmsg）orchestrator モード — preview（G487–G496）

任意の 4 つ目の **orchestrator** スレッドが、独立した定期タイマーの代わりにローカルメッセージ
バス（agmsg）経由で **実装**・**レビュー** スレッドを調整できます。貼り付け可能なプロンプトと
運用契約は次で生成します:

```bash
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --format markdown
```

ガイド surface は次をカバーします:

- **ロールとフォルダー** — orchestrator / implementation / review。各ロールは自分のフォルダー・
  クローン・worktree から実行（G487, G494）。
- **single-domain と multi-domain のルーティング** — host repo は複数ドメインを保持でき、
  1 つの repo が複数ドメインに供給することもある。可視であることは権限ではない（G489）。
- **スケジュール wake のケイデンス** — orchestrator が唯一のスケジュールドライバー
  （Codex automation 5m または Claude `/loop 5m`）。実装/レビューは loopless receiver。
  **orchestrator モードでは同じルートに対して実装/レビューの定期タイマーを同時実行しない**（G490）。
- **bounded な next-slice publish** — orchestrator は canonical な intent-cli surface 経由で
  wake ごとに 1 件の `issue-cut-ready` issue を publish し、委譲前に検証する（G491）。
- **CI 待ち状態** — pending CI はブロッカーではなく、待って再確認するアクティブな状態（G492）。
- **自動レビュアーコメント triage** — `intent-cli guide review` が自動レビュアー（例: Copilot）の
  コメントを分類し、すべてを実装に回さない（G493）。
- **依存を考慮した計画** — 未充足の依存は、オペレーターのために止まらず最も早い未充足依存に
  ルーティングする（G495）。
- **stale-thread ヘルスチェック** — 行動前に尋ね、permission プロンプトの自動クリア・作業の
  キャンセル・タスクの重複を決して行わない安全な no-reply liveness チェック（G496）。
- **設計スレッドのセットアップ** — `guide workflow suggest` から到達できる具体的なセットアップ
  チェックリスト（パス・team・delivery・ロールプロンプト・最初の read-only wake・ping テスト・
  クリーンアップ）（G494）。

安全な repair とエスカレーション、next-slice publish、委譲の境界はすべて
[エージェントメッセージオーケストレーション](12-agent-message-orchestration.md) に記載されています。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.10`,
> `nextVersion: 0.3.11` を記録します。G497（本パケット）はリリース準備です。
> v0.3.11 リリース後のメタデータ前進（`stableVersion → 0.3.11`, `nextVersion → 0.3.12`）は
> オペレーターのリリース後ステップであり、本パケットのスコープ外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.11
```

または [v0.3.11 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.11)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを検証します。

## v0.3.10 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.11
```

v0.3.10 からの破壊的変更はありません。orchestrator モードはオプトインで、既存の timer-loop
セットアップには影響しません。

## リリース準備ゲート (G497)

次のすべてが成り立つまで `v0.3.11` タグ/リリースを作成しないでください
（このゲートは fail closed です — 1 つでも未充足なら停止し、タグ付けしない）:

- [ ] リリース対象の各パケットが **完了し PR が `main` にマージ済み**: G487–G496
      （および本準備の G497）。host queue-state / GitHub PR 状態で host/review 側から確認する
      （child 実装ループは parent queue-state を読まないため、これは host 所有の前提条件）。
- [ ] 本リリース対象の open な intent-system PR や WIP パケットが誤ってスキップされていない
      （タグ付け前に host queue / open PR リストを確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.11`（意図したリリースバージョン）で、作成する
      タグ（`v0.3.11`）と一致する。リリースワークフローは package バージョンをタグから導出し、
      `-p:Version=` が `src/IntentSystem.Cli/IntentSystem.Cli.csproj` のポリシー由来デフォルトを上書きする。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、公式サービスサイト
      `https://www.intent-driven-development.com/` が README からリンクされている。
- [ ] リリースノート / README が **orchestrator モードは preview/experimental** かつオプトインで
      あること、timer-loop モードが不変であることを明記している。
- [ ] リリースコミットで **Main CI が green**（`Build and test (source contract)`）であり、
      **preview-pack** workflow も green。

## v0.3.11 GitHub Release の作成

1. [リリース準備ゲート](#リリース準備ゲート-g497) を確認 — 未充足項目があれば進めない。
2. リリースコミットにタグ付け: `git tag v0.3.11 && git push origin v0.3.11`。
3. `release.yml` workflow が発火し、バイナリ・`.nupkg`・チェックサムをビルドする（バージョンは
   タグから導出）。green 完了を待つ。
4. workflow が GitHub Release draft を作成する。レビューし、本ファイルの内容をリリース本文として
   貼り付け、publish する。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.11` を push したことを確認する。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org の package ページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）にアクセスできる。
   - [ ] `.sha256` チェックサムがダウンロード成果物と一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.11`）後、
         `intent-cli --version` が `0.3.11` を報告する。
   - [ ] バイナリ成果物スモーク: プラットフォームアーカイブをダウンロードし `.sha256` を検証、
         展開して `./intent-cli --version` → `0.3.11`。
   - [ ] **orchestrator モード preview スモーク**: `intent-cli guide orchestrator-thread
         --domain <d> --target-repo <repo> --agent <agent> --format markdown` がロールプロンプト・
         セットアップチェックリスト・安全境界をレンダリングする。README と docs がモードを
         preview/experimental と表示している。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.11` の次の開発ラインを使う
         （[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後ステップに従い
         `eng/version.json` をバンプ）: `stableVersion → 0.3.11`, `nextVersion → 0.3.12`。
