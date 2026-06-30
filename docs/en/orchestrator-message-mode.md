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

This page does not change agmsg scripts (`watch.sh` / `delivery.sh`) and intent-cli does
not edit `~/.claude.json`; the trust repair is an operator action only.
