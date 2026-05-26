# リリースノート — intent-cli v0.3.1

> **メンテナー向けリリースチェックリスト:** [v0.3.1 GitHub Release の作成](#v031-github-release-の作成) を参照。

## v0.3.1 の内容

v0.3.1 は v0.3.0 に続く最初の OSS 対応強化リリースです。リリースパッケージングの改善、リポジトリクリーンアップ、コミュニティファイルの追加、および OSS レディネスチェックリストを含みます。新しいプロダクトコマンドはありません。

### リリースパッケージング (G409)

- リリースワークフローのチェックサムサイドカーが、配布物相対パスではなくベアファイル名
  (`intent-cli-linux-x64.tar.gz.sha256`) を使用するように変更。
  ダウンロードディレクトリから `sha256sum -c` / `CertUtil -hashfile` が
  アーカイブ展開なしで直接動作します。
- README の検証手順をプラットフォーム別（Linux / macOS / Windows）のセクションに更新。

### リポジトリクリーンアップ (G410)

- `.takt/` ランタイムトレースディレクトリとその `.gitignore` を削除。
- `ops/` 内の自動化ノートを `docs/automation-templates/` および `eng/` に移動。
  古い履歴 ops ノートを削除。
- `GuideRulesCommand` のソース参照を `ops/` から `docs/automation-templates/` に更新。

### OSS コミュニティファイル (G411)

- `CONTRIBUTING.md` を追加（ask-intent-cli-first ルール、開発環境セットアップ、
  PR の期待事項、コーディング規約を含む）。
- `CODE_OF_CONDUCT.md` を追加（Contributor Covenant v2.1 ベース）。
- `SECURITY.md` を追加（脆弱性のプライベート報告手順）。
- `SUPPORT.md` を追加。
- `.github/FUNDING.yml`、issue テンプレート、PR テンプレートを追加
  （いずれも intent-cli ガイダンスプロンプトを含む）。

### OSS レディネス (G412)

- `docs/oss-readiness-checklist.md` を追加（公開前の監査チェックリスト）。
- インストールドキュメントおよびエラーメッセージ内の古い「internal testing channel」/
  「private-preview-install」表現を修正。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.1
```

または [v0.3.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.1)
から self-contained バイナリをダウンロード。使用前に `.sha256` を検証してください。

## v0.3.0 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.1
```

v0.3.0 からの破壊的変更はありません。

## v0.3.1 GitHub Release の作成

1. リリースコミットにタグを付ける: `git tag v0.3.1 && git push origin v0.3.1`
2. `release.yml` ワークフローが起動し、バイナリ、`.nupkg`、チェックサムをビルドします。
   完了を待ちます。
3. ワークフローが GitHub Release ドラフトを作成します。内容を確認し、
   このファイルの内容をリリース本文として貼り付けて公開します。
4. 公開後数分以内に `dotnet tool install -g JTechJapan.IntentSystem.Cli` で
   新バージョンが NuGet.org から解決できることを確認します。
