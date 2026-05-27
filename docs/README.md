# intent-cli documentation

Welcome to the intent-cli documentation.

---

## Choose your language

| Language | Entry point |
|---|---|
| 日本語 (Japanese) | [`ja/README.md`](ja/README.md) |
| English | [`en/README.md`](en/README.md) |

---

## Where to start

If you are new to intent-cli, open a design thread in your AI agent and ask:

> I want to work on `<owner>/<repo>` with intent-cli.
> Ask intent-cli what phase I am in and what I should decide next.

The agent runs `intent-cli` internally and returns questions or results.
You do not need to memorize commands — intent-cli surfaces the right
guidance for your current workflow state.

---

## About `automation-templates/`

The [`automation-templates/`](automation-templates/) folder contains
**reusable loop prompt templates** for operators and AI agents running
automated coding loops.

They are:

- Intended for **operators** who are wiring up a local or cloud coding
  automation loop (e.g. hooking Claude Code to an intent-cli worker
  queue).
- **Not** the first thing a beginner should copy.  Copying a template
  without understanding the underlying workflow will produce commands
  that do the right things mechanically but are hard to debug when
  something unexpected happens.

If you are just getting started, follow the beginner path in your
language-specific README first.  When you reach the point of setting up
automation, your AI agent can ask `intent-cli` for the current
recommended prompt — the templates here serve as an up-to-date reference
for that conversation.
