using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G809: the notify delegate assignment contract is shared by the design and
/// orchestrator guides so neither surface can teach the old event-kind
/// destination substitution.
/// </summary>
internal sealed record NotifyDelegateAssignmentGuidance
{
    public const string HelpText =
        "G809 assignment contract: notify delegate preserves the explicit --to assignee after canonical role normalization and topology validation; event-kind inference never substitutes a destination. --report-to remains the reporting contract. Use --routing-root <host-root> and run the same command with --dry-run --format json to verify the recipient without a receiver call or durable write before --write. Report/escalate routing and Steward authority guards remain unchanged; historical misroutes are not replayed.";

    [JsonPropertyName("precedence")]
    public required string Precedence { get; init; }

    [JsonPropertyName("validation")]
    public required string Validation { get; init; }

    [JsonPropertyName("dry_run")]
    public required string DryRun { get; init; }

    [JsonPropertyName("authority")]
    public required string Authority { get; init; }

    [JsonPropertyName("historical_records")]
    public required string HistoricalRecords { get; init; }

    [JsonPropertyName("examples")]
    public required IReadOnlyList<string> Examples { get; init; }

    public static NotifyDelegateAssignmentGuidance Create(string domain, string team) => new()
    {
        Precedence = "For `notify delegate`, the explicit `--to` assignee wins after LogicalRoleNormalizer canonicalization. Event-kind inference remains task context and an authority-check input; it never substitutes Architect, Steward, or another default destination. `--report-to` remains the caller-declared reporting contract.",
        Validation = $"Resolve the canonical assignee against the recorded team topology before delivery. Use the explicit `--routing-root <host-root>` and `--domain {domain} --team {team}` so the same validated role appears in the result `to_role`, TASK role, pending recipient, and generated report command.",
        DryRun = "Run the identical command with `--dry-run --format json` to verify the recipient before sending: it performs topology/role resolution but invokes no receiver and changes no durable bytes. The subsequent `--write` sends exactly once to that recorded assignee.",
        Authority = "The existing Steward boundary remains binding: explicit destination does not grant judgement authority. Report/escalate retain their G796 event-kind routing, ruling and downstream-evidence guards, and no authority is manufactured from `--to`.",
        HistoricalRecords = "This correction is forward-only. It does not replay, retarget, resend, rewrite, settle, or delete historical misrouted dispatch/delivery records.",
        Examples =
        [
            $"`intent-cli notify delegate --domain {domain} --team {team} --from architect --to orchestrator --report-to architect --task-id <task-id> --objective <objective> --expected-artifact <artifact> --result-nonce <nonce> --routing-root <host-root> --dry-run --format json`",
            $"`intent-cli notify delegate --domain {domain} --team {team} --from orchestrator --to builder --report-to orchestrator --task-id <task-id> --objective <objective> --expected-artifact <artifact> --result-nonce <nonce> --routing-root <host-root> --write --format json`",
        ],
    };
}
