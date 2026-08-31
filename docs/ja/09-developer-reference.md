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
INTENT_CLI_BASE_VERSION="$(sed -n 's/^[[:space:]]*"nextVersion"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' eng/version.json)"
export INTENT_CLI_LOCAL_VERSION="$INTENT_CLI_BASE_VERSION-local.$(date -u +%Y%m%d%H%M%S)"
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

### host-local wrapper の安全な refresh

host-local orchestration CLI をこの checkout に追従させる場合は repository helper を使います:

```bash
eng/refresh-host-local-intent-cli.sh /path/to/host-repo
```

helper は local version の base を `eng/version.json` の `nextVersion` から導出し、package id を
CLI project から読み取ります。installed wrapper が使う package は削除せず、一意な candidate を
pack してから `.tmp` path の candidate wrapper で次を検証します。

1. `--version` が導出した local version を報告する;
2. `automation summary --format json` が `automationCommandSurfaceVersion` を出力する; および
3. required automation capability がすべて存在する。

すべての check が通った後に限り、同一 filesystem 上の 1 回の rename で candidate を
`.intent-cli/bin/intent-cli` へ promote します。check が失敗すると、失敗した check と remedy を
明示して non-zero で終了し、candidate package と `.tmp` wrapper を削除します。以前の installed
wrapper は byte-identical かつ runnable なままです。`HOST_ROOT`、`CHILD_INTENT_SYSTEM`、
`INTENT_CLI_LOCAL_VERSION` override は引き続き利用できますが、override は `nextVersion` から
導出した fixed version を再利用できません。

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

packet を push する、または queue seed に渡す前に、次の one-shot readiness check を
実行します:

```text
intent-cli packet draft --execution-unit <unit> --domain <d> --target-repo <owner/repo> --dry-run --format json
```

これは現在 disk 上にある packet を検査し、妥当な planned scaffold を既存 file として
数えません。green は、すべての canonical packet file が現在存在し、すべての required contract
section が存在することを意味します（`contract_publishable: true`）。さらに、その他の publication
check に refusal がないことも保証します。refusal は `missing_canonical_files`、
`missing_contract_sections`、`refusal_reasons`、`recommended_actions` をまとめて返します。
最初の 1 件だけ直して push せず、報告されたすべてを修正して同じ command を再実行します。

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

`intent-cli automation stalled-work --domain <d> --repo <r> [--stale-minutes <m>] [--claimed-silent-minutes <m>] [--backlog-idle-minutes <m>] --format json|markdown`
は、保留中の pipeline transition を age 付きで一覧化する **read-only** な
サーフェスです。これにより、1 回の orchestrator wake（あるいは外部の
heartbeat）だけで、人間が GitHub label・PR state・queue-state を手で
突き合わせることなく stall を検出・復旧できます。GitHub label、
queue-state、`runs.jsonl` を変更することは一切ありません — informational
な kind も、それが推奨する status check を自分で送ることはありません。
それは人間/orchestrator の行動として残ります。

#### G673 degraded GitHub detection

GitHub read が失敗し、structured な `gh api rate_limit` observation が query の resource exhaustion
を示した場合、result は `cause: github-api-quota-exhausted` と、`degraded_state.resource`、
`degraded_state.remaining`、`degraded_state.reset` / `reset_at` を持ちます。これは成功した
`action: none` / empty scan と、`github-command-failed`、`github-auth-failed`、
`github-json-invalid` とを区別します。

named state を観測した後、scanner は GitHub-derived lane を追加で読まず、local collector は継続します。
result は `partial: true`、`detection_available: false`、`detection_status: unavailable` を持ち、
保持した local item にも `partial: true` を付けます。partial な空 scan を healthy verdict に変換しては
いけません。command は retry、sleep、reset scheduling、request budget、transport change、cache、
batching を行いません。

すべての item は `is_informational`（`bool`）を持ち、2 つのグループを
区別します:

**actionable なカテゴリ**（`is_informational: false` —
`recommended_action` は、存在する場合は実行可能な `intent-cli` コマンドを
名指しします。`version-roll-required` は意図的に必要な human edit を名指しします）:

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
- `backlog-ready-idle` (G544) — 最後に残っていた未カバーの stall class:
  **ready だが一度も着手されていない** 作業。以下がすべて成立したときに
  発火します: (1) 対象 domain の WIP が空である — その domain に属すると
  解決される open な PR が一つも無く、かつ `intent-target` を持つ open な
  issue が、その domain に属すると解決される(あるいは属さないと確定
  できる)ものも一つも無い。PR 自体が `intent-target` を持つことは無いため、
  PR の domain はその CLOSING ISSUE を通じて(PR 自身のタイトルではなく)
  解決されます — この surface の他の箇所と同じ execution-unit/domain の
  corroboration ルールを使います。domain を全く corroborate できない
  candidate(PR/issue のいずれでも)— closing-issue のリンクが無い、
  closing issue が open issue の中に見つからない、execution unit が
  corroborate できない、等 — は、すべての domain を blocking すると
  保守的に扱われます。ここでは false な「idle」報告の方が危険な方向だから
  です。対象 domain と異なる domain に属すると **確定的に** confirm
  された candidate だけが例外として扱われます; (2) `issue publish-flow`
  preflight 自身が使うのと
  **同じ** canonical selector(`intent next-slice` の candidate selection
  — dependency/blocked-by、lifecycle、domain、contract-completeness の
  すべての gate を含み、別のヒューリスティックではない)が publishable な
  (`issue-cut-ready`) candidate を報告する; (3) `runs.jsonl` に
  `--backlog-idle-minutes`(デフォルト **45** 分)以上、活動が記録されて
  いない。ここでの「活動」は `runs.jsonl` の全行にわたる `ts` の
  **最大値** です — これは他のどの kind とも異なるシグナルです。
  この candidate はまだ publish されていないため、そもそも構造的に
  自分自身の GitHub timestamp を持たないからです。`runs.jsonl` が
  欠落・空・パース不能な場合、baseline を確立できないため
  `excluded[]`(`activity-data-unusable`)へ fail closed します —
  推測された age が使われることは決してありません。
  `recommended_action` は、対象ユニットの canonical な publish
  コマンド(`intent-cli issue publish-flow <unit> --repo <r> --write
  --format json`)です。field incident、2026-07-20(G539 closeout の
  直後): WIP は空で、4 つの authored packet(G540–G543)が
  `issue-cut-ready` かつ未公開の状態であったにもかかわらず、
  `stalled-work` は `stalled: false` と報告し続けていました — 復旧には
  明示的な人間/design 側からの WAKE メッセージが必要でした。
  `backlog_idle_minutes_threshold` は、すべての result で
  `stale_minutes_threshold` と並んで報告されます。
- `repair-stalled` (G546) — repair lifecycle にある PR
  (`intent-pr-request-update`、`intent-pr-update-in-progress`、
  `intent-pr-rereview-ready`)で、`--repair-silent-minutes`
  (デフォルト **180** 分)を超えて観測可能な活動が一切ないもの。これは
  G533 が「field data が出るまで」と保留した promotion であり、その
  field data は 2 度得られました。より鋭いのは次のケースです: G545 の
  repair が `intent-pr-update-in-progress` を claim したまま、implement
  セッションの死亡により **4 日間**(2026-07-23 → 07-27)沈黙し、その間
  `stalled-work` は `stalled: false, items: []` を報告し続け、復旧には
  手動 ping が必要でした。その PR は **draft** であり、ここの他の PR 系
  kind はすべて draft PR を除外するため、どの kind もこれを捕捉できて
  いませんでした。したがって `repair-stalled` は draft も対象にします
  (閾値内では draft の repair PR は従来どおり不可視のままで、そのために
  informational な item を新たに捏造することはしません)。
  `recommended_action` は常に**責任スレッドへの status check** です —
  `intent-pr-request-update` / `intent-pr-update-in-progress` は
  `implement`、`intent-pr-rereview-ready` は `review-dispatch` — であり、
  transition や担当の付け替えでは**決してありません**: 沈黙だけでは、
  repair が成功したのか失敗したのか、担当を取り上げるべきなのかを
  確立できないためです。観測可能な活動は PR 自身の `updatedAt` で、
  これは 3 つの活動クラスすべてをカバーする唯一のフィールドです
  (GitHub は head branch への push、あらゆる comment、あらゆる label 変更で
  これを更新します)。`claimed-but-silent` と同様に fail closed です:
  `updatedAt` が欠落・不正な場合は沈黙を確立できないため、使えない証拠に
  基づいて flag するのではなく promotion **しません**。
- `version-roll-required` (G725) — `stalled-work` は repository の公開済み
  stable release も読み取り、最新の stable tag と `eng/version.json` を
  比較します。対象は publish 済み、draft でなく、prerelease でなく、
  有効な `major.minor.patch` tag だけです。そのような release が無ければ、
  local policy が古く見えても host は沈黙します。policy がすでに
  `stableVersion = <releasedVersion>`、`nextVersion = <nextPatch>` なら
  それも沈黙します。それ以外では finding が `released_version`、
  `expected_stable_version`、`expected_next_version` と、
  `recommended_action` に finding を消すための正確な follow-up edit を
  持ちます。roll は human の follow-up commit のままです。この surface は
  意図的に read-only であり、roll は release-note stub、次リリース準備
  section、child-main CI と一緒に調整する必要があります。検出だけをこの
  slice の scope とし、release automation は行いません。
- `design-decision-pending` (G552) — **design の判断** で止まっている hold で、
  canonical な clarify surface (`intent-cli clarify open`) を通じて OPEN な
  clarification artifact として記録されたもの。ブロックされている execution
  unit、clarification の age(artifact 自身の `createdAt` — ブロックが記録された
  瞬間)、質問の 1 行サマリを報告します。`recommended_action` は、回答すべき
  clarification を正確に名指しし(`intent-cli clarify answer --execution-unit
  <unit> --question-id <id> --answer "<decision>"`)、オペレーターへの
  エスカレーション経路も併記します。**自動回答は決してしません** — 回答は
  design の content だからです。clarification に回答する(または applied /
  cancelled にする)ことが item を消す唯一の方法で、閾値も別の transition も
  ありません。これはここで唯一、自分自身の GitHub エンティティを持たない kind
  であり、まさにそれが検出対象の stall が不可視だった理由です: field incident
  2026-07-28 16:11 → 07-29 01:29 — G551 のレビューが、技術チェックがすべて
  green のまま 1 行の wording 判断のために **9 時間** final verdict を保留し、
  hold は agmsg メッセージ上にしか存在せず、`stalled-work` はその間ずっと
  `stalled: false` を報告していました(field record で 4 件目の design 不在
  stall)。両方向に fail closed です: 読めない・deserialize できない artifact は
  「回答済み」として飛ばすのではなくパス付きで `excluded[]`
  (`clarification-unreadable`)へ、packet が別 domain を宣言している
  clarification は要求 domain に帰属させず除外します。この kind が読む artifact を
  ディスク上に置くのは guide の clarification-backed hold ルールです — agmsg
  だけの hold は contract violation であり、hold が実在するのにこの kind が
  出ないなら、それは artifact が記録されなかったということです。
- `knowledge-writeback-pending` (G564) — **closeout 済み**の unit
  (`runs.jsonl` に `closeout-recorded` イベントがある)で、packet が knowledge
  write-back を**宣言**しているのに(`knowledge_updates.*.required: true` —
  `intent_tree` / `adr` / `diagram` / `docs` — または
  `closeout_learning.write_back_required: true`)、
  `.intent-cli/knowledge-writebacks/<unit>/record.json` に**記録が無い**もの。
  item は closeout からの age(最も**早い** `closeout-recorded` を基準にするため、
  closeout のリトライで age がリセットされることはありません)、宣言された facet、
  `declared_write_back_targets` を持ち、`recommended_action` は
  `intent-cli automation knowledge-writeback-record` を名指しします。何も required
  と宣言していない unit は決して現れません — 辞退は正当な回答であり、この kind は
  「破られた約束」を検出するものであって「熱意の不足」を検出するものではありません。
  両方向に fail closed です: 読めない packet 宣言、読めない `runs.jsonl`、読めない
  既存レコードはいずれも「保留なし」と解釈せず、**パス付き**で `excluded[]`
  (`knowledge-metadata-unreadable`)に出ます。本機能の出荷前に closeout された
  unit は既定で対象外です(floor: `2026-08-01T00:00:00Z`)。
  `--knowledge-writeback-since <iso-8601>` で明示的に遡れます。ここでは intent の
  content を書きません — tree を書くのは design です(G300)。この kind は記録されて
  いない義務を可視化し、経過時間を刻むだけです。field evidence はリリース前監査
  (2026-07-31): node 09 は実装前の設計を記述したまま、node 02 は docs が実装する 7 つの
  リリースフロー規則を 1 つも記録しておらず、node 08 は wake contract に対して数
  リリース分遅れていました — 構造的シグナルの無いまま数週間の drift です。

**informational なカテゴリ (G533)** — `is_informational: true`、
`recommended_action` は（transition コマンドではなく）説明的な prose、
age は可視性のためだけに報告されます:

- `repair-pending` — `intent-pr-request-update` および/または
  `intent-pr-update-in-progress` を持つ PR で、`--repair-silent-minutes`
  の**閾値内**にあるもの(G546 — 閾値を超えると上記の actionable な
  `repair-stalled` kind へ promotion されます。閾値内の出力は不変です)。
  field finding: まさにこの
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
  push され、re-review 待ち）で、`--repair-silent-minutes` の**閾値内**に
  あるもの(G546)。`repair-pending` と同じ `updatedAt` ベースの age
  近似です。
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
  follow-up です。**G545: queue-state item が `state=blocked` である
  unit を exempt します** — `stalled-work` 呼び出しごとに一度だけ
  consult され、`queue-state.json` が欠落・パース不能でも許容します
  (この kind の G545 以前の GitHub-labels-only な挙動へそのままフォール
  スルーするため、`queue-state.json` を一切使わない domain でもこの
  検出を失うことはありません)。queue-blocked な unit はここでは決して
  報告されません——下記の `blocked-label-drift` を参照してください。
- `blocked-label-drift` (G545) — GitHub と queue-state の一時的な
  mismatch であり、stall では**ありません**: この unit の queue-state
  item は明示的な `blocked_by` reason を伴う `state=blocked` ですが、
  対応する GitHub issue はまだ `intent-issue-blocked` を持っていません
  ——label 側がまだ reconcile されていない状態です。`recommended_action`
  は正確な canonical reconcile コマンド(`intent-cli automation
  issue-block <unit> --repo <r> --issue <n> --reason
  "<blocked_by のテキスト>" --write --format json`)を名指しします。
  このコマンドが label を収束させると、同じ unit は `stalled-work` から
  完全に姿を消します
  (`claimed-but-silent` も `blocked-label-drift` も発火しません——
  GitHub と queue-state が一致するためです)。field finding、
  2026-07-21(sekiban-as-a-service): 5 item(SKS-G818、SKS-G837、
  SKS-G835、SKS-G839、SKS-G840)が明示的な `blocked_by` dependency を
  伴って queue-state 上で `state=blocked` であったにもかかわらず、
  `claimed-but-silent` は GitHub label しか読まず、issue-level の
  「blocked」表現が全く存在しなかったため、毎 wake `claimed-but-silent`
  として報告されていました。

