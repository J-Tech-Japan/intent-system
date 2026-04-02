import { z } from 'zod'

export const ISSUE_KIND_VALUES = [
  'feature',
  'bugfix',
  'boundary-fix',
  'verification',
  'refactor',
  'clarification-followup',
] as const

export const DEFAULT_ISSUE_KIND = 'feature'

export const IssueKindSchema = z.enum(ISSUE_KIND_VALUES)

export type IssueKind = z.infer<typeof IssueKindSchema>
