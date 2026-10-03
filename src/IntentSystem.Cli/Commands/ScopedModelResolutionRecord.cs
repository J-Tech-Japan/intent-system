using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record ScopedModelResolutionRecordResult
{
    public required string Operation { get; init; }
    public required string CommandMode { get; init; }
    public required bool Applied { get; init; }
    public required bool Resolved { get; init; }
    public required string Reason { get; init; }
    public required string RecordPath { get; init; }
    public required ScopedModelResolutionQueryScope Scope { get; init; }
    public required ScopedModelResolutionQueryRequest Request { get; init; }
    public ModelResolutionLedgerEntry? Entry { get; init; }
    public required string ProviderOperation { get; init; }
    public string? Error { get; init; }
}

/// <summary>Scoped capture and append flow. It never reads prior ledger rows.</summary>
internal static class ScopedModelResolutionRecord
{
    private const string Usage =
        "Usage: intent-cli session-layer model-resolution record --routing-root <absolute-host-root> --domain <d> "
        + "--team <t> --role <role> --kind <codex|claude> --informal-name <name> --requested-effort <effort> "
        + "--outcome verified|refused --invocation <raw-invocation> (--evidence <READY-proof>|--error <text>) "
        + "[--requested-model <id>] [--capture-target-evidence] (--write|--dry-run) --format json|markdown";

    private static readonly HashSet<string> ScopedArguments = new(StringComparer.Ordinal)
    {
        "--routing-root", "--domain", "--team", "--role", "--requested-effort", "--requested-model",
        "--capture-target-evidence",
    };

