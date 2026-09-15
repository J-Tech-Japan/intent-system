using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834: the one authoritative team resolution shared by
/// <c>review cross-runtime record</c>, <c>review cross-runtime status</c>, and
/// <c>automation pr-transition --transition approved</c>. Callers never pass
/// domain or team as authority:
/// <list type="bullet">
/// <item>PR to unit — the host <c>queue-state.json</c> item whose
///   <c>linked_pr</c> matches (URL string, object, or legacy bare number);</item>
/// <item>domain — the packet's <c>implementation_issue_packet.domain</c>;</item>
/// <item>team — the <c>team</c> on the execution unit's held claim, read through
///   the reader <c>claim verify</c> uses.</item>
/// </list>
/// </summary>
internal static class CrossRuntimeReviewTeamResolver
{
    public const string SourceQueueLinkedPr = "queue-state:linked_pr";
    public const string SourceExecutionUnitArgument = "argument:--execution-unit";
    public const string SourcePacketDomain = "packet:implementation_issue_packet.domain";
    public const string SourceClaimTeam = "claim:execution-unit";

    /// <summary>
    /// Test seam for the claim read. Production calls
    /// <see cref="ClaimOwnershipVerifier.Verify"/> with no invoking team, the
    /// same reader <c>claim verify</c> uses.
    /// </summary>
    internal static Func<string, string, ClaimOwnershipVerification>? ClaimReader { get; set; }

