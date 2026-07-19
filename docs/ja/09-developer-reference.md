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

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] [--claimed-silent-minutes <m>] --format json|markdown`
は、保留中の pipeline transition を age 付きで一覧化する **read-only** な
サーフェスです。これにより、1 回の orchestrator wake（あるいは外部の
heartbeat）だけで、人間が GitHub label・PR state・queue-state を手で
突き合わせることなく stall を検出・復旧できます。GitHub label、
queue-state、`runs.jsonl` を変更することは一切ありません — informational
な kind も、それが推奨する status check を自分で送ることはありません。
それは人間/orchestrator の行動として残ります。

すべての item は `is_informational`（`bool`）を持ち、2 つのグループを
区別します:

**actionable なカテゴリ**（`is_informational: false` —
`recommended_action` は常に実行可能な `intent-cli` コマンド）:

- `published-not-delegated` — OPEN の issue が `intent-target` を持つが、
  claim label（`intent-issue-in-progress` / `intent-pr-created`）がまだ無く、
  PR も一度も作成されていない。
- `pr-created-not-reviewing` — 元の issue が `intent-pr-created` を持ち、
  その issue を close する PR に `review-start` transition がまだ適用
  されておらず（PR に `intent-pr-reviewing` / `intent-pr-approved` が無い）、
  かつその PR が repair/rereview lifecycle に既に入っていない場合（下記
  参照 — いずれかの状態にある PR は、それぞれ独自の informational な
  kind として報告されます）。
- `merged-not-closed-out` — MERGED 状態の PR に紐づく queue-state item が
  まだ `Completed` になっていない（closeout — `pr-merged` +
  `closeout-recorded` の runs event — がまだ記録されていない）。

**informational なカテゴリ (G533)** — `is_informational: true`、
`recommended_action` は（transition コマンドではなく）説明的な prose、
age は可視性のためだけに報告されます:

- `repair-pending` — `intent-pr-request-update` および/または
  `intent-pr-update-in-progress` を持つ PR。field finding: まさにこの
  状態にあった OPEN PR（PR #1750）が、以前は `pr-created-not-reviewing`
  として誤報告され、`review-start` が推奨されていました — repair の
  最中としては意味的に間違っています。推奨を毎回疑ってかからなければ
  ならない detector は、その価値を失います。`age_minutes` は、PR の
  作成時刻ではなく PR 自身の `updatedAt` から計測されます — これは
  「repair 状態に入った時点」の CONSERVATIVE な近似であり、正確な
  label 付与の瞬間ではありません: GitHub は per-label-application の
  タイムスタンプを公開しておらず、`updatedAt` は（専用の label-event
  fetch を追加しない限り）その label の変更より後になり得る、PR への
  あらゆる種類の最新の変更を反映します。
- `rereview-pending` — `intent-pr-rereview-ready` を持つ PR（repair が
  push され、re-review 待ち）。`repair-pending` と同じ `updatedAt` ベース
  の age 近似です。
- `claimed-but-silent` — `intent-issue-in-progress` を持つが **まだ PR が
  作成されていない** issue で、`--claimed-silent-minutes`（デフォルトは
  **720** 分 / 12 時間 — 通常の作業セッションでは決して発火しないよう
  選ばれています）を超えて observable な活動が無いもの。「observable な
  活動」は、issue 自身の `updatedAt`（GitHub は label 変更・コメント・
  その他の timeline event でこれを更新します — 専用の per-issue
  timeline-events fetch を持たないこの slice にとって、最も近い
  proxy です）と、その issue を close する closing reference を持つ
  OPEN な PR の `updatedAt`（`intent-pr-created` が付与される前でも、
  紐づく PR 自身の活動はカウントされます）の、より新しい方として
  近似されます。issue または紐づく PR の `updatedAt` が欠落・不正な
  場合、それを「古い活動」として `createdAt`（claim 取得時刻や最終
  タッチ時刻ではなく、issue/PR が開かれた時刻を表す）にフォールバック
  することは決してありません — 誤解を招くほど古い silence interval を
  作り出してしまう可能性があるためです。代わりに、`excluded[]`
  （`activity-data-unusable`、どのタイムスタンプが使用不能だったかを
  明示）に入り fail closed します。パースされたタイムスタンプが
  （clock skew などで）未来の時刻になっている場合は、そのまま信頼せず
  「今」にクランプされます — これは candidate をより silent でなく
  見せる方向にしか作用しません。`recommended_action` は常に、
  assigned worker への status check request として読めるテキストです
  — 沈黙だけから completion・failure・いかなる transition も決して
  仮定しません。issue に PR が作成されると（`intent-pr-created`）、
  代わりに PR-lifecycle の kind が引き継ぎます。repair 状態の PR 自体が
  閾値を超えて stale であることを検出するのは、明示的に out-of-scope の
  follow-up です。

各 item は `kind`、`execution_unit`、`issue` および/または `pr`
（番号 + url）、`age_minutes`、`is_informational`、`recommended_action`
を報告します。`--stale-minutes` は、指定した閾値より新しい item を
除外します（デフォルトは `0` — すべてを age 付きで報告し、閾値は
呼び出し側が選ぶ）— これは 6 つすべての kind に一律に適用されます。
`claimed-but-silent` は、そもそも item が検討される前に、それ自身の
`--claimed-silent-minutes` 閾値でも追加でゲートされます（そのため
`--stale-minutes` を上げるだけでは、`claimed-but-silent` の item が
自身の閾値より早く現れることは決してありません）。`age_minutes` は、
GitHub が label 適用時刻や、専用の per-issue fetch 無しでの timeline
event を公開していないため、該当する GitHub entity の
`createdAt`/`updatedAt` タイムスタンプからの近似値です。
`published-not-delegated` は、既に取得済みの PR closing reference も
issue label とは独立にチェックします — そのため、completion label が
実態とずれてしまっていても（intent-pr-created が一度も付与されていない、
または削除されてしまったが、OPEN の PR が既にその issue を close して
いる場合）、誤って `worker claim` を推奨することはありません。

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

`automation heartbeat` (G526) はこの同じ analyzer をラップし、
`is_informational` を `message_body` に反映します: サマリー行は
「`N` pending transition(s)」と、informational な item が 1 つでも
あれば「`M` informational note(s)」に分かれます。各 item の行は、
actionable な kind では `— recommended:` コマンド ``、informational な
kind では `— FYI:` prose `` で終わります — そのため読み手（人間でも
orchestrator でも）が「transition は不要」を actionable な次コマンドと
取り違えることはありません。

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

### Queue の堅牢化: list parsing・retired backfill・lifecycle を考慮した selection (G534)

実際の hand-authored な packet と queue state に対する、関連する 3 つの
field finding をここでまとめて修正します。

**`queue enqueue` が両方の YAML list-item convention を受け付ける。**
packet reader(現行の `implementation_issue_packet` /
`review_context_packet` schema 用の `ProjectionPacketSerializer`、および
legacy な `execution_unit` / `implementation_issue` fallback 用の
`ProjectionPacketRuntimeReader`)は、これまで block-sequence の list item
を「4 スペース + `"- "`」というちょうど renderer 自身が生成する形式に
インデントされている場合にのみ list item として認識していました。より
一般的な、各 list item が親 key と同じカラムに置かれる 2 スペース
convention を使う hand-authored(または他ツールが生成した)packet は、
quoted / unquoted を問わずすべての item で `field line is missing ':''`
として全面的に拒否されていました。両方の reader は、カラム数を数える
のではなく内容(先頭の空白を除去した行が `"- "` で始まる、または
ちょうど `"-"` である)で list item を検出するようになったため、どちら
の convention でもパースできます — さらに同じファイル内で異なる field
ごとに convention を混在させることも可能です。

**`queue transition --to retired` が queue-state エントリを backfill
する — guarded・idempotent・terminal な transition として。**
`intent-cli packet retire`(`lifecycle.yaml` のみを書き込み、
`queue-state.json` には一切触れない)で retire された packet や、queue
tracking より前に `automation issue-retire` で retire された packet は、
JSON ファイルを手編集することなく、その queue-state item を直接
`retired` としてマークする必要が生じることがあります。`retired` は
transition target として受け付けられるようになりましたが —
`queue transition <execution-unit> retired` — 他の non-blocking target と
異なり、汎用的で source state を問わない transition path は通りません。
`automation issue-retire` 自身の refusal(G525)と一貫した、専用の
guarded な entry point(`QueueManager.Retire`)を持ちます:

- `Completed` 以外のどの state からも legal です — completed な item は
  mutation も run event もゼロのまま retirement を refuse します。
  retirement は authored 通りには決して完了できない作業にのみ適用され
  るためです;
