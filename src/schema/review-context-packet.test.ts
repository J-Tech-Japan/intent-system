import { describe, expect, it } from 'vitest'

import { projectToReviewContextPacket } from '../mapping/project-to-review-context-packet.js'
import { ReviewContextPacketSchema } from './review-context-packet.js'

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
    ],
    parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    ...overrides,
  }
}

describe('ReviewContextPacketSchema', () => {
  it('Given a projected review context packet When parsing Then it accepts the packet', () => {
    const packet = projectToReviewContextPacket(createProjectionInput())

    expect(ReviewContextPacketSchema.parse(packet)).toEqual(packet)
  })

  it('Given a missing parent_intent_root When parsing Then it rejects the packet', () => {
    const packet = projectToReviewContextPacket(createProjectionInput())
    const { parent_intent_root, ...packetWithoutParentRoot } = packet

    expect(parent_intent_root).toBe('intents/intent-cli/intent-tree/00-map.md')
    expect(() => ReviewContextPacketSchema.parse(packetWithoutParentRoot)).toThrow()
  })

  it('Given a projected review context packet When parsing Then it exposes the full 7-field contract', () => {
    const packet = projectToReviewContextPacket(createProjectionInput())

    expect(Object.keys(packet)).toHaveLength(7)
    expect(packet.target_path).toBe('src/projection')
    expect(packet.review_mode).toBe('deterministic')
  })
})
