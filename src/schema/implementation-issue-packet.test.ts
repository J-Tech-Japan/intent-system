import { describe, expect, it } from 'vitest'

import { projectToImplementationPacket } from '../mapping/project-to-implementation-packet.js'
import { ImplementationIssuePacketSchema } from './implementation-issue-packet.js'

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
    ],
    parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    ...overrides,
  }
}

describe('ImplementationIssuePacketSchema', () => {
  it('Given a projected implementation packet When parsing Then it accepts the packet', () => {
    const packet = projectToImplementationPacket(createProjectionInput())

    expect(ImplementationIssuePacketSchema.parse(packet)).toEqual(packet)
  })

  it('Given an unsupported issue_kind When parsing Then it rejects the packet', () => {
    const packet = projectToImplementationPacket(createProjectionInput())

    expect(() =>
      ImplementationIssuePacketSchema.parse({
        ...packet,
        issue_kind: 'unsupported-kind',
      }),
    ).toThrow()
  })

  it('Given a projected implementation packet When parsing Then it exposes the full 17-field contract', () => {
    const packet = projectToImplementationPacket(createProjectionInput())

    expect(Object.keys(packet)).toHaveLength(17)
    expect(packet.target_path).toBe('src/projection')
    expect(packet.review_mode).toBe('deterministic')
    expect(packet.completion_action).toBe('open-pr')
    expect(packet.landing_policy).toBe('manual')
    expect(packet.parent_intent_root).toBe('intents/intent-cli/intent-tree/00-map.md')
  })
})