- **linked PR こそが authoritative な evidence であり、queue-state
  自体ではありません** — queue-state は stale になり得るため、何かを
  mutate する前に CLI boundary がその item の `linked_pr`(存在すれば)
  を `gh pr view` 経由で解決し、その PR が **merged または closed**
  であると確認された場合は retirement を拒否します — queue-state が
  まだ非 `Completed`(`Queued`/`Review`/`Fixing`)と言っている item で
  あってもです。linked PR の state が解決できない場合(lookup 失敗、
  parse 不能・誤った repo の URL、曖昧な response)も retirement は
  refuse します — fail closed であり、open だと推定することは決してあり
  ません。linked PR が一切無い item は、この check を完全にスキップし
  ます(検証するものが無いため)。この lookup が retirement path の中で
  GitHub に到達することが許されている唯一の場所です —
  `QueueManager.Retire` 自体は network-free のままで、既に検証済みの
  evidence を受け取るだけです;
- 既に `Retired` な item に対しては idempotent です — 何も変更しない
  no-op であり、何度再実行しても重複した `retired` run event が追記
  されることはありません;
- 一度適用されると terminal です — retired な item は `queue
  transition` を通じて他のどの state(`queued`、`active`、`completed`、
  `blocked` など)にも二度と transition できません。汎用的な
  non-blocking/blocking transition path は、現在の state が `Retired`
  の場合には即座に refuse するようになり、以前は静かに許されていた
  reactivation の抜け道を塞ぎました。

item は queue-state に既に存在している必要があります(存在しなければ
先に `queue enqueue` で作成してください)。サポートされていない target
を指定した場合は引き続き refuse され、許可されている target の完全な
一覧(今回 `retired` を含むようになったもの)が表示されます。

**publish selector は queue-state と packet-lifecycle の evidence を
明示的に組み合わせ、曖昧な lifecycle metadata に対しては fail closed
する。** `intent next-slice` は各 candidate の `lifecycle.yaml`(存在
すれば)を、4 つの明示的な state のいずれかとして読み取ります —
absent(sidecar が無い。エラーではない)、valid-active
(`lifecycle: ready`)、valid-retired
(`absorbed`/`retired`/`superseded`)、invalid(unreadable、`lifecycle`
key の欠落、blank、または未知の値)— そしてそれを queue-state の
`Retired` signal と組み合わせます:

- どちらか一方の signal だけでも retirement を記録していれば unit は
  除外されます — これは queue-state エントリが一切無い場合(lifecycle
  のみによる retirement)や、`lifecycle.yaml` が一切無い場合(例えば
  `automation issue-retire` や `queue transition --to retired` 経由の
  queue のみによる retirement)でも成り立ちます;
- 明示的な `lifecycle: ready` は queue-state の `Retired` レコードを
  上書き**せず**、また non-publishable な `lifecycle.yaml` も、
  *存在する* queue-state エントリが `Retired` ではない
  (`queued`/`active`/`review`/`fixing`/…)からといって上書きされること
  は**ありません** — どちらの方向も contradiction であり、それでも
  除外されます。そして今回、どちらの方向も actionable な diagnostic
  (`lifecycle-metadata-diagnostic` warning。unit 名・sidecar path・
  両方の state を記した note 付き)として表示されるようになりました —
  以前はどちらの方向も黙って解決されていました。後続の、無関係な
  candidate が、先行する unit の矛盾した evidence を隠してしまうことは
  もう決してありません;
- agreement(両方の signal が retired)や、queue エントリが**一切無い**
  lifecycle のみによる retirement は contradiction ではなく、以前と
  同じく黙って除外されます;
- invalid な lifecycle metadata(unreadable、blank、key の欠落、または
  未知の値)は、queue state に関わらず unit を除外し、同じ diagnostic
  を発生させます — 曖昧な retirement evidence は決して
  「publishable」に解決されてはならないためです。したがって malformed
  な sidecar は、それが唯一の packet directory であっても、次の
  candidate として静かに浮上することは決してありません。

これら 3 つの修正を組み合わせることで、repo は `queue enqueue` /
`queue transition` / `intent next-slice` だけを使って、行き詰まった
retirement や queue tracking 以前の retirement から、`queue-state.json`
を一切手編集することなく完全に復旧できます。

---

### `request-update` が stale な `intent-pr-rereview-ready` を supersede する (G535)

Field finding #5(SKS-G824 / PR #1760): `intent-cli automation pr-transition
--transition request-update` は自身の repair label(`intent-pr-request-update`
を追加し、`intent-pr-reviewing` を除去)を適用していましたが、既存の
`intent-pr-rereview-ready` はそのまま残していました。`worker claim` は
`intent-pr-rereview-ready` を持つ PR を正しく refuse します(rereview-ready
な PR は reviewer が拾うべきものであり、worker のものではないため)—
そのため、PR が rereview-ready の間に design amendment が到着すると、
`request-update` によって repair 対象としてマークされたにもかかわらず
`claim` が触れることを拒否する PR が生まれていました。2 つの canonical
な rule 同士の deadlock であり、インストール済みのどのコマンドも先に
進めない状態でした。唯一の脱出策は、`review-start` → `request-update`
という非自明な迂回策でした。

`request-update` は、`intent-pr-request-update` を追加し
`intent-pr-reviewing` を除去するのと**同じ** write の中で、
`intent-pr-rereview-ready`(および legacy な `rereview-ready` 文字列
形式)を除去するようになりました — repair request は常に pending な
rereview-readiness を supersede するためです。

**両方の mode で truthful な audit output。** `--dry-run` が存在有無に
関わらず常に完全な planned removal set を報告する
`review-start`/`approved`/`review-release` とは異なり、
`request-update` が報告する `remove_labels` は `--dry-run` と
`--write` の**両方**で、既に fetch 済みの current label から常に
導出されます。`intent-pr-rereview-ready` のみを持つ PR は、その label
のみを supersede すると報告され、存在しない `intent-pr-reviewing` や
存在しない legacy `rereview-ready` を一緒に claim することは決して
ありません。再実行(や、一度も rereview-ready になったことのない PR)
は空の removal set を報告・適用します — 単にエラーにならないだけでは
なく、真に idempotent です。

**逐次的な add/remove ではなく、1 回の atomic GitHub request。**
`gh <kind> edit --add-label --remove-label` は `gh` CLI の
convenience wrapper であり、GitHub 視点での atomicity は保証されて
いません。`request-update` の `--write` path は、代わりに完全な
desired label set(現在の label のうち supersede される label を除いた
もの、プラス `intent-pr-request-update`)を計算し、内部の
`IGitHubLabelSetReplacer.ReplaceLabelSet` seam 経由で、**1 回**の
GitHub REST call — `PUT /repos/{repo}/issues/{number}/labels` — として
置き換えます。desired set が current set と(順序を問わず)既に一致
している場合は、真の no-op です — 単に removal list が空になるだけで
はなく、GitHub 呼び出しがゼロになります。この atomic-replace path を
使うのは `request-update` のみであり、他のすべての transition は既存の
`ApplyLabelTransitions` による add/remove path を変更なく使い続けます。

**Phase-aware な failure report — safety を過大に主張しない、正直な
記述。** 1 回の HTTP call であるということは、*この call 自身の action*
が中途半端に反映される window は無い、という意味であり、すべての
failure が「無害だと分かっている」という意味では**ありません**。
コマンドの error report はこの違いを正確に反映します:

- PUT 用の `gh` process 自体が起動しなかった場合(例えば実行ファイルを
  起動できない)、何も送信されていません — 単純な failure として、
  `applied: false`、`may_have_applied: false` で報告されます;
- その process が起動した後は、いかなる failure(non-zero exit、
  write/read error、timeout)も曖昧です — `gh` は既に request を
  送信済みで、GitHub は failure が表面化する前に既に適用済みかも
  しれません。`applied: false`、**`may_have_applied: true`** として
  報告され、mutation が確立しようとしていた `intended_labels` と、
  曖昧さを解消するための正確な `recovery_command`(`gh <kind> view
  <n> --repo <repo> --json labels`)が付きます — 「何も変わらな
  かった」という誤った claim は決してしません;
- PUT 自体は success を報告したが post-write の verification read が
  失敗した場合、あるいは success したが read back した set が一致
  しない場合、どちらも同じく `may_have_applied: true` と同じ recovery
  情報として報告されます — rollback や「no mutation」の signal として
  では**ありません**。どちらの場合も PUT 自体はかなりの確率で適用
  されているためです。

**Bounded concurrency model — post-write verification の正直な限界。**
GitHub の「Set labels」endpoint には optimistic concurrency 用の
conditional/If-Match support が無いため、caller の初回 read(desired
set の計算に使われる)と PUT の間で競合する label 変更を完全に防ぐ
ことはできません。post-write の verification read は、*その read の
瞬間にまだ残っている*不一致だけを検出します。初回 read の**後**、
PUT の**前**に別プロセスが追加した label は — desired set には決して
反映されないため — PUT によって黙って上書きされる可能性があります。
PUT と verification read の間に他に何も label を変更しなければ、その
read は intended set と完全に一致し、コマンドは concurrent な追加が
まさに失われたにもかかわらず success を報告します。この race は
read-after-write check だけでは原理的に検出不能であり、doc とコード
はそれを「完全に保護されている」かのように暗示するのではなく、明示的
にそう述べています。

これが landed したことで、SKS-G824 の recovery sequence(行き詰まった
rereview-ready を除去するための `review-start` の後の `request-update`)
はもはや不要です — `request-update` 単体で、`worker claim` が受け入れる
状態に PR を残すようになりました。`worker claim` 自体は変更ありません
— `request-update` を経ていない rereview-ready な PR は引き続き
refuse されます。

---

### `issue publish-flow` の idempotent rerun が 3 つの durable artifact すべてを独立に検証・復元する (G536)

Field incident(2026-07-19、G530 を issue #1164、G531 を issue #1166 として
publish 中): それぞれの GitHub issue 作成後に host `main` が並行して
進み、publish の途中で stash + fast-forward sync を強いられました。
#1164 では `publish.yaml` が `issue-created` の記録を保持したまま
生き残りましたが、`queue-state.json` の `linked_issue` と `runs.jsonl` の
`issue-created` event は、どちらも publish 前(存在しない状態)に
戻ってしまいました。#1166 ではさらに深刻で、`queue-state.json` の
`linked_issue` と `publish.yaml` の `issue-created` record が両方とも
失われ、`runs.jsonl` の `issue-created` event だけが唯一生き残った
signal でした。G536 以前の idempotent rerun は `publish.yaml` か
`queue-state.json` しか参照しておらず、`runs.jsonl` を identity source
として一度も read していなかったため、#1166 の形状は通常の create path
に fall through してしまい、同一 execution unit に対して**2 つ目の
GitHub issue** を作成しかねない状態でした — これが今回の repair で
修正した最も深刻な defect です。

**単一の共有 analyzer `PublishDurableArtifactAnalyzer` が、`issue
publish-flow` の idempotent rerun と `automation publish-recovery` の
両方を今や支えています。** この analyzer は 3 つの durable artifact —
`queue-state.json` の `linked_issue`、`publish.yaml` の `issue-created`
record、`runs.jsonl` 内のすべての canonical `issue-created` event — を
独立に parse し、単一の canonical issue identity を解決するか、fail
closed します。両方のコマンドは、同じ durable-state の形状に対して
まったく同じ、安定した gap identifier
(`queue_linked_issue_missing`、`publish_yaml_missing`、
`runs_event_missing`)を報告するため、2 つの surface が何が欠けている
かについて食い違うことは決してありません。

**`runs.jsonl` は存在有無だけでなく分類されます。** ある execution
unit の `issue-created` event は集合として read され、以下のように
分類されます:

- **ゼロ件** の一致する event → `runs_event_missing` gap。
- **ちょうど 1 件**、あるいは **duplicate-identical**(複数 event が
  すべて同じ issue number を指している — 例えばリトライによる追記)
  → present、gap なし。
- **conflicting**(同一 execution unit に対して event が**異なる**
  issue number を指している)→ `runs_event_conflicting` として fail
  closed する data contradiction であり、どちらか一方に黙って解決
  されることは決してありません。

**malformed なデータは "missing" とは区別して fail closed します。**
parse 不能な `publish.yaml`(`publish_yaml_malformed`)や、
`linked_issue`(`repo#number`)も `reason` issue URL も認識可能な形で
持たない `issue-created` run event(`runs_malformed`)は、決して
silently に absent 扱いされません — malformed なデータを "missing"
扱いすることは、実際には壊れているだけで本物の record を持つファイルへの
安全でない上書きを招くためです。分析全体は、gap/復元 logic が走る前に
fail-closed な結果へと short-circuit します。

