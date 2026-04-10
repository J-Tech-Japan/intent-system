# Sekiban

## Scope

`intent-system` may reference Sekiban-oriented concepts, but this repo is not the place to re-implement Sekiban domain rules wholesale. Guidance should keep that boundary explicit.

## Repo-Specific Guidance

- treat Sekiban concepts as integration and modeling inputs, not as a reason to duplicate upstream domain behavior in this repo
- when a command or artifact mentions aggregates, read models, or auth boundaries, keep the language compatible with Sekiban-style concepts without coupling to a sibling repo's runtime
- prefer packet, review-context, and intent docs to capture Sekiban-facing expectations instead of embedding broad domain logic into CLI helpers

## Review Prompts

- does the change keep Sekiban knowledge at the repo-boundary level?
- is any Sekiban-specific assumption documented as an input rather than hidden runtime logic?
- does the implementation avoid bleeding sibling-repo behavior into generic CLI infrastructure?
