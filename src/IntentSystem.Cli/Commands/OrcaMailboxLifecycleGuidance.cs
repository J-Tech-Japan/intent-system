using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Shared, render-only instructions for an agent to operate an Orca mailbox.
/// This contract contains no process, filesystem, or transport execution.
/// </summary>
internal static class OrcaMailboxLifecycleGuidance
{
    public const string ContractVersion = "orca-mailbox-lifecycle/v1";
    public const string NoExecutionBoundary =
        "The agent executes the rendered Orca steps. intent-cli only renders guidance and reads existing host records; it never invokes Orca, launches a provider, sends or consumes mail, ACKs a Delivery, changes a binding, installs a timer, or executes a receive policy.";

    public static OrcaMailboxLifecycleContract CreateTemplate() => new()
    {
        ContractVersion = ContractVersion,
        ExecutionOwner = "agent",
        IntentCliExecutesOrca = false,
        CapabilityCheck = new OrcaMailboxInstruction
        {
            Preconditions = ["Use the installed CLI and its own read-only help; do not install, start, or upgrade Orca."],
            Commands =
            [
                Cmd("orca", "agent", "--version"),
                Cmd("orca", "agent", "skills", "get", "orchestration", "--json"),
                Cmd("orca", "agent", "orchestration", "run-create", "--help"),
                Cmd("orca", "agent", "orchestration", "request-show", "--help"),
                Cmd("orca", "agent", "orchestration", "check", "--help"),
                Cmd("intent-cli", "agent", "session-layer", "topology", "orca-runs", "--format", "json"),
            ],
            Evidence = "Confirm the installed version and required command forms, and inspect recorded Run bindings and their health. The measured command set is Orca 1.4.219; installed help is the capability check.",
            FailureAction = "Report capability unavailable, preserve any existing binding, and consult installed help or manual recovery. Do not install/start Orca, create a provider, or assume newer flags.",
        },
        CallerIdentity = new OrcaMailboxCallerIdentity
        {
            Frontends = ["orca", "codex-app", "claude-app"],
            Branches =
            [
                new OrcaMailboxCallerBranch
                {
                    Caller = "authenticated Orca session",
                    Rule = "Let Orca use the caller identity injected by that authenticated session; omit terminal caller overrides.",
                    Check = "Use the current Run evidence from that same authenticated caller; do not reconstruct or inject session environment.",
                    FailureAction = "If Orca does not expose the authenticated session identity, stop mutation and report the missing caller capability.",
                },
                new OrcaMailboxCallerBranch
                {
                    Caller = "terminal caller",
                    Rule = "Use only the terminal handle verified as this caller's own identity; use it consistently as --from for run/send and --terminal for check.",
                    Check = "Inspect the caller's own current Run and exact returned receipts before mutation.",
                    FailureAction = "Without a verified own handle, stop lifecycle mutation. Never use focus defaults, prose matching, or another pane's handle.",
                },
                new OrcaMailboxCallerBranch
                {
                    Caller = "identity-less Codex/Claude app caller",
                    Rule = "The app frontend label does not supply an Orca caller identity.",
                    Check = "Retain the canonical intent-cli inbox workflow and report the exact missing Orca caller identity prerequisite.",
                    FailureAction = "Stop lifecycle mutation. Never borrow a terminal handle, fabricate a session identity, or launch a worker as a workaround.",
                },
            ],
        },
        BindingSelection = new OrcaMailboxBindingSelection
        {
            ContextStatus = "unresolved",
            UnresolvedReason = "Metadata-free guides render all supported branches. Bootstrap selects a branch only from recorded team-mode, roster, and binding-health readers.",
            Branches =
            [
                new OrcaMailboxShapeBranch
                {
                    TeamShape = "solo-conductor",
                    SeatDescription = "design (Architect sidecar)",
                    RosterRule = "A recorded team-scoped solo-conductor mode selects the design/Architect sidecar; no seat roster is required.",
                    BindingLocation = OrcaRunBinding.OrcaRunFileLocation,
                },
                new OrcaMailboxShapeBranch
                {
                    TeamShape = "four-seat",
                    SeatDescription = "the recorded role key normalized as Architect",
                    RosterRule = "Use the exact recorded Architect alias key returned by topology; do not substitute a canonical-looking key that is not recorded.",
                    BindingLocation = OrcaRunBinding.TopologyRoleLocation,
                },
                new OrcaMailboxShapeBranch
                {
                    TeamShape = "five-seat",
                    SeatDescription = "steward",
                    RosterRule = "The recorded five-seat roster selects Steward as the mailbox binding seat.",
                    BindingLocation = OrcaRunBinding.TopologyRoleLocation,
                },
            ],
            FrontendPolicies =
            [
                "frontend=orca uses receive_policy=orca-push",
                "frontend=codex-app or frontend=claude-app uses receive_policy=inbox-pull",
                "A missing or unsupported frontend is an explicit prerequisite; do not infer it from provider kind.",
            ],
            MissingBranch = "No binding: inspect the same caller's run-current and deliberate-adoption evidence; create only after capability and identity checks plus an explicit create choice.",
            HealthyBranch = "Healthy recorded binding: inspect and resume that exact recorded Run with its own caller. Skip create.",
            UnusableBranch = "Unusable or contradictory binding: use the existing health remedy or explicit reconciliation. Preserve the binding and both caller states; never clear, take over, or rebind automatically.",
        },
        CreateOrAdopt = new OrcaMailboxInstruction
        {
            Preconditions =
            [
                "Installed capability is available and the acting caller identity is verified.",
                "Read current binding health and inspect run-current for that same caller.",
                "An existing healthy binding skips create. An unknown or contradictory caller/current state stops mutation.",
            ],
            Commands =
            [
                Cmd("orca", "authenticated session caller", "orchestration", "run-current", "--json"),
                Cmd("orca", "verified terminal caller", "orchestration", "run-current", "--from", "<own-terminal-handle>", "--json"),
                Cmd("orca", "same authorized caller", "orchestration", "run-show", "--id", "<exact-run-id>", "--json"),
                Cmd("orca", "authenticated session caller", "orchestration", "run-use", "--id", "<exact-run-id>", "--json"),
                Cmd("orca", "verified terminal caller", "orchestration", "run-use", "--id", "<exact-run-id>", "--from", "<own-terminal-handle>", "--json"),
                Cmd("orca", "authenticated session caller", "orchestration", "run-create", "--objective", "<domain>/<team> <selected-seat> mailbox", "--json"),
                Cmd("orca", "verified terminal caller", "orchestration", "run-create", "--objective", "<domain>/<team> <selected-seat> mailbox", "--from", "<own-terminal-handle>", "--json"),
            ],
            Evidence = "For adoption, inspect the exact Run and prove this caller may adopt/resume it. For creation, retain the actual returned result.run.id and reported request receipt. Choose one branch; the returned id is authoritative, never objective text or list order.",
            FailureAction = "Use run-use only for deliberate authorized adoption; do not emit --takeover-legacy. Create once only after explicit create choice. If create succeeded but recording failed, retain that exact id for canonical repair; never create a replacement.",
        },
        RecordBinding = new OrcaMailboxRecordBinding
        {
            WorkingDirectory = "Run record-orca-run from the canonical host routing root so its existing CAS lock/current-token behavior applies; there is no --routing-root flag.",
            CurrentTokenRule = "Supply the actual current binding token (`absent` only when the selected binding is truly absent) and the confirmed returned Run id. Use the shape-selected recorded role key. Solo-conductor also supplies its recorded frontend.",
            DryRunCommand = Cmd("intent-cli", "agent in canonical routing root", "session-layer", "topology", "record-orca-run", "--domain", "<d>", "--team", "<t>", "--role", "<selected-role>", "--current", "<actual-current|absent>", "--new", "<confirmed-run-id>", "--receive-policy", "<orca-push|inbox-pull>", "--confirm-record-orca-run", "--dry-run", "--format", "json"),
            WriteCommand = Cmd("intent-cli", "agent in canonical routing root", "session-layer", "topology", "record-orca-run", "--domain", "<d>", "--team", "<t>", "--role", "<selected-role>", "--current", "<actual-current|absent>", "--new", "<confirmed-run-id>", "--receive-policy", "<orca-push|inbox-pull>", "--confirm-record-orca-run", "--write", "--format", "json"),
            SoloFrontendOption = "For solo-conductor, append `--frontend <recorded-frontend>` to both record commands; select orca, codex-app, or claude-app from the actual recorded frontend. Topology-role bindings keep frontend on the role record and do not pass --frontend to record-orca-run.",
            Verification = "Re-read guide bootstrap, the shape-appropriate show/validate/team-mode validate surface, and topology orca-runs. Confirm recorded health and handover; this does not verify an Orca runtime.",
            FailureAction = "If CAS or health validation fails, preserve the same confirmed Run id and follow the canonical refusal remedy. Never create a replacement, clear a binding, or infer a new current token.",
        },
        Delivery = new OrcaMailboxDelivery
        {
            DiscoveryCommand = Cmd("intent-cli", "agent", "session-layer", "topology", "orca-runs", "--format", "json"),
            AddressRule = "Inspect discovered binding health and address the exact recipient as run:<id>; never route by group, prose, or first row.",
            NotifyFirstRule = "For intent-cli task/report activity, complete canonical intent-cli notify first and use Orca only for courtesy attention. Independent cross-team discussion may use Run mail without manufacturing a notify task.",
            SessionSendCommand = Cmd("orca", "authenticated session caller", "orchestration", "send", "--to", "run:<recipient-run-id>", "--type", "status", "--subject", "<subject>", "--body", "<body>", "--json"),
            TerminalSendCommand = Cmd("orca", "verified terminal caller", "orchestration", "send", "--to", "run:<recipient-run-id>", "--from", "<own-terminal-handle>", "--type", "status", "--subject", "<subject>", "--body", "<body>", "--json"),
            EnqueueRule = "A successful send proves durable enqueue only. Wake/nudge is best effort, not acceptance or proof a turn started, read, or accepted. Do not resend because attention is unproven; a second terminal send is optional only with independently authorized exact recipient-handle evidence.",
            FailureAction = "For an unknown send outcome, retain the exact command, executable, caller, payload and reported request id; follow request-show recovery. Never send a fresh message to compensate for uncertain attention.",
        },
        Receive = new OrcaMailboxReceive
        {
            PolicyBranches =
            [
                "orca-push: an Orca terminal receives attention; the receiver still checks and processes its mailbox.",
                "inbox-pull: the app uses its already-established canonical inbox wake/checkpoint cadence; intent-cli installs no scheduler.",
            ],
            SessionCheckCommand = Cmd("orca", "authenticated session caller", "orchestration", "check", "--run", "<own-run-id>", "--wait", "--timeout-ms", "30000", "--json"),
            TerminalCheckCommand = Cmd("orca", "verified terminal caller", "orchestration", "check", "--run", "<own-run-id>", "--terminal", "<own-terminal-handle>", "--wait", "--timeout-ms", "30000", "--json"),
            BatchRule = "A consuming check is agent-executed. Handle every row in the returned FIFO batch before ACK; --types affects wake conditions, not batch membership. Replay an unacknowledged batch without repeating already-applied canonical actions.",
            SessionAckCommand = Cmd("orca", "authenticated session caller", "orchestration", "check", "--run", "<own-run-id>", "--ack", "<returned-delivery-id>", "--json"),
            TerminalAckCommand = Cmd("orca", "verified terminal caller", "orchestration", "check", "--run", "<own-run-id>", "--terminal", "<own-terminal-handle>", "--ack", "<returned-delivery-id>", "--json"),
            AckRule = "ACK acknowledges transport delivery only, never task completion, review acceptance, or canonical notify receipt. Keep the whole batch unacknowledged if processing is incomplete. ACK may return another batch: inspect and retain/process it; never discard its output.",
            InspectionRule = "--peek and --all inspect only; they do not consume. Use finite waits and the existing receive/checkpoint cadence; add no polling timer, provider wake loop, lifecycle dispatch identity, or arbitrary wake-command execution.",
            FailureAction = "On interruption, re-check and reconcile actual message/task identities. If any row is unprocessed, do not ACK or selectively discard it.",
        },
        Recovery = new OrcaMailboxRecovery
        {
            RequestShowCommand = Cmd("orca", "agent", "orchestration", "request-show", "--request", "<reported-request-id>", "--json"),
            ExactReplayTemplate = "<exact-original-command> --retry-request <same-reported-request-id>",
            PreserveRule = "Retain the exact original command, payload, executable, caller, and reported request id. The replay line is a recovery template, not a new CLI verb; never mint an id or change scope.",
            CompletedBranch = "Completed: use the recorded receipt/outcome. Exact replay returns that outcome where installed Orca documents it; no exactly-once side-effect guarantee is asserted.",
            PendingBranch = "Pending: wait for a known live original command; otherwise use only the documented exact --retry-request recovery with the same id and unchanged command/caller.",
            UnknownBranch = "Absent is not proof of non-execution. Missing, malformed, inaccessible, or caller-mismatched receipts, and a missing request id, remain unknown. Inspect read-only Run/binding/mailbox evidence and require deliberate reconciliation.",
            FailureAction = "Unknown create/send/ACK outcomes never authorize a fresh create/send/ACK or a guessed Delivery id. No objective-based idempotency or empirical replay claim is made.",
        },
        CompletionBoundary = new OrcaMailboxCompletionBoundary
        {
            ParentIssue = "#1771 remains open until implementation, fresh reviews, tests, and owned live acceptance or an owner-accepted unresolved-boundary disposition are complete.",
            PolicyDisposition = "This G853 unit ships an agent-executed lifecycle under the standing policy that intent-cli never runs Orca; no automatic intent-cli create/send/check/ACK adapter or receive scheduler is shipped.",
            DispositionRows =
            [
                new OrcaMailboxDispositionRow { Ask = "Create once, record Run id, handover", Disposition = "G837 storage/handover plus G853 agent-executed create/adopt/recover/record", Evidence = "All entry guides render the flow; unknown outcomes never emit fresh create." },
                new OrcaMailboxDispositionRow { Ask = "Cross-team Run discovery", Disposition = "G837 orca-runs discovery retained", Evidence = "Existing discovery tests and exact-id addressing guidance." },
                new OrcaMailboxDispositionRow { Ask = "Durable attention and receive", Disposition = "Agent-executed enqueue, bounded whole-batch processing/ACK, existing cadence", Evidence = "Contract tests; no duplicate timer or mandatory second send." },
                new OrcaMailboxDispositionRow { Ask = "Report recovery in seat errors", Disposition = "G837 equal-root reconciliation retained", Evidence = "Existing report/collect recovery regression." },
                new OrcaMailboxDispositionRow { Ask = "Missing-binding validation", Disposition = "G837 informational finding retained where a recorded delivery shape applies", Evidence = "Existing shape/health regression controls; availability is agent-checked." },
                new OrcaMailboxDispositionRow { Ask = "Owned live acceptance", Disposition = "Root-owned adoption and bounded self-mail rehearsal after code gates", Evidence = "Not performed by this implementation seat; parent acceptance remains open until evidenced or explicitly dispositioned." },
                new OrcaMailboxDispositionRow { Ask = "Automatic Orca adapters", Disposition = "Intentionally not implemented under the standing no-executor ruling", Evidence = "Report the agent-executed disposition; do not claim an automatic adapter." },
            ],
            NoExecutionBoundary = NoExecutionBoundary,
        },
    };