**真の cross-artifact contradiction はどちらか一方を選ぶのではなく、
fail loud します。** artifact 同士が同一 execution unit の issue
number について食い違っている場合、それは欠けている artifact ではなく
data contradiction です — コマンドは refuse し
(exit 1、`cross_artifact_contradiction`)、矛盾するすべての値を named
し、どちらか一方を黙って信用することは決してありません。

**復元は本当に欠けている artifact だけに触れ、write helper の戻り値を
信用するのではなく re-read によって検証されます。** rerun は
analyzer の gap list を iterate し、first-run の success path が使う
のと全く同じ write helper(`TryPatchQueueStateLinkedIssue`、
`WritePublishArtifact`、`AppendIssueCreatedRunEvent`)を使って、
欠けている artifact だけを復元します。復元を試みた後、
`PublishDurableArtifactAnalyzer.Analyze` を**もう一度**、書き込み直後の
ファイルに対して独立に呼び出し、その re-read が残っている gap が
ゼロであることを確認した場合**のみ** `durable_state_synced: true` を
報告します — write helper が success を返しただけでは十分では
ありません。idempotent rerun の間、`gh` が再び呼び出されることは
ありません。

**復元が失敗した場合も、正確に何が欠けているか・どう recovery するか
を named した上で fail loud します。** artifact を復元できない場合
(例えば `queue-state.json` にこの execution unit を patch すべき
item がもはや存在しない場合)、コマンドは non-zero で exit し、
`durable_state_synced: false` を報告し、その `error` は(復元後の
re-analysis に基づいて)欠けている / 矛盾している artifact を正確に
named した上で、正確な recovery command(再試行する `issue
publish-flow ... --write`、または queue-state の linkage を reconcile
する `automation publish-recovery ... --write`)を示します。artifact
の検証は独立しており、all-or-nothing ではありません — 復元*できる*
artifact は、他の artifact が復元できなかった場合でも復元されます。

**dry-run は計画するだけで、決して書き込まず、`would_restore` を
報告します。** `issue publish-flow <unit> --repo <owner/repo>`
(`--write` なし)は同じ read-only な分析を実行し、既存の issue
identity が見つかった場合は、write helper や `gh` を一切呼び出す
ことなく、後続の `--write` rerun が復元するであろう正確な gap list
である `would_restore` を報告します。

**"local signal がゼロ" の unit に対して create する前に、まず
GitHub 側の existence check が走ります。** analyzer が 3 つの
local artifact すべてに対して identity を見つけられなかった場合、
そのまま `gh issue create` に fall through すると、すべての
local artifact が reset/lost されていても GitHub issue 自体は
再作成されていないケースで、本物の duplicate を生む risk があります。
create の前に `gh issue list --search` による corroboration check が
走り、title の一致が見つかった場合はコマンドは refuse し(exit 1)、
duplicate を作成したり曖昧な一致から identity を再構築しようと
試みたりするのではなく、operator に `automation
publish-recovery --write` か手動での backfill を案内します。

**`automation publish-recovery` は同一の gap を報告します。** すべての
unsafe stop は今や `durable_artifact_gaps` field を持ちます —
同じ execution unit・同じ path に対して呼び出された、同一の共有
analyzer の出力です。これにより operator(あるいは test)は、
`publish-recovery` の unsafe stop と、`issue publish-flow` 自身の
rerun が同一の durable state に対して独立に検出・復元するものとを、
直接比較できます。

**Round-4 review repair — canonical identity は number だけでなく完全な
tuple です。** 後続の review round で、"issue number が同じ" だけを
十分とみなすと、矛盾したデータや自己矛盾したデータをまだ通してしまう
ことが判明しました: 2 つの artifact が同じ issue *number* を記録して
いても *repo* が異なる場合や、単一の artifact 自身の repo/number/URL
field が互いに食い違っている場合が、silently に受け入れられて
いました。analyzer は今や、存在する各 signal に以下を要求します:

- **内部的に自己矛盾がないこと** — `queue-state.json` の
  `linked_issue`(`repo`、`number`、`url` を別々の field として持つ)や
  各 `runs.jsonl` event(`linked_issue` の `repo#number` descriptor と
  `reason` issue URL の両方を持ちうる)は、そもそも signal として
  受け入れられる前に、自分自身と一致していなければなりません。
- **canonical な GitHub issue URL であること** —
  `https://github.com/<owner>/<repo>/issues/<number>` に正確に一致する
  必要があります。この形に一致しない `/issues/` を含む文字列は、もはや
  canonical として受け入れられません。
- **確認済みの target repo と直接照合されること** — artifact 同士の
  pairwise な比較だけでなく、この command run がスコープする `--repo`
  と一致しない signal は、それが唯一の存在する signal であっても
  contradiction になります。

malformed / read 不能な `queue-state.json` も、`publish.yaml`/
`runs.jsonl` と全く同じように fail closed するようになりました
(`queue_state_malformed`)— 生き残った `publish.yaml` や `runs.jsonl` の
signal が、この analyzer が実際には read できなかった
`queue-state.json` の周りでの復元を authorize することは決してあり
ません。`publish.yaml` 自身の `execution_unit` field も、packet path が
スコープする unit と照合されます(不一致なら
`publish_yaml_malformed` — 別 unit の packet からコピーされたデータは
corruption であり、この unit の signal ではありません)。

**GitHub existence check は今や bool ではなく分類
(zero / exactly-one / multiple)であり、exact title と body の両方の
linkage を要求します。** 前の round の
`gh issue list --search ... --limit 20` と prefix-boundary な title
matching は、20 件を超えた実際の duplicate を見逃す可能性と、単に
似た title を持つだけの無関係な issue に一致する可能性の両方が
ありました。書き直された check は:

