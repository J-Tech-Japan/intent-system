import type { QueueItem, QueueState } from '../../src/supervisor/queue-state.js'
import type { RunEvent } from '../../src/supervisor/run-log.js'

const createPacketPaths = (overrides: Partial<QueueItem['packet_paths']> = {}): QueueItem['packet_paths'] => ({
  implementation: 'packets/issue-2-b1/implementation.md',
  review_context: 'packets/issue-2-b1/review-context.md',
  yaml: 'packets/issue-2-b1/packet.yaml',
  ...overrides,
})

export const createQueueItem = (overrides: Partial<QueueItem> = {}): QueueItem => ({
  execution_unit: 'issue-2-b1',
  title: 'Queue JSON And JSONL Schema',
  state: 'queued',
  dependencies: [],
  blocked_by: [],
  clarification_return_path: 'artifacts/clarify/issue-2-b1.md',
  packet_paths: createPacketPaths(),
  worker_role: 'coder',
  review_role: 'reviewer',
  priority: 'high',
  ...overrides,
})

export const createQueueState = (overrides: Partial<QueueState> = {}): QueueState => ({
  schema_version: '1',
  updated_at: '2026-04-02T07:33:49.000Z',
  items: [createQueueItem()],
  ...overrides,
})

export const createRunEvent = (overrides: Partial<RunEvent> = {}): RunEvent => ({
  ts: '2026-04-02T07:33:49.000Z',
  execution_unit: 'issue-2-b1',
  event: 'queued',
  by: 'supervisor',
  ...overrides,
})
