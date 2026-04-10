# AI-Assisted Delivery

## Scope

This repo encodes AI-assisted delivery as explicit artifacts and stage boundaries. Repo-local guidance should keep those handoffs inspectable and reproducible.

## Repo-Specific Guidance

- AI-assisted flows should read and write bounded artifacts rather than rely on hidden prompt state
- issue-cut, implement, review, fix, and closeout stages should carry forward only the fields the next stage actually needs
- provider-facing runtime traces should be append-only and safe under repeated or concurrent test execution
- child-repo docs may guide best-practice review, but they should not silently mutate parent intent or broaden command runtime
- docs-only issues should stay docs-only even when they touch AI-oriented guidance

## Review Prompts

- can a later stage reconstruct the decision from persisted artifacts alone?
- is the AI-assisted behavior bounded by repo-local docs/config instead of machine-local assumptions?
- does the change avoid inventing a parallel workflow path?
