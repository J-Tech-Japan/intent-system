import { describe, expect, test } from 'vitest'

import {
  appendRunEvent,
  findItemByUnit,
  getTransitionHistory,
  parseQueueState,
  parseRunLog,
  resolvePacketPaths,
  serializeQueueState,
} from '../../src/supervisor/index.js'
import { createQueueItem, createQueueState, createRunEvent } from './fixtures.js'

describe('supervisor public api', () => {
  test('should reconstruct queue state and run history for the same execution unit', () => {
    const queueState = createQueueState({
      items: [
        createQueueItem({
          execution_unit: 'issue-2-b1',
          state: 'blocked',
          blocked_by: ['issue-1-a1'],
        }),
      ],
    })
    const initialHistory = `${JSON.stringify(
      createRunEvent({
        execution_unit: 'issue-2-b1',
        event: 'review',
      }),
    )}\n`
    const resumedEvent = createRunEvent({
      execution_unit: 'issue-2-b1',
      ts: '2026-04-02T07:45:49.000Z',
      event: 'resumed',
    })

    const parsedQueueState = parseQueueState(serializeQueueState(queueState))
    const queueItem = findItemByUnit(parsedQueueState, 'issue-2-b1')
    expect(queueItem).toBeDefined()
    const packetPaths = resolvePacketPaths(queueItem!)
    const events = parseRunLog(appendRunEvent(initialHistory, resumedEvent))
    const history = getTransitionHistory(events, 'issue-2-b1')

    expect(queueItem?.blocked_by).toEqual(['issue-1-a1'])
    expect(packetPaths).toEqual(queueState.items[0]?.packet_paths)
    expect(history.map((event) => event.event)).toEqual(['review', 'resumed'])
  })
})