    private static readonly HashSet<string> ValueArguments = new(StringComparer.Ordinal)
    {
        "--routing-root", "--domain", "--team", "--role", "--requested-effort", "--requested-model",
        "--kind", "--informal-name", "--outcome", "--invocation", "--evidence", "--error", "--format",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static Func<string, string, string, NotifyTopologyResolution>? TopologyReadOverride { get; set; }
    internal static Func<string, NotifyTeamTopology, string, string, string, string,
        Task<ScopedModelResolutionObservationResult>>? ObserverOverride { get; set; }
    internal static Func<string, ModelResolutionLedgerEntry, bool, ModelResolutionLedgerWriteResult>? AppendOverride { get; set; }
    internal static Func<DateTimeOffset>? UtcNowOverride { get; set; }

    public static bool HasScopedArguments(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (ScopedArguments.Contains(args[index])) return true;
            if (ValueArguments.Contains(args[index])) index++;
        }
        return false;
    }

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (!TryParse(args, out var parsedValue, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }
        var parsed = parsedValue!;
        var grammar = AgentLaunchRecipeRegistry.FindModelFlagGrammar(parsed.Kind);
        if (grammar is null)
        {
            writer.WriteLine($"No measured model/effort flag grammar is recorded for kind '{parsed.Kind}'. Refusing to invent grammar.");
            writer.WriteLine(Usage);
            return 1;
        }

        ModelResolutionInvocationParseResult? invocation = null;
        if (parsed.Outcome == ModelResolutionLedgerCommand.VerifiedOutcome)
        {
            invocation = ModelResolutionInvocationParser.ParseInvocation(parsed.Invocation, grammar.Kind);
            if (!invocation.Resolved)
            {
                Emit(writer, parsed.Format, Failure(parsed, grammar, "invocation-unreadable", invocation.Reason));
                return 1;
            }
        }

        var request = new ScopedModelResolutionRequest(
            parsed.InformalName, parsed.RequestedModel, parsed.RequestedEffort);
        NotifyTopologyResolution topologyResolution;
        try
        {
            topologyResolution = TopologyReadOverride?.Invoke(parsed.RoutingRoot, parsed.Domain, parsed.Team)
                ?? NotifyRoleTopologyStore.Resolve(parsed.RoutingRoot, parsed.Domain, parsed.Team);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or JsonException)
        {
            Emit(writer, parsed.Format, Failure(parsed, grammar, "identity-unavailable", exception.Message));
            return 1;
        }

        if (!topologyResolution.Resolved || topologyResolution.Topology is null)
        {
            Emit(writer, parsed.Format, Failure(parsed, grammar, "identity-unavailable", topologyResolution.Summary));
            return 1;
        }

        // T1 is selected once and the same topology object is supplied to capture.
        var initialTopology = topologyResolution.Topology;
        var selection = ScopedModelResolutionTargetSelector.Select(
            initialTopology, parsed.Domain, parsed.Team, parsed.Role, grammar.Kind);
        if (!selection.Resolved || selection.Target is null)
        {
            Emit(writer, parsed.Format, Failure(parsed, grammar, selection.Reason, selection.Detail));
            return 1;
        }

        var target = selection.Target;
        var key = ScopedModelResolutionScopeKey.Create(target, request);
        ModelResolutionLedgerEntry entry;
        if (parsed.Outcome == ModelResolutionLedgerCommand.RefusedOutcome)
        {
            entry = CreateEntry(parsed, target, key, null, null);
        }
        else
        {
            ScopedModelResolutionObservationResult observation;
            try
            {
                observation = ObserverOverride is { } observer
                    ? observer(parsed.RoutingRoot, initialTopology, parsed.Domain, parsed.Team, parsed.Role, grammar.Kind)
                        .GetAwaiter().GetResult()
                    : new ScopedModelResolutionTargetObserver().ObserveStableTarget(
                        parsed.RoutingRoot, initialTopology, parsed.Domain, parsed.Team, parsed.Role, grammar.Kind)
                        .GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException
                or OperationCanceledException or TimeoutException)
            {
                Emit(writer, parsed.Format, Failure(parsed, grammar, "identity-unavailable", exception.Message, target));
                return 1;
            }

            if (!observation.Resolved || observation.Evidence is null)
            {
                Emit(writer, parsed.Format, Failure(parsed, grammar, observation.Reason,
                    observation.Detail ?? "The target capture did not produce stable process evidence.", target));
                return 1;
            }

            var evidence = observation.Evidence;
            if (!HasCompleteCapturedIdentity(evidence, grammar.Kind))
            {
                Emit(writer, parsed.Format, Failure(parsed, grammar, "identity-unavailable",
                    "The captured process evidence is incomplete or does not contain an exact local process-start identity.", target));
                return 1;
            }
            if (!string.Equals(evidence.TopologyDigest, target.TopologyDigest, StringComparison.Ordinal)
                || !evidence.RoleAliases.SequenceEqual(target.RoleAliases, StringComparer.Ordinal))
            {
                Emit(writer, parsed.Format, Failure(parsed, grammar, "topology-mismatch",
                    "The observed target declaration differs from the selected T1 scope.", target));
                return 1;
            }

            var observedArgv = ModelResolutionInvocationParser.ParseArgv(evidence.ObservedArgv, grammar.Kind);
            if (!observedArgv.Resolved
                || !string.Equals(observedArgv.Model, evidence.ObservedModel, StringComparison.Ordinal)
                || !string.Equals(observedArgv.Effort, evidence.ObservedEffort, StringComparison.Ordinal)
                || !string.Equals(invocation!.Model, evidence.ObservedModel, StringComparison.Ordinal)
                || !string.Equals(invocation.Effort, evidence.ObservedEffort, StringComparison.Ordinal)
                || !string.Equals(parsed.RequestedEffort, evidence.ObservedEffort, StringComparison.Ordinal)
                || parsed.RequestedModel is not null
                    && !string.Equals(parsed.RequestedModel, evidence.ObservedModel, StringComparison.Ordinal)
                || target.DeclaredModel is not null
                    && !string.Equals(target.DeclaredModel, evidence.ObservedModel, StringComparison.Ordinal)
                || target.DeclaredEffort is not null
                    && !string.Equals(target.DeclaredEffort, evidence.ObservedEffort, StringComparison.Ordinal))
            {
                Emit(writer, parsed.Format, Failure(parsed, grammar, "request-mismatch",
                    "The captured argv, supplied invocation, requested model/effort, or selected topology declaration disagrees.", target));
                return 1;
            }

            entry = CreateEntry(parsed, target, key, evidence, UtcNow().ToUniversalTime());
        }

        ModelResolutionLedgerWriteResult writeResult;
        try
        {
            writeResult = AppendOverride?.Invoke(parsed.RoutingRoot, entry, parsed.Write)
                ?? ModelResolutionLedgerStore.Append(parsed.RoutingRoot, entry, parsed.Write);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            Emit(writer, parsed.Format, Failure(parsed, grammar, "append-failed", exception.Message, target, entry));
            return 1;
        }

        var applied = writeResult.Applied;
        var reason = parsed.Write
            ? applied ? "recorded" : "append-failed"
            : "dry-run-ready";
        var result = BuildResult(parsed, grammar, reason, target, entry, writeResult.Path, writeResult.Error, applied);
        Emit(writer, parsed.Format, result);
        return parsed.Write && !applied || writeResult.Error is not null ? 1 : 0;
    }

