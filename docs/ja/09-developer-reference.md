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

host と child の bootstrap（G514）: host 側 automation コマンド（`automation summary`、
`automation same-repo-metadata-preflight`、`automation queue-seed-from-packet`）は解決された
repo root の `.intent-cli/config.toml` をロードするため、他の host コマンドと同じ effective な
`[project]` 設定（同じ same-repo トポロジ設定）を参照します。`.intent-cli/config.toml` を **持たない**
child/standalone 実装 repo は安全なデフォルト bootstrap 挙動を保ちます（parent metadata 不要）。
same-repo host repo で host コマンドを実行してもデフォルト挙動になる場合、コマンドが repo 内から
実行されている（resolver は `.intent-cli/` ディレクトリまで上に辿る）こと、config ファイルが
存在することを確認してください。

packet の正規の publish 経路は **`automation queue-seed-from-packet` →
`issue publish-flow` → `automation issue-publish`** で、手動の queue-state 編集や raw
`gh issue create` は不要です。ドメインの `execution_unit_regex`（
`intents/<domain>/automation/bindings.md` に宣言、例 `^E\d{3,}$`）は単一の共有ソースから
解決されるため、`automation summary --domain <d>` と
`queue-seed-from-packet --execution-unit <unit>` がどの unit を有効とみなすか常に一致します。
アクティブなドメインの regex に一致しない unit は、参照した bindings ソースを明示する精密な
診断とともに拒否されます。

### execution-unit を解決するサーフェスの domain 解決順序 (G522)

`--pr` や `--execution-unit` から execution unit を解決するサーフェス
（`review closeout-plan`、`automation queue-seed-from-packet`、
`automation publish-recovery`、および同じ lookup を使う peer サーフェス）は、
`--domain` が省略された場合に次の解決順序を適用します:

1. 明示的な `--domain` が優先される — 解決された packet 自身の `domain:`
   スカラーが宣言する値と矛盾する場合はエラーになる。
2. それ以外の場合、解決された packet.yaml / queue metadata が宣言する
   domain を使用する。
3. それ以外の場合、サーフェスは fail loud する — `intents/*/` から
   スキャンした候補 domain と、正確な `--domain` 再実行コマンドを示す。
   ホストのデフォルト domain binding（`.intent-cli/config.toml` の
   `[project] domain`）へ黙って fallback することは決してない。

これは multi-domain host での既知のギャップを解消します: 従来の default
binding fallback は、packet 自身の `domain:` フィールドが別の値を宣言して
いても、間違った domain に対して報告・検証してしまうことがありました
（例: `review closeout-plan --pr <n>` が、解決された packet の実際の domain
ではなくホストの default domain を報告してしまう、あるいは
`queue-seed-from-packet` が間違った domain の `execution_unit_regex`
チェックを実行してしまう、など）。default binding の仕組み自体は変更
されておらず他の箇所では引き続き使われます。変わったのは、これらの
サーフェスが `--domain` 省略時に何を参照するかだけです。

3つのサーフェスすべてがこの順序を厳密に適用します — domain を導出できない
場合に `[project] domain` へ fallback することはありません:

- `automation queue-seed-from-packet` — `--domain` と packet の `domain:`
  フィールドのどちらも無い場合、seed を拒否します。
- `review closeout-plan` — 解決された queue item に対して domain を
  導出できない場合（一致する queue item が無い、またはその packet.yaml に
  `domain:` フィールドが無い場合）、ホストの default domain binding を
  報告する代わりに、候補 domain と正確な `--domain` 再実行コマンドを示して
  fail loud します。
- `automation publish-recovery` は、各 execution unit の候補が repair 解析に
  参加する前に、必ず domain を解決します — `--domain` が指定されていれば
  それを使用し（その候補自身が宣言する packet-declared domain と矛盾する
  場合は候補ごとにエラーになります）、指定が無ければその候補自身の
  packet-declared domain から導出します。どちらも無い候補は、スキャンに
  黙って参加する（あるいは黙って除外される）のではなく、構造化された
  `domain-underivable` の unsafe stop になります。明示的な `--domain` と
  矛盾する候補は構造化された `domain-contradiction` の unsafe stop に
  なります。これは `--pr` でスコープされたパスと、スコープなしの broad
  scan の両方に適用されます。`--domain` を完全に省略した場合は
  cross-candidate なスコープを要求したことにはならないため、
  （個別に導出可能な）異なる domain を持つ複数の候補が 1 回の broad-scan
  結果に共存することがあります。

