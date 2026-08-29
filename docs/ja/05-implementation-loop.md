# 実装ループの設定

← [ドキュメント索引](README.md) | → [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)

実装ループは、このページの手順を直接コピーして作るものではありません。
正確な条件は installed intent-cli guidance が source of truth です。
設計スレッドで AI agent に依頼し、現在のループ作成プロンプトを生成してもらいます。

## フォルダー分離（最初に理解する）

実装ループを始める前に、3 つのフォルダーの役割を理解してください:

| フォルダー | 役割 |
|---|---|
| **設計/host フォルダー** | intent metadata・packet を保管。設計スレッドがここで動く |
| **実装フォルダー** | child implementation ループがコードを編集し PR を作成/更新する |
| **レビューフォルダー** | host review/next-slice ループが PR をレビューし次の issue を公開する |

> **注意 — cwd の誤りはよくある失敗パターンです。**
> 実装ループを設計/host フォルダーで起動したり、レビューループを実装フォルダーで起動したりすると
> 誤動作します。ループを開始する前に作業ディレクトリを必ず確認してください。

**同一リポジトリ metadata トポロジー**（`main-metadata` ブランチ使用）の場合も、
設計・実装・レビューの各ループは**同じリポジトリの別フォルダー/クローン/ワークツリー**
で動かすことを強く推奨します。3 つのロールが同じフォルダーを共有すると、
互いのブランチ操作や metadata 変更が干渉します。

## ループ作成の手順

1. **設計スレッドで** AI agent に intent-cli への問い合わせを依頼し、実装ループ作成プロンプトを生成する
2. domain、target repo、**実装フォルダーのパス**、PR の base branch を伝える
3. 生成されたプロンプトを **実装フォルダー** で開いた別スレッドに貼り付ける

## 設計スレッドプロンプト（ループ作成依頼用）

設計スレッド（設計/host フォルダーで動いている AI agent）に貼り付けてください:

> intent-cli に聞いて、`<owner>/<repo>` の child implementation loop を
> Claude Code の `/loop 5m` で作成するための依頼文を作ってください。
> domain は `<domain>`、作業場所は `<implementation-folder>`、
> 実装 PR の base は `<branch>` です。
> 詳細条件は intent-cli guidance に従う形にしてください。

生成されたプロンプトを**実装フォルダーを開いた別スレッド**に貼り付けます。
ループの詳細条件は intent-cli guidance から取得されるため、
このドキュメントに長いループ本体をコピーする必要はありません。

## child implementation ループの原則

- **GitHub-contract-only かつ metadata-free**: issue/PR とリポジトリローカルのコードのみが source of truth
- host の `.intent-cli/`、queue-state、metadata branch、`intents/**` を読んだり変更したりしない
- 作業の選択は `intent-cli worker next-action` のみ。1 wake で最大 1 アクション
- label 遷移はすべて `intent-cli worker` 経由 — raw `gh ... --add-label` は使わない

## metadata / label の安全境界

- child agent は PR に workflow label を手動で付けません。issue-to-PR の `pr-created` では、canonical な `worker complete --kind issue --outcome pr-created --github-only --write` が target repository の PR に `intent-target` を付けることがあります。`intent-pr-created` は issue 側の marker のままです。
- `worker complete` の `linked_pr_synced: false` は child-cwd で想定される警告 — 記録して先に進む

## G733: seat / host duty の契約上の境界（[ADR 0010](../adr/0010-seat-host-duty-route.md)）

implementation seat は child repository の作業を end to end で担当します。
割り当てられた GitHub issue から standalone contract を読み、`git fetch
origin main` を実行し、`origin/main` から branch を作り、child code を編集・
test し、commit、push、`Closes #<issue>` を含む **ready-for-review** PR を作り、
結果を報告します。この child path は target repository の GitHub facts と
`intent-cli worker ... --github-only` を使い、host への round trip を必要と
しません。

canonical な child completion も seat-owned path の一部です。issue-to-PR の
`pr-created` では、`worker complete --kind issue --outcome pr-created
--github-only --write` が source issue に `intent-pr-created` を付け、target
repository の PR に `intent-target` を付けます。これは intent-cli が所有する
target-repository transition であり、raw `gh` label mutation ではありません。
host-state の linkage / publication、queue synchronization、closeout は host
duty のままです。child-cwd completion はこの follow-up を
`linked_pr_synced: false` として報告します。

host role は host state を担当します。`.intent-cli` の queue-state、claims、
runs、packet、metadata branch、host repository の Git refresh / push、host
repository の credentials / API operation と host-state の linkage / publication が
対象です。execution-unit の
claim acquisition は host duty であり、lifecycle label や local file から
推測しません。seat が編集を始める前に host role は次の canonical JSON を
返します:

```bash
intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json
```