- `--limit 1000`(前 round の 20 に対して)で candidate を取得し、
  現実的などのような repo の issue 履歴に対しても、client-side の
  truncation によって本物の duplicate が silently に drop されない
  ようにします — GitHub 自身の `in:title` search は、identity の
  決定そのものではなく、高速な pre-filter として引き続き使用します。
- candidate の title が resolved された expected title と**完全に**
  一致することを要求します(prefix/boundary heuristic はもう
  ありません — 似た title を持つだけの無関係な issue は決して一致
  しません)。
- さらに、candidate の body が local packet の `github-body.md` の
  内容と(改行を normalize した上で)byte-for-byte 一致することを
  要求します — これは `gh issue create --body-file` で post された
  (あるいは post されるはずの)まさにその内容であり、title だけの
  推測ではなく本物の content-linkage check になります。
- 結果を分類します: **zero** 件の一致 → create しても安全、
  **ちょうど 1 件** → その確認済みの GitHub identity が、local
  signal による rerun と同じ `PublishDurableArtifactAnalyzer` ベースの
  復元 path に直接 feed され、`gh issue create` を一切呼び出す
  ことなく 3 つすべての local artifact を復元します。**multiple**
  件の一致 → fail closed、non-mutating、exit 1 — 曖昧さが自動的に
  解決されることは決してありません。

**Round-5 review repair — GitHub enumeration は raised limit ではなく
本物の cursor pagination であり、body normalization はより厳格に、
queue-state の cardinality も検証されます。** さらなる review round
で、固定の `--limit`(どれだけ高くても)は、filter 後の結果がそれを
超えた場合に本物の duplicate を silently に drop しうる cap のままで
あること、そして candidate/expected body への一律の `Trim()` が、
leading indentation が異なる Markdown(例えばcode block)を同一として
受け入れてしまいうることが判明しました。

- GitHub existence check は今や `gh api graphql` を使い、本物の
  `search(... first: 100, after: $cursor)` cursor-pagination loop を
  実行します — `state=all` は維持され(open と closed の両方の issue
  が参加します)、loop は `pageInfo.hasNextPage` が true である限り
  継続し、固定件数で止まるのではなくすべての page を蓄積します。
  `hasNextPage: true` かつ `endCursor` が無い page や、内部の safety
  ceiling(50 page / 5,000 candidate)以内に終了しない結果セットは、
  silently に truncate するのではなく fail loud します
  (`InvalidOperationException`)。
- body normalization は今や改行変換(`\r\n`/`\r` → `\n`)と、「末尾に
  ちょうど 1 つの改行がある」ことを「末尾に改行が無い」ことと同等と
  みなす処理(GitHub 自身の storage convention)**だけ**を行い、
  `Trim()` はもう呼び出しません。leading indentation、inner の
  spacing、改行の**前**にある trailing whitespace はすべて保持され、
  厳密に比較されます — そのため、たった 1 つの leading あるいは
  interior space が異なるだけの body は、正しく**別の** issue として
  扱われ、一致とはみなされません。
- `queue-state.json` の `ReadQueueSignal` は、最初に一致した item を
  返すのではなく、execution unit に一致する**すべて**の item を
  収集するようになりました — 同一 unit に対する 2 つ目の item は、
  それらが一致していようと矛盾していようと関係なく fail closed し
  (`queue_state_duplicate_execution_unit`、一致するすべての index と
  identity を named します)、identity が JSON array の順序に依存する
  ことは決してありません。
- 新しい test は、command level で使われる `IGitHubExistingIssueChecker`
  interface stub だけでなく、**本物の** `GhCliExistingIssueChecker`
  production class を、`PageFetcherOverride` という test seam経由で
  end-to-end に検証します — 実際の `gh` process の spawn だけを
  canned GraphQL JSON に置き換えます。multi-page の open+closed
  蓄積、2 つの fail-loud-on-truncation path、body-normalization の
  matrix(byte-identical / CRLF vs LF / single-trailing-newline /
  leading / inner / trailing whitespace drift)をカバーします。

**Round-6 review repair — GraphQL provider は構造的な pagination の
gap だけでなく、authoritative-response の欠陥に対しても fail closed
します。** さらなる review round で、checker がまだ authoritative に
「見える」response を、実際にはそうでない場合にも信用してしまうことが
判明しました: GraphQL response は、一見妥当な `data` と共に空でない
`errors` を持ちうること、誤動作する server が同じ `endCursor` を
2 回返し、safety cap まで永遠に loop してしまいうること、search の
`type: ISSUE` field は実際には query 自身が PR を除外しない限り issue
と pull request の両方に一致すること、そして個々の candidate が
(null body、null/empty title、non-positive number、あるいは requested
repo と正確に一致しない URL という形で)不完全なまま classification に
到達する前に reject されないことがありました。

- すべての GraphQL response は、その `data` が read される**前に**
  空でない `errors` array の有無を check します — spec は両方が同時に
  存在することを許容しており、error を伴う部分的な `data` が
  authoritative として扱われることは決してありません。
- search query は今や `is:issue` を含みます(`repo:<repo> <unit>
  in:title is:issue`)。これにより、似た title を持つ pull request は
  空/default に deserialize された node として risk を負うのではなく、
  server-side で除外されます。`state:` は意図的に含まれないままで、
  open と closed の両方の issue がスコープに残ります。正確な literal
  query 文字列は test で pin されています。
- 各 page の `endCursor` は seen-cursors set に追跡され、繰り返された
  cursor 値は 50-page の safety cap まで loop するのではなく、直ちに
  fail loud します(`InvalidOperationException`)。
- fetch されたすべての candidate は、蓄積される**前に**(そして
  classification や復元の write が既に進行してから発見するのでは
  なく)検証されます: positive な issue number、non-null/non-empty な
  title、non-null な body(null body は無効な provider response であり、
  空 text として silently に代替されることは決してありません)、そして
  この check がスコープする repo に対して canonical な
  `https://github.com/<requested repo>/issues/<number>` 形式に**厳密に**
  一致する URL。
- 新しい production-provider test は、部分的な data を伴う GraphQL
  errors、repeated-cursor 検出、literal な `is:issue`/`state:` 無しの
  query pin、page-fetcher failure の伝播、malformed JSON、そして
  すべての candidate-validation failure mode(non-positive number、
  null/empty title、null body、URL mismatch — 間違った repo、間違った
  number、間違った scheme、あるいは null)をカバーします。

