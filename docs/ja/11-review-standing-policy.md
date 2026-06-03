# レビュー standing-policy レジストリ (G451)

レビューループは、本来ドメインや wave 単位で安定しているはずのポリシー質問で
繰り返し停止しがちです — draft PR の扱い、device/operator/hardware-gated な
証跡ギャップ、外部 issue/PR の intake、テストが「十分」と言える条件、受理した
ギャップの追跡方法など。standing-policy レジストリはこれらの回答を **データ**
として記述できるようにし、`intent-cli guide review` がパケットごとに operator
へ再質問せず決定論的な判断を返せるようにします。

## 任意であり、デフォルトで安全

- **ポリシーファイルなし → 組み込みの安全なデフォルト。**
  `.intent-cli/review-policy.json` が無いホストは従来どおりに動作します。
  既存ホストの移行は不要です。
- **不正なポリシーファイル → fail-closed でデフォルトへ。** ファイルが欠落・空・
  不正な JSON の場合でも `guide review` は成功し、組み込みデフォルトを使い、
  warning を記録します。クラッシュせず、operator clarification を黙って外す
  こともありません。
- **読み取り専用。** ポリシー解決はファイルを書き込まず、ホスト状態を変更
  しません。

`guide review` は解決元を `review_policy_source` で報告します:
`built-in-default` / `domain-file` / `invalid-fallback-default`。

## ポリシーの追加

ホスト repo ルートに `.intent-cli/review-policy.json` を作成します。各セクションは
任意で、省略（または空）のセクションは安全なデフォルトを保持します。部分的な
ファイルでガイダンスが失われることはありません。

```json
{
  "domain": "intent-cli",
  "device_gated_evidence": {
    "approve_with_recorded_gap_allowed": true,
    "hard_block_categories": ["safety", "security", "data-loss", "payment", "primary-deliverable"],
    "rules": [
      "任意: デフォルトの device-gap ルールをドメイン固有の文言に置き換え。"
    ]
  },
  "draft_handling":          { "rules": ["draft PR はレビュー可能ではない。まず ready-for-review を要求。"] },
  "external_artifact_intake":{ "rules": ["外部 (非 intent-target) の issue/PR は host が publish 経路で昇格するまで intake のみ。"] },
  "test_evidence_sufficiency":{ "rules": ["テスト合格は必要だが十分ではない。contract 句 1 つと intent reference 1 つを再記述。"] },
  "follow_up_tracking":      { "rules": ["受理したギャップは必ず durable に追跡 (PR コメント / closeout note / follow-up issue)。"] }
}
```

### device-gated evidence

`device_gated_evidence` セクションは繰り返し発生する device-gap 判断を制御します:

- `approve_with_recorded_gap_allowed` (bool) — 通常の device-gap を
  **approve-with-recorded-gap** できるか（欠落証跡が純粋に device/automation の
  制約で、コード適合がそれ以外で検証済みの場合）。このドメインで全 device-gap を
  hard-block にしたい場合は `false`。
- `hard_block_categories` (string list) — 常に hard-block するカテゴリ
  （approve-with-gap 不可）。例: `safety`, `security`, `data-loss`, `payment`,
  `primary-deliverable`。
- `rules` (string list) — 任意の人間可読ルール文。省略時は組み込みの
  device-gap ルールが適用されます。

## OSS ユーザー向けスコープ指針

- ドメイン／wave 単位で本当に安定しているポリシーのみ記述する。
- 高リスク承認は gate を維持する。safety/security/data-loss/payment の承認を
  自動化するポリシーは設定しない。
- 単一プロジェクト固有のポリシーをグローバルルールのように埋め込まない。
  ファイルは host／domain 単位。
- 判断が真に新規・曖昧な場合、ポリシーは operator clarification を外しません。
  従来どおり surface してください。
