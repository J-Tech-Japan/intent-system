# Pattern: separate host × brand-new project

← [onboarding pattern を選ぶ](02a-getting-started-orchestration.md) | [docs インデックス](README.md)

## この setup

product code 用の空の `<owner>/<implementation-repo>` と host metadata 用の空の `<owner>/<intents-host-repo>` を作成します。**空の host repository だけを** checkout します。implementation repository は prompt で指定し、この最初の host session では checkout しません。

## Initial prompt — ちょうど 1 つを選ぶ

### Herdr-only

> 新しい target implementation repository `<owner>/<implementation-repo>` 用に intent-cli を設定します。空の intents host repository だけを開いています。まずインストール済み guidance で intent-cli を理解し、初期化して collocate した single-machine 4 スレッド team 用に `herdr-only` を record してください。

### Agmsg + herdr

> 新しい target implementation repository `<owner>/<implementation-repo>` 用に intent-cli を設定します。空の intents host repository だけを開いています。まずインストール済み guidance で intent-cli を理解し、初期化して distributed team または既存 agmsg investment 用に `agmsg` を record してください。

## agent が行うこと

shipped skill は `guide onboarding` に進みます。agent は version を確認し、`guide model` を読み、`intent init` を dry-run と `--write` で実行し、`host-check: ok` を確認します。観測した v0.11.0 の write は 9 files を生成します。選んだ session layer を `intent-cli session-layer set` で record してから、current guide で 4 スレッド team を provision します。2 つの prompt は最初の選択だけです。以降は recorded mode と installed guides に従います。

## 残る human decision

base-branch policy、transport の選択（collocation は herdr-only を最初に、distributed / existing-agmsg team は agmsg + herdr）、design・orchestration・implementation・review 各 role の agent kind を確認します。

## 次へ

[intent の整理・保守](03-intents.md) に進みます。
