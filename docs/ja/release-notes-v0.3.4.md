# リリースノート — intent-cli v0.3.4

> **メンテナ向けリリースチェックリスト:** [v0.3.4 GitHub リリースの作成](#v034-github-リリースの作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g446) を通過するまでタグを打たないこと。**

## v0.3.4 の内容

v0.3.4 は信頼性と host-loop ガイダンスのリリースです。修正済みの `v0.3.3`
リリース後に完了した、初回セットアップ/CI のリリースブロッカー修正と
host-loop / レビューポリシーの強化をまとめています。package id・ライセンス・
ワークフロー semantics の変更はありません。package id は
`JTechJapan.IntentSystem.Cli` のままです。

### 初回 host 初期化デッドロック修正 (G441)

- `intent init-tree --write` が `intents/<domain>/automation/bindings.md`
  （認識される `execution_unit_regex`、判明時は `child_repo` 付き）を生成し、
  初回ホストが手書きなしで `next-slice` / `host-check` / `automation summary`
  に認識される。
- `intent init --write` が durable-state スケルトン
  `.intent-cli/queue-state.json`（空・schema 1）と `.intent-cli/runs.jsonl`
  を生成し、新規ホストで `host-check` が `partially-initialized` ではなく
  `ok` を返す。
- 初回ドキュメントは、ソースを読んだり `bindings.md` を手書きせず intent-cli
  に次アクションを尋ねるよう誘導。

### リリース CI 安定化 (G443)

- installed-CLI surface probe が Linux の `Text file busy`（ETXTBSY）exec
  レースをリトライし、永続失敗時はコマンドをクラッシュさせず `missing` に
  degrade。以前 `v0.3.3` リリース CI をブロックした2つの flaky テストを解消。
- release/CI/preview ワークフローが一意名の `*.trx`（`LogFilePrefix`）を出力し、
  プロジェクトごとの結果が同一ファイルを上書きしない。

### host-loop scheduler invariant + 重複 publish ガード (G444)

- `guide prompt-matrix` の host-loop ガイダンスが安全な scheduling invariant
  （host repo + domain ごとに 1 active wake）を明記。5分の同一スレッド逐次
  ループは可、独立並走は不可。invariant を満たせる場合 agent は確認で止まらず
  進む。
- `automation host-loop-next-action` に `stale-next-slice-reconcile` を追加:
  `next-slice` が `issue-cut-ready` でも同一 execution unit に GitHub
  issue/PR が既存（token 境界一致）なら、重複 publish せず
  `automation reconcile --lane next-slice` へ誘導。

### device-gated レビュー証跡ポリシー (G445)

- `guide review` が standing `device_gated_evidence_policy` を出力:
  device/operator/hardware-gated な受け入れ基準で approve-with-recorded-gap
  か hard-block か、no-false-claim、durable follow-up tracking、同一ポリシーを
  パケットごとに再質問しないことを明文化。

> バージョンメタデータ注記: G442 が開発版ソースを 0.3.4 ライン
> （`eng/version.json` `stableVersion: 0.3.3`, `nextVersion: 0.3.4`）へ前進。
> G446（本パケット）はリリース準備ゲート。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.4
```

または
[v0.3.4 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.4)
から self-contained バイナリをダウンロード。使用前に `.sha256` を検証。

## v0.3.3 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.4
```

v0.3.3 からの破壊的変更はありません。

## リリース準備ゲート (G446)

以下がすべて満たされるまで `v0.3.4` タグ/リリースを作成しないこと
（このゲートは fail-closed — 未達があれば停止しタグを打たない）:

- [ ] リリース対象パケットがすべて **完了し PR が `main` にマージ済み**:
      G441, G443, G444, G445（および G442 version bump、G446 本準備）。
      host/review 側の queue-state / GitHub PR 状態で確認すること — child
      実装ループは parent queue-state を読まないため、これは host-owned
      の前提条件。
- [ ] `eng/version.json` の `nextVersion` が `0.3.4`（意図するリリース版）で、
      作成するタグ（`v0.3.4`）と一致。リリースワークフローはタグから package
      version を導出し、`-p:Version=` が
      `src/IntentSystem.Cli/IntentSystem.Cli.csproj` の静的 `<Version>` を上書き。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system`、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、
      公式サイト `https://www.intent-driven-development.com/` が README から
      リンクされている。
- [ ] リリースコミットで **main CI が green**（`Build and test (source contract)`）、
      かつ **preview-pack** ワークフローが green。

## v0.3.4 GitHub リリースの作成

1. [リリース準備ゲート](#リリース準備ゲート-g446) を確認 — 未達があれば進めない。
2. リリースコミットにタグ: `git tag v0.3.4 && git push origin v0.3.4`。
3. `release.yml` ワークフローが発火し、バイナリ・`.nupkg`・チェックサムを
   ビルド（version はタグ由来）。green 完了まで待つ。
4. ワークフローが GitHub Release ドラフトを作成。レビューし、本ファイルの内容を
   リリース本文に貼り付けて公開。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.4` を push したか確認。
6. リリース後検証チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて解決する。
   - [ ] GitHub リリースのアセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）が
         アクセス可能。
   - [ ] `.sha256` チェックサムがダウンロード成果物と一致。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` 後に
         `intent-cli --version` が `0.3.4` を報告。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.4` の次の開発
         ラインを使う（[バージョンフロー](09-developer-reference.md#バージョンフロー)
         のリリース後手順に従って `eng/version.json` を bump）。
