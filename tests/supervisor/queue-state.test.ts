import { describe, expect, test } from 'vitest'

import {
  findItemByUnit,
  findItemsByState,
  getBlockedItems,
  parseQueueState,
  resolvePacketPaths,
  serializeQueueState,
} from '../../src/supervisor/queue-state.js'
import { createQueueItem, createQueueState } from './fixtures.js'

describe('parseQueueState', () => {
  test('should parse a queue snapshot with dependency and block metadata', () => {
    const queueState = createQueueState({
      items: [
        createQueueItem({
          state: 'blocked',
          dependencies: ['issue-1-a1'],
          blocked_by: ['issue-1-a1'],
        }),
      ],
    })
    const json = JSON.stringify(queueState)

    const parsedState = parseQueueState(json)

    expect(parsedState).toEqual(queueState)
  })

  test('should parse a queue snapshot with an optional linked issue', () => {
    const queueState = createQueueState({
      items: [
        createQueueItem({
          linked_issue: {
            repo: 'J-Tech-Japan/intent-system',
            number: 2,
            url: 'https://github.com/J-Tech-Japan/intent-system/issues/2',
          },
        }),
      ],
    })
    const json = JSON.stringify(queueState)

    const parsedState = parseQueueState(json)

    expect(parsedState.items[0]?.linked_issue).toEqual(queueState.items[0]?.linked_issue)
  })

  test('should reject queue snapshots with an unsupported schema version', () => {
    const json = JSON.stringify({
      ...createQueueState(),
      schema_version: '2',
    })

    const parse = () => parseQueueState(json)

    expect(parse).toThrow()
  })

  test('should reject queue snapshots with a non-iso updated timestamp', () => {
    const json = JSON.stringify({
      ...createQueueState(),
      updated_at: '2026/04/02 07:33:49',
    })

    const parse = () => parseQueueState(json)

    expect(parse).toThrow()
  })

  test('should reject queue snapshots with an unknown state value', () => {
    const json = JSON.stringify({
      ...createQueueState(),
      items: [
        {
          ...createQueueItem(),
          state: 'paused',
        },
      ],
    })

    const parse = () => parseQueueState(json)

    expect(parse).toThrow()
  })
})

describe('serializeQueueState', () => {
  test('should write diff-friendly indented json with a trailing newline', () => {
    const queueState = createQueueState()

    const serializedState = serializeQueueState(queueState)

    expect(serializedState).toBe(`${JSON.stringify(queueState, null, 2)}\n`)
  })
})

describe('queue-state queries', () => {
  test('should return the queue item that matches an execution unit', () => {
    const queueState = createQueueState({
      items: [
        createQueueItem({ execution_unit: 'issue-1-a1' }),
        createQueueItem({ execution_unit: 'issue-2-b1', state: 'active' }),
      ],
    })

    const item = findItemByUnit(queueState, 'issue-2-b1')

    expect(item).toEqual(queueState.items[1])
  })

  test('should filter queue items by state', () => {
    const queueState = createQueueState({
      items: [
        createQueueItem({ execution_unit: 'issue-1-a1', state: 'review' }),
        createQueueItem({ execution_unit: 'issue-2-b1', state: 'blocked' }),
        createQueueItem({ execution_unit: 'issue-3-c1', state: 'review' }),
      ],
    })

    const reviewItems = findItemsByState(queueState, 'review')

    expect(reviewItems).toEqual([queueState.items[0], queueState.items[2]])
  })

  test('should return only items that are blocked by another execution unit', () => {
    const blockedItem = createQueueItem({
      execution_unit: 'issue-2-b1',
      state: 'blocked',
      blocked_by: ['issue-1-a1'],
    })
    const queueState = createQueueState({
      items: [
        createQueueItem({ execution_unit: 'issue-1-a1', blocked_by: [] }),
        blockedItem,
      ],
    })

    const blockedItems = getBlockedItems(queueState)

    expect(blockedItems).toEqual([blockedItem])
  })

  test('should expose packet artifact paths from a queue item', () => {
    const queueItem = createQueueItem()

    const packetPaths = resolvePacketPaths(queueItem)

    expect(packetPaths).toEqual(queueItem.packet_paths)
  })
})
