using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G487: coverage for the optional agmsg-backed orchestrator-thread guide
/// surface — mode separation (no mixed-mode timer races), the three thread
/// prompts, the structured agmsg reply contract, the orchestrator first-wake,
/// and the safety boundaries — across both markdown and JSON output.
/// </summary>
public sealed class GuideOrchestratorThreadCommandTests
{
    [Fact]
    public void Execute_Markdown_SeparatesTimerLoopFromOrchestratorMode_AndForbidsMixedTimers()
    {
        var output = RunMarkdown(["--domain", "estivo", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("# Guide — agmsg-backed orchestrator thread (G487)", output, StringComparison.Ordinal);
        Assert.Contains("## Mode separation", output, StringComparison.Ordinal);
        Assert.Contains("timer-loop mode", output, StringComparison.Ordinal);
        Assert.Contains("orchestrator-message mode", output, StringComparison.Ordinal);
        // G540: orchestrator-message mode is the PRIMARY model; timer-loop
        // mode is the fully supported, simpler ALTERNATIVE — preserved, not
        // replaced or removed.
        Assert.Contains("PRIMARY model", output, StringComparison.Ordinal);
        Assert.Contains("ALTERNATIVE — fully supported", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Opt-in mode", output, StringComparison.Ordinal);
        Assert.DoesNotContain("preview", output, StringComparison.OrdinalIgnoreCase);
        // Mixed-mode timer race is explicitly forbidden.
        Assert.Contains("do NOT launch the implementation/review recurring timer loops", output, StringComparison.Ordinal);
        // agmsg is signal-only; intent-cli/GitHub authoritative.
        Assert.Contains("agmsg", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli and GitHub remain authoritative", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_HasDesignOrchestratorDoubleCheckRule_G540()
    {
        var output = RunMarkdown(["--domain", "estivo", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("design↔orchestrator double-check", output, StringComparison.Ordinal);
        Assert.Contains("intent shaping and clarifications", output, StringComparison.Ordinal);
        Assert.Contains("packet content and acceptance criteria", output, StringComparison.Ordinal);
        Assert.Contains("release scope and version selection", output, StringComparison.Ordinal);
        Assert.Contains("prioritization rulings", output, StringComparison.Ordinal);
        Assert.Contains("NEVER authors design content unilaterally", output, StringComparison.Ordinal);
        Assert.Contains("DESIGN NEVER bypasses the orchestrator for workflow transitions", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_EmitsThreePasteReadyThreadPrompts()
    {
        var output = RunMarkdown(["--domain", "estivo", "--target-repo", "owner/repo", "--agent", "codex"]);

        Assert.Contains("### orchestrator", output, StringComparison.Ordinal);
        Assert.Contains("### implementation", output, StringComparison.Ordinal);
        Assert.Contains("### review", output, StringComparison.Ordinal);
        // Placeholders are substituted into the prompts.
        Assert.Contains("domain `estivo`", output, StringComparison.Ordinal);
        Assert.Contains("`owner/repo`", output, StringComparison.Ordinal);
        Assert.Contains("`codex`", output, StringComparison.Ordinal);
        // Implementation thread still derives issue/PR numbers from worker next-action, not agmsg text.
        Assert.Contains("worker next-action", output, StringComparison.Ordinal);
        Assert.Contains("Closes #<issue>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_EmitsStructuredReplyContractAndFirstWakeAndBoundaries()
    {
        var output = RunMarkdown([]);

        // Reply contract: accepted / progress / completed / blocked.
        Assert.Contains("## agmsg reply contract", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"accepted\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"progress\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"blocked\"", output, StringComparison.Ordinal);

        // First wake: read replies, ask intent-cli, verify GitHub, per-receiver delegation cap (G524).
        Assert.Contains("## Orchestrator first wake", output, StringComparison.Ordinal);
        Assert.Contains("AT MOST ONE DELEGATION PER RECEIVER", output, StringComparison.Ordinal);

        // Safety boundaries.
        Assert.Contains("## Safety boundaries", output, StringComparison.Ordinal);
        Assert.Contains("No raw label mutation", output, StringComparison.Ordinal);
        Assert.Contains("No hand-editing queue-state", output, StringComparison.Ordinal);
        Assert.Contains("never replaces semantic review", output, StringComparison.Ordinal);
        Assert.Contains("Fail closed on duplicate orchestrators", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasStableShape_WithThreeThreadsAndReplyContract()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "estivo", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        var roles = root.GetProperty("threads").EnumerateArray()
            .Select(t => t.GetProperty("role").GetString())
            .ToArray();
        Assert.Equal(new[] { "orchestrator", "implementation", "review" }, roles);

        var contract = root.GetProperty("agmsg_reply_contract");
        Assert.True(contract.TryGetProperty("accepted", out _));
        Assert.True(contract.TryGetProperty("progress", out _));
        Assert.True(contract.TryGetProperty("completed", out _));
        Assert.True(contract.TryGetProperty("blocked", out _));

        Assert.NotEmpty(root.GetProperty("orchestrator_first_wake").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("safety_boundaries").EnumerateArray());

        var mode = root.GetProperty("mode_separation");
        Assert.True(mode.TryGetProperty("timer_loop_mode", out _));
        Assert.True(mode.TryGetProperty("orchestrator_message_mode", out _));
        Assert.True(mode.TryGetProperty("mixed_mode_warning", out _));
    }

    [Fact]
    public void Execute_Markdown_SingleDomain_ScopesToOneDomain_AndDefersOtherDomainMetadata()
    {
        // Default mode is single-domain.
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Domain routing — single-domain vs multi-domain", output, StringComparison.Ordinal);
        Assert.Contains("selected mode: `single-domain`", output, StringComparison.Ordinal);
        // The orchestrator prompt scopes to one domain and defers other-domain items.
        Assert.Contains("SINGLE-DOMAIN mode", output, StringComparison.Ordinal);
        Assert.Contains("OUT OF SCOPE", output, StringComparison.Ordinal);
        Assert.Contains("switch", output, StringComparison.Ordinal);
        // Prefix mismatch is explicitly not a wrong-repo signal.
        Assert.Contains("prefix", output, StringComparison.Ordinal);
        Assert.Contains("packet/domain metadata", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_MultiDomain_RequiresExplicitRoutingMetadata()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--mode", "multi-domain"]);

        Assert.Contains("selected mode: `multi-domain`", output, StringComparison.Ordinal);
        Assert.Contains("MULTI-DOMAIN mode", output, StringComparison.Ordinal);
        // All required routing fields are listed.
        Assert.Contains("execution unit", output, StringComparison.Ordinal);
        Assert.Contains("implementation cwd/worktree", output, StringComparison.Ordinal);
        Assert.Contains("review cwd/worktree", output, StringComparison.Ordinal);
        Assert.Contains("base branch policy", output, StringComparison.Ordinal);
        Assert.Contains("destination thread", output, StringComparison.Ordinal);
        // Delegation example carries the full routing payload, incl. one repo serving multiple domains.
        Assert.Contains("\"execution_unit\":\"G491\"", output, StringComparison.Ordinal);
        Assert.Contains("\"base_branch_policy\":\"direct-main\"", output, StringComparison.Ordinal);
        Assert.Contains("\"destination_thread\":", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DistinguishesMonitorToolFromDeliveryMode_WithVerificationAndRepairMarkers()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // G511 section is present.
        Assert.Contains("## Monitor tool vs delivery-mode (G511)", output, StringComparison.Ordinal);

        // The distinction: Monitor is a generic Claude Code tool fed by agmsg via watch.sh from SessionStart.
        Assert.Contains("`Monitor` is a generic Claude Code tool", output, StringComparison.Ordinal);
        Assert.Contains("`watch.sh` from the Claude Code SessionStart", output, StringComparison.Ordinal);

        // delivery-mode config is not proof of attachment.
        Assert.Contains("`delivery.sh status` `mode=monitor` is configuration only", output, StringComparison.Ordinal);
        Assert.Contains("NOT proof that a Monitor tool is attached and streaming", output, StringComparison.Ordinal);

        // The four live-attachment success markers, in checkable form.
        Assert.Contains("`ToolSearch select:Monitor` resolves Monitor", output, StringComparison.Ordinal);
        Assert.Contains("`Monitor(agmsg inbox stream)`", output, StringComparison.Ordinal);
        Assert.Contains("footer shows `1 monitor`", output, StringComparison.Ordinal);
        Assert.Contains("`Monitor event`", output, StringComparison.Ordinal);

        // The failure markers.
        Assert.Contains("falls back to a plain `Bash` / background `watch.sh`", output, StringComparison.Ordinal);
        Assert.Contains("footer shows `1 shell`", output, StringComparison.Ordinal);
        Assert.Contains("`Azure Monitor` / other MCP `monitor` tools", output, StringComparison.Ordinal);

        // The trust-repair runbook root cause and repair.
        Assert.Contains("`~/.claude.json` with `hasTrustDialogAccepted=false` suppresses", output, StringComparison.Ordinal);
        Assert.Contains("repair Claude project trust for that exact cwd, restart", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli never auto-detects or edits `~/.claude.json`", output, StringComparison.Ordinal);

        // G516: Windows / Git Bash guidance.
        Assert.Contains("Windows guidance:", output, StringComparison.Ordinal);
        Assert.Contains("start the monitor-mode Claude Code receiver from **Git Bash**", output, StringComparison.Ordinal);
        Assert.Contains("PowerShell / native-Windows startup may not attach the agmsg Monitor", output, StringComparison.Ordinal);

        // G516: bounded fallback ladder keeps orchestrator mode usable without realtime Monitor.
        Assert.Contains("Fallback ladder — orchestrator mode stays usable without realtime Monitor:", output, StringComparison.Ordinal);
        Assert.Contains("Realtime Monitor delivery is NOT required for orchestrator mode", output, StringComparison.Ordinal);
        Assert.Contains("fall back to `turn` delivery or manual `inbox.sh` polling", output, StringComparison.Ordinal);
        Assert.Contains("diagnostic/fallback only — never a substitute for the Claude Code Monitor", output, StringComparison.Ordinal);

        // G517: missing-Monitor project-settings diagnosis (tool-surface first, before agmsg).
        Assert.Contains("Missing-Monitor project-settings diagnosis (G517)", output, StringComparison.Ordinal);
        Assert.Contains("Claude Code TOOL-SURFACE problem FIRST, before debugging agmsg delivery", output, StringComparison.Ordinal);
        Assert.Contains("`.claude/settings.json`, `.claude/settings.local.json`, `~/.claude.json` project trust/onboarding flags", output, StringComparison.Ordinal);
        Assert.Contains("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=true", output, StringComparison.Ordinal);
        Assert.Contains("DISABLE_TELEMETRY=true", output, StringComparison.Ordinal);
        Assert.Contains("PRESERVING the agmsg SessionStart hooks", output, StringComparison.Ordinal);
        // G517 preserves the G516 marker distinction.
        Assert.Contains("`1 monitor` = live success, `1 shell` = diagnostic/fallback only", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_CodexBridgeGuidance_CoversSetupPreflightAndFailureModes_G521()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Codex monitor (beta) failure modes (G521)", output, StringComparison.Ordinal);

        // Version-observation framing: scopes the failure modes to the tested environment, not a permanent contract.
        Assert.Contains("Observed at agmsg 1.1.6 / Codex v0.144.1", output, StringComparison.Ordinal);
        Assert.Contains("not a permanent bridge contract", output, StringComparison.Ordinal);
        Assert.Contains("Re-verify against the installed agmsg/Codex versions after an upgrade", output, StringComparison.Ordinal);

        // Setup preflight: single-identity precondition before launching a Codex receiver.
        Assert.Contains("resolves to exactly ONE identity", output, StringComparison.Ordinal);
        Assert.Contains("`whoami.sh <project> codex` should print a single `agent=` line", output, StringComparison.Ordinal);

        // Healthy-state markers.
        Assert.Contains("`delivery.sh status` shows `Codex bridge: <team>/<role> alive (pid N)`", output, StringComparison.Ordinal);
        Assert.Contains("bridge arms on the FIRST turn", output, StringComparison.Ordinal);
        Assert.Contains("already-running Codex session stays unmonitored until it is restarted", output, StringComparison.Ordinal);

        // Troubleshooting entry 1: silent launcher blocked by multiple identities.
        Assert.Contains("mode: monitor but the Codex bridge never starts", output, StringComparison.Ordinal);
        Assert.Contains("resolves to more than one identity", output, StringComparison.Ordinal);
        Assert.Contains("retries silently every 0.3s forever", output, StringComparison.Ordinal);

        // Troubleshooting entry 2: static TUI from stale loaded app-server threads, full recovery sequence.
        Assert.Contains("bridge alive (pid shown) but the Codex TUI never moves", output, StringComparison.Ordinal);
        Assert.Contains("attaches to the FIRST (oldest) entry of `thread/loaded/list`", output, StringComparison.Ordinal);
        Assert.Contains("quit the TUI, stop the app-server/bridge/launcher processes", output, StringComparison.Ordinal);
        Assert.Contains("send one turn to re-arm", output, StringComparison.Ordinal);

        // Troubleshooting entry 3: doubled bridge.
        Assert.Contains("responses to one message appear twice across a restart window", output, StringComparison.Ordinal);
        Assert.Contains("Suspect a doubled bridge", output, StringComparison.Ordinal);

        // Links out to agmsg internals rather than restating them.
        Assert.Contains("https://github.com/fujibee/agmsg/blob/main/docs/codex-monitor-beta.md", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_CarriesCodexBridgeGuidance_WithTroubleshootingArray()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var guidance = doc.RootElement.GetProperty("codex_bridge_guidance");

        Assert.Contains("agmsg 1.1.6", guidance.GetProperty("observed_versions").GetString(), StringComparison.Ordinal);
        Assert.Contains("Codex v0.144.1", guidance.GetProperty("observed_versions").GetString(), StringComparison.Ordinal);
        Assert.True(guidance.TryGetProperty("setup_preflight", out _));
        Assert.NotEmpty(guidance.GetProperty("healthy_state_markers").EnumerateArray());
        Assert.Equal(3, guidance.GetProperty("troubleshooting").GetArrayLength());
        Assert.True(guidance.TryGetProperty("reference_link", out _));
    }

    [Fact]
    public void Execute_Json_CarriesMonitorToolDistinction_WithMarkerArrays()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var distinction = doc.RootElement.GetProperty("monitor_tool_distinction");

        Assert.True(distinction.TryGetProperty("summary", out _));
        Assert.True(distinction.TryGetProperty("delivery_mode_note", out _));
        Assert.Equal(4, distinction.GetProperty("success_markers").GetArrayLength());
        Assert.NotEmpty(distinction.GetProperty("failure_markers").EnumerateArray());
        Assert.NotEmpty(distinction.GetProperty("trust_repair").EnumerateArray());
        Assert.NotEmpty(distinction.GetProperty("windows_guidance").EnumerateArray());
        Assert.NotEmpty(distinction.GetProperty("fallback_ladder").EnumerateArray());
        Assert.NotEmpty(distinction.GetProperty("project_settings_diagnosis").EnumerateArray());
    }

    [Fact]
    public void Execute_Markdown_ImplementationThread_VerifiesLocalCheckoutBeforeClaiming()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // Implementation-thread prompt: target from worker next-action, checkout must match before claiming.
        Assert.Contains("verify your local checkout context matches the delegation", output, StringComparison.Ordinal);
        Assert.Contains("STOP and reply blocked instead of claiming", output, StringComparison.Ordinal);
        Assert.Contains("worker next-action", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasDomainRoutingShape_ForMultiDomain()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--mode", "multi-domain", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var routing = doc.RootElement.GetProperty("domain_routing");

        Assert.Equal("multi-domain", routing.GetProperty("mode").GetString());
        Assert.True(routing.TryGetProperty("single_domain_rule", out _));
        Assert.True(routing.TryGetProperty("multi_domain_rule", out _));
        Assert.True(routing.TryGetProperty("prefix_mismatch_note", out _));

        var fields = routing.GetProperty("routing_metadata_fields").EnumerateArray()
            .Select(f => f.GetString())
            .ToArray();
        Assert.Contains("domain", fields);
        Assert.Contains("execution unit", fields);
        Assert.Contains("implementation cwd/worktree", fields);
        Assert.Contains("review cwd/worktree", fields);
        Assert.Contains("base branch policy", fields);
        Assert.Contains("destination thread", fields);
    }

    [Fact]
    public void Execute_Markdown_IsMessageDriven_WithOptionalFallbackCodexAndClaudeLoopPrompts_G518()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Scheduled orchestrator cadence", output, StringComparison.Ordinal);
        // Steady state is message-driven; the orchestrator is no longer described as the
        // unconditional single recurring 5m driver.
        Assert.Contains("MESSAGE-DRIVEN", output, StringComparison.Ordinal);
        Assert.Contains("routine fast polling is NOT required", output, StringComparison.Ordinal);
        Assert.DoesNotContain("is the SINGLE recurring driver", output, StringComparison.Ordinal);
        Assert.Contains("scheduled thread when an explicit timer is used: `orchestrator`", output, StringComparison.Ordinal);
        // Both setup prompts are present, but framed as optional fallback/legacy polling.
        Assert.Contains("Codex automation (5m) — orchestrator (fallback/legacy, optional)", output, StringComparison.Ordinal);
        Assert.Contains("Claude `/loop 5m` — orchestrator (fallback/legacy, optional)", output, StringComparison.Ordinal);
        Assert.Contains("OPTIONAL fallback/legacy polling", output, StringComparison.Ordinal);
        // Receivers are explicitly loopless regardless of drive mode.
        Assert.Contains("loopless receiver", output, StringComparison.Ordinal);
        Assert.Contains("do NOT start your own", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_HasDesignThreadWatchdog_AsRecommendedSafetyNet_G539()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // G539: the design-thread watchdog is now the RECOMMENDED default,
        // superseding G526's external cron/launchd recommendation.
        Assert.Contains("## Design-thread watchdog (recommended safety net)", output, StringComparison.Ordinal);
        Assert.Contains("optional: yes", output, StringComparison.Ordinal);
        Assert.Contains("30-minute class", output, StringComparison.Ordinal);
        Assert.Contains("RECOMMENDED", output, StringComparison.Ordinal);
        Assert.Contains("Loop setup prompt", output, StringComparison.Ordinal);
        Assert.Contains("/loop 30m", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation heartbeat --domain intent-cli --repo J-Tech-Japan/intent-system --format json", output, StringComparison.Ordinal);
        Assert.Contains("exactly ONE", output, StringComparison.Ordinal);
        // G539 repair round 1: silence is reserved for a healthy stale=false
        // result ONLY — a command failure or malformed output must be
        // surfaced visibly (never silently swallowed/retried), while still
        // never fabricating or sending a nudge from broken input.
        Assert.Contains("- **failure visibility** —", output, StringComparison.Ordinal);
        Assert.Contains("silence is reserved for this healthy case ONLY", output, StringComparison.Ordinal);
        Assert.Contains("is NEVER silent", output, StringComparison.Ordinal);
        Assert.Contains("state the failure explicitly in this wake's own turn output", output, StringComparison.Ordinal);
        Assert.Contains("visible to the operator watching this live session", output, StringComparison.Ordinal);
        Assert.Contains("never fabricating or sending an agmsg nudge from broken input", output, StringComparison.Ordinal);
        Assert.DoesNotContain("stay silent this wake and retry next", output, StringComparison.Ordinal);
        Assert.Contains("### Watchdog checks", output, StringComparison.Ordinal);
        Assert.Contains("HITL", output, StringComparison.Ordinal);
        Assert.Contains("orchestrator staleness", output, StringComparison.Ordinal);
        Assert.Contains("AT MOST ONE canonical repair/status", output, StringComparison.Ordinal);
        Assert.Contains("stop condition", output, StringComparison.Ordinal);
        Assert.Contains("backlog and the human-decision (HITL) queues are", output, StringComparison.Ordinal);
        Assert.Contains("### Watchdog safety rules", output, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED: duplicate delegation", output, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED: clearing a permission prompt", output, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED: cancelling or resetting", output, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED: force-closing", output, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED: speculative durable-state surgery", output, StringComparison.Ordinal);
        // The 5-minute orchestrator fallback timer remains supported, unchanged, as legacy/discouraged.
        Assert.Contains("remains SUPPORTED as fallback/legacy polling", output, StringComparison.Ordinal);
        Assert.Contains("Claude same-thread `/loop 5m`", output, StringComparison.Ordinal);
        // G539: measured weakness, weighed against the retired cron scheduler's total silent failure.
        Assert.Contains("measured weakness", output, StringComparison.Ordinal);
        Assert.Contains("died 8-9 times in 16 days", output, StringComparison.Ordinal);
        Assert.Contains("failed SILENTLY on EVERY run for five continuous days", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_HasOrchestratorAutomationAlternative_AndRetiresExternalCron_G539()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Orchestrator-side long-interval automation (alternative safety net)", output, StringComparison.Ordinal);
        Assert.Contains("SELECTABLE ALTERNATIVE", output, StringComparison.Ordinal);
        Assert.Contains("30-60 minute class", output, StringComparison.Ordinal);
        Assert.Contains("trade-off", output, StringComparison.Ordinal);
        Assert.Contains("keeps the orchestrator strictly loopless", output, StringComparison.Ordinal);
        Assert.Contains("one fewer hop", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json", output, StringComparison.Ordinal);
        Assert.Contains("Setup prompt (paste into the orchestrator thread)", output, StringComparison.Ordinal);
        // No cron/launchd runner recommendation remains — it is explicitly retired.
        Assert.Contains("RETIRED (G539)", output, StringComparison.Ordinal);
        Assert.Contains("cron/launchd", output, StringComparison.Ordinal);
        Assert.Contains("credential-store access", output, StringComparison.Ordinal);
        Assert.Contains("invisible failure", output, StringComparison.Ordinal);
        Assert.Contains("outside the agmsg model", output, StringComparison.Ordinal);
        Assert.Contains("five continuous days", output, StringComparison.Ordinal);
        Assert.Contains("105-minute stall", output, StringComparison.Ordinal);
        Assert.Contains("G538 / PR #1179", output, StringComparison.Ordinal);
        // `automation heartbeat` itself stays scheduler-agnostic and unchanged.
        Assert.Contains("UNCHANGED and remains scheduler-agnostic", output, StringComparison.Ordinal);
        // The recommended design-thread watchdog section appears before this alternative.
        Assert.True(
            output.IndexOf("## Design-thread watchdog", StringComparison.Ordinal)
            < output.IndexOf("## Orchestrator-side long-interval automation", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_WakeResponsibilities_CoverStateChecksAndRepairEscalate()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "codex"]);

        Assert.Contains("### Each orchestrator wake", output, StringComparison.Ordinal);
        // State-check coverage.
        Assert.Contains("design-side progress", output, StringComparison.Ordinal);
        Assert.Contains("worker next-action", output, StringComparison.Ordinal);
        Assert.Contains("host-review-preflight", output, StringComparison.Ordinal);
        Assert.Contains("CI conclusion, approvals, merge state", output, StringComparison.Ordinal);
        Assert.Contains("stale blockers and no-reply receivers", output, StringComparison.Ordinal);
        // Repair vs escalate split.
        Assert.Contains("**repair**", output, StringComparison.Ordinal);
        Assert.Contains("**escalate**", output, StringComparison.Ordinal);
        Assert.Contains("credentials or security", output, StringComparison.Ordinal);
        Assert.Contains("destructive local action", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasSchedulingShape()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var scheduling = doc.RootElement.GetProperty("scheduling");

        Assert.Equal("orchestrator", scheduling.GetProperty("scheduled_thread").GetString());
        Assert.True(scheduling.TryGetProperty("codex_setup_prompt", out _));
        Assert.True(scheduling.TryGetProperty("claude_loop_setup_prompt", out _));
        Assert.True(scheduling.TryGetProperty("receiver_note", out _));
        Assert.NotEmpty(scheduling.GetProperty("wake_responsibilities").EnumerateArray());

        var repairEscalate = scheduling.GetProperty("repair_vs_escalate");
        Assert.True(repairEscalate.TryGetProperty("repair", out _));
        Assert.True(repairEscalate.TryGetProperty("escalate", out _));

        // Receiver thread prompts stay explicitly loopless.
        var prompts = doc.RootElement.GetProperty("threads").EnumerateArray()
            .ToDictionary(t => t.GetProperty("role").GetString()!, t => t.GetProperty("prompt").GetString()!);
        Assert.Contains("LOOPLESS receiver", prompts["implementation"], StringComparison.Ordinal);
        Assert.Contains("LOOPLESS receiver", prompts["review"], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_CarriesDesignWatchdog_AsRecommendedDefaultSafetyNet_G539()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var watchdog = doc.RootElement.GetProperty("design_watchdog");

        Assert.True(watchdog.GetProperty("optional").GetBoolean());
        Assert.Contains("30-minute class", watchdog.GetProperty("frequency").GetString(), StringComparison.Ordinal);
        Assert.Contains("RECOMMENDED", watchdog.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("G539", watchdog.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("/loop 30m", watchdog.GetProperty("loop_setup_prompt").GetString(), StringComparison.Ordinal);
        Assert.Contains("Codex automation", watchdog.GetProperty("loop_setup_prompt").GetString(), StringComparison.Ordinal);
        Assert.Contains("exactly ONE", watchdog.GetProperty("loop_setup_prompt").GetString(), StringComparison.Ordinal);

        // G539 repair round 1: the loop prompt must not tell the watchdog to
        // stay silent on a command failure or malformed output — silence is
        // reserved for a healthy stale=false result ONLY; a failure must be
        // surfaced visibly (never silently swallowed/retried), while still
        // never fabricating or sending a nudge from broken input.
        var loopPrompt = watchdog.GetProperty("loop_setup_prompt").GetString()!;
        Assert.Contains("silence is reserved for this healthy case ONLY", loopPrompt, StringComparison.Ordinal);
        Assert.Contains("is NEVER silent", loopPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("stay silent this wake and retry next", loopPrompt, StringComparison.Ordinal);

        var failureVisibility = watchdog.GetProperty("failure_visibility_rule").GetString()!;
        Assert.Contains("healthy `stale=false`", failureVisibility, StringComparison.Ordinal);
        Assert.Contains("surfaced VISIBLY", failureVisibility, StringComparison.Ordinal);
        Assert.Contains("never silently swallowed or silently retried", failureVisibility, StringComparison.Ordinal);
        Assert.Contains("never fabricating or sending an agmsg nudge from broken input", failureVisibility, StringComparison.Ordinal);

        Assert.Equal(
            "intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json",
            watchdog.GetProperty("heartbeat_command_example").GetString());
        Assert.NotEmpty(watchdog.GetProperty("checks").EnumerateArray());
        var checks = watchdog.GetProperty("checks").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Contains(checks, c => c.Contains("HITL", StringComparison.Ordinal));
        Assert.Contains(checks, c => c.Contains("automation heartbeat", StringComparison.Ordinal));
        Assert.True(watchdog.TryGetProperty("action", out _));
        Assert.True(watchdog.TryGetProperty("repair_status_request_template", out _));
        Assert.True(watchdog.TryGetProperty("stop_condition", out _));
        Assert.True(watchdog.TryGetProperty("fallback_timer_note", out _));

        var safetyRules = watchdog.GetProperty("safety_rules").EnumerateArray()
            .Select(r => r.GetString()!)
            .ToArray();
        Assert.Contains(safetyRules, r => r.Contains("duplicate delegation", StringComparison.Ordinal));
        Assert.Contains(safetyRules, r => r.Contains("permission prompt", StringComparison.Ordinal));
        Assert.Contains(safetyRules, r => r.Contains("cancelling or resetting", StringComparison.Ordinal));
        Assert.Contains(safetyRules, r => r.Contains("force-closing", StringComparison.Ordinal));
        Assert.Contains(safetyRules, r => r.Contains("speculative durable-state surgery", StringComparison.Ordinal));

        // Fallback timer (5-minute, legacy/discouraged) is present and unchanged in meaning.
        Assert.Contains("remains SUPPORTED as fallback/legacy polling", watchdog.GetProperty("fallback_timer_note").GetString(), StringComparison.Ordinal);
        Assert.Contains("Claude same-thread `/loop 5m`", watchdog.GetProperty("fallback_timer_note").GetString(), StringComparison.Ordinal);

        // Measured weakness is weighed against the retired external scheduler's total silent failure.
        Assert.Contains("died 8-9 times in 16 days", watchdog.GetProperty("measured_weakness").GetString(), StringComparison.Ordinal);
        Assert.Contains("failed SILENTLY on EVERY run for five continuous days", watchdog.GetProperty("measured_weakness").GetString(), StringComparison.Ordinal);

        // Scheduling no longer frames the orchestrator as the unconditional single recurring 5m driver.
        var scheduling = doc.RootElement.GetProperty("scheduling");
        var summary = scheduling.GetProperty("summary").GetString()!;
        Assert.Contains("MESSAGE-DRIVEN", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("is the SINGLE recurring driver", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_CarriesOrchestratorAutomationAlternative_AndRetiresExternalCron_G539()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var automation = doc.RootElement.GetProperty("orchestrator_automation_alternative");

        Assert.Contains("SELECTABLE ALTERNATIVE", automation.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("30-60 minute class", automation.GetProperty("frequency").GetString(), StringComparison.Ordinal);
        Assert.Contains("keeps the orchestrator strictly loopless", automation.GetProperty("trade_off").GetString(), StringComparison.Ordinal);
        Assert.Contains("one fewer hop", automation.GetProperty("trade_off").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            "intent-cli automation heartbeat --domain <domain> --repo <owner/repo> --format json",
            automation.GetProperty("command_example").GetString());
        Assert.Contains("IN THE ORCHESTRATOR THREAD", automation.GetProperty("setup_prompt").GetString(), StringComparison.Ordinal);

        // No cron/launchd runner recommendation remains — it is explicitly retired, with the
        // reason recorded and the measured field evidence cited (five-day silent failure and
        // the 105-minute unrecovered stall on G538 / PR #1179).
        var retired = automation.GetProperty("retired_cron_note").GetString()!;
        Assert.Contains("RETIRED (G539)", retired, StringComparison.Ordinal);
        Assert.Contains("cron/launchd", retired, StringComparison.Ordinal);
        Assert.Contains("credential-store access", retired, StringComparison.Ordinal);
        Assert.Contains("invisible failure", retired, StringComparison.Ordinal);
        Assert.Contains("outside the agmsg model", retired, StringComparison.Ordinal);
        Assert.Contains("five continuous days", retired, StringComparison.Ordinal);
        Assert.Contains("105-minute stall", retired, StringComparison.Ordinal);
        Assert.Contains("G538 / PR #1179", retired, StringComparison.Ordinal);
        Assert.Contains("UNCHANGED and remains scheduler-agnostic", retired, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_CiWaitState_RoutesPendingGreenRedStuck()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## CI wait state", output, StringComparison.Ordinal);
        // Pending is wait-and-recheck and does not trigger request-update/operator question.
        Assert.Contains("active wait state", output, StringComparison.Ordinal);
        Assert.Contains("- **pending**", output, StringComparison.Ordinal);
        Assert.Contains("do not apply request-update", output, StringComparison.Ordinal);
        // Green routes to review/closeout.
        Assert.Contains("- **green**", output, StringComparison.Ordinal);
        Assert.Contains("Route to review/closeout", output, StringComparison.Ordinal);
        // Red routes to repair/escalate by ownership.
        Assert.Contains("- **red**", output, StringComparison.Ordinal);
        Assert.Contains("Route by ownership", output, StringComparison.Ordinal);
        // Stuck escalates.
        Assert.Contains("- **stuck**", output, StringComparison.Ordinal);
        Assert.Contains("Escalate one operator decision", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasCiWaitStateShape_WithFourStates()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var ci = doc.RootElement.GetProperty("ci_wait_state");

        Assert.True(ci.TryGetProperty("summary", out _));
        var states = ci.GetProperty("states").EnumerateArray()
            .Select(s => s.GetProperty("state").GetString())
            .ToArray();
        Assert.Equal(new[] { "pending", "green", "red", "stuck" }, states);

        // The recurring-wake list references CI classification.
        var wake = doc.RootElement.GetProperty("scheduling").GetProperty("wake_responsibilities").EnumerateArray()
            .Select(w => w.GetString()!)
            .ToArray();
        Assert.Contains(wake, w => w.Contains("pending = wait-and-recheck", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_NextSlicePublication_IsOrchestratorResponsibility_OnePerWake()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Next-slice publication", output, StringComparison.Ordinal);
        // Routine publication is the orchestrator's job, not an operator question.
        Assert.Contains("ORCHESTRATOR responsibility, not an operator question", output, StringComparison.Ordinal);
        Assert.Contains("one_per_wake: yes", output, StringComparison.Ordinal);
        // Canonical publish surfaces are required; no raw gh.
        Assert.Contains("issue publish-flow", output, StringComparison.Ordinal);
        Assert.Contains("automation issue-publish", output, StringComparison.Ordinal);
        Assert.Contains("Never raw `gh issue create`", output, StringComparison.Ordinal);
        // Post-publish verification before delegating; receiver still uses worker next-action.
        Assert.Contains("### Post-publish verification", output, StringComparison.Ordinal);
        Assert.Contains("worker next-action", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_NextSlicePublication_ListsReadyGatesAndBlockers()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "codex"]);

        // Ready preconditions cover same-domain/routed, contract, clarification, dependencies, WIP, host-sync.
        Assert.Contains("### Publish only when ALL hold", output, StringComparison.Ordinal);
        Assert.Contains("never publish a cross-domain candidate without explicit routing", output, StringComparison.Ordinal);
        Assert.Contains("Dependencies are satisfied", output, StringComparison.Ordinal);
        Assert.Contains("WIP cap", output, StringComparison.Ordinal);
        Assert.Contains("host-sync / preflight", output, StringComparison.Ordinal);
        // Blocked cases.
        Assert.Contains("### Blocked by (hold or escalate)", output, StringComparison.Ordinal);
        Assert.Contains("Missing contract sections", output, StringComparison.Ordinal);
        Assert.Contains("Dependency mismatch", output, StringComparison.Ordinal);
        Assert.Contains("Ambiguous target repo or domain", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasNextSlicePublicationShape()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var publication = doc.RootElement.GetProperty("next_slice_publication");

        Assert.True(publication.GetProperty("one_per_wake").GetBoolean());
        Assert.NotEmpty(publication.GetProperty("preconditions").EnumerateArray());
        Assert.NotEmpty(publication.GetProperty("blockers").EnumerateArray());
        Assert.NotEmpty(publication.GetProperty("canonical_commands").EnumerateArray());
        Assert.NotEmpty(publication.GetProperty("post_publish_verification").EnumerateArray());

        // The orchestrator prompt describes publish-then-same-wake-delegate (G524).
        var orchestrator = doc.RootElement.GetProperty("threads").EnumerateArray()
            .First(t => t.GetProperty("role").GetString() == "orchestrator")
            .GetProperty("prompt").GetString()!;
        Assert.Contains("publish a ready next-slice issue", orchestrator, StringComparison.Ordinal);
        Assert.Contains("issue-cut-ready", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Setup_HasConcreteChecklist_PingTest_Cleanup_AndDbWarning_G494()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Setup (starting orchestrator mode)", output, StringComparison.Ordinal);
        // Decisions displayed: paths, base branch policy, agents, team, delivery.
        Assert.Contains("base branch policy", output, StringComparison.Ordinal);
        Assert.Contains("agmsg team name", output, StringComparison.Ordinal);
        Assert.Contains("delivery mode", output, StringComparison.Ordinal);
        Assert.Contains("implementation / review paths", output, StringComparison.Ordinal);
        // Role registration + delivery commands.
        Assert.Contains("### agmsg commands", output, StringComparison.Ordinal);
        Assert.Contains("join.sh", output, StringComparison.Ordinal);
        Assert.Contains("delivery.sh", output, StringComparison.Ordinal);
        // First read-only wake + ping test.
        Assert.Contains("read-only first wake", output, StringComparison.Ordinal);
        Assert.Contains("ping test", output, StringComparison.Ordinal);
        // Cleanup via agmsg scripts.
        Assert.Contains("### Cleanup", output, StringComparison.Ordinal);
        Assert.Contains("leave.sh", output, StringComparison.Ordinal);
        // Warn not to edit agmsg DB/team files directly.
        Assert.Contains("Never edit the agmsg database or team files directly", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasSetupShape_G494()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var setup = doc.RootElement.GetProperty("setup");

        Assert.NotEmpty(setup.GetProperty("decisions").EnumerateArray());
        Assert.NotEmpty(setup.GetProperty("checklist").EnumerateArray());
        Assert.NotEmpty(setup.GetProperty("agmsg_commands").EnumerateArray());
        Assert.NotEmpty(setup.GetProperty("cleanup").EnumerateArray());
        Assert.True(setup.TryGetProperty("ping_test", out _));
        Assert.Contains("agmsg scripts", setup.GetProperty("warning").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DependencyPlanning_RoutesToEarliestUnmetDependency_NotOperator_G495()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Dependency planning", output, StringComparison.Ordinal);
        // Unmet deps are normal work, not an operator stop.
        Assert.Contains("NORMAL orchestration work when explicit and resolvable", output, StringComparison.Ordinal);
        Assert.Contains("not an operator", output, StringComparison.Ordinal);
        // Earliest unmet dependency first; dependent held.
        Assert.Contains("EARLIEST unmet same-domain dependency first", output, StringComparison.Ordinal);
        Assert.Contains("**dependent hold**", output, StringComparison.Ordinal);
        // The five structured statuses.
        Assert.Contains("- **dependency-publish-ready**", output, StringComparison.Ordinal);
        Assert.Contains("- **dependency-actionable**", output, StringComparison.Ordinal);
        Assert.Contains("- **dependency-waiting**", output, StringComparison.Ordinal);
        Assert.Contains("- **dependency-ambiguous**", output, StringComparison.Ordinal);
        Assert.Contains("- **dependency-cycle**", output, StringComparison.Ordinal);
        // Escalation reserved for ambiguous/cycle/cross-domain/etc.
        Assert.Contains("### Escalate only when", output, StringComparison.Ordinal);
        Assert.Contains("dependency packet is missing", output, StringComparison.Ordinal);
        Assert.Contains("cross-domain dependency has no explicit route mapping", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasDependencyPlanningShape_WithFiveStatuses_G495()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var planning = doc.RootElement.GetProperty("dependency_planning");

        Assert.True(planning.TryGetProperty("selection_rule", out _));
        Assert.True(planning.TryGetProperty("dependent_hold", out _));
        var statuses = planning.GetProperty("statuses").EnumerateArray()
            .Select(s => s.GetProperty("status").GetString())
            .ToArray();
        Assert.Equal(
            new[] { "dependency-publish-ready", "dependency-actionable", "dependency-waiting", "dependency-ambiguous", "dependency-cycle" },
            statuses);
        Assert.NotEmpty(planning.GetProperty("escalation_cases").EnumerateArray());

        // The orchestrator prompt treats unmet dependencies as routine, not a stop.
        var orchestrator = doc.RootElement.GetProperty("threads").EnumerateArray()
            .First(t => t.GetProperty("role").GetString() == "orchestrator")
            .GetProperty("prompt").GetString()!;
        Assert.Contains("Unmet dependencies are normal work", orchestrator, StringComparison.Ordinal);
        Assert.Contains("EARLIEST unmet resolvable dependency", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_StaleThreadHealthCheck_AsksBeforeActing_AndProtectsPermission_G496()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Stale-thread health check", output, StringComparison.Ordinal);
        // Configurable 30-minute threshold.
        Assert.Contains("30 minutes", output, StringComparison.Ordinal);
        Assert.Contains("configurable", output, StringComparison.Ordinal);
        // Non-destructive status-request before any retry/escalate (ask-first).
        Assert.Contains("non-destructive status-request", output, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"status-request\"", output, StringComparison.Ordinal);
        // Required receiver statuses.
        Assert.Contains("- **working**", output, StringComparison.Ordinal);
        Assert.Contains("- **waiting-ci**", output, StringComparison.Ordinal);
        Assert.Contains("- **waiting-permission**", output, StringComparison.Ordinal);
        Assert.Contains("- **blocked**", output, StringComparison.Ordinal);
        Assert.Contains("- **completed**", output, StringComparison.Ordinal);
        Assert.Contains("- **idle**", output, StringComparison.Ordinal);
        // Permission-waiting cannot trigger automatic retry/clear.
        Assert.Contains("never auto-clear", output, StringComparison.OrdinalIgnoreCase);
        // Progress-detected => keep watching; repeated-no-progress => one idempotent re-entry.
        Assert.Contains("keep watching", output, StringComparison.Ordinal);
        Assert.Contains("idempotent re-entry", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasStaleThreadHealthCheckShape_WithSixReceiverStatuses_G496()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var health = doc.RootElement.GetProperty("stale_thread_health_check");

        Assert.True(health.TryGetProperty("no_reply_threshold", out _));
        Assert.True(health.TryGetProperty("status_request_template", out _));
        Assert.NotEmpty(health.GetProperty("procedure").EnumerateArray());
        Assert.NotEmpty(health.GetProperty("safety").EnumerateArray());

        var statuses = health.GetProperty("receiver_statuses").EnumerateArray()
            .Select(s => s.GetProperty("status").GetString())
            .ToArray();
        Assert.Equal(
            new[] { "working", "waiting-ci", "waiting-permission", "blocked", "completed", "idle" },
            statuses);

        // The orchestrator prompt references the safe health check + no-auto-clear.
        var orchestrator = doc.RootElement.GetProperty("threads").EnumerateArray()
            .First(t => t.GetProperty("role").GetString() == "orchestrator")
            .GetProperty("prompt").GetString()!;
        Assert.Contains("stale-thread health check", orchestrator, StringComparison.Ordinal);
        Assert.Contains("never auto-clear a permission prompt", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DesignThreadEscalation_KeepsRoutineInternal_EscalatesHumanNeeded_G498()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Design-thread escalation filter", output, StringComparison.Ordinal);
        // Design thread is the primary human surface; this is a noise filter, not a failure filter.
        Assert.Contains("PRIMARY human communication surface", output, StringComparison.Ordinal);
        Assert.Contains("never hide a failure that needs a human", output, StringComparison.Ordinal);
        // Quiet normal path: routine success/idle/progress kept internal.
        Assert.Contains("### Kept internal", output, StringComparison.Ordinal);
        Assert.Contains("Successful implementation", output, StringComparison.Ordinal);
        Assert.Contains("Idle wakes", output, StringComparison.Ordinal);
        Assert.Contains("CI waiting", output, StringComparison.Ordinal);
        // Human-needed escalation path.
        Assert.Contains("### Escalate to the design thread when", output, StringComparison.Ordinal);
        Assert.Contains("Clarification required", output, StringComparison.Ordinal);
        Assert.Contains("Permission / credentials / security", output, StringComparison.Ordinal);
        Assert.Contains("Release / public publish decision", output, StringComparison.Ordinal);
        // Structured escalation message with current authoritative state + evidence + decision needed.
        Assert.Contains("\"to\":\"design\"", output, StringComparison.Ordinal);
        Assert.Contains("\"current_state\"", output, StringComparison.Ordinal);
        Assert.Contains("AUTHORITATIVE state", output, StringComparison.Ordinal);
        Assert.Contains("\"decision_needed\"", output, StringComparison.Ordinal);
        // Field semantics make the current-state requirement explicit and options optional.
        Assert.Contains("current_state — the current AUTHORITATIVE state", output, StringComparison.Ordinal);
        Assert.Contains("options — OPTIONAL", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasDesignThreadEscalationShape_G498()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var escalation = doc.RootElement.GetProperty("design_thread_escalation");

        Assert.NotEmpty(escalation.GetProperty("kept_internal").EnumerateArray());
        Assert.NotEmpty(escalation.GetProperty("escalate_when").EnumerateArray());
        var template = escalation.GetProperty("escalation_message_template").GetString()!;
        Assert.Contains("current_state", template, StringComparison.Ordinal);
        Assert.Contains("evidence", template, StringComparison.Ordinal);
        Assert.Contains("decision_needed", template, StringComparison.Ordinal);

        // The required AC field — current authoritative state — is explicitly documented.
        var fields = escalation.GetProperty("message_fields").EnumerateArray()
            .Select(f => f.GetString()!)
            .ToArray();
        Assert.Contains(fields, f => f.StartsWith("current_state", StringComparison.Ordinal) && f.Contains("AUTHORITATIVE", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.StartsWith("options", StringComparison.Ordinal) && f.Contains("OPTIONAL", StringComparison.Ordinal));

        // The orchestrator prompt applies the filter and never hides failures.
        var orchestrator = doc.RootElement.GetProperty("threads").EnumerateArray()
            .First(t => t.GetProperty("role").GetString() == "orchestrator")
            .GetProperty("prompt").GetString()!;
        Assert.Contains("human-facing DESIGN thread ONLY human-needed decisions", orchestrator, StringComparison.Ordinal);
        Assert.Contains("never hide a failure that needs a human", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ManagedWorktreeCleanup_RecommendsManagedRoot_ForbidsRawRm_G499()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Managed worktree cleanup", output, StringComparison.Ordinal);
        // Recommend a managed root instead of arbitrary /tmp paths.
        Assert.Contains(".intent-cli/worktrees", output, StringComparison.Ordinal);
        Assert.Contains("git worktree add", output, StringComparison.Ordinal);
        // Forbid raw rm -rf and use git worktree remove.
        Assert.Contains("never raw `rm -rf`", output, StringComparison.Ordinal);
        Assert.Contains("git worktree remove", output, StringComparison.Ordinal);
        // approval_policy=never is not a substitute.
        Assert.Contains("approval_policy=never", output, StringComparison.Ordinal);
        Assert.Contains("not a substitute", output, StringComparison.Ordinal);
        // Refuse unsafe paths and dirty user work.
        Assert.Contains("### Refuse cleanup when", output, StringComparison.Ordinal);
        Assert.Contains("OUTSIDE the allowlisted managed root", output, StringComparison.Ordinal);
        Assert.Contains("uncommitted or untracked user work", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasWorktreeManagementShape_G499()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var worktree = doc.RootElement.GetProperty("worktree_management");

        Assert.Contains(".intent-cli/worktrees", worktree.GetProperty("managed_root").GetString()!, StringComparison.Ordinal);
        Assert.NotEmpty(worktree.GetProperty("allocation").EnumerateArray());
        Assert.NotEmpty(worktree.GetProperty("safe_cleanup").EnumerateArray());
        Assert.NotEmpty(worktree.GetProperty("refuse_when").EnumerateArray());
        Assert.Contains("NOT a substitute", worktree.GetProperty("approval_policy_note").GetString()!, StringComparison.Ordinal);

        // The guide does not instruct a raw rm -rf of arbitrary paths anywhere.
        var safeCleanup = worktree.GetProperty("safe_cleanup").EnumerateArray().Select(s => s.GetString()!).ToArray();
        Assert.Contains(safeCleanup, s => s.Contains("git worktree remove", StringComparison.Ordinal));

        // A dedicated safety boundary reinforces the no-raw-rm rule.
        var boundaries = doc.RootElement.GetProperty("safety_boundaries").EnumerateArray().Select(b => b.GetString()!).ToArray();
        Assert.Contains(boundaries, b => b.Contains("never raw `rm -rf`", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_ReviewDelegation_RequiresManagedWorktreeRoot_ProhibitsRawTmpRm_G520()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Review delegation — managed worktrees and design alignment", output, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/worktrees/review-<unit>", output, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED as the normal path", output, StringComparison.Ordinal);
        Assert.Contains("/tmp/...review...", output, StringComparison.Ordinal);
        Assert.Contains("rm -rf /tmp/... && git worktree add ...", output, StringComparison.Ordinal);
        Assert.Contains("git worktree remove <managed-path>", output, StringComparison.Ordinal);
        Assert.Contains("REGISTERED, CLEAN worktree only", output, StringComparison.Ordinal);
        // Unsafe/stale paths become a structured blocker reply, never an operator approval prompt.
        Assert.Contains("STRUCTURED BLOCKER agmsg reply to", output, StringComparison.Ordinal);
        Assert.Contains("NEVER an operator `rm -rf` approval prompt", output, StringComparison.Ordinal);
        // Delegation example carries the policy + design-alignment requirement.
        Assert.Contains("\"managed_worktree_policy\":", output, StringComparison.Ordinal);
        Assert.Contains("\"design_alignment_required\":true", output, StringComparison.Ordinal);
        // Design-alignment sources checklist.
        Assert.Contains("review-context — the review-context artifact", output, StringComparison.Ordinal);
        Assert.Contains("intent tree — the relevant intent-tree entries", output, StringComparison.Ordinal);
        Assert.Contains("ADR / decision notes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasReviewDelegationContractShape_G520()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var review = doc.RootElement.GetProperty("review_delegation_contract");

        Assert.Contains(".intent-cli/worktrees", review.GetProperty("managed_worktree_root").GetString()!, StringComparison.Ordinal);
        Assert.Contains("PROHIBITED", review.GetProperty("prohibited_pattern").GetString()!, StringComparison.Ordinal);
        Assert.Contains("git worktree remove", review.GetProperty("cleanup_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("STRUCTURED BLOCKER", review.GetProperty("unsafe_stale_path_rule").GetString()!, StringComparison.Ordinal);

        var delegationExample = review.GetProperty("delegation_example").GetString()!;
        Assert.Contains("\"managed_worktree_policy\"", delegationExample, StringComparison.Ordinal);
        Assert.Contains("\"design_alignment_required\":true", delegationExample, StringComparison.Ordinal);

        Assert.Equal(5, review.GetProperty("design_alignment_sources").GetArrayLength());
    }

    [Fact]
    public void Execute_Markdown_ReviewThreadPrompt_RequiresManagedWorktreeAndDesignAlignment_G520()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // The review thread prompt (Thread prompts / review) carries both requirements inline.
        var reviewPromptIndex = output.IndexOf("### review", StringComparison.Ordinal);
        Assert.True(reviewPromptIndex >= 0, "review thread prompt section must be present.");
        var reviewSection = output.Substring(reviewPromptIndex);

        Assert.Contains(".intent-cli/worktrees/review-<unit>", reviewSection, StringComparison.Ordinal);
        Assert.Contains("NEVER a raw `/tmp/...review...` path", reviewSection, StringComparison.Ordinal);
        Assert.Contains("STRUCTURED BLOCKER reply to the orchestrator", reviewSection, StringComparison.Ordinal);
        Assert.Contains("design_alignment_checked: true", reviewSection, StringComparison.Ordinal);
        Assert.Contains("packet, review-context, intent tree, ADR/decision", reviewSection, StringComparison.Ordinal);
        Assert.Contains("INCOMPLETE review", reviewSection, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_AgmsgReplyContract_HasReviewCompletedExample_AndIncompleteRule_G520()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var contract = doc.RootElement.GetProperty("agmsg_reply_contract");

        var reviewCompleted = contract.GetProperty("review_completed_example").GetString()!;
        Assert.Contains("\"design_alignment_checked\":true", reviewCompleted, StringComparison.Ordinal);
        Assert.Contains("\"design_alignment_sources_checked\":", reviewCompleted, StringComparison.Ordinal);
        Assert.Contains("\"managed_worktree_policy\":", reviewCompleted, StringComparison.Ordinal);

        var incompleteRule = contract.GetProperty("review_incomplete_rule").GetString()!;
        Assert.Contains("INCOMPLETE", incompleteRule, StringComparison.Ordinal);
        Assert.Contains("authoritative PRIOR approval state", incompleteRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Troubleshooting_IncludesRmRfTmpReviewSymptom_G520()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("Codex asks to approve `rm -rf /tmp/...review...`", output, StringComparison.Ordinal);
        Assert.Contains("RIGHT safety behavior for the WRONG workflow", output, StringComparison.Ordinal);
        // The fix is the managed root, not weakening approval settings.
        Assert.Contains("NOT weakening approval settings", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_SetupIntake_NoInputs_IsMissingInputs_ListsOnlyMissingFields_G500()
    {
        var output = RunMarkdown([]);

        // The intake is the first section (operational outcome before reference material).
        var intakeIndex = output.IndexOf("## Setup intake", StringComparison.Ordinal);
        var modeIndex = output.IndexOf("## Mode separation", StringComparison.Ordinal);
        Assert.True(intakeIndex >= 0 && intakeIndex < modeIndex, "Setup intake must render before the reference material.");

        Assert.Contains("status: `missing-inputs`", output, StringComparison.Ordinal);
        // Lists the required fields among the eleven.
        Assert.Contains("### Missing inputs", output, StringComparison.Ordinal);
        Assert.Contains("- orchestrator folder", output, StringComparison.Ordinal);
        Assert.Contains("- implementer agent", output, StringComparison.Ordinal);
        Assert.Contains("- delivery mode", output, StringComparison.Ordinal);
        Assert.Contains("- existing-loop stop policy", output, StringComparison.Ordinal);
        // Receivers are never scheduled; the orchestrator is message-driven by default,
        // with an explicit fallback/legacy timer as the only case where it is scheduled.
        Assert.Contains("Receivers are NEVER scheduled", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Only the orchestrator is scheduled", output, StringComparison.Ordinal);
        Assert.DoesNotContain("only the orchestrator is scheduled", output, StringComparison.Ordinal);
        // No setup-intake agmsg registration block emitted while inputs are missing
        // (the setup-ready path adds this header; reference sections may mention
        // agmsg commands generically).
        Assert.DoesNotContain("### agmsg registration + delivery (copy-paste)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_SetupIntake_CompleteInputs_IsSetupReady_EmitsCommandsAndPrompts_G500()
    {
        var output = RunMarkdown([
            "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system",
            "--orchestrator-path", "/work/orch", "--implementation-path", "/work/impl", "--review-path", "/work/review",
            "--orchestrator-agent", "claude", "--implementer-agent", "claude", "--reviewer-agent", "codex",
            "--team", "intent-orch", "--delivery-mode", "streamed-inbox-watch", "--existing-loop-policy", "none",
        ]);

        Assert.Contains("status: `setup-ready`", output, StringComparison.Ordinal);
        // Copy-paste agmsg join/delivery commands per role, using supplied folders + agents + team + delivery.
        Assert.Contains("agmsg join.sh intent-orch orchestrator claude /work/orch", output, StringComparison.Ordinal);
        Assert.Contains("agmsg delivery.sh set streamed-inbox-watch claude /work/impl", output, StringComparison.Ordinal);
        Assert.Contains("agmsg join.sh intent-orch review codex /work/review", output, StringComparison.Ordinal);
        // First role prompts for all three roles.
        Assert.Contains("#### orchestrator", output, StringComparison.Ordinal);
        Assert.Contains("#### implementation", output, StringComparison.Ordinal);
        Assert.Contains("#### review", output, StringComparison.Ordinal);
        // First validation: read-only wake, receiver readiness (ping/ack), existing-loop conflict check.
        Assert.Contains("First read-only wake", output, StringComparison.Ordinal);
        Assert.Contains("Receiver readiness: ping each receiver and require an ack", output, StringComparison.Ordinal);
        Assert.Contains("Existing-loop conflict check", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_SetupIntake_KeptExistingLoop_IsBlocked_G500()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            [
                "--domain", "intent-cli", "--target-repo", "owner/repo",
                "--orchestrator-path", "/o", "--implementation-path", "/i", "--review-path", "/r",
                "--orchestrator-agent", "claude", "--implementer-agent", "claude", "--reviewer-agent", "codex",
                "--team", "t", "--delivery-mode", "watch", "--existing-loop-policy", "keep",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var intake = doc.RootElement.GetProperty("setup_intake");

        Assert.Equal("blocked", intake.GetProperty("status").GetString());
        Assert.Empty(intake.GetProperty("missing_fields").EnumerateArray());
        Assert.Contains("would race the orchestrator", intake.GetProperty("headline").GetString()!, StringComparison.Ordinal);
        // No commands emitted while blocked.
        Assert.False(intake.TryGetProperty("agmsg_commands", out _) && intake.GetProperty("agmsg_commands").ValueKind == JsonValueKind.Array && intake.GetProperty("agmsg_commands").GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_Json_SetupIntake_RoleAgentsDefaultToAgent_G500()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            [
                "--domain", "intent-cli", "--target-repo", "owner/repo",
                "--orchestrator-path", "/o", "--implementation-path", "/i", "--review-path", "/r",
                "--agent", "claude",
                "--team", "t", "--delivery-mode", "watch", "--existing-loop-policy", "none",
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var intake = doc.RootElement.GetProperty("setup_intake");

        // With only --agent supplied, all three role agents resolve to it → setup-ready.
        Assert.Equal("setup-ready", intake.GetProperty("status").GetString());
        var inputs = intake.GetProperty("inputs");
        Assert.Equal("claude", inputs.GetProperty("orchestrator_agent").GetString());
        Assert.Equal("claude", inputs.GetProperty("implementer_agent").GetString());
        Assert.Equal("claude", inputs.GetProperty("reviewer_agent").GetString());
    }

    [Fact]
    public void Execute_UnknownExistingLoopPolicy_ExitsOne_G500()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--existing-loop-policy", "maybe", "--format", "markdown"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown --existing-loop-policy", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ReceiverReadiness_RequiresPingAck_AndExplainsBoundaries_G501()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Receiver readiness", output, StringComparison.Ordinal);
        // Monitor config is not enough.
        Assert.Contains("Monitor configuration is NOT enough", output, StringComparison.Ordinal);
        // The five readiness states.
        Assert.Contains("- **registered**", output, StringComparison.Ordinal);
        Assert.Contains("- **delivery-configured**", output, StringComparison.Ordinal);
        Assert.Contains("- **watcher-alive**", output, StringComparison.Ordinal);
        Assert.Contains("- **receiver-session-active**", output, StringComparison.Ordinal);
        Assert.Contains("- **ping-acknowledged**", output, StringComparison.Ordinal);
        // Ping/ack required for each receiver before real delegation, re-done after restart.
        Assert.Contains("ping/ack required", output, StringComparison.Ordinal);
        Assert.Contains("Treat a missing ack as NOT-READY", output, StringComparison.Ordinal);
        Assert.Contains("after any receiver launch or restart", output, StringComparison.Ordinal);
        // Not-ready recovery via inbox.sh / resend.
        Assert.Contains("resend", output, StringComparison.Ordinal);
        Assert.Contains("`inbox.sh`", output, StringComparison.Ordinal);
        // watch.sh is debug/fallback and occupies a terminal.
        Assert.Contains("debug / fallback", output, StringComparison.Ordinal);
        Assert.Contains("OCCUPIES a terminal", output, StringComparison.Ordinal);
        // Codex Desktop app is not a monitor receiver by default.
        Assert.Contains("Codex Desktop app threads are NOT agmsg monitor receivers", output, StringComparison.Ordinal);
        // Diagnostic commands use agmsg scripts only.
        Assert.Contains("agmsg team.sh", output, StringComparison.Ordinal);
        Assert.Contains("agmsg delivery.sh status", output, StringComparison.Ordinal);
        Assert.Contains("agmsg inbox.sh", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_ReceiverReadiness_HasFiveStatesAndRecovery_G501()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var readiness = doc.RootElement.GetProperty("receiver_readiness");

        var states = readiness.GetProperty("states").EnumerateArray()
            .Select(s => s.GetProperty("state").GetString())
            .ToArray();
        Assert.Equal(
            new[] { "registered", "delivery-configured", "watcher-alive", "receiver-session-active", "ping-acknowledged" },
            states);

        Assert.True(readiness.TryGetProperty("ping_ack_required", out _));
        Assert.NotEmpty(readiness.GetProperty("not_ready_recovery").EnumerateArray());
        Assert.Contains("inbox.sh", readiness.GetProperty("codex_desktop_note").GetString()! + string.Join(" ", readiness.GetProperty("not_ready_recovery").EnumerateArray().Select(e => e.GetString())), StringComparison.Ordinal);

        // Diagnostic commands use only agmsg scripts (no raw gh / db edits).
        var diagnostics = readiness.GetProperty("diagnostic_commands").EnumerateArray().Select(d => d.GetString()!).ToArray();
        Assert.All(diagnostics, d => Assert.StartsWith("agmsg ", d, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_ReceiverStartupOrder_IsNumbered_AndWarnsSendBeforeReady_G502()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("### Startup order", output, StringComparison.Ordinal);
        // The numbered order: join → delivery → launch/restart → wait attach → ping → ack/inbox → delegate.
        Assert.Contains("1. Join the three roles to the team", output, StringComparison.Ordinal);
        Assert.Contains("2. Set the delivery mode", output, StringComparison.Ordinal);
        Assert.Contains("3. Launch or restart the receiver CLI sessions", output, StringComparison.Ordinal);
        Assert.Contains("4. Wait for the monitor/bridge to attach", output, StringComparison.Ordinal);
        Assert.Contains("5. Send a ping to each receiver only AFTER its session is active", output, StringComparison.Ordinal);
        Assert.Contains("6. Require an ack from each receiver", output, StringComparison.Ordinal);
        Assert.Contains("7. Only then send the first real delegation", output, StringComparison.Ordinal);
        // Send-before-ready failure mode + recovery.
        Assert.Contains("Send-before-ready:", output, StringComparison.Ordinal);
        Assert.Contains("stored in agmsg history but NOT visibly delivered", output, StringComparison.Ordinal);
        Assert.Contains("receiver-NOT-READY, not a successful delegation", output, StringComparison.Ordinal);
        Assert.Contains("`inbox.sh`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_ReceiverStartupOrder_HasSevenStepsAndWarning_G502()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var readiness = doc.RootElement.GetProperty("receiver_readiness");

        var order = readiness.GetProperty("startup_order").EnumerateArray().Select(s => s.GetString()!).ToArray();
        Assert.Equal(7, order.Length);
        // First step joins; last step delegates — ordering is meaningful.
        Assert.StartsWith("Join the three roles", order[0], StringComparison.Ordinal);
        Assert.Contains("real delegation", order[^1], StringComparison.Ordinal);

        var warning = readiness.GetProperty("send_before_ready_warning").GetString()!;
        Assert.Contains("not a successful delegation", warning, StringComparison.Ordinal);
        Assert.Contains("inbox.sh", warning, StringComparison.Ordinal);

        // A short copy-paste operator message for receivers launched after the
        // initial messages were sent (packet AC).
        var template = readiness.GetProperty("recovery_message_template").GetString()!;
        Assert.Contains("session started AFTER I sent earlier messages", template, StringComparison.Ordinal);
        Assert.Contains("inbox.sh", template, StringComparison.Ordinal);
        Assert.Contains("ack", template, StringComparison.Ordinal);
        Assert.Contains("receiver-not-ready", template, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_HasCopyPasteRecoveryMessage_ForLateLaunchedReceivers_G502()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("Copy-paste operator message when receivers were launched after the initial messages were sent", output, StringComparison.Ordinal);
        Assert.Contains("Heads up: your session started AFTER I sent earlier messages", output, StringComparison.Ordinal);
        Assert.Contains("reply `ack`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DesignReceiver_DescribesFourRoles_OptionalAndLoopless_G505()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Design / human receiver (optional)", output, StringComparison.Ordinal);
        // Optional for routine, recommended for escalation delivery.
        Assert.Contains("optional for routine operation: yes", output, StringComparison.Ordinal);
        Assert.Contains("RECOMMENDED", output, StringComparison.Ordinal);
        // Four logical roles, including the design/human receiver.
        Assert.Contains("### Four logical roles", output, StringComparison.Ordinal);
        Assert.Contains("orchestrator — paces the other roles over agmsg; message-driven by default", output, StringComparison.Ordinal);
        Assert.Contains("implementation receiver — LOOPLESS", output, StringComparison.Ordinal);
        Assert.Contains("review receiver — LOOPLESS", output, StringComparison.Ordinal);
        Assert.Contains("design / human receiver — OPTIONAL", output, StringComparison.Ordinal);
        // Paste-ready registration/addressing setup.
        Assert.Contains("### Design receiver setup", output, StringComparison.Ordinal);
        Assert.Contains("agmsg join.sh <team> design", output, StringComparison.Ordinal);
        // Minimal manual inbox trigger prompt (the packet's example wording).
        Assert.Contains("agmsg の inbox を確認してください。あなたは `<team>` の design です。", output, StringComparison.Ordinal);
        // Pre-start manual inbox inspection.
        Assert.Contains("Pre-start messages:", output, StringComparison.Ordinal);
        Assert.Contains("read its inbox with `inbox.sh`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_DesignReceiver_IsOptional_AndRoutineStaysInternal_G505()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var design = doc.RootElement.GetProperty("design_receiver");

        Assert.True(design.GetProperty("optional").GetBoolean());
        Assert.Equal(4, design.GetProperty("roles").GetArrayLength());
        Assert.True(design.TryGetProperty("manual_inbox_trigger_prompt", out _));
        Assert.True(design.TryGetProperty("pre_start_note", out _));

        // Routine progress stays internal; only human-needed reaches design.
        var summary = design.GetProperty("summary").GetString()!;
        Assert.Contains("Routine progress stays internal", summary, StringComparison.Ordinal);
        Assert.Contains("only human-needed decisions go to the design thread", summary, StringComparison.Ordinal);

        // The escalation filter (G498) keeps the design routing rule intact.
        var escalation = doc.RootElement.GetProperty("design_thread_escalation");
        Assert.NotEmpty(escalation.GetProperty("kept_internal").EnumerateArray());

        // No guidance tells implementation/review to start loops: prompts stay loopless.
        var prompts = doc.RootElement.GetProperty("threads").EnumerateArray()
            .ToDictionary(t => t.GetProperty("role").GetString()!, t => t.GetProperty("prompt").GetString()!);
        Assert.Contains("LOOPLESS receiver", prompts["implementation"], StringComparison.Ordinal);
        Assert.Contains("LOOPLESS receiver", prompts["review"], StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Preflight_RequiresAllThreeCwdChecks_G508()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Preflight (all three cwds)", output, StringComparison.Ordinal);
        // git status / dirty, expected repo, expected branch/base.
        Assert.Contains("`git status` is clean", output, StringComparison.Ordinal);
        Assert.Contains("git remote is the EXPECTED repo", output, StringComparison.Ordinal);
        Assert.Contains("expected branch/base", output, StringComparison.Ordinal);
        // Multi-domain filtering before publish/delegate.
        Assert.Contains("filter by the requested domain/target repo", output, StringComparison.Ordinal);
        // Existing timer-loop conflict check.
        Assert.Contains("no timer-loop", output, StringComparison.Ordinal);
        Assert.Contains("must not run together", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Troubleshooting_CoversTheFourFailureModes_G508()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("## Troubleshooting", output, StringComparison.Ordinal);
        Assert.Contains("**Message not received by a receiver**", output, StringComparison.Ordinal);
        Assert.Contains("**Monitor/delivery configured AFTER the session started**", output, StringComparison.Ordinal);
        Assert.Contains("**Codex Desktop app thread is the receiver**", output, StringComparison.Ordinal);
        Assert.Contains("**Receiver cwd sees a different repo/domain than delegated**", output, StringComparison.Ordinal);
        // Each routes through agmsg scripts / blocked-and-reroute, no raw label/db edits.
        Assert.Contains("`inbox.sh`", output, StringComparison.Ordinal);
        Assert.Contains("reply blocked and re-route", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasPreflightAndTroubleshootingShape_G508()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());

        var preflight = doc.RootElement.GetProperty("preflight");
        Assert.True(preflight.TryGetProperty("summary", out _));
        Assert.NotEmpty(preflight.GetProperty("checks").EnumerateArray());

        var troubleshooting = doc.RootElement.GetProperty("troubleshooting").EnumerateArray()
            .Select(t => t.GetProperty("symptom").GetString()!)
            .ToArray();
        Assert.Contains(troubleshooting, s => s.Contains("Message not received", StringComparison.Ordinal));
        Assert.Contains(troubleshooting, s => s.Contains("different repo/domain", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_SetupReady_FirstValidation_IncludesPreflightOfThreeCwds_G508()
    {
        var output = RunMarkdown([
            "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system",
            "--orchestrator-path", "/work/orch", "--implementation-path", "/work/impl", "--review-path", "/work/review",
            "--orchestrator-agent", "claude", "--implementer-agent", "claude", "--reviewer-agent", "codex",
            "--team", "intent-orch", "--delivery-mode", "streamed-inbox-watch", "--existing-loop-policy", "none",
        ]);

        Assert.Contains("status: `setup-ready`", output, StringComparison.Ordinal);
        // The first-validation references preflighting the three concrete cwds.
        Assert.Contains("Preflight all three cwds BEFORE mutating", output, StringComparison.Ordinal);
        Assert.Contains("/work/orch", output, StringComparison.Ordinal);
        Assert.Contains("/work/impl", output, StringComparison.Ordinal);
        Assert.Contains("/work/review", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DesignHandoff_HasFirstMessage_AutonomousPublish_AndBoundary_G509()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Design handoff (start / resume)", output, StringComparison.Ordinal);
        // First message pattern design → orchestrator with domain, target repo, requested action,
        // one-action-per-wake, escalation boundary.
        Assert.Contains("\"to\":\"orchestrator\",\"type\":\"start\"", output, StringComparison.Ordinal);
        Assert.Contains("\"domain\":\"intent-cli\"", output, StringComparison.Ordinal);
        Assert.Contains("\"target_repo\":\"J-Tech-Japan/intent-system\"", output, StringComparison.Ordinal);
        Assert.Contains("one action per wake", output, StringComparison.Ordinal);
        // Autonomous publish: orchestrator publishes one issue-cut-ready issue itself.
        Assert.Contains("**autonomous publish**", output, StringComparison.Ordinal);
        Assert.Contains("creates/publishes ONE GitHub issue ITSELF", output, StringComparison.Ordinal);
        Assert.Contains("does NOT ask design to do each step", output, StringComparison.Ordinal);
        // Human-decision escalation vs routine delegation.
        Assert.Contains("**escalation boundary**", output, StringComparison.Ordinal);
        Assert.Contains("Return to DESIGN only for human decisions", output, StringComparison.Ordinal);
        // Design-thread manual inbox workflow.
        Assert.Contains("**design inbox workflow**", output, StringComparison.Ordinal);
        Assert.Contains("`inbox.sh`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_MonitorRecovery_CoversTheFourRecoveryCases_G509()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("## Monitor recovery", output, StringComparison.Ordinal);
        Assert.Contains("**Monitor did not start**", output, StringComparison.Ordinal);
        Assert.Contains("**Message not visible**", output, StringComparison.Ordinal);
        Assert.Contains("**Receiver started after the message was sent**", output, StringComparison.Ordinal);
        Assert.Contains("**Orchestrator idle despite a packet existing**", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasDesignHandoffAndMonitorRecoveryShape_G509()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());

        var handoff = doc.RootElement.GetProperty("design_handoff");
        Assert.True(handoff.TryGetProperty("first_message_template", out _));
        Assert.Contains("issue-cut-ready", handoff.GetProperty("autonomous_publish_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("human decisions", handoff.GetProperty("escalation_boundary").GetString()!, StringComparison.Ordinal);
        Assert.Contains("inbox.sh", handoff.GetProperty("design_inbox_workflow").GetString()!, StringComparison.Ordinal);

        var recovery = doc.RootElement.GetProperty("monitor_recovery").EnumerateArray()
            .Select(r => r.GetProperty("symptom").GetString()!)
            .ToArray();
        Assert.Contains(recovery, s => s.Contains("Monitor did not start", StringComparison.Ordinal));
        Assert.Contains(recovery, s => s.Contains("Orchestrator idle despite a packet", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_Markdown_IntakeForm_HasQuestionsDefaultsAndActasMessages_G510()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Setup intake form", output, StringComparison.Ordinal);
        // Questions elicited/inferred include design cwd/type + manual-inbox-vs-monitored.
        Assert.Contains("### Ask for / infer", output, StringComparison.Ordinal);
        Assert.Contains("orchestrator cwd + agent type", output, StringComparison.Ordinal);
        Assert.Contains("design cwd + agent type, and whether design is manual-inbox or monitored", output, StringComparison.Ordinal);
        Assert.Contains("delivery mode per role", output, StringComparison.Ordinal);
        // Recommended defaults: orchestrator=Claude, implementer=Claude, reviewer=Codex, design=manual Codex, receivers=monitor.
        Assert.Contains("orchestrator = Claude", output, StringComparison.Ordinal);
        Assert.Contains("reviewer = Codex", output, StringComparison.Ordinal);
        Assert.Contains("design = manual-inbox Codex", output, StringComparison.Ordinal);
        Assert.Contains("receivers = monitor", output, StringComparison.Ordinal);
        // Role startup messages: Claude /agmsg actas vs Codex $agmsg actas.
        Assert.Contains("`/agmsg actas <role>`", output, StringComparison.Ordinal);
        Assert.Contains("`$agmsg actas <role>`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DesignTrafficController_HasPlaybookIdleDiagnosticAndContextOnly_G510()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("## Design traffic-controller playbook", output, StringComparison.Ordinal);
        Assert.Contains("TRAFFIC CONTROLLER, not an implementer", output, StringComparison.Ordinal);
        // Playbook: inbox, read-only state, nudge, no direct mutation, summarize only human-needed.
        Assert.Contains("Check the design inbox", output, StringComparison.Ordinal);
        Assert.Contains("READ-ONLY state", output, StringComparison.Ordinal);
        Assert.Contains("Do NOT directly mutate", output, StringComparison.Ordinal);
        Assert.Contains("Summarize ONLY human-needed", output, StringComparison.Ordinal);
        // Idle diagnostic before escalating.
        Assert.Contains("\"Orchestrator appears idle\" diagnostic", output, StringComparison.Ordinal);
        // Context-only rule for design -> receiver.
        Assert.Contains("Context-only:", output, StringComparison.Ordinal);
        Assert.Contains("mark it context-only", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasIntakeFormAndTrafficControllerShape_G510()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());

        var intake = doc.RootElement.GetProperty("intake_form");
        Assert.NotEmpty(intake.GetProperty("questions").EnumerateArray());
        Assert.NotEmpty(intake.GetProperty("defaults").EnumerateArray());
        var startupTypes = intake.GetProperty("role_startup_messages").EnumerateArray()
            .Select(s => s.GetProperty("agent_type").GetString())
            .ToArray();
        Assert.Contains("claude", startupTypes);
        Assert.Contains("codex", startupTypes);

        var controller = doc.RootElement.GetProperty("design_traffic_controller");
        Assert.NotEmpty(controller.GetProperty("playbook").EnumerateArray());
        Assert.NotEmpty(controller.GetProperty("idle_diagnostic").EnumerateArray());
        Assert.Contains("context-only", controller.GetProperty("context_only_rule").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DraftPrReviewability_DocumentsDomainGatedReviewViaCanonicalSurfaces_G510()
    {
        var markdown = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Draft PR reviewability", markdown, StringComparison.Ordinal);
        // A draft may still be reviewable depending on domain guidance.
        Assert.Contains("DRAFT PR may still be reviewable depending on domain guidance", markdown, StringComparison.Ordinal);
        // But review/merge/approval go through canonical intent-cli review surfaces.
        Assert.Contains("canonical", markdown, StringComparison.Ordinal);
        Assert.Contains("review closeout-plan", markdown, StringComparison.Ordinal);
        Assert.Contains("closeout pr", markdown, StringComparison.Ordinal);
        // No raw label / host-metadata editing.
        Assert.Contains("never approved/merged by hand or by raw label edits", markdown, StringComparison.Ordinal);

        using var writer = new StringWriter();
        GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);
        using var doc = JsonDocument.Parse(writer.ToString());
        var note = doc.RootElement.GetProperty("draft_pr_reviewability").GetString()!;
        Assert.Contains("reviewable depending on domain guidance", note, StringComparison.Ordinal);
        Assert.Contains("review closeout-plan", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_RoleBoundary_DesignAuthorsOrchestratorCoordinates_G513()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Role boundary (design authors; orchestrator coordinates)", output, StringComparison.Ordinal);
        // Design owns packet authoring; orchestrator must not become the author.
        Assert.Contains("DESIGN creates packets", output, StringComparison.Ordinal);
        Assert.Contains("must NOT silently become the product/release/design author", output, StringComparison.Ordinal);
        Assert.Contains("### Design owns", output, StringComparison.Ordinal);
        Assert.Contains("Packet content and acceptance criteria", output, StringComparison.Ordinal);
        // Orchestrator may publish exactly one already-authored issue-cut-ready packet per wake.
        Assert.Contains("### Orchestrator owns", output, StringComparison.Ordinal);
        Assert.Contains("Publish exactly ONE already-authored, `issue-cut-ready` packet per wake", output, StringComparison.Ordinal);
        // Missing-packet behavior: request to design + wait, do not author.
        Assert.Contains("**missing packet**", output, StringComparison.Ordinal);
        Assert.Contains("does NOT invent the packet", output, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"packet-needed\"", output, StringComparison.Ordinal);
        // Release-prep is design-owned.
        Assert.Contains("**release-prep**", output, StringComparison.Ordinal);
        Assert.Contains("Release-prep is design-owned", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasRoleBoundaryShape_G513()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var boundary = doc.RootElement.GetProperty("role_boundary");

        Assert.NotEmpty(boundary.GetProperty("design_owns").EnumerateArray());
        Assert.NotEmpty(boundary.GetProperty("orchestrator_owns").EnumerateArray());
        Assert.Contains("packet-needed", boundary.GetProperty("missing_packet_message_template").GetString()!, StringComparison.Ordinal);
        Assert.Contains("design-owned", boundary.GetProperty("release_prep_rule").GetString()!, StringComparison.Ordinal);
        Assert.Contains("does NOT invent the packet", boundary.GetProperty("missing_packet_response").GetString()!, StringComparison.Ordinal);

        // Existing orchestrator publish ability is preserved (not removed).
        var publication = doc.RootElement.GetProperty("next_slice_publication");
        Assert.True(publication.GetProperty("one_per_wake").GetBoolean());
    }

    [Fact]
    public void Execute_UnknownMode_ExitsOne()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--mode", "all-domains", "--format", "markdown"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown --mode", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOne()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--nope"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Help_ExplainsPrimaryAndNonReplacing()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide orchestrator-thread", output, StringComparison.Ordinal);
        Assert.Contains("PRIMARY", output, StringComparison.Ordinal);
        Assert.DoesNotContain("OPTIONAL agmsg-backed", output, StringComparison.Ordinal);
        Assert.Contains("not replaced", output, StringComparison.Ordinal);
    }

    // ----- G524: orchestrator wake contract (finish pending transitions in-wake) -----

    [Fact]
    public void Execute_Markdown_PublishAndDelegate_HappenInSameWake_NoDeferToNextWakeWording()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: guide output states publish + delegate happen in the same wake.
        Assert.Contains("THE SAME WAKE", output, StringComparison.Ordinal);
        Assert.Contains("delegate that same issue to implementation", output, StringComparison.Ordinal);

        // AC: no longer contains the defer-delegation-to-next-wake instruction.
        Assert.DoesNotContain("delegation intentionally deferred to the next wake", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deferred to the next wake", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Markdown_MessageCap_IsPerReceiverDelegation_NotAtMostOneMessage()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: the message cap reads "at most one delegation per receiver per wake"
        // and explicitly permits publish + delegation + reports within one wake.
        Assert.Contains("AT MOST ONE DELEGATION PER RECEIVER", output, StringComparison.Ordinal);
        Assert.Contains("NOT at-most-one-message", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Send AT MOST ONE message this wake", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Decide the single action for this wake", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_EndOfWakeCheck_RequiresStalledWorkAndNeverDefers()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: guide output contains the end-of-wake stalled-work check (G523)
        // and the never-end-with-unprocessed-actionable-transitions rule.
        Assert.Contains("## End-of-wake check (G523/G524)", output, StringComparison.Ordinal);
        Assert.Contains("automation stalled-work --domain", output, StringComparison.Ordinal);
        Assert.Contains("never defer", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("escalate", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_Json_EndOfWakeCheck_HasCommandAndRules()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var check = doc.RootElement.GetProperty("end_of_wake_check");
        Assert.Contains("stalled-work", check.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("automation stalled-work", check.GetProperty("command").GetString(), StringComparison.Ordinal);
        Assert.True(check.GetProperty("never_defer_rule").GetString()!.Length > 0);
        Assert.True(check.GetProperty("escalate_instead_of_defer_rule").GetString()!.Length > 0);
    }

    [Fact]
    public void Execute_Markdown_ReceiverPrompts_RequireCompletionOrBlockedReportAsFinalStep()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: implementation and review thread prompts contain the required
        // completion-or-blocked report step with its expected shape.
        Assert.Contains("REQUIRED FINAL STEP of EVERY delegation", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\",\"thread\":\"implementation\"", output, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"completed\",\"thread\":\"review\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_ReceiverPrompts_ContainRequiredReportStep()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var threads = doc.RootElement.GetProperty("threads").EnumerateArray().ToArray();
        var implementation = threads.First(t => t.GetProperty("role").GetString() == "implementation").GetProperty("prompt").GetString()!;
        var review = threads.First(t => t.GetProperty("role").GetString() == "review").GetProperty("prompt").GetString()!;

        Assert.Contains("REQUIRED FINAL STEP", implementation, StringComparison.Ordinal);
        Assert.Contains("REQUIRED FINAL STEP", review, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DispatchVerification_RequiresRosterCheckBeforeSend()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: dispatch guidance requires team-roster verification of the
        // recipient id before sending.
        Assert.Contains("## Dispatch verification (G524)", output, StringComparison.Ordinal);
        Assert.Contains("team roster", output, StringComparison.Ordinal);
        Assert.Contains("team.sh", output, StringComparison.Ordinal);
        Assert.Contains("`review`", output, StringComparison.Ordinal);
        Assert.Contains("`reviewer`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_DispatchVerification_HasRuleAndDeadAddressExample()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var verification = doc.RootElement.GetProperty("dispatch_verification");
        Assert.Contains("team.sh", verification.GetProperty("rule").GetString(), StringComparison.Ordinal);
        Assert.True(verification.GetProperty("dead_address_example").GetString()!.Length > 0);
    }

    [Fact]
    public void Execute_Markdown_TerminalWorkspaceProvisioning_HasAllSixElements_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: the section exists with all six elements in paste-ready form.
        Assert.Contains("## Terminal-workspace provisioning (G549)", output, StringComparison.Ordinal);
        Assert.Contains("### Placeholders", output, StringComparison.Ordinal);
        Assert.Contains("### 1. Role folders (create them when absent)", output, StringComparison.Ordinal);
        Assert.Contains("### 2. Workspace topology", output, StringComparison.Ordinal);
        Assert.Contains("### 3. Launch rules (and why)", output, StringComparison.Ordinal);
        Assert.Contains("### 4. Role initialization (actas and readiness)", output, StringComparison.Ordinal);
        Assert.Contains("### 5. Role exclusivity and handover", output, StringComparison.Ordinal);
        Assert.Contains("### 6. Reference workspace manager — herdr", output, StringComparison.Ordinal);
        Assert.Contains("### Provisioning checklist (paste-ready)", output, StringComparison.Ordinal);
        // Placeholders the design thread fills in from its own context.
        Assert.Contains("`<Project>`", output, StringComparison.Ordinal);
        Assert.Contains("`<owner/host-repo>`", output, StringComparison.Ordinal);
        Assert.Contains("`<workspace-root>`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_FolderCommands_SplitHostSideFromImplementation_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        // AC: host-side roles clone the HOST metadata repo; the implementation
        // role clones the TARGET repo — with runnable commands for each.
        Assert.Contains("git clone https://github.com/<owner/host-repo>.git <workspace-root>/<Project>Orchestrator", output, StringComparison.Ordinal);
        Assert.Contains("git clone https://github.com/<owner/host-repo>.git <workspace-root>/<Project>Review", output, StringComparison.Ordinal);
        Assert.Contains("git clone https://github.com/J-Tech-Japan/intent-system.git <workspace-root>/<Project>Implementation", output, StringComparison.Ordinal);
        // AC: the never-share rule appears WITH its (project, type)-scoping reason.
        Assert.Contains("NEVER share a folder between two roles", output, StringComparison.Ordinal);
        Assert.Contains("(project, type)-scoped", output, StringComparison.Ordinal);
        Assert.Contains("G521", output, StringComparison.Ordinal);
        // An absent folder is created, not worked around.
        Assert.Contains("When a folder is absent, CREATE it", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_TopologyAndLaunchRules_WarnAgainstDirectSpawn_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--team", "orch-team"]);

        // AC: one workspace / one team-named tab / one pane per role, design outside.
        Assert.Contains("One WORKSPACE per agmsg team", output, StringComparison.Ordinal);
        Assert.Contains("One TAB named after the team (`orch-team`)", output, StringComparison.Ordinal);
        Assert.Contains("One PANE per role", output, StringComparison.Ordinal);
        Assert.Contains("The DESIGN thread stays OUTSIDE the workspace", output, StringComparison.Ordinal);
        // AC: the shim-safe launch rule appears with its reason, and direct
        // executable spawn is explicitly warned against.
        Assert.Contains("typing into the pane's interactive shell", output, StringComparison.Ordinal);
        Assert.Contains("`codex()` shell shim", output, StringComparison.Ordinal);
        Assert.Contains("exec's the canonical `codex` executable directly BYPASSES the shim", output, StringComparison.Ordinal);
        // Operator-chosen permission mode for claude; attended first-run screens.
        Assert.Contains("permission mode the OPERATOR chose", output, StringComparison.Ordinal);
        Assert.Contains("DURABLE allowlist/trust record", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_ActasReadinessAndHandover_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // AC: both actas forms, the readiness wait, and the ping-test reference.
        Assert.Contains("`/agmsg actas <role>`", output, StringComparison.Ordinal);
        Assert.Contains("`$agmsg actas <role>`", output, StringComparison.Ordinal);
        Assert.Contains("**readiness wait**", output, StringComparison.Ordinal);
        Assert.Contains("run the existing ping test before ANY delegation", output, StringComparison.Ordinal);
        // AC: exclusivity + graceful drop with operator confirmation before the successor claims.
        Assert.Contains("Exactly ONE live session may hold a role at a time", output, StringComparison.Ordinal);
        Assert.Contains("GRACEFUL DROP", output, StringComparison.Ordinal);
        Assert.Contains("OPERATOR CONFIRMATION", output, StringComparison.Ordinal);
        Assert.Contains("only AFTER the drop is confirmed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_SeparatesDeliveryConfigFromLiveAttachment_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // G549 repair: a delivery mode proves CONFIGURATION, never live
        // attachment — the two layers must stay separate in the guidance.
        Assert.Contains("#### Readiness layers (do not collapse them)", output, StringComparison.Ordinal);
        Assert.Contains("**1. delivery configuration**", output, StringComparison.Ordinal);
        Assert.Contains("It does NOT prove a watcher is alive", output, StringComparison.Ordinal);
        Assert.Contains("Never treat a delivery mode as readiness", output, StringComparison.Ordinal);

        // Live-attachment evidence is agent-specific: Claude Monitor markers…
        Assert.Contains("**2. live attachment (claude)**", output, StringComparison.Ordinal);
        Assert.Contains("`Monitor(agmsg inbox stream)`", output, StringComparison.Ordinal);
        Assert.Contains("`1 monitor`", output, StringComparison.Ordinal);
        Assert.Contains("NOT `1 shell`", output, StringComparison.Ordinal);
        // …vs the codex bridge-alive marker.
        Assert.Contains("**2. live attachment (codex)**", output, StringComparison.Ordinal);
        Assert.Contains("`Codex bridge: <team>/<role> alive (pid N)`", output, StringComparison.Ordinal);

        // Ping/ack stays the SOLE end-to-end proof.
        Assert.Contains("**3. end-to-end**", output, StringComparison.Ordinal);
        Assert.Contains("PING/ACK is the ONLY end-to-end proof", output, StringComparison.Ordinal);
        Assert.Contains("never a substitute", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_AuthorityBoundary_EscalatesUnauthorizedPrompts_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // G549 repair: attending a pane is not authority to decide for the
        // operator — read-first, explicit-authorization-only, escalate the rest.
        Assert.Contains("> **Authority boundary:**", output, StringComparison.Ordinal);
        Assert.Contains("ONLY on pane contents it has actually READ", output, StringComparison.Ordinal);
        Assert.Contains("Unsticking a pane is not deciding for the operator", output, StringComparison.Ordinal);

        // Round-2 repair: authorization reaches read-pane trust/allowlist cases
        // ONLY — credential/security/permission prompts are absolutely never
        // answerable by design, with or without prior authorization.
        Assert.Contains("ONLY to read-pane TRUST/ALLOWLIST", output, StringComparison.Ordinal);
        Assert.Contains("own hook-trust case, which it may accept for itself", output, StringComparison.Ordinal);
        Assert.Contains("CREDENTIAL, SECURITY, and PERMISSION prompts are NEVER answerable", output, StringComparison.Ordinal);
        Assert.Contains("ALWAYS remain unanswered and are ALWAYS ESCALATED to the operator", output, StringComparison.Ordinal);
        Assert.Contains("with or without prior authorization", output, StringComparison.Ordinal);
        Assert.Contains("no authorization makes them answerable", output, StringComparison.Ordinal);
        // The old conditional framing ("outside that authorization") is gone.
        Assert.DoesNotContain("outside that explicit authorization", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_LaunchUiState_DoesNotEraseDeliveryConfiguration_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // Round-2 repair: a trust screen means NOT live-attached / NOT
        // session-active — it says nothing about delivery configuration, which
        // is set before launch. The layers must stay separate in both
        // directions.
        Assert.Contains("NOT live-attached and NOT session-active", output, StringComparison.Ordinal);
        Assert.Contains("Launch-UI state never erases configuration, and configuration never implies attachment", output, StringComparison.Ordinal);
        Assert.DoesNotContain("is not even configured yet", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Provisioning_NamesHerdrSurfaces_AndLinksOutInternals_G549()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // AC: herdr is named as the REFERENCE manager with its surfaces listed…
        Assert.Contains("`workspace create`", output, StringComparison.Ordinal);
        Assert.Contains("`pane split`", output, StringComparison.Ordinal);
        Assert.Contains("`pane send-text` / `send-keys`", output, StringComparison.Ordinal);
        Assert.Contains("`agent prompt`", output, StringComparison.Ordinal);
        Assert.Contains("`agent wait`", output, StringComparison.Ordinal);
        // …internals linked out, not restated, and any equivalent manager allowed.
        Assert.Contains("intent-cli does not own, ship, or wrap herdr", output, StringComparison.Ordinal);
        Assert.Contains("consult herdr's own", output, StringComparison.Ordinal);
        Assert.Contains("ANY equivalent workspace manager may be substituted", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_TerminalWorkspaceProvisioning_HasStructuredShape_G549()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var provisioning = doc.RootElement.GetProperty("terminal_workspace_provisioning");

        Assert.NotEmpty(provisioning.GetProperty("placeholders").EnumerateArray());
        Assert.NotEmpty(provisioning.GetProperty("checklist").EnumerateArray());

        var folders = provisioning.GetProperty("folder_provisioning");
        Assert.Contains("(project, type)-scoped", folders.GetProperty("never_share_rule").GetString(), StringComparison.Ordinal);

        var roles = folders.GetProperty("roles").EnumerateArray()
            .Select(r => (Role: r.GetProperty("role").GetString()!, Command: r.GetProperty("create_command").GetString()!))
            .ToArray();
        Assert.Equal(3, roles.Length);
        Assert.Contains(roles, r => r.Role == "orchestrator" && r.Command.Contains("<owner/host-repo>", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.Role == "review" && r.Command.Contains("<owner/host-repo>", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.Role == "implementation" && r.Command.Contains("owner/repo", StringComparison.Ordinal));

        var launchRules = provisioning.GetProperty("launch_rules");
        Assert.Contains("shim", launchRules.GetProperty("codex_shim_rule").GetString(), StringComparison.Ordinal);

        // G549 repair: the authority boundary is a first-class field, not prose
        // folded into the attended-first-run rule.
        var authorityBoundary = launchRules.GetProperty("authority_boundary").GetString()!;
        Assert.Contains("READ", authorityBoundary, StringComparison.Ordinal);
        Assert.Contains("read-pane TRUST/ALLOWLIST", authorityBoundary, StringComparison.Ordinal);
        // Round-2 repair: the escalation rule is absolute in JSON too.
        Assert.Contains("NEVER answerable", authorityBoundary, StringComparison.Ordinal);
        Assert.Contains("ALWAYS ESCALATED to the operator", authorityBoundary, StringComparison.Ordinal);
        Assert.Contains("no authorization makes them answerable", authorityBoundary, StringComparison.Ordinal);

        var roleInitialization = provisioning.GetProperty("role_initialization");
        Assert.NotEmpty(roleInitialization.GetProperty("actas_forms").EnumerateArray());

        // G549 repair: configuration proof, agent-specific live attachment, and
        // the end-to-end ack are three separate JSON fields.
        Assert.Contains("does NOT prove", roleInitialization.GetProperty("configuration_proof").GetString(), StringComparison.Ordinal);
        Assert.Contains("ONLY end-to-end proof", roleInitialization.GetProperty("end_to_end_proof").GetString(), StringComparison.Ordinal);

        // Round-2 repair: a trust screen is a live-attachment/session-active
        // fact, not a configuration fact.
        var readinessWait = roleInitialization.GetProperty("readiness_wait").GetString()!;
        Assert.Contains("NOT live-attached and NOT session-active", readinessWait, StringComparison.Ordinal);
        Assert.DoesNotContain("not even configured", readinessWait, StringComparison.Ordinal);

        var liveEvidence = roleInitialization.GetProperty("live_attachment_evidence").EnumerateArray()
            .Select(e => (AgentType: e.GetProperty("agent_type").GetString()!, Evidence: e.GetProperty("evidence").GetString()!))
            .ToArray();
        Assert.Contains(liveEvidence, e => e.AgentType == "claude" && e.Evidence.Contains("1 monitor", StringComparison.Ordinal));
        Assert.Contains(liveEvidence, e => e.AgentType == "codex" && e.Evidence.Contains("Codex bridge", StringComparison.Ordinal));
        Assert.True(provisioning.GetProperty("exclusivity_handover").GetProperty("operator_confirmation_rule").GetString()!.Length > 0);

        var referenceManager = provisioning.GetProperty("reference_manager");
        Assert.Equal("herdr", referenceManager.GetProperty("name").GetString());
        Assert.NotEmpty(referenceManager.GetProperty("surfaces").EnumerateArray());
        Assert.True(referenceManager.GetProperty("substitution_rule").GetString()!.Length > 0);
    }

    [Fact]
    public void Execute_Markdown_Supervision_GrantedAuthorityIsSessionLayerOnly_G550()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("## Design-thread workspace supervision (G550)", output, StringComparison.Ordinal);
        Assert.Contains("### Granted authority — session layer only", output, StringComparison.Ordinal);
        // AC: the grant is the operator's, and it is not assumed.
        Assert.Contains("**authority is granted, not assumed**", output, StringComparison.Ordinal);
        Assert.Contains("because the operator asked it to", output, StringComparison.Ordinal);
        // AC: workflow-state ownership is explicitly UNCHANGED — this slice
        // moves no workflow authority.
        Assert.Contains("> **Workflow state ownership:**", output, StringComparison.Ordinal);
        Assert.Contains("workflow state ownership does not move", output, StringComparison.Ordinal);
        Assert.Contains("remain with intent-cli, GitHub, and the orchestrator", output, StringComparison.Ordinal);
        Assert.Contains("Supervising a session never authorizes a workflow transition", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Supervision_SessionLifecycle_ExclusivityAndGracefulDrop_G550()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Session lifecycle (investigate, then replace gracefully)", output, StringComparison.Ordinal);
        // Investigate before replacing — read the pane first.
        Assert.Contains("READ the pane first", output, StringComparison.Ordinal);
        Assert.Contains("replacement is the last step, not the first", output, StringComparison.Ordinal);
        // AC: exclusivity + graceful drop + operator-visible confirmation.
        Assert.Contains("never means two sessions holding the same role", output, StringComparison.Ordinal);
        Assert.Contains("Replace through the GRACEFUL DROP", output, StringComparison.Ordinal);
        Assert.Contains("**operator-visible confirmation**", output, StringComparison.Ordinal);
        Assert.Contains("the decision to retire a live session remains the operator's", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Supervision_ThreeLayersWithCadences_AndRearmRule_G550()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("### Three supervision layers", output, StringComparison.Ordinal);
        // AC: three layers, each with a purpose and a cadence.
        Assert.Contains("**real-time message monitor**", output, StringComparison.Ordinal);
        Assert.Contains("continuous / real-time", output, StringComparison.Ordinal);
        Assert.Contains("**blocking-UI pane scan**", output, StringComparison.Ordinal);
        Assert.Contains("sub-minute class", output, StringComparison.Ordinal);
        Assert.Contains("**periodic state watchdog**", output, StringComparison.Ordinal);
        Assert.Contains("tens-of-minutes class", output, StringComparison.Ordinal);
        // The watchdog layer resolves to the existing heartbeat command.
        Assert.Contains("intent-cli automation heartbeat --domain intent-cli --repo J-Tech-Japan/intent-system", output, StringComparison.Ordinal);
        // AC: the re-arm rule with the measured cost of forgetting it.
        Assert.Contains("> **Re-arm across restarts:**", output, StringComparison.Ordinal);
        Assert.Contains("RE-ARMED as the first act of the new session", output, StringComparison.Ordinal);
        Assert.Contains("5.5 HOURS", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Supervision_DialogLists_AreClosedSets_WithVerifiedRead_G550()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // AC: the verified-read rule gates every answer.
        Assert.Contains("> **Verified read before answer:**", output, StringComparison.Ordinal);
        Assert.Contains("ONLY after it has actually read that dialog's content from the pane", output, StringComparison.Ordinal);
        Assert.Contains("blind keystroke into a dialog it has not rendered is prohibited", output, StringComparison.Ordinal);

        // AC: the MAY list is exactly four items, each with its verification.
        Assert.Contains("#### MAY answer (only after the verified read)", output, StringComparison.Ordinal);
        Assert.Contains("**confirmations of work the design thread itself requested** — verify:", output, StringComparison.Ordinal);
        Assert.Contains("**command approvals verified read-only** — verify:", output, StringComparison.Ordinal);
        Assert.Contains("**trust screens for hooks the design thread itself installed** — verify:", output, StringComparison.Ordinal);
        Assert.Contains("**operator-preauthorized mode changes** — verify:", output, StringComparison.Ordinal);

        // AC: the MUST-ESCALATE list is the four categories.
        Assert.Contains("#### MUST escalate to the operator", output, StringComparison.Ordinal);
        Assert.Contains("**unreadable or unverifiable dialogs**", output, StringComparison.Ordinal);
        Assert.Contains("**destructive or irreversible approvals**", output, StringComparison.Ordinal);
        Assert.Contains("**choices that embed a product or design decision**", output, StringComparison.Ordinal);
        Assert.Contains("**credential, security, and permission waits**", output, StringComparison.Ordinal);
        // Credential/security/permission stays absolute, matching G549's boundary.
        Assert.Contains("NEVER answerable by the design thread, with or without prior authorization", output, StringComparison.Ordinal);

        // AC: the boundary sentence.
        Assert.Contains("UNSTICKING A SESSION IS NOT DECIDING FOR IT", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_Supervision_CrossReferencesResolve_G550()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        // AC: the cross-references point at sections that actually exist in the
        // same rendered guide — provisioning (G549) and the watchdog safety rules.
        Assert.Contains("see `Terminal-workspace provisioning`", output, StringComparison.Ordinal);
        Assert.Contains("## Terminal-workspace provisioning (G549)", output, StringComparison.Ordinal);
        Assert.Contains("See `Design-thread watchdog (recommended safety net)`", output, StringComparison.Ordinal);
        Assert.Contains("## Design-thread watchdog (recommended safety net)", output, StringComparison.Ordinal);
        // The safety rules are restated as applying verbatim to supervision.
        Assert.Contains("no duplicate delegation, no clearing a permission prompt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_DesignWorkspaceSupervision_HasStructuredShape_G550()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var supervision = doc.RootElement.GetProperty("design_workspace_supervision");

        var authority = supervision.GetProperty("granted_authority");
        Assert.NotEmpty(authority.GetProperty("design_operates_session_layer").EnumerateArray());
        Assert.Contains("does not move", authority.GetProperty("workflow_state_ownership_unchanged").GetString(), StringComparison.Ordinal);

        var lifecycle = supervision.GetProperty("session_lifecycle");
        Assert.NotEmpty(lifecycle.GetProperty("unresponsive_session_investigation").EnumerateArray());
        Assert.Contains("GRACEFUL DROP", lifecycle.GetProperty("graceful_drop_rule").GetString(), StringComparison.Ordinal);
        Assert.True(lifecycle.GetProperty("operator_visible_confirmation").GetString()!.Length > 0);

        // Exactly three layers, each carrying a purpose and a cadence.
        var layers = supervision.GetProperty("supervision_layers").EnumerateArray()
            .Select(l => (Layer: l.GetProperty("layer").GetString()!, Cadence: l.GetProperty("cadence").GetString()!, Purpose: l.GetProperty("purpose").GetString()!))
            .ToArray();
        Assert.Equal(3, layers.Length);
        Assert.All(layers, l => Assert.NotEmpty(l.Purpose));
        Assert.All(layers, l => Assert.NotEmpty(l.Cadence));
        Assert.Contains(layers, l => l.Layer == "real-time message monitor");
        Assert.Contains(layers, l => l.Layer == "blocking-UI pane scan" && l.Cadence.Contains("sub-minute", StringComparison.Ordinal));
        Assert.Contains(layers, l => l.Layer == "periodic state watchdog" && l.Cadence.Contains("tens-of-minutes", StringComparison.Ordinal));

        Assert.Contains("5.5 HOURS", supervision.GetProperty("rearm_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("actually read", supervision.GetProperty("verified_read_rule").GetString(), StringComparison.Ordinal);

        // Both dialog lists are closed four-item sets; every MAY entry carries
        // its verification condition.
        var mayAnswer = supervision.GetProperty("may_answer").EnumerateArray()
            .Select(m => (Dialog: m.GetProperty("dialog").GetString()!, Verification: m.GetProperty("verification").GetString()!))
            .ToArray();
        Assert.Equal(4, mayAnswer.Length);
        Assert.All(mayAnswer, m => Assert.NotEmpty(m.Verification));
        Assert.Contains(mayAnswer, m => m.Dialog.Contains("itself requested", StringComparison.Ordinal));
        Assert.Contains(mayAnswer, m => m.Dialog.Contains("verified read-only", StringComparison.Ordinal));
        Assert.Contains(mayAnswer, m => m.Dialog.Contains("hooks the design thread itself installed", StringComparison.Ordinal));
        Assert.Contains(mayAnswer, m => m.Dialog.Contains("operator-preauthorized mode changes", StringComparison.Ordinal));

        var mustEscalate = supervision.GetProperty("must_escalate").EnumerateArray()
            .Select(m => (Category: m.GetProperty("category").GetString()!, Reason: m.GetProperty("reason").GetString()!))
            .ToArray();
        Assert.Equal(4, mustEscalate.Length);
        Assert.All(mustEscalate, m => Assert.NotEmpty(m.Reason));
        Assert.Contains(mustEscalate, m => m.Category.Contains("unreadable", StringComparison.Ordinal));
        Assert.Contains(mustEscalate, m => m.Category.Contains("destructive or irreversible", StringComparison.Ordinal));
        Assert.Contains(mustEscalate, m => m.Category.Contains("product or design decision", StringComparison.Ordinal));
        Assert.Contains(mustEscalate, m => m.Category.Contains("credential, security, and permission waits", StringComparison.Ordinal));

        Assert.Contains("UNSTICKING A SESSION IS NOT DECIDING FOR IT", supervision.GetProperty("boundary_sentence").GetString(), StringComparison.Ordinal);
        Assert.Contains("Terminal-workspace provisioning", supervision.GetProperty("provisioning_reference").GetString(), StringComparison.Ordinal);
        Assert.Contains("Design-thread watchdog", supervision.GetProperty("watchdog_safety_rules_reference").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ClarificationBackedHold_MakesAgmsgOnlyHoldsAContractViolation_G552()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("## Design-decision holds and bounded authority (G552)", output, StringComparison.Ordinal);
        Assert.Contains("### Clarification-backed holds", output, StringComparison.Ordinal);
        // AC: the hold is recorded through the canonical clarify surface, with
        // the four fields.
        Assert.Contains("RECORDS A CLARIFICATION ARTIFACT through the canonical clarify surface", output, StringComparison.Ordinal);
        Assert.Contains("blocking execution unit", output, StringComparison.Ordinal);
        Assert.Contains("recommended answer", output, StringComparison.Ordinal);
        Assert.Contains("`intent-cli clarify open`", output, StringComparison.Ordinal);
        Assert.Contains("`intent-cli clarify answer`", output, StringComparison.Ordinal);
        // AC: the contract-violation sentence.
        Assert.Contains("> **Contract violation:**", output, StringComparison.Ordinal);
        Assert.Contains("An agmsg-only hold is a CONTRACT VIOLATION", output, StringComparison.Ordinal);
        Assert.Contains("if the artifact does not exist, you are not waiting, you are stalled", output, StringComparison.Ordinal);

        // G552 repair: a paste-ready invocation that actually persists the real
        // question and its recommendation/evidence in the OPEN artifact —
        // agmsg may notify, but it can never substitute for the artifact.
        Assert.Contains("Paste-ready — the OPEN artifact carries the real content", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli clarify open <execution-unit>", output, StringComparison.Ordinal);
        Assert.Contains("--question ", output, StringComparison.Ordinal);
        Assert.Contains("--recommended-answer ", output, StringComparison.Ordinal);
        Assert.Contains("--evidence ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ReviewerHoldRule_NeverAnUntrackedWait_G552()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Reviewer hold rule (refined)", output, StringComparison.Ordinal);
        // Green technical + fact-checkable non-semantic -> resolve under authority.
        Assert.Contains("Technical checks are GREEN and the only pending item is NON-SEMANTIC and MECHANICALLY FACT-CHECKABLE", output, StringComparison.Ordinal);
        // Otherwise -> recorded clarification and a visible pending state.
        Assert.Contains("becomes a recorded clarification and a VISIBLE pending state", output, StringComparison.Ordinal);
        // Never a third option.
        Assert.Contains("> **Never an untracked wait:**", output, StringComparison.Ordinal);
        Assert.Contains("there is no third option", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_BoundedDefaultAuthority_IsGrantedEnumeratedLoggedAmendableAndNonSemantic_G552()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Bounded default authority", output, StringComparison.Ordinal);
        // AC element 1: operator grant requirement.
        Assert.Contains("GRANTED, never assumed", output, StringComparison.Ordinal);
        Assert.Contains("classes the OPERATOR has explicitly pre-delegated", output, StringComparison.Ordinal);
        // AC element 2: the enumeration is the whole MAY scope, each with its facts.
        Assert.Contains("#### Enumerated fact-checkable classes (the whole MAY scope)", output, StringComparison.Ordinal);
        Assert.Contains("**count and enumeration corrections** — verify:", output, StringComparison.Ordinal);
        Assert.Contains("**wording corrections that follow from a cited fact** — verify:", output, StringComparison.Ordinal);
        Assert.Contains("**cross-reference and link corrections** — verify:", output, StringComparison.Ordinal);
        Assert.Contains("**identifier and metadata mismatches against a canonical source** — verify:", output, StringComparison.Ordinal);
        // AC element 3: mandatory evidence logging.
        Assert.Contains("MANDATORY EVIDENCE LOGGING", output, StringComparison.Ordinal);
        Assert.Contains("An unlogged resolution is not a granted-authority resolution", output, StringComparison.Ordinal);
        // AC element 4: post-hoc amendment right.
        Assert.Contains("DESIGN MAY AMEND POST HOC", output, StringComparison.Ordinal);
        Assert.Contains("buys latency, not finality", output, StringComparison.Ordinal);
        // G552 repair: the evidence log has a CONCRETE durable sink and a
        // paste-ready operation, not just prose.
        Assert.Contains("**evidence sink**", output, StringComparison.Ordinal);
        Assert.Contains("CANONICAL `clarify record` surface", output, StringComparison.Ordinal);
        Assert.Contains("`## Recently Resolved`", output, StringComparison.Ordinal);
        Assert.Contains("intents/<domain>/clarifications/open.md", output, StringComparison.Ordinal);
        Assert.Contains("Paste-ready evidence operation:", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli clarify record --domain <domain> --from-file", output, StringComparison.Ordinal);
        Assert.Contains("## Rationale", output, StringComparison.Ordinal);
        // AC element 5: semantic exclusion, with the double-check scope untouched.
        Assert.Contains("> **Semantic exclusion:**", output, StringComparison.Ordinal);
        Assert.Contains("SEMANTIC AND PRODUCT DECISIONS ARE EXCLUDED, absolutely", output, StringComparison.Ordinal);
        Assert.Contains("double-check rule, whose scope this contract does not touch", output, StringComparison.Ordinal);
        Assert.Contains("deciding what SHOULD be true rather than checking what IS true", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_DesignReminderLoop_HasIntervalCapAndStopCondition_G552()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Periodic design-reminder loop", output, StringComparison.Ordinal);
        // AC: interval class, one-per-interval cap, stop-on-answer.
        Assert.Contains("30–60 minute class", output, StringComparison.Ordinal);
        Assert.Contains("AT MOST ONE reminder per interval PER OPEN CLARIFICATION", output, StringComparison.Ordinal);
        Assert.Contains("STOP ON ANSWER", output, StringComparison.Ordinal);
        // Sent by the orchestrator's existing long-interval automation — no new scheduler.
        Assert.Contains("The ORCHESTRATOR sends the reminder from its long-interval automation", output, StringComparison.Ordinal);
        // The operator-app reminder model, with no workspace-residency requirement.
        Assert.Contains("OPERATOR APP", output, StringComparison.Ordinal);
        Assert.Contains("finds it waiting in the inbox on resume", output, StringComparison.Ordinal);
        Assert.Contains("no workspace-residency requirement", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_DesignDecisionHolds_HasStructuredShape_G552()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var holds = doc.RootElement.GetProperty("design_decision_holds");

        var hold = holds.GetProperty("clarification_backed_hold");
        Assert.Equal(4, hold.GetProperty("required_fields").GetArrayLength());
        Assert.Contains("CONTRACT VIOLATION", hold.GetProperty("contract_violation_rule").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(hold.GetProperty("canonical_commands").EnumerateArray());
        var invocation = hold.GetProperty("paste_ready_invocation").GetString()!;
        Assert.Contains("--question", invocation, StringComparison.Ordinal);
        Assert.Contains("--recommended-answer", invocation, StringComparison.Ordinal);
        Assert.Contains("--evidence", invocation, StringComparison.Ordinal);

        var reviewer = holds.GetProperty("reviewer_hold_rule");
        Assert.Contains("FACT-CHECKABLE", reviewer.GetProperty("resolve_under_authority_when").GetString(), StringComparison.Ordinal);
        Assert.Contains("VISIBLE pending state", reviewer.GetProperty("record_clarification_otherwise").GetString(), StringComparison.Ordinal);
        Assert.Contains("no third option", reviewer.GetProperty("never_untracked_wait").GetString(), StringComparison.Ordinal);

        var authority = holds.GetProperty("bounded_default_authority");
        Assert.Contains("GRANTED, never assumed", authority.GetProperty("operator_grant_requirement").GetString(), StringComparison.Ordinal);
        var classes = authority.GetProperty("fact_checkable_classes").EnumerateArray()
            .Select(c => (Class: c.GetProperty("decision_class").GetString()!, Facts: c.GetProperty("verifying_facts").GetString()!))
            .ToArray();
        Assert.Equal(4, classes.Length);
        // Every enumerated class carries the facts that verify it — the
        // enumeration is what bounds the authority, so an entry without a
        // verification condition would silently widen it.
        Assert.All(classes, c => Assert.NotEmpty(c.Facts));
        Assert.Contains("MANDATORY EVIDENCE LOGGING", authority.GetProperty("evidence_logging_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("Recently Resolved", authority.GetProperty("evidence_sink").GetString(), StringComparison.Ordinal);
        Assert.Contains("clarify record --domain", authority.GetProperty("evidence_operation").GetString(), StringComparison.Ordinal);
        Assert.Contains("AMEND POST HOC", authority.GetProperty("post_hoc_amendment_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("EXCLUDED", authority.GetProperty("semantic_exclusion_rule").GetString(), StringComparison.Ordinal);

        var reminder = holds.GetProperty("design_reminder_loop");
        Assert.Contains("30–60 minute class", reminder.GetProperty("interval_class").GetString(), StringComparison.Ordinal);
        Assert.Contains("AT MOST ONE", reminder.GetProperty("one_per_interval_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("STOP ON ANSWER", reminder.GetProperty("stop_condition").GetString(), StringComparison.Ordinal);
        Assert.Contains("OPERATOR APP", reminder.GetProperty("operator_app_note").GetString(), StringComparison.Ordinal);

        // The guide points at the detector that reads what these rules write.
        Assert.Contains("design-decision-pending", holds.GetProperty("detection_reference").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_CrossProjectIsolation_RequiresAttributionBeforeMutation_G555()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("## Cross-project isolation on a shared machine (G555)", output, StringComparison.Ordinal);
        Assert.Contains("### Attribution before mutation", output, StringComparison.Ordinal);
        // AC: the four gated mutations.
        Assert.Contains("- injecting keys or text into a pane", output, StringComparison.Ordinal);
        Assert.Contains("- killing a process", output, StringComparison.Ordinal);
        Assert.Contains("- closing or restructuring a workspace", output, StringComparison.Ordinal);
        Assert.Contains("- removing or rewriting a state file", output, StringComparison.Ordinal);
        // AC: the four verification keys.
        Assert.Contains("**workspace label**", output, StringComparison.Ordinal);
        Assert.Contains("**pane cwd**", output, StringComparison.Ordinal);
        Assert.Contains("**process cwd**", output, StringComparison.Ordinal);
        Assert.Contains("**agmsg `(team, role)` file naming**", output, StringComparison.Ordinal);
        // Attribution is positive, not the absence of counter-evidence.
        Assert.Contains("not the absence of evidence that it belongs to someone else", output, StringComparison.Ordinal);
        // AC: the read-only default.
        Assert.Contains("> **Unverifiable = read-only:**", output, StringComparison.Ordinal);
        Assert.Contains("you may not mutate", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_CrossProjectIsolation_HasWorkspaceAndFolderExclusivity_G555()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Workspace and folder exclusivity", output, StringComparison.Ordinal);
        Assert.Contains("one workspace per team, labelled with the team/project name", output, StringComparison.Ordinal);
        Assert.Contains("Never reuse, repurpose, or borrow", output, StringComparison.Ordinal);
        // AC: the folder rule carries the G521 folder-scoping REASON, not just the rule.
        Assert.Contains("one folder belongs to exactly ONE team", output, StringComparison.Ordinal);
        Assert.Contains("(G521)", output, StringComparison.Ordinal);
        Assert.Contains("takes over THEIR identity and delivery", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_CrossProjectIsolation_TableListsExactlyTheFourSubstrates_G555()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Shared substrates and who owns what", output, StringComparison.Ordinal);
        Assert.Contains("| substrate | sharing unit | ownership rule |", output, StringComparison.Ordinal);
        Assert.Contains("workspace-manager server (e.g. the herdr server)", output, StringComparison.Ordinal);
        Assert.Contains("agmsg run directory (`~/.agents/skills/agmsg/run`)", output, StringComparison.Ordinal);
        Assert.Contains("codex app-servers", output, StringComparison.Ordinal);
        Assert.Contains("host repo", output, StringComparison.Ordinal);
        // AC: the host-repo row references G548 rather than restating it.
        Assert.Contains("(G548)", output, StringComparison.Ordinal);
        Assert.Contains("not a licence to hand-edit another domain's state", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_CrossProjectIsolation_RecoveryPreservesTheirsAndRebuildsYours_G555()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude"]);

        Assert.Contains("### Non-destructive recovery", output, StringComparison.Ordinal);
        Assert.Contains("**preserve theirs**", output, StringComparison.Ordinal);
        Assert.Contains("Never delete another team's workspace", output, StringComparison.Ordinal);
        Assert.Contains("**rebuild yours**", output, StringComparison.Ordinal);
        // The operator's own one-line form of the rule.
        Assert.Contains("> **Recovery defaults to RECREATE, NOT CLEANUP.**", output, StringComparison.Ordinal);
        // The slice narrows the OBJECT set, not the action set — G550's
        // authority boundary is explicitly untouched.
        Assert.Contains("it does not widen or narrow what you may DO", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_CrossProjectIsolation_HasStructuredShape_G555()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--agent", "claude", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var isolation = doc.RootElement.GetProperty("cross_project_isolation");

        var attribution = isolation.GetProperty("attribution_before_mutation");
        Assert.Equal(4, attribution.GetProperty("gated_mutations").GetArrayLength());

        var keys = attribution.GetProperty("verification_keys").EnumerateArray()
            .Select(k => (Key: k.GetProperty("key").GetString()!, How: k.GetProperty("how_to_check").GetString()!))
            .ToArray();
        Assert.Equal(4, keys.Length);
        Assert.All(keys, k => Assert.NotEmpty(k.How));
        Assert.Contains(keys, k => k.Key == "workspace label");
        Assert.Contains(keys, k => k.Key == "pane cwd");
        Assert.Contains(keys, k => k.Key == "process cwd");
        Assert.Contains(keys, k => k.Key.Contains("(team, role)", StringComparison.Ordinal));
        Assert.Contains("read-only", attribution.GetProperty("unverifiable_is_read_only").GetString(), StringComparison.OrdinalIgnoreCase);

        Assert.Contains("G521", isolation.GetProperty("team_exclusive_role_folders").GetString(), StringComparison.Ordinal);
        Assert.NotEmpty(isolation.GetProperty("one_workspace_per_team").GetString());

        // AC: EXACTLY four substrates, each with a sharing unit and an
        // ownership rule — the table is the whole set, so a fifth row or a
        // missing column would change the contract.
        var substrates = isolation.GetProperty("shared_substrates").EnumerateArray()
            .Select(x => (Name: x.GetProperty("substrate").GetString()!, Unit: x.GetProperty("sharing_unit").GetString()!, Rule: x.GetProperty("ownership_rule").GetString()!))
            .ToArray();
        Assert.Equal(4, substrates.Length);
        Assert.All(substrates, x => Assert.NotEmpty(x.Unit));
        Assert.All(substrates, x => Assert.NotEmpty(x.Rule));
        Assert.Contains(substrates, x => x.Name.Contains("workspace-manager server", StringComparison.Ordinal));
        Assert.Contains(substrates, x => x.Name.Contains("agmsg run directory", StringComparison.Ordinal));
        Assert.Contains(substrates, x => x.Name.Contains("codex app-servers", StringComparison.Ordinal));
        Assert.Contains(substrates, x => x.Name == "host repo" && x.Rule.Contains("G548", StringComparison.Ordinal));

        var recovery = isolation.GetProperty("non_destructive_recovery");
        Assert.Contains("Never delete", recovery.GetProperty("preserve_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("REBUILD YOUR OWN", recovery.GetProperty("rebuild_rule").GetString(), StringComparison.Ordinal);
        Assert.Contains("RECREATE, NOT CLEANUP", recovery.GetProperty("default_is_recreate_not_cleanup").GetString(), StringComparison.Ordinal);
    }

    private static string RunMarkdown(string[] args)
    {
        using var writer = new StringWriter();
        var fullArgs = args.Concat(new[] { "--format", "markdown" }).ToArray();
        var exitCode = GuideOrchestratorThreadCommand.Execute(CreateContext(), fullArgs, writer);
        Assert.Equal(0, exitCode);
        return writer.ToString();
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
    }
}
