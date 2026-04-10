# Auth Model

## Model

Auth in this repo is primarily a boundary model around provider access and remote service interactions, not a full identity domain.

## Main Elements

- GitHub issue/PR operations performed through `gh`
- direct-run provider selection and command-policy configuration
- repo-local config that points at external repos, providers, or artifact roots

## Repo-Specific Expectations

- auth-sensitive values should live in config or external credential tools, not in persisted artifacts
- command logic should treat provider and GitHub access as bounded integration seams
- best-practice review should be able to flag auth assumptions from docs and config references without changing runtime behavior
