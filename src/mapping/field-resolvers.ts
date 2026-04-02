import { DEFAULT_ISSUE_KIND, type IssueKind, type ProjectionInput } from '../schema/index.js'

const RULES_AND_SPECS_PATH_SEGMENTS = ['/rules/', '/specs/', '/designs/'] as const

function uniqueStrings(values: readonly string[]): string[] {
  return [...new Set(values)]
}

function hasExplicitList(values: string[] | undefined): values is string[] {
  return values !== undefined
}

function resolveSourceConcepts(input: ProjectionInput): string[] {
  if (hasExplicitList(input.source_concepts)) {
    return input.source_concepts
  }

  return []
}

export function resolveDependencies(input: ProjectionInput): string[] {
  if (hasExplicitList(input.depends_on_subslices)) {
    return [...input.depends_on_subslices]
  }

  if (hasExplicitList(input.depends_on)) {
    return [...input.depends_on]
  }

  return []
}

export function resolveIntentReferences(input: ProjectionInput): string[] {
  const relatedIntents = hasExplicitList(input.related_intents) ? input.related_intents : []
  return uniqueStrings([...relatedIntents, ...resolveSourceConcepts(input)])
}

export function resolveRulesAndSpecs(input: ProjectionInput): string[] {
  if (hasExplicitList(input.rules_and_specs)) {
    return [...input.rules_and_specs]
  }

  return resolveSourceConcepts(input).filter((sourceConcept) =>
    RULES_AND_SPECS_PATH_SEGMENTS.some((segment) => sourceConcept.includes(segment)),
  )
}

export function resolveAcceptanceCriteria(input: ProjectionInput): string[] {
  return [input.success_signal]
}

export function resolveInScope(input: ProjectionInput): string[] {
  if (hasExplicitList(input.in_scope)) {
    return [...input.in_scope]
  }

  return [input.target_part]
}

export function resolveOutOfScope(input: ProjectionInput): string[] {
  if (hasExplicitList(input.out_of_scope)) {
    return [...input.out_of_scope]
  }

  return []
}

export function resolveIssueTitle(input: ProjectionInput): string {
  if (input.issue_title !== undefined) {
    return input.issue_title
  }

  return `${input.source_execution_unit}: ${input.goal}`
}

export function resolveIssueKind(input: ProjectionInput): IssueKind {
  if (input.issue_kind !== undefined) {
    return input.issue_kind
  }

  return DEFAULT_ISSUE_KIND
}

export function resolveParentIntentRoot(input: ProjectionInput): string {
  return input.parent_intent_root
}
