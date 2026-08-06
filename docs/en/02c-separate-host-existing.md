# Pattern: separate host × existing project

← [Choose an onboarding pattern](02a-getting-started-orchestration.md) | [docs index](README.md)

## Your setup

Keep the existing `<owner>/<implementation-repo>` unchanged. Create a separate empty `<owner>/<intents-host-repo>` for host metadata and check out **only that host repository** for the initial session. Name the existing implementation repository in the prompt; do not mix its checkout with the host checkout now.

## Initial prompts — choose exactly one

### Herdr-only

> I am adding intent-cli to existing target implementation repository `<owner>/<implementation-repo>`. I have only the empty separate intents host repository open. First understand intent-cli using installed guidance, then initialize the host and record `herdr-only` for a collocated single-machine team.

### Agmsg + herdr

> I am adding intent-cli to existing target implementation repository `<owner>/<implementation-repo>`. I have only the empty separate intents host repository open. First understand intent-cli using installed guidance, then initialize the host and record `agmsg` for a distributed or existing-agmsg team.

## What the agent does

The shipped skill leads to `guide onboarding`. The agent verifies the version, reads `guide model`, dry-runs `intent init`, applies `init --write`, and checks that the host is ok before recording the selected session layer with `intent-cli session-layer set` and provisioning the four-thread team from current guides. A fresh v0.11.0 host write creates nine files. The prompt variants only choose the initial transport; do not merge their downstream procedures.

## Your remaining decisions

Confirm the base-branch policy for child PRs, the transport choice (prefer herdr-only for collocation because it has fewer dependencies; agmsg + herdr remains supported and not retired for distributed/existing-agmsg teams), and the agent kind for each design, orchestration, implementation, and review role.

## Then

Continue with [Organize & maintain intents](03-intents.md).