**`intent-cli automation issue-block <execution-unit> --repo <owner/repo>
--issue <n> --reason <text> [--write] [--dry-run] [--format text|json]`**
(および、その `--clear` 版——`--reason` を省略)は、単一 execution unit に
ついて「blocked」の**両方の authoritative な表現を収束させる唯一の
canonical で bounded な transition** です:

- **queue-state** — `state=blocked` と `blocked_by: ["<reason>"]`。既存の
  変更されていない `QueueManager` の blocking transition(`queue transition
  <unit> blocked` と同じ機構)経由で適用し、あわせて永続化された
  `runs.jsonl` audit event(`event: blocked` / `queued`、`by: intent-cli
  automation issue-block`、reason 付き)を追記します。
- **GitHub** — `intent-issue-blocked` label。`worker claim`/`worker complete`
  と同じ `IGitHubLabelMutator` seam 経由で適用します。raw な
  `gh ... edit --add-label`/`--remove-label` は決して許可されません。

`--clear` は両側を元に戻します: `state=queued` へ戻すことに加えて
**`blocked_by` を空にし**、その後 label を削除します。`blocked_by` を空に
するのは体裁上の話ではありません——`intent next-slice` の eligibility gate は
state に関係なく `blocked_by` が非空の item を不適格にするため、stale な
reason を残すと「clear された」はずの unit が永久に選択不能のままとなり、
drift を GitHub から queue-state へ移動させただけになります。

execution unit は**必須の positional 引数**であり、issue title から推測する
ことはありません。さらに queue item は**完全な `linked_issue`**(repo と
number の両方)を持っている必要があり、その両方が `--repo`/`--issue` と
一致しなければなりません: repo は canonical 化(URL / ssh / `.git` / 末尾
スラッシュの各形を `owner/repo` へ正規化)した上で case-insensitive に、
number は厳密に比較されます。linkage 欠落・repo 不一致・number 不一致は
いずれも拒否されます——linkage の欠落は「異議なし」ではなく証拠の欠如であり、
また issue #818 はほぼ全ての repository に存在するため number の一致だけでは
同一 issue の証明になりません。既に**異なる** reason で blocked になっている
unit も同様に、黙って上書きせず拒否します(先に clear してください)。

`runs.jsonl` が存在するのに読めない/parse できない場合も hard stop です:
audit 追記・queue-state 書き込みはもちろん、GitHub label の**読み取り前**に
拒否し、修復手段として `intent-cli automation runs-audit` を名指しします。
parse できない trail に対して transition すると、その trail をさらに壊した上、
「retry か新規追記か」を証拠ゼロで判断することになるためです。run log が
**存在しない**場合は従来どおり正当な first-event ケースです。上記の拒否は
いずれも run log / queue-state / GitHub への一切の interaction より前に
起きるため、3つの side すべてが byte 単位で無変更のまま残ります。

**書き込み順序は fail-loud かつ repairable です。** `runs.jsonl` の audit
event を先に追記し、`queue-state.json` の書き込みを後に行います
(`queue reprioritize` の規約と同じ)。これにより queue の変更が audit なしで
黙って成立することは決してありません。その上で両側は**独立に**収束されます:
各 side は自分自身の現在状態を確認し、既に目標状態であれば変更しません。
したがって partial failure 後に全く同じコマンドを再実行すると、まだ収束して
いない側だけが再試行され、完了済みの step が繰り返されることはありません。
queue 側の audit idempotency は run log 自身で判定します: 一致する event が
再利用される(二重追記されない)のは、それがその unit の最新 event であり
**かつ** queue-state がまだ追いついていない場合だけであり、これにより
「partial failure の再試行」と「block/unblock を一巡した後に同じ reason
テキストで再度 block した」ケースが区別されます。

デフォルトは dry-run: 両側で何が変わるかを報告するだけで、
`queue-state.json`、`runs.jsonl`、GitHub のいずれにも触れず、
`converged: true` を返すこともありません。`--reason` は apply 時には必須です
(`--clear` とは決して併用できません)——reason が記録されない blocked
transition は拒否されます。これは `queue reprioritize` の reason 要件を
踏襲しています。`intent-issue-blocked` は `intent-issue-in-progress` を
置き換えるのではなく共存します——worker は引き続き issue を所有しており、
単に現時点で作業を進められないだけです。

各 item は `kind`、`execution_unit`、`issue` および/または `pr`
（番号 + url）、`age_minutes`、`is_informational`、`recommended_action`
を報告します。`--stale-minutes` は、指定した閾値より新しい item を
除外します（デフォルトは `0` — すべてを age 付きで報告し、閾値は
呼び出し側が選ぶ）— これは 9 つすべての kind に一律に適用されます。
`claimed-but-silent`、`backlog-ready-idle`、`repair-stalled` は、そもそも
item が検討される前に、それぞれ自身の `--claimed-silent-minutes` /
`--backlog-idle-minutes` / `--repair-silent-minutes` 閾値でも追加で
ゲートされます（そのため `--stale-minutes` を上げるだけでは、いずれの
kind の item も自身の閾値より早く現れることはありません）。`age_minutes` は、
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

### REST GitHub read の equivalence と GraphQL-bound remainder (G674 — preview-through-1.x)

issue #1442 で測定した 6 つの GitHub-consulting surface は、完全な
field set の検証が済んだ issue-list projection だけを REST に切り替えます。
正確な endpoint は `gh api --paginate --slurp` で呼ぶ
`GET /repos/{owner}/{repo}/issues?state=open&labels=<...>` です。field の
対応は `number`、`title`、`html_url -> url`、`created_at -> createdAt`、
`body`、`updated_at -> updatedAt`、`labels[].name`、`state`（既存の大文字
語彙へ normalize）です。REST の `pull_request` marker は、従来の
`gh issue list` と同じ issue-only の結果を保つため adapter 内だけで使います。

surface ごとの dependency は明示します:

| surface | 検証済み REST read (`core`) | 残る GraphQL-bound read |
|---|---|---|
| `worker next-action` | `intent-target` issue list | open PR list: `closingIssuesReferences` |
| `host-loop-next-action` | open issue list | open PR list: `closingIssuesReferences` |
| `host-review-preflight` | `intent-target` と published issue list | open PR list: `closingIssuesReferences` |
| `reconcile` | published issue list | open PR list: `closingIssuesReferences` |
| `stalled-work` | open issue list | open/merged/closed PR list: `closingIssuesReferences` と `statusCheckRollup` union |
| `heartbeat` | stalled-work から継承する issue list | stalled-work から継承する PR read |

`closingIssuesReferences` にはこの slice で検証済みの field-complete な
REST equivalent がなく、check-runs endpoint だけでも GraphQL の
`CheckRun`/`StatusContext` union と同値だとは証明できません。したがって
body text や partial な check-runs response で近似せず、これらの read は
GraphQL-bound のままです。quota で止まったとき、G673 degraded state は
`dependency: graphql-bound` と `unverified_fields` を追加します。REST
failure は `core` resource と `dependency: rest-core` で分類します。これに
より caller output、mutation path、認証、`gh` binary を変えずに read ごとの
quota dependency を表せます。pagination は endpoint の順序付き page traversal
だけであり、cache、batch budget、retry、sleep、reset scheduler は追加しません。

### next-slice と backlog idle の shared publish-gate readiness (G670, preview-through-1.x)

`intent next-slice` と `automation stalled-work` は、`issue publish-flow` が
使うのと**同じ shared publish-gate readiness judgment**を参照します。
gate の横に「placeholder らしい」という別の判定を複製しません。

- `github-body.md` の required section が欠けている packet、または
  `Related Links` が placeholder のみの packet は、next-slice の
  issue-cut-ready 候補から除外されます。既存の `notes` channel に
  execution unit と gate の cause（欠けている section や TODO のみの
  Related Links 拒否など）が明示され、黙って消えません。
- `backlog-ready-idle` は同じ除外を既存の `excluded[]` に
  `reason: contract-incomplete` として出します。その unit に対する
  publish command は出しません。別の完成済み candidate は、
  `--backlog-idle-minutes` の通常の threshold を満たせば G544 の item として
  従来どおり報告されます。
- packet を埋めるだけで十分です。次の read では同じ judgment が
  `issue-cut-ready` を返すため、unit は自動的に候補へ戻ります。marker、
  repair command、packet field の保守は不要です。
- G474 の lifecycle retirement/absorption/supersession は独立した除外のまま、
  G544 の WIP と idle-time gating も不変です。これは preview-through-1.x の
  挙動修正であり、新しい stalled-work kind ではありません。

### 判断待ちの永続記録 (G596, G623)

進行を止める判断は、流れて消える通知ではなく state です。判断を担う party が
human operator、design、または別の recorded owner のどれであっても、次の明示的な
lifecycle surface を使います。

```text
intent-cli judgment-wait open --record <id> --domain <d> --team <t> --owner <owner> --blocking-reference <issue|pr|unit|release> --action-needed <action> --evidence <evidence> [--supersedes <id>] --write --format json
intent-cli judgment-wait resolve --record <id> --resolution-evidence <evidence> --write --format json
intent-cli judgment-wait supersede --record <id> --evidence <evidence> --write --format json
intent-cli judgment-wait query [--domain <d>] [--team <t>] --format json
```

進行が別の party の判断待ちで止まっており、かつ既存の clarification
mechanism（この仕組みでは変更しません）で扱うものではないとき、record を
open します。party は human operator でも design のような logical thread でも構いません。
特に design の ruling が必要な thread は、GitHub comment だけを投稿せず、この record を open
します。open した thread は owner、blocking reference、chat を再構成せず
実行できる具体的 action、establishing evidence を記録しなければなりません。
その record を記録済み owner に route し、後で evidence 付きの明示的 terminal
command を実行することも、その thread の責務です。

lifecycle は厳密に `open`、`resolved`、`superseded` の 3 つです。
superseded は回答済みではなく、`resolution_evidence` を持ちません。同じ義務が
再発した場合、古い record をまず supersede し、`--supersedes` でそれを参照する
新しい ID を open します。terminal な古い record を変更したり reopen したりは
しません。全 transition と evidence/timestamp は
`.intent-cli/operator-attention.json` に残り、共有 atomic な永続 writer で
publish されます。

store を write/transition するのはこれらの明示 command だけです。pane text、
`events.jsonl`、`intent-cli notify escalate`、prose/event heuristic は何かが
起きたことを通知できますが、record を open / resolve / supersede することは
ありません。domain 単独、team 単独、または両方で query すると、該当する全
record、現在 open の集合、open からの age が返ります。store が無い、または選択
scope に履歴が無ければ `check-not-completed`、malformed または unreadable なら
`cannot-determine` であり、
`no-attention-pending` には決してなりません。

`operator-attention` は 1.x line を通じて残る deprecated compatibility alias です。
`judgment-wait` と同じ結果を返し、replacement `judgment-wait` と removal `next-major` を持つ
structured な `deprecation_warning` field を追加します。新しい name の出力にはこの field はありません。
on-disk record は `.intent-cli/operator-attention.json` のままで identifier も変わりません。open record は既存の
`automation stalled-work` と `automation heartbeat` に `operator-attention-pending` として即時に現れます。
item は record を名指しし、recorded `required_actor` と `orchestrator_actionable: false` を持ちます。その
record だけが item の heartbeat は recorded owner の `route_to` と
`ROUTE TO <RECORDED OWNER>` を返し、reader-facing item に owner と blocking reference を含めます。
orchestrator は義務を route しますが、別の party の判断を自分で clear
できるかのように扱ってはいけません。新しい watchdog、scheduler、timer、
polling loop、process launch、automatic open/resolve path は追加しません。

### session-layer モード: 4 スレッドがどの transport を使うか (G570)

`intent-cli session-layer show --domain <d> [--team <t>] [--format markdown|json]`
`intent-cli session-layer set --domain <d> [--team <t>] --mode agmsg|herdr-only [--dry-run|--write] [--format markdown|json]`

4 スレッドモデル(design / orchestrator / implementation / review)と、そのスレッド群が
会話する **session layer** は別の話です。2026-08-01 のオペレーター裁定により、後者は
固定ではなく**選択可能**になりました。

- **`herdr-only`（preferred — fewer dependencies）** — team agent 全員が 1 台に常駐する場合に
  優先する選択肢です。herdr が terminal controller になり、別立ての message bridge を動かしません。
- **`agmsg` + herdr（supported, not retired）** — team member が複数 machine に分散する場合、または
  既存の agmsg investment がある場合のサポート対象の選択肢です。記録が無いときは `agmsg` が既定値です。
- **primary で無限定なのは 4 スレッドモデル**であり、両モードで変わりません（G540 の裁定
  どおり）。どちらの transport も primary ではありません。
- **1 チーム 1 モード。** 1 つのチーム内で agmsg と herdr-only の配送を混在させることは
  fallback ではなく contract violation です。transport が 2 つあるということは「誰に何を
  伝えたか」の見え方が 2 つあるということです。

セマンティクス:

- **スコープ** — domain 単位で記録し、team が modeled されている場合は team 単位でも
  記録します。team 単位の記録が domain 全体の記録に優先します(より狭い言明だからです)。
- **既定** — 記録が無ければ `agmsg`。`show` は決して書きません。
- **永続化** — `.intent-cli/session-layer-mode.json`。書き込むのは
  `session-layer set --write` **のみ**です(G548 の系譜: durable state（永続状態）という
  正本となる定義は、正本のコマンド経由でのみ変更し、手編集はしない)。
- **冪等** — 同一スコープで既に有効なモードを再記録しても no-op で、transition も
  記録しません。セットアップスクリプトがモードを表明しても、trail が「決定の記録」から
  「実行の記録」に変質しません。
- **可逆＋trail** — 各エントリは全 transition(`from` / `to` / `at`)を保持します。
  agmsg へ戻すことは herdr-only へ切り替えることと同じくらい普通の操作であり、記録は
  その両方を示します。
- **fail-closed** — 未知のモードは記録せず拒否し、読めない記録は上書きせず拒否します。

**ルーティング。** 記録されたモードが `guide orchestrator-thread` の描画セクションを
選びます。

- `agmsg` では本スライス以前と**完全に同じ**描画になります(ルーティングは恒等写像で、
  モード概念が増えたことで実運用パスが動くことはありません);
- `herdr-only` では、完全に agmsg 固有の操作セクション(setup / 登録、receiver
  readiness、monitor / bridge 診断、agmsg reply contract、design-receiver 登録)が
  herdr-only 操作セクションへのポインタに置き換わります。その内容は **G571** で出荷され
  ます;
- モード非依存の canon は**両モードで**描画されます — supervision、isolation、liveness、
  wake contract、publish 権限、design↔orchestrator double-check ルール、依存計画、
  エスカレーション。これらはモデルの性質であり、transport では変わりません。

