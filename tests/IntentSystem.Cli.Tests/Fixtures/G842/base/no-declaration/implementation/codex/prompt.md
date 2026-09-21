# Implementation review: J-Tech-Japan/intent-system PR #1812 (G842)

You are an independent reviewer on a different runtime from the author. Review pull request #1812 in J-Tech-Japan/intent-system at head 1111111111111111111111111111111111111111.

## Inputs

- Issue contract (packet github-body.md): '{{G842_WORKSPACE_ROOT}}/.intent-cli/issues/G842/github-body.md'
- Review context (packet review-context.md): '{{G842_WORKSPACE_ROOT}}/.intent-cli/issues/G842/review-context.md'
- Implementation notes (packet implementation.md): '{{G842_WORKSPACE_ROOT}}/.intent-cli/issues/G842/implementation.md'
- Pull request: J-Tech-Japan/intent-system#1812
- Head SHA: 1111111111111111111111111111111111111111
- Read-only clone checked out at that head: '{{G842_WORKSPACE_ROOT}}/clone'

## Rules

- Work read-only in the clone. Do not edit, create, or delete files; do not commit, push, or post anything.
- If you can run commands, first confirm that `git -C '{{G842_WORKSPACE_ROOT}}/clone' rev-parse HEAD` prints 1111111111111111111111111111111111111111; if it does not, return request-changes with one finding that names the mismatch. If your runtime cannot run commands, say so in notes.
- The change under review is the commits on HEAD that are not on the base branch named in the issue contract. If you cannot run git, review the target paths the issue contract names and say in notes that you could not list the commits.
- Judge the change against the issue contract and the review context: correctness, contract conformance, tests, and every blocking item the review context names.
- A blocking finding names the file, the line, and the concrete scenario that fails. Put non-blocking observations in notes.

## Output

Return only one JSON object that matches the schema below, with no text before or after it.

- "head_sha": echo the head SHA you reviewed (1111111111111111111111111111111111111111).
- "verdict": "approve" only when there is no blocking finding; "blocking_findings" must then be [].
- "verdict": "request-changes" when there is at least one blocking finding.
- "notes": non-blocking observations; may be [].

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": [
    "verdict",
    "head_sha",
    "blocking_findings",
    "notes"
  ],
  "properties": {
    "verdict": {
      "type": "string",
      "enum": [
        "approve",
        "request-changes"
      ]
    },
    "head_sha": {
      "type": "string"
    },
    "blocking_findings": {
      "type": "array",
      "items": {
        "type": "object",
        "additionalProperties": false,
        "required": [
          "file",
          "line",
          "scenario"
        ],
        "properties": {
          "file": {
            "type": "string"
          },
          "line": {
            "type": "integer"
          },
          "scenario": {
            "type": "string"
          }
        }
      }
    },
    "notes": {
      "type": "array",
      "items": {
        "type": "string"
      }
    }
  }
}
```