**Round-7 review repair — provider fail-closed の残り 2 つの gap:
non-null だが empty な cursor、そして "non-nullable" な形状の代わりの
JSON `null`。** さらなる review round で、`hasNextPage=true` かつ
**empty あるいは whitespace-only** な `endCursor` が、round 6 の
null のみの check をすり抜けてしまうことが判明しました — それは次の
request で `cursor=` として送り返され、あたかも本物の値であるかのように
seen-cursors set に記録されてしまいます。別の問題として、
System.Text.Json は、JSON の値が `null` の場合、宣言上
non-nullable な reference-type property に silently に `null` を
代入します(C# は runtime で non-null を強制しません)— そのため
`pageInfo: null`、`nodes: null`、あるいは `nodes` 内の `null` な
entry は、以前は意図的な provider diagnostic ではなく、偶発的な
`NullReferenceException` へと degrade してしまっていました。

- `endCursor` は今や `string.IsNullOrWhiteSpace` で check されます —
  null、empty、whitespace-only はすべて「missing cursor」として扱われ、
  loop が再度 fetch する前に fail loud します。
- `pageInfo`、`nodes`、そして個々の node は、parse 直後に明示的に
  `null` check されます — これらのどの位置での `null` も、どの部分が
  欠けていたかを named した具体的な diagnostic で fail loud し、
  未処理の NRE になることは決してありません。
- 新しい test は、`FetchAllCandidates` 自身を通した完全な
  malformed-shape matrix を pin します: empty/whitespace-only な
  `endCursor`、`null`/wrong-type な top-level envelope(`null`、
  `[]`、単なる数値、単なる文字列)、empty な process output、
  `pageInfo: null`、`nodes: null`、そして `nodes` 内の `null` な
  entry。

---

### Canonical な publish-order override — queue priority (G537)

Field incident(2026-07-19): G529 の closeout 後、orchestrator は
——正当な理由をもって——field-impact fix である G532/G534 を
G530/G531 の continuation よりも先に publish するよう ruling を
下しました。`queue-state.json` の `priority` field(field で観測された
`high` のような値)は既に存在していましたが、どの selection surface も
それを参照していませんでした——orchestrator は「host state を
hand-edit するか、ruling を諦めるか」という禁じられた選択を迫られ、
正しく ruling を諦めて gap を報告しました。

**`intent-cli queue reprioritize <execution-unit> --priority
<high|normal|low> --reason <text> [--write]`** は、この gap を埋める
bounded canonical transition です:

- **queued かつ未 publish** の item の `priority` のみを mutate します
  ——item の state が `queued` でない場合、あるいは既に linked GitHub
  issue がある場合は refuse します(mutation なし、理由を named)。
- `--reason <text>` は必須です——理由が記録されない priority 変更は
  決して許可されません。
- **デフォルトは dry-run です。** `--write` なしでは、コマンドは
  実際に起こる mutation(old priority、requested priority、実際に
  何か変わるかどうか)を報告するだけで、`queue-state.json` には
  一切触れません。mutate して `priority-changed` runs event(old/new
  priority と operator の reason)を追記するには `--write` が必要です。
- item の現在の priority を再度 request した場合は no-op(idempotent)
  です——write も runs event も無く、`changed: false` です。

**`intent next-slice` は、eligible な candidate を priority-class-first
(high > normal > low)で order し、class 内では authoring order
(queue-state array order)を tiebreak として使います。** 既存の
すべての eligibility gate——packet directory の存在、execution-unit
namespace regex、domain/repo filter、**dependency completeness /
non-empty `blocked_by`**(review repair: `QueueSelection.SelectNext` が
既に強制しているのと同じ rule——`dependencies` のすべての entry が
`completed` であること、`blocked_by` が空であること)、G534 の
lifecycle-aware exclusion、legacy-retirement-marker check——は、以前と
全く同じように同じ loop 内で candidate ごとに実行され続けます。
priority が、candidate が本来 fail するはずの gate を skip させる
ことは決してありません。incomplete な dependency や non-empty な
`blocked_by` を持つ "high" priority の queued unit は、eligible な
lower-priority unit よりも先に選ばれることは決してありません——loop
は単に、他の gate failure と全く同じように、priority/authoring order
で次の candidate を試すだけです。priority が変えるのは、すでに
eligible な candidate のうちどれを、どの順序で試すかだけであり、
それは per-candidate の gate loop が走る**前**に行われます。この
reorder は **stable** な sort を使うため、すべての item が enqueue
のデフォルト値(`"normal"`)を持つ host——つまり実質的に priority が
設定されていない host——では、G537 以前の挙動と byte-identical な
出力になります。

`QueueItem.Priority` は schema level では引き続き単なる、validate
されない `string` です(変更なし)——`queue reprioritize` だけが
それを normalize・validate します(`high`/`normal`/`low`、
case-insensitive)。`next-slice` の ranking function は、認識できない
値や欠けている値をすべて `normal` として扱い、error にはしません。
そのため、手作業で書かれた、あるいは historical な `queue-state.json`
ファイルがこの field によって fail closed することはありません。

**Review repair — `queue reprioritize --write` は fail-closed かつ
repairable な write 順序を使います。** `queue-state.json` を必須の
`priority-changed` runs event の追記より先に書き込むと、追記 step が
その後失敗した場合に、audit record の無い durable な priority mutation
が残ってしまう可能性がありました。順序は逆にされています——runs
event を**先に**追記し、`queue-state.json` は**後で**書き込みます:

- event の追記が失敗した場合、`queue-state.json` は一切触れられません
  ——durable な変更は何も起きておらず、単純な retry がまっさらな状態
  から始まります。
- event の追記が成功した後で `queue-state.json` の書き込みが失敗した
  場合、state file がまだそれを反映していなくても、audit trail は
  既に試みられた変更とその reason を証明します——silent で unaudited
  な mutation には決してなりません。全く同じコマンドを再実行すると、
  既に記録されている event を検出し、`queue-state.json` の書き込み
  **のみ**を retry するため、convergence が duplicate な event を
  生成することは決してありません。

**Round-2 review repair(round 3 で置き換え済み)— dedup の match は
まず `queue-state.json` の `UpdatedAt` timestamp に束縛されました。**
これは本物の collision を修正するためでした: 全く同じ transition を
後で replay した場合(例えば `normal→high` reason `R`、続いて
`high→normal` reason `S`、そして再び `normal→high` reason `R`——これは
正当な 3 番目の mutation です)、生成される reason 文字列が最初の
event と byte-identical になるため、execution unit + event name +
reason text だけに頼る素朴な dedup は、その stale な historical event
を 3 番目の mutation の pending audit だと誤認してしまいます。

**Round-3 review repair(round 4 で置き換え済み)— dedup の match は
次に、mutation 前の `queue-state.json` bytes の SHA-256 content
fingerprint に束縛されました。** これは round 2 の `Ts >= UpdatedAt`
という束縛(timestamp が等しい場合や clock rollback で破綻し、そもそも
`changedAt` が `UpdatedAt` を厳密に上回ることも保証していなかった)から
wall-clock への依存を完全に排除するためでした。

**Round-4 review repair — content fingerprint は bytes を識別するもの
であり、この state machine は同一の bytes を再訪しうる。dedup token は
今や、何かの fingerprint ではなく、durable かつ injective な
`priority_revision` counter です。** round 3 の fingerprint は異なる
content に対しては collision-resistant ですが、genuinely revisit
可能です: 1 つの固定 clock のもとで `normal→high(R)`、続いて
`high→normal(S)` を行うと、**元の file の bytes そのもの**が再現されて
しまいます(同じ priority、同じ `updated_at`、その他すべて同じ)——
これは仮定ではなく本物の revisit です。その後の `normal→high(R)`
request は、最初の event と同一の fingerprint と tagged reason を計算
してしまうため、fingerprint ベースの dedup は、その stale な最初の
event を、正当に異なる 3 番目の mutation の pending event だと誤認
します。

`QueueItem` は今や `priority_revision` を持ちます——単なる `int` で、
意図的に `required` にはしていません。そのため、この field より前の
legacy な `queue-state.json` は、単に `0` として deserialize されます
(正しい migration semantics です: revision の計測は、その item に
初めて `queue reprioritize` が適用された時点から始まります)。成功した
`--write` は必ずそれを 1 だけ進めます。記録される reason は今や
`fromRevision->toRevision` のペアを持ちます(例: `... (revision
0->1)`)。dedup の match は、その tagged reason(に加えて execution
unit + event name)への exact match のままです——変わったのは tag の
**source** だけです:

- `toRevision` は、同一 item に対する 2 つの異なる成功した mutation
  によって生成されることが数学的に決してありません: 各 mutation は、
  durable に永続化された sequence の「次」の整数を厳密に消費し、一度
  消費されると二度と「次」にはなりません——**`queue-state.json` の
  他のすべての field が後で byte-identical な content に戻っても
  関係ありません**。counter 自身がその同じ durable な content の一部
  であり、常に前にしか進まないためです。
- 本物の retry(失敗した queue-state write の後の re-run)は、両方の
  試行で、依然として未 mutate な file から同じ `fromRevision` を
  read します——失敗した attempt はその bump を書き込んでいません
  ——そのため同一の `fromRevision->toRevision` ペアを計算し、自分
  自身の既に記録された event を見つけます。
- revision tag を全く持たない historical event(この fix より前の
  データ、あるいは手作業で編集されたもの)は、新たに tag された
  reason と exact-match することは決してありません。

**Round-5 review repair — revision counter 自身に input validation が
必要であり、recovery には bare existence check ではなく明示的な
cardinality/ownership の classification が必要であり、最終 write には
concurrent writer に対する保護が必要でした。**

- `PriorityRevision` は制約のない `int` でした——negative な値も
  問題なく deserialize され、`fromRevision + 1` は unchecked な
  arithmetic であり、`int.MaxValue` で silently に `int.MinValue` へと
  wrap し、monotonic/injective という invariant に直接違反していました。
  dry-run と `--write` の両方が、今や何かを preview・mutate する**前**
  に `PriorityRevision >= 0` を validate し、`checked` arithmetic で
  `toRevision` を計算します——negative あるいは exhausted な revision
  は、event も queue-state の write も無く fail closed し、手動の
  修復を要求します。
- Recovery は `events.Any(...)` という bare な existence check を
  使っていました。revision pair が operation identity である以上、
  同じ pair を claim する 2 つの IDENTICAL な event は silently に
  「1 つの pending attempt」として受け入れられてしまい(本物の
  duplication bug を隠蔽してしまいます)、genuinely CONFLICTING な
  event——同じ pair だが異なる reason や direction——は silently に
  無視され、その脇をすり抜けて 2 つ目の異なる event が追記されて
  いました。recovery は今や明示的な classification です: **zero**
  match → append しても安全、**ちょうど 1 つの exact match**(reason
  も一致)→ in-progress な retry の pending audit、**2 つ以上の
  identical match**、あるいは**同じ pair 上の任意の reason 不一致な
  match** → fail closed(exit 1、queue-state は無傷のまま、
  conflicting/duplicate な event を named)——どちらか一方に silently
  に解決されることは決してありません。
- `Execute` の先頭での read → event の追記 → queue write という
  sequence は、今や stale な concurrent writer に対して保護されて
  います。最終的な `queue-state.json` の write の直前に、file が
  fresh に re-read されます。target item の `priority_revision` が、
  この attempt が開始した時点の `fromRevision` と一致しなくなっていた
  場合、write は refuse します(audit event は既に durably に記録
  されているため、これは決して silent にはなりません)——concurrent
  writer が生成したものを blind に上書きするのではなく。最終的な
  mutation も、その **fresh** な re-read の上に適用されます
  (`Execute` の先頭で読んだ stale な copy の上ではなく)。そのため、
  他の field や item への無関係な concurrent change は、上書きされる
  のではなく保持されます。

**Round-6 review repair — round 5 の「re-read + compare」は依然として
TOCTOU check であり、authoritative な mutual exclusion ではありません
でした。** 2 つの concurrent な invocation が、両方とも同じ
`priority_revision` を read し、両方とも event claim ゼロを classify
し、両方とも自分自身の event を追記し、両方とも re-read してまだ
変わっていない revision を見て、両方ともそのまま commit してしまう
可能性がありました——同一の request であれば audit trail が
duplicate し、異なる request であれば silently に last-writer-wins な
state と conflicting な orphaned event が残ってしまいます。

`--write` は今や、authoritative な queue-state/runs.jsonl の read
**より前**に **non-blocking な OS-level exclusive lock**
(`queue-state.json` の隣に置く stable な sibling file、例えば
`queue-state.reprioritize.lock` に対する `FileShare.None`)を取得し、
revision validation、event-claim の classification/追記、fresh な
re-read、そして最終的な commit にわたってそれを保持し続けます——解放
されるのは、この invocation が完全に完了した時だけです。同じ lock を
取得できなかった 2 つ目の concurrent な invocation は、compare point
に到達することすらなく、**即座に** fail closed します(wait も retry
もありません)。dry-run は決して mutate せず、決して lock を取得
しません。round 5 の fresh-re-read-and-rebuild は、その lock の
**内側**に維持されます——これは、この lock を経由しない non-cooperating
な writer(`queue-state.json` を直接 mutate する任意の tool)に対する
保護であり続けます。一方、lock 自体が、2 つの **cooperating** な
`queue reprioritize` invocation を互いに排他的にするものです。

**Round-7 review repair — round 6 の保証には、throw する test callback
によって lock が leak しうる境界がまだ一箇所残っていました。**
test 専用の `OnLockAcquiredForTest` hook は、取得した lock stream を
dispose する `try`/`finally` に入る**前**に発火していました。この
callback から例外が飛ぶと(あるいは、同じ形で、`try` より前に誤って
配置された将来の post-acquisition コードから飛んでも)、OS-level の
lock handle が dispose されないまま残ってしまい——後続の独立した
invocation は、GC/finalization がいずれ handle を閉じるまで、
unbounded かつ non-deterministic な期間 lock され続けてしまいます。

callback を含む、lock 取得後のすべての操作は、今や lock stream を
dispose する `try`/`finally` の**内側**で実行されます——取得と
guarded region は隣接しており、その間にあるのは callback の呼び出し
だけです。callback (あるいは他の post-acquisition のステップ) が
throw しても、他の invocation がそれを「利用不可」として観測できる
期間を必要以上に長くする前に、例外が unwind する過程で lock は
直ちに解放されます。新しい deterministic な test は、throw する
callback を仕込んで、1 回目の call が例外を伝播しつつ queue/runs の
state が byte 単位で変化していないことを確認し、その上で 2 回目の
独立した call が同じ lock を直ちに取得して正常に完了することを
確認します。

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
- 拒否された `--scope` ヒントも、決して黙って消えることはありません:
  `facet_context_scope_warnings` エントリ（`hint`、`reason`）を生成し、
  どのヒントが・なぜ拒否されたか（domain root の外側、`..` トラバーサル
  など）を正確に示します — そのため「すべてのヒントが無効だったので
  何にもマッチしなかった」場合と、「本物の有効なヒントがたまたま
  どの node とも overlap しなかった」場合が、区別できないまま同じに
  見えることがなくなります。混在したリストでも、有効なヒントは
  そのまま適用されつつ、拒否されたヒントはすべて報告されます。
  `facet_context_all_scope_hints_rejected` は、要求されたヒントが
  すべて拒否された場合にのみ `true` になります。
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
  `facet_context_warnings: [{path, reason}]`、
  `facet_context_scope_warnings: [{hint, reason}]`、
  `facet_context_all_scope_hints_rejected: bool`。

**`intent-cli packet draft`** は、scaffold される `review-context.md`
の中に `## Facet context` セクションを生成するようになりました。
これは、その packet 自身の
`implementation_issue_packet.intent_references` と overlap する
facet node を一覧化します — `context collect` の `--scope` が使うのと
全く同じ overlap ロジック（上記の拒否ヒントの可視化を含む）なので、
2 つのサーフェスが「overlap」の意味について食い違うことはありません。
生成される内容は、2 つの HTML コメントマーカー（`<!-- BEGIN/END
GENERATED FACET CONTEXT (G530) -->`）の間に存在します。
`review-context.md` の残りの部分は手による所有物であり、一切触れられ
ません。マーカーの扱いは fail-closed です — 変更が試みられるのは、
ファイルが「開始マーカー 1 つ、終了マーカー 1 つ、その順序」を
正確に持つ場合のみです:

- **ファイルがまだ存在しない場合**: 現在の `intent_references`
  （既に `packet.yaml` が存在していれば、そのディスク上の値を
  読みます — 例えば、以前の `packet draft` 実行の後に operator が
  手で編集していた場合。この同じ呼び出しが別途書き込むかもしれない、
  テンプレートの空の `[]` では決してありません）を使って、ファイル
  全体が新規に書き込まれます。`created` として報告されます。
- **ファイルが存在し、かつ正確に 1 組の正しい順序のマーカーを持つ
  場合**: マーカーの「間」にある内容だけが、packet の現在の
  `intent_references` から再計算され、ファイル自身の既存の改行規則
  （CRLF か LF か — 決してハードコードしません。既存の CRLF ファイルが
  改行スタイル混在になることはありません）を使って置き換えられます —
  これにより、通常のワークフロー（空の references で scaffold →
  operator が `packet.yaml` に本物の references を追加 → `packet
  draft` を再実行 → block にそれが反映される）を通して、セクションが
  最新に保たれます。開始マーカーより前、終了マーカーより後のすべて
  （block の周りに手で書かれた文章を含む）は、バイト単位でそのまま
  保持されます。再計算された内容が既存のものと異なる場合は
  `updated`、異ならない場合は `skipped`（本物の no-op であり、
  見せかけの update ではない）として報告されます。
- **ファイルが存在するが、マーカーが全く無い場合**（この機能より前に
  作られたか、operator がマーカーを削除した場合）: 他の 3 つの
  scaffold ファイルの、単純な「存在すればスキップ」の挙動と全く
  同じように、完全に触れられないままになります。マーカーが手による
  所有物の中に後から注入されることは決してありません。`skipped`
  として報告されます。
- **ファイルが存在し、マーカーが他のいずれかの形（開始・終了の
  どちらか、または両方が重複している、終了マーカーがその開始マーカー
  より前に現れる、どちらか片方しか無い）である場合**: これも完全に
  触れられないままになります（黙った部分的な update や、「どちらが
  本物のペアか」を勝手に推測することは決してありません）が、
  `markers-malformed` として明確に区別して報告され、正確にどのような
  形が見つかったかを示す `detail` 文字列が付きます — この状態が、
  健全な「マーカーが全く無い」場合と同じに見えることはありません。

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

### facet-check (G531)

`intent-cli intent facet-check` は read-only な lexical scaffold で、
変更提案を G530 が届けるようにした G529 の facet node に照らし
チェックします — 提案が持つ候補の command/event 用語を、既存の
`vocabulary`/`invariant` node と突き合わせることで、reviewer が命名の
衝突やカバレッジの gap を早期に発見できるようにします（手作業での
突き合わせの代わりに）。これは明示的に semantic verifier では
**ありません**し、**決して** gate にもなりません: マッチングは
lexical（両辺を case/`-`/`_` 正規化した後の完全一致）であり、
false negative は起こり得るものとして許容されており、このコマンドは
結果に関わらず常に exit code `0` を返します。

```bash
intent-cli intent facet-check --domain <d> --packet G531 --format json
intent-cli intent facet-check --domain <d> --terms CreateOrder,ShipPackage --format json
```

- `--packet <execution-unit>` か `--terms <カンマ区切り>` のどちらか
  一方が必須です（互いに排他的。両方または どちらも無い場合は usage
  error）。`--terms` は、他の箇所の `--facets`/`--scope` と同様に、
  空の要素を拒否します。
- **`--packet` モード**では、その packet の `github-body.md` と
  `implementation.md`（連結。github-body が先）から候補の用語を
  抽出します — 抽出されるのは、バッククォート内の裸の識別子（例:
  `` `CreateOrder` `` — 空白や他の記号を含むバッククォートの範囲は、
  コマンド例などであり用語ではないためスキップされます。バックスラッシュ
  でエスケープされたバッククォート、例えば `` \` `` は、決して span を
  開始しません）、内部に camelCase/PascalCase の境界を持つプレーン
  テキストの単語（例: `CreateOrder`）、または `Command`・`Event`・
  `Query` で終わるプレーンテキストの単語です。ノイズは、どちらの
  ルールが走るより前に、Markdown を意識した形で除外されます:
  - **fenced code block** はブランクされます（その中の識別子は決して
    用語になりません）— 大まかな近似ではなく、CommonMark の実際の境界を
    尊重します: フェンスの開始/終了は最大 3 スペースまでしかインデント
    できません（4 スペース以上、またはタブでのインデントは、フェンス
    としては全く認識されません — なぜそれが「ノイズではない」ことを
    意味しないのかは、すぐ下の独立した indented code block パスを
    参照してください）。tilde（`~~~`）フェンスと 4 個以上の
    バッククォートのフェンスも認識されます。バッククォートのフェンスの
    info string は、バッククォートを含んではいけません（CommonMark
    ではこれをオープナーとして拒否します。インラインコードと曖昧になる
    ためです）。閉じる行は開始行と同じフェンス文字を使い、開始行以上の
    長さでなければなりません — 文字が違う、短すぎる、あるいはインデント
    が深すぎる行は決して閉じ行とはみなされず、フェンスを早期に終了
    させることはありません。CRLF と LF の両方の改行を扱い、閉じられて
    いないフェンスは fail closed です: 開始フェンスから文書の末尾まで
    がコードとしてマスクされ、識別子が漏れ出す余地を残しません。
    インラインの単一バッククォートの範囲は別の、影響を受けない関心事
    であり、バックスラッシュでエスケープされたバッククォートは決して
    span を開始しません。
  - **indented code block** — 上記のフェンス認識とは別の、独立した
    マスキングパスです: 「この行はフェンスを開始しない」ことと
    「この行はコードではない」ことは別の問いです。行は、先頭の
    空白が視覚上の column 4 に達した時点で条件を満たします。この
    計算は CommonMark 自身の計算方法と同じです — タブは「常に 4
    column」ではなく、次の 4 の倍数の column stop まで進みます —
    そのため、1 個のスペースの後にタブが続く場合（column 1 → タブで
    column 4 まで進む）も、4 個のリテラルなスペースや先頭の 1 個の
    タブとまったく同じように条件を満たします。1〜3 視覚 column は
    通常の、単に揃えられただけの prose であり、意図的に除外されます。
    連続する行のうち、各行が（上記の column ルールにより）インデント
    されているか、または空行であるものの最大の連続範囲が、1 つの
    block としてマスクされます。範囲の「内部」にある空行はそれを
    終了させません（indented code block の内部にある空行の継続を
    許容する CommonMark 自身の挙動を反映しています）。範囲は、本当に
    非空・非インデントの行に達した時点で必ず終了し、そこから通常の
    抽出がすぐに再開されます。このパスが持つ文書化された簡略化は
    ちょうど 1 つです — CommonMark の完全な list-item-continuation
    の判別（インデントを column 0 ではなく list マーカーからの相対位置
    で測る）は再現しません。そのため、list item 内の 4 column
    インデントされた継続行は、トップレベルの indented code と同様に
    扱われます。これはこの scaffold の、受け入れられた・文書化された
    制限であり、バグではありません。
  - **インライン Markdown/image リンク** — `[label](destination title)`
    / `![alt](destination title)` — は、小さな手書きのスキャナーに
    よって選択的にマスクされます（最初の `)` で終わってしまう素朴な
    正規表現ではありません）: destination と optional な title だけが
    ブランクされ、角括弧のラベル/alt テキストはそのまま残ります。
    可視のラベルは意図的に authored された提案テキストだからです
    （例えば `` [CreateOrder](design.md) `` は依然として
    `CreateOrder` という用語を生み出します）。destination は
    angle-bracket 形式（`<...>`。スペースを含むことができます）か、
    バランスの取れた、エスケープ可能な括弧を持つ bare 形式（
    `docs/(v1)/x.md` は、最初の `)` までではなく、正しく全体が
    マスクされます）のいずれかです。optional な title は、二重引用符、
    単一引用符、または括弧で囲むことができます。
  - **reference-style リンク** — リンクの「使用」側、`[label][ref]`・
    `[label][]`・裸の `[label]` は、それ自身の destination を隣に
    持たないため、ラベルだけが問題になり、そのまま触れられません
    （他の可視テキストと同様に完全に抽出可能です）。リンクの
    「定義」行（`[ref]: destination "title"`）は純粋な destination/
    title のメタデータであり、行全体がブランクされます。
  - **autolink**（`<scheme://...>`）は全体がブランクされます —
    `[label](url)` と異なり、保持すべき別個の可視ラベルが無いためです。
  - **裸の URL** と **複数セグメントのパス**（例:
    `src/Commands/CreateOrder.cs`）は、そのままブランクされます。

  抽出は、連結されたドキュメント全体を通じて「出現順」です（位置に
  関係なく「まずバッククォートのヒットをすべて、その後にプレーン
  ワードのヒットをすべて」ではありません — implementation.md 自身の
  候補は、常に github-body.md のすべての候補より後にソートされます。
  ここでの「ドキュメント順」を決めるのは連結の順序だからです）。
  用語マッチングと同じ正規化で重複除去され、先に出現した表記が
  保持されます — そのため `github-body.md` で言及された用語が、
  （別の形で）`implementation.md` で再び言及されても、
  `github-body.md` 側の出現の表記が保たれます。指定された
  execution-unit の packet ディレクトリが存在しない場合は usage
  error（exit `1`）になります。packet ディレクトリは存在するが
  どちらのソースファイルも無い場合は、単に抽出される用語が 0 件に
  なるだけです。
