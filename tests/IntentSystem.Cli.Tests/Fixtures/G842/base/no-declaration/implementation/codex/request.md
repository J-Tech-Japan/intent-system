# Cross-runtime review request (G834)

- repo: J-Tech-Japan/intent-system
- pr: 1812
- head sha: 1111111111111111111111111111111111111111
- execution unit: G842
- runtime: codex
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/prompt.md
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/verdict.schema.json
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/invocation.txt

Run by the seat:

```sh
# run by the seat (codex); intent-cli renders this text and never runs it
codex exec -s read-only -C '{{G842_WORKSPACE_ROOT}}/clone' --output-schema '{{G842_WORKSPACE_ROOT}}/request-markdown/verdict.schema.json' -o '{{G842_WORKSPACE_ROOT}}/request-markdown/verdict.raw.json' - < '{{G842_WORKSPACE_ROOT}}/request-markdown/prompt.md'
```

Read-only enforcement: codex `-s read-only` is sandbox-enforced: the reviewer can read and run commands, but the sandbox refuses file writes.
intent-cli renders text and records evidence only; the seat runs the reviewer and posts with gh. intent-cli does not start, launch, or manage any agent or provider process.
Confirming each vendor's automation terms for headless reviewer runs is the operator's responsibility; intent-cli does not assert that any use is permitted.
