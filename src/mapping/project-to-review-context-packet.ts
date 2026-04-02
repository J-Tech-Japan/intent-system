import {
  ProjectionInputSchema,
  ReviewContextPacketSchema,
  type ProjectionInput,
  type ReviewContextPacket,
} from '../schema/index.js'
import {
  resolveIntentReferences,
  resolveParentIntentRoot,
  resolveRulesAndSpecs,
} from './field-resolvers.js'

export function projectToReviewContextPacket(input: ProjectionInput): ReviewContextPacket {
  const projectionInput = ProjectionInputSchema.parse(input)

  return ReviewContextPacketSchema.parse({
    source_execution_unit: projectionInput.source_execution_unit,
    target_repo: projectionInput.target_repo,
    target_path: projectionInput.target_path,
    review_mode: projectionInput.review_mode,
    parent_intent_root: resolveParentIntentRoot(projectionInput),
    intent_references: resolveIntentReferences(projectionInput),
    rules_and_specs: resolveRulesAndSpecs(projectionInput),
  })
}