- **`--terms` モード**では、用語リストを明示的に受け取ります —
  packet も抽出も coverage セクションもありません（照合すべき packet
  scope が無いため、`coverage` は `null` になります。作り物の gap には
  なりません）。
- すべての用語は、domain の facet node に対して lexical にチェック
  されます。常に full-token の完全一致のみで（決して substring 検索
  ではありません）、node が持つ 2 つの自己申告サーフェスと比較され
  ます: node 自身の domain-relative な id の「最後のセグメント」
  （ファイル名由来の名前。例えば `commands/create-order` なら
  `create-order`）と、その title（抽出された `summary`。通常は
  node の見出し）です。どちらも用語と同じ方法で正規化されてから
  （小文字化、camelCase/PascalCase の境界、そして `-`/`_`/その他の
  記号の連続を単一のハイフンに畳み込む）比較されます — そのため
  `CreateOrder`・`create-order`・`create_order` はすべて「同じ用語」
  として扱われ、"Create Order" という title を持つ node は、id が
  一致しなくてもマッチします。複数の facet を持つ node は 1 回だけ
  報告されます（順序付けは最優先の facet グループが勝ちます）。facet
  の数だけ重複して報告されることはありません。
  - `related_nodes`: マッチしたすべての node を、4 つの facet
    すべてを対象に、正規の順序
    `vocabulary → invariant → decider → acceptance-property` で。
    各エントリは `{node: {id, facets, summary, path}, evidence}` の
    形です — `evidence` は、マッチした node-authored サーフェスごとの
    レコードのリストです: `{field: "id" | "title", value, match_kind}`。
    `value` は実際に比較された生の authored テキスト（node 自身の
    id の最後のセグメント、または title）、`match_kind` は、その
    フィールド固有の生テキストが用語と完全に一致した場合 `"exact"`、
    正規化した後にのみ一致した場合 `"normalized"` です。マッチ全体に
    対する単一の集約 match-kind は意図的にありません — id が
    normalized のみでマッチし、title が exact にマッチした node は、
    1 つの混ぜ合わされたフラグにせず、両方の事実を別々に報告します。
  - `collisions`: `related_nodes` のうち、node が `vocabulary` facet
    を持つ部分集合 — 提案の用語が重複または衝突している、既存の
    名前付き概念です。同じフィールドごとの `evidence` を持ちます。
  - `unmatched`: `related_nodes` が空の場合に `true`（その用語には
    facet によるカバレッジが全く無いということです）。
