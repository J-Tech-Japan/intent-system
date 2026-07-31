---
name: intent-cli
description: Drive intent-driven development on GitHub with intent-cli. Use when the user wants to shape an intent, cut the next slice, publish an issue, implement or review a packet, close out a PR, or set up the four-thread orchestration loop — or asks what intent-cli can do.
---

# intent-cli

This skill is a **dispatcher, not a manual**. It tells you which `intent-cli`
command to ask for the guidance you need. It deliberately restates none of the
workflow: the installed CLI's `guide` output is the single source of truth, and
it moves with the tool while a copied-out description would not.

**Rule: installed guide output wins.** If anything you remember — including
anything in this file — disagrees with what `intent-cli guide ...` prints, the
guide output is correct. Run the command; do not answer from memory.

## Before anything else

Confirm the tool is available and see what it offers:

```bash
intent-cli --version
intent-cli guide model
```

`guide model` describes the collaboration model the rest of the workflow
assumes. Read it before acting on any other guidance.

## Which guide command to run

Ask the guide, then follow exactly what it prints.

| The user wants to… | Run |
| --- | --- |
| get oriented / set up for the first time | `intent-cli guide onboarding` |
| see every available command surface | `intent-cli guide commands list` |
| find the right workflow for a goal | `intent-cli guide workflow suggest --goal "<what they said>"` |
| shape or deepen an intent | `intent-cli guide collaborate` |
| interview the user to draw out intent | `intent-cli grill` |
| decide what to work on next | `intent-cli guide next` |
| take a slice from issue to PR | `intent-cli guide worker issue-to-pr` |
| review a pull request | `intent-cli guide review --pr <n> --repo <r> --domain <d>` |
| close out a merged PR | `intent-cli guide closeout` |
| run the four-thread orchestration loop | `intent-cli guide orchestrator-thread` |
| understand the rules that bind a surface | `intent-cli guide rules list` |

When none of these obviously fits, run `intent-cli guide workflow suggest --goal
"<the user's own words>"` and let it route.

Most guide commands accept `--format markdown|json`. Prefer `markdown` when you
are going to read and act on the output; `json` when you need to extract a
specific field.

## How to use what it prints

1. **Run the guide command first.** Do not plan the work from this file.
2. **Follow its steps literally**, including any preflight or verification it
   names. Guides state their own gates; skipping one is how a loop breaks.
3. **Prefer the canonical command it names** over hand-editing files or calling
   `gh` directly. Where a guide names an `intent-cli` command for a state
   change, that command is the supported path.
4. **Report what the command actually returned** — exit code, and the fields the
   guide told you to check. Do not summarize a result you did not observe.

## Keeping this skill current

This file ships inside the `intent-cli` package. Update it with the tool:

```bash
intent-cli skill list              # what is installed, and whether it has drifted
intent-cli skill diff              # what differs between installed and shipped
intent-cli skill install --target all
```

`skill install` refuses to overwrite a copy you have edited unless you pass
`--force`, so local changes are never lost silently.
