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

**npm エントリーポイント（.NET SDK 不要）:** npm からランタイム同梱のプラットフォーム
パッケージをインストールするか、npx で一回だけ実行できます。

```bash
npm install -g intent-system
intent-cli --version
npx intent-system guide onboarding
```

npm shim が代わりにグローバルインストールを実行することはありません。npx の 1 行 guidance、
checksum、release gate、.NET tool と同じ `PATH` で共存するルールは
[npm 配布ガイド](13-npm-distribution.md)を参照してください。

**.NET SDK が無い場合**は、各プラットフォーム向けの `self-contained`（ランタイム同梱）バイナリを
[最新 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest)
から取得（ランタイム同梱）。使用前に `.sha256` を検証する。手順は
[開発者リファレンス](09-developer-reference.md#net-sdk-なしでインストール) を参照。

**プレビューチャンネルユーザー**（`preview-pack` アーティファクト利用）は
[開発者リファレンスのプレビューセクション](09-developer-reference.md#preview-インストール)
を参照。

## 0.32.1-preview.1 を選んでインストールする場合

以下は、`0.32.1-preview.1` の公開後に利用者が選べる手順です。
この文書の変更によって version が利用可能になるわけではありません。.NET tool には
.NET 10 SDK が必要です。安定版のグローバル tool を残す場合は、preview 版を別の directory に
入れてください。実行前に `<isolated-directory>` を自分が管理する directory に置き換えてください。

```bash
dotnet tool install --tool-path <isolated-directory> JTechJapan.IntentSystem.Cli --version 0.32.1-preview.1
```

インストールしたコマンドは完全な path で実行し、現在の version を確認します。

```bash
<isolated-directory>/intent-cli --version
```

npm 経由の導入も利用者が明示して選べます。npm の全体用 install は、同じ npm prefix にある
`intent-system` package を置き換えることがあります。

```bash
npm install -g intent-system@0.32.1-preview.1
intent-cli --version
```

明示した version はこの preview 版を選び、`latest` は安定版の配布経路にとどまります。npm の全体用
install を安定版 0.32.0 に戻す場合は、意図して
`npm install -g intent-system@0.32.0` を実行します。isolated な .NET install の場合は、専用
directory を削除すれば既存の安定版コマンドをそのまま使えます。

公開後、ランタイム同梱の macOS-arm64 / Linux-x64 / Windows-x64 asset は、正確な
[0.32.1-preview.1 GitHub release page](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1)
から取得します。実行前に各 asset を対応する `.sha256` sidecar と照合してください。
preview-pack artifact や workflow-dispatch candidate は、公開済みリリースではありません。

.NET の global tool、npm の shim、単独実行形式はいずれも `intent-cli` というコマンド名を
使うため、`PATH` で先に見つかったものが実行されます。隔離した preview 版は上記の完全な tool-path
で起動してください。別のインストール先を調べるには `command -v intent-cli`（macOS/Linux）または
`where intent-cli`（Windows）を実行し、その後 `intent-cli --version` で配布経路を確認します。

preview 版は安定版 0.32.0 の既定動作を変更しません。利用者が選択していない verification と lesson の
動作は従来どおりです。worker evidence gate は明示的な flag を要求し、team の既定値から policy を
推測しません。ローカル report の来歴は呼び出し元から提供され、認証済みではありません。lesson の
適用状況の説明は助言にとどまり、`declared-not-verified` と示されます。gate として有効ではありません。文書準備 PR は
製品の公開や元に戻す操作を保証しません。brief の出力に失敗すると、不完全な出力や別プロセスが置いたファイルが
残ることがあるため、別途確認してください。raw TRX report には test 名・output・machine path が含まれ、
registry 全体の digest から registry の識別情報が分かる場合があります。commit または公開の前に開示内容を
確認してください。

対象範囲・制約・公開までの手順は
[0.32.1-preview.1 リリースノート](release-notes-v0.32.1-preview.1.md) を参照してください。

## 次へ

`intent-cli --version` を確認したら [プロジェクト開始](02-project-start.md) へ。
