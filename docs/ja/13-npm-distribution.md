# npm 配布

← [ドキュメント索引](README.md) | → [開発者リファレンス](09-developer-reference.md)

G702 では npm 配布を `intent-cli` のインストール用インターフェースとして扱います。
非スコープの `intent-system` パッケージは薄いエントリーポイントで、optional dependency
から macOS Apple Silicon、Linux x64、または Windows x64 向けのランタイム同梱 release
バイナリを取得します。

## 常用と一回限りの実行

常用するコマンドはグローバルにインストールしてから実行します。

```bash
npm install -g intent-system
intent-cli --version
```

一回限りのコマンドは npx で実行します。

```bash
npx intent-system guide onboarding
```

shim は npm user agent から npx を検出し、`PATH` に `intent-cli` があるかを確認します。
両方の条件が一回限りの実行を示す場合だけ、実行完了後に
`npm install -g intent-system` と、その結果使う `intent-cli` を案内する短い行を**正確に 1 行**表示します。
shim 自身がインストールを実行することはありません。`postinstall` hook もパッケージインストール時の
ネットワークダウンロードもありません。

## channel-aware update (G703)

`intent-cli update` は、実行中 executable の symlink を解決した real path から毎回 channel を導出します。
channel marker は保存しません。update action の前に channel と path evidence を表示します。

```bash
intent-cli update
```

対応する action は次のとおりです。

- .NET global tool: `dotnet tool update -g JTechJapan.IntentSystem.Cli`。
- npm global: `npm install -g intent-system@latest`。
- npx cache: guidance のみ。`npx intent-system@latest` で再実行し、cache の変更や global install は行いません。
- standalone binary: 対応する release archive を取得し、`.sha256` sidecar を検証してから、同一 volume の
  temp+rename swap で binary を置き換えます。checksum mismatch の場合、元の binary は byte-identical のままです。

metadata の無い directory から、書き込みのない check を実行できます。

```bash
intent-cli update --check --format json
intent-cli update --check --format markdown
```

check は current version、latest release、would-be action を表示し、結果に
`process_spawned=false` と `writes_performed=false` を含めます。unknown または ambiguous な executable path は推測せず
fail closed し、4 channel すべての手動 guidance を表示します。

## リリースの整合性

release workflow は公開された Git tag から 1 つのバージョンを導出し、NuGet パッケージ、すべての
npm パッケージ、ランタイム同梱バイナリ、およびバイナリの `--version` 出力に同じ値を使います。
各プラットフォーム npm パッケージには SHA-256 digest と一致する `.sha256` sidecar を含めます。
プラットフォームパッケージの公開は NuGet と同じ保護された operator release transaction の一部だけで行います。
Pull request CI は package preparation、`npm pack`、checksum/version 検証、packed-install smoke test
までを実行します。npm への公開は行わず、npm 組織の credential も必要としません。

## .NET tool との共存

npm 経路と .NET global tool は共存できます。

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli
# または: npm install -g intent-system
command -v intent-cli
intent-cli --version
```

両方の経路が `intent-cli` という同じコマンドをインストールします。`PATH` で先に見つかるディレクトリが
使われるため、`$HOME/.dotnet/tools` と npm global bin directory の順序で使用する channel を選びます。
バージョン差を調査するときは古いバイナリと新しいパッケージを混在させず、その channel の package manager の
update command を使ってください。