**適用範囲は、両描画の識別子を 1 行に持つ単一の表でセクション単位に宣言し、4 値です**(design 裁定、host main `fb1913c8`):
`agmsg-only` / `herdr-only` / `mode-independent` /
`mode-independent-with-transport-mechanics`。renderer は記録されたモードから
**セクション単位で**選択します。markdown と JSON の双方で行うため、フィールド利用者と
本文の読者が「何が適用されるか」で食い違うことはありません。

- **agmsg-only** セクションは、置換したものを列挙する *Session-layer switch checklist*
  セクション 1 つに**丸ごと置き換え**られます。注記を添えて残すのではなく、描画しません。
- **mode-independent** セクションは両モードでそのまま描画されます。
- **mode-independent-with-transport-mechanics** セクションは、両モードで拘束する canon を
  agmsg の mechanic で表現しているものです。セクションは**保持**し、**フラグメント単位で型付け**
  します(design 明確化 G570)。各フラグメントは `structural`(見出し・表の骨格・フェンス。
  決してルーティングしない)、`canon-descriptive`(仕組み・経緯・`agmsg run directory` の
  ような基盤の識別子。両モードでバイト同一)、`mode-independent-operative`(**両モードで**
  拘束する指示 — intent-cli / GitHub の手順や four-thread model の規則。これもバイト同一)、
  `transport-operative`(transport を操作する指示。herdr-only では pointer 化)のいずれかです。
  セクション単位のフラグでは両方を含むセクションを表現できず、ラベルの裏に命令が残るか、
  記述的 canon を削りすぎるかのどちらかになります。

  型付けは**導出ではなく宣言**です。guide が描画する非 structural なフラグメントはすべて、
  markdown と JSON それぞれについて `SessionLayerFragments` に逐語で列挙され、人間が割り当てた
  型を持ちます。参照は厳密一致で **fail closed** です — 宣言のないフラグメントが renderer に
  到達すると例外になるため、文を追加・改稿すると必ずテストが落ち、型付けの判断を求められます。
  以前の実装は命令語の手掛かり(cue)から型を推論していましたが、その失敗モードはスイート内部
  からは見えませんでした。cue の語彙から外れた言い回しの指示は description に分類されて
  herdr-only 出力に残り、テストは同じ分類器に答えを尋ねていたため「分類器が自分自身と一致する」
  ことしか確認できなかったからです。網羅性の guard は現在、**出力側**から独自の markdown 解釈で
  フラグメントを再導出し、production の分類器を参照せずに、各フラグメントがちょうど 1 つの宣言を
  消費することを要求します。

  宣言テキストは呼び出し側の入力を**衝突しない sentinel** として保持し、参照時に展開します。
  これにより 1 つの宣言があらゆる呼び出し形態をカバーします。展開は**前方向のみ**です。逆方向の
  正規化(描画済みの値を placeholder に書き戻す)は文書を壊します — `--delivery-mode` の値
  `monitor` のような短い値は通常の散文にも現れ、また guide には読者が埋めるための
  `<domain>` などの literal な placeholder がコマンド雛形として正当に含まれるからです。

  文書タイトルは**1 つの宣言された identity** で、モードごとに明示的な rendering を持ちます
  (`SessionLayerSections.DocumentTitle`)。renderer はどちらの文字列も自前で保持しません。
  2 つの兄弟宣言では両タイトルが無関係な surface としてモデル化され、guard を緑のまま片方だけ
  改稿できてしまいました。

  **宣言すること**と**正しく型付けすること**は別です。最初の実装は型を**構成的に**割り当てて
  いました(transport の mechanic を含まないものはすべて `canon-descriptive`)。その結果
  `canon-descriptive` 454 件に対し `mode-independent-operative` は 14 件となり、「まず pane を
  READ する」「他チームの workspace を決して削除しない」「label 遷移はすべて intent-cli 経由」と
  いった**拘束力のある義務**が散文として分類されていました。現在は非 structural なフラグメントを
  すべて個別に判定し、transport によらず拘束する義務は `mode-independent-operative`、
  `canon-descriptive` は仕組み・経緯・基盤の識別子のみとしています。

  記述的な節と operative な節が独立して適用される形で**混在**するフラグメントは、**個別に型付け
  された clause** として宣言し、連結すると元の本文に厳密に一致します。isolation の表の行は 1 行に
  基盤の識別子と拘束力のある所有ルールを併せ持つため、行単位の単一の型ではルールを散文として
  扱うか、識別子を削り落とすかのどちらかになってしまいます。

  型付けは**文(sentence)粒度で全称的**です。すべての宣言は clause のリストであり、各 clause は
  1 つの文、または文間・表セル間の scaffolding であり、連結すると元の本文に厳密一致します。
  フラグメント単位の型付けと数行の表分割だけでは不十分でした — 仕組みと拘束力ある義務が混在する
  複数文フラグメントは依然として 1 つの判定を共有し、命令語リストによる部分一致 fixture では
  それを証明できないからです。**両方の renderer が clause リストを consume** するため、routing も
  ラベル付けも型を決めた粒度で作用します。canon と transport 手順が混在する行は canon を保持し、
  その手順だけを pointer 化します。

  agmsg example のラベルは、**修飾する記述的 clause を名指し(引用)します**。より弱い 3 つの
  スコープをいずれも試し、いずれも過剰適用でした — セクション単位の banner はその中の義務すべてを
  例示扱いにし、連続 run の banner は直後の指示まで覆い、行単位のラベルは同じ行の operative な文まで
  覆いました。自らのスコープを引用するラベルだけが、その範囲を越えられません。JSON の context も
  同様に、プロパティではなく**記述的 clause を 1 対 1 で列挙**します。

  ラベルの文面は**位置に依存しない**表現とし、保留されたラベルは対象文の所在を明示します。以前は
  対象文が「下にある」と述べていましたが、保留によりそれは偽になりました — 引用がどれほど厳密でも、
  ラベルが保証できない方向を述べることは読者への誤った指示です。

  markdown の**表の内部**では、ラベルを保留し、表が完結してから出力します。行と行の間に
  blockquote と空行を挟むと GFM の表はそこで終端し、それ以降の行が表の一部でなくなるためです —
  ラベルの配置は、それが置かれる構造に譲る必要があります。移動できるのは、ラベルが**自らのスコープを
  引用している**からです。自己スコープ型のラベルは、覆う行から離れて読まれても厳密なままです。

pointer-only テキストは G570 の**ルーティングのメタデータ**です。「何が適用されないか」と
「対応物がどこで出荷されるか」を述べ、**代わりに何を実行するかは述べません** — 具体的な
herdr 手順は G571 の内容であり、ここでは禁止されているからです。

部分文字列/トークン置換は正しさの機構としては採用せず**却下**されました。弱すぎ(「agmsg の
delegation を待つ」のような実行指示は mechanic トークンを含まない)、かつ強すぎる(初期案は
agmsg に言及しているという理由だけで timer-loop の canon を削除した)からです。適用範囲は
セクションの**主題**の性質なので、`SessionLayerSections` で 1 度だけ宣言し、レビュー可能に
しています。

setup intake は「トークン置換」ではなく**モード固有**です。herdr-only ではオブジェクトに
`agmsg_commands` も agmsg 形状の `role_prompts` も `team` / `delivery_mode` 入力も**存在せず**、
headline も登録手順を指示しません。両モードでバイト同一の**記述的**な agmsg 内容(モデルの
仕組みや経緯)には、明示的な「agmsg 例」ラベルを直前に付し、読者が読んだその場で「例示」と
「指示」を区別できるようにしています。順序付きリスト内で置換されたステップは**自身の番号を
保持**するので、playbook が 1, 2, 3, 5 と読めることはありません。

**fail-closed な state。** **存在するが不正な**記録は「不在」ではありません。壊れた
ファイル、未知のモード、あるいは現在のモードが自身の transition trail と食い違う記録
(`session-layer set --write` が書いたものではない証拠)がある場合、モード依存の全
サーフェスは `session-layer-mode-unreadable` という名前付きエラーで失敗し、guidance を
**一切描画しません**。既定値で描画すれば、そのチームが走らせていないかもしれない
transport の手順を読者に渡すことになるからです。`set` はそのような記録を黙って修復せず、
上書きを拒否します。

`guide model` と `guide onboarding` は両モードを説明し、onboarding は transport 固有の
手順より**前に**モードを読ませるので、新規 agent が誤ったセットアップに従うことは
ありません。

### intent-tree の共進化: 実施した knowledge write-back を記録する (G564)

`intent-cli automation knowledge-writeback-record --execution-unit <u> --commit <host-commit-sha> [--target <path>]... [--note <text>] [--dry-run|--write] [--format json|markdown]`

は、packet が**宣言した** write-back を実施したことを、host commit を証跡として
記録します。上記 `knowledge-writeback-pending` を消す側の半分です。

- **記録するだけ。** write-back 自体は design の host 側の行為です。このコマンドは
  intent の content を書かず、intent tree を変更しません(G300)。artifact の手編集は
  正規の経路ではありません。
- **artifact。** `.intent-cli/knowledge-writebacks/<unit>/record.json`(execution
  unit ごとに 1 つ): `artifact_kind` / `execution_unit` / `host_commit` /
  `recorded_at` / `targets` / `note`。
- **冪等。** 同一 commit(大文字小文字は区別しない)の再記録は no-op success —
  `already_recorded: true`、`applied: false`、ファイルはバイト単位で不変です。
  closeout をリトライしても `recorded_at` が実際のイベントからずれることはありません。
- **fail closed。** `.intent-cli/issues/<u>/packet.yaml` を持たない execution unit は
  UNKNOWN として拒否。7〜40 文字の 16 進 SHA でない証跡は拒否。**異なる** commit を
  持つ既存レコードは上書きせず拒否(証跡を黙って差し替えることは、監査証跡を監査
  証跡でなくすことです)。読めない既存レコードも上書きせず拒否します。
- **execution unit は canonical な識別子であり、ファイルシステムに触れる前に検証
  します。** 使えるのは ASCII 英数字 / `-` / `_` / `.` のみで、先頭 `.` と `..` を
  含むものは禁止です。これにより path separator、絶対パス、ドライブ文字や ADS の
  コロン、dot-segment、空白、制御文字が構造的に排除されます。導出した 2 つのパス
  (packet と record)は、その後さらに `.intent-cli/issues` と
  `.intent-cli/knowledge-writebacks` の配下にあることを検査します。同じ検証は検出側が
  `runs.jsonl` から読む execution unit にも適用されます — runs log は信頼済みの識別子
  ではなくデータだからです。そこに canonical でない unit があれば `excluded[]` に
  報告し、そこからパスを導出しません。
- **レコードは、それが名指しする unit に対してのみ証跡です。** 消費のたびに(検出側の
  クリア経路でも、記録側の冪等/拒否判定でも)、レコードに埋め込まれた
  `execution_unit` が保存先の unit と一致し、`host_commit` が SHA 形状であることを
  要求します。`…/G564/record.json` にありながら `execution_unit: G999` を宣言する
  レコードや、commit でない証跡を持つレコードは、`knowledge-writeback-pending` を
  消さず、パス付きで「読めない」として報告されます。
- **既定は `--dry-run`。** 永続化には `--write` が必要です。
- 何も required と宣言していない unit への記録も成功しますが、結果に警告が付きます。
  そこで tree が本当に何かを負っていたなら、packet の宣言が不誠実だったということで、
  直すべき欠陥はそちらです。

この責務は本リファレンスだけでなく guide 側にも書かれています: design thread の
playbook(`guide orchestrator-thread`)、packet 作成時のプロンプト
(`guide workflow task packet-draft`)、closeout プロンプト(`guide closeout run` の
Stage 5b)がいずれも同じ文言を単一ソースから共有しており、互いに drift できません。
orchestrator の closeout レポートは、packet が宣言した write-back と、その各々が
recorded か pending かを列挙します — packet metadata の read-only な伝播であり、
host の変更は行いません。

### closeout debt としての guide reachability (G645, preview-through-1.x)

keyword-to-guide standard は運用上の規約です。thread に keyword を渡せば、その thread は named guide に
到達し、surface を理解して action できなければなりません。packet は role-facing な各 surface について
guide_reachability の route (guide_surface / role / target_surface) を 1 つずつ宣言するか、
no_role_facing_surface: true を明示します。declaration の欠落は explicit no-surface と同じではなく、
intent-cli は route を推測せず guide wording も判定しません。

intent-cli automation guide-reachability-record --execution-unit <u> --commit <host-commit-sha>
[--note <text>] [--dry-run|--write] [--format json|markdown] は named guide route を更新した host commit を
記録します。artifact は .intent-cli/guide-reachability/<u>/record.json です。record ができるまで、closeout
済みの declared route は automation stalled-work の guide-reachability-pending として execution unit、
guide surface、role を示します。explicit no-surface なら debt はありません。これは 1.0 promise の対象外の
preview surface であり、merge gate ではなく closeout debt です。

recorder は evidence-only です。guide content を書かず、intent tree を変更せず、guide の良し悪しを決めません。
同一 commit には冪等で、競合または unreadable な証跡は拒否し、packet declaration がないまたは malformed の
場合は fail closed します。

### role-scoped closeout record (G698)

orchestration は mechanical closeout を担当し、design は intent-tree / ADR / diagram / docs の lesson と guide
update を担当します。運用で使う exact command syntax は次の通りです。

```text
intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role design [--target <path>]... --write
intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit <host-sha> --role orchestration [--target <path>]... --write
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --role design --write
intent-cli automation guide-reachability-record --execution-unit <unit> --commit <host-sha> --role orchestration --write
intent-cli automation stalled-work --domain <domain> --repo <owner/repo> --role <design|orchestration> --format json
```

knowledge record は `.intent-cli/knowledge-writebacks/<unit>/records/<role>.json`、guide record は
`.intent-cli/guide-reachability/<unit>/records/<role>.json` に異なる role ごとに併存し、read/result surface は
全 role を列挙します。同じ role の duplicate や conflicting commit は拒否します。既存の legacy `record.json` は
unattributed として readable のまま保持し、自動 migration / rewrite は行いません。role 無指定の scan は
compatibility のため valid な record を受け入れますが、`stalled-work --role` は指定 role だけを clear します。
`guide closeout run` と `guide orchestrator-thread` はこの分担と exact command を表示し、metadata-free の bare
directory から実行できます。

### closeout の runs 書込み事実と runs-only 修復 (G708)

`intent-cli closeout pr --pr <n> --repo <owner/repo> --pr-merged true
--write --format json` は、実際の `runs.jsonl` 書込みだけを報告します。skip の場合は
`runs_events` が空の list、`runs_appended: false`、named な `runs_skip_reason` になり、append の場合は
`runs_appended: true` とその呼出しが追加した行だけを示します。JSON と Markdown は同じ内容を示します。
completed の queue item に対応する closeout event が無ければ
`queue-completed-missing-closeout-runs-events` として報告し、自動修復は行いません。

明示的な修復 command は次の通りです:

```text
intent-cli closeout pr --pr <n> --repo <owner/repo> --pr-merged true --repair-runs --write --format json
```

