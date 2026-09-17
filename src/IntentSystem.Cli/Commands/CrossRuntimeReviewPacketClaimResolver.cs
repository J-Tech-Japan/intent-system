namespace IntentSystem.Cli.Commands;

/// <summary>
/// G835: shared packet and claim resolution for design review and publish-flow.
/// </summary>
internal static class CrossRuntimeReviewPacketClaimResolver
{
    public sealed record PacketClaimResolution
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

    public static PacketClaimResolution Resolve(string repoRoot, string executionUnit)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            return Unresolved("execution-unit", unitError, "pass the canonical execution unit id.");
        }

        var packetPath = Path.Combine(CrossRuntimeReviewPaths.PacketDirectory(repoRoot, executionUnit), "packet.yaml");
        var relativePacketPath = $".intent-cli/issues/{executionUnit}/packet.yaml";
        string? domain = null;
        string? targetRepo = null;
        if (File.Exists(packetPath))
        {
            string text;
            try
            {
                text = PacketFileReader.ReadAllText(packetPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return PacketUnreadable(
                    relativePacketPath,
                    exception.Message,
                    executionUnit);
            }

            if (!PacketYamlDocument.TryParseWithLocation(text, out var document, out var parseError) || document is null)
            {
                return PacketInvalid(
                    relativePacketPath,
                    parseError!,
                    executionUnit);
            }

            if (document.Fields.TryGetValue("implementation_issue_packet.domain", out var domainValue))
            {
                domain = domainValue.Trim().Trim('"', '\'').Trim();
            }

            if (document.Fields.TryGetValue("implementation_issue_packet.target_repo", out var targetValue))
            {
                targetRepo = targetValue.Trim().Trim('"', '\'').Trim();
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

        return new PacketClaimResolution
        {
            Resolved = true,
            ExecutionUnit = executionUnit,
            Domain = domain,
            Team = team,
            TargetRepo = targetRepo,
        };
    }

    private static ClaimOwnershipVerification DefaultClaimReader(string repoRoot, string scope) =>
        ClaimOwnershipVerifier.Verify(repoRoot, scope, invokingTeam: null);

    private static PacketClaimResolution PacketInvalid(string relativePacketPath, PacketYamlParseError error, string unit) =>
        new()
        {
            Resolved = false,
            Cause = CrossRuntimeReviewCauses.PacketInvalid,
            Missing = "packet-invalid",
            Detail = PacketYamlParseMessages.ComposeCrossRuntimeParseDetail(relativePacketPath, error),
            Fix = $"repair `{relativePacketPath}` so the whole document parses as YAML.",
            ExecutionUnit = unit,
        };

    private static PacketClaimResolution PacketUnreadable(string relativePacketPath, string exceptionMessage, string unit) =>
        new()
        {
            Resolved = false,
            Cause = CrossRuntimeReviewCauses.PacketUnreadable,
            Missing = "packet-unreadable",
            Detail = $"packet '{relativePacketPath}' could not be read: {exceptionMessage}",
            Fix = $"make `{relativePacketPath}` readable, then re-run.",
            ExecutionUnit = unit,
        };

    private static PacketClaimResolution Unresolved(string missing, string detail, string fix, string? unit = null, string? domain = null) =>
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
