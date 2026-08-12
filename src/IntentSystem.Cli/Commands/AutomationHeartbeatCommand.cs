using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G526: <c>intent-cli automation heartbeat --domain &lt;d&gt; --repo &lt;r&gt;
/// [--stale-minutes &lt;m, default 45&gt;] [--format json|markdown]</c> — a
/// read-only wrapper around <see cref="AutomationStalledWorkCommand.Analyze"/>
/// that additionally emits a ready-to-send orchestrator reconcile message
/// body when anything is stale.
///
/// Field motivation: the orchestrator reconciles correctly within minutes of
/// ANY inbound wake (G524), but the two existing safety nets — a 5-minute
/// in-session orchestrator fallback timer, and a design-side watchdog —
/// both failed a real 16-day field trial (fast polling nobody wanted; a
/// watchdog living in the single most fragile, frequently-restarting
/// component). A session-independent external scheduler (cron/launchd)
/// running this command at a LOW frequency (60-minute class) survives every
/// agent restart, stays completely silent while the pipeline is healthy,
/// and caps the worst-case stall at its own interval.
///
/// This command NEVER sends a message, launches an agent, or mutates any
/// state — it only computes and emits text. A thin external wrapper
/// (documented in the orchestrator-thread guide) is responsible for piping
/// <see cref="AutomationHeartbeatResult.MessageBody"/> to <c>agmsg send.sh</c>
/// when present, at most once per run.
/// </summary>
internal static class AutomationHeartbeatCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    /// <summary>
    /// Default staleness threshold: below G523's typical single-message
    /// recovery latency (minutes) but well above one external-pinger
    /// interval cycle, so a healthy pipeline reconciling within its normal
    /// rhythm never trips a false alarm.
    /// </summary>
    public const int DefaultStaleMinutes = 45;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation heartbeat --domain <name> --repo <owner/repo> [--team <logical-team>] [--stale-minutes <m, default 45>] [--format json|markdown]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var domain, out var repo, out var team, out var staleMinutes, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        AutomationStalledWorkResult stalledWork;
        try
        {
            stalledWork = AutomationStalledWorkCommand.Analyze(context, domain!, repo!, staleMinutes);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to read GitHub state for {repo}: {exception.Message}");
            return 1;
        }

        var decision = Decide(context, domain!, repo!, team, staleMinutes, stalledWork);
        var result = new AutomationHeartbeatResult
        {
            Domain = domain!,
            Repo = repo!,
            Team = team,
            StaleMinutesThreshold = staleMinutes,
            // G597: the closed decision is the authoritative heartbeat
            // outcome. Keep the legacy field aligned so pre-decision callers
            // cannot suppress an overdue CI wait merely because stalled-work
            // intentionally treats a pending check as non-stale.
            Stale = stalledWork.Stalled
                || stalledWork.DetectionAvailable is false
                || string.Equals(decision.Verdict, "actionable-stall", StringComparison.Ordinal),
            Items = stalledWork.Items,
            Warnings = stalledWork.Warnings,
            OperatorAttentionStatus = stalledWork.OperatorAttentionStatus,
            OperatorAttentionError = stalledWork.OperatorAttentionError,
            RouteTo = ResolveRoute(stalledWork),
            MessageBody = stalledWork.Stalled
                || string.Equals(decision.Verdict, "actionable-stall", StringComparison.Ordinal)
                ? BuildMessageBody(stalledWork)
                : null,
            Verdict = decision.Verdict,
            Reason = decision.Reason,
            LastProgressBasis = decision.LastProgressBasis,
            LastProgressSource = decision.LastProgressSource,
            LastProgressAgeMinutes = decision.LastProgressAgeMinutes,
            DedupeKey = decision.DedupeKey,
            ActionOwner = decision.ActionOwner,
            TargetRole = decision.TargetRole,
            CanonicalNotifyCommand = decision.CanonicalNotifyCommand,
            WaitCondition = decision.WaitCondition,
            WaitEndSignal = decision.WaitEndSignal,
            WaitBoundMinutes = decision.WaitBoundMinutes,
            SuggestedAction = decision.SuggestedAction,
            Partial = stalledWork.Partial,
            DetectionAvailable = stalledWork.DetectionAvailable,
            DetectionStatus = stalledWork.DetectionStatus,
            GithubApiStatus = stalledWork.GithubApiStatus,
            Degraded = stalledWork.Degraded,
            Cause = stalledWork.Cause,
            DegradedState = stalledWork.DegradedState,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    /// <summary>
    /// Builds a deterministic, ready-to-send reconcile status-request naming
    /// every stale item and its canonical next command. Per the G524 wake
    /// contract, a single message is sufficient to trigger a full recovery
    /// wake — this body is meant to be sent once per heartbeat run, verbatim,
    /// by the external wrapper.
    ///
    /// G533: the summary line and per-item framing distinguish ACTIONABLE
    /// items (a runnable command is recommended) from INFORMATIONAL ones
    /// (<see cref="StalledWorkItem.IsInformational"/> — repair-pending,
    /// rereview-pending, claimed-but-silent) so a reader — human or
    /// orchestrator — never mistakes "FYI, no transition needed" for
    /// "pending transition(s)".
    /// </summary>
    private static string BuildMessageBody(AutomationStalledWorkResult stalledWork)
    {
        var attentionItems = stalledWork.Items
            .Where(item => item.OrchestratorActionable is false && !string.IsNullOrWhiteSpace(item.RequiredActor))
            .ToArray();
        var attentionOwners = attentionItems
            .Select(item => item.RequiredActor!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var actionableCount = stalledWork.Items.Count(item =>
            !item.IsInformational && item.OrchestratorActionable is not false);
        var informationalCount = stalledWork.Items.Count(item => item.IsInformational);

        var builder = new StringBuilder();
        builder.Append("WAKE (heartbeat): ");
        if (stalledWork.DetectionAvailable is false)
        {
            builder
                .Append("DETECTION UNAVAILABLE (partial local findings; ")
                .Append(stalledWork.Cause ?? GitHubApiQuotaConstants.DetectionUnavailableCause)
                .Append(", reset=")
                .Append(stalledWork.ResetAt ?? stalledWork.Reset?.ToString() ?? "unknown")
                .Append("). ");
        }
        if (attentionItems.Length > 0)
        {
            var ownerRoute = string.Join(" AND ", attentionOwners).ToUpperInvariant();
            builder
                .Append("ROUTE TO ").Append(ownerRoute).Append(" — ")
                .Append(attentionItems.Length).Append(" ")
                .Append(string.Join("-and-", attentionOwners)).Append("-required attention item(s), ");
        }
        builder
            .Append(actionableCount)
            .Append(" pending transition(s)");
        if (attentionItems.Length > 0)
        {
            builder
                .Append(" (")
                .Append(actionableCount)
                .Append(" orchestrator-actionable)");
        }
        if (informationalCount > 0)
        {
            builder.Append(", ").Append(informationalCount).Append(" informational note(s)");
        }
        builder
            .Append(" stale >= ")
            .Append(stalledWork.StaleMinutesThreshold)
            .Append("m in ")
            .Append(stalledWork.Repo)
            .Append('.');

        foreach (var item in stalledWork.Items)
        {
            builder
                .Append('\n')
                .Append("- `")
                .Append(item.ExecutionUnit)
                .Append("` (")
                .Append(item.Kind)
                .Append(", ")
                .Append(item.AgeMinutes)
                .Append('m');

            if (item.Issue is { } issue)
            {
                builder.Append(", issue #").Append(issue.Number);
            }
            if (item.Pr is { } pr)
            {
                builder.Append(", pr #").Append(pr.Number);
            }

            builder.Append(')');
            if (item.OrchestratorActionable is false && item.RequiredActor is { } requiredActor)
            {
                builder
                    .Append(" — ").Append(requiredActor.ToUpperInvariant())
                    .Append(" REQUIRED (orchestrator_actionable=false): ")
                    .Append(item.RecommendedAction);
                if (item.OperatorAttentionOwner is { } owner)
                {
                    builder.Append(" Owner: ").Append(owner).Append('.');
                }
                if (item.BlockingReference is { } blockingReference)
                {
                    builder.Append(" Blocking reference: ").Append(blockingReference).Append('.');
                }
            }
            else if (item.IsInformational)
            {
                builder.Append(" — FYI: ").Append(item.RecommendedAction);
            }
            else
            {
                builder.Append(" — recommended: `").Append(item.RecommendedAction).Append('`');
            }
        }

        return builder.ToString();
    }

    private static string? ResolveRoute(AutomationStalledWorkResult stalledWork)
    {
        if (!stalledWork.Stalled)
        {
            return null;
        }

        var attentionOwners = stalledWork.Items
            .Where(item => item.Kind is AutomationStalledWorkCommand.KindOperatorAttentionPending
                or AutomationStalledWorkCommand.KindOperatorAttentionCannotDetermine)
            .Select(item => item.RequiredActor)
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (attentionOwners.Length == 0)
        {
            return null;
        }
        var hasOrchestrator = stalledWork.Items.Any(item =>
            !item.IsInformational && item.OrchestratorActionable is not false);
        var ownerRoute = string.Join("-and-", attentionOwners);
        return hasOrchestrator ? $"{ownerRoute}-and-orchestration" : ownerRoute;
    }

    private static HeartbeatDecision Decide(
        CliContext context,
        string domain,
        string repo,
        string? team,
        int staleMinutes,
        AutomationStalledWorkResult stalledWork)
    {
        // G673: a quota-degraded scan is never allowed to fall through to the
        // healthy/no-pending path. The named blind spot is itself the
        // heartbeat verdict; callers decide whether to wait for reset.
        if (stalledWork.DetectionAvailable is false)
        {
            var source = stalledWork.Degraded ? "github.api.rate_limit" : "github.api";
            var suggestedAction = stalledWork.Degraded
                ? "Record the named GitHub quota blind spot and let orchestration decide whether to wait deliberately; no automatic retry or scheduling is performed."
                : "Record the named non-quota GitHub read failure, retain the partial local findings, and let orchestration decide the next action; no automatic retry or scheduling is performed.";
            return new HeartbeatDecision(
                Verdict: "detection-unavailable",
                Reason: $"stalled-work could not complete GitHub-backed detection ({stalledWork.Cause ?? GitHubApiQuotaConstants.DetectionUnavailableCause}); "
                    + $"resource={stalledWork.Resource ?? "unknown"}; reset_at={stalledWork.ResetAt ?? stalledWork.Reset?.ToString() ?? "unknown"}.",
                LastProgressBasis: "named upstream GitHub detection availability observation",
                LastProgressSource: source,
                LastProgressAgeMinutes: 0,
                DedupeKey: $"heartbeat:detection-unavailable:{domain}:{repo}:{stalledWork.Resource ?? "unknown"}:{stalledWork.ResetAt ?? stalledWork.Reset?.ToString() ?? "unknown"}",
                ActionOwner: "monitor",
                TargetRole: null,
                CanonicalNotifyCommand: null,
                WaitCondition: null,
                WaitEndSignal: null,
                WaitBoundMinutes: null,
                SuggestedAction: suggestedAction);
        }

        var operatorStateUnknown = stalledWork.Items.FirstOrDefault(item =>
            string.Equals(item.Kind, AutomationStalledWorkCommand.KindOperatorAttentionCannotDetermine, StringComparison.Ordinal));
        if (operatorStateUnknown is not null)
        {
            return CannotDetermine(
                $"Operator-attention state cannot be determined: {operatorStateUnknown.RecommendedAction}",
                operatorStateUnknown,
                "operator-attention store");
        }

        var operatorRecord = stalledWork.Items.FirstOrDefault(item =>
            string.Equals(item.Kind, AutomationStalledWorkCommand.KindOperatorAttentionPending, StringComparison.Ordinal));
        if (operatorRecord is not null)
        {
            var owner = operatorRecord.OperatorAttentionOwner;
            if (string.IsNullOrWhiteSpace(owner))
            {
                return CannotDetermine(
                    $"Open operator-attention record '{operatorRecord.OperatorAttentionRecordId}' has no recorded owner.",
                    operatorRecord,
                    "operator-attention owner");
            }

            if (!string.Equals(owner, "operator", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(team))
                {
                    return CannotDetermine(
                        $"Recorded owner '{owner}' cannot be routed because --team is required for this heartbeat decision.",
                        operatorRecord,
                        "operator-attention owner routing");
                }

                var ownerRoute = ResolveAttentionOwnerRoute(context, domain, team, owner);
                if (!ownerRoute.Resolved)
                {
                    return CannotDetermine(ownerRoute.Reason!, operatorRecord, "operator-attention owner routing");
                }

                return AttentionRequired(operatorRecord, owner, ownerRoute);
            }

            return AttentionRequired(operatorRecord, owner, null);
        }

        if (string.IsNullOrWhiteSpace(team))
        {
            return CannotDetermine(
                "Recorded routing cannot be resolved because --team is required for this heartbeat decision.",
                stalledWork.Items.FirstOrDefault(),
                "heartbeat routing context");
        }

        var route = ResolveOrchestratorRoute(context, domain, team);
        if (!route.Resolved)
        {
            return CannotDetermine(route.Reason!, stalledWork.Items.FirstOrDefault(), "recorded topology");
        }

        // A concrete actionable finding always wins over a legitimate active
        // wait. Otherwise one fresh PR CI run could hide an unrelated stale
        // pipeline and force callers to re-derive the answer from Items.
        var actionable = stalledWork.Items.FirstOrDefault(item =>
            !item.IsInformational
            && !string.Equals(item.Kind, AutomationStalledWorkCommand.KindOperatorAttentionPending, StringComparison.Ordinal)
            && !string.Equals(item.Kind, AutomationStalledWorkCommand.KindCiPending, StringComparison.Ordinal));
        if (actionable is not null)
        {
            return ActionableStall(actionable, route, staleMinutes);
        }

        var ciPending = stalledWork.Items.FirstOrDefault(item =>
            string.Equals(item.Kind, AutomationStalledWorkCommand.KindCiPending, StringComparison.Ordinal));
        if (ciPending is not null && ciPending.AgeMinutes < staleMinutes)
        {
            return new HeartbeatDecision(
                Verdict: "healthy-active-wait",
                Reason: $"CI for PR #{ciPending.Pr?.Number} remains pending at its recorded exact head.",
                LastProgressBasis: $"PR #{ciPending.Pr?.Number} exact-head CI pending observation",
                LastProgressSource: "github.pr.status_check_rollup",
                LastProgressAgeMinutes: ciPending.AgeMinutes,
                DedupeKey: MaterialKey("healthy-active-wait", ciPending, "orchestration", route.TargetRole!),
                ActionOwner: "orchestration",
                TargetRole: route.TargetRole,
                CanonicalNotifyCommand: null,
                WaitCondition: $"CI for PR #{ciPending.Pr?.Number} head {ciPending.PrHeadSha} to reach a terminal outcome",
                WaitEndSignal: "the mode-specific CI-completion wake followed by an exact-head GitHub re-check",
                WaitBoundMinutes: staleMinutes,
                SuggestedAction: "Wait for the named CI completion signal, then re-evaluate this heartbeat decision.");
        }

        if (ciPending is not null)
        {
            return ActionableStall(ciPending, route, staleMinutes);
        }

        if (stalledWork.Items.Count > 0)
        {
            var unresolved = stalledWork.Items[0];
            return CannotDetermine(
                $"No action owner can be determined for informational stalled-work kind '{unresolved.Kind}'.",
                unresolved,
                "stalled-work classification");
        }

        return new HeartbeatDecision(
            Verdict: "actionable-stall",
            Reason: "No pending pipeline transition or named active wait is currently reported.",
            LastProgressBasis: "current successful stalled-work observation",
            LastProgressSource: "automation.stalled-work",
            LastProgressAgeMinutes: 0,
            DedupeKey: $"heartbeat:actionable-stall:orchestration:{route.TargetRole}:no-pending-transition:{domain}:{repo}:{team}",
            ActionOwner: "orchestration",
            TargetRole: route.TargetRole,
            CanonicalNotifyCommand: route.CanonicalNotifyCommand,
            WaitCondition: null,
            WaitEndSignal: null,
            WaitBoundMinutes: null,
            SuggestedAction: "Run the canonical notify command once for this no-pending-transition dedupe key.");
    }

    private static HeartbeatDecision AttentionRequired(StalledWorkItem record, string owner, HeartbeatRoute? route)
    {
        var targetRole = route?.TargetRole;
        var dedupeKey = MaterialKey("operator-required", record, owner, targetRole);
        var action = string.Equals(owner, "operator", StringComparison.Ordinal)
            ? $"Operator action required for '{record.OperatorAttentionRecordId}': {record.RecommendedAction}"
            : $"{owner} action required for '{record.OperatorAttentionRecordId}': {record.RecommendedAction}";
        return new HeartbeatDecision(
            Verdict: "operator-required",
            Reason: $"Open operator-attention record '{record.OperatorAttentionRecordId}' requires the recorded owner '{owner}'.",
            LastProgressBasis: $"operator-attention record '{record.OperatorAttentionRecordId}' opened",
            LastProgressSource: "operator-attention.opened_at",
            LastProgressAgeMinutes: record.AgeMinutes,
            DedupeKey: dedupeKey,
            ActionOwner: owner,
            TargetRole: targetRole,
            CanonicalNotifyCommand: route?.CanonicalNotifyCommand,
            WaitCondition: null,
            WaitEndSignal: null,
            WaitBoundMinutes: null,
            SuggestedAction: action);
    }

    private static HeartbeatDecision ActionableStall(
        StalledWorkItem item,
        HeartbeatRoute route,
        int staleMinutes)
    {
        var isOverdueCi = string.Equals(item.Kind, AutomationStalledWorkCommand.KindCiPending, StringComparison.Ordinal);
        var reason = isOverdueCi
            ? $"CI wait for PR #{item.Pr?.Number} exceeded its {staleMinutes}-minute bound without a terminal signal."
            : $"Stalled-work reports actionable '{item.Kind}' for '{item.ExecutionUnit}'.";
        var dedupeKey = MaterialKey("actionable-stall", item, "orchestration", route.TargetRole!);
        return new HeartbeatDecision(
            Verdict: "actionable-stall",
            Reason: reason,
            LastProgressBasis: ProgressBasis(item),
            LastProgressSource: ProgressSource(item),
            LastProgressAgeMinutes: item.AgeMinutes,
            DedupeKey: dedupeKey,
            ActionOwner: "orchestration",
            TargetRole: route.TargetRole,
            CanonicalNotifyCommand: route.CanonicalNotifyCommand!,
            WaitCondition: null,
            WaitEndSignal: null,
            WaitBoundMinutes: null,
            SuggestedAction: $"Run the canonical notify command once for dedupe key '{dedupeKey}'.");
    }

    private static HeartbeatDecision CannotDetermine(string reason, StalledWorkItem? item, string source) => new(
        Verdict: "cannot-determine",
        Reason: reason,
        LastProgressBasis: item is null ? source : ProgressBasis(item),
        LastProgressSource: item is null ? source : ProgressSource(item),
        LastProgressAgeMinutes: item?.AgeMinutes ?? 0,
        DedupeKey: $"heartbeat:cannot-determine:monitor:{source}:{reason}",
        ActionOwner: "monitor",
        TargetRole: null,
        CanonicalNotifyCommand: null,
        WaitCondition: null,
        WaitEndSignal: null,
        WaitBoundMinutes: null,
        SuggestedAction: $"Repair or inspect the named {source} failure before treating this heartbeat as healthy.");

    private static HeartbeatRoute ResolveOrchestratorRoute(
        CliContext context,
        string domain,
        string team)
    {
        var topology = NotifyRoleTopologyStore.Resolve(context.RepoRoot, domain, team);
        if (!topology.Resolved)
        {
            return HeartbeatRoute.Failure(topology.Summary);
        }

        var sender = NotifyRoleTopologyStore.ResolveDeliveryTarget(context.RepoRoot, topology.Topology!, "design");
        var recipient = NotifyRoleTopologyStore.ResolveDeliveryTarget(context.RepoRoot, topology.Topology!, "orchestration");
        if (!sender.Resolved || !recipient.Resolved)
        {
            return HeartbeatRoute.Failure($"Recorded routing for team '{team}' is unresolved: "
                + $"{sender.Summary} {recipient.Summary}");
        }

        var command = "intent-cli notify report"
            + $" --domain {domain} --team {team} --from design --to orchestration"
            + " --task-id <heartbeat-dedupe-key> --status question --artifact <heartbeat-evidence>"
            + " --summary <one-line-summary>"
            + $" --routing-root '{context.RepoRoot.Replace("'", "'\\''", StringComparison.Ordinal)}'"
            + " --write --format json";
        return new HeartbeatRoute(true, null, "orchestration", command);
    }

    private static HeartbeatRoute ResolveAttentionOwnerRoute(CliContext context, string domain, string team, string owner)
    {
        var topology = NotifyRoleTopologyStore.Resolve(context.RepoRoot, domain, team);
        if (!topology.Resolved)
        {
            return HeartbeatRoute.Failure(topology.Summary);
        }

        var sender = NotifyRoleTopologyStore.ResolveDeliveryTarget(context.RepoRoot, topology.Topology!, "orchestration");
        var recipient = NotifyRoleTopologyStore.ResolveDeliveryTarget(context.RepoRoot, topology.Topology!, owner);
        if (!sender.Resolved || !recipient.Resolved)
        {
            return HeartbeatRoute.Failure($"Recorded owner '{owner}' cannot be resolved for team '{team}': {sender.Summary} {recipient.Summary}");
        }

        var command = "intent-cli notify report"
            + $" --domain {domain} --team {team} --from orchestration --to {owner}"
            + " --task-id <heartbeat-dedupe-key> --status question --artifact <heartbeat-evidence>"
            + " --summary <one-line-summary>"
            + $" --routing-root '{context.RepoRoot.Replace("'", "'\\''", StringComparison.Ordinal)}'"
            + " --write --format json";
        return new HeartbeatRoute(true, null, owner, command);
    }

    private static string MaterialKey(string verdict, StalledWorkItem? item, string owner, string? targetRole) =>
        $"heartbeat:{verdict}:{owner}:{targetRole ?? "none"}:{item?.DedupeKey ?? $"{item?.Kind ?? "none"}:{item?.ExecutionUnit ?? "none"}:{item?.Issue?.Number}:{item?.Pr?.Number}"}";

    private static string ProgressBasis(StalledWorkItem item) => item.Kind switch
    {
        AutomationStalledWorkCommand.KindOperatorAttentionPending =>
            $"operator-attention record '{item.OperatorAttentionRecordId}' opened",
        AutomationStalledWorkCommand.KindCiPending =>
            $"PR #{item.Pr?.Number} exact-head CI pending observation",
        _ => $"stalled-work kind '{item.Kind}' for '{item.ExecutionUnit}'",
    };

    private static string ProgressSource(StalledWorkItem item) => item.Kind switch
    {
        AutomationStalledWorkCommand.KindOperatorAttentionPending => "operator-attention.opened_at",
        AutomationStalledWorkCommand.KindCiPending => "github.pr.status_check_rollup",
        _ => "automation.stalled-work",
    };

    private static bool TryParseArguments(
        string[] args,
        out string? domain,
        out string? repo,
        out string? team,
        out int staleMinutes,
        out string format,
        out string error)
    {
        domain = null;
        repo = null;
        team = null;
        staleMinutes = DefaultStaleMinutes;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }
                    team = args[++index].Trim();
                    break;
                case "--stale-minutes":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedMinutes)
                        || parsedMinutes < 0)
                    {
                        error = "--stale-minutes requires a non-negative integer.";
                        return false;
                    }
                    staleMinutes = parsedMinutes;
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            error = "automation heartbeat requires '--domain <name>'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation heartbeat requires '--repo <owner/repo>'.";
            return false;
        }
        return true;
    }

    private static void WriteMarkdown(TextWriter writer, AutomationHeartbeatResult result)
    {
        writer.WriteLine($"# automation heartbeat — `{result.Domain}` / `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- stale_minutes_threshold: {result.StaleMinutesThreshold}");
        writer.WriteLine($"- stale: {(result.Stale ? "true" : "false")}");
        writer.WriteLine($"- verdict: {result.Verdict}");
        writer.WriteLine($"- dedupe_key: {result.DedupeKey}");
        writer.WriteLine($"- last_progress: {result.LastProgressBasis} ({result.LastProgressSource}, {result.LastProgressAgeMinutes}m)");
        writer.WriteLine($"- action_owner: {result.ActionOwner}");
        if (result.TargetRole is not null)
        {
            writer.WriteLine($"- target_role: {result.TargetRole}");
        }
        if (result.WaitCondition is not null)
        {
            writer.WriteLine($"- wait: {result.WaitCondition}");
            writer.WriteLine($"- wait_end_signal: {result.WaitEndSignal}");
            writer.WriteLine($"- wait_bound_minutes: {result.WaitBoundMinutes}");
        }
        if (result.CanonicalNotifyCommand is not null)
        {
            writer.WriteLine($"- canonical_notify_command: `{result.CanonicalNotifyCommand}`");
        }
        if (result.Reason is not null)
        {
            writer.WriteLine($"- reason: {result.Reason}");
        }
        writer.WriteLine($"- suggested_action: {result.SuggestedAction}");
        writer.WriteLine($"- items: {result.Items.Count}");
        if (result.OperatorAttentionStatus is not null)
        {
            writer.WriteLine($"- operator_attention_status: {result.OperatorAttentionStatus}");
        }
        if (result.RouteTo is not null)
        {
            writer.WriteLine($"- route_to: {result.RouteTo}");
        }
        writer.WriteLine();

        if (result.Items.Count == 0)
        {
            writer.WriteLine("Healthy — nothing stale.");
        }
        else
        {
            foreach (var item in result.Items)
            {
                var kindLabel = item.IsInformational ? $"{item.Kind}, informational" : item.Kind;
                writer.WriteLine($"## `{item.ExecutionUnit}` — {kindLabel} ({item.AgeMinutes}m)");
                if (item.Issue is { } issue)
                {
                    writer.WriteLine($"- issue: #{issue.Number} — {issue.Url}");
                }
                if (item.Pr is { } pr)
                {
                    writer.WriteLine($"- pr: #{pr.Number} — {pr.Url}");
                }
                if (item.IsInformational)
                {
                    writer.WriteLine($"- status: {item.RecommendedAction}");
                }
                else
                {
                    writer.WriteLine($"- recommended_action: `{item.RecommendedAction}`");
                }
                if (item.RequiredActor is { } requiredActor)
                {
                    writer.WriteLine($"- required_actor: {requiredActor}");
                }
                if (item.OrchestratorActionable is { } orchestratorActionable)
                {
                    writer.WriteLine($"- orchestrator_actionable: {(orchestratorActionable ? "true" : "false")}");
                }
                writer.WriteLine();
            }
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
            writer.WriteLine();
        }

        if (!string.IsNullOrEmpty(result.MessageBody))
        {
            writer.WriteLine("## message_body (ready to send)");
            writer.WriteLine();
            writer.WriteLine(result.MessageBody);
        }
    }
}

