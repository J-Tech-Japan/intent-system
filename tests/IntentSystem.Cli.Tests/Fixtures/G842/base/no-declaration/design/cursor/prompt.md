# Design review: G842

You are an independent design reviewer. Judge the packet against the operator decisions it names, the truth of the "Current Observed State" claims, gate soundness, testability, and the no-launch rule.

## Packet digest

`2fb5a6518521c25503243e0611b447f16784251d25077888fca55f0ff406624c`

Echo this exact digest in your verdict as `packet_digest`.

## Packet files

### packet.yaml

```packet.yaml
implementation_issue_packet:
  issue_title: "G842 back-compat fixture"
  domain: intent-cli
  target_repo: J-Tech-Japan/intent-system
```

### github-body.md

```github-body.md
# G842 back-compat fixture
```

### review-context.md

```review-context.md
# review context
```

### implementation.md

```implementation.md
# implementation notes
```

## Output

Return only one JSON object that matches the schema below, with no text before or after it.

- "packet_digest": echo the packet digest you reviewed (2fb5a6518521c25503243e0611b447f16784251d25077888fca55f0ff406624c).
- "verdict": "approve" only when there is no blocking finding; "blocking_findings" must then be [].
- "verdict": "request-changes" when there is at least one blocking finding.
- "notes": non-blocking observations; may be [].

```json
{
  "type": "object",
  "additionalProperties": false,
  "required": [
    "verdict",
    "packet_digest",
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
    "packet_digest": {
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
