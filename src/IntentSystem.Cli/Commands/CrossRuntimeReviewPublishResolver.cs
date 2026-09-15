using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G835: publish-flow team resolution from the packet and held claim only.
/// </summary>
internal static class CrossRuntimeReviewPublishResolver
{
    public sealed record PublishResolution
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
        public bool Declared { get; init; }
        public CrossRuntimeReviewTeamDeclaration? Declaration { get; init; }
        public bool DomainMismatch { get; init; }
        public string? AlternateDeclaredDomain { get; init; }
    }

    public static PublishResolution Resolve(string repoRoot, string executionUnit, string repo, CrossRuntimeReviewConfig config)
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

        if (config.TryGetDeclared(domain, team, out var declaration))
        {
            return new PublishResolution
            {
                Resolved = true,
                ExecutionUnit = executionUnit,
                Domain = domain,
                Team = team,
                TargetRepo = targetRepo,
                Declared = true,
                Declaration = declaration,
            };
        }

        if (config.TryFindDeclaredForTeamAndRepo(team, repo, out _, out var alternateDomain))
        {
            return new PublishResolution
            {
                Resolved = true,
                ExecutionUnit = executionUnit,
                Domain = domain,
                Team = team,
                TargetRepo = targetRepo,
                Declared = false,
                DomainMismatch = true,
                AlternateDeclaredDomain = alternateDomain,
            };
        }

        return new PublishResolution
        {
            Resolved = true,
            ExecutionUnit = executionUnit,
            Domain = domain,
            Team = team,
            TargetRepo = targetRepo,
            Declared = false,
        };
    }

    private static ClaimOwnershipVerification DefaultClaimReader(string repoRoot, string scope) =>
        ClaimOwnershipVerifier.Verify(repoRoot, scope, invokingTeam: null);

    private static PublishResolution Unresolved(string missing, string detail, string fix, string? unit = null, string? domain = null) =>
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
