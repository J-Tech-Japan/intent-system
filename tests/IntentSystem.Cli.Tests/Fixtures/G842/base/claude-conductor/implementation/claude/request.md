# Cross-runtime review request (G834)

- repo: J-Tech-Japan/intent-system
- pr: 1812
- head sha: 1111111111111111111111111111111111111111
- execution unit: G842
- runtime: claude
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/prompt.md
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/verdict.schema.json
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/invocation.txt

Run by the seat:

```sh
# run by the seat (claude); intent-cli renders this text and never runs it
cd '{{G842_WORKSPACE_ROOT}}/clone' && claude -p --permission-mode plan --disallowedTools Edit,Write,NotebookEdit --output-format json --json-schema "$(cat '{{G842_WORKSPACE_ROOT}}/request-markdown/verdict.schema.json')" < '{{G842_WORKSPACE_ROOT}}/request-markdown/prompt.md' > '{{G842_WORKSPACE_ROOT}}/request-markdown/verdict.raw.json'
```

Read-only enforcement: claude `--permission-mode plan --disallowedTools Edit,Write,NotebookEdit` removes the file-writing tools only. Command execution (for example building and running tests) is allowed, and writes made through shell commands are not sandbox-enforced.
intent-cli renders text and records evidence only; the seat runs the reviewer and posts with gh. intent-cli does not start, launch, or manage any agent or provider process.
Confirming each vendor's automation terms for headless reviewer runs is the operator's responsibility; intent-cli does not assert that any use is permitted.
