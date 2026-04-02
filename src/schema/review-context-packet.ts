import { z } from 'zod'

const stringListSchema = z.array(z.string())

export const ReviewContextPacketSchema = z.object({
  source_execution_unit: z.string().min(1),
  target_repo: z.string().min(1),
  target_path: z.string().min(1),
  review_mode: z.string().min(1),
  parent_intent_root: z.string().min(1),
  intent_references: stringListSchema,
  rules_and_specs: stringListSchema,
})

export type ReviewContextPacket = z.infer<typeof ReviewContextPacketSchema>