    public static OrcaMailboxLifecycleContract ResolveBootstrap(
        string routingRoot,
        string? domain,
        string? team)
    {
        var template = CreateTemplate();
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            return template with
            {
                BindingSelection = template.BindingSelection with
                {
                    ContextStatus = "prerequisite",
                    UnresolvedReason = "Supply the accepted domain and team before reading team mode, roster, or binding health; no shape or frontend is guessed.",
                },
            };
        }

        var normalizedDomain = domain.Trim();
        var normalizedTeam = team.Trim();
        var shape = OrcaRunTeamShape.Resolve(routingRoot, normalizedDomain, normalizedTeam);
        var binding = OrcaRunBindingHealth.TryResolveBootstrapBinding(routingRoot, normalizedDomain, normalizedTeam);
        var bindingSeatKey = binding is not null
            && string.Equals(binding.Health, OrcaRunBinding.RecordedHealth, StringComparison.Ordinal)
            && LogicalRoleNormalizer.TryNormalize(binding.Role, out var bindingCanonical, out _)
            && string.Equals(bindingCanonical, shape.RequiredSeat, StringComparison.Ordinal)
                ? binding.Role
                : ResolveBindingSeatKey(routingRoot, normalizedDomain, normalizedTeam, shape);
        var roleSelectionAmbiguous = shape.Resolved
            && string.Equals(shape.BindingLocation, OrcaRunBinding.TopologyRoleLocation, StringComparison.Ordinal)
            && bindingSeatKey is null;
        var bindingStatus = binding is null
            ? shape.Resolved && !roleSelectionAmbiguous ? "absent" : "prerequisite"
            : string.Equals(binding.Health, OrcaRunBinding.RecordedHealth, StringComparison.Ordinal) ? "healthy" : "unusable";

