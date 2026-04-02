import { z } from 'zod'

import { IssueKindSchema } from './issue-kind.js'

const stringListSchema = z.array(z.string())

export const ImplementationIssuePacketSchema = z.object({
  issue_title: z.string().min(1),
  issue_kind: IssueKindSchema,
  source_execution_unit: z.string().min(1),
  goal: z.string().min(1),
  target_repo: z.string().min(1),
  target_part: z.string().min(1),
  target_path: z.string().min(1),
  review_mode: z.string().min(1),
  completion_action: z.string().min(1),
  landing_policy: z.string().min(1),
  parent_intent_root: z.string().min(1),
  dependencies: stringListSchema,
  intent_references: stringListSchema,
  rules_and_specs: stringListSchema,
  acceptance_criteria: stringListSchema.min(1),
  in_scope: stringListSchema.min(1),
  out_of_scope: stringListSchema,
})

export type ImplementationIssuePacket = z.infer<typeof ImplementationIssuePacketSchema>