- **`--packet` モードのみ**: `coverage` セクションが、packet 自身の
  `implementation_issue_packet.intent_references` と overlap する
  `acceptance-property` node を報告します — `context collect
  --scope`/`packet draft` が使うのと全く同じ G530 の scope-overlap
  ロジックです（個々の拒否された reference の `scope_warnings` の
  可視化も含みます）。`acceptance-property` の node が packet の
  scope と 1 つも overlap しない場合、`gap` は `true` になります。
  `scope_status` フィールドは、その scope が「なぜ」そうなったかを
  区別します — `"valid-empty"`（意図的に authored された空の
  `intent_references: []`）、`"valid-non-empty"`、`"missing"`
  （`packet.yaml` が無いか、`intent_references` キー自体が無い）、
  `"malformed"`（ファイルが YAML としてパースできない）、
  `"wrong-shape"`（キーは存在するがシーケンスではない）のいずれかで、
  valid 以外の状態には `scope_status_detail` 文字列が付きます。
  これが必要な理由は、missing/malformed/wrong-shape な packet scope
  が、genuinely authored な空リストと「同じ」計算結果の `gap: true`
  に degrade してしまうためです（空/壊れた scope hint も、coverage
  を「何にもマッチしない」に絞り込みます）— `scope_status` が無ければ、
  この 2 つのケースは見分けが付きません。既存の packet ソースファイル
  — `github-body.md`、`implementation.md`、または `packet.yaml` —
  を読む際の本物の I/O エラー（「無い」のではなく実際の読み取り
  エラー）は本物の実行エラーとして扱われ（exit `1`、"Failed to read
  packet source..."）、決して黙って空の scope や空の用語リストに
  畳み込まれることはありません。