        return template with
        {
            BindingSelection = template.BindingSelection with
            {
                ContextStatus = bindingStatus,
                TeamShape = shape.TeamShape,
                BindingSeatKey = bindingSeatKey,
                BindingLocation = binding?.BindingLocation ?? shape.BindingLocation,
                BindingHealth = binding?.Health,
                BindingCurrentToken = binding is null
                    ? shape.Resolved ? "absent" : null
                    : string.Equals(binding.Health, OrcaRunBinding.RecordedHealth, StringComparison.Ordinal) ? binding.RunId : null,
                ObservedBindingRole = binding?.Role,
                ReceivePolicy = binding?.ReceivePolicy,
                RecordedRunId = binding?.RunId,
                    UnresolvedReason = shape.Resolved
                    ? roleSelectionAmbiguous
                        ? $"The recorded roster has more than one role key normalized as '{shape.RequiredSeat}'. Resolve the exact selected role key before recording a binding."
                        : null
                    : "Recorded team mode or roster is a prerequisite. Repair canonical mode or roster before selecting a binding branch; no shape or frontend is guessed.",
            },
        };
    }

    private static string? ResolveBindingSeatKey(
        string routingRoot,
        string domain,
        string team,
        OrcaRunTeamShapeResult shape)
    {
        if (!shape.Resolved)
        {
            return null;
        }

        if (string.Equals(shape.TeamShape, "solo-conductor", StringComparison.Ordinal))
        {
            return "design";
        }

        var resolution = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!resolution.Resolved || resolution.Topology is null)
        {
            return null;
        }

        var candidates = resolution.Topology.Roles.Keys
            .Where(roleKey => LogicalRoleNormalizer.TryNormalize(roleKey, out var canonical, out _)
                && string.Equals(canonical, shape.RequiredSeat, StringComparison.Ordinal))
            .OrderBy(roleKey => roleKey, StringComparer.Ordinal)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static OrcaMailboxCommand Cmd(string executable, string caller, params string[] arguments) => new()
    {
        Executable = executable,
        Caller = caller,
        Arguments = arguments,
    };

    public static void WriteMarkdown(TextWriter writer, OrcaMailboxLifecycleContract contract)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(contract);
        writer.WriteLine("## Orca mailbox lifecycle");
        writer.WriteLine();
        writer.WriteLine($"- contract: `{contract.ContractVersion}`");
        writer.WriteLine($"- execution owner: **{contract.ExecutionOwner}**");
        writer.WriteLine($"- intent-cli executes Orca: **{contract.IntentCliExecutesOrca.ToString().ToLowerInvariant()}**");
        writer.WriteLine($"- boundary: {contract.CompletionBoundary.NoExecutionBoundary}");
        writer.WriteLine();
        writer.WriteLine("### Capability and caller identity");
        WriteInstruction(writer, contract.CapabilityCheck);
        foreach (var branch in contract.CallerIdentity.Branches)
        {
            writer.WriteLine($"- **{branch.Caller}:** {branch.Rule} Check: {branch.Check} Failure: {branch.FailureAction}");
        }
        writer.WriteLine();
        writer.WriteLine("### Recorded binding selection");
        writer.WriteLine($"- context status: `{contract.BindingSelection.ContextStatus}`");
        if (contract.BindingSelection.TeamShape is not null) writer.WriteLine($"- recorded team shape: `{contract.BindingSelection.TeamShape}`");
        if (contract.BindingSelection.BindingSeatKey is not null) writer.WriteLine($"- selected recorded role key: `{contract.BindingSelection.BindingSeatKey}`");
        if (contract.BindingSelection.ObservedBindingRole is not null) writer.WriteLine($"- role key carrying the observed binding: `{contract.BindingSelection.ObservedBindingRole}`");
        if (contract.BindingSelection.RecordedRunId is not null) writer.WriteLine($"- recorded Run id: `{contract.BindingSelection.RecordedRunId}`");
        if (contract.BindingSelection.ReceivePolicy is not null) writer.WriteLine($"- recorded receive policy: `{contract.BindingSelection.ReceivePolicy}`");
        if (contract.BindingSelection.UnresolvedReason is not null) writer.WriteLine($"- prerequisite: {contract.BindingSelection.UnresolvedReason}");
        foreach (var branch in contract.BindingSelection.Branches)
        {
            writer.WriteLine($"- **{branch.TeamShape}:** {branch.SeatDescription}; {branch.RosterRule} Binding location: `{branch.BindingLocation}`.");
        }
        writer.WriteLine($"- **healthy:** {contract.BindingSelection.HealthyBranch}");
        writer.WriteLine($"- **absent:** {contract.BindingSelection.MissingBranch}");
        writer.WriteLine($"- **unusable:** {contract.BindingSelection.UnusableBranch}");
        foreach (var policy in contract.BindingSelection.FrontendPolicies) writer.WriteLine($"- {policy}");
        writer.WriteLine();
        writer.WriteLine("### Create, deliberate adoption, record, and handover");
        WriteInstruction(writer, contract.CreateOrAdopt);
        writer.WriteLine($"- record working directory: {contract.RecordBinding.WorkingDirectory}");
        writer.WriteLine($"- current-token rule: {contract.RecordBinding.CurrentTokenRule}");
        writer.WriteLine($"- dry run: `{contract.RecordBinding.DryRunCommand.Render()}`");
        writer.WriteLine($"- write after inspecting dry run: `{contract.RecordBinding.WriteCommand.Render()}`");
        writer.WriteLine($"- frontend option: {contract.RecordBinding.SoloFrontendOption}");
        writer.WriteLine($"- verify: {contract.RecordBinding.Verification}");
        writer.WriteLine($"- record failure: {contract.RecordBinding.FailureAction}");
        writer.WriteLine();
        writer.WriteLine("### Durable delivery");
        writer.WriteLine($"- discover: `{contract.Delivery.DiscoveryCommand.Render()}`");
        writer.WriteLine($"- address: {contract.Delivery.AddressRule}");
        writer.WriteLine($"- canonical notify: {contract.Delivery.NotifyFirstRule}");
        writer.WriteLine($"- send from authenticated session: `{contract.Delivery.SessionSendCommand.Render()}`");
        writer.WriteLine($"- send from verified terminal: `{contract.Delivery.TerminalSendCommand.Render()}`");
        writer.WriteLine($"- enqueue and attention: {contract.Delivery.EnqueueRule}");
        writer.WriteLine($"- send failure: {contract.Delivery.FailureAction}");
        writer.WriteLine();
        writer.WriteLine("### Bounded FIFO receive and ACK");
        foreach (var policy in contract.Receive.PolicyBranches) writer.WriteLine($"- {policy}");
        writer.WriteLine($"- check from authenticated session: `{contract.Receive.SessionCheckCommand.Render()}`");
        writer.WriteLine($"- check from verified terminal: `{contract.Receive.TerminalCheckCommand.Render()}`");
        writer.WriteLine($"- batch: {contract.Receive.BatchRule}");
        writer.WriteLine($"- ACK from authenticated session: `{contract.Receive.SessionAckCommand.Render()}`");
        writer.WriteLine($"- ACK from verified terminal: `{contract.Receive.TerminalAckCommand.Render()}` — {contract.Receive.AckRule}");
        writer.WriteLine($"- inspection and cadence: {contract.Receive.InspectionRule}");
        writer.WriteLine($"- receive failure: {contract.Receive.FailureAction}");
        writer.WriteLine();
        writer.WriteLine("### Mutation recovery");
        writer.WriteLine($"- inspect: `{contract.Recovery.RequestShowCommand.Render()}`");
        writer.WriteLine($"- exact replay template: `{contract.Recovery.ExactReplayTemplate}`");
        writer.WriteLine($"- preserve: {contract.Recovery.PreserveRule}");
        writer.WriteLine($"- completed: {contract.Recovery.CompletedBranch}");
        writer.WriteLine($"- pending: {contract.Recovery.PendingBranch}");
        writer.WriteLine($"- unknown: {contract.Recovery.UnknownBranch}");
        writer.WriteLine($"- stop: {contract.Recovery.FailureAction}");
        writer.WriteLine();
        writer.WriteLine("### #1771 disposition and completion boundary");
        foreach (var row in contract.CompletionBoundary.DispositionRows)
        {
            writer.WriteLine($"- **{row.Ask}:** {row.Disposition}. Evidence: {row.Evidence}");
        }
        writer.WriteLine($"- {contract.CompletionBoundary.ParentIssue}");
        writer.WriteLine($"- {contract.CompletionBoundary.PolicyDisposition}");
    }

    private static void WriteInstruction(TextWriter writer, OrcaMailboxInstruction instruction)
    {
        writer.WriteLine($"- Preconditions: {string.Join("; ", instruction.Preconditions)}");
        foreach (var command in instruction.Commands) writer.WriteLine($"- agent command ({command.Caller}): `{command.Render()}`");
        writer.WriteLine($"- Evidence: {instruction.Evidence}");
        writer.WriteLine($"- Failure action: {instruction.FailureAction}");
    }
}

