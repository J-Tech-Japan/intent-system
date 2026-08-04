# Pattern: same repository metadata branch × existing project

← [onboarding pattern を選ぶ](02a-getting-started-orchestration.md) | [docs インデックス](README.md)

## この setup

既存の `<owner>/<implementation-repo>` を維持し、初期化前に intended implementation base branch から metadata branch（例: `main-metadata`）を作成します。最初の host session では **metadata-branch checkout だけを**開きます。implementation branch と既存 code は host metadata の作業から分けます。

## Initial prompt — ちょうど 1 つを選ぶ

### Herdr-only

> 既存 repository `<owner>/<implementation-repo>` に intent-cli を追加します。metadata-branch checkout だけを開いています。まず installed guidance で intent-cli を理解し、この host を初期化して collocate した single-machine 4 スレッド team 用に `herdr-only` を record してください。

### Agmsg + herdr

> 既存 repository `<owner>/<implementation-repo>` に intent-cli を追加します。metadata-branch checkout だけを開いています。まず installed guidance で intent-cli を理解し、この host を初期化して distributed team または既存 agmsg investment 用に `agmsg` を record してください。

## agent が行うこと

shipped skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` dry-run を実行し、`init --write` を適用して host を確認します。その後、選んだ session layer を `intent-cli session-layer set` で record し、current guide で 4 スレッド team を provision します。新規 v0.11.0 write は 9 files を作ります。2 つの prompt は initial transport だけを選び、以降は recorded mode を使います。

## 残る human decision

child PR 用の base-branch policy、transport の選択（collocation は herdr-only を最初に、distributed / existing-agmsg team は agmsg + herdr）、各 role の agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