欠落した closeout event だけを追加し、queue item を再び completed にせず、queue-state や他の record を書きません。
queue-state の bytes は同一に保たれ、二回目は `runs_skip_reason: runs-events-already-present` を示す no-op になります。

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
（不安定な GitHub 状態の再チェックではなく）永続状態です。`--write`
を使った際、queue-state は既に retired だが直前の partial write による
`runs.jsonl` イベントが欠落している場合、再実行はその欠落したステップ
だけを(GitHub 呼び出しゼロで)完了させます — 永久に黙って失われることは
ありません。packet ディレクトリと issue のコメント履歴には一切触れず、
削除もしません。

retired になった item は自動的に WIP gating から外れます:
`automation host-review-preflight` の進行中項目スキャンは OPEN で
`intent-target` ラベル付きの GitHub issue/PR をライブに読むため、close
されて label が外れた issue は単にそこから消えるだけです — 別途コード
パスは不要です。

**queue で blocked のユニットは WIP gate から除外されます(G553)。**
work が WIP から外れる経路は retirement だけではなく、*parked*(意図的に脇へ
置く)ももう 1 つの経路です。queue item が **converged blocked state** —
queue `state=blocked` **かつ** `blocked_by` が非空 — にある issue は
`in_flight_issues` にカウントされなくなり、進行中で blocked 状態のものだけに
なった時点で next-slice candidate は `skip-next-slice-due-to-wip` から
`candidate-ready` に切り替わります。blocked のユニットは設計上 parked で
あり、unblock されるまで進行できません。それをカウントすることは、
オペレーターが意図的に work を脇に置いたまさにそのときに publish を
枯渇させます。field finding(sekiban-as-a-service、2026-07-26、0.5.0 上):
gate が issue #1783 を挙げて publish を抑止しましたが、そのユニット
SKS-G818 は claim を保持したままのサポート対象の block transition で
parked されていました — G545 は blocked ユニットを `claimed-but-silent`
から除外しましたが、この gate はカバーされていませんでした。

- **convergence は必須で、かつ two-sided です。** `state=blocked` なのに
  `blocked_by` が空、または `state=blocked` でない item に `blocked_by` の
  理由がある状態は、G545 の言う **drift** であって exemption ではありません。
  half-converged な item はカウントされ続け(fail-closed)、state/reason の
  不一致は修復コマンドを名指しする warning として報告されます。
- **exemption が silent になることはありません。** 除外された各ユニットは
  新しい `wip_exempt_blocked_units` diagnostics フィールドに execution unit・
  issue 番号・`blocked_by` の理由つきで現れます(JSON / text 両方の出力)。
- **linkage は queue item 自身の `linked_issue`**(repo + number)です —
  `issue publish-flow` が書く canonical な記録であり、title からの推測では
  ありません。queue item に紐付けられない issue は exempt されず、別 repo を
  指す `linked_issue` がこの repo の issue を免除することもありません。
- **読めない host state では fail-closed。** queue-state が存在しない場合は
  何も exempt せず(G553 以前の挙動そのまま)、パースできない場合も何も
  exempt せず warning を出します。unblock した場合は次の呼び出しから即座に
  カウントが戻ります。
- **peer surface**: `intent next-slice` は元々 `active`/`review`/`fixing` の
  item だけを WIP として数えており、blocked はカウントしていません — この
  ルールの divergent copy は不要でしたし、追加もしていません。

G553 で変更しないもの: block/clear の transition と convergence rule(G545 が
所有)、非 blocked ユニットの WIP-cap セマンティクス、そして
`automation stalled-work`(G545 が既に blocked ユニットを除外済み)。

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

### `issue publish-flow` の idempotent rerun が 3 つの永続 artifact すべてを独立に検証・復元する (G536)

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
両方を今や支えています。** この analyzer は 3 つの永続 artifact —
`queue-state.json` の `linked_issue`、`publish.yaml` の `issue-created`
record、`runs.jsonl` 内のすべての canonical `issue-created` event — を
独立に parse し、単一の canonical issue identity を解決するか、fail
closed します。両方のコマンドは、同じ永続状態の形状に対して
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
rerun が同一の永続状態に対して独立に検出・復元するものとを、
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

**G544 review repair — all-packet fallback も同じ dependency/blocked-by
gate を維持します。** primary の `queued`-ordered loop が eligible な
candidate を一つも見つけられなかった場合、`next-slice` は
`.intent-cli/issues/*` 配下のすべての packet directory を再列挙する
fallback へ移ります(queue-state に一切 entry を持たない runtime-created
packet をカバーするため)。この fallback は、primary loop がある
queue-known な unit を reject するのに使ったのと同じ dependency/
blocked-by gate を再適用しておらず、その unit を `issue-cut-ready` として
無条件に復活させてしまっていました。fallback は現在、queue-state が
`Queued` として追跡しているすべての unit に対して同一の gate を適用
します——queue-state に entry が無い unit(gate する対象が無い)は
影響を受けません。この問題は、G544 の `backlog-ready-idle` 検出が
この同じ selector が誤って `issue-cut-ready` を報告しないことに依存
していたために顕在化しました。

`QueueItem.Priority` は schema level では引き続き単なる、検証
されない `string` です(変更なし)——`queue reprioritize` だけが
それを正規化・検証します(`high`/`normal`/`low`、
case-insensitive)。`next-slice` の ranking function は、認識できない
値や欠けている値をすべて `normal` として扱い、error にはしません。
そのため、手作業で書かれた、あるいは historical な `queue-state.json`
ファイルがこの field によって fail closed することはありません。

**Legacy / out-of-enum な priority 値とその ordering rule(G543)。**
Field observation、2026-07-20: host の `queue-state.json`(1467 items)
は `high` 1405、`medium` 59、`normal` 3 という分布であり、`medium` は
documented された `high|normal|low` enum に含まれません。documented
enum 自体は `medium` などの legacy 値を含むように拡張**しません**——
その代わり、既存の out-of-enum なデータがどう振る舞うかを正確に定義し、
実データに対する selector の挙動が undefined にならないようにします:

- **priority が何のためのものか**: すでに **eligible** な candidate
  だけを order します(上記のとおり、すべての gate が引き続き優先され
  ます)。host のように 1467 items 中 1405 items が `high` である場合、
  priority-first selection は `high` bucket 内では実質的に authoring
  order に退化します——これは欠陥ではなく、ほぼすべての item が同じ
  priority class を共有しているときにこの機構が生む自然な形です。
- **すべての値(legacy 値を含む)に対する ordering rule**: `high` が
  最初、`low` が最後にランクされ、**それ以外の値——欠けている値、空の
  値、`medium` のような out-of-enum/legacy な文字列すべて——は明示的な
  `normal` と全く同じにランクされ**、`high` と `low` の間に位置します。
  これは total かつ deterministic です——selector の ordering position が
  undefined になる priority 値は存在しません。
  `QueuePriorityClassification.Rank`(`IntentSystem.Cli.Commands` 内)が
  唯一の shared 実装であり、`next-slice` の ordering と(後述の)drift
  report の両方がこれを使うため、この 2 つの surface が食い違うことは
  ありません。リテラルの `"medium"` item を含む regression fixture が
  この位置を証明しています。
- **Migration recipe(新規 command は不要)**: `queue reprioritize
  <execution-unit> --priority <high|normal|low> --reason <text> --write`
  は、legacy 値からの canonical な migration path としてすでに機能して
  います——検証されるのは *requested* 値だけで、documented enum に
  対して検証されます。*既存の* 値は validation 無しに読み取られ、
  report され(`old_priority`)、比較されます。そのため `medium`(他の
  legacy 値も同様)にある item は、`queue-state.json` を hand-edit する
  ことなく documented な値へ移行できます。下記で説明する fail-closed・
  audited な `priority-changed` runs event はそのまま適用されます。
- **Drift visibility**: **`intent-cli queue priority-drift [--format
  json|markdown]`** は新しい read-only な report です——
  `queue-state.json` や `runs.jsonl` を一切 mutate しません——存在する
  distinct な priority 値ごとの item count を一覧表示し、常に
  `high`/`normal`/`low` を(count が 0 でも)含めて report の形を安定
  させ、documented enum の外にある値を flag します(`has_drift: true`、
  `out_of_enum_values: [...]`)。out-of-enum な値は count の降順で
  order され、tie は alphabetically に解決されます。これにより、
  59 item の `medium` case を手書き script 無しに可視化できます。
- **Silent な書き換えの禁止**: 無関係な操作の side effect として
  `priority` を mutate する command はありません——例えば `queue
  transition` は `state`/`blocked_by` を変更するために `QueueState`
  全体を re-serialize しますが、同じ item に既存の `medium` 値があれば
  byte-for-byte で変更されず残ります。

**Review repair — `queue reprioritize --write` は fail-closed かつ
repairable な write 順序を使います。** `queue-state.json` を必須の
`priority-changed` runs event の追記より先に書き込むと、追記 step が
その後失敗した場合に、audit record の無い永続化された priority mutation
が残ってしまう可能性がありました。順序は逆にされています——runs
event を**先に**追記し、`queue-state.json` は**後で**書き込みます:

- event の追記が失敗した場合、`queue-state.json` は一切触れられません
  ——永続化された変更は何も起きておらず、単純な retry がまっさらな状態
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
今や、何かの fingerprint ではなく、永続的かつ injective な
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
  永続的に記録された sequence の「次」の整数を厳密に消費し、一度
  消費されると二度と「次」にはなりません——**`queue-state.json` の
  他のすべての field が後で byte-identical な content に戻っても
  関係ありません**。counter 自身がその同じ永続的な content の一部
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
  に `PriorityRevision >= 0` を検証し、`checked` arithmetic で
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
- この slice の Out of Scope(完全な境界は G531 issue を参照):
  semantic/embedding ベースのマッチング、あらゆる blocking/gating の
  挙動、reviewer guidance や orchestrator delegation preflight への
  組み込み、そしてどの domain tree への annotation も — このコマンドは
  読み取りと報告のみを行います。

---

### セーフティネットの再配置: design-thread watchdog を推奨に、外部 OS スケジューラを retire(G539)

