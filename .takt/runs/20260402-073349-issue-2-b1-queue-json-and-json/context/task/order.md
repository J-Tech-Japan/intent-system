## Issue #2: [B1] Queue JSON And JSONL Schema

# Goal

`.intent-cli/queue-state.json` と `.intent-cli/runs.jsonl` の最小 schema を、selective block と run trace が復元できる形で固定する。

# In Scope

- queue current snapshot の最小 field 定義
- run history / transition log の append-only 形
- queue item から packet artifact path をたどるための field

# Out Of Scope

- dependency update の実装ロジック
- workflow engine 実行
- clarify artifact や interview artifact の詳細 schema

# Target

- repo: `J-Tech-Japan/intent-system`
- path: `.`
- part: `supervisor state model`

# Dependencies

- none

# Intent References

- parent repo: [tomohisa/MyIntentHost](https://github.com/tomohisa/MyIntentHost)
- [Product Goal](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/intent-tree/purpose/04-product-goal.md)
- [Persistence Strategy](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/intent-tree/means/05-persistence-strategy.md)
- [Issue Ready Slices](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/execution/01-issue-ready-slices.md)
- [MVP Sub-Slices](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/execution/04-mvp-sub-slices.md)

# Rules And Specs

- [Queue JSON And JSONL Schema](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md)
- [Config And Run Model](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/specs/08-config-and-run-model.md)
- [Bootstrap Manual Operation](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/rules/03-bootstrap-manual-operation.md)

# Acceptance Criteria

- selective block と dependency を current state で復元できる
- review / fix / clarify の遷移を JSONL から追跡できる
- queue item から packet artifact path をたどれる

# Verification

- required evidence:
  - `contract-reviewed`
  - `tests-passing`
  - `acceptance-criteria-checked`

# Review Context

- parent intent root: `intents/intent-cli/intent-tree/00-map.md`
- deterministic review checks:
  - current state と append-only history の責務が混ざっていない
  - queue item から packet path と return path が確実に引ける
  - commit 対象として扱っても diff が読める shape を保っている
