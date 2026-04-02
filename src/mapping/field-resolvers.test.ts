import { describe, expect, it } from 'vitest'

import {
  resolveAcceptanceCriteria,
  resolveDependencies,
  resolveInScope,
  resolveIntentReferences,
  resolveIssueKind,
  resolveIssueTitle,
  resolveOutOfScope,
  resolveParentIntentRoot,
  resolveRulesAndSpecs,
} from './field-resolvers.js'

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
      'intents/intent-cli/notes/ignore-me.md',
    ],
    parent_intent_root: 'intents/intent-cli/intent-tree/00-map.md',
    ...overrides,
  }
}

describe('field resolvers', () => {
  it('Given both dependency sources When resolving dependencies Then depends_on_subslices takes precedence', () => {
    const input = createProjectionInput()

    expect(resolveDependencies(input)).toEqual(['A0-bootstrap'])
  })

  it('Given related intents and source concepts When resolving intent references Then it appends source concepts after related intents', () => {
    const input = createProjectionInput()

    expect(resolveIntentReferences(input)).toEqual([
      'intents/intent-cli/intent-tree/00-map.md',
      'intents/rules/issue-projection-format.md',
      'intents/intent-cli/specs/01-projection-schema.md',
      'intents/intent-cli/designs/projection-packets.md',
      'intents/intent-cli/notes/ignore-me.md',
    ])
  })

  it('Given explicit rules_and_specs When resolving rules and specs Then it preserves the explicit list', () => {
    const input = createProjectionInput({
      rules_and_specs: ['docs/custom-rule.md'],
    })

    expect(resolveRulesAndSpecs(input)).toEqual(['docs/custom-rule.md'])
  })

  it('Given source concepts without explicit rules_and_specs When resolving rules and specs Then it extracts only rule spec and design paths', () => {
    const input = createProjectionInput({
      rules_and_specs: undefined,
    })

    expect(resolveRulesAndSpecs(input)).toEqual([
      'intents/rules/issue-projection-format.md',
      'intents/intent-cli/specs/01-projection-schema.md',
      'intents/intent-cli/designs/projection-packets.md',
    ])
  })

  it('Given success_signal When resolving acceptance criteria Then it returns a single-item array', () => {
    const input = createProjectionInput({
      success_signal: 'tests-passing',
    })

    expect(resolveAcceptanceCriteria(input)).toEqual(['tests-passing'])
  })

  it('Given explicit scope boundaries When resolving scope Then it preserves both arrays', () => {
    const input = createProjectionInput({
      in_scope: ['projection input mapping'],
      out_of_scope: ['yaml rendering'],
    })

    expect(resolveInScope(input)).toEqual(['projection input mapping'])
    expect(resolveOutOfScope(input)).toEqual(['yaml rendering'])
  })

  it('Given no explicit scope boundaries When resolving scope Then it creates the minimal in-scope boundary and an empty out-of-scope list', () => {
    const input = createProjectionInput({
      in_scope: undefined,
      out_of_scope: undefined,
    })

    expect(resolveInScope(input)).toEqual(['projection schema'])
    expect(resolveOutOfScope(input)).toEqual([])
  })

  it('Given no explicit issue metadata When resolving issue fields Then it derives the title and defaults the kind to feature', () => {
    const input = createProjectionInput({
      issue_title: undefined,
      issue_kind: undefined,
    })

    expect(resolveIssueTitle(input)).toContain('A1 Projection Schema')
    expect(resolveIssueTitle(input)).toContain('Fix the projection schema contract')
    expect(resolveIssueKind(input)).toBe('feature')
  })

  it('Given a valid projection input When resolving the parent intent root Then it preserves the required value', () => {
    const input = createProjectionInput()

    expect(resolveParentIntentRoot(input)).toBe('intents/intent-cli/intent-tree/00-map.md')
  })

  it('Given no source concepts When resolving references Then it falls back to the explicit related intents only', () => {
    const input = createProjectionInput({
      source_concepts: undefined,
    })

    expect(resolveIntentReferences(input)).toEqual(['intents/intent-cli/intent-tree/00-map.md'])
    expect(resolveRulesAndSpecs(input)).toEqual([])
  })
})
