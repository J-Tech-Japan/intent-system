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
        // Only the orchestrator is scheduled.
        Assert.Contains("Only the orchestrator is scheduled", output, StringComparison.Ordinal);
        // No agmsg commands emitted while inputs are missing.
        Assert.DoesNotContain("agmsg join.sh", output, StringComparison.Ordinal);
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