    public static CrossRuntimeReviewResolution Resolve(
        string repoRoot,
        string repo,
        int pr,
        string? executionUnitArgument,
        string executionUnitFixCommand)
    {
        var prText = pr.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var queueStatePath = CliRuntimeContracts.GetQueueStatePath(repoRoot);
        if (!File.Exists(queueStatePath))
        {
            return Unresolved(
                "queue-linkage",
                $"host queue-state '{queueStatePath}' does not exist, so PR #{prText} cannot be resolved to an execution unit.",
                "run from the host root that owns `.intent-cli/queue-state.json`.");
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
        {
            return Unresolved(
                "queue-linkage",
                $"host queue-state could not be read: {exception.Message}",
                "repair `.intent-cli/queue-state.json` through the canonical queue commands.");
        }

        var linked = queueState.Items
            .Where(item => GitHubWorkItemIdentity.MatchesPullRequest(item, repo, pr))
            .ToArray();
        var linkageFix =
            $"link the PR from the host with `intent-cli worker complete --repo {repo} --kind issue --number <issue> --outcome pr-created --pr {prText} --write`, "
            + $"or {executionUnitFixCommand}.";
        if (linked.Length > 1)
        {
            return Unresolved(
                "queue-linkage",
                $"more than one queue item links PR #{prText} in {repo} ({string.Join(", ", linked.Select(item => item.ExecutionUnit))}).",
                "repair the duplicate queue linkage so exactly one execution unit links the PR.");
        }

        QueueItem item;
        string unitSource;
        if (linked.Length == 1)
        {
            item = linked[0];
            unitSource = SourceQueueLinkedPr;
            if (!string.IsNullOrWhiteSpace(executionUnitArgument)
                && !string.Equals(executionUnitArgument, item.ExecutionUnit, StringComparison.Ordinal))
            {
                return Mismatch(
                    $"PR #{prText} in {repo} is linked to execution unit '{item.ExecutionUnit}', not '{executionUnitArgument}'.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(executionUnitArgument))
        {
            var named = queueState.Items
                .Where(candidate => string.Equals(candidate.ExecutionUnit, executionUnitArgument, StringComparison.Ordinal))
                .ToArray();
            if (named.Length != 1)
            {
                return Unresolved(
                    "queue-linkage",
                    named.Length == 0
                        ? $"PR #{prText} has no linked queue item and execution unit '{executionUnitArgument}' has no queue item."
                        : $"execution unit '{executionUnitArgument}' has more than one queue item.",
                    linkageFix);
            }

            item = named[0];
            if (!string.IsNullOrWhiteSpace(item.LinkedPr))
            {
                return Mismatch(
                    $"execution unit '{executionUnitArgument}' is linked to '{item.LinkedPr}', not PR #{prText} in {repo}.");
            }

            unitSource = SourceExecutionUnitArgument;
        }
        else
        {
            return Unresolved(
                "queue-linkage",
                $"no queue item links PR #{prText} in {repo}.",
                linkageFix);
        }

        var unit = item.ExecutionUnit;
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(unit, out var unitError))
        {
            return Unresolved("execution-unit", unitError, "repair the queue item's execution unit id.", unit, unitSource);
        }

        var packetPath = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(repoRoot, unit), "packet.yaml");
        string? domain = null;
        if (File.Exists(packetPath))
        {
            try
            {
                var fields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetPath));
                if (fields.TryGetValue("implementation_issue_packet.domain", out var value))
                {
                    domain = value.Trim().Trim('"', '\'').Trim();
                }
            }
            catch (Exception exception) when (exception is IOException or FormatException)
            {
                return Unresolved(
                    "packet-domain",
                    $"packet '.intent-cli/issues/{unit}/packet.yaml' could not be read: {exception.Message}",
                    $"repair `.intent-cli/issues/{unit}/packet.yaml` so it declares `implementation_issue_packet.domain`.",
                    unit,
                    unitSource);
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            return Unresolved(
                "packet-domain",
                $"packet '.intent-cli/issues/{unit}/packet.yaml' is missing or declares no `implementation_issue_packet.domain`.",
                $"author the packet with `intent-cli packet draft --execution-unit {unit} --domain <domain> --target-repo {repo}` so it declares `implementation_issue_packet.domain`.",
                unit,
                unitSource);
        }

        var scope = $"execution-unit:{unit}";
        var claim = (ClaimReader ?? DefaultClaimReader)(repoRoot, scope);
        var team = string.Equals(claim.Status, ClaimOwnershipVerification.StatusTeamRequired, StringComparison.Ordinal)
            ? claim.HolderTeam
            : null;
        if (string.IsNullOrWhiteSpace(team))
        {
            return Unresolved(
                "claim-team",
                $"execution unit '{unit}' has no held claim with a team (claim status '{claim.Status}': {claim.Detail}).",
                $"acquire the execution-unit claim with a team: `intent-cli claim acquire --scope {scope} --actor <actor> --team <team> --reason <text> --write`.",
                unit,
                unitSource,
                domain);
        }

        return new CrossRuntimeReviewResolution
        {
            Resolved = true,
            ExecutionUnit = unit,
            ExecutionUnitSource = unitSource,
            Domain = domain,
            DomainSource = SourcePacketDomain,
            Team = team,
            TeamSource = SourceClaimTeam,
        };
    }

    private static ClaimOwnershipVerification DefaultClaimReader(string repoRoot, string scope) =>
        ClaimOwnershipVerifier.Verify(repoRoot, scope, invokingTeam: null);

    private static CrossRuntimeReviewResolution Unresolved(
        string missing,
        string detail,
        string fix,
        string? unit = null,
        string? unitSource = null,
        string? domain = null) =>
        new()
        {
            Resolved = false,
            Cause = CrossRuntimeReviewCauses.TeamUnresolved,
            Missing = missing,
            Detail = detail,
            Fix = fix,
            ExecutionUnit = unit,
            ExecutionUnitSource = unitSource,
            Domain = domain,
            DomainSource = domain is null ? null : SourcePacketDomain,
        };

    private static CrossRuntimeReviewResolution Mismatch(string detail) =>
        new()
        {
            Resolved = false,
            Cause = CrossRuntimeReviewCauses.UnitMismatch,
            Detail = detail,
            Fix = "pass the execution unit the host queue links to this PR, or repair the queue linkage.",
        };
}

internal sealed record CrossRuntimeReviewResolution
{
    [JsonPropertyName("resolved")]
    public required bool Resolved { get; init; }

    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }

    [JsonPropertyName("missing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Missing { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    [JsonPropertyName("fix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fix { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("execution_unit_source")]
    public string? ExecutionUnitSource { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("domain_source")]
    public string? DomainSource { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("team_source")]
    public string? TeamSource { get; init; }
}
