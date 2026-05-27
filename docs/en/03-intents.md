# Organize & maintain intents

← [docs index](index.md) | → [Create packets & publish issues](04-packets-issues.md)

This page covers **host/design** work. Before cutting packets, deepen your product and technical intent in a design thread with an AI agent.

## What is intent deepening?

"Intent deepening" is the process of working with an AI agent to clarify what you want to build and why.

You start with a rough description. The AI agent uses intent-cli guidance to ask structured questions with background, options, pros/cons, and a recommendation. As you answer, the project direction, technical choices, and open questions become clear. The results are organized into an **intent tree** — a discoverable folder structure that feeds packets and GitHub issues.

You do not need deep technical expertise. If you do not know the best technical choice, ask the AI agent to suggest options and explain tradeoffs.

## Prompt to paste in your design thread

**Short prompt (when you already have some intent):**

> Ask intent-cli what I should do next for this repository.

**Rich prompt for starting a new product or domain:**

> Ask intent-cli to help organize the intent for this project.
>
> I want to build `<product or feature>`.
> The important direction is `<user value, business goal, quality bar, operational policy>`.
> Technically, I am considering `<language, cloud, architecture, event sourcing, libraries>`.
> Some decisions are still unclear, so ask me structured questions with background, options, pros/cons, and a recommendation with reasons.
> Organize the result into an intent tree that can lead to packets and GitHub issues.

Information you can include in the prompt:

- Product goal: what should exist and who it helps
- Mission/value/vision: why this project matters and what tradeoffs it prefers
- Functional requirements: what the system must do
- Non-goals: what should not be included yet
- Technical preferences: language, framework, database, cloud, event sourcing, testing style
- Constraints: budget, team skill, deployment environment, compliance, performance
- Uncertainty: decisions where you want AI suggestions and tradeoff analysis
- Rationale: why a choice is important

## What the AI agent will do

After you paste the prompt in your design thread, the AI agent will:

1. Run `intent-cli guide workflow` and `intent-cli intent status` internally to check current state
2. Ask structured questions about unresolved decisions
3. Persist your answers with `intent-cli interview record-answer`
4. Organize the results into the right intent tree folders (see below)

## Structured question style

AI agent questions take this shape:

- **Current understanding**: what is known so far
- **Background / why it matters**: how this decision affects later packets and implementation
- **Question**: one focused, specific question
- **Choices**: 2–4 concrete options
- **Pros / cons**: tradeoffs for each option
- **Recommendation**: the agent's suggested choice
- **Recommendation reason**: why that option is preferred
- **What this decides**: what this answer locks down in the intent tree or packet

## Why this is better than ad-hoc chat

- The conversation result lives in a **persistent intent tree** that can be referenced and updated later
- Structured questions cover angles that are easy to miss (security, operations, migration constraints)
- A clear traceability chain is produced: packet → GitHub issue → implementation loop
- Decision context does not get lost across sessions or team members

## Durable artifacts

| Conversation content | Artifact produced |
|---|---|
| Technical choices and decisions | `decisions/` ADR-style notes, `technology/` |
| Unresolved questions | `clarifications/open.md` |
| Feature requirements and user stories | `features/<slug>/` |
| Executable slices | `packets/` → GitHub issue |
| Mission/value/vision | `identity/` |

## Ask-intent-cli prompt template

> I'm organizing intents for domain `<name>`. Ask intent-cli what I should do next.

## Metadata / label safety

- Interview/draft artifacts are written through `intent-cli interview …`
  (`record-answer` is the only mutation here); don't hand-edit the durable Q/A
  files.
- Child implementation agents do **not** read the intent tree (`intents/**`) or
  host metadata — that's host/design territory.

## Intent knowledge-tree layout (tree-v1)

New domains should organize intent into discoverable folders rather than a single flat
file. The **tree-v1** layout defines recommended categories (`identity`, `product`,
`features`, `technology`, `operations`, `decisions`, `clarifications`, `packets`, `links`)
and a manifest schema that supports custom folder names and project types.

```bash
# Get current tree-layout authoring guidance
intent-cli guide intent-work setup \
  --kind tree-layout \
  --domain <name> \
  --target-repo <owner/repo> \
  --format markdown
```

See [Intent knowledge-tree layout (tree-v1)](03a-intent-tree-layout.md) for the full spec,
manifest schema, project type examples, and cross-linking rules.

## Next

[Create packets & publish issues](04-packets-issues.md).
