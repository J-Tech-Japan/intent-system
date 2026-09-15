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
        var resolution = CrossRuntimeReviewPacketClaimResolver.Resolve(repoRoot, executionUnit);
        if (!resolution.Resolved)
        {
            return new PublishResolution
            {
                Resolved = false,
                Cause = resolution.Cause,
                Missing = resolution.Missing,
                Detail = resolution.Detail,
                Fix = resolution.Fix,
                ExecutionUnit = resolution.ExecutionUnit,
                Domain = resolution.Domain,
            };
        }

        if (config.TryGetDeclared(resolution.Domain!, resolution.Team!, out var declaration))
        {
            return new PublishResolution
            {
                Resolved = true,
                ExecutionUnit = resolution.ExecutionUnit,
                Domain = resolution.Domain,
                Team = resolution.Team,
                TargetRepo = resolution.TargetRepo,
                Declared = true,
                Declaration = declaration,
            };
        }

        if (config.TryFindDeclaredForTeamAndRepo(resolution.Team!, repo, out _, out var alternateDomain))
        {
            return new PublishResolution
            {
                Resolved = true,
                ExecutionUnit = resolution.ExecutionUnit,
                Domain = resolution.Domain,
                Team = resolution.Team,
                TargetRepo = resolution.TargetRepo,
                Declared = false,
                DomainMismatch = true,
                AlternateDeclaredDomain = alternateDomain,
            };
        }

        return new PublishResolution
        {
            Resolved = true,
            ExecutionUnit = resolution.ExecutionUnit,
            Domain = resolution.Domain,
            Team = resolution.Team,
            TargetRepo = resolution.TargetRepo,
            Declared = false,
        };
    }
}
