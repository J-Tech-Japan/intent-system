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
