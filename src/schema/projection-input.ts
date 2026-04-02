import { z } from 'zod'

import { IssueKindSchema } from './issue-kind.js'

const stringListSchema = z.array(z.string())

export const ProjectionInputSchema = z.object({
  source_execution_unit: z.string().min(1),
  goal: z.string().min(1),
  target_repo: z.string().min(1),
  target_part: z.string().min(1),
  target_path: z.string().min(1),
  success_signal: z.string().min(1),
  source_concepts: stringListSchema.optional(),
  review_mode: z.string().min(1),
  completion_action: z.string().min(1),
  landing_policy: z.string().min(1),
  depends_on: stringListSchema.optional(),
  depends_on_subslices: stringListSchema.optional(),
  related_intents: stringListSchema.optional(),
  rules_and_specs: stringListSchema.optional(),
  in_scope: stringListSchema.optional(),
  out_of_scope: stringListSchema.optional(),
  issue_title: z.string().min(1).optional(),
  issue_kind: IssueKindSchema.optional(),
  parent_intent_root: z.string().min(1),
})

export type ProjectionInput = z.infer<typeof ProjectionInputSchema>
