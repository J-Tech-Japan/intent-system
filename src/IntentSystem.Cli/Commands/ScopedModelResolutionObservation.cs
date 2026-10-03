using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed record ScopedModelResolutionObservedEvidence(
    string Host,
    long Pid,
    string ProcessStartTimeUtc,
    string IdentitySource,
    string TopologyDigest,
    IReadOnlyList<string> RoleAliases,
    IReadOnlyList<string> ObservedArgv,
    string ObservedModel,
    string ObservedEffort);

internal sealed record ScopedModelResolutionObservationResult(
    bool Resolved,
    string Reason,
    ScopedModelResolutionTarget? Target,
    ScopedModelResolutionObservedEvidence? Evidence,
    string? Detail);

/// <summary>
/// Reads only one already-selected recorded pane and confirms its registration,
/// complete provider argv, local process generation and selected topology stayed
/// stable across the bounded observation window.
/// </summary>
internal sealed class ScopedModelResolutionTargetObserver
{
    private const string IdentitySource = "local-process-start-time";
    private readonly Func<string, string, string, NotifyTopologyResolution> topologyReader;
    private readonly INotifyProcessRunner processRunner;
    private readonly Func<long, Task<ScopedLocalProcessIdentityReadResult>> processIdentityReader;
    private readonly TimeSpan commandTimeout;

    public ScopedModelResolutionTargetObserver()
        : this(
            NotifyRoleTopologyStore.Resolve,
            new ScopedModelResolutionReaders(),
            new ScopedLocalProcessIdentityReader().ReadAsync,
            ScopedModelResolutionReaders.DefaultTimeout)
    {
    }