G526 の外部 cron/launchd heartbeat 推奨は、**5 日間連続してすべての実行が
サイレントに失敗** しました(2026-07-15..07-20)— ラッパーの `gh`/agmsg 認証は
ログイン keychain に存在し、cron ジョブはそこにアクセスできないため、認証情報の
ステップを一度も通過できませんでした。2026-07-20 の 105 分のスタール
(G538 / PR #1179)は、`automation stalled-work` が正しく検知した
(`pr-created-not-reviewing, age=105m`)にもかかわらず回復されず、人間による
ping だけがそれを表面化させました。`intent-cli guide orchestrator-thread` は
これに応じて再配置されました:

- **design-thread watchdog(推奨されるデフォルト)** — **design** スレッドから
  実行する **30 分クラス** の間隔の watchdog loop: `intent-cli automation
  heartbeat --domain <domain> --repo <owner/repo> --team <team> --format json` を呼び出し、
  返された closed `verdict` が actionable の場合は `message_body` を使って orchestrator へ
  最大 1 通の canonical な nudge を送ります — それ以外は完全に沈黙します。
  生きた、人間が監視しているエージェントセッションの内側で動作するため、
  見えない外部プロセスとは異なり、別途の credential/keychain セットアップも
  不要で、壊れた瞬間にオペレーターの画面上で可視化されます。既存の watchdog
  安全ルール(delegation を重複させない、permission プロンプトをクリアしない、
  進行中の作業をキャンセルしない、強制クローズしない、永続状態を
  手編集しない)と停止条件は逐語的に維持されます。
- **failure visibility は staleness とは異なります。** 沈黙は健全な
  `stale=false` の heartbeat 結果にのみ許されます。heartbeat コマンドの
  実行失敗や不正な/オブジェクトでない出力は、この wake の watchdog 自身の
  turn 出力で可視的に表面化させなければなりません — 決して黙って飲み込んだり、
  黙ってリトライしたりしません。沈黙した失敗こそが、このスライスが外部 OS
  スケジューラを retire する理由そのものだからです — その一方で、壊れた入力
  から agmsg の nudge を捏造・送信することは決してありません。実際に送信
  されるメッセージは、本物の `stale=true` 結果の場合だけです。
- **orchestrator-side の長間隔 automation(選択可能な alternative)** — 同じ
  `automation heartbeat` の呼び出しを、design スレッドではなく
  **orchestrator 自身のスレッド** の中で、30〜60 分クラスの間隔の長間隔
  automation(Codex automation または Claude 同一スレッド `/loop`)から
  直接実行します。トレードオフ: design-side(推奨)は、1 つの追加ホップ
  (design watchdog から orchestrator へ)の代償として orchestrator を厳密に
  loopless に保ちます。orchestrator-side はそのホップを取り除きますが、
  orchestrator 自身が定期ループを実行する必要があります — これは
  orchestrator-message モードが定常状態で避けるよう設計されているまさに
  そのパターンです。
- **外部 OS スケジューラの heartbeat は RETIRED です。** cron/launchd 推奨は
  (単に降格されるのではなく)完全に retire されました: credential-store
  access、invisible failure、agmsg モデルの完全に外側で動作すること、
  いずれも失格の理由です。`intent-cli automation heartbeat` /
  `automation stalled-work` 自体は **変更なし** であり、引き続き
  scheduler-agnostic です — cron を含む任意のスケジューラが引き続き
  呼び出せます — ガイドが外部 OS スケジューラをメカニズムとして推奨しなく
  なっただけです。5 分の in-session orchestrator fallback タイマー
  (legacy、discouraged)は意味が変わりません。

詳細: [エージェントメッセージオーケストレーション](12-agent-message-orchestration.md)。

---

### runs-log スキーマ監査・修復と、domain-scoped な publish-flow validation (G542)

フィールドインシデント、2026-07-20: G539(domain `intent-cli`)の publish が、
**sekiban-as-a-service** domain に属するレガシーな `runs.jsonl` の行によって
永続状態分析で **2 回** 拒否されました — 最初は `ts`/`by` が欠落した 1 行、
次に `execution_unit` が欠落した 16 行。G542 以前の validator
(`RunLogSerializer.DeserializeAll`)はファイル全体を 1 回の呼び出しでパースし、
domain に関わらずファイル中の **最初の** malformed な行で例外を投げるため、
1 つ修復するたびに次の違反行が現れるだけで、canonical な一括監査/修復サーフェスは
存在しませんでした。

**`intent-cli automation runs-audit [--repo <r>] [--domain <d>] [--write]
[--apply-inferred] --format json|markdown`** は、read-only をデフォルトとする
サーフェスで、**1 pass** で **すべての** malformed な行を報告します —
行番号、欠落している必須フィールド(`ts`、`event`、`execution_unit`、`by`)、
推定される owning domain、そして存在する場合は **レコード自身の内部から**
導出される修復値:

- **`ts`** ← レコード自身の `timestamp` フィールドから。
- **`execution_unit`** ← `skip-next-slice-due-to-wip` 行では `wip[0].eu` から、
  `pr-merged-closeout` 行では `stage1.eu` から。実際のレガシー行では判別子は
  もう一段深い位置にある: そうした行はいずれも `event` がリテラル文字列
  `wake-summary` であり、分岐はその代わりレコード自身の `status` フィールドで
  選択される。`event` が直接 `skip-next-slice-due-to-wip` /
  `pr-merged-closeout` である行(`wake-summary`/`status` のラッパーが無い形)も
  引き続き同じように解決される — direct-event 互換性は
  `wake-summary`/`status` 形式に置き換えられたのではなく、意図的に両方
  サポートされている。

これらが唯一の documented された **within-record** 導出です(design ruling、
2026-07-20)— 値はすでに別のキーの下でレコード内部に存在しているため、
canonical なキーへコピーすることは推測ではなく lossless な正規化です。
within-record のソースが **無い** フィールド(最も典型的には `by`)は常に
`non_derivable` として報告されます。report にはそれでも `inferred_suggestion`
(同じ `event` の妥当な peer 行の中での多数派 `by` 値と、その evidence。例:
"all 12 peer record(s) of event 'issue-created' use by=issue-publish-flow")
が含まれることがありますが、それは evidence であってレコードそのものでは
ありません — `runs.jsonl` は audit trail であり、レコードがそう言っていないのに
「このレコードは X によって authored された」と書き込むことは、存在しない fact を
記録することになります。

- **`--write`** は within-record な修復のみを適用し、修復ごとに 1 つの
  `runs-repair` audit event を追記し(行、修復されたフィールド、derivation
  class、source を記録)、ファイルの他のすべてのバイト — および修復対象の
  行自身の他のすべてのバイト — を変更しません(欠落しているキー/値のペアは、
  行の先頭の `{` の直後に挿入されるだけで、他は一切動きません)。
  `execution_unit` 自体が欠落しており within-record のソースが無い行は、
  `--write` の下では(その行の他の導出可能なフィールドも含めて)完全に拒否
  されます — `runs-repair` audit event を紐づけるための安全な unit が無く、
  "unknown" を捏造することはそれ自体が永続的な trail 上の推測になってしまう
  ためです。
- **`--apply-inferred`**(独立した明示的なフラグで、`--write` だけでは
  決して implied されず、`--write` なしで渡された場合は usage error として
  拒否されます)は、peer-convention の `inferred_suggestion` 値をさらに
  適用し、`derivation: inferred-peer-convention` を記録する **別の**
  `runs-repair` event として記録します — 2 つの derivation class は、
  たとえ同じ行を修復する場合でも、同じ audit event に混ぜられることは
  決してありません。
- パースできない行(有効な JSON でない、または JSON だがオブジェクトでない)は、
  すべての必須フィールドが欠落しているものとして報告され、どちらのモードでも
  決して修復されません。
- 何も malformed でなければ clean report + exit `0`。

**`issue publish-flow` の永続状態分析は domain-scoped になりました。**
共有される `PublishDurableArtifactAnalyzer`(G536)は、1 回の whole-file な
`DeserializeAll` 呼び出しの代わりに、今や `runs.jsonl` を **行単位** で
パースし、各 malformed な行の owning domain を `runs-audit` と同じ方法で
解決します(その unit 自身の `packet.yaml` の `domain:` フィールドが
ディスク上にまだ存在する場合はそれを使用。それ以外の場合は、
`intents/<domain>/automation/bindings.md` の `execution_unit_regex` に
一意に一致する候補 domain が 1 つだけあればそれを使用。それ以外の場合は、
行の `by` フィールド内の domain らしい prefix を、実在する domain
ディレクトリと突き合わせて corroborate)。**publish 対象と異なる** domain に
解決された malformed な行は、hard block ではなく **warning**(`runs-audit`
を名指しし、結果の `warnings` に表示)になります — publish は続行されます。
**同じ** domain に解決された行、または owning domain がまったく解決できない
行(他の誰かに属すると決して推測しない)は、以前と同様に **fail closed**
します — これはレガシーな行の blast radius を狭めるものであり、publish
対象の domain 自体の validation を弱めるものではありません。
`automation publish-recovery` は意図的にレガシーな whole-file の挙動のまま
残しています(このスライスのスコープ外)。`RunLogSerializer` /
RunEvent の必須フィールド契約は変更されていません。

### queue-state の書き込みは guard 経由: no-item-loss invariant と stale-base 再適用 (G548)

`queue-state.json` は multi-domain host において**全 domain が共有する 1 つの
ファイル**であり、複数の loop が別々の checkout から並行して書き込みます。
canonical な writer はいずれもファイル全体を deserialize し、メモリ上で変更し、
ファイル全体を再 serialize します — したがって read-modify-write の race は
単なる衝突ではなく、stale な in-memory copy がたまたま保持していなかったものを
**黙って消去**します。

**field incident、2026-07-23**(host commit `2ab082cf`): sekiban domain の
書き込みが 1 時間前に読んだ base から G841 の PR linkage を記録し、その間に
seed された intent-cli の G545 queue item を削除しました。エラーは一切出ず、
commit message は linkage 変更のみを主張していました。この loss は 4 日間
不可視のままとなり、その後 `closeout-plan host-metadata-blocked` として表面化し、
`pr-is-draft` の recovery gate と組み合わさって循環デッドロックになりました。
復旧には 3 つの canonical surface と operator の介入が必要でした
(host commit `c0897649`)。

現在は、すべての canonical mutation が 1 つの共有 guard
`QueueStatePersistence` 経由で書き込みます。この guard は
**`IntentSystem.Supervisor`** — queue-state の model と serializer を所有し、
`IntentSystem.Cli` と `IntentSystem.Drift` の*両方*が参照する唯一の assembly —
に置かれているため、「すべての canonical writer」は CLI 内だけでなく solution
全体の writer(drift service の corrective enqueue を含む)を意味します。
強制する内容は次の 3 点です:

1. **stale-base の検出と再適用。** caller が*読んだ*状態と、persist 時点で
   ディスク上に*実際にある*状態を比較します(同一の serializer round-trip を
   通すため、単なる書式の差異が並行書き込みと誤認されることはありません)。
   不一致の場合、caller の mutation — base と outgoing state の item 単位
   delta として自動導出されます — を stale copy ではなく**新しい**状態へ
   再適用し、再適用が行われたことを結果として報告します(不可視になりません)。
2. **no-item-loss invariant。** ディスク上に存在するのに outgoing state から
   欠落しており、かつ明示的な削除として指定されていない execution unit が
   1 つでもあれば、**書き込みを中止**します — 対象 unit を正確に名指しし、
   canonical な復旧手段(`queue-seed-from-packet` → 冪等な
   `issue publish-flow` 再実行 → `closeout-plan --write-recovered-linkage`)も
   併記します。ファイルは無変更のまま残ります。ディスク上の状態が*読めない*
   場合も、どちらの保証も確立できないため中止します。
3. **item-scoped な再適用。** 再適用される mutation が触れるのは、その delta が
   実際にカバーする unit と `updated_at` だけです。無関係な item はすべて、
   新しい状態から byte 単位で同一のまま、その順序も保って引き継がれます —
   したがって stale copy が持つ古い他 item の姿が、新しいものを上書きすることは
   ありません。

**明示的な削除は引き続き正当です。** retire (G525)、completed item の
lifecycle、および契約上その item を削除しうると宣言している操作は、対象 unit を
expected removal として渡します。invariant が対象とするのは**要求されていない**
loss のみであり、allow-list の entry はその unit だけを免除します。retire 自体は
entry 不要です — item を削除するのではなく `state=retired` へ書き換えるためです。

**再適用は報告され、決して silent になりません。** 書き込みが再適用された
canonical command は、その事実と再適用対象の execution unit を自身の出力に
記載します — 並行書き込みに対して黙って自己修復した writer は、その contention
について operator に何も伝えないためです。例えば `queue transition` は
`note: queue-state changed after it was read (a concurrent canonical write);
this transition was re-applied to the current state for <units> and no other
item was modified.` を出力します。

**唯一の raw-text writer。** `metadata update` は bounded controlled metadata
writer です: 所有していない field を書き換えないよう queue-state を raw JSON
として変更し、完全な `QueueItem` 契約を満たさない document も受け付けます。
この writer は `PersistRawJson` を使い、invariant を JSON から直接読んだ
`items[].execution_unit` で検査し(deserialize は一切しません)、base が clean
なら caller 自身のテキストをそのまま書き込みます。並行書き込みを実際に検出した場合の再適用も **JSON レベル**で行われます —
この writer が触れていない item は fresh document 自身の node としてそのまま
引き継がれるため、model が知らない field も両側で保持され、stale copy が持つ
他 item の古い姿が新しいものを上書きすることはありません。

`metadata update` は bounded **linkage** writer — 2ab082cf incident における
writer B の役割そのもの — であるため、この経路での再適用は自身の result に
記録されます: JSON では `queue_state_reapplied` /
`queue_state_reapplied_execution_units`、text 出力では `queue_state_reapplied:`
ブロックです。

**共有 host における multi-writer の期待。** canonical writer の並行実行は
サポートされ、想定されています: 競争に負けた writer は拒否されるのではなく修復
(再適用)されるため、どの loop も他をシリアライズして待つ必要はありません。
サポート**されない**のは guard を迂回する writer です — `queue-state.json` の
手編集や、新規コマンドが直接 `File.WriteAllText` を呼ぶことです。source レベルの
fixture (`QueueStateWriterCoverageTests`) が、`src/` 配下のどこかに guard を
迂回する writer が追加された場合にファイル名と行番号付きで失敗するため、
all-writers の主張が再び目視確認頼みで退行することはありません。意図的に
スコープ外かつ本スライスで不変なもの: per-domain の queue file 分割(将来の設計
判断であり、本スライス後の再発が escalation criterion)、file-locking daemon、
プロセス間 mutex、git レベルの merge 戦略 — 2ab082cf の loss は
fast-forward-clean な履歴の内側で起きたため、防御は commit に到達する前の
writer 層に置く必要があります。

---

### クロスプラットフォーム agent skill: 単一の埋め込みソースと `intent-cli skill` (G559)

Claude Code / Codex / Copilot はいずれも**同じ** `SKILL.md` フォーマットを読みます。
異なるのは**設置場所だけ**です:

| Target | Scope | パス |
| --- | --- | --- |
| `claude` | `repo`(既定) / `user` | `<repo>/.claude/skills/<name>/SKILL.md`、`~/.claude/skills/<name>/SKILL.md` |
| `codex` | `user` | `~/.codex/skills/<name>/SKILL.md` |
| `copilot` | `repo` | `<repo>/.github/skills/<name>/SKILL.md` |

場所しか違わないからこそ、実際には手でコピーされます。そして手コピーした skill は
drift します。証拠はこのプロジェクト自身の host にありました。`host-review-loop`
skill が `~/.claude/skills` と `~/.codex/skills` に別々のコピーとして存在し、
すでに内容が乖離していたのです。同じ skill を名乗る 2 つのファイルがあり、
どちらも権威ではない状態は、skill が無いことより悪い失敗です。agent は古い方に
従い、ツールがもう実行しない workflow を報告します。

そこで skill は**単一ソース**として出荷します。リポジトリの
`skills/<name>/SKILL.md` を build 時に tool package へ埋め込みます。編集対象の
ファイルはちょうど 1 つ、記述対象のコードと同じバージョン管理下にあり、
リリースされた package と一緒に移動します。

```bash
intent-cli skill list                              # 全 target/scope とその状態
intent-cli skill install --target all              # 各プラットフォーム固有の場所へ install
intent-cli skill install --target claude --scope user
intent-cli skill diff --target claude              # 編集済みコピーの差分
```

`list` と `diff` は `--format text|json` を受け取ります。`install` は
`--target claude|codex|copilot|all`、`--scope user|repo`、`--skill <name>`、
`--force`、`--format` を受け取ります。

installed copy には `not-installed`、`current`、`stale-shipped`、
`locally-modified` の 4 状態があります。`stale-shipped` は正規化 content hash が package の
shipped-version lineage にある以前の entry と一致する状態です。`skill list` は
`update_available` を示し、`skill diff` は previous-shipped → current の比較だと明示します。
`skill install` は `--force` なしで更新し、`updated-stale` を報告します。lineage 外の content は
`locally-modified` で、後述する拒否保護を維持します。

`skills/<name>/SKILL.md` を変更するたびに、旧版と新版の正規化 SHA-256 identity を embedded
lineage へ追記しなければなりません。current embedded content が lineage に無いと guard が失敗する
ため、lineage duty を伴わない skill-content change は出荷できません。guide group の各 command は
known install location だけを bounded な local-filesystem check で確認します。stale-shipped copy が
あれば Markdown へ footer を 1 行、JSON へ exact な `skill install` command を含む
`skill_update_nudge` field を 1 つ追加します。not-installed / locally-modified は nudge せず、probe
failure は黙って無視され、guide output を block・変更しません。

installed official skill の変更は unsupported です。`locally-modified` は data-safety state であり
customization feature ではありません。installed-guide-wins rule により編集は意味的に inert です。
local behavior には別の own-named skill を作るか、upstream feedback を送ってください。

install 契約は引き続き次の 4 点です:

1. **プラットフォームが定義していない scope は、書かずに拒否する。** `codex` に対する
   `--scope repo` は、サポートされる scope を明示して失敗します。そのプラットフォームが
   決して読まない、それらしいディレクトリへ書くことは、install 成功に見えて
   install していないのと同じ挙動になります。
2. **編集済みコピーを黙って置き換えない。** install は正規化した installed hash を shipped
   lineage と比較します。lineage 外の content は locally-modified として `refused-drifted` を
   報告し、ファイルを 1 バイトも変えずに残し、**非ゼロで終了**します(script が検知できるように)。置き換えは
   `--force` による明示的な opt-in です。改行コードの違いは drift 扱いしないため、
   Windows checkout ですべての install が編集済みと報告されることはありません。
3. **最初の書き込み前に plan 全体を解決・検査する。** install は 2 フェーズで動きます。
   まず全 target/scope の組み合わせを検証し、全 destination のパスを解決し、全
   destination の状態を検査します。書き込みはその後です。不正な target/scope の
   組み合わせ**または** locally-modified destination が plan の**どこかに**1 つでもあれば、
   何も作らず何も変更せずに実行全体を中止します。書き込み可能だった destination は
   `skipped-plan-aborted` として報告され、plan に含まれていたが意図的に書かれなかった
   ことが分かるようになっています。検査と書き込みを 1 パスで行うのは「書き込み前の
   検証」ではありません。`--target all` の下では、後続の local edit が見つかった時点で
   先行する未 install の target はすでにディスク上にあり、「何も起きていない」と主張する
   exit code の裏で部分 install が残ります。同じ plan を最後まで成功させるのが
   `--force` です。
4. **すでに最新のものは書かない。** 一致するコピーは `already-current` を報告し、
   ファイルには触れません。

**skill 自体は dispatcher であって manual ではありません。** workflow を一切
再記述しません。持っているのは *「installed guide output wins」* というルールと、
ユーザーがやりたいことを、それに答える `intent-cli guide ...` コマンドへ対応づける
表だけです。これは意図的です。workflow を書き写した skill ファイルは、ツールに対して
古びていく 2 つ目の source of truth であり、それこそが一段上のレイヤーで起きる
drift 問題そのものだからです。guide surface は CLI と一緒に動きますが、
そこへのポインタは陳腐化しません。

`SkillCommandTests` は、使い捨ての repo root と使い捨ての user home に対する実際の
書き込みで挙動を証明します。拒否された install が operator の編集済みファイルを
そのまま残すこと、および埋め込みリソースが `skills/intent-cli/SKILL.md` と
バイト単位で一致すること(asset の同梱に失敗した build は、何も書かない installer を
出荷するのではなくテストで落ちる)を含みます。

---

### publish 優先順位を lifecycle として扱う (G561)

あるスライスを優先して先に流したいとき、正規のやり方は次の unit を**手で選ぶこと
でも**、先に流れるはずだった unit を**retire することでも**ありません。3 つの状態を
持つ lifecycle であり、それぞれに canonical なコマンドがあります。

1. **未 publish の unit を block する** — 待たせたい unit を `state=blocked` にし、
   理由を `blocked_by` に記録します。理由は通常、待ち先の execution unit です。
2. **block 中は selector がスキップする。** `intent next-slice` は blocked 状態の
   item と、`blocked_by` が空でない item の**両方**を除外します。両方が重要です。
   自分では unblock されたと主張しつつ古い理由を抱えたままの item は、事実上まだ
   blocked であり、永久に選ばれません。
3. **優先理由が消えたら clear する** — unit を再び選択可能にする exit です。

壊れていたのは手順 3 でした。`automation issue-block --clear` は何かに触れる前に
**完全な** `linked_issue` を要求します。これは正しい設計です(このコマンドは GitHub の
`intent-issue-blocked` label も収束させますし、issue #818 はほぼどのリポジトリにも
存在します)。しかし **publish 前**に block された unit には issue が存在せず、
`linked_issue` は null なので、この経路は動きません。素の `queue transition` は state
だけ動かして `blocked_by` を残すため、手順 2 が除外するまさに半収束状態の unit を
作ります。canonical な exit が存在せず、たった 1 つの unit を動かすために design thread が
one-off ruling を出す必要がありました(field incident 2026-07-31、G559 wake)。

pre-publish exit がこれを閉じます:

```bash
intent-cli automation issue-block <execution-unit> --clear --pre-publish --write
```

queue 側**のみ**を収束させ(`state=queued` と `blocked_by` の空化を 1 回の guarded write
で行い、run-log event に解除した wait reason を記録します)、GitHub とは一切やり取り
しません。触る issue が存在しないからです。label の読み取りも行わず、mutator も
生成しません。

fail closed するのは次の 2 ケースで、いずれも意図的です。

- **unit に `linked_issue` が少しでもある場合。** ルールは**完全な不在**です。
  pre-publish unit と認めるのは `linked_issue: null` のみです。publish 済み unit は
  two-sided path が所有し、そちらは label も収束させます。queue-only のショートカットを
  使えば label が取り残されます — two-sided コマンドが防ぐために存在する、まさにその
  drift です。**部分的な** linkage も同じ理由で拒否し、**空オブジェクト**
  `{repo: "", number: null}` も拒否します。オブジェクトが存在すること自体が「何かが
  linkage を記録した」証跡であり、「フィールドがたまたま空である」ことは
  「この unit は publish されていない」という主張とは別物だからです。空オブジェクトは
  two-sided path でも拒否されるため(完全な linkage を要求するため)、linkage を修復する
  までその item に exit はありません — これは意図的です。壊れた linkage は迂回する状態では
  なく修正すべきデータ欠陥であり、エラーメッセージがどちらの修復を行うべきかを示します。
- **`--repo` / `--issue` が渡された場合。** 無視ではなく拒否します。identifier を渡す
  呼び出し側はそれが処理されることを期待しますが、この経路は GitHub 側に触れません。
  黙って受け取れば、何も触っていないのに GitHub 側が収束したと誤認させます。

`--pre-publish` は **exit 専用**であり `--clear` を必須とします。publish 前に block する
こと自体はすでに動いていました。欠けていたのは戻り道です。

**このパターンの次回利用に design ruling は不要です。** block → selector のスキップ →
pre-publish clear → publish の 4 手順がすべて canonical コマンドで揃いました。

### `clarify open` が scaffold 済み packet で動く (G561)

design 上の blocking question は、packet がまだ draft で、誤った答えをまだ実装して
いない**早い段階**で記録するのが最も価値があります。G561 以前は、まさにその段階で
それが不可能でした。`clarify open` は `packet.yaml` を projection の完全な契約で
デシリアライズしており、`review_context_packet` セクションと 20 個の
`implementation_issue_packet` 必須フィールドを要求していたからです。
`intent-cli packet draft` が作る packet にはどちらもありません
(`implementation_issue_packet` / `intent_placement` / `knowledge_updates` /
`closeout_learning` を持ち、review context は packet ではなく `review-context.md` に
あります)。scaffold 直後の packet は mutation 前にすべて拒否され、G552 の
design-decision フローは、それが存在する理由そのものの瞬間に構造的に使えませんでした。

`clarify open` は clarification レコードが実際に含む事実だけを読むようになり、
strictness は意図的に非対称です。

- packet の `source_execution_unit` は**必須**で、queue item と一致しなければ
  なりません。誤った unit に対して clarification を記録することは記録しないことより
  悪いため、identity は決して緩めません。
- それ以外の packet フィールドは optional です。scaffold はまだ埋まっておらず、
  未記入の TODO は blocking question の記録を拒否する理由になりません。導出される
  question / reason のテキストはフィールド単位で degrade し、packet に存在しない
  詳細を断定するのではなく、欠落を明示します。
- 経路は**宣言**で決まります。宣言の中身がどうであるかでは決まりません。
  `review_context_packet` セクションを宣言している packet は「完全な projection
  packet である」と主張しているので、**変更していない** `ProjectionPacketSerializer`
  でデシリアライズされます(必須フィールド、型チェック、検証順序とメッセージ、失敗の
  仕方はすべて従来どおり)。その上で従来の cross-check もすべて実行されます。
  宣言はしているが壊れている packet(必須フィールド欠落、型違い、スカラー本体で宣言された
  セクション)は、従来とまったく同じ大きさで、mutation の前に失敗します。完全だと
  主張する packet に許容は一切適用しません。
- **その宣言が無い** packet — 完全性を主張したことがない `packet draft` の scaffold —
  だけが許容経路を通ります。
- `review-context.md` は同じ canonical parser が読むため、execution-unit の規則は
  不変です(`# Execution Unit` セクションが存在して不正な場合は従来どおり失敗します)。
  唯一の緩和は scaffold にまだ無い `# Deterministic Review Checks` セクションで、
  その不在が影響するのは導出 question テキストだけです — そしてそれは `--question` で
  上書きできます。

strict な projection serializer は**変更していません**。publish-flow と review は
完全な契約を要求して当然であり、そこを緩めれば不完全な packet が publish を通過して
しまいます。許容は `clarify open` にスコープされています。

---

## claim transaction の teardown (G738, G743)

`claim acquire`、`claim release`、`claim takeover` は OS の temp root 配下に一時 clone を
作ります。claim state の plain push 成功が transaction boundary であり、その一時 clone の
cleanup は boundary の後に実行されます。

- claim state が commit され push される前は、transaction または teardown の failure は
  command failure のままです。cleanup が未完了の claim を成功に変えることはありません。
  transaction 自体が boundary 前に失敗した場合は元の原因を保持し、finally の teardown failure
  がその原因を隠すことはありません。
- push process が nonzero を返しても remote ref が transaction を受理している場合があります。
  command は `origin` を fetch し、transaction commit と結果の claim state を照合します。
  完全一致だけを ownership fact として `acquired`、`released`、`taken-over` と
  `push_succeeded: true` で報告します。それ以外は既存の rejected-push/retry path のままで、
  claim を absent と誤報して duplicate acquire を誘発しません。

- push 成功後の cleanup は best-effort です。delete の 1 回ごとの待機は 250 ms に制限し、
  通常の failure には最大 3 回まで試行し、retry 間には 50 ms の backoff を置きます。
  timeout した試行は、worker が directory に触れている可能性がある間は retry しません。
  最悪時の追加待機は 850 ms (`3 × 250 ms + 2 × 50 ms`) で、実装の定数にも明記しています。
- cleanup で directory を削除できなかった場合、command は leftover path を含む warning を
  stderr に出力します。commit 済み claim の stdout result と exit code は変わりません。
  leftover は OS temp root 配下に残るため、OS の reaper が後で cleanup できます。

warning は実際の一時 path を差し込むと次の形です:

```text
warning: claim transaction committed successfully, but best-effort cleanup could not remove temporary directory '<path>' after 3 bounded attempt(s); the claim result and exit code are unchanged. The leftover path remains under the OS temp root.
```

これにより、commit 済み claim は operator と downstream の claim-gated flow から見え続け、
claim の background 実行や packet の手書きは不要です。

### remote の default branch を対象にする場合 (G747)

すべての claim transaction は `git ls-remote --symref origin HEAD` から remote の
`default branch` を解決します。transaction clone はその branch から始め、transaction
commit を `refs/heads/<resolved-default-branch>` へ明示的に push します。呼び出し元
checkout の current branch は claim の target にせず、refresh でも進めません。symref が
無い、曖昧、安全に扱えない、または query できない場合は fail closed とし、current branch
へは fallback しません。成功した transaction result には解決した `target_ref` が含まれます。

active record の actor と team が同じ acquire は意図した no-op です。result は scope が
すでに保持され、claim commit は不要 (`nothing to commit`) であることを示します。teardown
も失敗した場合は leftover path を含む warning を別に出し、その no-op の主原因を置き換え
ません。`--format json` の stdout は JSON 文書を 1 つだけ含み、cleanup warning は stderr
へ出力されるため、stdout を直接 JSON parser に渡せます。

## すべての claim outcome に対する transaction teardown (G771)

Cleanup はすべての claim outcome で best-effort です。acquire、release、takeover、
already-held、not-held、holder mismatch、retry の各 result は、一時 root の cleanup
も失敗した場合でも元の status、detail、ownership fields、exit code を保持します。
cleanup warning は別の evidence として leftover path を示し、pre-commit failure を
success に変えず、元の原因も隠しません。

各 transaction は一時 root の横に exclusive lease を作ります。後続の write command は
5 分の age grace period 後に、`intent-cli-claim-*` に一致する stale transaction root を
bounded に best-effort sweep します。候補は最大 32 件、sweep budget は 250 ms です。
active または読めない lease は保護し、削除しません。stale transaction root の delete
failure は warning evidence であり、claim result や exit code を変更しません。これにより
放置された root を回収しながら live な concurrent transaction を保護し、既存の 250 ms
per-attempt、3 回の cleanup contract は変更しません。

---

## バージョンフロー

リポジトリのバージョンポリシーは `eng/version.json` に記載されています。`stableVersion`
（最新の公開済み安定版）と `nextVersion`（準備中 / 開発中のライン）の単一の source of
truth です。G468 以降、ローカル `dotnet pack` のデフォルト `<Version>` はこのファイルから
導出されるため、ローカル pack / install は stale な csproj リテラルではなく開発中の
`nextVersion` を報告します:

```json
{
  "stableVersion": "<stableVersion>",
  "nextVersion": "<nextVersion>"
}
```

形だけをプレースホルダーで示しているのは意図的です: **実際の値は `eng/version.json`
から読んでください**。現在カット中のラインは下記の「次リリース準備」セクションを参照します。
ここに具体値の例を置くと、バージョンの組の 2 つ目のコピーができ、次の roll で必ず stale に
なります — それこそ G557/G560 が取り除こうとしている欠陥です。

| ステージ | バージョン形式 | 導出方法 |
| --- | --- | --- |
| ローカル pack / install | `<nextVersion>-<sha>-<G-unit>` | `eng/version.json` の `nextVersion`（G468） |
| Main CI preview | `<nextVersion>-preview.<run>.<attempt>` | `eng/version.json` の `nextVersion` |
| リリース候補（任意） | `<nextVersion>-rc.N` | タグ `v<nextVersion>-rc.N` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する |
| 安定版リリース | `<nextVersion>` | タグ `v<nextVersion>` の GitHub Release を publish すると `release.yml`（`on: release: published`）がトリガーされる。タグはバージョンを供給する（`-p:Version=<tag>` が優先） |
| リリース後の main ビルド | `<nextPatch>-preview.<run>.<attempt>` | `nextVersion` を `<nextPatch>` に roll した後 |

### リリース commit の到達性 gate(G726)

リリースタグを作成する前に、タグを付ける正確な commit が repository の
default branch に到達しているかを確認します。

```bash
./eng/release-reachability.sh \
  --commit <commit-or-tag> \
  --default-branch <repository-default-branch>
```

この gate は repository の default branch を解決し、
`git merge-base --is-ancestor <commit> <default-branch>` を実行します。
branch 名、pull request の状態、または現在の checkout であることを identity
evidence として使いません。到達している commit は成功し、
`ordinary_path=non-interactive` を出力します。通常の release path に prompt はありません。

到達していない commit は non-zero で拒否されます。出力は、**commit が
default branch に入るまで repository の default branch は released source を
含まない**ため、release build / publish を続行してはいけない、という結果を明示します。
operator は commit を default branch に入れてから gate を再実行します。曖昧な確認 prompt や
黙った override はありません。

`release.yml` workflow でも、publish された release tag の正確な target を build、upload、
package publish の各 job より前に再検査します。同じ default branch に対して、既存のすべての
`v*` tag に `--survey` を実行し、各 tag を reachable / unreachable / unresolved として出力します。
survey は診断専用で read-only です。tag や branch を作成、移動、削除、history rewrite しません。
通常の実行では次のような evidence が出ます。

```text
release-reachability: reachable ... ordinary_path=non-interactive
release-reachability: REFUSED ...
consequence: the repository default branch will not contain the released source until this commit lands; no release build or publish may proceed.
release-tag-survey: total=<n> reachable=<n> unreachable=<n> unresolved=<n> ...
```

### リリース後の version roll(G554) — 必須・即時

**GitHub Release を publish して検証した直後に、follow-up commit で
`eng/version.json` を roll してください**: `stableVersion` = 今リリースした
バージョン、`nextVersion` = 次の patch。これは任意の後片付けではなく、リリース
closeout の 1 ステップであり、飛ばすと preview チャンネルが壊れます。

```json
{
  "stableVersion": "<今リリースしたバージョン>",
  "nextVersion": "<次の patch>"
}
```

**なぜ必須か。** preview とローカル pack の build は `nextVersion` から導出されます。
`nextVersion` が今リリースしたばかりのバージョンを指したままだと、以降の preview は
すべて `<released>-preview.N` として build され、prerelease は SemVer 上そのリリース
バージョンより **下** にソートされます。field incident、2026-07-29: `v0.6.0` を publish
した後に roll を飛ばしたため preview は `0.6.0-preview.N` のまま build され続け、
`dotnet tool update` は新しい build を「より古い」として拒否し、手動 uninstall/install
以外に手段がありませんでした。直ちに roll すれば次の preview は `0.6.2-preview.N` となり
`0.6.1` より上にソートされるため、`dotnet tool update` が再び機能します。

#### roll 飛ばしの検出(G725)

既存の `intent-cli automation stalled-work --domain <d> --repo <r>`
surface がこの closeout を確認します。公開済みの stable GitHub Release を読み、policy の
記録済み stable line より新しい release がある場合（または next value が次の patch でない
場合）に、`version-roll-required` を出力します。finding には release version、期待する
`stableVersion`、期待する `nextVersion`、そして解消するための edit が含まれます。公開済み
release が無い場合と、すでに正しい組になっている場合はどちらも finding を出しません —
健康な host の empty result を release の取りこぼしと解釈してはいけません。この command は
read-only のままで、`eng/version.json` を編集したり release を publish したりしません。
operator が release-note / readiness の更新と一緒に follow-up edit を行い、child-main CI を
検証します。これは closeout ルールが要求する手順であり、検出だけがこの slice の scope です。

command を、`eng/version.json` を持たない configured host root から実行した場合は、domain の
`automation summary` binding が指定する target checkout（例: `submodules/intent-system`）に
従って、その場所の policy を読みます。別の sibling repository を推測したり checkout を
同期したりはせず、finding の edit path に configured child のファイルを明示します。

**リリース closeout チェックリスト**(roll はステップ 5 — ステップ 4 で止めないこと。
そして roll はステップ 7 まで終えて初めて完了です):

1. **release tag を作成、または GitHub Release を publish する前に、tag を付ける正確な
   commit に対して gate を実行する:**

   ```bash
   ./eng/release-reachability.sh \
     --commit <exact-commit-to-tag> \
     --default-branch <repository-default-branch>
   ```

   reachable の結果の場合だけ続行します。拒否された場合は tag を作成せず Release も
   publish せず、commit を default branch に入れてから gate を再実行します。
2. release tag を作成し、対象バージョンの GitHub Release を publish する(これが
   `release.yml` を発火させる)。ステップ 1 の pre-publish operator gate が、この
   tag/Release 作成の act を保護します。実際の release では、この act の後に
   `release.yml` が `release: published` を受け取り、package publication の step はその
   event を条件にしつつ reachability job の下流で実行されます。`workflow_dispatch` path は
   dry run であり publish しません。workflow 自体が tag/Release の作成を保護することはありません。
3. publish された成果物を検証する: NuGet ページ、release assets、`.sha256` チェックサム、
   `dotnet tool update` 後の `intent-cli --version`。
4. オペレーターと、待っている下流の利用者へ通知する。
5. **follow-up commit で `eng/version.json` を roll する** — `stableVersion` = リリース
   したバージョン、`nextVersion` = 次の patch — **さらに同じ commit で DRAFT の
   `docs/{en,ja}/release-notes-v<nextVersion>.md` stub を追加する。** G475 のガードは
   `nextVersion` が指すバージョンのノートの存在を要求するため、stub 無しでフィールドだけを
   動かす roll は、着地した瞬間に main を red にします。stub に changelog の中身は不要で、
   実際の内容は次の release-prep パケットが author します。
6. **同じ roll で「次リリース準備」セクションを新しいラインへ更新する。** このセクションは
   カット対象のリリースを名指しするため、`nextVersion` だけを動かす roll はセクションを
   前サイクルの記述のまま残します。ja/en 両方のミラーを更新してください。
7. **push 後に child main の CI が green であることを検証する。** roll は CI が green に
   なって初めて完了です: red な main は、それを継承するすべての無関係な PR をブロックする
   ため、roll した人は commit だけでなく結果まで責任を持ちます。

既存の preview 成果物は **遡って番号を振り直しません**。このルールはチャンネルを今後に
向けて修正するものです。

> **ステップ 5-7 がこの形である理由(G557, G560)。** roll の最初の実運用(commit `00936844`、
> `nextVersion` 0.6.1 → 0.6.2)はフィールドだけを動かし、4 つのチェックで main を red に
> しました: 3 つのテストがバージョンの組を値で固定しており、G475 のガードが
> `release-notes-v0.6.2.md` を要求したためです。無関係な PR が red な main を継承して
> 凍結され、hotfix が着地するまで解除されませんでした。assertion は `eng/version.json`
> から導出するようになり、正しい roll がそれらを壊すことはなくなりました。残りを塞ぐのが
> 残りの closeout ステップです: roll と同時に stub を作り、readiness を更新し、green を
> 確認してから完了とする。
>
> **2 件目のインシデント(G560、roll 0.6.2 → 0.6.3)。** 改訂したルールは機能しました —
> roll 後の CI チェックが、readiness セクションが前のラインを記述したままであることを
> 検出したのです。それまで通っていたのは、新しいバージョンがたまたま無関係な preview の
> 例に現れていたからにすぎませんでした。そこでセクションを更新すると、今度は *前サイクル* の
> readiness 見出しをリテラルで固定していた 4 つの transitional な test theory が落ちました。
> それが上記ステップ 6 と、下記のルールの理由です。

**release-prep のガイダンス: current-state のバージョンリテラルを新たに書かない。**
release-prep パケットが developer reference・README・その他「今のリポジトリの状態」を
記述するファイルに対するガードを書く/更新するときは、期待するバージョンを
`eng/version.json` から導出します — タイプした文字列からではありません。このルールを
破ったことで実運用のインシデントが 2 件起き、いずれも無関係な PR の CI を落としました:
1 件目はバージョンの組そのものを固定(G557)、2 件目は readiness セクションの見出しを
固定(G560)。リテラルが安全なのは成果物が **凍結** されている場合だけです:
リリース済みの `X` に対する `release-notes-v<X>.md` は今後変わらないため、その内容を
assert するのは構造的に安定です — 上記のようなインシデント記録も同様で、「今どうであるか」
ではなく「何が起きたか」を記述しています。

同じ理由で、上の version flow の例は具体的なバージョンの組ではなくプレースホルダーを
使っています: 現在のバージョンの 2 つ目のコピーは同期し続けるべき対象が 1 つ増えることを
意味し、しかも誰も見ていない roll でこそ stale になります。

### supervision state の shrink (G734)

supervision state は evidence なので、`.intent-cli/supervision` を手編集しないでください。
`stalls.jsonl` または `cycles.jsonl` が大きくなったときは、次の sanctioned command を使います。

```bash
intent-cli notify supervise shrink --domain <domain> --team <team> --dry-run --format json
intent-cli notify supervise shrink --domain <domain> --team <team> --write --format json
```

この command は supervision の append writer と同じ directory lock を取得し、2 つの JSONL を
検証してから、完全なファイルを同じ directory 内で atomic に置き換えます。そのため running
supervisor に対して実行しても、現在の append が終わってから shrink されるか、完全な replacement
へ append され、次の cycle も読み取り可能です。JSON result は観測した supervisor の PID、start
time、host と writer がまだ `running` かを出すので、write は standing supervisor が running の間に
実行してください。writer identity には process metadata から取得した start time か platform の
clock fallback かも明記されます。fallback の場合は同じ host の live PID による evidence だけを使い、
result にそのことを明示します。stopped writer に対する one-off も同じ lock と atomic replacement で保護される
ため、supported です。

既存の legacy stall record もその場で書き換えます。new file にだけ効く機能ではありません。
繰り返される registration definition は human-readable な `evidence-definitions.json` manifest に
1 回だけ保存し、各 record は `evidence_ref` を持ちます。CLI は read 時にその reference を sentence
へ解決するので、record から元の prose を取り除いても audit の意味を後から確認できます。JSON
result は literal bytes の削減、追加した reference bytes、record の net savings、その他の savings、
前後の bytes、前後の平均 bytes per record を測定して出します。

`cycles.jsonl` も、現在の event に invariant-text rewrite が不要でも、同じ atomic boundary の対象です。
この command は record を archive、discard、rotate しません。その事実を result と
`shrink-audit.jsonl` に明記します。`.intent-cli/runs/*.provider.jsonl` は別の provider-run state なので
scope 外です。`--dry-run` なら manifest、JSONL、audit を書き込まずに測定済み plan だけを確認できます。

### 次リリース準備(v0.27.1)

**POST-RELEASE ROLL / PLACEHOLDER ONLY。**

shipped stable line は `intent-cli 0.27.0-f43fbd1-G753`、source revision は
`f43fbd19f6e0cb7fa284ccd2f89d2932f63ca330` です。tracked な EN/JA
`release-notes-v0.27.0.md` は shipped evidence であり、この roll では
byte-identical のままです。この roll 後の policy pair は次のとおりです:

```json
{
  "stableVersion": "0.27.0",
  "nextVersion": "0.27.1"
}
```

`0.27.1` は replaceable placeholder だけで、次の real release number を
決定したものではありません。EN/JA に `release-notes-v0.27.1.md` の DRAFT
stub を追加します。それぞれの stub 自身が replaceable planning scaffold で
changelog ではないと明記します。tag、GitHub Release、package publish、
post-release roll、G725 detector の変更はこの作業に含めません。

version-flow の pair は `stableVersion → 0.27.0` と
`nextVersion → 0.27.1` です。対応する package artifact 名は
`JTechJapan.IntentSystem.Cli.0.27.1.nupkg` ですが、placeholder の名前だけです。
canonical release-prep coordination scope は
`release-prep:<owner/repo>:0.27.1` です。plain policy の current value は
stableVersion 0.27.0、nextVersion 0.27.1 です。v0.27.0 GitHub Release は
shipped evidence の正式な根拠です:
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.27.0。この roll
では tag、publish、GitHub Release の作成を行いません。

過去の readiness audit との continuity のため、v0.23.0 GitHub Release と
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.23.0、
`release-notes-v0.23.0.md` は自己完結型バイナリを記録します。npm leg は
registry に到達しなかったため、npm で利用できると扱ってはいけません。
installed 0.23.2 CLI と `checkout_freshness/provenance` は historical evidence
として残します。v0.27.0 release-note file は shipped-note evidence ですが、
prepare-only の wording と authoritative な GitHub Release の間には
source-note inconsistency があります。この source-note inconsistency はこの
roll より前から存在し、この scope 外の点は後の explicitly scoped remediation
で扱います。

### この roll の G725 evidence boundary

提供された pre-roll の host-root observation は target checkout
`f43fbd19f6e0cb7fa284ccd2f89d2932f63ca330` に対して実行され、`stalled=true` と
次の actionable item を返しました:

```text
kind=version-roll-required
is_informational=false
released_version=0.27.0
expected_stable_version=0.27.0
expected_next_version=0.27.1
```

この observation に使った host 側 metadata checkout は local revision
`35c6d96a` が `origin/main` `209b1369` より stale だと warning を出しました。
この provenance はそのまま記録し、fresh な synced-main measurement とは
扱いません。この edit 前に同じ target checkout から available child run を
実行した結果も上記の actionable `version-roll-required` item を返しました。
同時に informational な stale-claim item と child queue-state file missing の
warning も返しました。

この child roll 後、checkout
`f43fbd19f6e0cb7fa284ccd2f89d2932f63ca330` から実行した child run は
`stalled=true` を返し、informational な G717、G719、G725 の `claim-stale` item
だけを含み、`version-roll-required` item はなく、child queue state が見つからない
warning も返しました。policy pair は正しいですが、この silence だけでは evidence ではなく、
roll の証明にもなりません。valid な answer には、この PR merge 後に
synced host-main checkout から同じ command を実行し、checkout commit と
freshness/provenance を記録する必要があります。implementation seat は host
repository に入りません。**HOST DUTY REQUEST:** merge 後、synced host-main
target checkout から
`intent-cli automation stalled-work --domain intent-cli --repo J-Tech-Japan/intent-system --format json`
を実行し、関連する before/after の `items`、checkout commit、freshness/provenance
を返してください。この child-side silence を proof として扱わないでください。

最終 post-change child run は committed checkout
`a21c1f2334d0a81412fa1f9b49e0b8320e39de91` で実行しました。同じく
`stalled=true` ですが、informational な G717、G719、G725 の `claim-stale` item
だけで、`version-roll-required` item はありませんでした。freshness warning は
local HEAD `a21c1f2` が `origin/main`
`f43fbd19f6e0cb7fa284ccd2f89d2932f63ca330` より stale だと示し、child queue-state
warning も残りました。これは child evidence であり、synced host-root proof では
ありません。

host worker は #1640 を selected しましたが、issue-preflight は
`.git/FETCH_HEAD` を読めないため `canonical-unavailable` を返しました。
fresh child selector は local `execution-unit:G754` が unheld なので
`next-action=wait` を返しました。これは既知の child/host registry
contradiction であり、child が ownership を決めたものではありません。提供された
host claim を正本として使い、この child は execution-unit claim を create、alter、
release、verify せず、host repository に入りませんでした。

shipped line の real-install evidence は `intent-cli 0.27.0-f43fbd1-G753` です。
この roll で観測した child verification は、dedicated v0.27.1 guard が 4 passed、
0 failed、0 skipped、adjacent release guard が 65 passed、0 failed、0 skipped、
G613 が 6 passed、0 failed、0 skipped、Full Release suite が 5336 passed、0 failed、
1 skipped (5337 total)、`git diff --check` は clean です。CI は push 後の別の
check です。

### 以前の v0.27.0 release-prep evidence (provenance)

**RELEASE PREPARED / NOT PUBLISHED。**

shipped stable line は intent-cli 0.26.0-93f07f8-G749、source revision は
93f07f892f6514bc561493339b11e36de0e36555 です。tracked な EN/JA
release-notes-v0.26.0.md はこの preparation で変更していません。
v0.26.0 の GitHub Release と tag が shipped evidence です。
real-install identity は `intent-cli 0.26.0-93f07f8-G749` です。

過去の readiness audit との互換性のため、この current block には shipped-artifact
evidence も残します。v0.26.0 GitHub Release は shipped evidence の正式な根拠です。
v0.23.0 GitHub Release
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.23.0 と
release-notes-v0.23.0.md は自己完結型バイナリを記録しています。npm leg は
registry に到達しなかったため、npm で利用できると扱ってはいけません。
prior roll の evidence は POST-RELEASE ROLL / PLACEHOLDER ONLY です。
source-note inconsistency はこの roll より前から存在し、scope 外であり、後の
explicitly scoped remediation で扱います。installed 0.23.2 CLI と
checkout_freshness/provenance も記録しています。

version-flow guard のため、stableVersion 0.26.0 と nextVersion 0.27.0 が
policy pair です。stableVersion → 0.26.0 と nextVersion → 0.27.0 が監査した
transition で、JTechJapan.IntentSystem.Cli.0.27.0.nupkg は package artifact
名だけです。canonical release-prep coordination scope は
release-prep:<owner/repo>:0.27.0 です。tag、GitHub Release、package publish
という post-release action は行いません。
現在の policy は stableVersion `0.26.0`、nextVersion `0.27.0` です。
この preparation は両方の mirror に
[release-notes-v0.27.0.md](release-notes-v0.27.0.md) を追加し、以前の
v0.26.1 DRAFT placeholder stub を削除します。v0.26.1 は post-v0.25.0
roll が置いた placeholder だけで、選ばれた release ではありません。
tag、GitHub Release、package publish、post-release roll、G725 の
diagnosis/fix はこの PR の範囲外です。

正確な prepared functional head は
`bb9754859ac8055adbd504f294145b7494668c1a` です。この revision を clean
Release build した identity は `intent-cli 0.26.0-bb97548-G751`、
installed baseline は `intent-cli 0.26.0-93f07f8-G749` でした。version
file から先に推測せず、この build を version decision の根拠にします。

programmatic sweep は 32 個すべての command group の help と、各 direct
subcommand の help を呼び出しました。installed CLI は group descriptor
32 + direct-help usage 71 = **103 usages**、Release build は 32 + 72 =
**104 usages** でした。増えたのは
`notify supervise repair-cycle-history` の一つだけで、removal はありません。
installed の `notify supervise repair-cycle-history` は
`invalid-notification: Unknown argument 'repair-cycle-history'.` を返し、
prepared usage は
`notify supervise repair-cycle-history --domain <d> --team <t> [--dry-run|--write]
[--format markdown|json]` です。`automation`、`claim`、`worker` の help、
および `state-doctor` と `closeout-drift-check` の usage は
byte-identical でした。この測定した operator surface が minor bump の
監査可能な reason です。

release inventory は operator が観測できる outcome を持つ、次の二つの
unit だけです。

- G750 — PR #1634; merge commit `b525191a24e361419b03f77e15e659110a22c395`。
  **Operator-observable outcome:** supervision cycle history を git に
  持たなくなり、100MB の cycle-history file で push が block された host
  も push できます。すでに tracking 中の host には
  `notify supervise repair-cycle-history` という supported migration が
  あり、file は preserve され、delete されません。
- G751 — PR #1635; merge commit `bb9754859ac8055adbd504f294145b7494668c1a`。
  **Operator-observable outcome:** observation のない成功した event-mode
  wait は永続 cycle record を作らず、genuine observation と interval
  safety-floor record は永続のままです。そのため running supervisor
  の write rate は空の wait ごとの event-wait record ではなく、宣言した
  one-record-per-interval に settle します。

正確な first-parent range は
`v0.26.0..086344540d70a052555502971fa968aff6a252ac` で、`git rev-list
--first-parent --reverse` と `git rev-list --first-parent --count` により
3 commits でした。

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| b525191a24e361419b03f77e15e659110a22c395 | G750 release unit; PR #1634 | included |
| bb9754859ac8055adbd504f294145b7494668c1a | G751 release unit; PR #1635 | included |
| 086344540d70a052555502971fa968aff6a252ac | G752 post-v0.26.0 version roll to the 0.26.1 placeholder; not a release unit | classified only |

G752 は classification table に残し、release unit として数えません。
release inventory は G750、G751 の二つだけです。v0.26.0 の G744 entry は
live file を bounded にしただけで write volume を減らしませんでした。
v0.26.0 に upgrade して history growth が止まると期待した operator は、
この release の G751 までその outcome を得られませんでした。これは二つの
release にまたがる一つの三-unit problem です。G744 は live history を
bounded にし、G750 は runtime-local cycle history を git から外し、
non-deleting migration を用意し、G751 は write rate を減らしました。

測定値は形容ではなく source を付けた measurement です。G750 の記録では
`cycles.jsonl` は GitHub の 100MB tracking limit で 111.5MB に達しました。
G751 は no-observation change 前を 3.6 records/second、後を 12.00/hour
と測定しました。最初の値は git blockage を、後の二つは running
supervisor の before/after write rate を示します。

host worker の next-action は #1638 を selected しましたが、issue-preflight
は `canonical-unavailable` で、`.git/FETCH_HEAD` を読めませんでした。
fresh child worker selector は別に `next-action=wait`、local execution-unit
G753 の holder none/unheld を返しました。これは既知の child/host registry
contradiction であり、child が ownership を決めたものではありません。
supplied host claim を正本として使用し、child execution-unit claim
を create、alter、release、verify せず、host repository に入りませんでした。

release-prep の verification は v0.27.0 notes と PR report に記録します。
focused release/doc/version guard: 14 passed, 0 failed, 0 skipped (14 total)。
adjacent release/readiness guard: 51 passed, 0 failed, 0 skipped (51 total)。
dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total)。
Full Release suite: 5332 passed, 0 failed, 1 skipped (5333 total)。`git diff --check` は
clean です。tracked な v0.26.0 EN/JA shipped-note は byte-identical のままです。

