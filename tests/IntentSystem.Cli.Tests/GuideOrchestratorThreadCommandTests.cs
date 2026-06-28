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
        // The existing timer-loop mode is preserved, not replaced.
        Assert.Contains("still fully supported", output, StringComparison.Ordinal);
        // Mixed-mode timer race is explicitly forbidden.
        Assert.Contains("do NOT launch the implementation/review recurring timer loops", output, StringComparison.Ordinal);
        // agmsg is signal-only; intent-cli/GitHub authoritative.
        Assert.Contains("agmsg", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli and GitHub remain authoritative", output, StringComparison.Ordinal);
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

        // First wake: read replies, ask intent-cli, verify GitHub, one message per wake.
        Assert.Contains("## Orchestrator first wake", output, StringComparison.Ordinal);
        Assert.Contains("Send AT MOST ONE message this wake", output, StringComparison.Ordinal);

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
    public void Execute_Markdown_SchedulesOnlyOrchestrator_WithCodexAndClaudeLoopPrompts()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude"]);

        Assert.Contains("## Scheduled orchestrator cadence", output, StringComparison.Ordinal);
        // Orchestrator is the single recurring driver.
        Assert.Contains("single recurring driver", output, StringComparison.Ordinal);
        Assert.Contains("scheduled thread: `orchestrator`", output, StringComparison.Ordinal);
        // Both setup prompts are present.
        Assert.Contains("Codex automation (5m)", output, StringComparison.Ordinal);
        Assert.Contains("Claude `/loop 5m`", output, StringComparison.Ordinal);
        // Receivers are explicitly loopless.
        Assert.Contains("loopless receiver", output, StringComparison.Ordinal);
        Assert.Contains("do NOT start your own", output, StringComparison.Ordinal);
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

        // The orchestrator prompt lists publication as a possible single action.
        var orchestrator = doc.RootElement.GetProperty("threads").EnumerateArray()
            .First(t => t.GetProperty("role").GetString() == "orchestrator")
            .GetProperty("prompt").GetString()!;
        Assert.Contains("publish one ready next-slice issue", orchestrator, StringComparison.Ordinal);
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
        // Structured escalation message with evidence + decision needed.
        Assert.Contains("\"to\":\"design\"", output, StringComparison.Ordinal);
        Assert.Contains("\"decision_needed\"", output, StringComparison.Ordinal);
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
        Assert.Contains("evidence", template, StringComparison.Ordinal);
        Assert.Contains("decision_needed", template, StringComparison.Ordinal);

        // The orchestrator prompt applies the filter and never hides failures.
        var orchestrator = doc.RootElement.GetProperty("threads").EnumerateArray()
            .First(t => t.GetProperty("role").GetString() == "orchestrator")
            .GetProperty("prompt").GetString()!;
        Assert.Contains("human-facing DESIGN thread ONLY human-needed decisions", orchestrator, StringComparison.Ordinal);
        Assert.Contains("never hide a failure that needs a human", orchestrator, StringComparison.Ordinal);
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
    public void Execute_Help_ExplainsOptionalAndNonReplacing()
    {
        using var writer = new StringWriter();
        var exitCode = GuideOrchestratorThreadCommand.Execute(
            CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide orchestrator-thread", output, StringComparison.Ordinal);
        Assert.Contains("OPTIONAL", output, StringComparison.Ordinal);
        Assert.Contains("not replaced", output, StringComparison.Ordinal);
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
