# Pattern: separate host × existing project

← [onboarding pattern を選ぶ](02a-getting-started-orchestration.md) | [docs インデックス](README.md)

## この setup

既存の `<owner>/<implementation-repo>` は変更しません。host metadata 用に空の `<owner>/<intents-host-repo>` を別に作り、最初の session では **その host repository だけを** checkout します。既存 implementation repository は prompt で指定し、ここで host checkout と混ぜません。

## Initial prompt — ちょうど 1 つを選ぶ

### Herdr-only

> 既存 target implementation repository `<owner>/<implementation-repo>` に intent-cli を追加します。空の separate intents host repository だけを開いています。まず installed guidance で intent-cli を理解し、host を初期化して collocate した single-machine team 用に `herdr-only` を record してください。

### Agmsg + herdr

> 既存 target implementation repository `<owner>/<implementation-repo>` に intent-cli を追加します。空の separate intents host repository だけを開いています。まず installed guidance で intent-cli を理解し、host を初期化して distributed または existing-agmsg team 用に `agmsg` を record してください。

## agent が行うこと

shipped skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` を dry-run し、`init --write` を適用して host が ok であることを確認します。その後 session layer を `intent-cli session-layer set` で record し、current guide で 4 スレッド team を provision します。新規 v0.11.0 host write は 9 files を作ります。prompt variant は最初の transport だけを選び、下流の手順を混ぜません。

## 残る human decision

child PR 用の base-branch policy、transport の選択（collocation は herdr-only を最初に、distributed / existing-agmsg team は agmsg + herdr）、各 role の agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
