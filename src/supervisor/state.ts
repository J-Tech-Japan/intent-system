import { z } from 'zod'

export const queueItemStateValues = [
  'queued',
  'active',
  'review',
  'fixing',
  'clarify-blocked',
  'blocked',
  'completed',
] as const

export const queueItemStateSchema = z.enum(queueItemStateValues)

export type QueueItemState = z.infer<typeof queueItemStateSchema>
