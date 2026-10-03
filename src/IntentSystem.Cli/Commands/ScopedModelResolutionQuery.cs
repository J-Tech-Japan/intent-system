using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record ScopedModelResolutionQueryScope(
    string Domain,
    string Team,
    string Role,
    string Kind,
    string? WorkspaceId,
    string? PaneId,
    IReadOnlyList<string>? RoleAliases,
    string? TopologyDigest);

internal sealed record ScopedModelResolutionQueryRequest(
    string Form,
    string InformalName,
    string? RequestedModel,
    string RequestedEffort);

internal sealed record ScopedModelResolutionQueryObservedEvidence(
    string Host,
    long Pid,
    string ProcessStartTimeUtc,
    string IdentitySource,
    string TopologyDigest,
    IReadOnlyList<string> RoleAliases,
    IReadOnlyList<string> Argv,
    string Model,
    string Effort);

internal sealed record ScopedModelResolutionQueryResult
{
    public required string Operation { get; init; }
    public required bool Resolved { get; init; }
    public required bool HumanRequired { get; init; }
    public required string Reason { get; init; }
    public required ScopedModelResolutionQueryScope Scope { get; init; }
    public required ScopedModelResolutionQueryRequest Request { get; init; }
    public ScopedModelResolutionQueryObservedEvidence? ObservedEvidence { get; init; }
    public required string NextStep { get; init; }
    public required string ProviderOperation { get; init; }
    public string? RecordPath { get; init; }
    public string? Error { get; init; }
}

/// <summary>Read-only scoped query form for G814. There is deliberately no append seam.</summary>
internal static class ScopedModelResolutionQuery
{
    private const string Usage =
        "Usage: intent-cli session-layer model-resolution query --routing-root <absolute-host-root> --domain <d> --team <t> "
        + "--role <role> --kind <codex|claude> --informal-name <name> --requested-effort <effort> "
        + "[--requested-model <id>] [--candidate-invocation <full-invocation>] --format json|markdown";

