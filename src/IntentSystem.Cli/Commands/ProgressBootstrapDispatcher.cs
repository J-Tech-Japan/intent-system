using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record ProgressBootstrapRequest
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("project")] public required string Project { get; init; }
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("unit")] public required string Unit { get; init; }
    [JsonPropertyName("task_id")] public required string TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public required string ResultNonce { get; init; }
    [JsonPropertyName("intended_recipient")] public required string IntendedRecipient { get; init; }
    [JsonPropertyName("delegating_role")] public required string DelegatingRole { get; init; }
    [JsonPropertyName("expected_artifact")] public required string ExpectedArtifact { get; init; }
    [JsonPropertyName("failed_primary_route")] public required string FailedPrimaryRoute { get; init; }
    [JsonPropertyName("claim_valid")] public bool ClaimValid { get; init; }
    [JsonPropertyName("wip_valid")] public bool WipValid { get; init; }
    [JsonPropertyName("preflight_valid")] public bool PreflightValid { get; init; }
    [JsonPropertyName("published")] public bool Published { get; init; }
    [JsonPropertyName("command")] public string? Command { get; init; }
    [JsonPropertyName("event_root")] public string? EventRoot { get; init; }
}

internal sealed record ProgressBootstrapDispatchResult
{
    [JsonPropertyName("accepted")] public bool Accepted { get; init; }
    [JsonPropertyName("already_converged")] public bool AlreadyConverged { get; init; }
    [JsonPropertyName("idempotency_key")] public required string IdempotencyKey { get; init; }
    [JsonPropertyName("action")] public required string Action { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("task_identity")] public string? TaskIdentity { get; init; }
    [JsonPropertyName("pending_identity")] public string? PendingIdentity { get; init; }
    [JsonPropertyName("delivery_identity")] public string? DeliveryIdentity { get; init; }
}

internal interface IProgressBootstrapAdapter
{
    ProgressBootstrapDispatchResult Dispatch(ProgressBootstrapRequest request, string idempotencyKey);
}

/// <summary>
/// Command-owned bootstrap gate. It validates the original published task and
/// recipient before invoking an independently supplied host adapter. Arbitrary
/// event commands and roots never become executable transport instructions.
/// </summary>
internal sealed class ProgressBootstrapDispatcher
{
    private readonly IProgressBootstrapAdapter adapter;
    private readonly HashSet<string> dispatched = new(StringComparer.Ordinal);

    public ProgressBootstrapDispatcher(IProgressBootstrapAdapter adapter) => this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

    public ProgressBootstrapDispatchResult Dispatch(ProgressBootstrapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = string.Join("/", request.Domain, request.Team, request.Repo, request.Unit, request.TaskId, request.ResultNonce);
        if (dispatched.Contains(key))
        {
            return new ProgressBootstrapDispatchResult
            {
                Accepted = true,
                AlreadyConverged = true,
                IdempotencyKey = key,
                Action = "canonical-bootstrap-dispatch",
                Summary = "The canonical bootstrap dispatch is already recorded; no duplicate semantic task was emitted.",
            };
        }

        var error = Validate(request);
        if (error is not null)
        {
            return new ProgressBootstrapDispatchResult
            {
                Accepted = false,
                IdempotencyKey = key,
                Action = "infrastructure-escalation",
                Summary = "Bootstrap refused before transport.",
                Error = error,
            };
        }

        var result = adapter.Dispatch(request, key);
        if (result.Accepted || result.AlreadyConverged) dispatched.Add(key);
        return result with { IdempotencyKey = key, Action = "canonical-bootstrap-dispatch" };
    }

    private static string? Validate(ProgressBootstrapRequest request)
    {
        if (!request.Published) return "published-unit-required";
        if (!request.ClaimValid) return "claim-or-ownership-refused";
        if (!request.WipValid) return "wip-authority-refused";
        if (!request.PreflightValid) return "recipient-preflight-refused";
        if (!string.Equals(request.DelegatingRole, "orchestrator", StringComparison.OrdinalIgnoreCase)) return "bootstrap-owner-must-be-orchestrator";
        if (string.IsNullOrWhiteSpace(request.FailedPrimaryRoute)) return "failed-primary-route-evidence-required";
        if (string.IsNullOrWhiteSpace(request.ExpectedArtifact)) return "expected-artifact-required";
        if (string.IsNullOrWhiteSpace(request.TaskId) || string.IsNullOrWhiteSpace(request.ResultNonce)) return "task-and-nonce-required";
        if (!string.Equals(request.IntendedRecipient, "builder", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.IntendedRecipient, "reviewer", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.IntendedRecipient, "orchestrator", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.IntendedRecipient, "steward", StringComparison.OrdinalIgnoreCase)) return "wrong-intended-recipient";
        if (request.Command is not null || request.EventRoot is not null) return "event-supplied-command-or-root-refused";
        return null;
    }
}