evidence には `status=acquired`、`push_succeeded=true`、一致する
scope/actor/team と push 済みの `commit`、続いて `passed=true` と
`status=owned` が必要です。これは G679 の Git compare-and-swap を守るため
です: pull、immutable な claim record、commit、plain push の順であり、remote
push 成功だけが ownership です。label、local claim file、local commit、
preflight output は ownership ではありません。force-push、時間による expiry、
推測した takeover も許可しません。

### 正確な host-duty request

この evidence がない場合、または別の host-owned operation が必要な場合、
implementation seat は team の canonical message channel を使って次の request
を送ります。host repository に入ったり、agmsg / herdr transport を手書きしたり
しません:

```bash
intent-cli notify report --domain <domain> --team <team> --from implementation --to orchestration --task-id <task-id> --status question --artifact <child-artifact> --summary 'HOST DUTY REQUEST: run intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json; then intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json; return the JSON evidence, pushed commit, and owned verdict.' --routing-root <host-routing-root> --report-root . --write --format json
```

seat は host の `.intent-cli/`、queue-state、claims、runs、packet、metadata
branch、host Git を読んだり変更したりできません。execution-unit claim の
acquire / release / takeover、host repository の credentials や host repository
GitHub API の使用、host-state の publish、linkage、review、closeout transition も
できません。seat から host-aware preflight を呼び、host `FETCH_HEAD` を refresh
できないという refusal が返った場合は、正確な command と refusal を host duty
として報告します。root を広げたり、即席の clone を作ったり、host repository に
入って再試行したりしません。

この rule は co-located machine でも変わりません。host file や credentials が
今日読めるように見えるのは seat が同じ machine を共有しているからにすぎず、
remote-herdr では implementation seat が別 VM に配置され、shared filesystem も
host credentials も持たない可能性があります。したがって co-location は
contractual capability ではなく、child の dependency にしてはいけません。

seat-owned verification は次の 3 境界をすべて出力します。child の
`worker ... --github-only` による selection / claim / complete JSON と branch、
test、commit、push、PR の evidence、正確な `notify report` host-duty request と
host claim JSON、そして拒否され続ける boundary probe です。例えば
`touch <host-routing-root>/.intent-cli/probe-should-fail` は `Operation not
permitted` とともに nonzero で終了しなければなりません。test-owned sentinel を
使い、negative probe が予想に反して成功した場合は削除しません。

## G736: topology に host-state capacity を記録する（[ADR 0011](../adr/0011-topology-host-state-role.md)）

session-layer topology には、host-state を担当する role と named envelope を明示的に記録できます:

```bash
intent-cli session-layer topology record-host-state --domain <domain> --team <team> --role <role> --envelope <named-host-state-envelope> --write --format json
```

これは `resident`、`kind`、external placement、co-location から推測する権限ではなく、明示的な declaration です。
`topology validate` は legacy record を valid のままにして migration もしませんが、declaration がない場合は publish 前に
informational な `host-state-role-missing` を報告します。その finding は、team が必要な host-state publication や
repository-Git work を実行できないことを伝えます。declaration だけで non-sandboxed participant が作られるわけではなく、
実際に capable な participant と明示的な declaration の両方が必要です。orchestrator は record から role と envelope を
探索します。design role を host-state と明示的に宣言することは正当であり、既存の禁止は undeclared / ad-hoc な
routine request に限られます。

record、validate、render された discovery の永続的な証拠は
[G736 verification transcript](../g736-topology-host-state-verification.md) にあります。

## G724: multi-domain host の worker domain identity

startup marker は display evidence であり、worker binding ではありません。host context の
`worker complete --kind issue --outcome pr-created` は execution-unit の domain を durable な
queue/packet record から解決します。そのため shared `CLAUDE.md` が現在 domain A を表示していても、
domain B の worker は完了できます。JSON result には選択された `domain`、通常は `queue-record` となる
`domain_source`、`execution_unit` が出力され、worker complete は marker を書き換えません。

host invocation で `--domain` を指定する場合は durable queue domain、または domain のない legacy queue
row では authoritative な session-layer record と一致する必要があります。durable identity が欠落、矛盾、
読取不能、または曖昧な場合は fail closed し、`--domain <name>` を含む正確な再実行 command を出力します。
canonical queue/packet record を修復または選択してから、その worker surface recovery を使います。marker の
手編集、手動 label 操作、PR-linkage recovery による domain identity 回避は行いません。child の
`--github-only` は metadata-free のままで host queue state を読みません。

## Preview: Git-backed cross-clone scope claim (G679)

この decision と boundary は
[ADR 0003](../adr/0003-git-push-cas-work-ownership.md) に記録しています。

`worker claim` は上記の GitHub issue/PR lifecycle transition のままです。別の preview
`claim` group は server なしで host clone 間の named work unit を調整します。

