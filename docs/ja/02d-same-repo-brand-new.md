# Pattern: same repository metadata branch × brand-new project

← [onboarding pattern を選ぶ](02a-getting-started-orchestration.md) | [docs インデックス](README.md)

## この setup

新しい `<owner>/<implementation-repo>` に intended implementation base branch を作り、初期化前にそこから metadata branch（例: `main-metadata`）を作成します。この session では **その metadata-branch checkout だけを**開きます。product code と child PR は implementation base branch、host metadata は metadata branch に置きます。

## Initial prompt — ちょうど 1 つを選ぶ

### Herdr-only

> 新しい repository `<owner>/<implementation-repo>` 用に intent-cli を設定します。metadata-branch checkout だけを開いています。まず installed guidance で intent-cli を理解し、この host を初期化して collocate した single-machine 4 スレッド team 用に `herdr-only` を record してください。

### Agmsg + herdr

> 新しい repository `<owner>/<implementation-repo>` 用に intent-cli を設定します。metadata-branch checkout だけを開いています。まず installed guidance で intent-cli を理解し、この host を初期化して distributed team または既存 agmsg investment 用に `agmsg` を record してください。

## agent が行うこと

shipped skill は `guide onboarding` に dispatch します。agent は version を確認し、`guide model` を読み、`intent init` を dry-run し、`init --write` を適用して host が ok か確認します。それから session layer を `intent-cli session-layer set` で record し、current guide で 4 スレッド team を provision します。新規 v0.11.0 write は 9 files を作ります。違うのは initial prompt だけで、以降は recorded mode に従います。

## 残る human decision

base-branch policy、transport の選択（collocation は herdr-only を最初に、distributed / existing-agmsg team は agmsg + herdr）、各 role の agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
