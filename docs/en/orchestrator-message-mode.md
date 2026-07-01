# Orchestrator-message mode — Monitor tool vs delivery-mode

← [Agent-message orchestration](12-agent-message-orchestration.md) | [docs index](README.md)

This page documents one operationally critical distinction for orchestrator-message
mode: **Claude Code's `Monitor` tool is the real mechanism that streams agmsg inbox
messages into a receiver, and agmsg's `delivery.sh status` `mode=monitor` is only
configuration — not proof that a Monitor is attached and streaming.** The
authoritative, paste-ready guidance is rendered by installed intent-cli — generate it
with:

```text
intent-cli guide orchestrator-thread --domain <name> --target-repo <owner/repo> --agent <agent> --format markdown
```

This page mirrors the **Monitor tool vs delivery-mode (G511)** section of that guide so
the published docs and the guide stay in sync. The verification/repair checklist below
is what tells a healthy receiver from a silently broken one.

## Why "monitor" is overloaded

The word "monitor" names three unrelated things, so operators and agents cannot tell a
healthy receiver from a silently broken one until the distinction is named:

1. Claude Code's generic `Monitor` tool — the real inbox-stream delivery mechanism.
2. agmsg's `delivery.sh` `mode=monitor` configuration.
3. Unrelated `Azure Monitor` / other MCP `monitor` tools.

Claude Code's `Monitor` is a generic Claude Code tool — the real mechanism that streams
the agmsg inbox into a receiver. agmsg attaches it by launching `watch.sh` from the
Claude Code SessionStart directive; the running Monitor task is what turns incoming
agmsg lines into live transcript events.

agmsg `delivery.sh status` `mode=monitor` is configuration only and is **not** proof
that a Monitor tool is attached and streaming — a receiver can report `mode=monitor`
while no Claude Code `Monitor` is running and nothing is delivered live. Confirm live
attachment with the success markers below, not with the delivery mode alone.

## Live-attachment success markers

Verify all four to confirm the inbox stream is live:

- `ToolSearch select:Monitor` resolves Monitor in the receiver session (the tool is available).
- the transcript shows `Monitor(agmsg inbox stream)` — the Monitor tool attached to the inbox stream.
- the Claude Code footer shows `1 monitor` (a live Monitor task is attached).
- the transcript shows `Monitor event` lines as inbox messages arrive (the stream is live).

## Failure markers

A receiver reporting `mode=monitor` may still be silently broken:

- delivery falls back to a plain `Bash` / background `watch.sh` task instead of an attached Monitor — no live stream.
- the footer shows `1 shell` instead of `1 monitor` (a background shell is running, not a Monitor).
- confusion with `Azure Monitor` / other MCP `monitor` tools — those are unrelated to agmsg inbox streaming and never prove attachment.

## Trust-repair runbook

When the success markers are missing:

- Root cause: the exact-cwd project key in `~/.claude.json` with
  `hasTrustDialogAccepted=false` suppresses the SessionStart directive that launches
  Monitor, so no Monitor attaches and the inbox never streams (the receiver still reports
  `mode=monitor`).
- Repair (operator action only): repair Claude project trust for that exact cwd, restart
  the receiver session, then re-verify the success markers above. intent-cli never
  auto-detects or edits `~/.claude.json`.

## Windows guidance

- On Windows, start the monitor-mode Claude Code receiver from **Git Bash**. Dogfooding
  showed PowerShell / native-Windows startup may not attach the agmsg Monitor reliably
  (the SessionStart `watch.sh` directive assumes a bash environment), so the receiver can
  report `mode=monitor` yet never stream.
- If Git Bash is unavailable or the Monitor still does not attach on Windows, fall back to
  `turn` delivery or manual `inbox.sh` polling (see the fallback ladder) — do not report
  the receiver ready on `mode=monitor` alone.

## Fallback ladder — orchestrator mode stays usable without realtime Monitor

Realtime Monitor delivery is a convenience, **not** a requirement for orchestrator mode.
When the success markers are missing, work this bounded ladder and then keep going with
an explicit fallback; do not silently claim a live monitor.

1. Restart the receiver Claude Code session so the SessionStart directive re-launches
   `watch.sh`/Monitor on a fresh turn, then re-check the success markers.
2. Verify project trust / session: the exact-cwd `~/.claude.json` project key must have
   trust accepted (see the trust-repair runbook); confirm `ToolSearch select:Monitor`
   resolves the generic Monitor tool in that session.
3. On Windows, relaunch the receiver from Git Bash (see Windows guidance) rather than
   PowerShell / a native shell.
4. Compare against a known-good receiver project (one already showing `1 monitor` /
   `Monitor event`) to isolate whether the break is this cwd's config or the environment.
5. If it still will not attach, fall back to `turn` delivery or manual `inbox.sh` polling
   and say so explicitly, or escalate to the operator. A Bash/background `watch.sh`
   (`1 shell`) is diagnostic/fallback only — never a substitute for the Claude Code
   Monitor, and never a reason to report the receiver as live-monitored.

See the [agmsg monitor-delivery docs](https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md)
for backend-specific delivery/watch details; intent-cli does not own or modify agmsg
internals or Claude Code tool availability.

## Missing-Monitor project-settings diagnosis

The trust-repair runbook and fallback ladder above assume a `Monitor` tool *exists* but is
not attached. A distinct, higher-priority failure is when **`ToolSearch select:Monitor`
finds no `Monitor` tool at all** — the tool is simply absent, not `1 shell` vs `1 monitor`.
That is a **Claude Code tool-surface problem first, before it is an agmsg delivery
problem**, regardless of what `delivery.sh status` `mode=monitor` reports. agmsg cannot
stream through a Monitor tool that Claude Code is not exposing, so debugging agmsg here
wastes effort.

**Known-good comparison checklist** — diff this project's Claude Code config against a
folder where `1 monitor` already works:

- `.claude/settings.json`
- `.claude/settings.local.json`
- `~/.claude.json` project trust / onboarding flags
- the enabled / disabled MCP server lists
- project-level `env` settings

**Suspect project-level `env` overrides** (observed in dogfooding under `.claude/settings.json`
`env`) that can suppress the tool surface so `Monitor` never appears:

- `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true`
- `CLAUDE_CODE_ENABLE_TELEMETRY=false`
- `DISABLE_ERROR_REPORTING=true`
- `DISABLE_TELEMETRY=true`

Removing or isolating these project `env` overrides (agmsg hooks preserved) restored
`ToolSearch select:Monitor` in the affected folders.

**Safe remediation** (operator action; does not touch agmsg):

1. Close the Claude Code sessions.
2. Remove or isolate the suspect project-level `env` settings, **preserving the agmsg
   SessionStart hooks**.
3. Reopen Claude Code.
4. Run `ToolSearch select:Monitor`.
5. Verify `Monitor(agmsg inbox stream)`, the footer `1 monitor`, and
   `Monitor event: "agmsg inbox stream"` as inbox messages arrive.

This is a Claude Code project-config repair, not an agmsg change. Preserve the G516
distinction throughout: `1 monitor` is success, `1 shell` is fallback/failure.

This page does not change agmsg scripts (`watch.sh` / `delivery.sh`) and intent-cli does
not edit `.claude/settings.json` or `~/.claude.json`; the trust repair and project-settings
repair are operator actions only.
