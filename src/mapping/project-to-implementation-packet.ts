import {
  ImplementationIssuePacketSchema,
  ProjectionInputSchema,
  type ImplementationIssuePacket,
  type ProjectionInput,
} from '../schema/index.js'
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

export function projectToImplementationPacket(input: ProjectionInput): ImplementationIssuePacket {
  const projectionInput = ProjectionInputSchema.parse(input)

  return ImplementationIssuePacketSchema.parse({
    issue_title: resolveIssueTitle(projectionInput),
    issue_kind: resolveIssueKind(projectionInput),
    source_execution_unit: projectionInput.source_execution_unit,
    goal: projectionInput.goal,
    target_repo: projectionInput.target_repo,
    target_part: projectionInput.target_part,
    target_path: projectionInput.target_path,
    review_mode: projectionInput.review_mode,
    completion_action: projectionInput.completion_action,
    landing_policy: projectionInput.landing_policy,
    parent_intent_root: resolveParentIntentRoot(projectionInput),
    dependencies: resolveDependencies(projectionInput),
    intent_references: resolveIntentReferences(projectionInput),
    rules_and_specs: resolveRulesAndSpecs(projectionInput),
    acceptance_criteria: resolveAcceptanceCriteria(projectionInput),
    in_scope: resolveInScope(projectionInput),
    out_of_scope: resolveOutOfScope(projectionInput),
  })
}
