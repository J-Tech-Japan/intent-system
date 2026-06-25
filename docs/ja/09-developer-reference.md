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

### 日本語 / 非 UTF-8 の Windows コンソール (G484)

intent-cli は GitHub CLI（`gh`）サブプロセスの出力を、**周囲のコンソールのコードページに
依存せず UTF-8 として** 読み取ります。そのため日本語 Windows コンソール（cp932/932）でも
issue/PR のタイトルや本文が valid な JSON のまま保たれます。`worker next-action`,
`worker issue-preflight`, `worker pr-comment-preflight`, および host/review preflight の各経路が
このデコードを共有します。`chcp 65001` の実行や `$OutputEncoding` /
`[Console]::OutputEncoding` の手動設定は **不要** です。macOS/Linux の挙動は変わりません
（これらのコンソールは既に UTF-8 です）。

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

## same-repo メタデータトポロジ (G485)

same-repo トポロジは **コードブランチ** と **メタデータブランチ** を 1 つの GitHub
リポジトリに同居させる構成です（例: コードは `main`、メタデータ（`.intent-cli/` の
queue-state・runs・packets・`intents/<domain>/`）は `main-metadata`）。
`.intent-cli/config.toml` の `[project]` で設定します:

```toml
[project]
domain = "estivo"
artifact_root = ".intent-cli"
same_repo_topology = true
metadata_source_branch = "main-metadata"   # host loop がメタデータを READ するブランチ
metadata_write_branch  = "main-metadata"   # host loop がメタデータを WRITE するブランチ
```

これらのキーがそのまま `intent-cli automation same-repo-metadata-preflight` と
`intent-cli automation summary` に読み取られます。`same-repo-metadata-preflight` が
`not-configured` を返す場合、上記キーが解決されていません。`[project]`（別テーブルでない）
配下にあること、`metadata_source_branch` / `metadata_write_branch` の綴りが正確であることを
確認してください。

packet の正規の publish 経路は **`automation queue-seed-from-packet` →
`issue publish-flow` → `automation issue-publish`** で、手動の queue-state 編集や raw
`gh issue create` は不要です。ドメインの `execution_unit_regex`（
`intents/<domain>/automation/bindings.md` に宣言、例 `^E\d{3,}$`）は単一の共有ソースから
解決されるため、`automation summary --domain <d>` と
`queue-seed-from-packet --execution-unit <unit>` がどの unit を有効とみなすか常に一致します。
アクティブなドメインの regex に一致しない unit は、参照した bindings ソースを明示する精密な
診断とともに拒否されます。

---

## バージョンフロー

リポジトリのバージョンポリシーは `eng/version.json` に記載されています。`stableVersion`
（最新の公開済み安定版）と `nextVersion`（準備中 / 開発中のライン）の単一の source of
truth です。G468 以降、ローカル `dotnet pack` のデフォルト `<Version>` はこのファイルから
導出されるため、ローカル pack / install は stale な csproj リテラルではなく開発中の
`nextVersion` を報告します:

```json
{
  "stableVersion": "0.3.9",
  "nextVersion": "0.3.10"
}
```

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `0.3.10-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `0.3.10-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `0.3.10-rc.N` | タグ `v0.3.10-rc.N` がリリースワークフローをトリガー |
| 安定版リリース | `0.3.10` | タグ `v0.3.10` がリリースワークフローをトリガー（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `0.3.11-preview.<run>.<attempt>` | `nextVersion` を `0.3.11` にバンプ後 |

**`v0.3.10` リリース後**、`eng/version.json` の両フィールドをバンプしてください:

```json
{
  "stableVersion": "0.3.10",
  "nextVersion": "0.3.11"
}
```

これにより次の main ブランチ CI ビルド（およびローカル pack）が
`0.3.11-preview.<run>.<attempt>` / `0.3.11-<sha>-<G-unit>` を生成し、`0.3.10`（安定版
リリースバージョンと衝突）の出力が継続されなくなります。

### 次リリース準備（v0.3.10）

**`v0.3.9` は publish 済み**（GitHub Release + NuGet）で、バージョンポリシーは
`0.3.10` 開発ラインにバンプされました。リポジトリは現在 in-development の **`0.3.10`**
`nextVersion` 上にあり、G486（本パケット）が `v0.3.10` リリースを準備します。次のリリースは
[リリース準備ゲート](release-notes-v0.3.10.md#リリース準備ゲート-g486)を通過後に `v0.3.10`
タグ付けで publish されます。準備はリリースを cut しません。完全な changelog と operator
チェックリスト: [release-notes-v0.3.10.md](release-notes-v0.3.10.md)。

**`v0.3.10` で出荷予定（`v0.3.9` 以降の変更）— dogfooding 安定性リリース:**

- **Windows 日本語 `gh` JSON デコード**（G484）— すべての `gh` サブプロセスが UTF-8 の
  ストリームデコードを pin するため、日本語 Windows コンソール（cp932）でも issue/PR の
  タイトルや本文が valid な JSON のまま保たれます。`worker next-action` / preflight が非 ASCII
  ペイロードで壊れなくなり、`chcp 65001` や手動の出力エンコーディング設定は不要です。
  macOS/Linux の挙動は不変です。
- **same-repo メタデータの publish-flow 信頼性**（G485）— `queue-seed-from-packet` が
  ドメインの `execution_unit_regex` を `automation summary` と同じ共有 resolver で解決する
  ため、valid な same-repo packet（コードブランチ `main`、メタデータブランチ `main-metadata`）が
  `missing-domain-binding-regex` で拒否されず、正規の `queue-seed-from-packet` →
  `issue publish-flow` → `automation issue-publish` 経路で seed/publish できます。サポートされる
  `[project]` の same-repo 設定キーも文書化しました。

**リリース準備の検証（次の `v0.3.10` タグ付け前に実行）:**

```bash
cat eng/version.json   # stableVersion 0.3.9（公開済み）, nextVersion 0.3.10（リリース対象）
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   期待形: intent-cli 0.3.10-<sha>-G48x （stale なリテラルではない）
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.3.10.nupkg
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

公式リリースは `v0.3.10` タグの GitHub Release publish で cut され、リリースワークフローが
`-p:Version=0.3.10` を渡します（ローカルデフォルトより優先）。publish 後、上記のリリース後
`eng/version.json` バンプ（`stableVersion → 0.3.10`, `nextVersion → 0.3.11`）を適用します。

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