- domain に facet-annotated な node が 1 つも無い場合は
  `no_facet_data: true` になります（error ではありません — facets は
  optional です）が、それでも各用語の抽出・マッチング結果は報告され
  ます（当然すべて `unmatched` になります）— そのため呼び出し側は、
  「そもそも照合対象が無い」場合と「照合はしたが何も見つからなかった」
  場合を区別できます。このフィールドは JSON でも Markdown でも
  無条件です — Markdown は常に明示的な `No facet data: yes|no` の
  行をレンダリングし、`false` のときに省略することはありません。
- 壊れた `facets:` 宣言や node 上の未知の facet 値は、G530 と同じ
  `warnings` エントリ（`path`、`reason`）を生成します — 黙って
  消えることはありません。
- すべての結果は、lexical scaffold であり gate ではないという
  position を明示する `disclaimer` フィールドを、JSON でも Markdown
  でも持ちます。
- JSON の形: `{domain, disclaimer, no_facet_data, terms: [{term,
  related_nodes: [{node: {id, facets, summary, path}, evidence: [{field,
  value, match_kind}]}], collisions: [...], unmatched}], coverage:
  {nodes: [...], gap, scope_status, scope_status_detail,
  scope_warnings: [{hint, reason}]} | null, warnings: [{path, reason}]}`。
- この slice の Out of Scope（完全な境界は G531 issue を参照）:
  semantic/embedding ベースのマッチング、あらゆる blocking/gating の
  挙動、reviewer guidance や orchestrator delegation preflight への
  組み込み、そしてどの domain tree への annotation も — このコマンドは
  読み取りと報告のみを行います。

---

## バージョンフロー

リポジトリのバージョンポリシーは `eng/version.json` に記載されています。`stableVersion`
（最新の公開済み安定版）と `nextVersion`（準備中 / 開発中のライン）の単一の source of
truth です。G468 以降、ローカル `dotnet pack` のデフォルト `<Version>` はこのファイルから
導出されるため、ローカル pack / install は stale な csproj リテラルではなく開発中の
`nextVersion` を報告します:

```json
{
  "stableVersion": "0.4.0",
  "nextVersion": "0.5.0"
}
```

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `0.5.0-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `0.5.0-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `0.5.0-rc.N` | タグ `v0.5.0-rc.N` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する |
| 安定版リリース | `0.5.0` | タグ `v0.5.0` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `0.5.1-preview.<run>.<attempt>` | `nextVersion` を `0.5.1` にバンプ後 |

**`v0.5.0` リリース後**、`eng/version.json` の両フィールドをバンプしてください:

```json
{
  "stableVersion": "0.5.0",
  "nextVersion": "0.5.1"
}
```

これにより次の main ブランチ CI ビルド（およびローカル pack）が
`0.5.1-preview.<run>.<attempt>` / `0.5.1-<sha>-<G-unit>` を生成し、`0.5.0`（安定版
リリースバージョンと衝突）の出力が継続されなくなります。

### 次リリース準備(v0.5.0)

**`v0.4.0` は publish 済み**(GitHub Release + NuGet)で、バージョンポリシーは
`0.5.0` 開発ラインにバンプされました — これは patch ではなく **minor** バンプです:
このバッチは 2 つの新しいコマンド(`intent facet-check`、`queue reprioritize`)、新しい
intent-tree schema サーフェス(`facets`)、新しい stalled-work kind、新しい transition
target(`retired`)を出荷するため、patch リリース以上の扱いが妥当です。リポジトリは現在
in-development の **`0.5.0`** `nextVersion` 上にあり、G538 は **prepare-only** です —
version メタデータと docs をバンプするだけで publish ステップを追加しません。
version-bump マージ自体は GitHub Release やタグを作成しません。マージされ
[リリース準備ゲート](release-notes-v0.5.0.md#リリース準備ゲート-g538)が成り立った後、
**メンテナ/オペレーター(または外部のリリース automation)が `v0.5.0` の GitHub Release を作成・
publish** します。その Release の publish が `.github/workflows/release.yml`(`on: release: published`)を
発火させ、NuGet package とプラットフォームごとのバイナリ成果物を build・publish します。
完全な changelog と operator チェックリスト:
[release-notes-v0.5.0.md](release-notes-v0.5.0.md)。

**`v0.5.0` で出荷予定（`v0.4.0` 以降の変更）— semantic facets、stalled-work の正確性、
queue の頑健性、label supersession、publish の信頼性、priority override:**

- **semantic facets**(G529–G531)— intent-tree ノードが frontmatter で宣言できる
  4 つの closed set の `facets:` 値(`vocabulary`、`invariant`、`decider`、
  `acceptance-property`)。`intent-cli context collect` と `packet draft` は、
  分類されていない queue-state/clarification context より前に、facet で分類された
  `## Facet context` セクションを優先的な局所化された semantic context として供給する
  ようになりました。read-only な `intent facet-check` は、change proposal を facet
  ノードに照らして、命名衝突やカバレッジのギャップを、決して gate することなく
  表面化します。
- **stalled-work の正確性**(G532–G533)— leading-ID/nested-domain の execution-unit
  識別は、タイトルだけを信頼するのではなく実際の packet/queue linkage で裏付けられる
  ようになりました。`--domain` は必須の authoritative なスコープ入力です。3 つの
  新しい informational kind(`repair-pending`、`rereview-pending`、
  `claimed-but-silent`)により、修理中/再レビュー待ちの PR が誤った `review-start`
  推奨で誤報告されることがなくなりました。
- **queue の頑健性**(G534)— packet reader は両方の YAML list-item インデント慣習を
  受け入れるようになりました。`retired` は今や guarded かつ terminal な `queue
  transition` target です(適用前に紐づく PR の実際の GitHub 状態を検証)。
  `intent next-slice` は queue-state と packet-lifecycle の retirement エビデンスを
  明示的に組み合わせ、矛盾があれば fail closed します。
- **label supersession**(G535)— `automation pr-transition --transition
  request-update` は、同じ write の中で古い `intent-pr-rereview-ready` を
  クリアするようになりました。これにより、`request-update` が修理対象としてマークした
  PR を `worker claim` が拒否してしまうデッドロックが解消されました。
- **publish の信頼性**(G536)— `issue publish-flow` の idempotent な再実行は、
  3 つの durable artifact(GitHub issue、queue-state エントリ、`runs.jsonl`
  イベント)すべてを、1 つのシグナルを信頼するのではなく独立に検証・復元する
  ようになり、state が矛盾したまま黙って残るのではなく fail loud するようになりました。
- **priority override**(G537)— 新しい `queue reprioritize` コマンドは、queued かつ
  未 publish の execution unit の priority を、durable かつ revision-counted、
  lock-protected な audit protocol の下で設定します。`intent next-slice` は
  priority class を優先(high > normal > low、authoring order による安定した
  tiebreak)して候補を選択し、dependency/WIP/clarification/lifecycle の gate は
  常に priority に優先します。
- orchestrator モードは引き続き **preview/experimental** です: オプトインで、まだ hardening 中であり、
  timer-loop モードは完全サポート・不変です。
  [エージェントメッセージオーケストレーション](12-agent-message-orchestration.md) を参照。

**リリース準備の検証（`v0.5.0` version bump のマージ前に実行）:**

```bash
cat eng/version.json   # stableVersion 0.4.0（公開済み）, nextVersion 0.5.0（リリース対象）
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet run --project src/IntentSystem.Cli -c Release --no-build -- --version
#   期待形: intent-cli 0.5.0-<sha>-G53x （stale なリテラルではない）
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release -o .artifacts/packages
ls .artifacts/packages/   # JTechJapan.IntentSystem.Cli.0.5.0.nupkg
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  -c Release --filter "FullyQualifiedName~ReleasePackageMetadataTests"
```

version-bump マージが `main` に入った後、メンテナ/オペレーター（または外部のリリース automation）が
`v0.5.0` の GitHub Release を作成・publish します。その publish が `release.yml`
（`on: release: published`）を発火させ、NuGet package とプラットフォームごとのバイナリ成果物を
build・publish します。publish 後、上記のリリース後 `eng/version.json` バンプ
（`stableVersion → 0.5.0`, `nextVersion → 0.5.1`）を適用します — これは今回のパケットではなく
NEXT のリリース準備パケットに委ねられます。

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
