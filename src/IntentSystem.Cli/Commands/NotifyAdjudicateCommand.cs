using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G690's canonical design adjudication surface. The command accepts only a
/// recorded pane plus a live CAS identity, delegates classification and
/// authority to the shared pipeline, and is the only design path that may
/// reach the bounded herdr key executor.
/// </summary>
internal static class NotifyAdjudicateCommand
{
    private const string Usage =
        "Usage: intent-cli notify adjudicate --domain <d> --team <t> --actor-role <role> "
        + "--agent-kind <kind> --prompt-class <class> --pane <pane> --state-sequence <n> "
        + "--text-hash <sha256> --routing-root <host-root> [--cycle-id <id>] "
        + "[--herdr-executable <path>] [--dry-run|--write] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (args is ["--help"])
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var options, out var error))
        {
            writer.WriteLine($"invalid-adjudicate: {error}");
            writer.WriteLine(Usage);
            return 1;
        }

        try
        {
            if (TeamModeStore.Resolve(options.RoutingRoot!, options.Domain!, options.Team!).IsAuthoringOnly)
            {
                return Emit(
                    writer,
                    options,
                    Refused(
                        options.ActorRole,
                        "not-applicable-team-mode",
                        "authoring-only teams have no supervision or worker adjudication lifecycle."));
            }
        }
        catch (InvalidOperationException exception)
        {
            return Emit(writer, options, Refused(options.ActorRole, "team-mode-unreadable", exception.Message));
        }

        var runner = NotifyCommand.ProcessRunnerFactory?.Invoke() ?? new NotifyProcessRunner();
        var executable = options.HerdrExecutable
            ?? NotifyCommand.HerdrExecutableFactory?.Invoke()
            ?? NotifyTransportPaths.ResolveHerdrExecutable();
        var topology = NotifyRoleTopologyStore.Resolve(options.RoutingRoot!, options.Domain!, options.Team!);
        if (!topology.Resolved || topology.Topology is null)
        {
            return Emit(writer, options, Refused(options.ActorRole, "topology-unresolved", topology.Summary));
        }

        var roleRecord = topology.Topology.Roles.Values.FirstOrDefault(role =>
            string.Equals(role.PaneId, options.Pane, StringComparison.Ordinal)
            && string.Equals(role.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal));
        if (roleRecord is null)
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "pane-unrecorded",
                $"Pane '{options.Pane}' is not a recorded herdr pane in the requested topology."));
        }

        var live = ReadLive(runner, executable, topology.Topology.WorkspaceId, options.Pane!);
        if (!live.Resolved || live.Agent is null)
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "live-dialog-unreadable",
                live.Error ?? "The live dialog could not be read."));
        }

        var initialCas = PromptDialogCas.Verify(
            options.Pane!,
            live.Agent.PaneId ?? string.Empty,
            options.StateSequence,
            live.Agent.StateChangeSequence,
            options.TextHash!,
            live.TextHash);
        if (!initialCas.Matches)
        {
            return Emit(writer, options, Refused(options.ActorRole, "stale-dialog-cas-refused", initialCas.Summary));
        }

        var classified = AgentLaunchRecipeRegistry.Classify(options.AgentKind!, live.Text);
        if (!classified.Known || !string.Equals(classified.PromptClass, options.PromptClass, StringComparison.Ordinal))
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "prompt-class-mismatch",
                $"The live pane classified as '{classified.PromptClass}', not the supplied exact class '{options.PromptClass}'."));
        }

        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var state = NotifySupervisionStore.Read(artifactRoot, options.Domain!, options.Team!);
        if (!state.Resolved)
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "supervision-state-unreadable",
                state.Error ?? "Supervision state could not be read."));
        }
        var policy = NotifyPreApprovalPolicyStore.Read(artifactRoot, options.Domain!, options.Team!);
        if (!policy.Resolved)
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "policy-unreadable",
                policy.Error ?? "Pre-approval policy could not be read."));
        }

        // A caller may provide --cycle-id as a compatibility assertion, but it
        // is never an authority input. The current/last recorded supervision
        // cycle is the only identity passed to policy evaluation. A mismatch
        // is refused before the shared pipeline can see the shell payload.
        var trustedCycleId = state.TrustedCycleId;
        if (options.CycleId is not null
            && !string.Equals(options.CycleId, trustedCycleId, StringComparison.Ordinal))
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "cycle-identity-mismatch",
                $"The supplied cycle identity '{options.CycleId}' does not match the recorded current/last supervision cycle '{trustedCycleId ?? "none"}'; caller-supplied wake identities cannot authorize a stale scope."));
        }

        var authorization = PromptAdjudicationPipeline.Evaluate(
            classified,
            policy.Policy,
            options.ActorRole,
            roleRecord.Cwd ?? live.Agent.Cwd,
            state.PromptAudits,
            trustedCycleId);
        if (authorization.Decision != "accept")
        {
            return Emit(writer, options, Result(authorization, live, audited: false, executed: false));
        }

        var auditPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, options.Domain!, options.Team!);
        var audit = new NotifyPromptAudit
        {
            CycleId = trustedCycleId,
            AttemptId = Guid.NewGuid().ToString("N"),
            PromptKey = $"adjudicated-prompt:{topology.Topology.WorkspaceId}:{options.Pane}:{live.TextHash[..16]}",
            Seat = "design-adjudication",
            Pane = options.Pane!,
            AgentKind = options.AgentKind!,
            PromptClass = options.PromptClass!,
            Rule = authorization.Rule,
            Actor = authorization.DecisionActorRole,
            DecisionActorRole = authorization.DecisionActorRole,
            MechanicalExecutor = authorization.MechanicalExecutor,
            ScopeOrRuleId = authorization.ScopeOrRuleId,
            StateChangeSequence = live.Agent.StateChangeSequence,
            ObservedTextHash = live.TextHash,
            Timestamp = Now(),
            Outcome = "authorized-before-execution",
            ExactAnswerScope = authorization.ExactAnswerScope,
            MatchedScopes = authorization.MatchedScopes,
            CommandDigest = authorization.CommandDigest,
            DialogHash = authorization.DialogHash,
        };
        if (!options.Write)
        {
            return Emit(writer, options, Result(authorization, live, audited: false, executed: false)
                with { Summary = authorization.Summary + " Dry-run: no audit or key sequence was written." });
        }

        var initialAudit = NotifySupervisionStore.RecordPromptAudit(auditPath, audit, write: true);
        if (!initialAudit.Applied)
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "audit-write-failed",
                initialAudit.Error ?? "Authorization audit was not appended."));
        }

        var reread = ReadLive(runner, executable, topology.Topology.WorkspaceId, options.Pane!);
        var cas = reread.Resolved && reread.Agent is not null
            ? PromptDialogCas.Verify(
                options.Pane!,
                reread.Agent.PaneId ?? string.Empty,
                live.Agent.StateChangeSequence,
                reread.Agent.StateChangeSequence,
                live.TextHash,
                reread.TextHash)
            : new PromptDialogCasResult
            {
                Matches = false,
                Summary = reread.Error ?? "The live dialog could not be reread before execution.",
            };
        if (!cas.Matches)
        {
            _ = NotifySupervisionStore.RecordPromptAudit(
                auditPath,
                audit with { Timestamp = Now(), Outcome = "stale-dialog-cas-refused", Rule = audit.Rule + " (stale-dialog-cas-refused)" },
                write: true);
            return Emit(writer, options, Result(authorization, live, audited: true, executed: false)
                with { Summary = authorization.Summary + $" No key was sent: {cas.Summary}" },
                exitCode: 1);
        }

        var pending = NotifySupervisionStore.RecordPromptAudit(
            auditPath,
            audit with { Timestamp = Now(), Outcome = "bounded-answer-execution-pending" },
            write: true);
        if (!pending.Applied)
        {
            return Emit(writer, options, Refused(
                options.ActorRole,
                "execution-pending-audit-failed",
                pending.Error ?? "Execution-pending audit was not appended."));
        }

        NotifyProcessResult execution;
        try
        {
            execution = runner.Run(executable, ["agent", "send-keys", options.Pane!, .. authorization.AnswerKeys]);
        }
        catch (InvalidOperationException exception)
        {
            execution = new NotifyProcessResult(1, string.Empty, exception.Message);
        }
        var outcome = execution.ExitCode == 0 ? "bounded-answer-executed" : "bounded-answer-failed";
        var terminalAudit = NotifySupervisionStore.RecordPromptAudit(
            auditPath,
            audit with { Timestamp = Now(), Outcome = outcome },
            write: true);
        if (!terminalAudit.Applied)
        {
            return Emit(writer, options, Result(authorization, live, audited: true, executed: execution.ExitCode == 0)
                with
                {
                    Decision = "escalate",
                    Rule = authorization.Rule + " (terminal-audit-write-failed)",
                    Summary = $"The bounded answer {(execution.ExitCode == 0 ? "was sent" : "failed")}, but its terminal audit could not be durably appended: {terminalAudit.Error ?? "not applied"}. Reconciliation is required.",
                    MechanicalExecutor = null,
                },
                exitCode: 1);
        }
        return Emit(writer, options, Result(authorization, live, audited: true, executed: execution.ExitCode == 0)
            with
            {
                Summary = execution.ExitCode == 0
                    ? authorization.Summary + $" Executed only registry keys [{string.Join(", ", authorization.AnswerKeys)}]."
                    : authorization.Summary + $" The bounded answer failed: {execution.StandardError}",
            },
            exitCode: execution.ExitCode == 0 ? 0 : 1);
    }

    private static DateTimeOffset Now() =>
        (NotifyCommand.UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

    private static AdjudicationResult Result(
        PromptAdjudicationAuthorization authorization,
        LiveDialog live,
        bool audited,
        bool executed) => new()
    {
        Decision = authorization.Decision,
        Rule = authorization.Rule,
        Summary = authorization.Summary,
        ActorRole = authorization.DecisionActorRole,
        MechanicalExecutor = authorization.MechanicalExecutor,
        AnswerableBy = authorization.AnswerableBy,
        RiskTags = authorization.RiskTags,
        ScopeOrRuleId = authorization.ScopeOrRuleId,
        StateChangeSequence = live.Agent?.StateChangeSequence,
        ObservedTextHash = live.TextHash,
        Audited = audited,
        Executed = executed,
    };

    private static AdjudicationResult Refused(string? actorRole, string rule, string summary) => new()
    {
        Decision = "escalate",
        Rule = rule,
        Summary = summary,
        ActorRole = NormalizeActorRole(actorRole),
        Audited = false,
        Executed = false,
    };

    private static LiveDialog ReadLive(
        INotifyProcessRunner runner,
        string executable,
        string workspaceId,
        string pane)
    {
        NotifyProcessResult roster;
        try
        {
            roster = runner.Run(executable, ["agent", "list"]);
        }
        catch (InvalidOperationException exception)
        {
            return LiveDialog.Failure(exception.Message);
        }
        if (roster.ExitCode != 0)
        {
            return LiveDialog.Failure("herdr agent list failed before adjudication.");
        }

        IReadOnlyList<HerdrAgentState> agents;
        try
        {
            agents = HerdrNotifyTransport.ParseAgents(roster.StandardOutput);
        }
        catch (InvalidOperationException exception)
        {
            return LiveDialog.Failure(exception.Message);
        }
        var agent = agents.SingleOrDefault(candidate =>
            string.Equals(candidate.WorkspaceId, workspaceId, StringComparison.Ordinal)
            && string.Equals(candidate.PaneId, pane, StringComparison.Ordinal));
        if (agent is null || !agent.AgentRunning)
        {
            return LiveDialog.Failure($"Recorded pane '{pane}' is not a running herdr seat in workspace '{workspaceId}'.");
        }

        NotifyProcessResult read;
        try
        {
            read = runner.Run(executable, ["agent", "read", pane, "--source", "detection", "--lines", "200"]);
        }
        catch (InvalidOperationException exception)
        {
            return LiveDialog.Failure(exception.Message);
        }
        if (read.ExitCode != 0 || string.IsNullOrWhiteSpace(read.StandardOutput))
        {
            return LiveDialog.Failure("herdr agent read returned no live dialog text.");
        }

        var text = read.StandardOutput.Trim();
        return new LiveDialog
        {
            Resolved = true,
            Agent = agent,
            Text = text,
            TextHash = PromptDialogCas.HashText(text),
        };
    }

    private static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new Options();
        string? domain = null, team = null, actor = null, kind = null, prompt = null, pane = null;
        string? routingRoot = null, herdr = null, cycle = null, hash = null;
        long? sequence = null;
        var write = false;
        var format = "markdown";
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is "--write" or "--dry-run")
            {
                write = argument == "--write";
                continue;
            }
            if (argument == "--format")
            {
                if (!Read(args, ref index, out format, out error)
                    || format is not ("json" or "markdown"))
                {
                    error = "--format must be markdown or json.";
                    return false;
                }
                continue;
            }
            if (!Read(args, ref index, out var value, out error))
            {
                return false;
            }
            switch (argument)
            {
                case "--domain": domain = value; break;
                case "--team": team = value; break;
                case "--actor-role": actor = value; break;
                case "--agent-kind": kind = value; break;
                case "--prompt-class": prompt = value; break;
                case "--pane": pane = value; break;
                case "--routing-root": routingRoot = value; break;
                case "--herdr-executable": herdr = value; break;
                case "--cycle-id": cycle = value; break;
                case "--text-hash": hash = value; break;
                case "--state-sequence":
                    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    {
                        error = "--state-sequence must be a non-negative integer.";
                        return false;
                    }
                    sequence = parsed;
                    break;
                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        var missing = new (string Name, string? Value)[]
        {
            ("--domain", domain), ("--team", team), ("--actor-role", actor), ("--agent-kind", kind),
            ("--prompt-class", prompt), ("--pane", pane), ("--routing-root", routingRoot), ("--text-hash", hash),
        }.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Value));
        if (missing.Name is not null)
        {
            error = $"{missing.Name} is required.";
            return false;
        }
        if (sequence is null)
        {
            error = "--state-sequence is required for live-dialog CAS.";
            return false;
        }
        if (hash!.Length != 64 || !hash.All(char.IsAsciiHexDigit))
        {
            error = "--text-hash must be a 64-character SHA-256 value.";
            return false;
        }
        if (cycle is not null && !IsSafeCycleIdentity(cycle))
        {
            error = "--cycle-id must be a non-empty safe wake/cycle identity.";
            return false;
        }
        if (new[] { domain!, team!, actor!, kind!, prompt!, pane! }.Any(value => !Safe(value)))
        {
            error = "role, domain, team, agent kind, prompt class, and pane values must be safe identifiers.";
            return false;
        }

        options = new Options
        {
            Domain = domain,
            Team = team,
            ActorRole = actor,
            AgentKind = kind,
            PromptClass = prompt,
            Pane = pane,
            RoutingRoot = Path.GetFullPath(routingRoot!),
            HerdrExecutable = herdr,
            CycleId = cycle,
            StateSequence = sequence.Value,
            TextHash = hash.ToLowerInvariant(),
            Write = write,
            Format = format,
        };
        return true;
    }

    private static bool Read(string[] args, ref int index, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;
        if (index + 1 >= args.Length)
        {
            error = $"{args[index]} requires a value.";
            return false;
        }
        value = args[++index];
        return true;
    }

    private static bool Safe(string value) =>
        value.Length > 0 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or ':');

    private static int Emit(TextWriter writer, Options options, AdjudicationResult result, int? exitCode = null)
    {
        var processExitCode = exitCode
            ?? (string.Equals(result.Decision, "accept", StringComparison.Ordinal) ? 0 : 1);
        if (options.Format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                command = "notify adjudicate",
                exit_code = processExitCode,
                result,
            }, JsonOptions));
        }
        else
        {
            writer.WriteLine("# Prompt adjudication");
            writer.WriteLine();
            writer.WriteLine($"- exit-code: {processExitCode}");
            writer.WriteLine($"- decision: {result.Decision}");
            writer.WriteLine($"- rule: {result.Rule}");
            writer.WriteLine($"- actor: {result.ActorRole}");
            writer.WriteLine($"- mechanical executor: {result.MechanicalExecutor ?? "none"}");
            writer.WriteLine($"- audited: {result.Audited}; executed: {result.Executed}");
            writer.WriteLine($"- summary: {result.Summary}");
        }
        return processExitCode;
    }

    private static string NormalizeActorRole(string? actorRole) =>
        string.IsNullOrWhiteSpace(actorRole) ? "unknown" : actorRole.Trim().ToLowerInvariant();

    private static bool IsSafeCycleIdentity(string value) =>
        value.Length > 0
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private sealed record Options
    {
        public string? Domain { get; init; }
        public string? Team { get; init; }
        public string? ActorRole { get; init; }
        public string? AgentKind { get; init; }
        public string? PromptClass { get; init; }
        public string? Pane { get; init; }
        public string? RoutingRoot { get; init; }
        public string? HerdrExecutable { get; init; }
        public string? CycleId { get; init; }
        public long StateSequence { get; init; }
        public string? TextHash { get; init; }
        public bool Write { get; init; }
        public string Format { get; init; } = "markdown";
    }

    private sealed record LiveDialog
    {
        public required bool Resolved { get; init; }
        public HerdrAgentState? Agent { get; init; }
        public string Text { get; init; } = string.Empty;
        public string TextHash { get; init; } = string.Empty;
        public string? Error { get; init; }

        public static LiveDialog Failure(string error) => new() { Resolved = false, Error = error };
    }

    private sealed record AdjudicationResult
    {
        public required string Decision { get; init; }
        public required string Rule { get; init; }
        public required string Summary { get; init; }
        public required string ActorRole { get; init; }
        public string? MechanicalExecutor { get; init; }
        public string? AnswerableBy { get; init; }
        public IReadOnlyList<string> RiskTags { get; init; } = [];
        public string? ScopeOrRuleId { get; init; }
        public long? StateChangeSequence { get; init; }
        public string? ObservedTextHash { get; init; }
        public bool Audited { get; init; }
        public bool Executed { get; init; }
    }
}
