# intent-system / `intent-cli`

`intent-cli` is **deterministic support tooling** for running an intent-driven
development workflow on top of GitHub. It helps you organize intents, prepare
and publish Child Issue Contracts, drive implementation and review loops, and
recover when a loop looks wrong — all through explicit, inspectable commands.

> `intent-cli` never launches Claude, Codex, or any other AI provider. It emits
> guidance, validates contracts, and performs bounded GitHub/metadata
> transitions. The AI agent (you, or your coding assistant) stays in the driver's
> seat and **asks `intent-cli` what to do next**.

- Package id / command: `intent-cli`
- License: [Apache-2.0](https://github.com/J-Tech-Japan/intent-system/blob/main/README.md#license)
- Repository: <https://github.com/J-Tech-Japan/intent-system>
- Official site: <https://www.intent-driven-development.com/> — the Intent-Driven Development concept & intent-system service site, operated by J-Tech Japan ([日本語](https://www.intent-driven-development.com/jp))

> [intent-driven-development.com](https://www.intent-driven-development.com/) is
> operated by J-Tech Japan and covers the broader Intent-Driven Development
> concept and the intent-system service overview. This GitHub repository remains
> the source for code, releases, installation, and detailed docs.

**ドキュメント / Documentation:**
[日本語](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/ja/README.md) | [English](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/README.md)

> **はじめての方へ:** インストール後、`intent-cli --version` で動作確認し、
> Claude・Codex・Copilot などの AI エージェントのチャットで
> `intent-cli に聞いて...` と伝えるだけで始められます。
> 詳しくは[日本語ドキュメント](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/ja/README.md)をご覧ください。

---

## Quickstart

### 1. Install

You need a **.NET 10 SDK** (`dotnet --version` should report `10.x`).

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli
```

If `intent-cli` is not found after install, add `~/.dotnet/tools` (macOS/Linux)
or `%USERPROFILE%\.dotnet\tools` (Windows) to your `PATH`.

> No .NET SDK? See **[Install without a .NET SDK](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/01-install.md#install-without-a-net-sdk)**
> for self-contained binaries. Need the preview channel? See the
> **[developer reference](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/09-developer-reference.md#preview-install)**.

### 2. Verify

```bash
intent-cli --version
```

### 3. Ask an AI agent

Open Claude, Codex, Copilot, or another coding assistant with access to your
repository and paste one of these prompts:

**Start or continue a project:**

> I want to work on `<owner>/<repo>` with intent-cli.
> Ask intent-cli what phase I'm in and what I should decide next.

**Start four-thread agmsg orchestration (primary path):**

> I want to start agmsg orchestrator mode for `<owner>/<repo>` with intent-cli.
> Ask intent-cli for the orchestrator setup checklist.

**Start an implementation loop (timer-loop alternative):**

> Set up a child implementation loop for `<owner>/<repo>`.
> Ask intent-cli for the next step.

**Grill a topic (persistent interview mode):**

> Grill `<topic>` with intent-cli.
> Keep asking me one question at a time until the intent is packet-ready.

**Stack the backlog (create packets, publish the first issue):**

> Stack the available packets for `<owner>/<repo>` with intent-cli.
> Create the ready packets and publish only the first issue.

**Ask what to do next:**

> intent-cli に聞いて、次に何をしたらいいか教えてください。
> (Ask intent-cli `next` to recommend the right design-side process.)

**Inspect the real product (evidence-backed observation):**

> Inspect `<target>` with intent-cli.
> Observe the real behavior, separate evidence from inference, and propose packet candidates.

The agent runs `intent-cli` commands internally and brings back questions or
results. You focus on intent, priorities, and approval decisions. In **grill**
mode (`intent-cli grill`) the thread stays persistent — it builds an
open-question backlog and keeps asking one question at a time, continuing after
each answer until a stop condition is reached.

---

## Documentation

- **English:** [`docs/en/`](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/README.md) — install, project start, intent
  organization, packet/issue creation, implementation & review loop setup, recovery.
- **日本語:** [`docs/ja/`](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/ja/README.md) — 同上のドキュメント日本語版。
- **Command reference:** [`docs/en/08-command-reference.md`](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/08-command-reference.md)
  — agent-facing and power-user command surfaces.
- **Developer reference:** [`docs/en/09-developer-reference.md`](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/09-developer-reference.md)
  — packaged invocation smoke test, preview channel, version flow.
- **Agent-message orchestration (primary):** [`docs/en/12-agent-message-orchestration.md`](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/en/12-agent-message-orchestration.md)
  — the primary four-thread agmsg orchestrator model (日本語: [`docs/ja/12`](https://github.com/J-Tech-Japan/intent-system/blob/main/docs/ja/12-agent-message-orchestration.md)).

> **Four-thread agmsg orchestration is the primary way to run intent-cli.** A
> **design** thread authors intent and packets; an **orchestrator** thread
> moves ready packets through the workflow and paces the loopless
> **implementation** and **review** threads over a local message bus (agmsg)
> instead of independent timers. The steady state is message-driven —
> implementation/review replies wake the orchestrator, so routine fast polling
> is not required — with a 30-minute-class design-thread watchdog loop as the
> recommended default safety net; an explicit orchestrator timer remains
> supported only as a fallback/legacy polling option. It covers
> single/multi-domain routing, next-slice publication, CI wait, dependency
> planning, a safe stale-thread health check, and safe-repair vs escalation.
> agmsg is a signal layer only — intent-cli and GitHub stay authoritative.
> This is the practiced, maintained model (still being hardened in places).
>
> **Timer loops remain a fully supported, simpler alternative:** implementation
> and review run as independent timer loops with no orchestrator thread
> required. Exactly one mode applies per domain/repo — never mix the two for
> the same domain/repo.

---

## Community

Join the [J-Tech JAPAN OSS Discord](https://discord.gg/kMdv978X) for community
discussion, questions, and lightweight support. Discord is for general chat;
for reproducible bugs or actionable feature requests, please open a
[GitHub issue](https://github.com/J-Tech-Japan/intent-system/issues) instead.
Security-sensitive reports go to [SECURITY.md](https://github.com/J-Tech-Japan/intent-system/blob/main/SECURITY.md), not Discord.

---

## License

This project is licensed under the Apache License, Version 2.0 — see the
[`LICENSE`](https://github.com/J-Tech-Japan/intent-system/blob/main/LICENSE) file for the full text and [`NOTICE`](https://github.com/J-Tech-Japan/intent-system/blob/main/NOTICE) for
attribution. The published `intent-cli` NuGet package declares `Apache-2.0` via
SPDX license metadata.

Release artifacts (the NuGet package and self-contained binaries) and OSS
preview CI artifacts carry no expiration or private-use gating.
