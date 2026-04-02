import { describe, expect, it } from 'vitest'

import { ProjectionInputSchema } from './projection-input.js'

describe('ProjectionInputSchema', () => {
  it('Given the required projection fields When parsing Then it accepts the input', () => {
    const input = {
      source_execution_unit: 'A1 Projection Schema',
      goal: 'Fix the projection schema contract',
      target_repo: 'submodules/intent-system',
      target_part: 'projection schema',
      target_path: 'src/projection',
      success_signal: 'contract-reviewed',
      review_mode: 'deterministic',
      completion_action: 'open-pr',
      landing_policy: 'manual',
      parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    }

    expect(ProjectionInputSchema.parse(input)).toEqual(input)
  })

  it('Given a missing source_execution_unit When parsing Then it rejects the input', () => {
    const input = {
      goal: 'Fix the projection schema contract',
      target_repo: 'submodules/intent-system',
      target_part: 'projection schema',
      target_path: 'src/projection',
      success_signal: 'contract-reviewed',
      review_mode: 'deterministic',
      completion_action: 'open-pr',
      landing_policy: 'manual',
      parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    }

    expect(() => ProjectionInputSchema.parse(input)).toThrow()
  })

  it('Given an unsupported issue_kind When parsing Then it rejects the input', () => {
    const input = {
      source_execution_unit: 'A1 Projection Schema',
      goal: 'Fix the projection schema contract',
      target_repo: 'submodules/intent-system',
      target_part: 'projection schema',
      target_path: 'src/projection',
      success_signal: 'contract-reviewed',
      review_mode: 'deterministic',
      completion_action: 'open-pr',
      landing_policy: 'manual',
      parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
      issue_kind: 'unsupported-kind',
    }

    expect(() => ProjectionInputSchema.parse(input)).toThrow()
  })

  it('Given omitted source_concepts When parsing Then it still accepts the input', () => {
    const input = {
      source_execution_unit: 'A1 Projection Schema',
      goal: 'Fix the projection schema contract',
      target_repo: 'submodules/intent-system',
      target_part: 'projection schema',
      target_path: 'src/projection',
      success_signal: 'contract-reviewed',
      review_mode: 'deterministic',
      completion_action: 'open-pr',
      landing_policy: 'manual',
      parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    }

    expect(ProjectionInputSchema.parse(input)).toEqual(input)
  })
})
