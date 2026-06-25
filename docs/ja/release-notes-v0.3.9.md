# リリースノート — intent-cli v0.3.9

> **メンテナ向けリリースチェックリスト:** [v0.3.9 GitHub リリースの作成](#v039-github-リリースの作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g483) を通過するまでタグを打たないこと。**

## v0.3.9 の内容

v0.3.9 は loop 安定性のリリースです。`v0.3.8` 後に完了した 4 つの信頼性修正を出荷し、
Claude/Codex の実装/レビュー loop が interactive prompt・role 混乱・重複 issue race・
publish ブロックする packet 欠落で停止せずに進み続けるようにします。package id・ライセンス・
ワークフロー semantics の変更はありません。package id は `JTechJapan.IntentSystem.Cli`
のままです。

### loop wake 中の Asking UI ではなく fail closed (G479)

- すべての recurring loop prompt（`intent-cli guide prompt-matrix`）が単一の共有ポリシーを
  持つようになりました: automation loop wake 中、agent は操作上の曖昧さ（重複 publish・
  queue / linkage 不一致・role 混乱・CI pending・WIP-cap・draft PR・stale lease）で
  interactive Asking UI を使って **停止しません**。
- Asking は狭い safety gate のみに限定されます — security 承認・外部認証 / login・破壊的
  ローカル操作・不可逆な公開 publish・operator が明示的に要求した policy 判断。
- 回復可能な曖昧さは intent-cli safe repair か通常の wait に収束し、回復不能なものは
  `STOP: <classification>` と 1 つの operator アクションで終わります。並行 host loop を
  両方継続するような unsafe な選択肢は提示されません — 安全な不変条件は host repo + domain
  ごとに 1 つの active wake です。

### host-orchestrator と semantic-reviewer の role を明示化 (G480)

- host review / next-slice prompt が 3 つの責務を区別するようになりました:
  **host-orchestrator**（preflight・diagnostics・safe repair・承認済み PR の merge・
  closeout・next-slice publish・metadata 整合）、**semantic-reviewer**（diff レビュー・
  packet / intent への対応付け・approve / request-update — running agent が packet
  `review_role`、既定 `Codex` と一致、または明示割当のときのみ許可）、
  **child-implementer**。
- agent は過剰レビューも「host は決してレビューしない」という結論も避けます。承認済み PR は
  別 agent がレビューしても orchestrator が merge 可能なままで、role 不一致は wait /
  `STOP: review-role-mismatch` であり Asking UI ではありません。Claude host-orchestrator と
  Codex semantic-reviewer の prompt variant がそれぞれの role で正しく読めます。

### 重複 host publish を検出し fail-closed で canonical 化 (G481)

- `intent-cli automation state-doctor` が重複 execution-unit issue と並行 host publish を
  分類するようになりました: `concurrent-host-publish-detected`,
  `canonical-issue-mismatch`, `pr-closes-noncanonical-issue`（最後のものは通常の
  missing-`linked_pr` 回復とは別分類）。
- canonical 選択は live GitHub recency より durable な証拠（queue-state `linked_issue`、
  次に packet `publish.yaml`）を優先します。safe repair（非 canonical な重複を close）は、
  canonical issue が一意で重複に active PR が無い場合の
  `duplicate-execution-unit-issue-detected` のときのみ提示されます。
- 曖昧または thrashing する race は、recency で勝者を選ぶ・issue を恣意的に reopen/close
  する・race 中に PR body を自動編集する、のいずれもせず fail closed します。

### packet 作成が完全な publish-ready contract を生成 (G482)

- packet scaffold（`intent-cli packet draft`）と publish-body validator が必須セクションの
  単一情報源を共有するようになり、二度と乖離しません。
- 新規 scaffold された `github-body.md` は既定で完全な contract 形状（`Standalone Child
  Issue Contract` を含む）を持ち、packet-draft guide は packet を GitHub issue 化の準備完了と
  宣言する前に publish validation（`issue validate-body`, `packet draft --dry-run`,
  `intent next-slice --dry-run`）を dry-run するよう指示します。publish validation は不完全な
  body に対して引き続き fail-closed のままで、繰り返し発生していた section 欠落の publish
  ブロックが解消されます。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.8`,
> `nextVersion: 0.3.9` を記録しており、G483（本パケット）はリリース準備です。v0.3.9 後の
> メタデータ前進（`stableVersion → 0.3.9`, `nextVersion → 0.3.10`）は operator のリリース後
> 手順であり、本パケットの対象外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.9
```

または
[v0.3.9 GitHub リリース](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.9)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを
検証してください。

## v0.3.8 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.9
```

v0.3.8 からの破壊的変更はありません。

## リリース準備ゲート (G483)

以下が **すべて** 満たされるまで `v0.3.9` タグ/リリースを作成しないこと
（このゲートは fail-closed — 1 つでも未達なら停止しタグを打たない）:

- [ ] リリース対象パケットがすべて **完了し PR が `main` にマージ済み**:
      G479, G480, G481, G482（および本準備 G483）。host/review 側で host queue-state /
      GitHub PR state により確認すること — child 実装 loop は親 queue-state を読まないため、
      これは host 所有の前提条件です。
- [ ] 本リリース対象の open な intent-system PR や WIP packet を取りこぼしていないこと
      （タグ付け前に host queue / open PR 一覧を確認）。
- [ ] `eng/version.json` の `nextVersion` が `0.3.9`（意図したリリースバージョン）で、
      作成するタグ（`v0.3.9`）と一致すること。release ワークフローはタグからパッケージ
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

## v0.3.9 GitHub リリースの作成

1. [リリース準備ゲート](#リリース準備ゲート-g483) を確認 — 未達項目があれば進めない。
2. release コミットにタグ: `git tag v0.3.9 && git push origin v0.3.9`。
3. `release.yml` ワークフローが発火し、バイナリ・`.nupkg`・チェックサムをビルド
   （バージョンはタグから導出）。green 完了を待つ。
4. ワークフローが GitHub Release draft を作成。確認し、本ファイルの内容を release body
   として貼り付け、publish。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.9` を push したことを確認。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）が
         アクセス可能。
   - [ ] `.sha256` チェックサムがダウンロードしたアーティファクトと一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.9`）の後、
         `intent-cli --version` が `0.3.9` を報告する。
   - [ ] バイナリアーティファクトの smoke check: プラットフォームアーカイブをダウンロードし、
         `.sha256` を検証、展開して `./intent-cli --version` → `0.3.9`。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.9` 後の次の開発ラインを
         使う（[Version flow](09-developer-reference.md#version-flow) のリリース後手順に
         従い `eng/version.json` を bump）: `stableVersion → 0.3.9`, `nextVersion → 0.3.10`。
