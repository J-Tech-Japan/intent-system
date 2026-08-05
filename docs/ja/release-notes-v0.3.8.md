# リリースノート — intent-cli v0.3.8

> **メンテナ向けリリースチェックリスト:** [v0.3.8 GitHub リリースの作成](#v038-github-リリースの作成) を参照。
> **[リリース準備ゲート](#リリース準備ゲート-g478) を通過するまでタグを打たないこと。**

## v0.3.8 の内容

v0.3.8 は loop 信頼性のリリースです。`v0.3.7` 後に完了した 2 つの automation 修正を
出荷し、実装/レビュー loop が operator の手動介入なしで進み続けるようにします: child
`pr-comment-preflight` が review コメントの host メタデータ path を根拠（evidence）と
して引用してもデッドロックしなくなり、host closeout が queue item の `linked_pr`
projection 欠落時に deterministic に回復できるようになりました。package id・ライセンス・
ワークフロー semantics の変更はありません。package id は `JTechJapan.IntentSystem.Cli`
のままです。

### packet evidence の引用が child PR 修復をデッドロックさせない (G476)

- `intent-cli worker pr-comment-preflight` が review コメントを、付随的な
  `.intent-cli/` や `intents/` の言及ではなく **要求された編集対象（requested edit
  target）** で分類するようになりました。G316 形式の request-update コメントが
  `.intent-cli/issues/<unit>/packet.yaml` のような packet path を根拠として引用しつつ
  実装ファイルの変更を求める場合は `repair-required` / actionable に分類され、child
  worker は何も修復することのない host を永遠に待つのではなく claim して修復できます。
- すべての要求された編集対象が host メタデータ path のときだけ
  `host-artifact-repair-required` になります。本物の host-artifact 編集要求は引き続き
  host 修復エージェントへ回送されます（G353 を維持）。
- 分類は（truncate された excerpt ではなく）コメント本文全体で行われ、結果は
  `actionable_comments[].requested_edit_paths` と
  `actionable_comments[].host_evidence_paths` を公開するため、host メタデータを読まずに
  判定理由を説明できます。`worker next-action` は同じ分類器を参照するため、2 つの surface
  が child-claimability で食い違うことはありません。

### `linked_pr` 欠落時の deterministic な closeout 回復 (G477)

- `intent-cli closeout pr --pr <n>` が、`linked_pr` が host durable state に projection
  されていないことだけを理由に queue item を照合できない場合に自動回復するようになりました。
  merged PR の GitHub closing references が（`linked_issue` 経由で）ちょうど 1 つの queue
  item を特定できれば、operator が `--issue <n>` の fallback を知らなくても closeout が
  完了し、write 時に欠落していた `linked_pr` projection を修復します。
- 結果は `recoverable_missing_linked_pr` / `inferred_issue` /
  `recovery_source`（`github-closing-reference`）/ `recovery_action` を surface し、回復を
  監査可能にします。曖昧な証拠（closing references が複数の queue item に一致）は推測せず
  `linkage-ambiguous` エラーで fail closed します。その場合のみ手動 `--issue <n>` の再実行が
  必要です。
- これは host 所有の deterministic recovery であり operator の policy 判断ではありません。
  child `--github-only` loop は引き続き `linked_pr` を書きません。

> バージョンメタデータ注記: `eng/version.json` は `stableVersion: 0.3.7`,
> `nextVersion: 0.3.8` を記録しており、G478（本パケット）はリリース準備です。v0.3.8 後の
> メタデータ前進（`stableVersion → 0.3.8`, `nextVersion → 0.3.9`）は operator のリリース後
> 手順であり、本パケットの対象外です。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.8
```

または
[v0.3.8 GitHub リリース](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.8)
から self-contained バイナリをダウンロードしてください。使用前に `.sha256` サイドカーを
検証してください。

## v0.3.7 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.8
```

v0.3.7 からの破壊的変更はありません。

## リリース準備ゲート (G478)

以下が **すべて** 満たされるまで `v0.3.8` タグ/リリースを作成しないこと
（このゲートは fail-closed — 1 つでも未達なら停止しタグを打たない）:

- [ ] リリース対象パケットがすべて **完了し PR が `main` にマージ済み**:
      G476, G477（および本準備 G478）。host/review 側で host queue-state /
      GitHub PR state により確認すること — child 実装 loop は親 queue-state を読まないため、
      これは host 所有の前提条件です。
- [ ] `eng/version.json` の `nextVersion` が `0.3.8`（意図したリリースバージョン）で、
      作成するタグ（`v0.3.8`）と一致すること。release ワークフローはタグからパッケージ
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

## v0.3.8 GitHub リリースの作成

1. [リリース準備ゲート](#リリース準備ゲート-g478) を確認 — 未達項目があれば進めない。
2. release コミットにタグ: `git tag v0.3.8 && git push origin v0.3.8`。
3. `release.yml` ワークフローが発火し、バイナリ・`.nupkg`・チェックサムをビルド
   （バージョンはタグから導出）。green 完了を待つ。
4. ワークフローが GitHub Release draft を作成。確認し、本ファイルの内容を release body
   として貼り付け、publish。
5. NuGet publish ステップが `JTechJapan.IntentSystem.Cli 0.3.8` を push したことを確認。
6. リリース後の検証チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて正しく解決する。
   - [ ] GitHub release アセットリンク（`.tar.gz`, `.zip`, `.exe`, `.nupkg`）が
         アクセス可能。
   - [ ] `.sha256` チェックサムがダウンロードしたアーティファクトと一致する。
   - [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`（または
         `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.8`）の後、
         `intent-cli --version` が `0.3.8` を報告する。
   - [ ] バイナリアーティファクトの smoke check: プラットフォームアーカイブをダウンロードし、
         `.sha256` を検証、展開して `./intent-cli --version` → `0.3.8`。
   - [ ] ローカル preview/dry-run のバージョンメタデータが `0.3.8` 後の次の開発ラインを
         使う（[バージョンフロー](09-developer-reference.md#バージョンフロー) のリリース後手順に
         従い `eng/version.json` を bump）: `stableVersion → 0.3.8`, `nextVersion → 0.3.9`。
