# G290 Resolve issue publish titles from packet metadata reliably

## Why this slice

`intent-cli issue publish-flow SKS-G190 --repo
J-Tech-Japan/SekibanAsAService --write` published GitHub issue #500 as
`SKS-G190 (untitled)` even though
`.intent-cli/issues/SKS-G190/packet.yaml` carried a real title:
`SKS-G190 Approval-Gated Production Credential Issuance And Rotation
Lifecycle Baseline`. The body file started at `## Goal` (no leading
H1), so the previous title resolver fell straight through to the
`(untitled)` last-resort path.

## What changed

### `IssuePublishFlowCommand`

Title resolution now follows a deterministic priority:

1. `packet.yaml` `title:` (preferred). Whitespace-only / quoted values
   are normalised; an empty value falls through.
2. First H1 line (`# ...`) of `github-body.md`.
3. Last-resort `<execution-unit> (untitled)`.

The result JSON gains two new fields:

- `title_source` — one of `packet-yaml`, `github-body-h1`, or
  `fallback-untitled`.
- `warnings` — list of structured codes; `title-fallback` is added when
  the last-resort path fires so a fallback publish never ships
  silently.

Markdown output adds `- title source: <source>` and `- warnings: ...`
lines so the operator sees both the resolved source and any warning at
a glance.

`TryReadPacketTitle` is a lightweight YAML scanner — it accepts the
first non-empty `title:` line at any indentation, strips optional
surrounding `"`/`'` quotes, and treats blank values as missing. It
does not require the full packet schema to load (no new dependency on
the full YAML deserializer for this read).

## Boundaries

- Read-only on the title-resolution side; does not rewrite existing
  GitHub issue titles.
- Backward compatible: packets without `packet.yaml` continue to
  resolve via the body H1 (existing behavior).
- Empty / whitespace-only packet titles fall through (so a malformed
  yaml doesn't trap the resolver into publishing an empty title).
- Idempotent reruns are unchanged: when `publish.yaml` already
  records `issue-created` (or queue-state has `linked_issue`), the
  command short-circuits before any title write — the new fields just
  describe what would have been used.
- Real code/contract review findings (missing required contract
  sections) still surface as validation errors before the GitHub call.
- No raw `gh` title mutation. No change to child loop behavior.

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~IssuePublishFlow"

git diff --check
```

Four new focused `IssuePublishFlowCommandTests` cover:

- SKS-G190-shaped: packet.yaml title + body without H1 →
  `title_source: packet-yaml` and the real packet title resolves; no
  `title-fallback` warning.
- Older packet without packet.yaml + body with H1 →
  `title_source: github-body-h1` (existing behavior).
- No packet.yaml AND no body H1 → `title: <id> (untitled)`,
  `title_source: fallback-untitled`, `warnings: ["title-fallback"]`.
- Empty packet title → falls through to body H1 (defensive against
  malformed metadata).

Full suite: 2101 passed, 1 skipped.
