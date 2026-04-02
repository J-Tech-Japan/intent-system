import { describe, expect, test } from 'vitest'

import { queueItemStateSchema, queueItemStateValues } from '../../src/supervisor/state.js'

describe('queueItemStateSchema', () => {
  test('should expose the documented queue item states in order', () => {
    const expectedStates = [
      'queued',
      'active',
      'review',
      'fixing',
      'clarify-blocked',
      'blocked',
      'completed',
    ]

    const actualStates = queueItemStateValues

    expect(actualStates).toEqual(expectedStates)
  })

  test('should parse every documented queue item state', () => {
    const validStates = queueItemStateValues

    const parsedStates = validStates.map((state) => queueItemStateSchema.parse(state))

    expect(parsedStates).toEqual(validStates)
  })

  test('should reject unknown queue item states', () => {
    const invalidState = 'paused'

    const result = queueItemStateSchema.safeParse(invalidState)

    expect(result.success).toBe(false)
  })
})