### 以前の v0.26.1 post-release roll evidence (provenance)

**POST-RELEASE ROLL / PLACEHOLDER ONLY。**

**v0.26.0 shipped evidence:** shipped baseline は
intent-cli 0.26.0-93f07f8-G749、source revision は
93f07f892f6514bc561493339b11e36de0e36555 です。tracked な EN/JA
release-notes-v0.26.0.md はこの post-release roll で変更していません。
前の v0.24.0 shipped-note file、release-notes-v0.24.0.md は両方の mirror にある historical evidence として残ります。
v0.26.0 GitHub Release と tag は shipped evidence の正式な根拠です。
real-install identity は `intent-cli 0.26.0-93f07f8-G749` です。
source-note inconsistency はこの roll より前から存在し、修正は scope 外であり、後続の explicitly scoped remediation で扱います。
比較対象には installed 0.23.2 CLI を使い、checkout_freshness/provenance をこの preparation とともに記録します。
現在の policy は stableVersion 0.26.0 と nextVersion 0.26.1 です。
Rolled policy: stableVersion → 0.26.0; nextVersion → 0.26.1 (placeholder only)。
次の line の package identity は `JTechJapan.IntentSystem.Cli.0.26.1.nupkg` です。これは placeholder の値であり、publish action ではありません。
release-readiness gate はこの post-release roll とは別です。

