# リリースノート — intent-cli v0.3.2

> **メンテナー向けリリースチェックリスト:** [v0.3.2 GitHub Release の作成](#v032-github-release-の作成) を参照。

## v0.3.2 の内容

v0.3.2 は、G432 で行った絶対 URL クリーンアップ後に NuGet.org パッケージページの
すべてのリンクが正しく解決されることを検証することに重点を置いた、
ドキュメントおよびパッケージング品質リリースです。新しいプロダクトコマンドはありません。

### NuGet パッケージ README 絶対リンク化 (G432)

- `README.md` 内のリポジトリ相対リンク（`./docs/...`、`./SECURITY.md` 等）を
  すべて絶対 GitHub blob URL に変換し、NuGet.org パッケージページでも正しく
  レンダリングされるようになりました。
- インストール/アップグレードコマンド、ドキュメントリンク、コミュニティリンク、
  ライセンス/通知リファレンスがすべて正しい安定パスを指していることを確認済みです。

### コントラクト検証の一貫性 (G433)

- `intent next-slice --dry-run` が `issue publish-flow` と同一の必須
  Child Issue Contract セクションリストを使用するようになりました。
  これにより、`next-slice` が `publish-flow` では拒否されるパケット
  （例: `Base Branch Policy` が欠如しているもの）に対して `issue-cut-ready`
  を報告するという不一致が解消されました。
- `automation host-review-diagnostics --candidate <unit>` が
  `issue-publish-ready` を報告する前に候補パケットの契約を検証するようになり、
  セクションが欠如している場合は JSON 出力に `missing_contract_sections` を
  含めるようになりました。
- 両サーフェスに `Base Branch Policy` を対象とした回帰テストを追加。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.3.2
```

または [v0.3.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.3.2)
から self-contained バイナリをダウンロード。使用前に `.sha256` を検証してください。

## v0.3.1 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.3.2
```

v0.3.1 からの破壊的変更はありません。

## v0.3.2 GitHub Release の作成

1. リリースコミットにタグを付ける: `git tag v0.3.2 && git push origin v0.3.2`
2. `release.yml` ワークフローが起動し、バイナリ、`.nupkg`、チェックサムをビルドします。
   完了を待ちます。
3. ワークフローが GitHub Release ドラフトを作成します。内容を確認し、
   このファイルの内容をリリース本文として貼り付けて公開します。
4. リリース後の確認チェックリスト:
   - [ ] NuGet.org パッケージページのリンクがすべて正しく解決される（G432 の
         絶対 URL が表示・機能する）。
   - [ ] GitHub リリースアセットリンク（`.tar.gz`、`.zip`、`.exe`、`.nupkg`）
         がアクセス可能。
   - [ ] `.sha256` チェックサムがダウンロードしたアーティファクトと一致する。
   - [ ] `intent-cli --version` が `0.3.2` を返す。
   - [ ] ローカルプレビュー/dry-run バージョンメタデータが次の開発ラインとして
         `0.3.3` を使用する。
