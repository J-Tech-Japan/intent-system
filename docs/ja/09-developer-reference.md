# 開発者リファレンス

> 日本語版。English version: [`../en/09-developer-reference.md`](../en/09-developer-reference.md)

このページはインストールオプション、パッケージ化された実行によるスモークテスト、
preview チャンネル、バージョンポリシーについて説明します。
メンテナー、コントリビューター、パワーユーザー向けです。
[クイックスタート](../../README.md#quickstart) に従う初心者向けではありません。

---

## .NET SDK なしでインストール

各 [GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest) には
SDK フリーの自己完結型バイナリが添付されています（.NET ランタイムが同梱されており、
SDK は不要です）。

| Platform | Asset |
| --- | --- |
| macOS (Apple Silicon) | `intent-cli-<version>-osx-arm64.tar.gz` |
| Windows (x64) | `intent-cli-<version>-win-x64.zip` |
| Linux (x64) | `intent-cli-<version>-linux-x64.tar.gz` |

各アーカイブには `.sha256` サイドカーが同梱されています。
使用前に両ファイルを同じディレクトリにダウンロードして確認してください。

**macOS:**

```bash
# 1. 確認（両ファイルがあるフォルダで実行）。
shasum -a 256 -c intent-cli-<version>-osx-arm64.tar.gz.sha256

# 2. 展開して PATH に配置。
tar -xzf intent-cli-<version>-osx-arm64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. 確認。
intent-cli --version
```

**Linux:**

```bash
# 1. 確認。
sha256sum -c intent-cli-<version>-linux-x64.tar.gz.sha256

# 2. 展開して PATH に配置。
tar -xzf intent-cli-<version>-linux-x64.tar.gz
chmod +x intent-cli
sudo mv intent-cli /usr/local/bin/

# 3. 確認。
intent-cli --version
```

**Windows:** `intent-cli-<version>-win-x64.zip` と `.sha256` サイドカーをダウンロード。
`CertUtil -hashfile intent-cli-<version>-win-x64.zip SHA256` のハッシュを
`.sha256` ファイルの最初のフィールドと比較し、解凍後 `intent-cli.exe` を PATH に配置してください。

リリースバイナリと OSS preview CI アーティファクトにはビルド時の有効期限はありません。

### 日本語 / 非 UTF-8 の Windows コンソール (G484)

intent-cli は GitHub CLI（`gh`）サブプロセスの出力を、**周囲のコンソールのコードページに
依存せず UTF-8 として** 読み取ります。そのため日本語 Windows コンソール（cp932/932）でも
issue/PR のタイトルや本文が valid な JSON のまま保たれます。`worker next-action`,
`worker issue-preflight`, `worker pr-comment-preflight`, および host/review preflight の各経路が
このデコードを共有します。`chcp 65001` の実行や `$OutputEncoding` /
`[Console]::OutputEncoding` の手動設定は **不要** です。macOS/Linux の挙動は変わりません
（これらのコンソールは既に UTF-8 です）。

---

## パッケージ化された実行（ローカルスモークテスト）

CLI は .NET ツールとしてパッケージ化されています（パッケージ id `JTechJapan.IntentSystem.Cli`、
コマンド `intent-cli`）。ローカルビルドパッケージのスモークテスト:

```bash
export INTENT_CLI_LOCAL_VERSION="0.3.2-local.$(date -u +%Y%m%d%H%M%S)"
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj \
  -p:Version="$INTENT_CLI_LOCAL_VERSION" \
  -o .artifacts/packages
mkdir -p .artifacts/smoke-repo/.intent-cli
cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'
default_domain = "intent-cli"
artifact_root = ".intent-cli"
worktree_root = ".intent-cli/worktrees"
EOF
(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" JTechJapan.IntentSystem.Cli project status)
```

等価な `dnx` パス:

```bash
(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version "$INTENT_CLI_LOCAL_VERSION" JTechJapan.IntentSystem.Cli project status)
```

---

## Preview インストール

> OSS preview チャンネル。公開ユーザーは安定版 NuGet
> (`dotnet tool install -g JTechJapan.IntentSystem.Cli`) または上記のリリースバイナリを
> 使用してください。このセクションは stable リリース前の最新変更が必要なユーザー向けです。

`preview-pack` GitHub Actions ワークフローは `main` へのマージごとに実行され、
ワークフローアーティファクトとして `intent-cli-preview-<version>` という名前の
自己完結型インストールバンドルをアップロードします。

パッケージバージョンパターン: `<nextVersion>-preview.<run_number>.<run_attempt>`
（例: `0.3.1-preview.42.1`）。

```bash
# 1. ワークフローアーティファクトをダウンロードして解凍、そのディレクトリに cd。
cd ./intent-cli-preview-0.3.1-preview.42.1

# 2. チェックサムを確認（macOS: shasum; Linux: sha256sum）。
shasum -a 256 -c JTechJapan.IntentSystem.Cli.*.nupkg.sha256

# 3. .NET ツールをこのローカルフォルダからインストール（または更新）:
dotnet tool install --global --add-source . \
  --version 0.3.1-preview.42.1 JTechJapan.IntentSystem.Cli
# アップグレード:
dotnet tool update --global --add-source . \
  --version 0.3.1-preview.42.1 JTechJapan.IntentSystem.Cli

# アンインストール:
dotnet tool uninstall --global JTechJapan.IntentSystem.Cli
```

インストール済みバイナリは `intent-cli --version` で preview メタデータを表示します:

```text
intent-cli 0.3.1-preview.42.1-<short-sha>-G<unit>
channel=preview built=<iso-utc> commit=<full-sha>
```

**OSS preview パッケージには有効期限はなく、無期限で実行可能です。**

---

## same-repo メタデータトポロジ (G485)

same-repo トポロジは **コードブランチ** と **メタデータブランチ** を 1 つの GitHub
リポジトリに同居させる構成です（例: コードは `main`、メタデータ（`.intent-cli/` の
queue-state・runs・packets・`intents/<domain>/`）は `main-metadata`）。
`.intent-cli/config.toml` の `[project]` で設定します:

```toml
[project]
domain = "estivo"
artifact_root = ".intent-cli"
same_repo_topology = true
metadata_source_branch = "main-metadata"   # host loop がメタデータを READ するブランチ
metadata_write_branch  = "main-metadata"   # host loop がメタデータを WRITE するブランチ
```

これらのキーがそのまま `intent-cli automation same-repo-metadata-preflight` と
`intent-cli automation summary` に読み取られます。`same-repo-metadata-preflight` が
`not-configured` を返す場合、上記キーが解決されていません。`[project]`（別テーブルでない）
配下にあること、`metadata_source_branch` / `metadata_write_branch` の綴りが正確であることを
確認してください。

host と child の bootstrap（G514）: host 側 automation コマンド（`automation summary`、
`automation same-repo-metadata-preflight`、`automation queue-seed-from-packet`）は解決された
repo root の `.intent-cli/config.toml` をロードするため、他の host コマンドと同じ effective な
`[project]` 設定（同じ same-repo トポロジ設定）を参照します。`.intent-cli/config.toml` を **持たない**
child/standalone 実装 repo は安全なデフォルト bootstrap 挙動を保ちます（parent metadata 不要）。
same-repo host repo で host コマンドを実行してもデフォルト挙動になる場合、コマンドが repo 内から
実行されている（resolver は `.intent-cli/` ディレクトリまで上に辿る）こと、config ファイルが
存在することを確認してください。

packet の正規の publish 経路は **`automation queue-seed-from-packet` →
`issue publish-flow` → `automation issue-publish`** で、手動の queue-state 編集や raw
`gh issue create` は不要です。ドメインの `execution_unit_regex`（
`intents/<domain>/automation/bindings.md` に宣言、例 `^E\d{3,}$`）は単一の共有ソースから
解決されるため、`automation summary --domain <d>` と
`queue-seed-from-packet --execution-unit <unit>` がどの unit を有効とみなすか常に一致します。
アクティブなドメインの regex に一致しない unit は、参照した bindings ソースを明示する精密な
診断とともに拒否されます。

### execution-unit を解決するサーフェスの domain 解決順序 (G522)

`--pr` や `--execution-unit` から execution unit を解決するサーフェス
（`review closeout-plan`、`automation queue-seed-from-packet`、
`automation publish-recovery`、および同じ lookup を使う peer サーフェス）は、
`--domain` が省略された場合に次の解決順序を適用します:

1. 明示的な `--domain` が優先される — 解決された packet 自身の `domain:`
   スカラーが宣言する値と矛盾する場合はエラーになる。
2. それ以外の場合、解決された packet.yaml / queue metadata が宣言する
   domain を使用する。
3. それ以外の場合、サーフェスは fail loud する — `intents/*/` から
   スキャンした候補 domain と、正確な `--domain` 再実行コマンドを示す。
   ホストのデフォルト domain binding（`.intent-cli/config.toml` の
   `[project] domain`）へ黙って fallback することは決してない。

これは multi-domain host での既知のギャップを解消します: 従来の default
binding fallback は、packet 自身の `domain:` フィールドが別の値を宣言して
いても、間違った domain に対して報告・検証してしまうことがありました
（例: `review closeout-plan --pr <n>` が、解決された packet の実際の domain
ではなくホストの default domain を報告してしまう、あるいは
`queue-seed-from-packet` が間違った domain の `execution_unit_regex`
チェックを実行してしまう、など）。default binding の仕組み自体は変更
されておらず他の箇所では引き続き使われます。変わったのは、これらの
サーフェスが `--domain` 省略時に何を参照するかだけです。

3つのサーフェスすべてがこの順序を厳密に適用します — domain を導出できない
場合に `[project] domain` へ fallback することはありません:

- `automation queue-seed-from-packet` — `--domain` と packet の `domain:`
  フィールドのどちらも無い場合、seed を拒否します。
- `review closeout-plan` — 解決された queue item に対して domain を
  導出できない場合（一致する queue item が無い、またはその packet.yaml に
  `domain:` フィールドが無い場合）、ホストの default domain binding を
  報告する代わりに、候補 domain と正確な `--domain` 再実行コマンドを示して
  fail loud します。
- `automation publish-recovery` は、各 execution unit の候補が repair 解析に
  参加する前に、必ず domain を解決します — `--domain` が指定されていれば
  それを使用し（その候補自身が宣言する packet-declared domain と矛盾する
  場合は候補ごとにエラーになります）、指定が無ければその候補自身の
  packet-declared domain から導出します。どちらも無い候補は、スキャンに
  黙って参加する（あるいは黙って除外される）のではなく、構造化された
  `domain-underivable` の unsafe stop になります。明示的な `--domain` と
  矛盾する候補は構造化された `domain-contradiction` の unsafe stop に
  なります。これは `--pr` でスコープされたパスと、スコープなしの broad
  scan の両方に適用されます。`--domain` を完全に省略した場合は
  cross-candidate なスコープを要求したことにはならないため、
  （個別に導出可能な）異なる domain を持つ複数の候補が 1 回の broad-scan
  結果に共存することがあります。
- `automation stalled-work`（G532）もこの順序を適用します — 詳細は後述。
  ただし、candidate の execution unit 自体が実在する packet/queue の
  linkage によって裏付けられている場合に限ります。`stalled-work` では
  `--domain` が必須引数のため、裏付けが取れている candidate については、
  その linkage が domain について沈黙していても明示的な domain が常に
  代わりに使えます。しかし `--domain` は scan の範囲を指定するものであり、
  それ単体で身元不明の candidate をそのメンバーだと認定するものではない
  ため、裏付けが取れない candidate は引き続き除外されます。

### stalled-work 検出 (G523)

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] --format json|markdown`
は、保留中の pipeline transition を age 付きで一覧化する **read-only** な
サーフェスです。これにより、1 回の orchestrator wake（あるいは外部の
heartbeat）だけで、人間が GitHub label・PR state・queue-state を手で
突き合わせることなく stall を検出・復旧できます。GitHub label、
queue-state、`runs.jsonl` を変更することは一切ありません。

カテゴリ:

- `published-not-delegated` — OPEN の issue が `intent-target` を持つが、
  claim label（`intent-issue-in-progress` / `intent-pr-created`）がまだ無く、
  PR も一度も作成されていない。
- `pr-created-not-reviewing` — 元の issue が `intent-pr-created` を持ち、
  その issue を close する PR に `review-start` transition がまだ適用
  されていない（PR に `intent-pr-reviewing` / `intent-pr-approved` が無い）。
- `merged-not-closed-out` — MERGED 状態の PR に紐づく queue-state item が
  まだ `Completed` になっていない（closeout — `pr-merged` +
  `closeout-recorded` の runs event — がまだ記録されていない）。

各 item は `kind`、`execution_unit`、`issue` および/または `pr`
（番号 + url）、`age_minutes`、`recommended_action`（次に実行すべき
正確な canonical コマンド — それぞれ `worker claim`、`automation
pr-transition --transition review-start`、`closeout pr`）を報告します。
`--stale-minutes` は、指定した閾値より新しい item を除外します
（デフォルトは `0` — すべてを age 付きで報告し、閾値は呼び出し側が選ぶ）。
`age_minutes` は、GitHub が label 適用時刻を公開していないため、
該当する GitHub entity の `createdAt`/`updatedAt` タイムスタンプからの
近似値です。`published-not-delegated` は、既に取得済みの PR closing
reference も issue label とは独立にチェックします — そのため、
completion label が実態とずれてしまっていても（intent-pr-created が
一度も付与されていない、または削除されてしまったが、OPEN の PR が
既にその issue を close している場合）、誤って `worker claim` を推奨する
ことはありません。

**execution unit と domain の特定 (G532)**

candidate の execution unit は、issue/PR タイトルの先頭 ID トークン —
`^[A-Z][A-Z0-9]*-G?[0-9]+`（英数字の prefix。例: `SKS-G815`、`Z4R-G3`）
または単純な `^G[0-9]+`（例: `G523`）で、直後に文字・数字が続かない
（右境界必須）— であり、最初のコロンより前すべてではありません。
`"SKS-G815 G812 sub-slice 1: ..."` のようなタイトルは `SKS-G815` に
解決され、コロン前のフレーズ全体にはなりません。`"G12abc: ..."` の
ようなタイトルが `G12` に切り詰められることもありません。この先頭
トークンは、実在する `.intent-cli/issues/<token>/packet.yaml` が
裏付ける場合にのみ信頼されます。先頭トークンが無い、または裏付けが
取れない場合は、`.intent-cli/issues/*/packet.yaml` の各 packet が
宣言する `source_execution_unit`（nested の
`implementation_issue_packet.source_execution_unit` を優先し、bare な
`source_execution_unit` を alias として使用）がタイトル中の独立した
トークンとして現れるかどうかで candidate を照合します。裏付けとして
認められるのは、ちょうど 1 つの packet ファイルが一致した場合のみで
あり、単に宣言された unit の値が 1 種類であることではありません。
2 つ以上の packet ファイルが一致した場合は、宣言している unit の
文字列がたまたま同じであっても（重複宣言はそれ自体がデータ整合性の
問題であり、値によって 1 つにまとめられることはありません）、
（最長一致を選ぶなどして）推測することなく、ambiguous
（`execution-unit-ambiguous`、一致したすべての candidate パスを明示）
として報告されます。1 つの packet が自身の nested フィールドと
top-level alias の両方で同じ unit を宣言している場合は、それでも
1 ファイルであり、影響を受けません。

この execution-unit 文字列は candidate の
`.intent-cli/issues/<unit>/packet.yaml` を特定するためだけに使われます
— domain 所属の判定そのものには使いません。domain は、その packet の
nested `implementation_issue_packet.domain` フィールドを最初に読み、
それが無い場合のみ top-level `domain:` フィールドを互換 alias として
使用します。

`merged-not-closed-out` では、execution unit とその裏付けとなる
linkage はタイトルではなく queue-state から得られます: merged PR
自身の PR 番号を queue item の `linked_pr` と照合しますが、その
bare な番号一致だけでは、shared/multi-repo な queue-state に対する
裏付けとして十分ではありません（無関係な repo にたまたま同じ番号の
PR が存在する可能性があるため）。queue item 自身が宣言する
`linked_issue`（repo + number）が、scan 対象の repo について
merged PR 自身が GitHub 上で報告する closing-issue reference の
いずれかと一致することも追加で必要です — `linked_issue` が無い、
repo が違う、対応する issue が無い場合は、単なる仮定ではなく
`excluded[]` へ fail-closed します。同じ merged PR を参照する
ACTIVE（非 Completed）な queue item は、まず全件を収集します —
必要なのはちょうど 1 件のみで、2 件以上（同じ repo+issue に
collapse するが execution unit が異なる場合も、一方だけが
妥当性検証を通る場合も含む）は、JSON の並び順に関わらず ambiguous
（`execution-unit-ambiguous`、試みたすべての queue item の unit・
state・linkage と queue-state のパスを明示）になります。Completed
になった重複が、本当に active な item 1 件と共存している場合は
ambiguous とはみなされません — 権威を争うのは active な item
同士のみです。

**domain の確認は、他の execution-unit を解決するすべてのサーフェスと
同じ G522 の順序（`--domain` > packet-declared domain > fail-loud）を
適用します。** ただし、candidate の execution unit 自体が上記の
実在する packet/queue の linkage によって裏付けられている場合に
限ります。そのような candidate について、
`stalled-work` では `--domain` が必須引数のため、その linkage が
domain について沈黙していても常に明示的な `--domain` が代わりに
使えます — candidate が `items[]` から除外されるのは、`--domain` と、
実際に別の domain を宣言している packet とが本当に矛盾する場合のみです。
これは PR #1148 での従来の締め付け — packet-declared domain が
無い/導出できない場合を、明示的な `--domain` があっても常に
fail-closed とする方針、しかも candidate の execution unit 自体が
何によっても裏付けられていない場合も含む — よりも狭い範囲です。
その従来の広い締め付けは、identification のロジック自体が誤っていた
ときに、まさにこのサーフェスが見つけるべき stall を除外してしまい
ました（下流 adopter に対する field finding、2026-07-15 と
2026-07-18。いずれも表面化されずチームの workaround で覆い隠されて
いました）。

execution unit が全く裏付けられない candidate（先頭トークンの
packet.yaml が存在せず、かつどの packet の `source_execution_unit`
もタイトルに一致しない）は、引き続き除外されます
（`domain-underivable`）: 明示的な `--domain` は scan の範囲を
指定するものであり、それ単体で身元不明の candidate をそのメンバーだと
認定するものではないからです。`excluded[]`（`kind`、`execution_unit`、
`issue`/`pr`、`reason`、`detail`）はすべての除外を報告します —
`domain-contradiction`（矛盾している具体的な packet-declared domain と
試みた derivation — nested フィールドと top-level alias のどちらを、
どの packet.yaml パスで確認したか — を明示）、`domain-underivable`
（execution unit が裏付けられなかった場合）、`execution-unit-ambiguous`
（一致したすべての candidate packet パスを明示）のいずれかであり、
常に理由と試みた derivation とともに報告され、黙って消えることは
ありません。

このスライスは検出のみです — orchestrator wake procedure や外部
heartbeat からこのサーフェスを利用する部分は、別の後続スライスです。

### 行き詰まった published issue を retire する (G525)

`intent-cli automation issue-retire --repo <r> --issue <n> --reason <superseded|decomposed|obsolete> [--note <text>] [--domain <name>] [--write]`
は、authored 通りには決して開始できない published な `intent-target` issue
（例: research pass によって slice を decompose する必要があると判明した場合）
のための canonical かつ atomic な transition です。このコマンドが存在する
以前は、このデッドロックからの唯一の逃げ道は、operator が承認する
noncanonical なリカバリ（手動での GitHub close、手動での label 除去、
手書きの queue-state 編集）であり、その後 `metadata validate` はそれを
認識できませんでした。

`--write` は次の順序で実行します:

1. GitHub issue を **not planned** として close し、reason と任意の note
   を記載したコメントを付ける(issue が既に close 済みの場合はスキップ
   される — 下記の partial-failure recovery を参照);
2. issue に付いている `intent-target` およびその他の workflow label を
   すべて除去する;
3. 対応する queue-state item の lifecycle を（reason 付きで）`retired`
   としてマークする — **エントリが存在しなければ新規作成する**。
   publish されたが一度も delegate されていない issue には queue-state
   エントリが無いことが一般的なためです。これにより `metadata validate`
   は queue entry の欠落を報告するのではなく、retired lifecycle を
   認識できるようになります;
4. `runs.jsonl` に `packet-retired` イベントを追記する。

`--write` を付けない場合は、正確な planned mutation を一覧表示する
dry-run になります。次の場合は **fail closed**（一切の変更なし）します:

- 同じ repo の OPEN な PR がその issue を close する — 先にその PR を
  merge・close、またはリリースしてください;
- issue が `intent-issue-in-progress` を持つ — アクティブな claim が
  進行中です。先にそれを解放してください（例:
  `intent-cli worker complete --kind issue --number <n> --outcome
  declined-contract-incomplete --write`）。
- マッチした queue item が既に `Completed`(merge/完了済みの作業)である
  — retire は authored 通りには決して完了できない published work にのみ
  適用されます。この refusal は GitHub にも local state にも一切触れません;
- 解決された domain が導出不能、または明示的な `--domain` と矛盾する
  (下記の domain resolution を参照)。

**Partial-failure recovery**: 対象 issue は OPEN issue の一覧スキャンでは
なく、open/closed を問わない直接の GitHub 参照で解決されます。`--write`
がシーケンスの途中で失敗した場合(issue は close されたが label 除去・
queue-state 書き込み・`runs.jsonl` 追記のいずれかが完了しなかった場合)、
同じコマンドを再実行するだけで issue が再び見つかり、残りのステップが
完了します — 「OPEN issues の中に見つからない」で行き詰まることは
ありません。既に CLOSED な issue に対する recovery は、GitHub 自身の
close reason が **not planned**(このコマンドが close 時に使う reason
そのもの)である場合のみ許可されます — それ以外の reason(例: merge に
よる completed)で close された issue には一切触れません。

**Domain resolution (G522 boundary)**: queue item のマッチングは
`(repo, issue number)` の完全一致を要求します — 別 repo の同番号 issue が
この execution unit にマッチすることはありません。execution unit の
domain は、他の execution-unit-resolving なサーフェスと同じ順序で解決
されます: 明示的な `--domain` が優先されます(解決された packet.yaml が
宣言する domain と矛盾する場合はエラー); それ以外の場合は
packet-declared な `domain:` フィールドが使われます; どちらも無い場合は
candidate domains と正確な `--domain` re-invocation を示して fail loud
します。これは既存の queue item にも、issue タイトルから新規に導出される
item にも適用されます — misleading な title prefix だけでは、packet.yaml
(または operator が明示的に指定した `--domain`)による裏付けなしに queue
を作成することは決してできません。

**冪等**: queue-state エントリが既に `retired` になっている execution
unit に対して再実行しても安全な no-op です — 冪等性の判断根拠は
（不安定な GitHub 状態の再チェックではなく）durable state です。`--write`
を使った際、queue-state は既に retired だが直前の partial write による
`runs.jsonl` イベントが欠落している場合、再実行はその欠落したステップ
だけを(GitHub 呼び出しゼロで)完了させます — 永久に黙って失われることは
ありません。packet ディレクトリと issue のコメント履歴には一切触れず、
削除もしません。

retired になった item は自動的に WIP gating から外れます:
`automation host-review-preflight` の in-flight スキャンは OPEN で
`intent-target` ラベル付きの GitHub issue/PR をライブに読むため、close
されて label が外れた issue は単にそこから消えるだけです — 別途コード
パスは不要です。

---

### facet を意識した context 供給 (G530)

G529 の 4 つの semantic facet（`vocabulary`、`invariant`、`decider`、
`acceptance-property`）を土台に、2 つの read-only サーフェスが、
facet で分類された node を「変更が尊重すべき、局所化された semantic
context」として優先的に供給するようになりました — implement/review
エージェントが手作業でその surface を再構築する代わりに。

**`intent-cli context collect`** は `## Facet context` セクションを
追加で持つようになりました。これは、その下にある未分類の
queue-state/clarification/automation-bindings/recent-events の
context より AHEAD（前）にレンダリングされます（これは
おまけではなく semantic の核だからです）。このセクションは facet
ごとに 1 グループを持ち、常に正規の順序
`vocabulary → invariant → decider → acceptance-property` で並び、
各 node は `id`、`facets`（現在のグループだけでなく、その node の
全 facet 値）、`summary`（frontmatter 後の最初の空でない行）、
`path`（`intents/<domain>/...`）として報告されます:

```bash
intent-cli context collect --domain <d> --format json
intent-cli context collect --domain <d> --facets invariant,decider   # これらの facet だけに絞る
intent-cli context collect --domain <d> --scope intents/<d>/means,identity/mission.md  # overlap で絞り込む
```

- `--facets <カンマ区切り>` は、そもそもどの facet グループが現れるかを
  制限します（それでも正規の順序でレンダリングされます）。認識できない
  facet 名は usage error になります（`intent search --facet` の
  バリデーションと同様）。カンマ区切りのオプションはすべて（`--facets`
  も `--scope` も）、trim 後の各要素が非空であることを要求し、
  先勝ちの順序で重複を除去します — `--scope ","` や `--facets
  "vocabulary,,decider"`（空の要素）は usage error であり、黙って
  「scope なし」になったり、要素が黙って捨てられたりすることは
  ありません。
- `--scope <カンマ区切りのパス>` は、各グループを、パスがヒントと
  overlap する node だけに対称的に（両方向で）絞り込みます — ヒントが
  node の祖先ディレクトリを指す場合に加えて、node 自身のパスが
  （より具体的な）ヒントの祖先である場合も、両方とも overlap と
  みなされます。完全一致も同様です。どのヒントの形式も、比較の前に
  node 自身の id が既に使っている domain-relative なセグメント列に
  正規化されます — そのため、domain root 配下の絶対ファイルシステム
  パス、repo-relative な `intents/<domain>/...` 形式、そして短い
  domain-relative の id 形式（末尾の `.md` の有無を問わず）は、
  すべて等価です。`..` セグメントは拒否されます（黙って解決される
  ことはありません — そうしないと、ヒントが自身の scope 対象で
  あるはずの domain の外へ抜け出してしまう可能性があるためです）。
  比較は常に大文字小文字を区別します。`--scope` を省略した場合は、
  domain の facet node すべてが返されます。`--scope` を渡したが、
  すべてのヒントが無効または domain 外だった場合は、何にもマッチ
  しません — 黙って「scope 要求なし」にフォールバックすることは
  ありません。
- domain に facet-annotated な node が 1 つも無い場合（`--scope`/
  `--facets` のクエリがたまたま何にもマッチしなかっただけの場合とは
  異なります）は、`facet_context_note` が設定され、空のセクションの
  代わりに明示的なノートがレンダリングされます — graceful な
  degradation であり、決して error にはなりません。facets は
  optional であり、tree がまだ採用していない段階ではこれが通常です。
- 壊れた `facets:` 宣言、または Present な宣言に未知の値が含まれる
  場合、それが黙って消えることはありません: どちらも
  `facet_context_warnings` エントリ（`path`、`reason`）を JSON に、
  Markdown では `Warnings` リストを生成し、何が・なぜ除外されたのかを
  正確に示します — これにより「そもそも facets が無い」場合と
  「facets はあったが除外された」場合が、区別できないまま同じに
  見えることがなくなります。未知の値があっても、その node の他の
  有効な facet からその node が除外されることはありません。
- JSON の形: `facet_context: [{facet, nodes: [{id, facets, summary,
  path}]}]`（常に 4 要素。`--facets` を渡した場合はそれより少なくなる
  こともあります）、`facet_context_note: string | null`、
  `facet_context_warnings: [{path, reason}]`。

**`intent-cli packet draft`** は、scaffold される `review-context.md`
の中に `## Facet context` セクションを生成するようになりました。
これは、その packet 自身の
`implementation_issue_packet.intent_references` と overlap する
facet node を一覧化します — `context collect` の `--scope` が使うのと
全く同じ overlap ロジックなので、2 つのサーフェスが「overlap」の
意味について食い違うことはありません。生成される内容は、2 つの
HTML コメントマーカー（`<!-- BEGIN/END GENERATED FACET CONTEXT
(G530) -->`）の間に存在します。`review-context.md` の残りの部分は
手による所有物であり、一切触れられません:

- **ファイルがまだ存在しない場合**: 現在の `intent_references`
  （既に `packet.yaml` が存在していれば、そのディスク上の値を
  読みます — 例えば、以前の `packet draft` 実行の後に operator が
  手で編集していた場合。この同じ呼び出しが別途書き込むかもしれない、
  テンプレートの空の `[]` では決してありません）を使って、ファイル
  全体が新規に書き込まれます。`created` として報告されます。
- **ファイルが存在し、かつ両方のマーカーを持つ場合**: マーカーの
  「間」にある内容だけが、packet の現在の `intent_references` から
  再計算され、置き換えられます — これにより、通常のワークフロー
  （空の references で scaffold → operator が `packet.yaml` に
  本物の references を追加 → `packet draft` を再実行 → block に
  それが反映される）を通して、セクションが最新に保たれます。
  開始マーカーより前、終了マーカーより後のすべて（block の周りに
  手で書かれた文章を含む）は、バイト単位でそのまま保持されます。
  再計算された内容が既存のものと異なる場合は `updated`、異ならない
  場合は `skipped`（本物の no-op であり、見せかけの update では
  ない）として報告されます。
- **ファイルが存在するが、認識できるマーカーが無い場合**（この機能
  より前に作られたか、operator がマーカーを削除した場合）: 他の
  3 つの scaffold ファイルの、単純な「存在すればスキップ」の挙動と
  全く同じように、完全に触れられないままになります。マーカーが
  手による所有物の中に後から注入されることは決してありません。
  `skipped` として報告されます。

空の `intent_references` リストそれ自体が、意味のある scope です —
「この packet は（今のところ）何も参照していない」— そのため block は
すべての facet グループを空として表示します。domain 全体の facet
node を表示することは決してありません。domain に facet node が
1 つも存在しない場合にのみ、graceful-degradation のノートが
レンダリングされます。

両サーフェスは 1 つのセレクター（`FacetContextSelector`）を共有して
スキャン・分類・グループ化・scope-overlap のマッチングを行うため、
順序付け・フィルタリング・warning・degradation のセマンティクスが
両者の間で食い違うことはあり得ません。バケット分けされるのは有効な
facet 値（G529 の閉じた集合）だけです。

---

## バージョンフロー

リポジトリのバージョンポリシーは `eng/version.json` に記載されています。`stableVersion`
（最新の公開済み安定版）と `nextVersion`（準備中 / 開発中のライン）の単一の source of
truth です。G468 以降、ローカル `dotnet pack` のデフォルト `<Version>` はこのファイルから
導出されるため、ローカル pack / install は stale な csproj リテラルではなく開発中の
`nextVersion` を報告します:

```json
{
  "stableVersion": "0.3.15",
  "nextVersion": "0.4.0"
}
```

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `0.4.0-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `0.4.0-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `0.4.0-rc.N` | タグ `v0.4.0-rc.N` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する |
| 安定版リリース | `0.4.0` | タグ `v0.4.0` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `0.4.1-preview.<run>.<attempt>` | `nextVersion` を `0.4.1` にバンプ後 |

**`v0.4.0` リリース後**、`eng/version.json` の両フィールドをバンプしてください:

```json
{
  "stableVersion": "0.4.0",
  "nextVersion": "0.4.1"
}
```

これにより次の main ブランチ CI ビルド（およびローカル pack）が
`0.4.1-preview.<run>.<attempt>` / `0.4.1-<sha>-<G-unit>` を生成し、`0.4.0`（安定版
リリースバージョンと衝突）の出力が継続されなくなります。

### 次リリース準備（v0.4.0）

**`v0.3.15` は publish 済み**（GitHub Release + NuGet）で、バージョンポリシーは
`0.4.0` 開発ラインにバンプされました — これは patch ではなく **minor** バンプです:
3 つの新しい automation コマンドに加えて、目に見える fail-loud な挙動変更があるため、
patch リリース以上の扱いが妥当です。リポジトリは現在 in-development の **`0.4.0`**
`nextVersion` 上にあり、G528 は **prepare-only** です — version メタデータと docs をバンプするだけで
publish ステップを追加しません。version-bump マージ自体は GitHub Release やタグを作成しません。
マージされ
[リリース準備ゲート](release-notes-v0.4.0.md#リリース準備ゲート-g528)が成り立った後、
**メンテナ/オペレーター（または外部のリリース automation）が `v0.4.0` の GitHub Release を作成・
publish** します。その Release の publish が `.github/workflows/release.yml`（`on: release: published`）を
発火させ、NuGet package とプラットフォームごとのバイナリ成果物を build・publish します。
完全な changelog と operator チェックリスト:
[release-notes-v0.4.0.md](release-notes-v0.4.0.md)。

**`v0.4.0` で出荷予定（`v0.3.15` 以降の変更）— orchestrator スタール防止バッチ、
fail-loud な domain 解決、parser 修正:**

- **3 つの新しい automation コマンド** — `automation stalled-work`（G523）、
  `automation heartbeat`（G526）、`automation issue-retire`（G525）: 保留中の
  transition を一覧する read-only な棚卸しコマンド、それを外部の低頻度スケジューラ向けに
  ラップして送信可能な reconcile メッセージを返す read-only コマンド、そして authored 通りには
  決して開始できない published issue を retire する canonical かつ atomic な transition。
- **fail-loud な domain 解決**（G522）— execution-unit を解決するサーフェス
  （`automation queue-seed-from-packet`、`review closeout-plan`、
  `automation publish-recovery`）は、明示的な `--domain` が優先され（packet 自身の
  `domain:` フィールドと矛盾する場合はエラー）、次に packet-declared な domain、
  どちらも無い場合は candidate domains と正確な re-invocation を示して fail loud する
  ようになりました — host の config-default domain への黙ったフォールバックは
  もうありません。**移行方法:** これまで黙ったフォールバックに依存していたスクリプトや
  automation は、`--domain` を明示的に渡すか、解決対象の packet.yaml が `domain:`
  フィールドを宣言していることを確認する必要があります。
- **orchestrator wake contract**（G524）— publish と delegate を同じ wake 内で行う
  (「次の wake に持ち越す」はもうありません); メッセージ上限は「wake ごと・receiver ごとに
  最大 1 通の delegation」と再定義; 新しい end-of-wake の `automation stalled-work`
  チェック（never-defer ルール付き）; receiver の completion/blocked レポートは
  すべての delegation の REQUIRED FINAL STEP になりました; そして送信のたびに
  dispatch roster を検証（`team.sh`）。
- **managed review worktree + design-alignment チェック**（G520）— review worktree は
  managed root（`.intent-cli/worktrees/review-<unit>`）配下で強制され、`/tmp` は
  使われません; design-alignment のエビデンス（packet、review-context、intent tree、
  ADR/decision note）が無い review completed 返信は incomplete 扱いになります。
- **Codex monitor（beta）ガイダンス**（G521）— agmsg Codex bridge 向けの setup preflight
  と 3 つの新しい troubleshooting エントリ（silent launcher、static TUI、doubled
  responses）。
- **packet-yaml parser 修正**（G527）— `PreparedPacketYamlScalarParser` の
  quote-balance チェックが delimiter-aware になり、アポストロフィだけを含む
  double-quoted 値が誤って拒否されていたフィールドインシデントを修正しました。
- orchestrator モードは引き続き **preview/experimental** です: オプトインで、まだ hardening 中であり、
  timer-loop モードは完全サポート・不変です。
  [エージェントメッセージオーケストレーション](12-agent-message-orchestration.md) を参照。

**リリース準備の検証（`v0.4.0` version bump のマージ前に実行）:**

```bash
cat eng/version.json   # stableVersion 0.3.15（公開済み）, nextVersion 0.4.0（リリース対象）
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   期待形: intent-cli 0.4.0-<sha>-G52x （stale なリテラルではない）
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.4.0.nupkg
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

version-bump マージが `main` に入った後、メンテナ/オペレーター（または外部のリリース automation）が
`v0.4.0` の GitHub Release を作成・publish します。その publish が `release.yml`
（`on: release: published`）を発火させ、NuGet package とプラットフォームごとのバイナリ成果物を
build・publish します。publish 後、上記のリリース後 `eng/version.json` バンプ
（`stableVersion → 0.4.0`, `nextVersion → 0.4.1`）を適用します。

### 削除済みリリースタグ（`v0.3.3`）の再作成

`v0.3.3` は早すぎる段階でタグ付けされ、タグは削除されました。**`v0.3.3` タグ/リリースの再作成は、
リリースブロッカーの2パケットが両方 `main` にマージされ、リリース CI のテストジョブが green に
なってから**のみ行ってください:

- **G441** — 初回 host 初期化デッドロックの修正。
- **G443** — リリース CI 安定化（installed-CLI surface probe を Linux runner 上の
  `Text file busy` / ETXTBSY exec レースに対し堅牢化し、各テストプロジェクトが一意な名前の
  `*.trx` を出力してリリース CI 結果を診断可能にする）。

両修正を含むコミットで green な CI 実行を得る前に再タグすると、元の失敗したリリースジョブが
再現します。
