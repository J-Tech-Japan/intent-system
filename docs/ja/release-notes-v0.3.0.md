# リリースノート — intent-cli v0.3.0

> **メンテナー向けリリースチェックリスト:** [v0.3.0 GitHub Release の作成](#v030-github-release-の作成) を参照してください。

## v0.3.0 の内容

v0.3.0 は `intent-cli` の最初の OSS 向け安定リリースです。GitHub 上のインテント駆動開発ワークフローをサポートするコマンド一式と、初の公開 NuGet およびセルフコンテインドバイナリ配布が含まれています。

### このリリースの新コマンド

| コマンドグループ | 追加コマンド |
|---|---|
| `intent-cli intent` | `init-tree`, `add-feature`, `analyze-tree`, `lint-layout` |
| `intent-cli guide intent-work setup` | `--kind restructure` |

### Intent ナレッジツリー (G403/G404/G405)

- **`intent init-tree`** — ドメインを `tree-v1` レイアウト (`manifest.yaml` + カテゴリフォルダ) に初期化。4つのプロジェクトタイプに対応: `product-app`, `library-tool`, `infrastructure`, `research-prototype`。
- **`intent add-feature`** — 7つのスターターファイルとともに機能フォルダを追加し、`features/index.md` を自動更新。
- **`intent analyze-tree`** — フラットな intent ファイルのドライランまたは書き込みモードによる分析: 見出し抽出、キーワードベースのカテゴリ提案、参照検出 (Markdown リンク、アンカー、実行ユニット ID、パケットパス、GitHub URL)、移行参照マップ、`.restructure-backup/` コピー。
- **`intent lint-layout`** — フラットおよび tree-v1 ドメインのレイアウト健全性チェック。7つの lint コードを Markdown または JSON で出力 (`MISSING-DOMAIN`, `MISSING-MANIFEST`, `MISSING-CATEGORY-FOLDER`, `LARGE-FLAT-FILE`, `BROKEN-RELATIVE-LINK`, `MISSING-FEATURES-INDEX`, `MISSING-FEATURE-OVERVIEW`)。
- **`guide intent-work setup --kind restructure`** — デザイン AI プロンプトを出力し、フラットからツリーへの再設計ワークフローを駆動。`intent-cli` が決定論的分析を担い、セマンティックなグループ化はオペレーター + AI ペアが担当。

### 配布 (G386/G387)

- NuGet 安定版パッケージ: NuGet.org の `intent-cli` (Apache-2.0)。
- GitHub Release に添付されるセルフコンテインドバイナリ: `osx-arm64`, `win-x64`, `linux-x64`。
- Preview/main ビルドは引き続き `eng/version.json` の `nextVersion` を使用 (リリース後は `0.3.1-preview.*`)。

---

## インストール / アップデート

### .NET SDK を使用 (推奨)

```bash
# 新規インストール
dotnet tool install -g JTechJapan.IntentSystem.Cli

# 旧バージョンからのアップグレード
dotnet tool update -g JTechJapan.IntentSystem.Cli
```

**.NET 10 SDK** が必要です (`dotnet --version` → `10.x`)。

### .NET SDK なし (セルフコンテインドバイナリ)

[v0.3.0 GitHub Release アセット](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.0) からプラットフォームに合ったアーカイブをダウンロードし、展開して `intent-cli` バイナリを `PATH` に追加してください。

| プラットフォーム | アーカイブ名 |
|---|---|
| macOS (Apple Silicon) | `intent-cli-0.3.0-osx-arm64.tar.gz` |
| Windows (x64) | `intent-cli-0.3.0-win-x64.zip` |
| Linux (x64) | `intent-cli-0.3.0-linux-x64.tar.gz` |

対応する `.sha256` サイドカーファイルで `sha256sum` を使って検証してください。

### インストール確認

```bash
intent-cli --version
# 期待値: 0.3.0
```

---

## ライセンス

Apache-2.0 — リポジトリルートの [LICENSE](../../LICENSE) を参照してください。

---

## v0.3.0 GitHub Release の作成

> **メンテナー専用。** 安定リリースを切るための正式な手順です。GitHub Release を公開すると、リリースワークフローが自動的にトリガーされ、NuGet パッケージとセルフコンテインドバイナリがビルドされて添付されます。

### リリース前チェックリスト

- [ ] このリリースで意図した全 PR が `main` にマージ済みであること。
- [ ] `eng/version.json` に `"stableVersion": "0.3.0"` および `"nextVersion": "0.3.1"` が設定されていること (リリース後のバンプは G406 の一部としてすでにコミット済み — タグ付け前に変更しないこと。これは次の開発ラインを示すものであり、このリリース自体を示すものではない)。
- [ ] リリースワークフロー (`release.yml`) が `main` 上で正しいこと:
  - 安定バージョンをリリースタグから導出している (`eng/version.json` からではない)。
  - 安定パック手順で `PrivatePreview*` や有効期限プロパティを設定していない。
  - バイナリのドライランバージョンが `eng/version.json` の `nextVersion` にフォールバックしている。
- [ ] ローカルビルド後に `intent-cli --version` が期待するバージョンを報告すること。
- [ ] `git diff --check` が通ること (空白エラーなし)。
- [ ] `main` で CI がグリーンであること。

### リリース手順

1. **GitHub Release を作成** (GitHub UI または `gh release create`):
   - タグ: `v0.3.0` (`main` から作成)。
   - タイトル: `intent-cli v0.3.0 — first OSS-oriented stable release`。
   - 本文: 「v0.3.0 の内容」から「ライセンス」までの内容を貼り付ける (このチェックリストセクションは除く)。
   - **公開** (ドラフトではない) — 公開するとリリースワークフローがトリガーされる。

2. **リリースワークフローを監視** (Actions タブ):
   - `nupkg` ジョブ: `JTechJapan.IntentSystem.Cli.0.3.0.nupkg` をビルドし NuGet.org にプッシュ (`NUGET_API_KEY` が設定されている場合)。`.nupkg` + `.sha256` をリリースに添付。
   - `binaries` ジョブ (3×): `osx-arm64`, `win-x64`, `linux-x64` のセルフコンテインドアーカイブをビルド。`intent-cli --version` のスモークテスト後にリリースへ添付。

3. **GitHub Release ページでリリースアセットを確認**:
   - `JTechJapan.IntentSystem.Cli.0.3.0.nupkg` + `.sha256`
   - `intent-cli-0.3.0-osx-arm64.tar.gz` + `.sha256`
   - `intent-cli-0.3.0-win-x64.zip` + `.sha256`
   - `intent-cli-0.3.0-linux-x64.tar.gz` + `.sha256`

4. **NuGet.org を確認** (インデックス化に最大 15 分かかる場合あり):
   ```bash
   dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.0
   intent-cli --version
   # 期待値: 0.3.0
   ```

5. **必要に応じてアナウンス** (内部ドキュメントの更新、チームへの通知など)。

### リリース後のバージョンポリシー

`v0.3.0` リリース後、`main` の preview ビルドは `eng/version.json` の `nextVersion` からバージョンを導出します (現在は `0.3.1`)。Preview ビルドは `0.3.1-preview.<build>.<commit>` の形式になります。

次の安定リリース (`v0.3.1` 以降) の準備時には、この G406 PR と同様に、**リリース準備 PR 内で** `eng/version.json` を更新し、`stableVersion` を新しい安定バージョンに、`nextVersion` を次のパッチラインに設定してください。

> **ルール:** リリース後に `nextVersion` を公開済みの安定バージョンと同じ値のまま放置しないこと。`eng/version.json` のバンプがリリース境界を越えたことを示すコミット上の証拠となります。
