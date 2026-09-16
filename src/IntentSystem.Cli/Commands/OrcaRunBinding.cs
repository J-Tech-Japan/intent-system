using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

internal static partial class OrcaRunBinding
{
    public const string RecordedHealth = "recorded";
    public const string TopologyRoleLocation = "topology-role";
    public const string OrcaRunFileLocation = "orca-run-file";
    public const string OrcaPushPolicy = "orca-push";
    public const string InboxPullPolicy = "inbox-pull";
    public const string SuccessSummarySuffix =
        "Recorded only; intent-cli did not run orca or verify the Run.";

    public const string ReceiveInstructionOrcaPush =
        "Receive by Orca push: this seat runs in an Orca terminal; intent-cli sends and wakes nothing.";
    public const string ReceiveInstructionInboxPull =
        "Receive by scheduled pull: this app seat reads its canonical inbox and status once per scheduled wake; intent-cli schedules nothing.";

    [GeneratedRegex("^run_[0-9a-f]{12}$")]
    private static partial Regex RunIdPattern();

    public static bool IsValidRunId(string? value) =>
        value is not null && RunIdPattern().IsMatch(value);

    public static bool IsValidReceivePolicy(string? value) =>
        value is OrcaPushPolicy or InboxPullPolicy;

    public static OrcaRunRoleBinding ParseRoleBindingNode(JsonNode node)
    {
        if (node is not JsonObject obj)
        {
            return new OrcaRunRoleBinding(null, null, false, "orca-run-binding-malformed");
        }

        obj.TryGetPropertyValue("run_id", out var runIdNode);
        obj.TryGetPropertyValue("receive_policy", out var policyNode);

        string? runId = null;
        string? policy = null;
        var malformed = false;

        if (runIdNode is null)
        {
            malformed = true;
        }
        else if (runIdNode is JsonValue runIdValue && runIdValue.TryGetValue(out string? parsedRunId))
        {
            runId = parsedRunId;
        }
        else
        {
            malformed = true;
        }

        if (policyNode is not null)
        {
            if (policyNode is JsonValue policyValue && policyValue.TryGetValue(out string? parsedPolicy))
            {
                policy = parsedPolicy;
            }
            else
            {
                malformed = true;
            }
        }

        return new OrcaRunRoleBinding(runId, policy, !malformed, malformed ? "orca-run-binding-malformed" : null);
    }

    public static string? EvaluateReceivePolicyForSeat(
        NotifyRecordedRole record,
        string? requestedPolicy,
        out string? cause,
        out string? fix)
    {
        cause = null;
        fix = null;
        if (string.Equals(record.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
        {
            cause = "receive-policy-herdr-seat";
            fix = BuildHerdrFixPlaceholder();
            return null;
        }

        var frontend = record.Frontend;
        if (string.IsNullOrWhiteSpace(frontend)
            || frontend is not ("orca" or "claude-app" or "codex-app"))
        {
            cause = "receive-policy-seat-kind-unrecorded";
            fix = "intent-cli session-layer topology update-field --field frontend --current <value|absent> --new orca|claude-app|codex-app …";
            return null;
        }

        if (requestedPolicy is null)
        {
            return null;
        }

        var expected = frontend == "orca" ? OrcaPushPolicy : InboxPullPolicy;
        if (!string.Equals(requestedPolicy, expected, StringComparison.Ordinal))
        {
            cause = "receive-policy-seat-mismatch";
            return null;
        }

        return requestedPolicy;
    }

    public static string BuildHerdrFix(string domain, string team, string role) =>
        $"intent-cli session-layer topology update-residence --domain {domain} --team {team} --role {role} "
        + "--current-resident herdr --new-resident external --reader <routing-root-relative-path> --frontend orca "
        + "--confirm-update-residence --dry-run --format json";

    private static string BuildHerdrFixPlaceholder() =>
        "intent-cli session-layer topology update-residence --domain <d> --team <t> --role <role> "
        + "--current-resident herdr --new-resident external --reader <routing-root-relative-path> --frontend orca "
        + "--confirm-update-residence --dry-run --format json";

    public static string BuildRecordOrcaRunCommand(string domain, string team, string role) =>
        $"intent-cli session-layer topology record-orca-run --domain {domain} --team {team} --role {role} "
        + "--current absent --new <run-id> --receive-policy <orca-push|inbox-pull> --confirm-record-orca-run --dry-run --format json";

    public static string BuildClearRecordOrcaRunCommand(string domain, string team, string role, string currentId) =>
        $"intent-cli session-layer topology record-orca-run --domain {domain} --team {team} --role {role} "
        + $"--current {currentId} --new absent --confirm-record-orca-run --write --format json";

    public static string? ReceiveInstructionForPolicy(string? policy) => policy switch
    {
        OrcaPushPolicy => ReceiveInstructionOrcaPush,
        InboxPullPolicy => ReceiveInstructionInboxPull,
        _ => null,
    };
}

internal sealed record OrcaRunRoleBinding(
    string? RunId,
    string? ReceivePolicy,
    bool WellFormed,
    string? MalformedCause);

internal sealed record OrcaRunShownBinding
{
    public string? RunId { get; init; }
    public string? ReceivePolicy { get; init; }
    public required string Health { get; init; }
}
