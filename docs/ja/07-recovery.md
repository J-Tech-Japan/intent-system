# ループがおかしいときの復旧

← [ドキュメント索引](README.md) | → [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)

ループが止まったとき、最初に必要なのはコマンドではなく症状の説明です。
label や metadata を直接編集せず、AI agent に症状を伝えて
intent-cli への修復依頼を任せてください。

## まず症状を伝える

設計スレッドまたは対象の automation スレッドで、次のように伝えてください:

**具体的な症状がある場合:**

```text
PRができて review-in-progress になっているけど、AIが作業していない。
何か問題があったかもしれないので、intent-cli に方法を聞いて修復してください。
```

**何か止まっているが原因が不明な場合:**

```text
これができていないけど、進めたい。intent-cli に方法を聞いて修復してください。
```

agent は intent-cli の現在の guidance を確認し、安全な修復だけを実行します。
manual な状態編集や label の手付けは通常の修復パスではありません。

## よくある症状とプロンプト例

| 症状 | 伝え方の例 |
|---|---|
| PR が `review-in-progress` のまま作業が止まっている | `PR #<n> が review-in-progress のまま動いていない。intent-cli に聞いて修復して。` |
| issue が published だが実装が始まらない | `issue #<n> に intent-target が付いているのに実装ループが拾っていない。intent-cli に聞いて。` |
| PR コメントへの修正が始まらない | `PR #<n> に request-update があるが修正が始まらない。intent-cli に聞いて修復して。` |
| マージ後に次の issue が切り出されない | `PR #<n> がマージされたのに次の issue が来ない。intent-cli に聞いて。` |
| ループが idle を報告するが作業がありそう | `ループが idle と言うが issue #<n> があるはず。intent-cli に聞いて状況を確認して。` |
| metadata の状態が不整合に見える | `状態がおかしい。intent-cli に診断して安全な修復を実行して。` |

## 修復の原則

- **状態を手編集しない**: `queue-state.json`、label、metadata を直接変更しない
- **agent に任せる**: intent-cli がどのコマンドが修復を所有するかを判断する
- **1 回ずつ**: 1 回の修復サイクルで最大 1 件の guided repair のみ適用する
- **operator 判断が必要なら止まる**: intent-cli が `host-artifact-repair-required` または `clarification-required` を返した場合はオペレーターへ報告して止まる

## コマンドリファレンス（agent・メンテナ・トラブルシューティング向け）

> **注意:** 以下のコマンドは AI agent が内部で実行します。通常、ユーザーが直接実行する必要はありません。ワークフローのデバッグや host automation のメンテナンスを行う場合に参照してください。

```bash
# この PR のレビュー指摘は安全で scope 内の child 修復か？
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr <n> --format json

# この issue を issue-to-pr として（再）claim して安全か？
intent-cli worker issue-preflight --repo <owner>/<repo> --issue <n> --format json

# CLI の鮮度 / host-state 解決
intent-cli automation doctor --format json
```

結果の読み方: `actionable` / `safe_repair_available` / `repair_category` が
child-loop 所有の修復が存在するかを示す。host 所有のカテゴリは
`host-artifact-repair-required` として現れ、host ループへ戻す。

> **packet evidence の引用は host 編集要求ではない（G476）。** レビューコメントは、
> 実装ファイルの変更を求めつつ `.intent-cli/issues/<unit>/packet.yaml` のような
> host metadata path を packet-aware review の根拠（G316）として引用することがある。
> `pr-comment-preflight` は付随的な `.intent-cli/` や `intents/` の言及ではなく
> **要求された編集対象（requested edit target）** で分類する: すべての編集対象が
> host metadata path のときだけ `host-artifact-repair-required` になる。結果は
> `actionable_comments[].requested_edit_paths` と
> `actionable_comments[].host_evidence_paths` を公開するので、host metadata を読まず
> にどの path が判定を駆動したか確認できる。child が `host-artifact-repair-required`
> と言うのに host ループが host drift なしと報告する場合は、黙ってループせず
> `pr-comment-preflight` を再実行し、この 2 つの path リストで実際の編集対象を切り分ける。

> **`linked_pr` 欠落の closeout は host 所有の deterministic recovery（G477）。**
> `intent-cli closeout pr --pr <n>` が、`linked_pr` が host durable state に projection
> されていないことだけを理由に queue item を照合できない場合、GitHub facts から自動回復する:
> merged PR の closing references が（`linked_issue` 経由で）ちょうど1つの queue item を
> 特定できれば、手動の `--issue <n>` 再実行なしで completed になる。結果は
> `recoverable_missing_linked_pr: true` / `inferred_issue` /
> `recovery_action: recover-linked-pr-from-github-closing-reference` を報告し、write 時に
> 欠落していた `linked_pr` を修復する。これは host 所有の projection recovery であり、
> operator の policy 判断ではない — 手動 `--issue` 再実行を必須の tribal knowledge として
> 扱わないこと。曖昧な証拠（closing references が複数の queue item に一致）は
> `linkage-ambiguous` エラーで fail closed する。その場合のみ正しい `--issue <n>` で再実行する。
> child `--github-only` ループは `linked_pr` を書かない。この recovery は host closeout surface の責務。

> **repo-qualified linkage と completed repair（G603）。** GitHub の issue / PR
> 番号は `owner/repo` と組になって初めて identity になる。worker complete、closeout、
> review planning、stalled-work は bare な番号または別 repo の linkage を拒否し、
> 同じ番号の foreign queue item を選ばない。host context の `worker complete` は、
> 選ばれた unit の declared domain と command domain が違う write も拒否する。
> completed unit の `linked_pr` が別 repo を指す場合は
> `automation host-queue-item-recovery --repo <owner/repo> --unit <unit> --issue <n> --pr <n> --write`
> を使う。提案する PR の存在・merged・その unit 自身の issue を closes することを
> GitHub facts で確認してから、evidence を含む `completed-linkage-repair` run event を追加する。
> readable な `publish.yaml` identity が無い legacy packet は
> `legacy-publish-identity-missing` で不足 evidence を明示して停止し、推測や completed
> linkage の変更は行わない。

### 繰り返しストール回復（G408）

同じターゲットで同じブロッカーに **2 回以上連続** してヒットした場合は、
停止を報告し続けるのではなく自己回復する。回復フロー:

```bash
intent-cli guide model --format json
intent-cli guide onboarding --format json
intent-cli automation summary --domain <domain> --format json

# child ループ: 詰まったターゲットに対応する preflight を実行
intent-cli worker issue-preflight      --repo <owner>/<repo> --issue <n> --format json
intent-cli worker pr-comment-preflight --repo <owner>/<repo> --pr    <n> --format json

# host ループ: 鮮度と状態を確認
intent-cli automation doctor --format json
```

| 結果 | 対応 |
|------|------|
| `safe_repair_category: child-selector-label-gap` | `intent-cli` が安全と判断した修復を 1 回適用し、リトライ |
| `host-artifact-repair-required` | 停止。構造化された operator stop を報告する。手修正しない |
| `clarification-required` | 停止。何が曖昧かを報告し、operator の入力を待つ |
| 1 回修復してもストール継続 | operator stop へエスカレートする。無限リトライしない |

## 次へ

[ドキュメント索引](README.md) | [レビュー / next-slice ループの設定](06-review-next-slice-loop.md)