    private static readonly HashSet<string> ScopeArguments = new(StringComparer.Ordinal)
    {
        "--routing-root", "--domain", "--team", "--role", "--requested-effort", "--requested-model",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static Func<string, ModelResolutionLedgerReadResult>? LedgerReadOverride { get; set; }
    internal static Func<string, string, string, NotifyTopologyResolution>? TopologyReadOverride { get; set; }
    internal static Func<string, NotifyTeamTopology, string, string, string, string,
        Task<ScopedModelResolutionObservationResult>>? ObserverOverride { get; set; }

    public static bool HasScopedArguments(IReadOnlyList<string> args) => args.Any(ScopeArguments.Contains);

    public static async Task<int> ExecuteAsync(CliContext context, string[] args, TextWriter writer)
    {
        if (!TryParse(args, out var parsedValue, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }
        var parsed = parsedValue!;

        ModelResolutionLedgerReadResult read;
        try
        {
            read = LedgerReadOverride?.Invoke(parsed.RoutingRoot)
                ?? ModelResolutionLedgerStore.Read(parsed.RoutingRoot);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            var failure = CreateResult(parsed, null, null, "ledger-unreadable", "repair-ledger-read", null, exception.Message);
            Emit(writer, parsed.Format, failure);
            return 1;
        }

        if (!read.Resolved)
        {
            var failure = CreateResult(parsed, null, null, "ledger-unreadable", "repair-ledger-read", null, read.Error);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 1;
        }

        NotifyTopologyResolution topologyResolution;
        try
        {
            topologyResolution = TopologyReadOverride?.Invoke(
                    parsed.RoutingRoot, parsed.Domain, parsed.Team)
                ?? NotifyRoleTopologyStore.Resolve(parsed.RoutingRoot, parsed.Domain, parsed.Team);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or JsonException)
        {
            var failure = CreateResult(parsed, null, null, "identity-unavailable", "ask-human", null, exception.Message);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        if (!topologyResolution.Resolved || topologyResolution.Topology is null)
        {
            var failure = CreateResult(parsed, null, null, "identity-unavailable", "ask-human", null, topologyResolution.Summary);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        // T1 is read exactly once and this same object is passed to the observer.
        var initialTopology = topologyResolution.Topology;
        var targetSelection = ScopedModelResolutionTargetSelector.Select(
            initialTopology, parsed.Domain, parsed.Team, parsed.Role, parsed.Kind);
        if (!targetSelection.Resolved || targetSelection.Target is null)
        {
            var failure = CreateResult(parsed, null, null, targetSelection.Reason, "ask-human", null, targetSelection.Detail);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        var target = targetSelection.Target;
        var key = ScopedModelResolutionScopeKey.Create(target, parsed.Request);
        var baselineSelection = ScopedModelResolutionEvidencePolicy.SelectNewestBaseline(read.Entries, key);
        if (!baselineSelection.Resolved || baselineSelection.Entry is null)
        {
            var failure = CreateResult(parsed, target, null, baselineSelection.Reason, "ask-human", null, null);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        var baseline = baselineSelection.Entry;
        if (!IsCompleteAndConsistentBaseline(baseline, key, parsed.Kind))
        {
            var failure = CreateResult(parsed, target, null, "identity-unavailable", "ask-human", null,
                "The newest scoped verified baseline is incomplete or its recorded invocation disagrees with its observed model/effort.");
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        var candidateSafety = ScopedModelResolutionEvidencePolicy.EvaluateQueryCandidates(
            read.Entries, key, baseline.FullInvocation!, parsed.CandidateInvocation);
        if (!candidateSafety.Permitted)
        {
            var refusal = candidateSafety.BaselineInvocation.BlockingLegacyRefusal
                ?? candidateSafety.BaselineInvocation.BlockingScopedRefusal
                ?? candidateSafety.OptionalCandidate?.BlockingLegacyRefusal
                ?? candidateSafety.OptionalCandidate?.BlockingScopedRefusal;
            var failure = CreateResult(parsed, target, null, "refused-invocation", "ask-human", null,
                refusal is null ? "An applicable refusal prevents this invocation." : "An applicable exact-invocation refusal prevents retry.");
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        ScopedModelResolutionObservationResult observation;
        try
        {
            observation = ObserverOverride is { } observer
                ? await observer(parsed.RoutingRoot, initialTopology, parsed.Domain, parsed.Team, parsed.Role, parsed.Kind)
                    .ConfigureAwait(false)
                : await new ScopedModelResolutionTargetObserver().ObserveStableTarget(
                    parsed.RoutingRoot, initialTopology, parsed.Domain, parsed.Team, parsed.Role, parsed.Kind)
                    .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            var failure = CreateResult(parsed, target, null, "identity-unavailable", "ask-human", null, exception.Message);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        var observed = observation.Evidence is null ? null : ToObservedEvidence(observation.Evidence);
        if (!observation.Resolved || observation.Evidence is null)
        {
            var failure = CreateResult(parsed, observation.Target ?? target, observed, observation.Reason,
                "ask-human", observed, observation.Detail);
            failure = failure with { RecordPath = read.Path };
            Emit(writer, parsed.Format, failure);
            return 0;
        }

        var evidence = observation.Evidence;
        if (!string.Equals(baseline.Host, evidence.Host, StringComparison.Ordinal))
            return EmitUnresolved(writer, parsed, target, read.Path, "host-mismatch", observed,
                "The current local host differs from the captured scoped baseline.");
        if (baseline.ProcessId != evidence.Pid
            || !string.Equals(baseline.ProcessStartTimeUtc, evidence.ProcessStartTimeUtc, StringComparison.Ordinal))
            return EmitUnresolved(writer, parsed, target, read.Path, "generation-mismatch", observed,
                "The current foreground PID or exact UTC process start time differs from the captured baseline.");
        if (!string.Equals(baseline.TopologyDigest, evidence.TopologyDigest, StringComparison.Ordinal))
            return EmitUnresolved(writer, parsed, target, read.Path, "topology-mismatch", observed,
                "The selected routing declaration digest differs from the captured scoped baseline.");

        var expectedModel = parsed.Request.RequestedModel ?? baseline.ObservedModel;
        var expectedEffort = parsed.Request.RequestedEffort;
        if (string.IsNullOrWhiteSpace(expectedModel)
            || !string.Equals(baseline.ObservedEffort, expectedEffort, StringComparison.Ordinal)
            || !string.Equals(evidence.ObservedModel, baseline.ObservedModel, StringComparison.Ordinal)
            || !string.Equals(evidence.ObservedEffort, baseline.ObservedEffort, StringComparison.Ordinal)
            || !string.Equals(evidence.ObservedModel, expectedModel, StringComparison.Ordinal)
            || !string.Equals(evidence.ObservedEffort, expectedEffort, StringComparison.Ordinal)
            || target.DeclaredModel is not null && !string.Equals(target.DeclaredModel, expectedModel, StringComparison.Ordinal)
            || target.DeclaredEffort is not null && !string.Equals(target.DeclaredEffort, expectedEffort, StringComparison.Ordinal)
            || parsed.CandidateParse is not null
                && (!string.Equals(parsed.CandidateParse.Model, expectedModel, StringComparison.Ordinal)
                    || !string.Equals(parsed.CandidateParse.Effort, expectedEffort, StringComparison.Ordinal)))
            return EmitUnresolved(writer, parsed, target, read.Path, "request-mismatch", observed,
                "The captured mapping, current argv, requested model/effort, candidate, or selected topology declaration disagrees.");

        var success = CreateResult(parsed, observation.Target ?? target, observed,
            "target-current", "use-scoped-model-effort", observed, null) with { RecordPath = read.Path, Resolved = true, HumanRequired = false };
        Emit(writer, parsed.Format, success);
        return 0;
    }

    private static bool TryParse(string[] args, out ParsedQuery? parsed, out string error)
    {
        string? routingRoot = null, domain = null, team = null, role = null;
        string? kind = null, informalName = null, requestedEffort = null, requestedModel = null, candidate = null;
        var format = "markdown";
        var hasRequestedModel = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--write" or "--dry-run" or "--capture-target-evidence")
            {
                parsed = null;
                error = "Scoped query is read-only and does not accept record, capture, --write, or --dry-run flags.";
                return false;
            }
            if (!seen.Add(option))
            {
                parsed = null;
                error = $"Duplicate argument '{option}' is not allowed.";
                return false;
            }
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed = null;
                error = $"{option} requires a non-empty value.";
                return false;
            }
            var value = args[++index];
            switch (option)
            {
                case "--routing-root": routingRoot = value; break;
                case "--domain": domain = value; break;
                case "--team": team = value; break;
                case "--role": role = value; break;
                case "--kind": kind = value; break;
                case "--informal-name": informalName = value; break;
                case "--requested-effort": requestedEffort = value; break;
                case "--requested-model": requestedModel = value; hasRequestedModel = true; break;
                case "--candidate-invocation": candidate = value; break;
                case "--format" when value is "json" or "markdown": format = value; break;
                case "--format":
                    parsed = null; error = "--format must be json or markdown."; return false;
                default:
                    parsed = null; error = $"Unknown scoped query argument '{option}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(informalName))
        {
            parsed = null; error = "--kind and --informal-name are required."; return false;
        }
        if (routingRoot is null || domain is null || team is null || role is null || requestedEffort is null)
        {
            parsed = null; error = "Scoped query requires --routing-root, --domain, --team, --role, and --requested-effort together."; return false;
        }
        if (!Path.IsPathFullyQualified(routingRoot))
        {
            parsed = null; error = "--routing-root must be an absolute host root."; return false;
        }
        try
        {
            routingRoot = Path.GetFullPath(routingRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            parsed = null; error = $"--routing-root is invalid: {exception.Message}"; return false;
        }

        var grammar = AgentLaunchRecipeRegistry.FindModelFlagGrammar(kind.Trim());
        if (grammar is null)
        {
            parsed = null;
            error = $"No measured model/effort flag grammar is recorded for kind '{kind}'.";
            return false;
        }
        ModelResolutionInvocationParseResult? candidateParse = null;
        if (candidate is not null)
        {
            candidateParse = ModelResolutionInvocationParser.ParseInvocation(candidate, grammar.Kind);
            if (!candidateParse.Resolved)
            {
                parsed = null;
                error = $"--candidate-invocation is not supported: {candidateParse.Reason}.";
                return false;
            }
        }

        var request = new ScopedModelResolutionRequest(informalName, hasRequestedModel ? requestedModel : null, requestedEffort);
        parsed = new ParsedQuery(routingRoot, domain, team, role, grammar.Kind, request, candidate, candidateParse, format);
        error = string.Empty;
        return true;
    }

    private static bool IsCompleteAndConsistentBaseline(
        ModelResolutionLedgerEntry entry,
        ScopedModelResolutionScopeKey key,
        string kind)
    {
        if (!key.Matches(entry)
            || entry.RecordedAt == default
            || string.IsNullOrWhiteSpace(entry.InformalName)
            || string.IsNullOrWhiteSpace(entry.FullInvocation)
            || string.IsNullOrWhiteSpace(entry.Evidence)
            || entry.RoleAliases is not { Count: > 0 }
            || entry.RoleAliases.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(entry.Host)
            || entry.ProcessId is not > 0
            || !IsUtcRoundTrip(entry.ProcessStartTimeUtc)
            || !IsSha256(entry.TopologyDigest)
            || !entry.ObservedAt.HasValue
            || entry.ObservedAt.Value == default
            || entry.ObservedArgv is not { Count: > 0 }
            || string.IsNullOrWhiteSpace(entry.ObservedArgv[0])
            || string.IsNullOrWhiteSpace(entry.ObservedModel)
            || string.IsNullOrWhiteSpace(entry.ObservedEffort)
            || !string.Equals(entry.IdentitySource, "local-process-start-time", StringComparison.Ordinal))
            return false;

        var launch = ModelResolutionInvocationParser.ParseInvocation(entry.FullInvocation!, kind);
        var observed = ModelResolutionInvocationParser.ParseArgv(entry.ObservedArgv!, kind);
        return launch.Resolved && observed.Resolved
            && string.Equals(launch.Model, entry.ObservedModel, StringComparison.Ordinal)
            && string.Equals(launch.Effort, entry.ObservedEffort, StringComparison.Ordinal)
            && string.Equals(observed.Model, entry.ObservedModel, StringComparison.Ordinal)
            && string.Equals(observed.Effort, entry.ObservedEffort, StringComparison.Ordinal);
    }

    private static bool IsUtcRoundTrip(string? value) =>
        value is not null
        && DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
        && parsed.Kind == DateTimeKind.Utc;

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    private static int EmitUnresolved(
        TextWriter writer,
        ParsedQuery parsed,
        ScopedModelResolutionTarget target,
        string recordPath,
        string reason,
        ScopedModelResolutionQueryObservedEvidence? observed,
        string detail)
    {
        var result = CreateResult(parsed, target, null, reason, "ask-human", observed, detail)
            with { RecordPath = recordPath };
        Emit(writer, parsed.Format, result);
        return 0;
    }

    private static ScopedModelResolutionQueryResult CreateResult(
        ParsedQuery parsed,
        ScopedModelResolutionTarget? target,
        ScopedModelResolutionQueryObservedEvidence? observedEvidence,
        string reason,
        string nextStep,
        ScopedModelResolutionQueryObservedEvidence? availableObservedEvidence,
        string? detail) => new()
    {
        Operation = "model-resolution-query",
        Resolved = reason == "target-current",
        HumanRequired = reason != "target-current",
        Reason = reason,
        Scope = new ScopedModelResolutionQueryScope(
            target?.Domain ?? parsed.Domain,
            target?.Team ?? parsed.Team,
            target?.Role ?? parsed.Role,
            target?.Kind ?? parsed.Kind,
            target?.WorkspaceId,
            target?.PaneId,
            target?.RoleAliases,
            target?.TopologyDigest),
        Request = new ScopedModelResolutionQueryRequest(
            parsed.Request.Form,
            parsed.Request.InformalName,
            parsed.Request.RequestedModel,
            parsed.Request.RequestedEffort),
        ObservedEvidence = availableObservedEvidence ?? observedEvidence,
        NextStep = nextStep,
        ProviderOperation = "none",
        Error = detail,
    };

    private static ScopedModelResolutionQueryObservedEvidence ToObservedEvidence(
        ScopedModelResolutionObservedEvidence evidence) => new(
        evidence.Host,
        evidence.Pid,
        evidence.ProcessStartTimeUtc,
        evidence.IdentitySource,
        evidence.TopologyDigest,
        evidence.RoleAliases,
        evidence.ObservedArgv,
        evidence.ObservedModel,
        evidence.ObservedEffort);

    private static void Emit(TextWriter writer, string format, ScopedModelResolutionQueryResult result)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine("# Scoped model resolution");
        writer.WriteLine();
        writer.WriteLine($"- resolved: **{result.Resolved.ToString().ToLowerInvariant()}**");
        writer.WriteLine($"- human required: **{result.HumanRequired.ToString().ToLowerInvariant()}**");
        writer.WriteLine($"- reason: **{result.Reason}**");
        writer.WriteLine($"- scope: `{result.Scope.Domain}/{result.Scope.Team}/{result.Scope.Role}` at `{result.Scope.WorkspaceId ?? "<unselected>"}/{result.Scope.PaneId ?? "<unselected>"}` ({result.Scope.Kind})");
        writer.WriteLine($"- request: `{result.Request.Form}` informal-name `{result.Request.InformalName}`, model `{result.Request.RequestedModel ?? "<informal mapping>"}`, effort `{result.Request.RequestedEffort}`");
        if (result.ObservedEvidence is { } observed)
        {
            writer.WriteLine($"- observed evidence: host `{observed.Host}`, pid `{observed.Pid}`, process start `{observed.ProcessStartTimeUtc}`, digest `{observed.TopologyDigest}`");
            writer.WriteLine($"- observed model/effort: `{observed.Model}` / `{observed.Effort}`");
            writer.WriteLine($"- observed argv: `{string.Join(" ", observed.Argv)}`");
        }
        if (!string.IsNullOrWhiteSpace(result.Error))
            writer.WriteLine($"- detail: {result.Error}");
        writer.WriteLine($"- next step: **{result.NextStep}**");
        writer.WriteLine("- provider operation: **none**");
    }

    private sealed record ParsedQuery(
        string RoutingRoot,
        string Domain,
        string Team,
        string Role,
        string Kind,
        ScopedModelResolutionRequest Request,
        string? CandidateInvocation,
        ModelResolutionInvocationParseResult? CandidateParse,
        string Format);
}
