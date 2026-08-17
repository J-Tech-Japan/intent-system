using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G664: render-only genesis guide for an application conversation. The
/// guide composes existing herdr, topology, supervision-install, design-seat,
/// and notify surfaces; it never invokes any of them.
/// </summary>
internal static class GuideBootstrapCommand
{
    public const string CommandName = "intent-cli guide bootstrap";
    public const string TriggerEnglish = "Start this work in a herdr-only team.";
    public const string TriggerJapanese = "herdr-only で起動して。";
    private const string UsageLine =
        "Usage: intent-cli guide bootstrap [--domain <name>] [--team <team>] [--target-repo <owner/repo>] [--routing-root <path>] [--format markdown|json]";

    private static readonly string[] ExpectedRoles = ["design", "orchestration", "implementation", "review"];

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            writer.WriteLine("Read-only G664 bootstrap composition guide. Preview-through-1.x; emits commands and questions, but executes no herdr, OS, scheduler, provider, or application operation.");
            return 0;
        }

        if (!TryParse(args, out var domain, out var team, out var targetRepo, out var routingRoot, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var root = string.IsNullOrWhiteSpace(routingRoot) ? context.RepoRoot : Path.GetFullPath(routingRoot);
        var result = BuildResult(context, root, domain, team, targetRepo);
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    internal static BootstrapGuideResult BuildResult(
        CliContext context,
        string routingRoot,
        string? domain,
        string? team,
        string? targetRepo)
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain.Trim();
        var teamArg = string.IsNullOrWhiteSpace(team) ? "<team>" : team.Trim();
        var repoArg = string.IsNullOrWhiteSpace(targetRepo) ? "<owner/repo>" : targetRepo.Trim();
        if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(team))
        {
            var teamMode = TeamModeStore.Resolve(routingRoot, domain.Trim(), team.Trim());
            if (teamMode.IsAuthoringOnly)
            {
                return BuildAuthoringOnlyResult(routingRoot, domain.Trim(), team.Trim(), targetRepo, teamMode);
            }
        }

        var state = InspectState(context, routingRoot, domain, team);

        return new BootstrapGuideResult
        {
            Process = "application-front-door-team-bootstrap",
            PreviewStatus = "preview-through-1.x",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim(),
            Team = string.IsNullOrWhiteSpace(team) ? null : team.Trim(),
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo.Trim(),
            RoutingRoot = routingRoot,
            TriggerPhrases = new BootstrapTriggerPhrases { English = TriggerEnglish, Japanese = TriggerJapanese },
            SessionLayerCoverage = ["agmsg", "herdr-only"],
            TargetSessionLayer = "herdr-only",
            TeamFormula = "four judgment-bearing threads plus one supervision process",
            State = state,
            Flow = state.TopologyRecorded ? "join-and-delegate" : "create-and-delegate",
            Reachability = new BootstrapReachability
            {
                Command = CommandName,
                Catalog = "intent-cli guide commands list --format json",
                Advisor = $"intent-cli guide next --domain {domainArg} --team {teamArg} --target-repo {repoArg} --format json",
            },
            ModelResolution = new BootstrapModelResolution
            {
                PreviewStatus = AgentModelResolutionGuidance.PreviewStatus,
                ResolutionOrder = AgentModelResolutionGuidance.ResolutionOrder,
                NeverGuessRule = AgentModelResolutionGuidance.NeverGuessRule,
                QueryCommand = AgentModelResolutionGuidance.QueryCommand,
                RecordCommand = AgentModelResolutionGuidance.RecordCommand,
                Incident = AgentModelResolutionGuidance.Incident,
                LiveArgvFallback = AgentModelResolutionGuidance.LiveArgvFallback,
                LaunchEvidenceWorkflow = AgentModelResolutionGuidance.LaunchEvidenceWorkflow,
            },
            Steps =
            [
                new BootstrapStep
                {
                    Number = 1,
                    Id = "ask-seat-cli-and-model",
                    Instruction = "Ask the human which CLI and informal model/effort each design, orchestration, implementation, and review seat should run. Resolve each answer in exactly this order: host-local ledger hit, currently-running same-kind seat argv, then ask the human for the full invocation. Never guess a bare id or consult a shipped list.",
                    EmittedCommands =
                    [
                        AgentModelResolutionGuidance.QueryCommand,
                        AgentModelResolutionGuidance.LiveArgvFallback.ListCommand,
                        AgentModelResolutionGuidance.LiveArgvFallback.InspectCommand,
                    ],
                },
                new BootstrapStep
                {
                    Number = 2,
                    Id = "emit-workspace-pane-and-seat-commands",
                    Instruction = state.TopologyRecorded
                        ? "Join the recorded workspace. Emit only commands for named missing seats or fields; do not recreate the workspace or already-recorded seats. Use the installed per-kind recipe and the G637 layout guide."
                        : "Emit the herdr workspace, pane, and typed-seat commands from the installed per-kind recipes and G637 layout convention. Resolve every non-empty id from command output before emitting the next targeted command.",
                    EmittedCommands =
                    [
                        "herdr workspace create --cwd <host-repo> --label <team> --no-focus",
                        "herdr pane split --pane <pane-id> --direction right|down --cwd <role-cwd> --no-focus",
                        "herdr agent start <logical-role> --kind <human-chosen-cli-kind> --pane <pane-id> -- <human-approved-recipe-flags-and-model>",
                        AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Verified.Command,
                        AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Refused.Command,
                        "intent-cli guide workspace-layout --workspace-id <workspace-id> --tab-id <tab-id> --shape <observed-shape> --format markdown",
                    ],
                },
                new BootstrapStep
                {
                    Number = 3,
                    Id = "record-topology",
                    Instruction = "Record operator-supplied workspace, pane, cwd, kind, and reader facts through the canonical topology writer, once per role; validate and show the finished roster. Exact repeats are idempotent and conflicts fail closed.",
                    EmittedCommands =
                    [
                        $"intent-cli session-layer topology record --domain {domainArg} --team {teamArg} --role <role> --resident herdr --workspace-id <workspace-id> --pane-id <pane-id> --cwd <role-cwd> --kind <human-chosen-kind> --write --format json",
                        $"intent-cli session-layer topology validate --domain {domainArg} --team {teamArg} --format json",
                        $"intent-cli session-layer topology show --domain {domainArg} --team {teamArg} --format json",
                    ],
                },
                new BootstrapStep
                {
                    Number = 4,
                    Id = "emit-supervision-install",
                    Instruction = state.SupervisionCycleRecorded
                        ? "Keep the existing per-team supervision installation; do not emit or register a duplicate."
                        : $"Emit the current-platform supervision artifact and exact current-session registration/unregistration commands. {SupervisionGuideText.SessionLifetimeRule} {SupervisionGuideText.InstallBoundRule} {SupervisionGuideText.InstallArtifactRule} {SupervisionGuideText.InstallEvidenceRule} The human may register it for the current GUI session; use reconcile/uninstall to unload and remove drift.",
                    EmittedCommands = state.SupervisionCycleRecorded
                        ? []
                        : [$"intent-cli notify supervise install --domain {domainArg} --team {teamArg} --repo {repoArg} --owner-role orchestration --bound <seconds> --interval <seconds> --startup-bound <seconds> --write --format json"],
                },
                new BootstrapStep
                {
                    Number = 5,
                    Id = "ask-app-kind-and-place-design",
                    Instruction = "Ask the human which agent kind this application conversation uses and whether that kind has an inbound application monitor. Never infer or default either answer. With a monitor, design may be the recorded external reader; without one, design must be a recorded herdr seat at the routing-root cwd. This placement rule does not move recovery authority from orchestration.",
                    EmittedCommands =
                    [
                        $"intent-cli session-layer topology record --domain {domainArg} --team {teamArg} --role design --resident <external-or-herdr-from-human-answer> <reader-or-workspace-pane-and-cwd-fields> --write --format json",
                        $"intent-cli guide design-thread --domain {domainArg} --team {teamArg} --routing-root {routingRoot} --format markdown",
                    ],
                },
                new BootstrapStep
                {
                    Number = 6,
                    Id = "delegate-first-task",
                    Instruction = "After the recorded topology validates and the chosen seats are ready, delegate the first task to orchestration with a fresh task id/result nonce. Do not run it in the app conversation.",
                    EmittedCommands =
                    [
                        $"intent-cli notify delegate --domain {domainArg} --team {teamArg} --from design --to orchestration --task-id <task-id> --result-nonce <fresh-result-nonce> --message <first-task> --report-to design --routing-root {routingRoot} --write --format json",
                    ],
                },
            ],
            PartialStateRule = "Name recorded facts and missing facts. Never re-emit the whole bootstrap merely because one seat, field, supervision cycle, or handoff step is missing.",
            NoExecutionBoundary =
            [
                "This guide renders questions and command text only. intent-cli does not execute herdr, launch a provider, start a seat, register/unregister a scheduler artifact, or run an OS command.",
                "No application-side integration is installed or generated. The application conversation reads the guide and remains the operator-facing entry point.",
                "Recorded recipes, the G654 design deployment rule, the four-thread-plus-one-process formula, and preview boundaries are composed unchanged.",
            ],
            FinalHandoffStatement = "HANDOFF: State which recorded thread is now the design seat. The application conversation remains the operator's front door for new requests; it is not a design, orchestration, implementation, review, or supervision loop seat.",
        };
    }

    private static BootstrapGuideResult BuildAuthoringOnlyResult(
        string routingRoot,
        string domain,
        string team,
        string? targetRepo,
        TeamModeResolution teamMode)
    {
        var domainArg = domain;
        var teamArg = team;
        var repoArg = string.IsNullOrWhiteSpace(targetRepo) ? "<owner/repo>" : targetRepo.Trim();
        return new BootstrapGuideResult
        {
            Process = "authoring-only-team-bootstrap",
            PreviewStatus = "preview-through-1.x",
            Domain = domain,
            Team = team,
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo.Trim(),
            RoutingRoot = routingRoot,
            TeamMode = TeamMode.AuthoringOnly,
            TriggerPhrases = new BootstrapTriggerPhrases
            {
                English = "Start this work in an authoring-only team.",
                Japanese = "authoring-only チームとして進めて。",
            },
            SessionLayerCoverage = ["transport-independent"],
            TargetSessionLayer = "not-applicable-team-mode",
            TeamFormula = "authoring front door plus repository, claim, and publish prerequisites; no delivery seats",
            State = BuildAuthoringOnlyState(teamMode),
            Flow = "authoring-only",
            Reachability = new BootstrapReachability
            {
                Command = CommandName,
                Catalog = "intent-cli guide commands list --format json",
                Advisor = $"intent-cli guide next --domain {domainArg} --team {teamArg} --target-repo {repoArg} --format json",
            },
            // Null is intentional for authoring-only and is omitted by the
            // command's WhenWritingNull serializer policy. Keep the public
            // property non-nullable so existing delivery callers retain the
            // original G685 API shape.
            ModelResolution = null!,
            Steps =
            [
                new BootstrapStep
                {
                    Number = 1,
                    Id = "accept-authoring-front-door",
                    Instruction = "Ask the operator to accept the authoring-only front door as the place where intent shape, packet authorship, and issue publication decisions are made.",
                    EmittedCommands = [],
                },
                new BootstrapStep
                {
                    Number = 2,
                    Id = "verify-repository-prerequisite",
                    Instruction = "Verify access to the target repository and the ordinary issue-authoring boundary. Do not create delivery seats or a delivery topology.",
                    EmittedCommands =
                    [
                        $"gh repo view {repoArg} --json nameWithOwner,defaultBranchRef",
                        $"intent-cli claim verify --scope release-prep:{repoArg}:authoring --team {teamArg} --format json",
                    ],
                },
                new BootstrapStep
                {
                    Number = 3,
                    Id = "author-packet",
                    Instruction = "Shape or interview the intent, then author a standalone packet whose issue body carries the complete contract. Keep the packet boundary explicit before publication.",
                    EmittedCommands =
                    [
                        $"intent-cli grill --domain {domainArg} --format markdown",
                        $"intent-cli packet draft --target-repo {repoArg} --format markdown",
                    ],
                },
                new BootstrapStep
                {
                    Number = 4,
                    Id = "publish-issue",
                    Instruction = "After operator acceptance and the repository/claim prerequisites pass, publish the reviewed issue through the canonical issue boundary. The resulting issue is the handoff artifact.",
                    EmittedCommands =
                    [
                        $"intent-cli issue publish-flow <packet-id> --repo {repoArg} --write --format json",
                    ],
                },
            ],
            PartialStateRule = "Authoring-only completion is measured from the recorded team mode and front-door shape; repository, claim, and publish commands are rendered operator prerequisites, not missing delivery facts. Never add delivery topology or a delivery lifecycle to this bootstrap.",
            NoExecutionBoundary =
            [
                "This guide renders authoring questions and command text only; it does not create delivery seats, start a delivery lifecycle, or execute an external process.",
                "The target repository and issue boundary remain explicit operator prerequisites; no transport selection is inferred from team mode.",
                "Issue publication remains the only emitted handoff artifact for this team shape.",
            ],
            FinalHandoffStatement = "HANDOFF: The authoring-only front door owns the accepted packet until the canonical issue is published; no delivery lifecycle is part of this bootstrap.",
        };
    }

    private static BootstrapGuideState BuildAuthoringOnlyState(TeamModeResolution teamMode)
    {
        var measured = teamMode.IsAuthoringOnly && teamMode.Source == TeamModeSource.Recorded;
        return new BootstrapGuideState
        {
            Name = measured ? "authoring-only-complete" : "authoring-only-unreadable",
            Inspected = measured,
            TopologyRecorded = false,
            TopologyResolved = false,
            SupervisionCycleRecorded = false,
            Complete = measured,
            CompletionBasis = measured
                ? "recorded team_mode=authoring-only is the durable acceptance of the front-door team shape; repository, claim, and publish checks remain explicit operator actions."
                : "authoring-only completion requires a recorded, readable team_mode entry.",
            ExistingFacts = measured
                ? ["recorded team_mode=authoring-only", "authoring front door is the operator entry point", "authoring-only bootstrap shape is complete without delivery topology"]
                : [],
            MissingFacts = measured ? [] : ["recorded readable team_mode=authoring-only"],
        };
    }

    internal static BootstrapGuideState InspectState(CliContext context, string routingRoot, string? domain, string? team)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            return new BootstrapGuideState
            {
                Name = "team-not-selected",
                Inspected = false,
                TopologyRecorded = false,
                SupervisionCycleRecorded = false,
                Complete = false,
                ExistingFacts = [],
                MissingFacts = ["explicit domain and team", "recorded topology", "completed supervision cycle", "explicit handoff"],
            };
        }

        var domainValue = domain.Trim();
        var teamValue = team.Trim();
        var teamMode = TeamModeStore.Resolve(routingRoot, domainValue, teamValue);
        if (teamMode.IsAuthoringOnly)
        {
            return BuildAuthoringOnlyState(teamMode);
        }

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(routingRoot, domainValue, teamValue);
        var topologyRecorded = File.Exists(topologyPath);
        var resolution = NotifyRoleTopologyStore.Resolve(routingRoot, domainValue, teamValue);
        var roles = resolution.Topology?.Roles.Keys.ToHashSet(StringComparer.Ordinal) ?? [];
        var missingRoles = ExpectedRoles.Where(role => !roles.Contains(role)).ToArray();
        var supervision = NotifySupervisionStore.Read(context.ResolveSupervisionArtifactRootPath(), domainValue, teamValue);
        var cycleRecorded = supervision.Resolved && supervision.LastCycle is not null;

        var existing = new List<string>();
        if (topologyRecorded) existing.Add($"topology record `{topologyPath}`");
        foreach (var role in ExpectedRoles.Where(roles.Contains)) existing.Add($"recorded `{role}` seat");
        if (cycleRecorded) existing.Add($"completed supervision cycle `{supervision.LastCycle!.CycleId}`");

        var missing = new List<string>();
        if (!topologyRecorded) missing.Add("recorded topology");
        foreach (var role in missingRoles) missing.Add($"recorded `{role}` seat");
        if (!cycleRecorded) missing.Add("completed supervision cycle and explicit application-front-door handoff");

        var rosterComplete = resolution.Resolved && missingRoles.Length == 0;
        var complete = rosterComplete && cycleRecorded;
        var name = !topologyRecorded
            ? "new-team"
            : !rosterComplete
                ? "topology-recorded-seats-missing"
                : !cycleRecorded
                    ? "topology-recorded-supervision-and-handoff-missing"
                    : "complete-join-and-delegate";

        return new BootstrapGuideState
        {
            Name = name,
            Inspected = true,
            TopologyRecorded = topologyRecorded,
            TopologyResolved = resolution.Resolved,
            SupervisionCycleRecorded = cycleRecorded,
            Complete = complete,
            TopologyPath = topologyPath,
            ExistingFacts = existing,
            MissingFacts = missing,
            ReadError = !resolution.Resolved && topologyRecorded ? resolution.Summary : !supervision.Resolved ? supervision.Error : null,
        };
    }

    private static void WriteMarkdown(TextWriter writer, BootstrapGuideResult result)
    {
        writer.WriteLine("# Application-front-door team bootstrap (G664)");
        writer.WriteLine();
        writer.WriteLine($"- status: **{result.PreviewStatus}**");
        writer.WriteLine($"- English trigger: **{result.TriggerPhrases.English}**");
        writer.WriteLine($"- 日本語トリガー: **{result.TriggerPhrases.Japanese}**");
        writer.WriteLine($"- flow: **{result.Flow}**");
        writer.WriteLine($"- named state: **{result.State.Name}**");
        writer.WriteLine($"- session-layer coverage: **{string.Join(" / ", result.SessionLayerCoverage)}**");
        writer.WriteLine($"- target session layer: **{result.TargetSessionLayer}** (guide renders from either recorded mode)");
        writer.WriteLine($"- formula: **{result.TeamFormula}**");
        writer.WriteLine();
        writer.WriteLine("## Recorded and missing facts");
        foreach (var fact in result.State.ExistingFacts) writer.WriteLine($"- exists: {fact}");
        foreach (var fact in result.State.MissingFacts) writer.WriteLine($"- missing: {fact}");
        if (result.State.CompletionBasis is not null) writer.WriteLine($"- completion basis: {result.State.CompletionBasis}");
        if (result.State.ReadError is not null) writer.WriteLine($"- state-read warning: {result.State.ReadError}");
        writer.WriteLine($"- {result.PartialStateRule}");
        writer.WriteLine();
        writer.WriteLine("## Reachability");
        writer.WriteLine($"- command: `{result.Reachability.Command}`");
        writer.WriteLine($"- catalog: `{result.Reachability.Catalog}`");
        writer.WriteLine($"- half-done advisor: `{result.Reachability.Advisor}`");
        writer.WriteLine();
        writer.WriteLine("## Guided pass — perform in this order");
        if (result.ModelResolution is { } modelResolution)
        {
            writer.WriteLine("### Model/effort resolution (G685)");
            writer.WriteLine($"- status: **{modelResolution.PreviewStatus}**");
            foreach (var item in modelResolution.ResolutionOrder) writer.WriteLine($"- {item}");
            writer.WriteLine($"- {modelResolution.NeverGuessRule}");
            writer.WriteLine($"- query: `{modelResolution.QueryCommand}`");
            writer.WriteLine($"- live selection: {modelResolution.LiveArgvFallback.Selection}");
            writer.WriteLine($"- live list (read-only): `{modelResolution.LiveArgvFallback.ListCommand}`");
            writer.WriteLine($"- argv inspection (read-only): `{modelResolution.LiveArgvFallback.InspectCommand}`");
            writer.WriteLine($"- argv field: `{modelResolution.LiveArgvFallback.ArgvPath}`");
            writer.WriteLine($"- agreement: {modelResolution.LiveArgvFallback.AgreementRule}");
            writer.WriteLine($"- human fallback: {modelResolution.LiveArgvFallback.HumanFallback}");
            writer.WriteLine($"- **mandatory launch evidence:** {modelResolution.LaunchEvidenceWorkflow.Rule}");
            writer.WriteLine($"- verified READY record: `{modelResolution.LaunchEvidenceWorkflow.Verified.Command}`");
            writer.WriteLine($"- refusal record: `{modelResolution.LaunchEvidenceWorkflow.Refused.Command}`");
            writer.WriteLine($"- incident: {modelResolution.Incident}");
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine("Authoring-only mode: perform only front-door acceptance, repository/claim checks, packet authoring, and issue publication.");
            writer.WriteLine();
        }
        foreach (var step in result.Steps)
        {
            writer.WriteLine($"### {step.Number}. {step.Id}");
            writer.WriteLine(step.Instruction);
            foreach (var command in step.EmittedCommands) writer.WriteLine($"- emit only: `{command}`");
            writer.WriteLine();
        }
        writer.WriteLine("## No-execution boundary");
        foreach (var item in result.NoExecutionBoundary) writer.WriteLine($"- {item}");
        writer.WriteLine();
        writer.WriteLine(result.FinalHandoffStatement);
    }

    private static bool TryParse(string[] args, out string? domain, out string? team, out string? targetRepo, out string? routingRoot, out string format, out string error)
    {
        domain = team = targetRepo = routingRoot = null;
        format = "markdown";
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument is not ("--domain" or "--team" or "--target-repo" or "--routing-root" or "--format"))
            {
                error = $"Unknown argument '{argument}'.";
                return false;
            }
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            {
                error = $"{argument} requires a value.";
                return false;
            }
            var value = args[index].Trim();
            if (argument == "--domain") domain = value;
            else if (argument == "--team") team = value;
            else if (argument == "--target-repo") targetRepo = value;
            else if (argument == "--routing-root") routingRoot = value;
            else if (value is "markdown" or "json") format = value;
            else
            {
                error = $"--format must be 'markdown' or 'json' (got '{value}').";
                return false;
            }
        }
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record BootstrapGuideResult
{
    public required string Process { get; init; }
    public required string PreviewStatus { get; init; }
    public string? Domain { get; init; }
    public string? Team { get; init; }
    public string? TargetRepo { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TeamMode { get; init; }
    public required string RoutingRoot { get; init; }
    public required BootstrapTriggerPhrases TriggerPhrases { get; init; }
    public required IReadOnlyList<string> SessionLayerCoverage { get; init; }
    public required string TargetSessionLayer { get; init; }
    public required string TeamFormula { get; init; }
    public required BootstrapGuideState State { get; init; }
    public required string Flow { get; init; }
    public required BootstrapReachability Reachability { get; init; }
    public required BootstrapModelResolution ModelResolution { get; init; }
    public required IReadOnlyList<BootstrapStep> Steps { get; init; }
    public required string PartialStateRule { get; init; }
    public required IReadOnlyList<string> NoExecutionBoundary { get; init; }
    public required string FinalHandoffStatement { get; init; }
}

internal sealed record BootstrapModelResolution
{
    public required string PreviewStatus { get; init; }
    public required IReadOnlyList<string> ResolutionOrder { get; init; }
    public required string NeverGuessRule { get; init; }
    public required string QueryCommand { get; init; }
    public required string RecordCommand { get; init; }
    public required string Incident { get; init; }
    public required AgentLiveArgvFallback LiveArgvFallback { get; init; }
    public required AgentLaunchEvidenceWorkflow LaunchEvidenceWorkflow { get; init; }
}

internal sealed record BootstrapGuideState
{
    public required string Name { get; init; }
    public required bool Inspected { get; init; }
    public required bool TopologyRecorded { get; init; }
    public bool TopologyResolved { get; init; }
    public required bool SupervisionCycleRecorded { get; init; }
    public required bool Complete { get; init; }
    public string? CompletionBasis { get; init; }
    public string? TopologyPath { get; init; }
    public required IReadOnlyList<string> ExistingFacts { get; init; }
    public required IReadOnlyList<string> MissingFacts { get; init; }
    public string? ReadError { get; init; }
}

internal sealed record BootstrapTriggerPhrases
{
    public required string English { get; init; }
    public required string Japanese { get; init; }
}

internal sealed record BootstrapReachability
{
    public required string Command { get; init; }
    public required string Catalog { get; init; }
    public required string Advisor { get; init; }
}

internal sealed record BootstrapStep
{
    public required int Number { get; init; }
    public required string Id { get; init; }
    public required string Instruction { get; init; }
    public required IReadOnlyList<string> EmittedCommands { get; init; }
}
