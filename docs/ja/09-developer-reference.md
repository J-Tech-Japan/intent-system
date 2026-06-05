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
  "stableVersion": "0.3.5",
  "nextVersion": "0.3.6"
}
```

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `0.3.6-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `0.3.6-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `0.3.6-rc.N` | タグ `v0.3.6-rc.N` がリリースワークフローをトリガー |
| 安定版リリース | `0.3.6` | タグ `v0.3.6` がリリースワークフローをトリガー（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `0.3.7-preview.<run>.<attempt>` | `nextVersion` を `0.3.7` にバンプ後 |

**`v0.3.6` リリース後**、`eng/version.json` の両フィールドをバンプしてください:

```json
{
  "stableVersion": "0.3.6",
  "nextVersion": "0.3.7"
}
```

これにより次の main ブランチ CI ビルド（およびローカル pack）が
`0.3.7-preview.<run>.<attempt>` / `0.3.7-<sha>-<G-unit>` を生成し、`0.3.6`（安定版
リリースバージョンと衝突）の出力が継続されなくなります。

### 次リリース準備（v0.3.6）

リポジトリは operator による明示的な **`v0.3.6`** リリース（現在の `nextVersion`）の
準備が整っています。リリース自体はタグ付けで publish され、このパケットはリリースを
cut しません。

**`v0.3.5` 以降のユーザー可視の変更:**

- **design-side プロセスファミリー** — 名前付きで discoverable な5つの design-thread
  surface: `intent-cli improve`（G456）・`intent-cli grill`（G463）・
  `intent-cli stack`（G464）・`intent-cli next`（G465）・`intent-cli inspect`（G466）。
  各々に top-level alias と `guide <name>` 形式があります。
- **guide カタログ / role 分類**（G467）— `intent-cli guide commands list` が role
  ベースのカタログ（design / host-review / child-implementation /
  recovery-diagnostics / advanced-developer）になり、`intent-cli guide help` が各
  role の surface を説明します。
- **version-source 整合**（G468）— ローカル pack/install・NuGet パッケージ・
  self-contained リリース artifact が package version・git short SHA・最新 G-unit を
  stale な csproj リテラルではなく `eng/version.json` と git から導出します。

**リリース準備の検証（`v0.3.6` タグ付け前に実行）:**

```bash
cat eng/version.json   # stableVersion 0.3.5（公開済み）, nextVersion 0.3.6（リリース対象）
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   期待形: intent-cli 0.3.6-<sha>-G46x （0.3.2-... ではない）
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.3.6.nupkg
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

公式リリースは `v0.3.6` タグの GitHub Release publish で cut され、リリースワークフローが
`-p:Version=0.3.6` を渡します（ローカルデフォルトより優先）。publish 後、上記のリリース後
`eng/version.json` バンプを適用します。

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
