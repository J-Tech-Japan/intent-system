# agent メッセージオーケストレーション（single-domain と multi-domain）

← [レビュー standing-policy](11-review-standing-policy.md) | [docs インデックス](README.md)

このページは **オプションの** agmsg ベースの orchestrator スレッドと、特に
1 つの host リポジトリが **複数の intent ドメイン** を保持する場合に、それを
どう安全に保つかを説明します。権威ある貼り付け可能なプロンプトはインストール済み
の intent-cli ガイダンスから生成され、このページのプロンプトを手で写してはいけません。
現在のプロンプトは次で生成します:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --mode single-domain|multi-domain --format markdown
```

## agmsg とは（そして何ではないか）

agmsg は **メッセージ / 進捗 / 完了 / ブロッカーのシグナル層のみ** です。スレッド間で
自然言語の委譲・返信シグナルを運びます。

`intent-cli` と GitHub は queue-state、issue/PR の事実、label、CI、レビュー、closeout、
recovery について **権威** であり続けます。シグナルはワークフロー状態ではありません。
orchestrator はそれに従って行動する前に、すべての主張を intent-cli / GitHub に対して
**再検証** します。intent-cli は Claude/Codex などの AI プロバイダーを起動しません。

## 2 つのドライバーモード（domain/repo ごとに 1 つを選ぶ）

| モード | ドライバー | 備考 |
|---|---|---|
| **timer-loop モード** | 定期タイマー | 既存・完全サポート。実装/レビュースレッドが自己スケジュールし、`worker next-action` / host review-next-slice を読む。orchestrator は不要。 |
| **orchestrator-message モード** | 4 つ目の orchestrator スレッド | オプトイン。orchestrator がタイマーの代わりに agmsg 経由で実装/レビュースレッドをペース配分する。 |

同じ domain/repo に対して両モードを同時に実行しては **いけません**。
orchestrator-message モードでは、実装/レビューの定期タイマーループも起動しないでください。
2 つのドライバーが同じ GitHub 状態を奪い合ってしまいます。

## スケジュールされた orchestrator のケイデンス

orchestrator-message モードでは、orchestrator スレッドが **唯一の定期ドライバー** です。
**orchestrator のみ** をスケジュールしてください。実装/レビュースレッドは長命ですが
**ループを持たない受信側（loopless receiver）** であり、orchestrator が委譲したときだけ
動作し、同じ domain/repo に対して自分の定期タイマーを起動しません。これにより定期ドライバー
を保ちつつ（設計進捗、agmsg 返信、完了した CI、承認済み PR を、オペレーターが停滞作業を
突く必要なく検知できる）、mixed-mode のタイマー競合を回避します。

orchestrator のスケジュール方法は次の 2 通り:

- **Codex automation（5 分ごと）** — 起動ごとに 1 回の orchestrator wake を実行: 設計進捗
  と返信を確認し、intent-cli に状態を問い合わせ、GitHub の事実を検証し、最大 1 通だけ
  メッセージを送って終了する。
- **Claude 同一スレッド `/loop 5m`** — orchestrator スレッドで `/loop 5m` を実行し、同じ
  スレッドが 5 分ごとに 1 パスずつ再起動する。

実装/レビュースレッドでは `/loop` や Codex automation を **同時に実行しないでください** —
これらは loopless receiver です。

### 各 orchestrator wake

権威ある wake プロンプトは intent-cli から生成します。各 wake は次を行うべきです:

- 設計側の進捗を確認（新しい packet/issue、intent status の変化）。
- 保留中の agmsg 返信を読む（シグナルのみ — intent-cli / GitHub に対して再検証）。
- intent-cli に worker 状態を問い合わせる（`worker next-action --github-only`）。
- host レビュー準備状況を確認（`automation host-review-preflight`）。
- GitHub の事実を直接検証: open PR、CI 結論、承認、マージ状態、closeout/label 状態。
- 停滞ブロッカーと無返信の receiver を検知する。
- この wake の単一アクションを決定する: 次の slice/PR を委譲、1 通の repair メッセージ送信、
  または 1 件のオペレーター判断にエスカレーション。

### repair と escalate

- **repair**: ルーチンな脱線状態は、適切なスレッドへメッセージを送って公式の intent-cli
  ワークフローに戻すことで自分で修復する — 停滞した receiver、`worker complete` を飛ばした、
  label を手動適用した、返信がない、など。ルーチンな復旧は repair メッセージであり
  エスカレーションではない。
- **escalate**: オペレーターへのエスカレーションは次の場合のみ — プロダクト/設計判断、
  認証情報やセキュリティ、破壊的なローカル操作、または解決不能な canonical な曖昧さ
  （intent-cli/GitHub の事実が本当に矛盾するか欠落している）。

### CI 待ち状態

pending/running の CI を持つ PR は **アクティブな待ち状態** であり、ブロッカーでは
ありません。GitHub checks が権威です。各 wake で必須チェックを再確認します。pending な CI
はそれ単独では request-update label、repair メッセージ、オペレーターへの質問を引き起こしません。
review / merge / closeout を委譲する直前には必ず必須チェックを再検証してください — 以前読んだ
green は古くなっている可能性があります。

- **pending / running** — 次の wake で待って再確認する。メッセージなし、request-update なし、
  オペレーター質問なし。PR を in-flight として追跡し、先へ進む。
- **green** — すべての必須チェックが通過。intent-cli review surface 経由で review/closeout を
  委譲する。委譲時に green を再検証する。
- **red** — 必須チェックが失敗。所有権でルーティング: 実装スレッドが直せる test/build/lint の
  失敗には 1 通の repair メッセージ。プロダクト/設計や canonical 判断が必要なものはエスカレーション。
  必須チェックが red の間は merge/closeout を委譲しない。
- **stuck / ambiguous** — チェックが開始されない、妥当な時間を大きく超えてハングする、または
  矛盾/不明なステータスを報告する。1 件のオペレーター判断にエスカレーション（fail closed）。
  green を推測しない。

## next-slice の publish

ルーチンな next-slice issue の publish は **orchestrator の責務** であり、オペレーターへの
質問ではありません。intent-cli が候補を `issue-cut-ready` と報告し、すべての安全ゲートを
通過したら、orchestrator はオペレーターに GitHub issue 作成を依頼して止まるのではなく、
canonical な intent-cli コマンドで自分で publish します。**1 wake につき最大 1 件** です。

次の **すべて** が成り立つときのみ publish します:

- same-domain コンテキスト、または明示的にルーティングされた multi-domain 委譲
  （明示ルーティングなしに cross-domain 候補を publish しない）;
- packet contract が完全（必須セクションの欠落なし）;
- open な clarification や contract の曖昧さがない;
- 依存が満たされている — 未 cut の依存より先に publish しない;
- WIP 上限内;
- host-sync / preflight がクリーンで、対象 repo/domain が一意。

それ以外は **hold またはエスカレーション** — 必須セクションの欠落、open clarification、
依存の不一致、WIP 上限到達、host-sync ブロッカー、対象 repo/domain の曖昧さはすべて
ブロッカーです。

publish は canonical な surface のみ — `intent-cli issue publish-flow` と
`intent-cli automation issue-publish` — を使い、生の `gh issue create` や
`gh ... --add-label` は使いません。publish 後は intent-cli / GitHub（チャットではなく）で
issue が期待どおりの body と `intent-target` label を持つこと、durable state がそれを
反映していることを検証し、その後 agmsg で実装を委譲します。実装 receiver は依然として
`intent-cli worker next-action` からターゲットを得ます（agmsg テキストからではありません）。

## single-domain と multi-domain のオーケストレーション

host チェックアウトは正当に **複数** の intent ドメインを含み得ます（例:
`sekiban-as-a-service`、`sekiban-wasm-runtime`、`intent-cli`）。さらに
**複数のドメインが同じ GitHub リポジトリを対象** にすることもあります。可視であることは
権限ではありません。そのため orchestrator は次の 2 モードのいずれかで動作します。

### single-domain orchestrator

- 選択したドメインのみがスコープ内。
- 同じ host repo に **可視** な他ドメインの queue 項目は **スコープ外** — たとえ
  同じリポジトリを対象にしていても、publish / delegate / repair してはいけません。
- 可視な他ドメイン項目を delegate 可能と見なすのではなく、domain/mode を切り替えるよう
  オペレーターにエスカレーションします。

### multi-domain orchestrator

- 意図的に複数ドメインを調整します。
- publish / delegate / review / repair の前に、**各委譲ごとに明示的なルーティング
  メタデータ** を要求します。
- 各 execution unit を、そのドメインのチェックアウトを所有するスレッドにのみ
  ルーティングします。

すべての multi-domain 委譲は次を伴わなければなりません:

- domain
- execution unit
- target repo
- implementation cwd/worktree
- review cwd/worktree
- base branch policy
- destination thread

委譲ペイロードの例（1 つの repo が 2 つのドメインに供給している点に注意）:

```json
{"delegate":{"domain":"sekiban-as-a-service","execution_unit":"G491","target_repo":"J-Tech-Japan/intent-system","impl_cwd":"/work/sekiban-saas","review_cwd":"/review/sekiban-saas","base_branch_policy":"direct-main","destination_thread":"implementation@sekiban-as-a-service"}}
```

### execution-unit の prefix はルーティングシグナルではない

ドメイン名と異なる execution-unit ID の prefix（例: 番号がドメインを符号化していない
`G###` ユニット）は、それ **単独では** wrong-repo シグナルでは **ありません**。
所有権の判断には prefix 文字列ではなく **packet/domain メタデータ** と
**ルーティングコンテキスト** を比較してください。

