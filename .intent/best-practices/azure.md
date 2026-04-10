# Azure

## Scope

This repository currently models Azure as a bounded deployment and integration concern, not as an always-on implementation requirement.

## Repo-Specific Guidance

- keep Azure guidance declarative until an issue explicitly asks for deployment or publish behavior
- do not add Azure-specific runtime branches to generic CLI flows unless a parent contract requires them
- when documenting Azure work, prefer artifact/config boundaries over hard-coded subscription, tenant, or machine-local paths
- Azure-related review should check whether the change preserves packaged CLI execution and deterministic local validation

## Review Prompts

- is Azure concern actually in scope for the issue?
- is any Azure assumption encoded as config or docs instead of hidden local state?
- does the change avoid widening repo runtime around deployment concerns?
