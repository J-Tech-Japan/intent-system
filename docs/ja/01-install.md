# インストール

> **まず intent-cli に聞く。** インストール後、ワークフロー作業の前に
> `intent-cli guide start` を実行する。 ← [ドキュメント索引](index.md)

基本ルートは NuGet.org の .NET グローバルツール（**.NET 10 SDK** が必要。
`dotnet --version` で確認）。macOS / Windows / Linux で同じコマンド:

```bash
# インストール
dotnet tool install -g intent-cli

# その場でアップグレード
dotnet tool update -g intent-cli

# 確認
intent-cli --version
```

`~/.dotnet/tools`（macOS/Linux）または `%USERPROFILE%\.dotnet\tools`（Windows）が
`PATH` に無い場合、インストール出力に追加すべき行が表示されます。

**.NET SDK が無い場合**は、各プラットフォーム向けの self-contained バイナリを
[最新 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest)
から取得（ランタイム同梱）。使用前に `.sha256` を検証する。手順は
[ルート README](../../README.md#install-without-a-net-sdk) を参照。

**社内テスター**（`private-preview-pack` アーティファクト利用）は
[ルート README の private-preview セクション](../../README.md#private-preview-install)
を参照。

## 次へ

`intent-cli --version` を確認したら [プロジェクト開始](02-project-start.md) へ。
ただし先に `intent-cli guide start` を実行する。