    private static bool TryParse(string[] args, out ParsedRecord? parsed, out string error)
    {
        string? routingRoot = null, domain = null, team = null, role = null, kind = null;
        string? informalName = null, requestedEffort = null, requestedModel = null;
        string? outcome = null, invocation = null, evidence = null, refusalError = null, format = "json";
        var capture = false;
        bool? write = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option))
            {
                parsed = null; error = $"Duplicate argument '{option}' is not allowed."; return false;
            }
            if (option is "--write" or "--dry-run")
            {
                if (write is not null)
                {
                    parsed = null; error = "Specify exactly one of --write or --dry-run."; return false;
                }
                write = option == "--write";
                continue;
            }
            if (option == "--capture-target-evidence")
            {
                capture = true;
                continue;
            }
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])
                || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                parsed = null; error = $"{option} requires a non-empty value."; return false;
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
                case "--requested-model": requestedModel = value; break;
                case "--outcome": outcome = value; break;
                case "--invocation": invocation = value; break;
                case "--evidence": evidence = value; break;
                case "--error": refusalError = value; break;
                case "--format" when value is "json" or "markdown": format = value; break;
                case "--format": parsed = null; error = "--format must be json or markdown."; return false;
                default: parsed = null; error = $"Unknown scoped record argument '{option}'."; return false;
            }
        }

        if (routingRoot is null || domain is null || team is null || role is null || requestedEffort is null)
        {
            parsed = null; error = "Scoped record requires --routing-root, --domain, --team, --role, and --requested-effort together."; return false;
        }
        if (!Path.IsPathFullyQualified(routingRoot))
        {
            parsed = null; error = "--routing-root must be an absolute host root."; return false;
        }
        try { routingRoot = Path.GetFullPath(routingRoot); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            parsed = null; error = $"--routing-root is invalid: {exception.Message}"; return false;
        }
        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(informalName)
            || string.IsNullOrWhiteSpace(outcome) || string.IsNullOrWhiteSpace(invocation))
        {
            parsed = null; error = "--kind, --informal-name, --outcome, and --invocation are required."; return false;
        }
        if (outcome is not (ModelResolutionLedgerCommand.VerifiedOutcome or ModelResolutionLedgerCommand.RefusedOutcome))
        {
            parsed = null; error = "--outcome must be verified or refused."; return false;
        }
        if (write is null)
        {
            parsed = null; error = "Specify exactly one of --write or --dry-run."; return false;
        }
        if (outcome == ModelResolutionLedgerCommand.VerifiedOutcome
            && (!capture || string.IsNullOrWhiteSpace(evidence) || refusalError is not null))
        {
            parsed = null; error = "A verified scoped outcome requires --capture-target-evidence and non-empty --evidence, and does not accept --error."; return false;
        }
        if (outcome == ModelResolutionLedgerCommand.RefusedOutcome
            && (capture || evidence is not null || string.IsNullOrWhiteSpace(refusalError)))
        {
            parsed = null; error = "A refused scoped outcome requires --error and does not accept --evidence or --capture-target-evidence."; return false;
        }

        parsed = new ParsedRecord(routingRoot, domain, team, role, kind, informalName, requestedEffort,
            requestedModel, outcome, invocation, evidence, refusalError, capture, write.Value, format!);
        error = string.Empty;
        return true;
    }

    private static ModelResolutionLedgerEntry CreateEntry(
        ParsedRecord parsed,
        ScopedModelResolutionTarget target,
        ScopedModelResolutionScopeKey key,
        ScopedModelResolutionObservedEvidence? evidence,
        DateTimeOffset? capturedAt)
    {
        var recordedAt = (capturedAt ?? UtcNow()).ToUniversalTime();
        var verified = parsed.Outcome == ModelResolutionLedgerCommand.VerifiedOutcome;
        return new ModelResolutionLedgerEntry
        {
            InformalName = parsed.InformalName,
            Kind = target.Kind,
            Outcome = parsed.Outcome,
            FullInvocation = verified ? parsed.Invocation : null,
            RefusedInvocation = verified ? null : parsed.Invocation,
            Evidence = verified ? parsed.Evidence : null,
            ErrorText = verified ? null : parsed.Error,
            RecordedAt = recordedAt,
            ScopeVersion = 1,
            Domain = key.Domain,
            Team = key.Team,
            Role = key.Role,
            RoleAliases = target.RoleAliases.ToArray(),
            WorkspaceId = key.WorkspaceId,
            PaneId = key.PaneId,
            RequestForm = key.RequestForm,
            RequestedModel = key.RequestedModel,
            RequestedEffort = key.RequestedEffort,
            Host = evidence?.Host,
            ProcessId = evidence?.Pid,
            ProcessStartTimeUtc = evidence?.ProcessStartTimeUtc,
            IdentitySource = evidence?.IdentitySource,
            TopologyDigest = target.TopologyDigest,
            ObservedAt = evidence is null ? null : recordedAt,
            ObservedArgv = evidence?.ObservedArgv.ToArray(),
            ObservedModel = evidence?.ObservedModel,
            ObservedEffort = evidence?.ObservedEffort,
        };
    }

    private static bool HasCompleteCapturedIdentity(ScopedModelResolutionObservedEvidence evidence, string kind)
    {
        if (string.IsNullOrWhiteSpace(evidence.Host)
            || evidence.Pid <= 0
            || !IsUtcRoundTrip(evidence.ProcessStartTimeUtc)
            || !string.Equals(evidence.IdentitySource, "local-process-start-time", StringComparison.Ordinal)
            || !IsSha256(evidence.TopologyDigest)
            || evidence.RoleAliases is not { Count: > 0 }
            || evidence.RoleAliases.Any(string.IsNullOrWhiteSpace)
            || evidence.ObservedArgv is not { Count: > 0 }
            || evidence.ObservedArgv.Any(string.IsNullOrWhiteSpace)
            || string.IsNullOrWhiteSpace(evidence.ObservedModel)
            || string.IsNullOrWhiteSpace(evidence.ObservedEffort))
            return false;

        var parsed = ModelResolutionInvocationParser.ParseArgv(evidence.ObservedArgv, kind);
        return parsed.Resolved
            && string.Equals(parsed.Model, evidence.ObservedModel, StringComparison.Ordinal)
            && string.Equals(parsed.Effort, evidence.ObservedEffort, StringComparison.Ordinal);
    }

    private static bool IsUtcRoundTrip(string? value) => value is not null
        && DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
        && parsed.Kind == DateTimeKind.Utc
        && string.Equals(parsed.ToString("O", CultureInfo.InvariantCulture), value, StringComparison.Ordinal);

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static DateTimeOffset UtcNow() => (UtcNowOverride?.Invoke() ?? ModelResolutionLedgerCommand.UtcNowFactory()).ToUniversalTime();

    private static ScopedModelResolutionRecordResult Failure(
        ParsedRecord parsed,
        AgentModelFlagGrammar grammar,
        string reason,
        string? error,
        ScopedModelResolutionTarget? target = null,
        ModelResolutionLedgerEntry? entry = null) => BuildResult(
        parsed,
        grammar,
        reason,
        target,
        entry,
        ModelResolutionLedgerStore.ResolvePath(parsed.RoutingRoot),
        error,
        applied: false);

    private static ScopedModelResolutionRecordResult BuildResult(
        ParsedRecord parsed,
        AgentModelFlagGrammar grammar,
        string reason,
        ScopedModelResolutionTarget? target,
        ModelResolutionLedgerEntry? entry,
        string recordPath,
        string? error,
        bool applied) => new()
    {
        Operation = "model-resolution-record",
        CommandMode = parsed.Write ? "write" : "dry-run",
        Applied = applied,
        Resolved = reason is "recorded" or "dry-run-ready",
        Reason = reason,
        RecordPath = recordPath,
        Scope = new ScopedModelResolutionQueryScope(
            target?.Domain ?? parsed.Domain,
            target?.Team ?? parsed.Team,
            target?.Role ?? parsed.Role,
            target?.Kind ?? grammar.Kind,
            target?.WorkspaceId,
            target?.PaneId,
            target?.RoleAliases,
            target?.TopologyDigest),
        Request = new ScopedModelResolutionQueryRequest(
            parsed.RequestedModel is null ? "informal" : "explicit",
            parsed.InformalName,
            parsed.RequestedModel,
            parsed.RequestedEffort),
        Entry = entry,
        ProviderOperation = "none",
        Error = error,
    };

    private static void Emit(TextWriter writer, string format, ScopedModelResolutionRecordResult result)
    {
        if (format == "json")
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }
        writer.WriteLine("# Scoped model resolution record");
        writer.WriteLine();
        writer.WriteLine($"- resolved: **{result.Resolved.ToString().ToLowerInvariant()}**");
        writer.WriteLine($"- applied: **{result.Applied.ToString().ToLowerInvariant()}**");
        writer.WriteLine($"- reason: **{result.Reason}**");
        writer.WriteLine($"- scope: `{result.Scope.Domain}/{result.Scope.Team}/{result.Scope.Role}` at `{result.Scope.WorkspaceId ?? "<unselected>"}/{result.Scope.PaneId ?? "<unselected>"}` ({result.Scope.Kind})");
        writer.WriteLine($"- request: `{result.Request.Form}` informal-name `{result.Request.InformalName}`, model `{result.Request.RequestedModel ?? "<informal mapping>"}`, effort `{result.Request.RequestedEffort}`");
        writer.WriteLine($"- outcome: **{result.Entry?.Outcome ?? "not recorded"}**; mode: **{result.CommandMode}**");
        if (!string.IsNullOrWhiteSpace(result.Error)) writer.WriteLine($"- detail: {result.Error}");
        writer.WriteLine("- provider operation: **none**");
    }

    private sealed record ParsedRecord(
        string RoutingRoot,
        string Domain,
        string Team,
        string Role,
        string Kind,
        string InformalName,
        string RequestedEffort,
        string? RequestedModel,
        string Outcome,
        string Invocation,
        string? Evidence,
        string? Error,
        bool Capture,
        bool Write,
        string Format);
}
