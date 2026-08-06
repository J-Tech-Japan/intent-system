# Pattern: same repository metadata branch × brand-new project

← [Choose an onboarding pattern](02a-getting-started-orchestration.md) | [docs index](README.md)

## Your setup

Create one new `<owner>/<implementation-repo>` with its intended implementation base branch, then create a metadata branch from it (for example `main-metadata`) before initialization. Check out **only that metadata-branch checkout** for this session. Product code and child PRs remain on the implementation base branch; host metadata stays on the metadata branch.

## Initial prompts — choose exactly one

### Herdr-only

> I am setting up intent-cli for new repository `<owner>/<implementation-repo>`. I have only its metadata-branch checkout open. First understand intent-cli using installed guidance, then initialize this host and record `herdr-only` for a collocated single-machine four-thread team.

### Agmsg + herdr

> I am setting up intent-cli for new repository `<owner>/<implementation-repo>`. I have only its metadata-branch checkout open. First understand intent-cli using installed guidance, then initialize this host and record `agmsg` for a distributed team or our existing agmsg investment.

## What the agent does

The shipped skill dispatches to `guide onboarding`. The agent verifies the version, reads `guide model`, dry-runs `intent init`, applies `init --write`, and checks that the host is ok before recording the session layer with `intent-cli session-layer set` and using current guides for four-thread provisioning. A fresh v0.11.0 write creates nine files. Only the initial prompt differs; follow the recorded mode afterwards.

## Your remaining decisions

Confirm the base-branch policy, transport selection (prefer herdr-only for collocation because it has fewer dependencies; agmsg + herdr remains supported and not retired for distributed/existing-agmsg teams), and the agent kind for each design, orchestration, implementation, and review role.

## Then

Continue with [Organize & maintain intents](03-intents.md).