```bash
intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json
intent-cli claim acquire --scope release-prep:<owner/repo>:<version> --actor <actor> --team <team> --write --format json
intent-cli claim verify --scope <scope> --team <team> --format json
```

acquire は厳密に `git pull --ff-only` → `.intent-cli/claims/` 配下に immutable record を
作成 → commit → plain push です。push 成功だけが acquire の事実です。push reject 後に
同じ scope が現れた場合は holder を名指す `held`、無関係な advance なら fresh base から
bounded retry で再適用します。release / takeover は actor/team/reason の明示的 attribution が
必要で、takeover は displaced holder を名指します。age で claim を expire / transfer しません。
`automation stalled-work` は actor、team、scope、age、last evidence を持つ `claim-stale` を
detect-only で報告します。

fresh host は `.gitattributes` に次の exact line をこの順序で置きます。

```gitattributes
.intent-cli/runs.jsonl merge=union
.intent-cli/**/*.jsonl merge=union
.intent-cli/claims/** -merge
```

existing host は自動 migrate しません。最後の specific line は broad union rule の後へ、
明示的に review した commit でのみ追加します。

### Preview: claim-aware start surface (G680)

packet draft、queue seed/publish-flow、worker next-action、release-prep は同じ
`claim verify` judgment を使います。Git worktree では verifier が claim acquire と同じ
remote-default-branch resolver を使って origin の canonical branch を解決し、そこを fetch して
fresh な `origin` ref の claims tree を読みます。読む checkout の current branch、local absence、
stale local record は ownership / no-store の証拠ではありません。detached checkout でも同じ
canonical answer を返します。default branch を解決または fetch できなければ
`canonical-unavailable` で fail closed します。canonical に claims store が存在しない host
だけは legacy single-team output を byte-identical に維持します。store が設定されている場合、invoking team が matching scope を
保持している必要があります。unheld / other-team の拒否は scope、holder、holder team を
名指します。next-slice は同じ judgment を recommendation mode で使い、unheld と own-team
unit は candidate のまま、claimed-elsewhere unit は holder evidence 付きで除外します。
これにより start が拒否する作業を recommendation が促しません。

番号は claim-then-draft です。scaffold 前に `execution-unit:<N>` を claim し、N に負けたら
fast-forward、次番号を再計算し、その番号を exactly once retry します。GitHub lifecycle label
は visible な defence in depth のままで acquisition fact ではありません。review/closeout gate と
`worker complete` は変更しません。

### Preview: host-state Git の bounded index.lock retry (G700)

sanctioned な `claim` transaction だけがこの retry policy の対象です。
その intent-cli-initiated host-state Git write（`pull`、`add`、`commit`、`push`、
invoking clone の refresh）について、intent-cli は `.git/index.lock` の contention
failure と判定できる場合だけ retry します。read-only Git inspection、agent の
free-form Git command は対象外で、queue や daemon は追加しません。

宣言済みの default configuration は
`max_attempts=4, window=2000ms, initial_delay=25ms, max_delay=250ms,
jitter=25ms` です。後続の retry で成功した contention は
`git_write_retry.outcome=succeeded` と実際の `attempts` を出力します。exhaustion は
`attempts`、`elapsed_milliseconds`、exact な `lock_path`、original Git error、
`manual_remediation` を持つ terminal error です。intent-cli は lock を delete、rename、
move、truncate、repair しません。named path を調べ、Git process が所有していないことを
確認してから stale lock を operator が手動で処理します。non-lock Git error は retry せずに返します。

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。ループの詳細条件は
> `intent-cli guide oneshot --kind child-implement-or-update --repo <owner>/<repo>`
> が source of truth です。通常、ユーザーが直接実行する必要はありません。

```bash
# 対象を 1 つだけ選ぶ（手動の label-walking はしない）
intent-cli worker next-action --repo <owner>/<repo> --team <team> --github-only --format json

# issue-to-pr: claim → 最小実装 → ready-for-review の PR を作成
intent-cli worker claim --kind issue --number <n> --repo <owner>/<repo> --github-only --write --format json
#    PR 本文に `Closes #<n>` を必ず含める。origin/main から開始する。
intent-cli worker result-summary --kind issue-to-pr --repo <owner>/<repo> --issue <n> --pr <pr> --outcome <outcome> --format json
intent-cli worker complete --kind issue --number <n> --repo <owner>/<repo> --github-only --outcome <outcome> --pr <pr> --write --format json
```

host-side completion で domain を明示的に選ぶ場合は、同じ `worker complete` command に
`--domain <durable-domain>` を加えます。queue/packet record と一致しなければならず、startup marker が
domain を供給・上書きすることはありません。

## 次へ

[レビュー / next-slice ループの設定](06-review-next-slice-loop.md) | [ドキュメント索引](README.md)
