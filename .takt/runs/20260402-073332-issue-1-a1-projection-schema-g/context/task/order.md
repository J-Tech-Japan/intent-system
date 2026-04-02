## Issue #1: [A1] Projection Schema

# Goal

`execution` の sub-slice を、`implementation_issue_packet` と `review_context_packet` に一意に写せる projection schema を実装可能な形で固定する。

# In Scope

- `A1` に必要な projection input / output field mapping
- sub-slice から packet への deterministic な変換規則
- issue packet と review context packet の必須 field 定義

# Out Of Scope

- Markdown / YAML の actual rendering 実装
- queue-state 更新ロジック
- workflow engine や takt adapter

# Target

- repo: `J-Tech-Japan/intent-system`
- path: `.`
- part: `projection schema`

# Dependencies

- none

# Intent References

- parent repo: [tomohisa/MyIntentHost](https://github.com/tomohisa/MyIntentHost)
- [Product Goal](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/intent-tree/purpose/04-product-goal.md)
- [Tooling Strategy](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/intent-tree/means/02-tooling-strategy.md)
- [Issue Ready Slices](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/execution/01-issue-ready-slices.md)
- [MVP Sub-Slices](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/execution/04-mvp-sub-slices.md)

# Rules And Specs

- [Issue Projection Format](https://github.com/tomohisa/MyIntentHost/blob/main/intents/rules/issue-projection-format.md)
- [Issue Template And Review Context](https://github.com/tomohisa/MyIntentHost/blob/main/intents/rules/issue-template-and-review-context.md)
- [Projection Schema](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/specs/01-projection-schema.md)
- [Bootstrap Manual Operation](https://github.com/tomohisa/MyIntentHost/blob/main/intents/intent-cli/rules/03-bootstrap-manual-operation.md)

# Acceptance Criteria

- sub-slice row から packet 生成に必要な field mapping が一意に決まる
- implementation packet と review packet の両方に必要な必須 field が固定される
- review packet から parent Intent root に戻れる

# Verification

- required evidence:
  - `contract-reviewed`
  - `tests-passing`
  - `acceptance-criteria-checked`

# Review Context

- parent intent root: `intents/intent-cli/intent-tree/00-map.md`
- deterministic review checks:
  - source of truth が parent Intent repo 側に残っている
  - projected packet にしかない重要仕様を作っていない
  - sub-slice から packet への mapping が再生成可能である
