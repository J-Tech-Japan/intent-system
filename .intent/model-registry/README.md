# Model Registry

These files describe the main repo-local models and seams that best-practice review should treat as first-class inputs.

- `aggregate.md`
  aggregate-like workflow/state transition seam used by queue, run, intake, and bug flows
- `read-model.md`
  the persisted read-side artifact family under `.intent-cli/`
- `api.md`
  the public CLI command surface and packaged invocation seam
- `auth-model.md`
  auth and provider-credential boundary model for direct-run and GitHub-facing work

Keep entries short and structural. This directory is a starter model registry, not a full architecture manual.