internal sealed record AutomationHeartbeatResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("team")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? Team { get; init; }

    [JsonPropertyName("stale_minutes_threshold")]
    public required int StaleMinutesThreshold { get; init; }

    [JsonPropertyName("stale")]
    public required bool Stale { get; init; }

    [JsonPropertyName("items")]
    public required IReadOnlyList<StalledWorkItem> Items { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("operator_attention_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? OperatorAttentionStatus { get; init; }

    [JsonPropertyName("operator_attention_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? OperatorAttentionError { get; init; }

    [JsonPropertyName("route_to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? RouteTo { get; init; }

    /// <summary>
    /// Ready-to-send orchestrator reconcile status-request, present only
    /// when <see cref="Stale"/> is true. Never sent by this command — an
    /// external wrapper (see the orchestrator-thread guide's "External
    /// heartbeat" section) pipes this verbatim to <c>agmsg send.sh</c>, at
    /// most once per run.
    /// </summary>
    [JsonPropertyName("message_body")]
    public string? MessageBody { get; init; }

    [JsonPropertyName("verdict")]
    public required string Verdict { get; init; }

    [JsonPropertyName("reason")]
    public required string? Reason { get; init; }

    [JsonPropertyName("last_progress_basis")]
    public required string LastProgressBasis { get; init; }

    [JsonPropertyName("last_progress_source")]
    public required string LastProgressSource { get; init; }

    [JsonPropertyName("last_progress_age_minutes")]
    public required int LastProgressAgeMinutes { get; init; }

    [JsonPropertyName("dedupe_key")]
    public required string DedupeKey { get; init; }

    [JsonPropertyName("action_owner")]
    public required string ActionOwner { get; init; }

    [JsonPropertyName("target_role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? TargetRole { get; init; }

    [JsonPropertyName("canonical_notify_command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? CanonicalNotifyCommand { get; init; }

    [JsonPropertyName("wait_condition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? WaitCondition { get; init; }

    [JsonPropertyName("wait_end_signal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required string? WaitEndSignal { get; init; }

    [JsonPropertyName("wait_bound_minutes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public required int? WaitBoundMinutes { get; init; }

    [JsonPropertyName("suggested_action")]
    public required string SuggestedAction { get; init; }

    /// <summary>G673: upstream availability projection inherited from stalled-work.</summary>
    [JsonPropertyName("partial")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Partial { get; init; }

    [JsonPropertyName("detection_available")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DetectionAvailable { get; init; }

    [JsonPropertyName("detection_unavailable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DetectionUnavailable => DetectionAvailable is false;

    [JsonPropertyName("detection_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DetectionStatus { get; init; }

    [JsonPropertyName("github_api_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GithubApiStatus { get; init; }

    [JsonPropertyName("degraded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Degraded { get; init; }

    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }

    [JsonPropertyName("degraded_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GitHubApiDegradedState? DegradedState { get; init; }

    [JsonPropertyName("resource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resource => DegradedState?.Resource;

    [JsonPropertyName("remaining")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Remaining => DegradedState?.Remaining;

    [JsonPropertyName("reset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Reset => DegradedState?.Reset;

    [JsonPropertyName("reset_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResetAt => DegradedState?.ResetAt;
}

internal sealed record HeartbeatDecision(
    string Verdict,
    string Reason,
    string LastProgressBasis,
    string LastProgressSource,
    int LastProgressAgeMinutes,
    string DedupeKey,
    string ActionOwner,
    string? TargetRole,
    string? CanonicalNotifyCommand,
    string? WaitCondition,
    string? WaitEndSignal,
    int? WaitBoundMinutes,
    string SuggestedAction);

internal sealed record HeartbeatRoute(bool Resolved, string? Reason, string? TargetRole, string? CanonicalNotifyCommand)
{
    public static HeartbeatRoute Failure(string reason) => new(false, reason, null, null);
}