この post-release roll は `eng/version.json` を stableVersion `0.26.0`、
nextVersion `0.26.1` にします。shipped v0.26.0 note file は変更せず、
former placeholder file release-notes-v0.26.1.md は current preparation で削除しました。
v0.26.1 DRAFT stub は replaceable planning scaffold で changelog ではありません。
release-prep は測定して次の real release number を決めた後、この stub を replace します。
この roll では tag、GitHub Release、package publish、unreleased content の追加を行いません。

v0.26.0 preparation でこの child が正確な prepared head
`a49ad93c36bd93d1ccc9317622d36fa01ea346b8` を Release build して測定した
identity は `intent-cli 0.25.1-a49ad93-G748`、installed baseline は
`intent-cli 0.25.0-74a1c72-G741` でした。installed の
`notify supervise archive` は `archive` を拒否しました。正確な metadata-only
policy update 後の final Release identity は `intent-cli 0.26.0-a49ad93-G748` です。
build した usage は
`notify supervise archive --domain <d> --team <t> [--live-window-days <days>] [--dry-run|--write] [--format markdown|json]` を示します。
`automation`、`claim`、`worker` の help は byte-identical で、notify の差は
この archive surface だけです。state-doctor と closeout-drift-check の usage
も byte-identical でした。minor bump の根拠はこの測定であり、version file から
先に推測したものではありません。

