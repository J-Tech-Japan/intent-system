# リリースノート — intent-cli v0.3.5

> **メンテナ向けリリースチェックリスト:** [v0.3.5 GitHub リリースの作成](#v035-github-リリースの作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g452) を通過するまでタグを打たないこと。**

## v0.3.5 の内容

v0.3.5 は構造的信頼性のリリースです。`v0.3.4` 後に完了した host-loop /
next-slice / review の信頼性パケットをまとめ、host-loop の繰り返し停止、host
メタデータの drift、矛盾する next-slice 判断、長い prompt choreography、繰り返す
review ポリシー質問を削減します。package id・ライセンス・ワークフロー semantics の
変更はありません。package id は `JTechJapan.IntentSystem.Cli` のままです。

### 統一 state doctor + fail-closed safe repair (G448)

- 新 `intent-cli automation state-doctor` — queue-state / publish artifact /
  GitHub PR（open + merged）にまたがる host メタデータ drift の統一的で OSS-safe な
  診断。デフォルト read-only で、決定論的な drift カテゴリ（linked_pr 欠落、publish
  artifact 由来の linked_issue 欠落、merged-PR-not-completed）を evidence 付きで報告し、
  曖昧なケース（重複 issue evidence、複数 closing PR）は fail-closed の unsafe finding
  に分類。
- `--write` は high-confidence・forward-only の queue-state 修復のみを適用し、修復ごとに
  append-only の `runs.jsonl` イベントを追加。既存 host データを消去・書き換え・downgrade
  せず、古い host を migration しません。`--workdir` は全 read/write の host context を
  一貫して駆動します。

### 統一 next-slice readiness エンジン (G449)

- 新しい共有 `NextSliceReadinessEvaluator`（および `IsPublishable` アダプタ）が
  「この候補は issue を切れるか?」判断の単一エンジン。`intent next-slice`、
  `automation host-loop-next-action`、`automation host-review-diagnostics`、
  next-slice classify、packet-draft 検証、`issue publish-flow` が contract 完全性／
  publishability 判断をこれ経由にし、ある surface が拒否した候補を別 surface が
  `issue-cut-ready` と報告しないことを保証。fail-closed の優先順位:
  true-idle → contract-incomplete → clarification-required → duplicate-existing →
  issue-cut-ready。既存の open GitHub issue/PR は重複 publish ではなく
  reconcile/recovery へ誘導。

### one-safe-wake host-loop コマンド (G450)

- 新 `intent-cli automation host-loop-wake` は host loop の順序付き preflight / sync /
  review / closeout / publish / diagnostics choreography を 1 つの構造化判定
  （`true-idle` / `review` / `publish` / `blocker`）に集約。installed-CLI surface を
  gate にし、既存の host-loop-next-action 判断を再利用。1 wake あたり PR review 1 件・
  issue publish 1 件の不変条件を強制。
- デフォルト read-only。`--write` は安全で判断不要なレーンを既存 surface 経由で実行:
  決定論的な host メタデータ修復と next-slice publish chain
  （`packet draft` → `issue publish-flow --write` → `automation issue-publish --write`）。
  各ステップ fail-closed。review approval / request-update transition は専門家判断が
  必要なため自動承認せず `pending_command` を提示。

### domain review standing-policy レジストリ (G451)

- 繰り返す review 判断（draft 扱い、device/operator/hardware-gated evidence、外部
  artifact intake、test-evidence 十分性、follow-up 追跡）のための任意
  `.intent-cli/review-policy.json` standing-policy レジストリ。`guide review` と
  host-loop guidance（`guide prompt-matrix`）がこれを consume し `review_policy_source`
  を surface するため、agent が同じ standing-policy 質問を繰り返さない。ファイルが
  無い／不正な場合は安全な組み込みデフォルトへ fail-closed（migration 不要、既存 host は
  従来どおり）。組み込みデフォルトは installed の draft-aware フローを維持
  （draft 状態だけでは review stop ではなく、draft のままの approve/merge は禁止）。

> バージョンメタデータ注記: `eng/version.json` は既に `stableVersion: 0.3.4`,
> `nextVersion: 0.3.5` を記録。G452（本パケット）はリリース準備。post-v0.3.5
> （0.3.6）への前進は本パケットの対象外。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.5
```

または
[v0.3.5 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.5)
から self-contained バイナリをダウンロード。使用前に `.sha256` を検証。

## v0.3.4 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.5
```

v0.3.4 からの破壊的変更はありません。

## リリース準備ゲート (G452)

以下がすべて満たされるまで `v0.3.5` タグ/リリースを作成しないこと
（このゲートは fail-closed — 未達があれば停止しタグを打たない）:

- [ ] リリース対象パケットがすべて **完了し PR が `main` にマージ済み**:
      G448, G449, G450, G451（および G452 本準備）。host/review 側の queue-state /
      GitHub PR 状態で確認すること — child 実装ループは parent queue-state を読まないため、
      これは host-owned の前提条件。
- [ ] `eng/version.json` の `nextVersion` が `0.3.5`（意図するリリース版）で、作成する
      タグ（`v0.3.5`）と一致。リリースワークフローはタグから package version を導出し、
      `-p:Version=` が `src/IntentSystem.Cli/IntentSystem.Cli.csproj` の静的 `<Version>` を上書き。
- [ ] package メタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system`、
      `PackageLicenseExpression = Apache-2.0`、README/docs リンクが解決し、
      公式サイト `https://www.intent-driven-development.com/` が README からリンクされている。
- [ ] リリースコミットで **main CI が green**（`Build and test (source contract)`）、
      かつ **preview-pack** ワークフローが green。

## v0.3.5 GitHub リリースの作成

1. [リリース準備ゲート](#リリース準備ゲート-g452) を確認 — 未達があれば進めない。
2. リリースコミットにタグ: `git tag v0.3.5 && git push origin v0.3.5`。
3. `release.yml` ワークフローが発火し、バイナリ・`.nupkg`・チェックサムを
   ビルド（version はタグ由来）。green 完了まで待つ。
4. ワークフローが GitHub Release ドラフトを作成。レビューし、本ファイルの内容を
   リリース本文に貼り付けて公開。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.5` を push したか確認。
6. リリース後検証チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて解決する。
   - [ ] GitHub リリースのアセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）が
         アクセス可能。
   - [ ] `.sha256` チェックサムがダウンロード成果物と一致。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli` 後に
         `intent-cli --version` が `0.3.5` を報告。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.5` の次の開発ラインを
         使う（[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後手順に
         従って `eng/version.json` を bump）。