    internal ScopedModelResolutionTargetObserver(
        Func<string, string, string, NotifyTopologyResolution> topologyReader,
        INotifyProcessRunner processRunner,
        Func<long, Task<ScopedLocalProcessIdentityReadResult>> processIdentityReader,
        TimeSpan? commandTimeout = null)
    {
        this.topologyReader = topologyReader;
        this.processRunner = processRunner;
        this.processIdentityReader = processIdentityReader;
        this.commandTimeout = commandTimeout ?? ScopedModelResolutionReaders.DefaultTimeout;
        if (this.commandTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));
    }

    /// <summary>
    /// The supplied topology is T1. This method never reselects a pane: it
    /// revalidates the selected role only against T2 after the second process
    /// and OS observations have matched.
    /// </summary>
    public async Task<ScopedModelResolutionObservationResult> ObserveStableTarget(
        string routingRoot,
        NotifyTeamTopology initialTopology,
        string domain,
        string team,
        string role,
        string kind)
    {
        var initialSelection = ScopedModelResolutionTargetSelector.Select(
            initialTopology, domain, team, role, kind);
        if (!initialSelection.Resolved || initialSelection.Target is null)
            return Failure(initialSelection.Reason, initialSelection.Detail);
        var target = initialSelection.Target;

        var registrationBefore = await ReadRegistrationAsync(target).ConfigureAwait(false);
        if (!registrationBefore.Resolved)
            return Failure(registrationBefore.Reason, registrationBefore.Detail, target);

        var processBefore = await ReadProviderAsync(target.Kind, target.PaneId).ConfigureAwait(false);
        if (!processBefore.Resolved || processBefore.Process is null)
            return Failure(processBefore.Reason, processBefore.Detail, target);

        var identityBefore = await ReadIdentityAsync(processBefore.Process.Pid).ConfigureAwait(false);
        if (!identityBefore.Resolved || identityBefore.Identity is null)
            return Failure(identityBefore.Reason, identityBefore.Detail, target);

        var registrationAfter = await ReadRegistrationAsync(target).ConfigureAwait(false);
        if (!registrationAfter.Resolved)
            return Failure("observation-race", registrationAfter.Detail ?? "The selected registration changed or disappeared during observation.", target);

        var processAfter = await ReadProviderAsync(target.Kind, target.PaneId).ConfigureAwait(false);
        if (!processAfter.Resolved || processAfter.Process is null)
            return Failure("observation-race", processAfter.Detail ?? "The selected foreground provider changed or disappeared during observation.", target);

        if (processAfter.Process.Pid != processBefore.Process.Pid
            || !processBefore.Process.Argv.SequenceEqual(processAfter.Process.Argv, StringComparer.Ordinal))
            return Failure("observation-race", "The selected foreground provider PID or complete argv changed during observation.", target);

        var identityAfter = await ReadIdentityAsync(processAfter.Process.Pid).ConfigureAwait(false);
        if (!identityAfter.Resolved || identityAfter.Identity is null)
            return Failure("observation-race", identityAfter.Detail ?? "The second local process identity read failed.", target);

        var beforeIdentity = identityBefore.Identity;
        var afterIdentity = identityAfter.Identity;
        var beforeStartTimeUtc = beforeIdentity.ProcessStartTimeUtc!.Value.ToUniversalTime();
        var afterStartTimeUtc = afterIdentity.ProcessStartTimeUtc!.Value.ToUniversalTime();
        if (beforeIdentity.Pid != afterIdentity.Pid
            || !string.Equals(beforeIdentity.Host, afterIdentity.Host, StringComparison.Ordinal)
            || beforeStartTimeUtc.Ticks != afterStartTimeUtc.Ticks)
            return Failure("observation-race", "The local host, PID, or actual process start time changed during observation.", target);

        NotifyTopologyResolution topologyAfter;
        try
        {
            topologyAfter = topologyReader(routingRoot, domain, team);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or JsonException)
        {
            return Failure("topology-mismatch", $"The selected topology could not be re-read: {exception.Message}", target);
        }

        if (!topologyAfter.Resolved || topologyAfter.Topology is null)
            return Failure("topology-mismatch", topologyAfter.Summary, target);
        var finalSelection = ScopedModelResolutionTargetSelector.Select(
            topologyAfter.Topology, domain, team, role, kind);
        if (!finalSelection.Resolved || finalSelection.Target is null)
            return Failure("topology-mismatch", finalSelection.Detail, target);
        if (!SameSelectedTarget(target, finalSelection.Target))
            return Failure("topology-mismatch", "The selected role identity or scoped topology digest changed during observation.", target);

        var actualStartTime = beforeStartTimeUtc;
        var argv = processBefore.Process.Argv.ToArray();
        return new ScopedModelResolutionObservationResult(
            true,
            "target-current",
            target,
            new ScopedModelResolutionObservedEvidence(
                beforeIdentity.Host!,
                processBefore.Process.Pid,
                actualStartTime.ToString("O", CultureInfo.InvariantCulture),
                IdentitySource,
                target.TopologyDigest,
                target.RoleAliases.ToArray(),
                argv,
                processBefore.Process.Model,
                processBefore.Process.Effort),
            null);
    }

    private async Task<RegistrationReadResult> ReadRegistrationAsync(ScopedModelResolutionTarget target)
    {
        var response = await ReadHerdrAsync(["agent", "list"]).ConfigureAwait(false);
        if (!response.Resolved || response.Result is null)
            return RegistrationReadResult.Failure("identity-unavailable", response.Detail);
        if (response.Result.ExitCode != 0)
            return RegistrationReadResult.Failure("identity-unavailable", "herdr agent list returned a nonzero exit code.");
        return ScopedModelResolutionObservationParser.ParseRegistration(
            response.Result.StandardOutput, target);
    }

    private async Task<ProviderReadResult> ReadProviderAsync(string kind, string paneId)
    {
        var response = await ReadHerdrAsync(["pane", "process-info", "--pane", paneId]).ConfigureAwait(false);
        if (!response.Resolved || response.Result is null)
            return ProviderReadResult.Failure("argv-unreadable", response.Detail);
        if (response.Result.ExitCode != 0)
            return ProviderReadResult.Failure("argv-unreadable", "herdr pane process-info returned a nonzero exit code.");
        return ScopedModelResolutionObservationParser.ParseProvider(
            response.Result.StandardOutput, kind);
    }

    private async Task<HerdrReadResult> ReadHerdrAsync(IReadOnlyList<string> arguments)
    {
        try
        {
            var result = await processRunner.RunAsync(
                    NotifyTransportPaths.ResolveHerdrExecutable(), arguments, CancellationToken.None)
                .WaitAsync(commandTimeout)
                .ConfigureAwait(false);
            return new HerdrReadResult(true, result, null);
        }
        catch (TimeoutException)
        {
            return new HerdrReadResult(false, null, "The Herdr read exceeded its bounded deadline.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception
            or OperationCanceledException)
        {
            return new HerdrReadResult(false, null, exception.Message);
        }
    }

    private async Task<IdentityReadResult> ReadIdentityAsync(long pid)
    {
        try
        {
            var result = await processIdentityReader(pid).WaitAsync(commandTimeout).ConfigureAwait(false);
            if (!result.Resolved || result.ProcessStartTimeUtc is null || string.IsNullOrWhiteSpace(result.Host))
                return IdentityReadResult.Failure(
                    "identity-unavailable",
                    result.TimedOut
                        ? "The local process identity read exceeded its bounded deadline."
                        : "The local process identity was unavailable.");
            if (result.Pid != pid)
                return IdentityReadResult.Failure("identity-unavailable", "The local process identity PID did not match the selected foreground PID.");
            return IdentityReadResult.Success(new ScopedLocalProcessIdentityReadResult(
                true, pid, result.Host, result.ProcessStartTimeUtc.Value.ToUniversalTime(), false, null));
        }
        catch (TimeoutException)
        {
            return IdentityReadResult.Failure("identity-unavailable", "The local process identity read exceeded its bounded deadline.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception
            or ArgumentException
            or OperationCanceledException)
        {
            return IdentityReadResult.Failure("identity-unavailable", exception.Message);
        }
    }

    private static bool SameSelectedTarget(
        ScopedModelResolutionTarget expected,
        ScopedModelResolutionTarget actual) =>
        string.Equals(expected.Domain, actual.Domain, StringComparison.Ordinal)
        && string.Equals(expected.Team, actual.Team, StringComparison.Ordinal)
        && string.Equals(expected.Role, actual.Role, StringComparison.Ordinal)
        && string.Equals(expected.WorkspaceId, actual.WorkspaceId, StringComparison.Ordinal)
        && string.Equals(expected.PaneId, actual.PaneId, StringComparison.Ordinal)
        && string.Equals(expected.Kind, actual.Kind, StringComparison.Ordinal)
        && string.Equals(expected.TopologyDigest, actual.TopologyDigest, StringComparison.Ordinal)
        && expected.RoleAliases.SequenceEqual(actual.RoleAliases, StringComparer.Ordinal);

    private static ScopedModelResolutionObservationResult Failure(
        string reason,
        string? detail,
        ScopedModelResolutionTarget? target = null) =>
        new(false, reason, target, null, detail);

    private sealed record HerdrReadResult(bool Resolved, NotifyProcessResult? Result, string? Detail);
    private sealed record IdentityReadResult(bool Resolved, string Reason, ScopedLocalProcessIdentityReadResult? Identity, string? Detail)
    {
        public static IdentityReadResult Success(ScopedLocalProcessIdentityReadResult identity) => new(true, "target-current", identity, null);
        public static IdentityReadResult Failure(string reason, string? detail) => new(false, reason, null, detail);
    }
}

internal static class ScopedModelResolutionObservationParser
{
    public static RegistrationReadResult ParseRegistration(
        string json,
        ScopedModelResolutionTarget target)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryProperty(root, "result", JsonValueKind.Object, out var result)
                || !TryProperty(result, "agents", JsonValueKind.Array, out var agents))
                return RegistrationReadResult.Failure("identity-unavailable", "herdr agent list has an unknown response shape.");

            var addressed = new List<JsonElement>();
            foreach (var agent in agents.EnumerateArray())
            {
                if (agent.ValueKind != JsonValueKind.Object)
                    return RegistrationReadResult.Failure("identity-unavailable", "herdr agent list contains a non-object registration.");
                if (!TryOptionalString(agent, "workspace_id", out var workspace)
                    || !TryOptionalString(agent, "pane_id", out var pane))
                    return RegistrationReadResult.Failure("identity-unavailable", "herdr registration workspace_id or pane_id has an unknown shape.");
                if (string.Equals(workspace, target.WorkspaceId, StringComparison.Ordinal)
                    && string.Equals(pane, target.PaneId, StringComparison.Ordinal))
                    addressed.Add(agent);
            }

            if (addressed.Count > 1)
                return RegistrationReadResult.Failure("target-ambiguous", "Multiple Herdr registrations address the exact recorded workspace and pane.");
            if (addressed.Count == 0)
                return RegistrationReadResult.Failure("target-absent", "No Herdr registration addresses the exact recorded workspace and pane.");

            var selected = addressed[0];
            if (!TryOptionalString(selected, "agent", out var agentKind))
                return RegistrationReadResult.Failure("identity-unavailable", "The exact recorded registration has an unknown agent kind shape.");
            if (!string.Equals(agentKind, target.Kind, StringComparison.OrdinalIgnoreCase))
                return RegistrationReadResult.Failure("target-absent", "The exact recorded pane has no registration for the selected provider kind.");
            if (!TryOptionalBoolean(selected, "interactive_ready", out var interactiveReady))
                return RegistrationReadResult.Failure("identity-unavailable", "The selected registration has an unknown interactive_ready shape.");
            if (!TryOptionalString(selected, "agent_status", out var agentStatus))
                return RegistrationReadResult.Failure("identity-unavailable", "The selected registration has an unknown agent_status shape.");
            if (interactiveReady == false
                || string.Equals(agentStatus, "unknown", StringComparison.OrdinalIgnoreCase)
                || !TryProperty(selected, "agent_session", JsonValueKind.Object, out _))
                return RegistrationReadResult.Failure("target-absent", "The exact recorded registration is not eligible for observation.");

            return RegistrationReadResult.Success();
        }
        catch (JsonException exception)
        {
            return RegistrationReadResult.Failure("identity-unavailable", $"herdr agent list returned invalid JSON: {exception.Message}");
        }
    }

    public static ProviderReadResult ParseProvider(string json, string kind)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryProperty(root, "result", JsonValueKind.Object, out var result)
                || !TryProperty(result, "process_info", JsonValueKind.Object, out var processInfo)
                || !TryProperty(processInfo, "foreground_processes", JsonValueKind.Array, out var foreground))
                return ProviderReadResult.Failure("argv-unreadable", "herdr pane process-info has an unknown response shape.");

            var providers = new List<ForegroundProvider>();
            foreach (var process in foreground.EnumerateArray())
            {
                if (process.ValueKind != JsonValueKind.Object
                    || !TryProperty(process, "pid", JsonValueKind.Number, out var pidElement)
                    || !pidElement.TryGetInt64(out var pid)
                    || pid <= 0
                    || !TryProperty(process, "argv", JsonValueKind.Array, out var argvElement))
                    return ProviderReadResult.Failure("argv-unreadable", "A foreground process lacks an object shape, positive Int64 PID, or argv array.");

                var argv = new List<string>();
                foreach (var token in argvElement.EnumerateArray())
                {
                    if (token.ValueKind != JsonValueKind.String || token.GetString() is not { } value)
                        return ProviderReadResult.Failure("argv-unreadable", "A foreground argv contains a non-string token.");
                    argv.Add(value);
                }
                if (argv.Count == 0)
                    return ProviderReadResult.Failure("argv-unreadable", "A foreground process has an empty argv array.");

                var executable = Path.GetFileNameWithoutExtension(argv[0]);
                if (string.Equals(executable, kind, StringComparison.OrdinalIgnoreCase))
                    providers.Add(new ForegroundProvider(pid, argv));
            }

            if (providers.Count > 1)
                return ProviderReadResult.Failure("target-ambiguous", "Multiple foreground processes match the selected provider executable.");
            if (providers.Count == 0)
                return ProviderReadResult.Failure("target-absent", "No foreground process matches the selected provider executable.");

            var selectedProvider = providers[0];
            var parsed = ModelResolutionInvocationParser.ParseArgv(selectedProvider.Argv, kind);
            if (!parsed.Resolved || parsed.Model is null || parsed.Effort is null)
                return ProviderReadResult.Failure(
                    parsed.Reason == "model-effort-missing" ? "model-effort-missing" : "argv-unreadable",
                    $"The selected foreground argv lacks an unambiguous explicit model and effort: {parsed.Reason ?? "unknown"}.");

            return ProviderReadResult.Success(new ScopedObservedProvider(
                selectedProvider.Pid,
                selectedProvider.Argv,
                parsed.Model,
                parsed.Effort));
        }
        catch (JsonException exception)
        {
            return ProviderReadResult.Failure("argv-unreadable", $"herdr pane process-info returned invalid JSON: {exception.Message}");
        }
    }

    private static bool TryProperty(
        JsonElement element,
        string property,
        JsonValueKind expectedKind,
        out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out value)
            && value.ValueKind == expectedKind;
    }

    private static bool TryOptionalString(JsonElement element, string property, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out var propertyValue))
            return true;
        if (propertyValue.ValueKind != JsonValueKind.String)
            return false;
        value = propertyValue.GetString();
        return true;
    }

    private static bool TryOptionalBoolean(JsonElement element, string property, out bool? value)
    {
        value = null;
        if (!element.TryGetProperty(property, out var propertyValue))
            return true;
        if (propertyValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = propertyValue.GetBoolean();
        return true;
    }

    private sealed record ForegroundProvider(long Pid, IReadOnlyList<string> Argv);
}

internal sealed record RegistrationReadResult(bool Resolved, string Reason, string? Detail)
{
    public static RegistrationReadResult Success() => new(true, "target-current", null);
    public static RegistrationReadResult Failure(string reason, string? detail) => new(false, reason, detail);
}

internal sealed record ScopedObservedProvider(
    long Pid,
    IReadOnlyList<string> Argv,
    string Model,
    string Effort);

internal sealed record ProviderReadResult(bool Resolved, string Reason, ScopedObservedProvider? Process, string? Detail)
{
    public static ProviderReadResult Success(ScopedObservedProvider process) => new(true, "target-current", process, null);
    public static ProviderReadResult Failure(string reason, string? detail) => new(false, reason, null, detail);
}
