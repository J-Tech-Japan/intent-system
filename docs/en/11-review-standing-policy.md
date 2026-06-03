# Review standing-policy registry (G451)

Review loops repeatedly stall on policy questions that are actually stable
within a domain or wave — how to handle draft PRs, device/operator/hardware-
gated evidence gaps, external issue/PR intake, when passing tests are
"enough", and how accepted gaps get tracked. The standing-policy registry lets
you encode those answers **as data** so `intent-cli guide review` returns a
deterministic decision instead of re-asking the operator every packet.

## It is optional and safe by default

- **No policy file → built-in safe defaults.** A host with no
  `.intent-cli/review-policy.json` behaves exactly as before. No migration is
  required for existing hosts.
- **Invalid policy file → fail-closed to defaults.** If the file is missing,
  empty, or not valid JSON, `guide review` still succeeds, uses the built-in
  defaults, and records a warning. It never crashes and never silently drops
  operator clarification.
- **Read-only.** Resolving the policy never writes the file or mutates host
  state.

`guide review` reports where the resolved policy came from in
`review_policy_source`: `built-in-default`, `domain-file`, or
`invalid-fallback-default`.

## Adding a policy

Create `.intent-cli/review-policy.json` at the host repo root. Every section is
optional — an omitted (or empty) section keeps the safe default, so a partial
file never removes guidance.

```json
{
  "domain": "intent-cli",
  "device_gated_evidence": {
    "approve_with_recorded_gap_allowed": true,
    "hard_block_categories": ["safety", "security", "data-loss", "payment", "primary-deliverable"],
    "rules": [
      "Optional: replace the default device-gap rules with domain-specific wording."
    ]
  },
  "draft_handling":          { "rules": ["A draft PR is not review-ready; request ready-for-review first."] },
  "external_artifact_intake":{ "rules": ["External (non-intent-target) issues/PRs are intake-only until the host promotes them."] },
  "test_evidence_sufficiency":{ "rules": ["Passing tests are necessary but not sufficient; restate one contract clause + one intent reference."] },
  "follow_up_tracking":      { "rules": ["Track every accepted gap durably (PR comment / closeout note / follow-up issue)."] }
}
```

### Device-gated evidence

The `device_gated_evidence` section controls the recurring device-gap decision:

- `approve_with_recorded_gap_allowed` (bool) — whether an ordinary device-gap
  may be **approved with a recorded gap** (the missing evidence is purely a
  device/automation limitation and code conformance is otherwise verified).
  Set `false` to require a hard block on every device gap in this domain.
- `hard_block_categories` (string list) — categories that are **always**
  hard-blocked (never approve-with-gap), e.g. `safety`, `security`,
  `data-loss`, `payment`, `primary-deliverable`.
- `rules` (string list) — optional human-readable rule text. When omitted, the
  built-in device-gap rules apply.

## Scope guidance for OSS users

- Encode only policies that are genuinely stable for your domain or wave.
- Keep high-risk approvals gated: never configure a policy that makes a
  safety/security/data-loss/payment approval automatic.
- Do **not** embed a single project's bespoke policy as if it were a global
  rule — the file is per host/domain.
- When a decision is genuinely new or ambiguous, the policy does not remove
  operator clarification; surface it as you would today.
