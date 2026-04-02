import { z } from 'zod'

import { queueItemStateSchema, type QueueItemState } from './state.js'

export const queueStateSchemaVersion = '1'

const linkedIssueSchema = z.object({
  repo: z.string(),
  number: z.number().int(),
  url: z.string().url(),
})

const packetPathsSchema = z.object({
  implementation: z.string(),
  review_context: z.string(),
  yaml: z.string(),
})

const queueItemSchema = z.object({
  execution_unit: z.string(),
  title: z.string(),
  state: queueItemStateSchema,
  dependencies: z.array(z.string()),
  blocked_by: z.array(z.string()),
  clarification_return_path: z.string(),
  packet_paths: packetPathsSchema,
  linked_issue: linkedIssueSchema.optional(),
  worker_role: z.string(),
  review_role: z.string(),
  priority: z.string(),
})

const queueStateSchema = z.object({
  schema_version: z.literal(queueStateSchemaVersion),
  updated_at: z.string().datetime(),
  items: z.array(queueItemSchema),
})

export type LinkedIssue = z.infer<typeof linkedIssueSchema>
export type PacketPaths = z.infer<typeof packetPathsSchema>
export type QueueItem = z.infer<typeof queueItemSchema>
export type QueueState = z.infer<typeof queueStateSchema>

export function parseQueueState(json: string): QueueState {
  return queueStateSchema.parse(JSON.parse(json))
}

export function serializeQueueState(state: QueueState): string {
  return `${JSON.stringify(queueStateSchema.parse(state), null, 2)}\n`
}

export function findItemByUnit(state: QueueState, unit: string): QueueItem | undefined {
  return state.items.find((item) => item.execution_unit === unit)
}

export function findItemsByState(state: QueueState, targetState: QueueItemState): QueueItem[] {
  return state.items.filter((item) => item.state === targetState)
}

export function getBlockedItems(state: QueueState): QueueItem[] {
  return state.items.filter((item) => item.blocked_by.length > 0)
}

export function resolvePacketPaths(item: QueueItem): PacketPaths {
  return item.packet_paths
}
