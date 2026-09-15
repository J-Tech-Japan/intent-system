namespace IntentSystem.Cli.Commands;

/// <summary>
/// G835: design-review team resolution from the packet and held claim only
/// (no queue-state PR linkage).
/// </summary>
internal static class CrossRuntimeReviewDesignTeamResolver
{
    public sealed record DesignResolution
    {
        public required bool Resolved { get; init; }
        public string? Cause { get; init; }
        public string? Missing { get; init; }
        public string? Detail { get; init; }
        public string? Fix { get; init; }
        public string? ExecutionUnit { get; init; }
        public string? Domain { get; init; }
        public string? Team { get; init; }
        public string? TargetRepo { get; init; }
    }

    public static DesignResolution Resolve(string repoRoot, string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            return Unresolved("execution-unit", unitError, "pass the canonical execution unit id.");
        }

        var packetPath = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(repoRoot, executionUnit), "packet.yaml");
        string? domain = null;
        string? targetRepo = null;
        if (File.Exists(packetPath))
        {
            try
            {
                var fields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetPath));
                if (fields.TryGetValue("implementation_issue_packet.domain", out var domainValue))
                {
                    domain = domainValue.Trim().Trim('"', '\'').Trim();
                }

                if (fields.TryGetValue("implementation_issue_packet.target_repo", out var targetValue))
                {
                    targetRepo = targetValue.Trim().Trim('"', '\'').Trim();
                }
            }
            catch (Exception exception) when (exception is IOException or FormatException)
            {
                return Unresolved(
                    "packet-domain",
                    $"packet '.intent-cli/issues/{executionUnit}/packet.yaml' could not be read: {exception.Message}",
                    $"repair `.intent-cli/issues/{executionUnit}/packet.yaml`.",
                    executionUnit);
            }
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            return Unresolved(
                "packet-domain",
                $"packet '.intent-cli/issues/{executionUnit}/packet.yaml' is missing or declares no `implementation_issue_packet.domain`.",
                $"author the packet with `intent-cli packet draft --execution-unit {executionUnit} --domain <domain> --target-repo <owner/repo>`.",
                executionUnit);
        }

        var scope = $"execution-unit:{executionUnit}";
        var claim = (CrossRuntimeReviewTeamResolver.ClaimReader ?? DefaultClaimReader)(repoRoot, scope);
        var team = string.Equals(claim.Status, ClaimOwnershipVerification.StatusTeamRequired, StringComparison.Ordinal)
            ? claim.HolderTeam
            : null;
        if (string.IsNullOrWhiteSpace(team))
        {
            return Unresolved(
                "claim-team",
                $"execution unit '{executionUnit}' has no held claim with a team (claim status '{claim.Status}': {claim.Detail}).",
                $"acquire the execution-unit claim with a team: `intent-cli claim acquire --scope {scope} --actor <actor> --team <team> --reason <text> --write`.",
                executionUnit,
                domain);
        }

        return new DesignResolution
        {
            Resolved = true,
            ExecutionUnit = executionUnit,
            Domain = domain,
            Team = team,
            TargetRepo = targetRepo,
        };
    }

    public static CrossRuntimeReviewResolution ToCrossRuntimeResolution(DesignResolution resolution) =>
        new()
        {
            Resolved = resolution.Resolved,
            Cause = resolution.Cause,
            Missing = resolution.Missing,
            Detail = resolution.Detail,
            Fix = resolution.Fix,
            ExecutionUnit = resolution.ExecutionUnit,
            ExecutionUnitSource = CrossRuntimeReviewTeamResolver.SourceExecutionUnitArgument,
            Domain = resolution.Domain,
            DomainSource = CrossRuntimeReviewTeamResolver.SourcePacketDomain,
            Team = resolution.Team,
            TeamSource = CrossRuntimeReviewTeamResolver.SourceClaimTeam,
        };

    private static ClaimOwnershipVerification DefaultClaimReader(string repoRoot, string scope) =>
        ClaimOwnershipVerifier.Verify(repoRoot, scope, invokingTeam: null);

    private static DesignResolution Unresolved(string missing, string detail, string fix, string? unit = null, string? domain = null) =>
        new()
        {
            Resolved = false,
            Cause = CrossRuntimeReviewCauses.TeamUnresolved,
            Missing = missing,
            Detail = detail,
            Fix = fix,
            ExecutionUnit = unit,
            Domain = domain,
        };
}