### stalled-work 検出 (G523)

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] --format json|markdown`
は、保留中の pipeline transition を age 付きで一覧化する **read-only** な
サーフェスです。これにより、1 回の orchestrator wake（あるいは外部の
heartbeat）だけで、人間が GitHub label・PR state・queue-state を手で
突き合わせることなく stall を検出・復旧できます。GitHub label、
queue-state、`runs.jsonl` を変更することは一切ありません。

カテゴリ:

- `published-not-delegated` — OPEN の issue が `intent-target` を持つが、
  claim label（`intent-issue-in-progress` / `intent-pr-created`）がまだ無く、
  PR も一度も作成されていない。
- `pr-created-not-reviewing` — 元の issue が `intent-pr-created` を持ち、
  その issue を close する PR に `review-start` transition がまだ適用
  されていない（PR に `intent-pr-reviewing` / `intent-pr-approved` が無い）。
- `merged-not-closed-out` — MERGED 状態の PR に紐づく queue-state item が
  まだ `Completed` になっていない（closeout — `pr-merged` +
  `closeout-recorded` の runs event — がまだ記録されていない）。

各 item は `kind`、`execution_unit`、`issue` および/または `pr`
（番号 + url）、`age_minutes`、`recommended_action`（次に実行すべき
正確な canonical コマンド — それぞれ `worker claim`、`automation
pr-transition --transition review-start`、`closeout pr`）を報告します。
`--stale-minutes` は、指定した閾値より新しい item を除外します
（デフォルトは `0` — すべてを age 付きで報告し、閾値は呼び出し側が選ぶ）。
`age_minutes` は、GitHub が label 適用時刻を公開していないため、
該当する GitHub entity の `createdAt`/`updatedAt` タイムスタンプからの
近似値です。`published-not-delegated` は、既に取得済みの PR closing
reference も issue label とは独立にチェックします — そのため、
completion label が実態とずれてしまっていても（intent-pr-created が
一度も付与されていない、または削除されてしまったが、OPEN の PR が
既にその issue を close している場合）、誤って `worker claim` を推奨する
ことはありません。

**domain isolation は（title-prefix の正規表現一致ではなく）G522 と同様に
packet/queue metadata に基づきます。** すべての GitHub issue/PR candidate
について `<unit>: ...` というタイトル prefix を導出しますが、これは
その candidate の `.intent-cli/issues/<unit>/packet.yaml` を特定するため
だけに使われます — 要求された `--domain` と照合する唯一の権威は、
その packet 自身が宣言する `domain:` フィールドです。candidate が
`items[]` に含まれるのは、packet が宣言する domain が要求された
`--domain` と完全に一致する場合のみです。packet が宣言する domain が
`--domain` と矛盾する candidate、あるいは domain を全く導出できない
candidate（packet.yaml が無い、またはそれに `domain:` フィールドが
無い）は FAIL-CLOSED になります: `items[]` から除外され、代わりに
`excluded[]`（`kind`、`execution_unit`、`issue`/`pr`、`reason`、
`detail`）に報告されます。`reason` は `domain-contradiction`
（`detail` に矛盾している具体的な packet-declared domain を明示）
または `domain-underivable`（`detail` に `intents/*/` からスキャンした
候補 domain **と** 正確に実行可能な再実行コマンド —
`intent-cli automation stalled-work --domain <name> --repo <owner/repo>
--format json` — の両方を明示。G522 の underivable diagnostic 契約を
継承）のいずれかです。（単一の operator 指定 execution unit に対しては
明示的な `--domain` 単独で成立する）他の G522 サーフェスとは異なり、
これは共有 repo の issue/PR にまたがる broad multi-candidate scan です
— 明示的な `--domain` だけでは、自身のメタデータで裏付けが取れない
candidate に適用されると信頼することはありません。したがって
candidate が黙って scan に紛れ込むことも、黙って消えることもありません。

このスライスは検出のみです — orchestrator wake procedure や外部
heartbeat からこのサーフェスを利用する部分は、別の後続スライスです。

---

## バージョンフロー

リポジトリのバージョンポリシーは `eng/version.json` に記載されています。`stableVersion`
（最新の公開済み安定版）と `nextVersion`（準備中 / 開発中のライン）の単一の source of
truth です。G468 以降、ローカル `dotnet pack` のデフォルト `<Version>` はこのファイルから
導出されるため、ローカル pack / install は stale な csproj リテラルではなく開発中の
`nextVersion` を報告します:

```json
{
  "stableVersion": "0.3.14",
  "nextVersion": "0.3.15"
}
```

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `0.3.15-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `0.3.15-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `0.3.15-rc.N` | タグ `v0.3.15-rc.N` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する |
| 安定版リリース | `0.3.15` | タグ `v0.3.15` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `0.3.16-preview.<run>.<attempt>` | `nextVersion` を `0.3.16` にバンプ後 |

**`v0.3.15` リリース後**、`eng/version.json` の両フィールドをバンプしてください:

```json
{
  "stableVersion": "0.3.15",
  "nextVersion": "0.3.16"
}
```

これにより次の main ブランチ CI ビルド（およびローカル pack）が
`0.3.16-preview.<run>.<attempt>` / `0.3.16-<sha>-<G-unit>` を生成し、`0.3.15`（安定版
リリースバージョンと衝突）の出力が継続されなくなります。

### 次リリース準備（v0.3.15）

**`v0.3.14` は publish 済み**（GitHub Release + NuGet）で、バージョンポリシーは
`0.3.15` 開発ラインにバンプされました。リポジトリは現在 in-development の **`0.3.15`**
`nextVersion` 上にあり、G519 は **prepare-only** です — version メタデータと docs をバンプするだけで
publish ステップを追加しません。version-bump マージ自体は GitHub Release やタグを作成しません。
マージされ
[リリース準備ゲート](release-notes-v0.3.15.md#リリース準備ゲート-g519)が成り立った後、
**メンテナ/オペレーター（または外部のリリース automation）が `v0.3.15` の GitHub Release を作成・
publish** します。その Release の publish が `.github/workflows/release.yml`（`on: release: published`）を
発火させ、NuGet package とプラットフォームごとのバイナリ成果物を build・publish します。
完全な changelog と operator チェックリスト:
[release-notes-v0.3.15.md](release-notes-v0.3.15.md)。

**`v0.3.15` で出荷予定（`v0.3.14` 以降の変更）— orchestrator/agmsg 運用修正:**

- **agmsg Monitor 欠落時の Claude project-settings 診断**（G517）— `ToolSearch select:Monitor` が
  Claude Code の `Monitor` ツールをまったく見つけられない場合（`1 shell` vs `1 monitor` の
  delivery-mode 混同ではなく、ツールサーフェス問題）、ガイドに known-good 比較チェックリスト、
  疑わしい project-level `env` override、安全なオペレーター修復手順を追加。
- **orchestrator モードのタイマーを design-side watchdog へシフト**（G518）— 通常の定常状態が
  メッセージ駆動になり（implementation/review の返信が orchestrator を起こす）、明示的な
  orchestrator タイマーは fallback/legacy ポーリングオプションとしてのみサポートされ、新しい
  任意・低頻度の design-side watchdog が推奨セーフティネットとして追加された。
- orchestrator モードは引き続き **preview/experimental** です: オプトインで、まだ hardening 中であり、
  timer-loop モードは完全サポート・不変です。
  [エージェントメッセージオーケストレーション](12-agent-message-orchestration.md) を参照。

**リリース準備の検証（`v0.3.15` version bump のマージ前に実行）:**

```bash
cat eng/version.json   # stableVersion 0.3.14（公開済み）, nextVersion 0.3.15（リリース対象）
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   期待形: intent-cli 0.3.15-<sha>-G51x （stale なリテラルではない）
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.3.15.nupkg
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

version-bump マージが `main` に入った後、メンテナ/オペレーター（または外部のリリース automation）が
`v0.3.15` の GitHub Release を作成・publish します。その publish が `release.yml`
（`on: release: published`）を発火させ、NuGet package とプラットフォームごとのバイナリ成果物を
build・publish します。publish 後、上記のリリース後 `eng/version.json` バンプ
（`stableVersion → 0.3.15`, `nextVersion → 0.3.16`）を適用します。

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
