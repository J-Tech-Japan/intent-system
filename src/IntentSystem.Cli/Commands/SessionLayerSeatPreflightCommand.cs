using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class SessionLayerSeatPreflightCommand
{
    internal const string RelativeLedgerPath = ".intent-cli/session-layer/seat-preflight.jsonl";
    internal const string FormatJson = "json";
    internal const string FormatMarkdown = "markdown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static Func<string, IGitRemoteCommandRunner>? GitRunnerFactory { get; set; }
    internal static Func<IEnumerable<KeyValuePair<string, string>>> EnvironmentFactory { get; set; } =
        () => Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (Key: entry.Key as string, Value: entry.Value as string))
            .Where(entry => entry.Key is not null && entry.Value is not null)
            .Select(entry => new KeyValuePair<string, string>(entry.Key!, entry.Value!));
    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } = () => DateTimeOffset.UtcNow;

    internal static IReadOnlyDictionary<string, string> RemedyTable { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["git-writable|unmarked"] = "Grant the seat write access to its own .git directory; the probe never changes the index or branch.",
            ["remote-reachable|unmarked"] = "Verify the origin remote and credentials/network access; preflight does not retry or change the remote.",
            ["identity-installed|unmarked"] = "Install the operator-approved Git identity tuple before the seat starts work.",
            ["claim-path-host-root|unmarked"] = "Run the seat from the host checkout whose claims path owns the execution-unit claim; do not invent a local claim store.",
            ["runtime-family|unmarked"] = "Use the operator-approved remedy for the detected runtime family; preflight does not launch, name, or grade a runtime.",
            ["git-writable|intent-cli-runtime"] = "Grant the seat write access to its own .git directory under the intent-cli runtime policy; the probe never changes the index or branch.",
            ["remote-reachable|intent-cli-runtime"] = "Verify the origin remote and credentials/network access under the intent-cli runtime policy; preflight does not retry or change the remote.",
            ["identity-installed|intent-cli-runtime"] = "Install the operator-approved Git identity tuple before the seat starts work under the intent-cli runtime policy.",
            ["claim-path-host-root|intent-cli-runtime"] = "Run the seat from the host checkout whose claims path owns the execution-unit claim; do not invent a local claim store.",
            ["runtime-family|intent-cli-runtime"] = "Use the operator-approved remedy for the intent-cli runtime family; preflight does not launch, name, or grade a runtime.",
            ["git-writable|intent-runtime"] = "Grant the seat write access to its own .git directory under the intent runtime policy; the probe never changes the index or branch.",
            ["remote-reachable|intent-runtime"] = "Verify the origin remote and credentials/network access under the intent runtime policy; preflight does not retry or change the remote.",
            ["identity-installed|intent-runtime"] = "Install the operator-approved Git identity tuple before the seat starts work under the intent runtime policy.",
            ["claim-path-host-root|intent-runtime"] = "Run the seat from the host checkout whose claims path owns the execution-unit claim; do not invent a local claim store.",
            ["runtime-family|intent-runtime"] = "Use the operator-approved remedy for the intent runtime family; preflight does not launch, name, or grade a runtime.",
            ["git-writable|agent-runtime"] = "Grant the seat write access to its own .git directory under the agent runtime policy; the probe never changes the index or branch.",
            ["remote-reachable|agent-runtime"] = "Verify the origin remote and credentials/network access under the agent runtime policy; preflight does not retry or change the remote.",
            ["identity-installed|agent-runtime"] = "Install the operator-approved Git identity tuple before the seat starts work under the agent runtime policy.",
            ["claim-path-host-root|agent-runtime"] = "Run the seat from the host checkout whose claims path owns the execution-unit claim; do not invent a local claim store.",
            ["runtime-family|agent-runtime"] = "Use the operator-approved remedy for the agent runtime family; preflight does not launch, name, or grade a runtime.",
        };

    private const string Usage =
        "Usage: intent-cli session-layer seat preflight --domain <domain> --team <team> --role <role> "
        + "[--launch-at <timestamp>] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(Usage);
            return 0;
        }

        if (!TryParse(args, out var domain, out var team, out var role, out var launchAt, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(Usage);
            return 1;
        }

        var observedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var recordedSeat = ResolveRecordedSeat(context.RepoRoot, domain!, team!, role!);
        var launchResolution = ResolveLaunchAt(context.RepoRoot, domain!, team!, role!, launchAt);
        var runner = GitRunnerFactory?.Invoke(context.RepoRoot)
            ?? new CheckoutFreshnessGitCommandRunner(TimeSpan.FromSeconds(10));
        var probes = new List<SessionLayerSeatProbe>
        {
            ProbeGitWritable(context.RepoRoot, runner),
            ProbeRemoteReachable(context.RepoRoot, runner),
            ProbeIdentity(context.RepoRoot, runner),
            ProbeClaimPath(context.RepoRoot, runner),
            ProbeRuntimeFamily(),
        };

        var allPassed = probes.All(probe => probe.Passed);
        var runtime = probes.Single(probe => probe.Name == "runtime-family");
        var ledger = new SessionLayerSeatPreflightRecord
        {
            Domain = domain!,
            Team = team!,
            Role = role!,
            Resident = recordedSeat?.Resident,
            WorkspaceId = recordedSeat?.WorkspaceId,
            PaneId = recordedSeat?.PaneId,
            Reader = recordedSeat?.Reader,
            ObservedAt = observedAt,
            LaunchAt = launchResolution.LaunchAt,
            LaunchSource = launchResolution.Source,
            Passed = allPassed,
            RuntimeFamily = runtime.Value,
            Probes = probes,
        };
        var ledgerResult = SessionLayerSeatPreflightStore.Append(context.RepoRoot, ledger);
        var result = new SessionLayerSeatPreflightResult
        {
            Domain = domain!,
            Team = team!,
            Role = role!,
            Passed = allPassed,
            ObservedAt = observedAt,
            LaunchAt = ledger.LaunchAt,
            LaunchSource = ledger.LaunchSource,
            LaunchError = launchResolution.Error,
            LedgerPath = ledgerResult.Path,
            LedgerRecorded = ledgerResult.Applied,
            RuntimeFamily = runtime.Value,
            Probes = probes,
            Summary = allPassed
                ? launchResolution.Resolved
                    ? $"Seat preflight passed for role '{role}' in team '{team}'. All five probes passed."
                    : $"Seat preflight probes passed for role '{role}' in team '{team}', but no durable launch source was available."
                : $"Seat preflight failed for role '{role}' in team '{team}'. Follow every failed probe remedy before delegating.",
        };

        Emit(writer, format, result);
        return allPassed && ledgerResult.Applied && launchResolution.Resolved ? 0 : 1;
    }

    private static RecordedSeatIdentity? ResolveRecordedSeat(
        string repoRoot,
        string domain,
        string team,
        string role)
    {
        var topology = NotifyRoleTopologyStore.Resolve(repoRoot, domain, team);
        if (!topology.Resolved
            || topology.Topology is not { } resolvedTopology
            || NotifyRoleTopologyStore.ResolveRecordedRole(resolvedTopology, role).Record is not { } recordedRole)
        {
            return null;
        }

        return new RecordedSeatIdentity(
            recordedRole.Resident,
            recordedRole.WorkspaceId ?? resolvedTopology.WorkspaceId,
            recordedRole.PaneId,
            recordedRole.Reader);
    }

    internal static SessionLayerSeatLaunchResolution ResolveLaunchAt(
        string repoRoot,
        string domain,
        string team,
        string role,
        DateTimeOffset? explicitLaunchAt)
    {
        if (explicitLaunchAt is { } supplied)
        {
            return SessionLayerSeatLaunchResolution.Success(
                supplied.ToUniversalTime(),
                "operator --launch-at");
        }

        // G808: when the operator omits --launch-at, use the durable verified
        // launch evidence already recorded by model-resolution.  The role's
        // recorded kind is the join key; observed_at is never a launch-time
        // substitute.
        var topology = NotifyRoleTopologyStore.Resolve(repoRoot, domain, team);
        if (topology.Resolved
            && topology.Topology is { } resolvedTopology
            && NotifyRoleTopologyStore.ResolveRecordedRole(resolvedTopology, role).Record is { } recordedRole
            && !string.IsNullOrWhiteSpace(recordedRole.Kind))
        {
            var modelResolution = ModelResolutionLedgerStore.Read(repoRoot);
            if (modelResolution.Resolved)
            {
                var verified = modelResolution.Entries
                    .Where(entry => entry.Outcome == ModelResolutionLedgerCommand.VerifiedOutcome
                        && string.Equals(entry.Kind, recordedRole.Kind, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(entry => entry.RecordedAt)
                    .FirstOrDefault();
                if (verified is not null)
                {
                    return SessionLayerSeatLaunchResolution.Success(
                        verified.RecordedAt.ToUniversalTime(),
                        $"model-resolution verified launch ({recordedRole.Kind})");
                }
            }
        }

        // A repeated preflight also has a durable launch source: retain the
        // previous record's launch_at rather than manufacturing one from the
        // current observation.
        var requestedSeat = ResolveRecordedSeat(repoRoot, domain, team, role);
        var previous = SessionLayerSeatPreflightStore.Read(repoRoot, domain, team)
            .Where(record => record.LaunchAt is not null
                && SessionLayerSeatIdentityMatcher.MatchesRequested(record, role, requestedSeat))
            .OrderByDescending(record => record.ObservedAt)
            .FirstOrDefault();
        if (previous is not null)
        {
            return SessionLayerSeatLaunchResolution.Success(
                previous.LaunchAt!.Value.ToUniversalTime(),
                previous.LaunchSource ?? "previous seat-preflight launch_at");
        }

        return SessionLayerSeatLaunchResolution.Failure(
            $"No durable launch time is recorded for role '{role}'. Record a verified model-resolution launch, "
            + "reuse a prior seat-preflight record, or supply --launch-at explicitly; observed_at is not used as a launch time.");
    }

    private static SessionLayerSeatProbe ProbeGitWritable(string repoRoot, IGitRemoteCommandRunner runner)
    {
        var reference = $"refs/intent-cli/preflight/{Guid.NewGuid():N}";
        var created = RunSafe(runner, repoRoot, ["update-ref", reference, "HEAD"]);
        var removed = RunSafe(runner, repoRoot, ["update-ref", "-d", reference]);

        var passed = created.ExitCode == 0 && removed.ExitCode == 0;
        return new SessionLayerSeatProbe
        {
            Name = "git-writable",
            Passed = passed,
            Value = passed ? "passed" : "failed",
            Detail = passed
                ? "created and deleted one isolated refs/intent-cli/preflight ref; index and branch were untouched."
                : CombineGitFailure(created, removed),
            Remedy = RemedyFor("git-writable", "unmarked"),
        };
    }

    private static SessionLayerSeatProbe ProbeRemoteReachable(string repoRoot, IGitRemoteCommandRunner runner)
    {
        var result = RunSafe(runner, repoRoot, ["ls-remote", "--exit-code", "origin", "HEAD"]);
        return new SessionLayerSeatProbe
        {
            Name = "remote-reachable",
            Passed = result.ExitCode == 0,
            Value = result.ExitCode == 0 ? "passed" : "failed",
            Detail = result.ExitCode == 0
                ? "origin HEAD was readable."
                : $"origin HEAD was not readable (exit {result.ExitCode}): {TrimError(result)}",
            Remedy = RemedyFor("remote-reachable", "unmarked"),
        };
    }

    private static SessionLayerSeatProbe ProbeIdentity(string repoRoot, IGitRemoteCommandRunner runner)
    {
        var result = RunSafe(runner, repoRoot, ["var", "GIT_AUTHOR_IDENT"]);
        var identity = result.StdOut.Trim();
        var passed = result.ExitCode == 0 && identity.Length > 0;
        return new SessionLayerSeatProbe
        {
            Name = "identity-installed",
            Passed = passed,
            Value = passed ? identity : "missing",
            Detail = passed ? "Git author identity tuple is installed." : $"Git author identity is unavailable: {TrimError(result)}",
            Remedy = RemedyFor("identity-installed", "unmarked"),
        };
    }

    private static SessionLayerSeatProbe ProbeClaimPath(string repoRoot, IGitRemoteCommandRunner runner)
    {
        var result = RunSafe(runner, repoRoot, ["rev-parse", "--show-toplevel"]);
        var reportedRoot = result.StdOut.Trim();
        var expectedRoot = Path.GetFullPath(repoRoot);
        var claimsPath = Path.Combine(expectedRoot, ClaimCommand.ClaimsDirectory.Replace('/', Path.DirectorySeparatorChar));
        var passed = result.ExitCode == 0
            && string.Equals(Path.GetFullPath(reportedRoot), expectedRoot, StringComparison.Ordinal)
            && Directory.Exists(claimsPath);
        return new SessionLayerSeatProbe
        {
            Name = "claim-path-host-root",
            Passed = passed,
            Value = passed ? claimsPath : "unresolved",
            Detail = passed
                ? $"cwd resolves to host root '{expectedRoot}' and the claim path is '{claimsPath}'."
                : $"cwd did not resolve to a host root with a claim path (reported '{reportedRoot}', expected '{expectedRoot}').",
            Remedy = RemedyFor("claim-path-host-root", "unmarked"),
        };
    }

    private static SessionLayerSeatProbe ProbeRuntimeFamily()
    {
        var markers = EnvironmentFactory?.Invoke() ?? [];
        var marker = markers
            .Where(pair => pair.Key.StartsWith("INTENT_CLI_RUNTIME_", StringComparison.OrdinalIgnoreCase)
                || pair.Key.StartsWith("INTENT_RUNTIME_", StringComparison.OrdinalIgnoreCase)
                || pair.Key.StartsWith("AGENT_RUNTIME_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(pair => !string.IsNullOrWhiteSpace(pair.Value));
        var family = MarkerFamily(marker.Key);
        return new SessionLayerSeatProbe
        {
            Name = "runtime-family",
            Passed = true,
            Value = family,
            Detail = string.IsNullOrWhiteSpace(marker.Key)
                ? "No runtime marker was present; the generic remedy applies."
                : $"Detected marker prefix '{marker.Key[..(marker.Key.LastIndexOf('_') + 1)]}'.",
            Remedy = RemedyFor("runtime-family", family),
        };
    }

    private static string MarkerFamily(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "unmarked";
        if (key.StartsWith("INTENT_CLI_RUNTIME_", StringComparison.OrdinalIgnoreCase)) return "intent-cli-runtime";
        if (key.StartsWith("INTENT_RUNTIME_", StringComparison.OrdinalIgnoreCase)) return "intent-runtime";
        if (key.StartsWith("AGENT_RUNTIME_", StringComparison.OrdinalIgnoreCase)) return "agent-runtime";
        return "unmarked";
    }

    private static string RemedyFor(string probe, string family)
        => RemedyTable.TryGetValue($"{probe}|{family}", out var remedy)
            ? remedy
            : RemedyTable[$"{probe}|unmarked"];

    private static string CombineGitFailure(GitRemoteCommandResult created, GitRemoteCommandResult removed)
    {
        var create = TrimError(created);
        var remove = TrimError(removed);
        return $"temporary ref create exit={created.ExitCode} ({create}); cleanup exit={removed.ExitCode} ({remove}).";
    }

    private static string TrimError(GitRemoteCommandResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return string.IsNullOrWhiteSpace(text) ? "no diagnostic" : text.Trim();
    }

    private static GitRemoteCommandResult RunSafe(
        IGitRemoteCommandRunner runner,
        string repoRoot,
        IReadOnlyList<string> arguments)
    {
        try
        {
            return runner.Run(repoRoot, arguments);
        }
        catch (Exception exception)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 1,
                StdOut = string.Empty,
                StdErr = exception.Message,
            };
        }
    }

    private static void Emit(TextWriter writer, string format, SessionLayerSeatPreflightResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        writer.WriteLine("# Session-layer seat preflight (G808)");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- team: {result.Team}");
        writer.WriteLine($"- role: {result.Role}");
        writer.WriteLine($"- verdict: {(result.Passed ? "passed" : "failed")}");
        writer.WriteLine($"- launch source: {result.LaunchSource}");
        if (result.LaunchError is not null)
            writer.WriteLine($"- launch source note: {result.LaunchError}");
        writer.WriteLine($"- runtime family: {result.RuntimeFamily}");
        writer.WriteLine($"- ledger: {result.LedgerPath} (recorded: {result.LedgerRecorded.ToString().ToLowerInvariant()})");
        writer.WriteLine();
        foreach (var probe in result.Probes)
        {
            writer.WriteLine($"- **{probe.Name}** — {(probe.Passed ? "pass" : "fail")}; {probe.Detail}");
            if (!probe.Passed)
                writer.WriteLine($"  - remedy: {probe.Remedy}");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }

    private static bool TryParse(
        string[] args,
        out string? domain,
        out string? team,
        out string? role,
        out DateTimeOffset? launchAt,
        out string format,
        out string error)
    {
        domain = team = role = null;
        launchAt = null;
        format = FormatMarkdown;
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--domain":
                    domain = Next(args, ref index, "--domain", out error);
                    break;
                case "--team":
                    team = Next(args, ref index, "--team", out error);
                    break;
                case "--role":
                    role = Next(args, ref index, "--role", out error);
                    break;
                case "--launch-at":
                    var raw = Next(args, ref index, "--launch-at", out error);
                    DateTimeOffset parsed = default;
                    if (error.Length == 0 && (!DateTimeOffset.TryParse(raw, out parsed) || parsed == default))
                        error = "--launch-at must be an ISO-8601 timestamp.";
                    else if (error.Length == 0)
                        launchAt = parsed.ToUniversalTime();
                    break;
                case "--format":
                    format = Next(args, ref index, "--format", out error) ?? string.Empty;
                    if (error.Length == 0 && format is not (FormatJson or FormatMarkdown))
                        error = "--format must be markdown or json.";
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    break;
            }
            if (error.Length > 0) return false;
        }

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(role))
        {
            error = "--domain, --team, and --role are required.";
            return false;
        }
        return true;
    }

    private static string? Next(string[] args, ref int index, string option, out string error)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            error = $"{option} requires a value.";
            return null;
        }
        error = string.Empty;
        return args[++index].Trim();
    }
}

