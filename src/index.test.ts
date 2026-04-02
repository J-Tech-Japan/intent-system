import { describe, expect, it } from 'vitest'

import {
  ImplementationIssuePacketSchema,
  ProjectionInputSchema,
  ReviewContextPacketSchema,
  projectToImplementationPacket,
  projectToReviewContextPacket,
} from './index.js'

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
    depends_on_subslices: ['A0-bootstrap'],
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

describe('public projection API', () => {
  it('Given a valid sub-slice row When projecting both packet types through the public API Then both packets stay schema-valid and consistent', () => {
    const input = createProjectionInput()

    expect(ProjectionInputSchema.parse(input)).toEqual(input)

    const implementationPacket = projectToImplementationPacket(input)
    const reviewPacket = projectToReviewContextPacket(input)

    expect(ImplementationIssuePacketSchema.parse(implementationPacket)).toEqual(implementationPacket)
    expect(ReviewContextPacketSchema.parse(reviewPacket)).toEqual(reviewPacket)
    expect(implementationPacket.source_execution_unit).toBe(reviewPacket.source_execution_unit)
    expect(implementationPacket.target_repo).toBe(reviewPacket.target_repo)
    expect(implementationPacket.target_path).toBe(reviewPacket.target_path)
    expect(implementationPacket.review_mode).toBe(reviewPacket.review_mode)
    expect(implementationPacket.intent_references).toEqual(reviewPacket.intent_references)
  })

  it('Given an invalid sub-slice row When projecting through the public API Then the input validation error surfaces', () => {
    const input = createProjectionInput({
      goal: undefined,
    }) as Parameters<typeof projectToImplementationPacket>[0]

    expect(() => projectToImplementationPacket(input)).toThrow()
    expect(() => projectToReviewContextPacket(input)).toThrow()
  })
})
