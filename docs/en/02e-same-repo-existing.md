# Pattern: same repository metadata branch × existing project

← [Choose an onboarding pattern](02a-getting-started-orchestration.md) | [docs index](README.md)

## Your setup

Keep the existing `<owner>/<implementation-repo>` and create a metadata branch (for example `main-metadata`) from its intended implementation base branch before initialization. Check out **only the metadata-branch checkout** for the initial host session. The implementation branch and existing code remain separate from host metadata work.

## Initial prompts — choose exactly one

### Herdr-only

> I am adding intent-cli to existing repository `<owner>/<implementation-repo>`. I have only its metadata-branch checkout open. First understand intent-cli using installed guidance, then initialize this host and record `herdr-only` for a collocated single-machine four-thread team.

### Agmsg + herdr

> I am adding intent-cli to existing repository `<owner>/<implementation-repo>`. I have only its metadata-branch checkout open. First understand intent-cli using installed guidance, then initialize this host and record `agmsg` for a distributed team or our existing agmsg investment.

## What the agent does

The shipped skill leads to `guide onboarding`. The agent verifies the version, reads `guide model`, runs the `intent init` dry-run, applies `init --write`, and checks the host before recording the selected session layer with `intent-cli session-layer set` and provisioning the four-thread team from the current guides. A fresh v0.11.0 write creates nine files. The two prompts only choose an initial transport; downstream uses the recorded mode, not blended instructions.

## Your remaining decisions

Confirm the base-branch policy for child PRs, transport choice (herdr-only first for collocation; agmsg + herdr for distributed/existing-agmsg teams), and the agent kind for each design, orchestration, implementation, and review role.

## Then

Continue with [Organize & maintain intents](03-intents.md).
