import { describe, expect, it } from 'vitest'

import { ImplementationIssuePacketSchema } from '../schema/implementation-issue-packet.js'
import { projectToImplementationPacket } from './project-to-implementation-packet.js'

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
    depends_on: ['A0-plan'],
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

describe('projectToImplementationPacket', () => {
  it('Given explicit projection overrides When projecting Then it preserves explicit packet fields', () => {
    const input = createProjectionInput({
      issue_title: '[A1] Projection Schema',
      issue_kind: 'bugfix',
      rules_and_specs: ['docs/custom-rule.md'],
      in_scope: ['projection input mapping'],
      out_of_scope: ['packet rendering'],
    })

    const packet = projectToImplementationPacket(input)

    expect(packet.issue_title).toBe('[A1] Projection Schema')
    expect(packet.issue_kind).toBe('bugfix')
    expect(packet.source_execution_unit).toBe('A1 Projection Schema')
    expect(packet.goal).toBe('Fix the projection schema contract')
    expect(packet.target_repo).toBe('submodules/intent-system')
    expect(packet.target_part).toBe('projection schema')
    expect(packet.target_path).toBe('src/projection')
    expect(packet.review_mode).toBe('deterministic')
    expect(packet.completion_action).toBe('open-pr')
    expect(packet.landing_policy).toBe('manual')
    expect(packet.parent_intent_root).toBe('intents/intent-cli/intent-tree/00-map.md')
    expect(packet.dependencies).toEqual(['A0-bootstrap'])
    expect(packet.intent_references).toEqual([
      'intents/intent-cli/intent-tree/00-map.md',
      'intents/rules/issue-projection-format.md',
      'intents/intent-cli/specs/01-projection-schema.md',
      'intents/intent-cli/designs/projection-packets.md',
    ])
    expect(packet.rules_and_specs).toEqual(['docs/custom-rule.md'])
    expect(packet.acceptance_criteria).toEqual(['contract-reviewed'])
    expect(packet.in_scope).toEqual(['projection input mapping'])
    expect(packet.out_of_scope).toEqual(['packet rendering'])
    expect(ImplementationIssuePacketSchema.parse(packet)).toEqual(packet)
  })

  it('Given omitted optional projection fields When projecting Then it derives deterministic defaults', () => {
    const input = createProjectionInput({
      depends_on_subslices: undefined,
      related_intents: undefined,
      rules_and_specs: undefined,
      in_scope: undefined,
      out_of_scope: undefined,
      issue_title: undefined,
      issue_kind: undefined,
      source_concepts: undefined,
    })

    const packet = projectToImplementationPacket(input)

    expect(packet.dependencies).toEqual(['A0-plan'])
    expect(packet.intent_references).toEqual([])
    expect(packet.rules_and_specs).toEqual([])
    expect(packet.acceptance_criteria).toEqual(['contract-reviewed'])
    expect(packet.in_scope).toEqual(['projection schema'])
    expect(packet.out_of_scope).toEqual([])
    expect(packet.issue_title).toContain('A1 Projection Schema')
    expect(packet.issue_title).toContain('Fix the projection schema contract')
    expect(packet.issue_kind).toBe('feature')
  })
})
