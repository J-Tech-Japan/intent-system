namespace IntentSystem.Cli.Commands;

/// <summary>
/// The single delivery contract for a recorded logical role.  Residency
/// selects the evidence that can establish delivery: an external reader uses
/// a durable append, while a herdr-resident role uses its recorded pane wake.
/// Callers supply evidence; they do not reinterpret residency themselves.
/// </summary>
internal sealed record NotifyRecipientDeliveryJudgment
{
    public const string RecordedReaderAppendBasis = "recorded-reader-append";
    public const string RecordedPaneWakeBasis = "recorded-pane-wake";

    public required bool Resolved { get; init; }
    public required string Role { get; init; }
    public string? Resident { get; init; }
    public string? Basis { get; init; }
    public string? Target { get; init; }
    public string? Cause { get; init; }
    public required string Summary { get; init; }

    public bool UsesRecordedReaderAppend =>
        string.Equals(Basis, RecordedReaderAppendBasis, StringComparison.Ordinal);

    public bool Judge(bool readerAppendSucceeded, bool paneWakeDelivered) =>
        Resolved && (UsesRecordedReaderAppend ? readerAppendSucceeded : paneWakeDelivered);

    public static NotifyRecipientDeliveryJudgment Resolve(
        string routingRoot,
        string domain,
        string team,
        string role)
    {
        var topology = NotifyRoleTopologyStore.Resolve(routingRoot, domain, team);
        if (!topology.Resolved || topology.Topology is null)
        {
            return Failure(role, topology.Cause ?? "topology-unavailable", topology.Summary);
        }

        var target = NotifyRoleTopologyStore.ResolveDeliveryTarget(routingRoot, topology.Topology, role);
        if (!target.Resolved)
        {
            return Failure(role, target.Cause ?? "delivery-target-unavailable", target.Summary);
        }

        return FromRecordedTarget(role, target.Resident, target.Target, target.Summary);
    }

    public static NotifyRecipientDeliveryJudgment Resolve(NotifyPendingDelegation record) =>
        FromRecordedTarget(record.RecipientRole, record.Resident, record.Reader ?? record.PaneId,
            $"Resolved the delivery contract captured for pending task '{record.TaskId}'.");

    private static NotifyRecipientDeliveryJudgment FromRecordedTarget(
        string role,
        string? resident,
        string? target,
        string summary)
    {
        var basis = resident switch
        {
            NotifyRecordedRole.ExternalResident => RecordedReaderAppendBasis,
            NotifyRecordedRole.HerdrResident => RecordedPaneWakeBasis,
            _ => null,
        };
        if (resident is null)
        {
            // Pending records written before residency was captured keep the
            // conservative wake-based behavior.  Do not label that legacy
            // fallback as recorded evidence.
            return new NotifyRecipientDeliveryJudgment
            {
                Resolved = true,
                Role = role,
                Target = target,
                Summary = $"{summary} The legacy pending record has no residency; durable-append delivery cannot be asserted.",
            };
        }

        return basis is null
            ? Failure(role, "unsupported-resident",
                $"Recorded logical role '{role}' has unsupported residency '{resident ?? "<missing>"}'.")
            : new NotifyRecipientDeliveryJudgment
            {
                Resolved = true,
                Role = role,
                Resident = resident,
                Basis = basis,
                Target = target,
                Summary = $"{summary} Delivery basis is '{basis}'.",
            };
    }

    private static NotifyRecipientDeliveryJudgment Failure(string role, string cause, string summary) => new()
    {
        Resolved = false,
        Role = role,
        Cause = cause,
        Summary = summary,
    };
}
