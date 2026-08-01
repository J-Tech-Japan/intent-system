# リリースノート — intent-cli v0.7.2

> **リリースモデル:** `v0.7.2` の **GitHub Release を作成・publish するのは
> メンテナ/オペレーター(または外部のリリース automation)**です。バージョンバンプの
> マージ自体は Release も tag も作成しません。GitHub Release を publish すると
> `.github/workflows/release.yml`(`on: release: published`)が起動し、NuGet
> パッケージとプラットフォーム別バイナリがビルド・publish されます。
> このパケットは **prepare-only** で、ノートを執筆するだけで publish 手順は
> **一切追加しません**。[マージ前のリリース準備ゲート](#リリース準備ゲートg572)と
> [v0.7.2 の publish](#v071-の-publish)を参照してください。

## v0.7.2 の内容

v0.7.2 は `v0.7.1` 以降にマージされた 5 スライス — **G565**、**G566**、**G567**、
**G568**、**G569** — を正確にカバーします。

**なぜ minor ではなく patch か。** 文書化されたポリシーは、新しいコマンドサーフェスに
対して minor バンプを予約しており、本リリースは**実際に 1 つ追加しています** — G568 の
`intent-cli automation queue-dependency-reconcile` です。それでも patch である理由は
「存在しないから」ではなく、「それが何であるか」にあります。

これは**バグ修正を完結させる、範囲の限定された修復ユーティリティ**であって、恒常的な
workflow 能力ではありません。同じスライスの修正によって今後は生成されなくなる queue
item を是正するためだけに存在し、どの workflow フェーズにも参加せず、自動実行もされず、
過去分を reconcile し終えればそれ以上の役割を持ちません。バグが既に書き込んでしまった
データの修復経路は、そのバグ修正の一部です。minor の予約は「workflow が継続的に新たに
*できるようになる*こと」を追加するサーフェスに対するものであり、これは workflow に何も
追加しません。

それ以外はすべてバグ修正または決定性の修正であり、削除・改名されたコマンド / 引数 /
フラグもありません。

本バッチには 3 つのテーマが通っています。

### packet の受理サーフェスを 1 つのパーサへ(G565 / G567)

packet は「どこでも valid」か「どこでも fail-closed で拒否」のどちらかになりました。
本リリース以前、ツールチェーンは `packet.yaml` が何であるかについて自分自身と食い違って
おり、その食い違いは実際に噛みつくまで不可視でした。

**projection が YAML の「近似」をやめました(G565)。** sekiban-as-a-service の design
thread からの field report(2026-07-31、v0.6.2)では、`intent-cli clarify open SKS-G837`
が既存の妥当な packet を "Projection packet YAML contains invalid section header" で拒否
しました。原因はタイトルに含まれる em-dash と長い句読点です。報告者の診断は正確でした —
2 つのパーサが食い違っており、本物の YAML を読んでいたのは packet サーフェス側でした。

欠陥はそのタイトルではありません。`ProjectionPacketSerializer` は手書きの行リーダーで
あり、**想定しなかった合法な YAML 構文はすべて** projection 側だけの失敗になっていま
した(先行スライスが block sequence のインデントを、別のスライスが必須セクション拒否を
既に個別対処済み。放置すれば 1 構文ずつ続いたはずです)。現在 projection は
`packet draft` / `clarify open` / facet check が既に使っている同じ YAML 実装で
`packet.yaml` を読みます。projection の*契約*は不変です(必須セクション・フィールド、
検証順序、メッセージはすべて同一)。動いたのは「何が valid な YAML か」だけで、しかも
ツールチェーンの他が既に出していた答えへ動きました。**報告された失敗は恒久的に修正
され**、報告チームへの約束を果たしています。

**queue-seed も同じパーサへ(G567)。** `automation queue-seed-from-packet` は文書を
一度も解析しない正規表現スカラーリーダーで分類・seed していたため、schema と projection
の両方が拒否する packet でも `queue-seed-ready` と分類され、壊れた unit がキューに入り
得ました。その失敗は publish や preflight の時点で、原因から遠く離れて表面化します。
現在は同じ全文書解析を通して検証し、壊れた YAML は **dry-run と `--write` の両方で
fail closed** します。パースエラーを明示し、非ゼロ終了し、`queue-state.json` も
`runs.jsonl` も変更を計画すらしません。

### 依存関係が忠実に seed される(G568)

queue seeding は flow シーケンス(`dependencies: [G1, G2]`)を生の bracket テキストとして
保持し、**block** シーケンス(`- G1`)は**まったく記録していません**でした。これは些細な
話ではありません。依存関係を考慮した選択は seed されたリストを読むため、依存が落ちると
dependent unit は root が未完了のまま publish-ready に見えます — 順序ルールがまさに
防ぐために存在する失敗が、最上流のサーフェスで静かに起きていたわけです。

現在は両方の記法が同一の構造化リストを生成し、順序制御は再び宣言された root で機能し
ます。欠落したまま seed 済みの item のために(そして `queue-state.json` の手編集は禁止
されているため)canonical な修復経路を用意しました。

```bash
intent-cli automation queue-dependency-reconcile                        # 診断のみ(read-only)
intent-cli automation queue-dependency-reconcile --execution-unit G540  # 1 件だけ診断
intent-cli automation queue-dependency-reconcile --write                # 修復
```

**マージではなく packet からの再導出**です(マージすると queue が「どの packet も覆せない
第 2 の真実」になります)。冪等で、`dependencies` フィールドのみを変更し、キューの
no-item-loss 不変条件を保ち、未知の unit や読めない packet には fail closed します —
「宣言が読めない」が「宣言が空」になってはならず、それは落ちた依存を"確定した不在"へ
修理してしまうからです。自動実行は決してされません。

### 信頼できる CI エビデンス(G566 / G569)

どちらも CLI の挙動を変えず、どちらも同じものを守ります。review と merge のゲートが
canonical として扱う、exact-head の「CI green」というエビデンスです。G566 はテスト
コードのみ、G569 は production の seam にも手を入れますが、実行時の挙動は意図的に
不変です(後述)。

**ランダムな red は red より悪い(G569)。** フルスイートが 1 回失敗し、2 回の再実行と
単独実行は成功 — インターリーブ競合の兆候です。`IssuePrepareCommand.TimestampFactory`
は process-global な可変 static で、共通の non-parallel collection を持たない 2 つの
テストクラスが代入していたため、あるテストが別のテストの clock を読み得ました。この
static は削除され、clock は呼び出しごとの引数になり、production 経路は
`DateTimeOffset.UtcNow` を渡します(削除した static の既定値と同一)。引数は競合し得ま
せん。同じパターンをスイート全体で監査した結果、non-parallel collection なしに共有
static を代入していたクラスがさらに 11 件見つかり、すべて修正済みです。複数クラスが
代入する残りの static もすべて disposition を記載しています。

**roll simulation が自分の fixture を壊し得た(G566)。** G560 の roll-simulation ヘルパー
は readiness 見出しを先に書き換え、素のバージョン置換を最後に適用していたため、live の
`stableVersion` が fixture の `nextVersion` と一致すると、直前に書いた見出しを最後の
パスが書き戻していました。0.7.2 の roll がこれに最初に衝突しました。現在は見出しを
どの置換も到達できないスロットへ退避して最後に書き戻し、衝突ケースも fixture 化した
ので、インスタンスではなく**欠陥のクラス**が閉じています。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.2
```

または self-contained バイナリを
[v0.7.2 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.7.2)
からダウンロードしてください。使用前に `.sha256` サイドカーを検証してください。

## v0.7.1 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.7.2
```

本リリースは**是正的**です。削除・改名されたコマンド / 引数 / フラグはなく、packet
schema の変更もありません。

**是正的な挙動変更 — これまで黙って通っていたものが fail closed になります。** 既存の
実行結果を変え得るのはこれらだけで、いずれも新しい結果は「他のサーフェスが既に出して
いた答え」です。

- **壊れた `packet.yaml` はキューを seed しなくなります。** `queue-seed-from-packet` は
  以前、解析不能な packet の一部を `queue-seed-ready` と分類していました。現在はパース
  エラーを報告し、何も変更せずに非ゼロ終了します。これまで"成功"していた seed が止まる
  なら、その packet は元から壊れており、失敗が「読める場所」へ移動しただけです。
- **projection はより多くを受理し、拒否の仕方が変わります。** projection だけが拒否して
  いた packet(em-dash、引用符内のコロン、列 0 のコメント、flow シーケンス、folded
  スカラー)は解析できるようになりました。本当に壊れた YAML は引き続き拒否され、
  section header に関する推測ではなく**パース失敗**として報告されます。
- **block 記法の `dependencies` がキューに届くようになります。** 依存が黙って落ちていた
  unit は、root が完了するまで正しく保留されるようになる場合があります。これは意図した
  ゲートが機能している状態です。seed 済みの item は
  `automation queue-dependency-reconcile` で整合させてください。

**CLI 利用者から見える差のない内部変更。** G566 はテストコードのみです。G569 は内部
およびテスト決定性の **seam** 変更で、production ソース(`IssuePrepareCommand`、および
`TaskingPublishReviewedBridgeCommand` の doc コメント)に手を入れ、process-global な
clock を呼び出しごとの clock に置き換えています。production 経路が「削除した static の
既定値と同一のもの」を渡すため、実行時の挙動は意図的に byte 単位で不変です。G568 の
パーサ配線は、既に正しく seed できていた packet に対して byte 互換です。

## リリース準備ゲート(G572)

以下は **`v0.7.2` の GitHub Release を publish する前**に満たされている必要があります。
このゲートは fail closed です — 1 つでも未達なら、まだ Release を publish しないで
ください。

- [ ] リリース対象のパケットがすべて**完了し、その PR が `main` にマージ済み**である:
      G565(PR #1236)、G566(PR #1234)、G567(PR #1238)、G568(PR #1240)、
      G569(PR #1242)、および G572 release-prep(PR #1244)。
      確認は host/review 側で host queue-state / GitHub PR state から行ってください —
      child implementation loop は parent queue-state を読んではならないため、これは
      host 側の前提条件です。
- [ ] **どちらの `release-notes-v0.7.2.md` にも DRAFT スタブが残っていない。** 未記入の
      スタブは release-prep が未実行であることを意味します。本パケットが両方を置換します。
- [ ] 本リリース向けの open な intent-system PR / WIP パケットが取りこぼされていない
      (publish 前に host queue / open PR リストを確認)。
- [ ] `eng/version.json` が `stableVersion` `0.7.0`、`nextVersion` `0.7.2`
      (意図したリリースバージョン)を示している — **本パケットでは変更しません**。
- [ ] パッケージメタデータが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      `RepositoryUrl` / `PackageProjectUrl` が
      `https://github.com/J-Tech-Japan/intent-system` を指す、
      `PackageLicenseExpression = Apache-2.0`、README/docs のリンクが解決する、
      公式サービスサイト `https://www.intent-driven-development.com/` が README から
      リンクされている。
- [ ] リリースコミット上で **main CI が green**(`Build and test (source contract)`)
      であり、**preview-pack** workflow も green である。
- [ ] マージコミット上の**マージ後 build + pack エビデンス**が PR に記録されている
      (G528/G538/G551/G554/G558/G562 の準備ゲートと同様)。

## v0.7.2 の publish

本パケットはリリースを publish せず、publish 手順を**一切追加しません**。このノートの
マージ自体は GitHub Release も tag も作成しません。

**これは silent release です**: 外部への告知・プロモーションは行いません。これは
プロモーションにのみ影響します。上記のノートは他のリリースと同じ基準で執筆されており、
以下の publish 手順も変わりません。

1. 本パケットがマージされ、上記の準備ゲートが満たされた後、**メンテナ/オペレーター
   (または外部のリリース automation)が `v0.7.2` の GitHub Release を作成・publish
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
      `dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.7.2`)後、
      `intent-cli --version` が `0.7.2` を報告する。
- [ ] バイナリ成果物のスモークチェック: プラットフォームアーカイブをダウンロードし、
      `.sha256` を検証し、展開して `./intent-cli --version` → `0.7.2`。
- [ ] **パーサ統一のスモーク**(G565/G567): `issue_title` に em-dash と引用符付きの
      `": "` を含む packet が `intent-cli clarify open` に受理され、
      `intent-cli automation queue-seed-from-packet` が意図的に壊した packet を
      パースエラー明示＋非ゼロ終了で拒否する。
- [ ] **依存忠実性のスモーク**(G568): `intent-cli automation
      queue-dependency-reconcile --help` が usage を表示する。
- [ ] **今すぐ `eng/version.json` を ROLL する**。G554 のルール(G557 による改訂、
      G560 による完成)に従い、`stableVersion → 0.7.2`、`nextVersion → 0.7.2` を、
      **新しい DRAFT `release-notes-v0.7.2.md` スタブ(EN/JA)と同じ commit で**行い、
      **「次リリース準備」セクションを両言語ミラーで新しいラインへ更新**した上で、
      **child main CI が green であることを確認**してから roll 完了とみなします。
      [バージョンフロー](09-developer-reference.md#バージョンフロー)を参照。
