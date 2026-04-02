import { describe, expect, it } from 'vitest'

import { ReviewContextPacketSchema } from '../schema/review-context-packet.js'
import { projectToReviewContextPacket } from './project-to-review-context-packet.js'

function createProjectionInput(overrides = {}) {
  return {
    source_execution_unit: 'A1 Projection Schema',
    goal: 'Fix the projection schema contract',
    target_repo: 'submodules/intent-system',
    target_part: 'projection schema',
    target_path: 'src/projection',
    success_signal: 'contract-reviewed',
    review_mode: 'deterministic',
    completion_action: 'open-pr',
    landing_policy: 'manual',
    related_intents: ['intents/intent-cli/intent-tree/00-map.md'],
    source_concepts: [
      'intents/rules/issue-projection-format.md',
      'intents/intent-cli/specs/01-projection-schema.md',
      'intents/intent-cli/designs/projection-packets.md',
    ],
    parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    ...overrides,
  }
}

describe('projectToReviewContextPacket', () => {
  it('Given an explicit parent intent root When projecting Then it preserves the review context fields', () => {
    const input = createProjectionInput({
      parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
      rules_and_specs: ['docs/custom-rule.md'],
    })

    const packet = projectToReviewContextPacket(input)

    expect(packet.source_execution_unit).toBe('A1 Projection Schema')
    expect(packet.target_repo).toBe('submodules/intent-system')
    expect(packet.target_path).toBe('src/projection')
    expect(packet.review_mode).toBe('deterministic')
    expect(packet.parent_intent_root).toBe('intents/intent-cli/intent-tree/00-map.md')
    expect(packet.intent_references).toEqual([
      'intents/intent-cli/intent-tree/00-map.md',
      'intents/rules/issue-projection-format.md',
      'intents/intent-cli/specs/01-projection-schema.md',
      'intents/intent-cli/designs/projection-packets.md',
    ])
    expect(packet.rules_and_specs).toEqual(['docs/custom-rule.md'])
    expect(ReviewContextPacketSchema.parse(packet)).toEqual(packet)
  })

  it('Given no parent intent root When projecting Then it surfaces the input validation error', () => {
    const input = createProjectionInput({
      parent_intent_root: undefined,
      rules_and_specs: undefined,
      related_intents: undefined,
    }) as Parameters<typeof projectToReviewContextPacket>[0]

    expect(() => projectToReviewContextPacket(input)).toThrow()
  })
})
