import { describe, expect, test } from 'vitest'

import * as supervisorApi from '../../src/supervisor/index.js'
import indexTestSource from './index.test.ts?raw'
import queueStateTestSource from './queue-state.test.ts?raw'
import runLogTestSource from './run-log.test.ts?raw'
import stateTestSource from './state.test.ts?raw'

const forbiddenCommentPattern = /^\s*\/\/\s*(Given|When|Then):/m

describe('supervisor contracts', () => {
  test('should expose only the documented public api from the barrel', () => {
    expect(supervisorApi).not.toHaveProperty('queueStateSchemaVersion')
    expect(supervisorApi).not.toHaveProperty('resumedRunEvent')
  })

  test('should keep supervisor tests free of Given When Then comments', () => {
    const testSources = [
      indexTestSource,
      queueStateTestSource,
      runLogTestSource,
      stateTestSource,
    ]

    expect(testSources.every((source) => !forbiddenCommentPattern.test(source))).toBe(true)
  })
})