internal sealed record OrcaMailboxLifecycleContract
{
    [JsonPropertyName("contract_version")] public required string ContractVersion { get; init; }
    [JsonPropertyName("execution_owner")] public required string ExecutionOwner { get; init; }
    [JsonPropertyName("intent_cli_executes_orca")] public required bool IntentCliExecutesOrca { get; init; }
    [JsonPropertyName("capability_check")] public required OrcaMailboxInstruction CapabilityCheck { get; init; }
    [JsonPropertyName("caller_identity")] public required OrcaMailboxCallerIdentity CallerIdentity { get; init; }
    [JsonPropertyName("binding_selection")] public required OrcaMailboxBindingSelection BindingSelection { get; init; }
    [JsonPropertyName("create_or_adopt")] public required OrcaMailboxInstruction CreateOrAdopt { get; init; }
    [JsonPropertyName("record_binding")] public required OrcaMailboxRecordBinding RecordBinding { get; init; }
    [JsonPropertyName("delivery")] public required OrcaMailboxDelivery Delivery { get; init; }
    [JsonPropertyName("receive")] public required OrcaMailboxReceive Receive { get; init; }
    [JsonPropertyName("recovery")] public required OrcaMailboxRecovery Recovery { get; init; }
    [JsonPropertyName("completion_boundary")] public required OrcaMailboxCompletionBoundary CompletionBoundary { get; init; }
}