internal static class SessionLayerSeatCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        if (args.Length == 0 || string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine("Usage: intent-cli session-layer seat preflight --domain <domain> --team <team> --role <role> [--launch-at <timestamp>] [--format markdown|json]");
            return args.Length == 0 ? 1 : 0;
        }

        return args[0] switch
        {
            "preflight" => SessionLayerSeatPreflightCommand.Execute(context, args[1..], writer),
            _ => Unknown(args[0], writer),
        };
    }

    private static int Unknown(string name, TextWriter writer)
    {
        writer.WriteLine($"Unknown session-layer seat subcommand '{name}'.");
        return 1;
    }
}

internal sealed record SessionLayerSeatProbe
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("passed")] public required bool Passed { get; init; }
    [JsonPropertyName("value")] public required string Value { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("remedy")] public required string Remedy { get; init; }
}

internal sealed record SessionLayerSeatPreflightRecord
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("resident")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resident { get; init; }
    [JsonPropertyName("workspace_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceId { get; init; }
    [JsonPropertyName("pane_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaneId { get; init; }
    [JsonPropertyName("reader")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reader { get; init; }
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("launch_at")] public DateTimeOffset? LaunchAt { get; init; }
    [JsonPropertyName("launch_source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LaunchSource { get; init; }
    [JsonPropertyName("passed")] public required bool Passed { get; init; }
    [JsonPropertyName("runtime_family")] public required string RuntimeFamily { get; init; }
    [JsonPropertyName("probes")] public required IReadOnlyList<SessionLayerSeatProbe> Probes { get; init; }
}

internal sealed record RecordedSeatIdentity(
    string Resident,
    string? WorkspaceId,
    string? PaneId,
    string? Reader);

internal static class SessionLayerSeatIdentityMatcher
{
    public static string? Build(
        string? resident,
        string? workspace,
        string? pane,
        string? reader)
    {
        if (string.Equals(resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(workspace)
            && !string.IsNullOrWhiteSpace(pane))
        {
            return $"seat|{resident}|{workspace}|{pane}";
        }

        if (string.Equals(resident, NotifyRecordedRole.ExternalResident, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(reader))
        {
            return $"reader|{workspace}|{reader}";
        }

        return null;
    }

    public static bool MatchesRequested(
        SessionLayerSeatPreflightRecord record,
        string requestedRole,
        RecordedSeatIdentity? requestedSeat)
    {
        var requestedIdentity = requestedSeat is null
            ? null
            : Build(requestedSeat.Resident, requestedSeat.WorkspaceId, requestedSeat.PaneId, requestedSeat.Reader);
        var recordedIdentity = Build(record.Resident, record.WorkspaceId, record.PaneId, record.Reader);
        if (requestedIdentity is not null && recordedIdentity is not null)
        {
            return string.Equals(requestedIdentity, recordedIdentity, StringComparison.Ordinal);
        }

        // Legacy rows without identity may still serve the exact role, but
        // their normalized spelling must never bridge distinct seats.
        return recordedIdentity is null
            && string.Equals(record.Role, requestedRole, StringComparison.Ordinal);
    }
}

internal sealed record SessionLayerSeatIdentity(
    [property: JsonPropertyName("representative_role")] string RepresentativeRole,
    [property: JsonPropertyName("resident"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Resident,
    [property: JsonPropertyName("workspace_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WorkspaceId,
    [property: JsonPropertyName("pane_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaneId,
    [property: JsonPropertyName("reader"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reader,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string> Aliases)
{
    [JsonIgnore]
    public string Description =>
        $"representative_role={RepresentativeRole}; resident={Resident ?? "<missing>"}; "
        + $"workspace={WorkspaceId ?? "<missing>"}; pane={PaneId ?? "<missing>"}; "
        + $"reader={Reader ?? "<missing>"}; aliases={string.Join(",", Aliases)}";
}

internal sealed record SessionLayerSeatPreflightResult
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("role")] public required string Role { get; init; }
    [JsonPropertyName("passed")] public required bool Passed { get; init; }
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("launch_at")] public DateTimeOffset? LaunchAt { get; init; }
    [JsonPropertyName("launch_source")] public required string LaunchSource { get; init; }
    [JsonPropertyName("launch_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LaunchError { get; init; }
    [JsonPropertyName("ledger_path")] public required string LedgerPath { get; init; }
    [JsonPropertyName("ledger_recorded")] public required bool LedgerRecorded { get; init; }
    [JsonPropertyName("runtime_family")] public required string RuntimeFamily { get; init; }
    [JsonPropertyName("probes")] public required IReadOnlyList<SessionLayerSeatProbe> Probes { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record SessionLayerSeatPreflightAppendResult(bool Applied, string Path, string? Error);

internal sealed record SessionLayerSeatLaunchResolution(
    bool Resolved,
    DateTimeOffset? LaunchAt,
    string Source,
    string? Error)
{
    public static SessionLayerSeatLaunchResolution Success(DateTimeOffset launchAt, string source)
        => new(true, launchAt, source, null);

    public static SessionLayerSeatLaunchResolution Failure(string error)
        => new(false, null, "none", error);
}

internal static class SessionLayerSeatPreflightStore
{
    public static string ResolvePath(string repoRoot) => Path.Combine(
        Path.GetFullPath(repoRoot),
        SessionLayerSeatPreflightCommand.RelativeLedgerPath.Replace('/', Path.DirectorySeparatorChar));

    public static SessionLayerSeatPreflightAppendResult Append(string repoRoot, SessionLayerSeatPreflightRecord record)
    {
        var path = ResolvePath(repoRoot);
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var line = JsonSerializer.Serialize(record, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }) + Environment.NewLine;
            File.AppendAllText(path, line, new UTF8Encoding(false));
            return new SessionLayerSeatPreflightAppendResult(true, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SessionLayerSeatPreflightAppendResult(false, path, exception.Message);
        }
    }

    public static IReadOnlyList<SessionLayerSeatPreflightRecord> Read(string repoRoot, string domain, string team)
    {
        var path = ResolvePath(repoRoot);
        if (!File.Exists(path)) return [];
        var records = new List<SessionLayerSeatPreflightRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<SessionLayerSeatPreflightRecord>(line);
            if (record is not null
                && string.Equals(record.Domain, domain, StringComparison.Ordinal)
                && string.Equals(record.Team, team, StringComparison.Ordinal))
                records.Add(record);
        }
        return records;
    }

    public static IReadOnlyList<SessionLayerTopologyFinding> EvaluateLive(
        string repoRoot,
        string domain,
        string team,
        NotifyTeamTopology topology)
    {
        var records = Read(repoRoot, domain, team);
        var findings = new List<SessionLayerTopologyFinding>();
        foreach (var seat in GroupSeats(topology))
        {
            var latest = records
                .Where(seat.MatchesRecord)
                .OrderByDescending(record => record.ObservedAt)
                .FirstOrDefault();
            var launch = SessionLayerSeatPreflightCommand.ResolveLaunchAt(
                repoRoot,
                domain,
                team,
                seat.RepresentativeRole,
                explicitLaunchAt: null);
            var current = latest is not null
                && latest.Passed
                && launch.Resolved
                && launch.LaunchAt is { } currentLaunch
                && latest.ObservedAt >= currentLaunch;
            if (!current)
            {
                var missingLaunchSource = !launch.Resolved || launch.LaunchAt is null;
                findings.Add(new SessionLayerTopologyFinding(
                    seat.RepresentativeRole,
                    "seat_preflight",
                    missingLaunchSource
                        ? "seat-preflight-launch-source-missing"
                        : "seat-preflight-missing-or-stale",
                    missingLaunchSource
                        ? $"Recorded seat '{seat.RepresentativeRole}' ({seat.SeatIdentity.Description}) has no durable launch source; run "
                            + $"a verified model-resolution launch or supply --launch-at for representative role '{seat.RepresentativeRole}' before relying on freshness."
                        : $"Recorded seat '{seat.RepresentativeRole}' ({seat.SeatIdentity.Description}) has no passing seat preflight newer than its current durable launch time; run "
                    + "intent-cli session-layer seat preflight --domain <domain> --team <team> --role "
                    + $"{seat.RepresentativeRole} --format json.")
                {
                    // A missing preflight is an operator-visible measurement,
                    // not a topology contract violation. Existing topology
                    // validation must remain valid until a real seat is
                    // explicitly enrolled in this opt-in check.
                    IsInformational = true,
                    SeatIdentity = seat.SeatIdentity,
                });
            }
        }

        return findings;
    }

    private static IReadOnlyList<SeatGroup> GroupSeats(NotifyTeamTopology topology)
    {
        var groups = new List<SeatGroup>();
        foreach (var (role, record) in topology.Roles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var canonical = GuideRoleContractGuidance.Normalize(role) ?? role;
            var workspace = record.WorkspaceId ?? topology.WorkspaceId;
            var identity = SessionLayerSeatIdentityMatcher.Build(record.Resident, workspace, record.PaneId, record.Reader)
                ?? $"role|{canonical}";
            var group = groups.FirstOrDefault(candidate =>
                string.Equals(candidate.GroupingKey, identity, StringComparison.Ordinal));
            if (group is null)
            {
                groups.Add(new SeatGroup(identity, canonical, role, record, workspace, [role]));
            }
            else
            {
                group.AddRole(role, record, workspace);
            }
        }

        return groups;
    }

    private sealed class SeatGroup(
        string identity,
        string canonicalRole,
        string representativeRole,
        NotifyRecordedRole representativeRecord,
        string? workspaceId,
        IReadOnlyList<string> initialRoles)
    {
        public string GroupingKey { get; } = identity;
        public string CanonicalRole { get; } = canonicalRole;
        public string RepresentativeRole { get; private set; } = representativeRole;
        public NotifyRecordedRole RepresentativeRecord { get; private set; } = representativeRecord;
        public string? WorkspaceId { get; private set; } = workspaceId;
        public List<string> Roles { get; } = [.. initialRoles];
        public SessionLayerSeatIdentity SeatIdentity => new(
            RepresentativeRole,
            RepresentativeRecord.Resident,
            WorkspaceId,
            RepresentativeRecord.PaneId,
            RepresentativeRecord.Reader,
            Roles);

        public void AddRole(string role, NotifyRecordedRole record, string? workspaceId)
        {
            Roles.Add(role);
            if (string.Equals(role, CanonicalRole, StringComparison.Ordinal))
            {
                RepresentativeRole = role;
                RepresentativeRecord = record;
                WorkspaceId = workspaceId;
            }
        }
        public bool MatchesRecord(SessionLayerSeatPreflightRecord record)
        {
            var recordedIdentity = SessionLayerSeatIdentityMatcher.Build(
                record.Resident,
                record.WorkspaceId,
                record.PaneId,
                record.Reader);
            if (recordedIdentity is not null
                && !string.Equals(recordedIdentity, GroupingKey, StringComparison.Ordinal))
            {
                return false;
            }

            if (recordedIdentity is null && !Roles.Contains(record.Role, StringComparer.Ordinal))
            {
                return false;
            }

            if (Roles.Contains(record.Role, StringComparer.Ordinal)) return true;
            var normalized = GuideRoleContractGuidance.Normalize(record.Role) ?? record.Role;
            return string.Equals(normalized, CanonicalRole, StringComparison.Ordinal);
        }
    }

}
