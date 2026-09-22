# Cross-runtime review request (G835 design)

- packet digest: 2fb5a6518521c25503243e0611b447f16784251d25077888fca55f0ff406624c
- execution unit: G842
- runtime: cursor
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/prompt.md
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/verdict.schema.json
- rendered: {{G842_WORKSPACE_ROOT}}/request-markdown/invocation.txt

Run by the seat:

```sh
# run by the seat (cursor); intent-cli renders this text and never runs it
cursor-agent -p --mode ask --sandbox enabled --trust --workspace '{{G842_WORKSPACE_ROOT}}/request-markdown' --output-format json "$(cat '{{G842_WORKSPACE_ROOT}}/request-markdown/prompt.md')" > '{{G842_WORKSPACE_ROOT}}/request-markdown/verdict.raw.json'
```

Read-only enforcement: cursor `--mode ask` refuses every non-read-only tool, including all shell commands, so the reviewer reads files but cannot run git or tests; the head it echoes is read from files such as `.git/HEAD`, not checked with git. `--mode plan` was not used because a measured plan-mode run switched itself to agent mode and wrote files inside and outside the workspace; `--sandbox enabled` did not stop those writes.
intent-cli renders text and records evidence only; the seat runs the reviewer and posts with gh. intent-cli does not start, launch, or manage any agent or provider process.
Confirming each vendor's automation terms for headless reviewer runs is the operator's responsibility; intent-cli does not assert that any use is permitted.