internal sealed record OrcaMailboxInstruction
{
    [JsonPropertyName("preconditions")] public required IReadOnlyList<string> Preconditions { get; init; }
    [JsonPropertyName("commands")] public required IReadOnlyList<OrcaMailboxCommand> Commands { get; init; }
    [JsonPropertyName("evidence")] public required string Evidence { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal sealed record OrcaMailboxCommand
{
    [JsonPropertyName("executable")] public required string Executable { get; init; }
    [JsonPropertyName("caller")] public required string Caller { get; init; }
    [JsonPropertyName("arguments")] public required IReadOnlyList<string> Arguments { get; init; }

    public string Render() => string.Join(" ", new[] { Executable }.Concat(Arguments.Select(RenderArgument)));

    private static string RenderArgument(string argument) => argument.Contains(' ', StringComparison.Ordinal)
        ? $"\"{argument}\""
        : argument;
}

internal sealed record OrcaMailboxCallerIdentity
{
    [JsonPropertyName("frontends")] public required IReadOnlyList<string> Frontends { get; init; }
    [JsonPropertyName("branches")] public required IReadOnlyList<OrcaMailboxCallerBranch> Branches { get; init; }
}

internal sealed record OrcaMailboxCallerBranch
{
    [JsonPropertyName("caller")] public required string Caller { get; init; }
    [JsonPropertyName("rule")] public required string Rule { get; init; }
    [JsonPropertyName("check")] public required string Check { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal sealed record OrcaMailboxBindingSelection
{
    [JsonPropertyName("context_status")] public required string ContextStatus { get; init; }
    [JsonPropertyName("team_shape")] public string? TeamShape { get; init; }
    [JsonPropertyName("binding_seat_key")] public string? BindingSeatKey { get; init; }
    [JsonPropertyName("observed_binding_role")] public string? ObservedBindingRole { get; init; }
    [JsonPropertyName("binding_location")] public string? BindingLocation { get; init; }
    [JsonPropertyName("binding_health")] public string? BindingHealth { get; init; }
    [JsonPropertyName("binding_current_token")] public string? BindingCurrentToken { get; init; }
    [JsonPropertyName("recorded_run_id")] public string? RecordedRunId { get; init; }
    [JsonPropertyName("receive_policy")] public string? ReceivePolicy { get; init; }
    [JsonPropertyName("unresolved_reason")] public string? UnresolvedReason { get; init; }
    [JsonPropertyName("branches")] public required IReadOnlyList<OrcaMailboxShapeBranch> Branches { get; init; }
    [JsonPropertyName("frontend_policies")] public required IReadOnlyList<string> FrontendPolicies { get; init; }
    [JsonPropertyName("missing_branch")] public required string MissingBranch { get; init; }
    [JsonPropertyName("healthy_branch")] public required string HealthyBranch { get; init; }
    [JsonPropertyName("unusable_branch")] public required string UnusableBranch { get; init; }
}

internal sealed record OrcaMailboxShapeBranch
{
    [JsonPropertyName("team_shape")] public required string TeamShape { get; init; }
    [JsonPropertyName("seat_description")] public required string SeatDescription { get; init; }
    [JsonPropertyName("roster_rule")] public required string RosterRule { get; init; }
    [JsonPropertyName("binding_location")] public required string BindingLocation { get; init; }
}

internal sealed record OrcaMailboxRecordBinding
{
    [JsonPropertyName("working_directory")] public required string WorkingDirectory { get; init; }
    [JsonPropertyName("current_token_rule")] public required string CurrentTokenRule { get; init; }
    [JsonPropertyName("dry_run_command")] public required OrcaMailboxCommand DryRunCommand { get; init; }
    [JsonPropertyName("write_command")] public required OrcaMailboxCommand WriteCommand { get; init; }
    [JsonPropertyName("solo_frontend_option")] public required string SoloFrontendOption { get; init; }
    [JsonPropertyName("verification")] public required string Verification { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal sealed record OrcaMailboxDelivery
{
    [JsonPropertyName("discovery_command")] public required OrcaMailboxCommand DiscoveryCommand { get; init; }
    [JsonPropertyName("address_rule")] public required string AddressRule { get; init; }
    [JsonPropertyName("notify_first_rule")] public required string NotifyFirstRule { get; init; }
    [JsonPropertyName("session_send_command")] public required OrcaMailboxCommand SessionSendCommand { get; init; }
    [JsonPropertyName("terminal_send_command")] public required OrcaMailboxCommand TerminalSendCommand { get; init; }
    [JsonPropertyName("enqueue_rule")] public required string EnqueueRule { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal sealed record OrcaMailboxReceive
{
    [JsonPropertyName("policy_branches")] public required IReadOnlyList<string> PolicyBranches { get; init; }
    [JsonPropertyName("session_check_command")] public required OrcaMailboxCommand SessionCheckCommand { get; init; }
    [JsonPropertyName("terminal_check_command")] public required OrcaMailboxCommand TerminalCheckCommand { get; init; }
    [JsonPropertyName("batch_rule")] public required string BatchRule { get; init; }
    [JsonPropertyName("session_ack_command")] public required OrcaMailboxCommand SessionAckCommand { get; init; }
    [JsonPropertyName("terminal_ack_command")] public required OrcaMailboxCommand TerminalAckCommand { get; init; }
    [JsonPropertyName("ack_rule")] public required string AckRule { get; init; }
    [JsonPropertyName("inspection_rule")] public required string InspectionRule { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal sealed record OrcaMailboxRecovery
{
    [JsonPropertyName("request_show_command")] public required OrcaMailboxCommand RequestShowCommand { get; init; }
    [JsonPropertyName("exact_replay_template")] public required string ExactReplayTemplate { get; init; }
    [JsonPropertyName("preserve_rule")] public required string PreserveRule { get; init; }
    [JsonPropertyName("completed_branch")] public required string CompletedBranch { get; init; }
    [JsonPropertyName("pending_branch")] public required string PendingBranch { get; init; }
    [JsonPropertyName("unknown_branch")] public required string UnknownBranch { get; init; }
    [JsonPropertyName("failure_action")] public required string FailureAction { get; init; }
}

internal sealed record OrcaMailboxCompletionBoundary
{
    [JsonPropertyName("parent_issue")] public required string ParentIssue { get; init; }
    [JsonPropertyName("policy_disposition")] public required string PolicyDisposition { get; init; }
    [JsonPropertyName("disposition_rows")] public required IReadOnlyList<OrcaMailboxDispositionRow> DispositionRows { get; init; }
    [JsonPropertyName("no_execution_boundary")] public required string NoExecutionBoundary { get; init; }
}

internal sealed record OrcaMailboxDispositionRow
{
    [JsonPropertyName("ask")] public required string Ask { get; init; }
    [JsonPropertyName("disposition")] public required string Disposition { get; init; }
    [JsonPropertyName("evidence")] public required string Evidence { get; init; }
}
