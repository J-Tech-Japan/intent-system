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
        var resolution = CrossRuntimeReviewPacketClaimResolver.Resolve(repoRoot, executionUnit);
        return new DesignResolution
        {
            Resolved = resolution.Resolved,
            Cause = resolution.Cause,
            Missing = resolution.Missing,
            Detail = resolution.Detail,
            Fix = resolution.Fix,
            ExecutionUnit = resolution.ExecutionUnit,
            Domain = resolution.Domain,
            Team = resolution.Team,
            TargetRepo = resolution.TargetRepo,
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
}
