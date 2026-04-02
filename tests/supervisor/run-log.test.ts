import { describe, expect, test } from 'vitest'

import {
  appendRunEvent,
  filterByUnit,
  getTransitionHistory,
  parseRunLog,
  serializeRunEvent,
} from '../../src/supervisor/run-log.js'
import { createRunEvent } from './fixtures.js'

describe('parseRunLog', () => {
  test('should parse an append-only run history that includes resumed events', () => {
    const events = [
      createRunEvent({ event: 'review' }),
      createRunEvent({
        ts: '2026-04-02T07:40:49.000Z',
        event: 'resumed',
      }),
    ]
    const jsonl = `${events.map((event) => JSON.stringify(event)).join('\n')}\n`

    const parsedEvents = parseRunLog(jsonl)

    expect(parsedEvents).toEqual(events)
  })

  test('should reject run history lines with an unknown event', () => {
    const jsonl = `${JSON.stringify({
      ...createRunEvent(),
      event: 'started',
    })}\n`

    const parse = () => parseRunLog(jsonl)

    expect(parse).toThrow()
  })

  test('should reject run history lines with a non-iso timestamp', () => {
    const jsonl = `${JSON.stringify({
      ...createRunEvent(),
      ts: '2026/04/02 07:33:49',
    })}\n`

    const parse = () => parseRunLog(jsonl)

    expect(parse).toThrow()
  })
})

describe('run-log serialization', () => {
  test('should serialize a single run event without a trailing newline', () => {
    const event = createRunEvent({ event: 'fixing' })

    const serializedEvent = serializeRunEvent(event)

    expect(serializedEvent).toBe(JSON.stringify(event))
  })

  test('should append a run event as a new jsonl line', () => {
    const existingTrace = `${JSON.stringify(createRunEvent({ event: 'review' }))}\n`
    const newEvent = createRunEvent({
      ts: '2026-04-02T07:45:49.000Z',
      event: 'fixing',
    })

    const updatedTrace = appendRunEvent(existingTrace, newEvent)

    expect(updatedTrace).toBe(`${existingTrace}${JSON.stringify(newEvent)}\n`)
  })

  test('should append the first run event to an empty trace', () => {
    const newEvent = createRunEvent({ event: 'queued' })

    const updatedTrace = appendRunEvent('', newEvent)

    expect(updatedTrace).toBe(`${JSON.stringify(newEvent)}\n`)
  })
})

describe('run-log queries', () => {
  test('should filter events by execution unit', () => {
    const events = [
      createRunEvent({ execution_unit: 'issue-1-a1', event: 'queued' }),
      createRunEvent({ execution_unit: 'issue-2-b1', event: 'review' }),
      createRunEvent({ execution_unit: 'issue-2-b1', event: 'fixing' }),
    ]

    const filteredEvents = filterByUnit(events, 'issue-2-b1')

    expect(filteredEvents).toEqual([events[1], events[2]])
  })

  test('should return transition history for an execution unit in chronological order', () => {
    const events = [
      createRunEvent({ execution_unit: 'issue-1-a1', event: 'queued' }),
      createRunEvent({ execution_unit: 'issue-2-b1', event: 'review' }),
      createRunEvent({
        execution_unit: 'issue-2-b1',
        ts: '2026-04-02T07:45:49.000Z',
        event: 'resumed',
      }),
    ]

    const history = getTransitionHistory(events, 'issue-2-b1')

    expect(history).toEqual([events[1], events[2]])
  })
})
