# Pattern: separate host × brand-new project

← [Choose an onboarding pattern](02a-getting-started-orchestration.md) | [docs index](README.md)

## Your setup

Create two empty repositories: `<owner>/<implementation-repo>` for product code and `<owner>/<intents-host-repo>` for host metadata. Check out **only the empty host repository**. Name the implementation repository in the prompt; do not check it out for this initial host session.

## Initial prompts — choose exactly one

### Herdr-only

> I am setting up intent-cli for new target implementation repository `<owner>/<implementation-repo>`. I have only the empty intents host repository open. First understand intent-cli using its installed guidance, then initialize it and record `herdr-only` for a collocated single-machine four-thread team.

### Agmsg + herdr

> I am setting up intent-cli for new target implementation repository `<owner>/<implementation-repo>`. I have only the empty intents host repository open. First understand intent-cli using its installed guidance, then initialize it and record `agmsg` for a distributed team or our existing agmsg investment.

## What the agent does

The shipped skill leads to `guide onboarding`. The agent verifies the version, reads `guide model`, runs `intent init` as a dry-run and then with `--write`, and verifies `host-check: ok`. The observed v0.11.0 write creates nine files. It records the selected session layer with `intent-cli session-layer set`, then uses the current guide to provision the four-thread team. The two prompts end at that initial choice: downstream follows the recorded mode and installed guides.

## Your remaining decisions

Confirm the base-branch policy, the transport selection (prefer herdr-only for collocation because it has fewer dependencies; agmsg + herdr remains supported and not retired for distributed/existing-agmsg teams), and the agent kind for each design, orchestration, implementation, and review role.

## Then

Continue with [Organize & maintain intents](03-intents.md).
