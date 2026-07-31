# リリースノート — intent-cli v0.7.0

> **リリースモデル:** `v0.7.0` の **GitHub Release を作成・publish するのは
> メンテナ/オペレーター(または外部のリリース automation)**です。バージョンバンプの
> マージ自体は Release も tag も作成しません。GitHub Release を publish すると
> `.github/workflows/release.yml`(`on: release: published`)が起動し、NuGet
> パッケージとプラットフォーム別バイナリがビルド・publish されます。
> このパケットは **prepare-only** で、ノートを執筆するだけで publish 手順は
> **一切追加しません**。[マージ前のリリース準備ゲート](#リリース準備ゲートg562)と
> [v0.7.0 の publish](#v070-の-publish)を参照してください。

## v0.7.0 の内容

v0.7.0 は `v0.6.2` 以降にマージされた 3 スライス — **G559**、**G560**、**G561** —
を正確にカバーします。

**なぜ patch ではなく minor か。** 文書化されたポリシーは、新しいコマンドサーフェスに
対して minor バンプを予約しています。G559 がまさにそれです。
`intent-cli skill list | install | diff` は既存グループの拡張ではなく、新しい
トップレベルのコマンドグループです。これだけで決まります — G560 と G561 は単独なら
それぞれ patch でした。削除も改名も無いため major ではなく minor です。v0.6.x の
コマンド・引数・フラグはすべて形を保ちます。パッケージ ID は
`JTechJapan.IntentSystem.Cli` のままで、パッケージ ID / ライセンス / workflow
セマンティクスの変更はありません。

見出しは skill サーフェスです。残る 2 つは、それぞれ実際のインシデントを 1 件ずつ
生んだリリースフローと publish 優先順位の機構の穴を塞ぎます。

### クロスプラットフォーム agent skill を 1 コマンドで install(G559)

Claude Code / Codex / Copilot はいずれも**同じ** `SKILL.md` フォーマットを読みます。
異なるのは**設置場所だけ**です。だからこそ手でコピーされ、手コピーした skill は
drift します。このプロジェクト自身の host が、`host-review-loop` skill を
`~/.claude/skills` と `~/.codex/skills` にすでに乖離した 2 つのコピーとして
抱えていました。同じ skill を名乗る 2 ファイルがあり、どちらも権威でない状態は、
skill が無いことより悪い状態です。agent は古い方に従い、ツールがもう実行しない
workflow を報告します。

そこで skill は**単一ソース**として build 時に tool package へ埋め込まれ、各
プラットフォーム固有の場所へ配置する installer が付きます。

```bash
intent-cli skill list                    # 全 target/scope とその状態
intent-cli skill install --target all    # すべてへ一括 install
intent-cli skill install --target claude --scope user
intent-cli skill diff --target claude    # 編集済みコピーの差分
```

| Target | Scope | パス |
| --- | --- | --- |
| `claude` | `repo`(既定) / `user` | `<repo>/.claude/skills/intent-cli/SKILL.md`、`~/.claude/skills/intent-cli/SKILL.md` |
| `codex` | `user` | `~/.codex/skills/intent-cli/SKILL.md` |
| `copilot` | `repo` | `<repo>/.github/skills/intent-cli/SKILL.md` |

**どのプラットフォームでも store / marketplace / registry への登録は不要です。**
3 つともディレクトリを読むことで skill を発見します。Claude Code と Codex は
それぞれの skill ディレクトリを、Copilot は利用側リポジトリの `.github/skills/` を
読みます。ファイルを置くこと**が** install であり、登録・申請・承認は一切ありません。
`skill install` がその全工程です。

**skill は dispatcher であって manual ではありません。** workflow を一切再記述せず、
持っているのは *installed guide output wins* という 1 つのルールと、やりたいことを
それに答える `intent-cli guide ...` コマンドへ対応づける表だけです。workflow を
書き写した skill ファイルは、ツールに対して古びていく 2 つ目の source of truth であり、
それこそ一段上のレイヤーで起きる drift 問題です。guide サーフェスは CLI と一緒に
動きますが、そこへのポインタは陳腐化しません。

**install は 3 フェーズで、部分的な結果を決して書きません。** まず全 target/scope の
組み合わせを検証し、次に全 destination を解決して状態を検査し、その後にはじめて
書き込みます。plan の**どこかに** drift した destination が 1 つでもあれば、
ディレクトリ作成も書き込みも一切行わずに実行全体を中止します — つまり
`--target all` が 2 つだけ install して 3 つ目でエラーになることはありません。
書かれるはずだった destination は `skipped-plan-aborted` として報告され、
「計画に含まれていたが意図的に触らなかった」ことが分かります。

さらに 2 つの保護があります。

- **編集済みコピーを黙って置き換えません。** install は installed ファイルを埋め込み
  ソースと比較し、差分があれば `refused-drifted` を報告し、ファイルを 1 バイトも
  変えずに残し、**非ゼロで終了**します(script が検知できるように)。置き換えは
  `--force` による明示的な opt-in です。改行コードの違いは drift 扱いしないため、
  Windows checkout ですべての install が編集済みと報告されることはありません。
- **プラットフォームが定義していない scope は、書かずに拒否します。** `codex` に
  対する `--scope repo` は失敗し、サポートされる scope を明示します。そのプラット
  フォームが決して読まない、それらしいディレクトリへ書くことは、install 成功に見えて
  install していないのと同じです。

### バージョン非依存の current-state ガードと、roll ルールの完成(G560)

v0.6.2 → 0.6.3 の roll — 改訂ルールの 2 回目の実運用 — で child main が再び red に
なりました。ルール自体は機能しました。**ガード**が機能しなかったのです。いくつかの
ドキュメント検査が現在バージョンを値で固定したままだったため、正しい roll を行うと
テストが壊れました。

- **current-state ガードは `eng/version.json` から導出します。** カットするリリースに
  関するすべての assertion は literal ではなく policy を読むようになり、バージョンを
  含む検査は active な readiness セクションにスコープされます。ファイル内の別の場所の
  テキストで偶然満たされることがなくなります — 以前のガードが roll で露見するまで
  通り続けていたのは、まさにそれが原因でした。
- **roll simulation が証明します。** 今回の回帰は「いくつかの assertion が一度間違って
  いた」ことではなく、current-state ガードが**毎回の roll で**反転することです。
  したがって証明も roll そのもので行います。一時的にバンプした `eng/version.json` を
  実際の policy reader で読み、current-state assertion と**同じ**ヘルパーで検査します。
  literal を取り戻したガードは、今日のドキュメントに対しては通っていてもここで落ちます。
- **version-flow の例はプレースホルダー化しました。** `<stableVersion>` /
  `<nextVersion>` / `<nextPatch>` は roll のたびに書き換える対象ではありません。
  それが変換の狙いです。
- **roll ルールは 6 ステップになりました。** 同一コミットの DRAFT スタブと roll 後の
  green CI 確認に加え、両言語ミラーでの readiness セクション更新が入りました。
  policy をバンプしても readiness セクションが前のラインを説明したままの roll は
  完了していません。

*(本リリースはそのガードの最初の実地証明です。0.6.3 から 0.7.0 への retarget で
current-state 検査は 1 つも反転しませんでした。)*

### publish 優先順位に canonical な exit を、`clarify open` を draft でも動くように(G561)

同一インシデントで 2 つの機構の穴が表面化し、いずれも 1 つの unit を動かすために
one-off の design ruling を必要としていました。

**publish 前の block には canonical な出口がありませんでした。** publish 優先順位は、
未 publish の unit を block して selector にスキップさせ、優先 unit を先に流すことで
機能します。しかし two-sided な unblock は何かに触れる前に完全な `linked_issue` を
要求します(GitHub の blocked label も収束させるため、これは正しい設計です)。
未 publish の unit にはそれがありません。素の queue transition は state だけ動かして
`blocked_by` を残し、selector はそれを依然 blocked と見なします。

```bash
intent-cli automation issue-block <execution-unit> --clear --pre-publish --write
```

は queue 側のみを収束させ(`state=queued` と `blocked_by` の空化を 1 回の guarded write
で行い、run-log event に解除した wait reason を記録します)、GitHub とは一切やり取り
しません。触る issue が存在しないからです。unit に `linked_issue` がある場合は fail
closed します(ルールは完全な不在であり、空の `{repo: "", number: null}` オブジェクトも
拒否します。オブジェクトの存在自体が「何かが linkage を記録した」証跡だからです)。
`--repo`/`--issue` も、検証も実行もできないため拒否します。`--clear` が必須です —
これは exit であって block する手段ではありません。

**`clarify open` は scaffold 直後の packet をすべて拒否していました。** `packet.yaml` を
projection の完全な契約でデシリアライズしていたためで、`packet draft` が作る packet は
それを満たしません。その結果、blocking な design question を記録することが、最も価値の
ある瞬間 — packet がまだ draft で、誤った答えをまだ実装していない段階 — に不可能でした。
現在は clarification レコードが含む事実だけを読みます。identity の検査は決して緩めず、
packet の execution unit は依然必須で queue item と一致しなければなりません。また
`review_context_packet` セクションを**宣言している** packet は「完全である」と主張して
いるので、従来どおり変更されていない strict serializer を通ります(必須フィールド、
メッセージ、失敗の仕方は同一)。許容が適用されるのは、完全性を主張したことがない
packet だけです。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.0
```

または self-contained バイナリを
[v0.7.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.7.0)
からダウンロードしてください。使用前に `.sha256` サイドカーを検証してください。

## v0.6.2 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.7.0
```

本リリースは **CLI サーフェスに対しては追加のみ、automation とリリースフローに
対しては是正的**です。削除・改名されたコマンド / 引数 / フラグはありません。

- **追加のみ — 新しい `skill` コマンドグループ。** 以前動いていたものの挙動は変わらず、
  単にグループが 1 つ増えます。アップグレード後に一度
  `intent-cli skill install --target all` を実行して dispatcher skill を各プラット
  フォーム固有の場所へ配置し、`intent-cli skill list` で確認してください。今後の
  アップグレードで新しい埋め込み skill を取り込むには `skill install` を再実行します。
  `skill diff` は編集済みコピーの差分を示し、置き換えには `--force` が必要です。
- **是正的 — automation サーフェス。** `automation issue-block` に
  `--clear --pre-publish` が加わります。既存の two-sided な block/unblock 経路は
  不変です。`clarify open` は従来拒否していた packet で成功するようになりました。
  従来受け入れていた packet の検証は以前とまったく同じです。
- **是正的 — リリースフローのみ。** G560 はリポジトリ自身のドキュメントガードの
  検査方法を変更し、リリース後の roll ルールを完成させます。影響するのはリリースを
  カットするメンテナであり、CLI の利用者ではありません。

パッケージ ID / ライセンス / CLI の引数・フラグ形状の変更はありません。

## リリース準備ゲート(G562)

以下は **`v0.7.0` の GitHub Release を publish する前**に満たされている必要があります。
このゲートは fail closed です — 1 つでも未達なら、まだ Release を publish しないで
ください。

- [ ] リリース対象のパケットがすべて**完了し、その PR が `main` にマージ済み**である:
      G559(PR #1224)、G560(PR #1222)、G561(PR #1226)、および本 G562 release-prep。
      確認は host/review 側で host queue-state / GitHub PR state から行ってください —
      child implementation loop は parent queue-state を読んではならないため、これは
      host 側の前提条件です。
- [ ] **`v0.6.3` のノートが残っていない。** `0.6.3` は決してカットされないバージョン
      です。その DRAFT スタブは本パケットで削除され、古いノートファイルが保留中の
      リリースと誤認される余地をなくします。
- [ ] 本リリース向けの open な intent-system PR / WIP パケットが取りこぼされていない
      (publish 前に host queue / open PR リストを確認)。
- [ ] `eng/version.json` が `stableVersion` `0.6.2`、`nextVersion` `0.7.0`
      (意図したリリースバージョン)を示している。
- [ ] パッケージメタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs のリンクが解決する、
      公式サービスサイト `https://www.intent-driven-development.com/` が README から
      リンクされている。
- [ ] リリースコミット上で **main CI が green**(`Build and test (source contract)`)
      であり、**preview-pack** workflow も green である。
- [ ] マージコミット上の**マージ後 build + pack エビデンス**が PR に記録されている
      (G528/G538/G551/G554/G558 の準備ゲートと同様)。

## v0.7.0 の publish

本パケットはリリースを publish せず、publish 手順を**一切追加しません**。このノートの
マージ自体は GitHub Release も tag も作成しません。

1. 本パケットがマージされ、上記の準備ゲートが満たされた後、**メンテナ/オペレーター
   (または外部のリリース automation)が `v0.7.0` の GitHub Release を作成・publish
   します**(リリースコミットに tag を付与)。これはマージ後の host/オペレーター/外部の
   アクションです。
2. その GitHub Release の publish が `.github/workflows/release.yml`
   (`on: release: published`)を起動し、NuGet パッケージとプラットフォーム別バイナリ
   アーカイブ(`.sha256` チェックサム付き)をビルドして、トリガーとなった Release に
   添付します。

リリース後の検証(GitHub Release publish 後、`release.yml` 実行後):

- [ ] NuGet.org のパッケージページのリンクがすべて正しく解決する。
- [ ] GitHub リリースアセットのリンク(`.tar.gz`、`.zip`、`.exe`、`.nupkg`)が
      アクセス可能である。
- [ ] `.sha256` チェックサムがダウンロードした成果物と一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli`(または
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.0`)の後、
      `intent-cli --version` が `0.7.0` を報告する。
- [ ] バイナリ成果物のスモークチェック: プラットフォームアーカイブをダウンロードし、
      `.sha256` を検証し、展開して `./intent-cli --version` → `0.7.0`。
- [ ] **skill スモーク**(G559): `intent-cli skill list` が `intent-cli` skill と全
      target/scope を表示し、`intent-cli skill install --target all` が各プラット
      フォーム固有の場所に `SKILL.md` を配置する。
- [ ] **今すぐ `eng/version.json` を ROLL する**。G554 のルール(G557 による改訂、
      G560 による完成)に従い、`stableVersion → 0.7.0`、`nextVersion → 0.7.1` を、
      **新しい DRAFT `release-notes-v0.7.1.md` スタブ(EN/JA)と同じ commit で**行い、
      **「次リリース準備」セクションを両言語ミラーで新しいラインへ更新**した上で、
      **child main CI が green であることを確認**してから roll 完了とみなします。
      [バージョンフロー](09-developer-reference.md#バージョンフロー)を参照。
- [ ] `v0.7.0` の publish **と**検証が完了したことをオペレーターと下流の利用者に通知する
      (publish 依頼自体は上記のリリース前フェーズに属します。この時点では Release は
      すでに publish 済みです)。
