# 開発者リファレンス

> 日本語版。English version: [`../en/09-developer-reference.md`](../en/09-developer-reference.md)

このページはインストールオプション、パッケージ化された実行によるスモークテスト、
preview チャンネル、バージョンポリシーについて説明します。
メンテナー、コントリビューター、パワーユーザー向けです。
[クイックスタート](../../README.md#quickstart) に従う初心者向けではありません。

---

## .NET SDK なしでインストール

各 [GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest) には
SDK フリーの自己完結型バイナリが添付されています（.NET ランタイムが同梱されており、
SDK は不要です）。

| Platform | Asset |
| --- | --- |
| macOS (Apple Silicon) | `intent-cli-<version>-osx-arm64.tar.gz` |
| Windows (x64) | `intent-cli-<version>-win-x64.zip` |
| Linux (x64) | `intent-cli-<version>-linux-x64.tar.gz` |

各アーカイブには `.sha256` サイドカーが同梱されています。
使用前に両ファイルを同じディレクトリにダウンロードして確認してください。

**macOS:**

```bash
# 1. 確認（両ファイルがあるフォルダで実行）。
shasum -a 256 -c intent-cli-<version>-osx-arm64.tar.gz.sha256

# 2. 展開して PATH に配置。
tar -xzf intent-cli-<version>-osx-arm64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. 確認。
intent-cli --version
```

**Linux:**

```bash
# 1. 確認。
sha256sum -c intent-cli-<version>-linux-x64.tar.gz.sha256

# 2. 展開して PATH に配置。
tar -xzf intent-cli-<version>-linux-x64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. 確認。
intent-cli --version
```

**Windows:** `intent-cli-<version>-win-x64.zip` と `.sha256` サイドカーをダウンロード。
`CertUtil -hashfile intent-cli-<version>-win-x64.zip SHA256` のハッシュを
`.sha256` ファイルの最初のフィールドと比較し、解凍後 `intent-cli.exe` を PATH に配置してください。

リリースバイナリと OSS preview CI アーティファクトにはビルド時の有効期限はありません。

---

## パッケージ化された実行（ローカルスモークテスト）

CLI は .NET ツールとしてパッケージ化されています（パッケージ id `JTechJapan.IntentSystem.Cli`、
コマンド `intent-cli`）。ローカルビルドパッケージのスモークテスト:

```bash
export INTENT_CLI_LOCAL_VERSION="0.3.2-local.$(date -u +%Y%m%d%H%M%S)"
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj \
  -p:Version="$INTENT_CLI_LOCAL_VERSION" \
  -o .artifacts/packages
mkdir -p .artifacts/smoke-repo/.intent-cli
cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'
default_domain = "intent-cli"
artifact_root = ".intent-cli"
worktree_root = ".intent-cli/worktrees"
EOF
(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" JTechJapan.IntentSystem.Cli project status)
```

等価な `dnx` パス:

```bash
(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" JTechJapan.IntentSystem.Cli project status)
```

---

## Preview インストール

> OSS preview チャンネル。公開ユーザーは安定版 NuGet
> (`dotnet tool install -g JTechJapan.IntentSystem.Cli`) または上記のリリースバイナリを
> 使用してください。このセクションは stable リリース前の最新変更が必要なユーザー向けです。

`preview-pack` GitHub Actions ワークフローは `main` へのマージごとに実行され、
ワークフローアーティファクトとして `intent-cli-preview-<version>` という名前の
自己完結型インストールバンドルをアップロードします。

パッケージバージョンパターン: `<nextVersion>-preview.<run_number>.<run_attempt>`
（例: `0.3.1-preview.42.1`）。

```bash
# 1. ワークフローアーティファクトをダウンロードして解凍、そのディレクトリに cd。
cd ./intent-cli-preview-0.3.1-preview.42.1

# 2. チェックサムを確認（macOS: shasum; Linux: sha256sum）。
shasum -a 256 -c JTechJapan.IntentSystem.Cli.*.nupkg.sha256

# 3. .NET ツールをこのローカルフォルダからインストール（または更新）:
dotnet tool install --global --add-source . \
  --version 0.3.1-preview.42.1 JTechJapan.IntentSystem.Cli
# アップグレード:
dotnet tool update --global --add-source . \
  --version 0.3.1-preview.42.1 JTechJapan.IntentSystem.Cli

# アンインストール:
dotnet tool uninstall --global JTechJapan.IntentSystem.Cli
```

インストール済みバイナリは `intent-cli --version` で preview メタデータを表示します:

```text
intent-cli 0.3.1-preview.42.1-<short-sha>-G<unit>
channel=preview built=<iso-utc> commit=<full-sha>
```

**OSS preview パッケージには有効期限はなく、無期限で実行可能です。**

---

## バージョンフロー

リポジトリのバージョンポリシーは `eng/version.json` に記載されています。`stableVersion`
（最新の公開済み安定版）と `nextVersion`（準備中 / 開発中のライン）の単一の source of
truth です。G468 以降、ローカル `dotnet pack` のデフォルト `<Version>` はこのファイルから
導出されるため、ローカル pack / install は stale な csproj リテラルではなく開発中の
`nextVersion` を報告します:

```json
{
  "stableVersion": "0.3.6",
  "nextVersion": "0.3.7"
}
```

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `0.3.7-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `0.3.7-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `0.3.7-rc.N` | タグ `v0.3.7-rc.N` がリリースワークフローをトリガー |
| 安定版リリース | `0.3.7` | タグ `v0.3.7` がリリースワークフローをトリガー（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `0.3.8-preview.<run>.<attempt>` | `nextVersion` を `0.3.8` にバンプ後 |

**`v0.3.7` リリース後**、`eng/version.json` の両フィールドをバンプしてください:

```json
{
  "stableVersion": "0.3.7",
  "nextVersion": "0.3.8"
}
```

これにより次の main ブランチ CI ビルド（およびローカル pack）が
`0.3.8-preview.<run>.<attempt>` / `0.3.8-<sha>-<G-unit>` を生成し、`0.3.7`（安定版
リリースバージョンと衝突）の出力が継続されなくなります。

### 次リリース準備（v0.3.7）

**`v0.3.6` は publish 済み**（GitHub Release + NuGet, 2026-06-05）で、バージョンポリシーは
`0.3.7` 開発ラインにバンプされました（G470）。リポジトリは現在 in-development の **`0.3.7`**
`nextVersion` 上にあり、G475（本パケット）が `v0.3.7` リリースを準備します。次のリリースは
[リリース準備ゲート](release-notes-v0.3.7.md#リリース準備ゲート-g475)を通過後に `v0.3.7`
タグ付けで publish されます。準備はリリースを cut しません。完全な changelog と operator
チェックリスト: [release-notes-v0.3.7.md](release-notes-v0.3.7.md)。

**`v0.3.7` で出荷予定（`v0.3.6` 以降の変更）— automation 安全性リリース:**

- **非デフォルト実装 base branch**（G471）— loop-prompt 本文と review ガイダンスが非デフォルトの
  実装 base branch を first-class として扱い、child loop は host メタデータを読まずに正しい PR
  base を選択し、review は正しい base の PR を誤判定しません。
- **`issue-published` queue 互換**（G472）— review-closeout parsing が `issue-published`
  state の queue-state 行を許容し closeout 読み取りを中断しません。host review loop ガイダンスは
  skill-free で CI-pending を defer 条件として扱います。
- **installed guide を local rule docs より優先**（G473）— loop prompt 生成時、インストール済み
  `intent-cli guide` の出力がローカル `intents/rules/automations/*.md` より正統です。hard rule は
  operator が名指ししてもローカル rule docs を読むことを禁じます。
- **absorbed パケット retirement の安全性**（G474）— machine-readable なパケット lifecycle
  retirement（`lifecycle.yaml` サイドカー + `intent-cli packet retire`）が absorbed/superseded/
  retired パケットを `intent next-slice` の issue-cut-ready 選択から除外し、古い human
  `STATUS: ABSORBED` marker を修復対象として flag するため、absorbed パケットが重複 issue として
  再 cut されることはありません。

**リリース準備の検証（次の `v0.3.7` タグ付け前に実行）:**

```bash
cat eng/version.json   # stableVersion 0.3.6（公開済み）, nextVersion 0.3.7（リリース対象）
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   期待形: intent-cli 0.3.7-<sha>-G47x （stale なリテラルではない）
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.3.7.nupkg
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

公式リリースは `v0.3.7` タグの GitHub Release publish で cut され、リリースワークフローが
`-p:Version=0.3.7` を渡します（ローカルデフォルトより優先）。publish 後、上記のリリース後
`eng/version.json` バンプ（`stableVersion → 0.3.7`, `nextVersion → 0.3.8`）を適用します。

### 削除済みリリースタグ（`v0.3.3`）の再作成

`v0.3.3` は早すぎる段階でタグ付けされ、タグは削除されました。**`v0.3.3` タグ/リリースの再作成は、
リリースブロッカーの2パケットが両方 `main` にマージされ、リリース CI のテストジョブが green に
なってから**のみ行ってください:

- **G441** — 初回 host 初期化デッドロックの修正。
- **G443** — リリース CI 安定化（installed-CLI surface probe を Linux runner 上の
  `Text file busy` / ETXTBSY exec レースに対し堅牢化し、各テストプロジェクトが一意な名前の
  `*.trx` を出力してリリース CI 結果を診断可能にする）。

両修正を含むコミットで green な CI 実行を得る前に再タグすると、元の失敗したリリースジョブが
再現します。