## 実装スレッド: claim 前にチェックアウトを検証する

実装スレッドは orchestrator の委譲で駆動されますが、worker のターゲットは依然として
受信側の `intent-cli worker next-action --repo <owner/repo> --github-only` から来ます
— agmsg のテキストからでは **ありません**。claim する前に:

1. ローカルチェックアウトのコンテキスト — cwd/worktree、git remote repo、委譲された
   domain — が、渡されたルーティングと一致することを検証する。
2. チェックアウトが委譲された repo/domain と一致しない場合は、claim せずに
   **停止して blocked を返す**。
3. prefix の不一致だけでは wrong-repo シグナルにならないことを忘れない。所有権は
   packet/domain メタデータとルーティングコンテキストで確認する。

実装スレッドは **GitHub-contract-only** を維持します。host メタデータ
（`.intent-cli/**`、`intents/**`）を読んだり変更したりしません。すべての label 遷移は
`intent-cli worker` / `intent-cli automation` を経由します。

## セーフティ境界（まとめ）

- agmsg はシグナル層のみ。intent-cli と GitHub がすべてのワークフロー状態の権威。
- 生の label 変更は禁止。すべての遷移は intent-cli worker/automation を経由。
- queue-state、runs ログ、packet、host メタデータの手編集は禁止。
- agmsg はセマンティックレビューを置き換えず、マージを認可しない。
- ドメイン分離: 可視であることは権限ではない。single-domain orchestrator は他ドメイン
  項目を無視/エスカレーションし、multi-domain orchestrator は委譲ごとの明示的ルーティング
  を要求する。
- orchestrator の重複や、シグナルが intent-cli/GitHub の事実と矛盾する場合は fail closed —
  推測せず、停止してエスカレーションする。
