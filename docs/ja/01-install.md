# インストール

← [ドキュメント索引](README.md) | → [プロジェクト開始](02-project-start.md)

`intent-cli` をインストールして動作確認します。インストールが終わったら [プロジェクト開始](02-project-start.md) に進んでください。

## .NET SDK が入っていない場合

NuGet global tool として intent-cli を入れるには .NET SDK が必要です。
まだ `dotnet` コマンドが使えない場合は、Microsoft の公式ページから .NET 10 SDK をインストールしてください。

- https://dotnet.microsoft.com/en-us/download

インストール後、ターミナルで次を確認します。

```bash
dotnet --version
```

バージョンが表示されたら、次の手順に進めます。

## インストール

基本ルートは NuGet.org の .NET グローバルツール（**.NET 10 SDK** が必要）。macOS / Windows / Linux で同じコマンド:

```bash
# インストール
dotnet tool install -g JTechJapan.IntentSystem.Cli

# その場でアップグレード
dotnet tool update -g JTechJapan.IntentSystem.Cli

# 確認
intent-cli --version
```

`~/.dotnet/tools`（macOS/Linux）または `%USERPROFILE%\.dotnet\tools`（Windows）が
`PATH` に無い場合、インストール出力に追加すべき行が表示されます。

**.NET SDK が無い場合**は、各プラットフォーム向けの `self-contained`（ランタイム同梱）バイナリを
[最新 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest)
から取得（ランタイム同梱）。使用前に `.sha256` を検証する。手順は
[開発者リファレンス](09-developer-reference.md#net-sdk-なしでインストール) を参照。

**プレビューチャンネルユーザー**（`preview-pack` アーティファクト利用）は
[開発者リファレンスのプレビューセクション](09-developer-reference.md#preview-インストール)
を参照。

## 次へ

`intent-cli --version` を確認したら [プロジェクト開始](02-project-start.md) へ。