internal sealed class RecordingProgressBootstrapAdapter : IProgressBootstrapAdapter
{
    private readonly List<(ProgressBootstrapRequest Request, string Key)> calls = [];
    public IReadOnlyList<(ProgressBootstrapRequest Request, string Key)> Calls => calls;

    public ProgressBootstrapDispatchResult Dispatch(ProgressBootstrapRequest request, string idempotencyKey)
    {
        calls.Add((request, idempotencyKey));
        return new ProgressBootstrapDispatchResult
        {
            Accepted = true,
            AlreadyConverged = false,
            IdempotencyKey = idempotencyKey,
            Action = "canonical-bootstrap-dispatch",
            Summary = "Validated host-side bootstrap wake recorded with attributed task, pending, and delivery identities.",
            TaskIdentity = $"{request.Domain}/{request.Team}/{request.Unit}/{request.TaskId}/{request.ResultNonce}",
            PendingIdentity = $"pending:{idempotencyKey}",
            DeliveryIdentity = $"delivery:{idempotencyKey}",
        };
    }
}

/// <summary>
/// Production adapter for the existing G809/G811 command-owned records. The
/// controller supplies the validated request; this adapter only appends the
/// normal pending and delivery identities and never changes topology.
/// </summary>
internal sealed class NotifyProgressBootstrapAdapter : IProgressBootstrapAdapter
{
    private readonly string routingRoot;
    private readonly DateTimeOffset now;

    public NotifyProgressBootstrapAdapter(string routingRoot, DateTimeOffset now)
    {
        this.routingRoot = Path.GetFullPath(routingRoot);
        this.now = now.ToUniversalTime();
    }

    public ProgressBootstrapDispatchResult Dispatch(ProgressBootstrapRequest request, string idempotencyKey)
    {
        var record = new NotifyPendingDelegation
        {
            Domain = request.Domain,
            Team = request.Team,
            TaskId = request.TaskId,
            TaskKind = "progress-bootstrap",
            DelegatingRole = request.DelegatingRole,
            RecipientRole = request.IntendedRecipient,
            ReportToRole = "orchestrator",
            RecipientIdentity = $"{request.IntendedRecipient}:{request.Unit}",
            ExpectedArtifact = request.ExpectedArtifact,
            Objective = $"Canonical bootstrap for {request.Unit}",
            Question = "Continue the published unit through its validated original recipient.",
            ResultNonce = request.ResultNonce,
            DispatchedAt = now,
            TransportMode = "validated-host-adapter",
            Kind = "progress-bootstrap",
        };
        var pending = NotifyPendingDelegationStore.WriteDispatch(routingRoot, record);
        if (!pending.Written)
        {
            return new ProgressBootstrapDispatchResult
            {
                Accepted = false,
                IdempotencyKey = idempotencyKey,
                Action = "infrastructure-escalation",
                Summary = "Canonical bootstrap pending record could not be written.",
                Error = pending.Error,
            };
        }

        var delivery = NotifyDelegationDeliveryStore.Write(routingRoot, record, now);
        if (!delivery.Written)
        {
            return new ProgressBootstrapDispatchResult
            {
                Accepted = false,
                IdempotencyKey = idempotencyKey,
                Action = "infrastructure-escalation",
                Summary = "Canonical bootstrap pending record exists but delivery evidence is unavailable.",
                Error = delivery.Error,
                TaskIdentity = record.TaskId,
                PendingIdentity = pending.Path,
            };
        }

        return new ProgressBootstrapDispatchResult
        {
            Accepted = true,
            IdempotencyKey = idempotencyKey,
            Action = "canonical-bootstrap-dispatch",
            Summary = "Validated canonical pending and delivery identities were recorded exactly once.",
            TaskIdentity = record.TaskId,
            PendingIdentity = pending.Path,
            DeliveryIdentity = delivery.Path,
        };
    }
}
