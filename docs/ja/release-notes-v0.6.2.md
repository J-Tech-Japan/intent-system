# リリースノート — intent-cli v0.6.2（DRAFT — 未リリース）

> **⚠️ DRAFT / 未リリース。** これはリリースノートではなく **stub** です。
> `eng/version.json` が `0.6.2` を「これからカットするリリース」として指しており、
> G475 のガードが publish 前にそのバージョンのノートの存在を要求するため置かれています。
> **実際の内容は v0.6.2 の release-prep パケットが author します**。ここには出荷済みの
> 挙動は一切書かれておらず、このファイルを changelog として扱ってはいけません。

## ステータス

`nextVersion` が `0.6.2` になった時点で、リリース後の version roll（G554 のルール、
G557 による改訂版）によって作成されました。release-prep パケットが埋めるまでは:

- **スライスは未記載です。** `v0.6.2` で何が出荷されるかを決めるのはこの stub ではなく
  release-prep パケットです。
- **バンプ根拠も未記載です**（patch か minor かは release-prep の判断です）。
- **リリース準備ゲートも未記載です。** このファイルが draft のままの間は `v0.6.2` の
  GitHub Release を publish **しないでください** — 埋まっていない stub は release-prep が
  未実行であることを意味します。

## release-prep パケットがこれを置き換える際に必要なもの

過去のノート（[v0.6.1](release-notes-v0.6.1.md)、[v0.6.0](release-notes-v0.6.0.md)）の
形に従ってください:

- 出荷内容をテーマ別にグループ化し、マージされたスライスを正確にカバーする;
- バンプ根拠（patch か minor か）をラベルだけでなく理由まで記述する;
- prepare-only の publishing セクションとリリース準備ゲート;
- 追加サーフェスと是正的な挙動変更を分離した upgrade セクション;
- リリース後の roll のリマインド。

## インストール（プレースホルダー — 下記バージョンがガードの検査対象です）

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.6.2
```

publish 後、self-contained バイナリは
[v0.6.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.6.2)
に添付されます。使用前に `.sha256` サイドカーを検証してください。