`v0.25.0..a49ad93c36bd93d1ccc9317622d36fa01ea346b8` の first-parent range は
六つの commit です。release inventory は G743、G744、G746、G747、G748 の
五つだけです。G745 の `b8f249e965cad2c3c2e19dda9dd99e726324485d`
post-v0.25.0 roll は release-note table で分類し、release unit には数えません。
G743 と G747 は v0.25.0 で shipped した claim-transaction contract を
finish/repair し、G748 は sixteen 件の qualifying incident で zero 回だった
G741 detector を repair しました。五つの outcome は shipped v0.26.0 note に
operator-observable として記録しています。

v0.26.0 release-prep verification の正確な count はその note に記録しています:
Targeted release-prep docs/version guard: 40 passed, 0 failed, 0 skipped (40 total)。
Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total)。
Adjacent release/readiness suite: 59 passed, 0 failed, 0 skipped (59 total)。
Full Release suite: 5305 passed, 0 failed, 1 skipped (5306 total)。git diff --check: clean。
child issue-preflight が canonical-unavailable になったのは sandbox による
`.git/FETCH_HEAD` refresh 拒否だけなので、supplied host claim を使用しました。
child execution-unit claim は作成・変更・release せず、host repository に入りませんでした。
host-only claim boundary は `release-prep:<owner/repo>:0.26.1` で、この child は host state を inspect しません。
orchestration seat の preflight は `next-action=wait`、`classification=claim-unavailable`、`actionable=false` を記録しました。`.git/FETCH_HEAD` (`Operation not permitted`) を open できなかったためです。fresh child selector は local holder none/unheld と報告しました。supplied host claim が正式な根拠であり、この child は execution-unit claim を create、modify、release、verify していません。
G725 evidence boundary: real host-root の pre-roll run は synced target checkout
`bb9754859ac8055adbd504f294145b7494668c1a` で actionable な
`version-roll-required` finding を 1 件返し、stableVersion `0.26.0` と
nextVersion `0.26.1` を期待していました。PR-head child-clone run が
`c73e12e6d08c6e7698f393c47c571f1320bedf90` で `version-roll-required` finding を
0 件返したのは、origin/main `bb9754859ac8055adbd504f294145b7494668c1a` に対して
checkout が stale で、queue state も missing と報告した状態だけです。この 0 件は
non-evidence であり roll の証明ではありません。valid な post-merge answer には synced host-main measurement が必要です。この child は G725 を diagnose/fix していません。
公開済みの [v0.23.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.23.0) とその tag は shipped evidence の正式な根拠です。
v0.23.0 の shipped artifact は GitHub Release、NuGet package、自己完結型バイナリです。ただし npm leg は registry に到達しなかったため、`0.23.0` は npm で利用できると扱ってはいけません。
tracked な EN/JA の `release-notes-v0.23.0.md` files はこの shipped-artifact status を記録します。



**Previous v0.25.0 preparation evidence (retained for provenance):** 正確な prepared functional head は
5c4af5d88ddcfa47335bad4df56ad3e40dae9140 です。その Release build は
intent-cli 0.24.1-5c4af5d-G741 を表示し、installed intent-cli は
intent-cli 0.24.0-df472fe-G737 を表示しました。増えた option は
session-layer topology record --model <text>、
session-layer topology record --reasoning-effort <text>、
notify supervise --delegation-execution-window-seconds <seconds>
(default 300) です。この測定した command-surface difference が
prior stableVersion 0.24.0 / nextVersion 0.25.0 preparation の監査可能な reason でした。

```bash
intent-cli --version
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release --no-restore
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
```

**First-parent proof:** `v0.24.0..5c4af5d88ddcfa47335bad4df56ad3e40dae9140`
は `git rev-list --first-parent --reverse` と
`git rev-list --first-parent --count` で測定し、commit は四つだけです。

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| f0a30f08de6281b34b6fd4a5e8732243ad176053 | G738 release unit; PR #1609 | included |
| f0ea90fd3df65de3f1b95bd38f6f8c79b011d171 | G739 release unit; PR #1611 | included |
| 8bcab9766412e3c946f3299274f969277135eb03 | G740 post-release version roll to the 0.24.1 placeholder; not a release unit | classified only |
| 5c4af5d88ddcfa47335bad4df56ad3e40dae9140 | G741 release unit; PR #1614 | included |

release inventory は G738、G739、G741 の三つだけです。G740 roll は
分類表に残し、release unit には数えません。

**Operator-observable outcomes:** G738 は commit 済み claim の teardown を
best-effort かつ bounded にし、teardown で fail や hang せず Windows user が
command を background にする必要をなくします。G739 は model と
reasoning-effort の attribution を topology の show/validate から確認でき、
absence は fail になりません。G741 は六条件が全て成立した
delivered-but-never-observably-started delegation だけを finding として報告し、
slow-but-started は finding にしません。classifier は observation-only で、
六つの motivating incident を seat 名なしで記録します。
preceding v0.25.0 preparation は **同一コミットに DRAFT note スタブ**(ステップ 5)、**両ミラーで新しいラインへ更新**(ステップ 6)、そして **roll 後の child main CI green 確認**(ステップ 7)を記録しました。
この post-release roll でも次の real release number は measurement に基づく decision のままで、0.25.1 は placeholder だけです。

過去の release line から維持する readiness guard は ReleaseNotesV0180DocsTests、ReleaseNotesV0190DocsTests、ReleaseNotesV0170DocsTests です。
この post-release roll は **ステップ 5–7** に従います。既存の release tag、Release、package、workflow、shipped note file は変更しません。
child 側の report は host boundary を記録し、host state を inspect しません。


**Release-prep verification:**

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~ReleaseNotesV0250DocsTests|FullyQualifiedName~ReleaseNotesV0240DocsTests|FullyQualifiedName~ReleasePackageMetadataTests|FullyQualifiedName~VersionSourcePolicyGuardTests|FullyQualifiedName~JapaneseTerminologyGuardG613Tests"
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~JapaneseTerminologyGuardG613Tests"
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~SessionLayerTopologyG739Tests|FullyQualifiedName~NotifySupervisionG741Tests"
dotnet test IntentSystem.sln --no-restore -c Release --logger "console;verbosity=minimal"
git diff --check
```

Targeted release-prep guard: 40 passed, 0 failed, 0 skipped (40 total)。
Dedicated G613 JA terminology guard: 6 passed, 0 failed, 0 skipped (6 total)。
Adjacent release tests: 50 passed, 0 failed, 0 skipped (50 total)。
Full Release suite: 5268 passed, 0 failed, 1 skipped (5269 total)。
`eng/version.json` は stableVersion 0.25.0 / nextVersion 0.25.1 になりました。
0.25.1 は placeholder だけであり、release-prep が次の real release number を測定して
決めます。tag、GitHub Release、package publish、source runtime change はこの roll の範囲外です。
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

### G746: 重複 queue item は recoverable であり impossible ではない (duplicates are recoverable, not impossible)

queue-state は順序付きリストですが、queue item の identity は
`execution_unit` です。`automation closeout-drift-check` は GitHub lookup
の前にこの identity ごとに entry をまとめます。重複しているすべての unit
を `duplicate-queue-item` として報告し、両方の full competing entries を
出力し、その unit の closeout だけを保留して、残りの drift check を継続
します。Dictionary.Add による未処理例外で command が停止してはいけません。

`automation state-doctor` も同じ finding を報告します。`--write` で削除
できるのは、他のすべての entry より **strictly more informative** な entry
が一つだけの場合に限ります。より進んだ lifecycle state、または相手にない
`linked_pr`/`linked_issue` を持ち、保持する entry から情報を失わない場合です。
byte-identical/equivalent または incomparable な entry は unsafe stop とし、
unit 名と full competing entries を示して mutation を行いません。operator
が competing fields を整理してから canonical command を再実行します。

この修復により duplicate queue item は **recoverable であり、impossible
ではありません**。queue-state を手編集してはいけません。対象は重複の報告と
strict dominance による安全な整理だけであり、concurrent-write prevention、
locking、CAS、queue schema の変更は別の作業です。
