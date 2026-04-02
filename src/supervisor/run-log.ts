import { z } from 'zod'

import { queueItemStateSchema } from './state.js'

export const resumedRunEvent = 'resumed'

const runEventSchema = z.object({
  ts: z.string().datetime(),
  execution_unit: z.string(),
  event: z.union([queueItemStateSchema, z.literal(resumedRunEvent)]),
  by: z.string(),
})

export type RunEvent = z.infer<typeof runEventSchema>

export function parseRunLog(jsonl: string): RunEvent[] {
  const lines = jsonl.split('\n').filter((line) => line.length > 0)

  return lines.map((line) => runEventSchema.parse(JSON.parse(line)))
}

export function serializeRunEvent(event: RunEvent): string {
  return JSON.stringify(runEventSchema.parse(event))
}

export function appendRunEvent(existingJsonl: string, event: RunEvent): string {
  const serializedEvent = `${serializeRunEvent(event)}\n`

  if (existingJsonl.length === 0) {
    return serializedEvent
  }

  return existingJsonl.endsWith('\n')
    ? `${existingJsonl}${serializedEvent}`
    : `${existingJsonl}\n${serializedEvent}`
}

export function filterByUnit(events: RunEvent[], unit: string): RunEvent[] {
  return events.filter((event) => event.execution_unit === unit)
}

export function getTransitionHistory(events: RunEvent[], unit: string): RunEvent[] {
  return filterByUnit(events, unit)
}
