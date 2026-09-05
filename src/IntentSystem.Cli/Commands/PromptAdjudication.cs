using System.Security.Cryptography;
using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G690's hard risk floor. These tags are deliberately a closed vocabulary:
/// a future answerable class may opt into a narrower authority model, but it
/// may not make a design answer for one of these categories.
/// </summary>
internal static class PromptRiskFloor
{
    public const string Destructive = "destructive";
    public const string Credential = "credential";
    public const string PermissionChange = "permission-change";
    public const string Security = "security";
    public const string ProductDecision = "product-decision";
    public const string Unverifiable = "unverifiable";

    public static IReadOnlySet<string> Tags { get; } = new HashSet<string>(
        [Destructive, Credential, PermissionChange, Security, ProductDecision, Unverifiable],
        StringComparer.Ordinal);

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? tags) => (tags ?? [])
        .Where(tag => !string.IsNullOrWhiteSpace(tag))
        .Select(tag => tag.Trim().ToLowerInvariant())
        .Distinct(StringComparer.Ordinal)
        .OrderBy(tag => tag, StringComparer.Ordinal)
        .ToArray();
}

internal sealed record PromptCapabilityResolution
{
    public required bool Allowed { get; init; }
    public required string RequestedRole { get; init; }
    public required string AnswerableBy { get; init; }
    public required IReadOnlyList<string> RiskTags { get; init; }
    public required IReadOnlyList<string> HardFloorTags { get; init; }
    public required string Summary { get; init; }
}

/// <summary>
/// Resolves the declared authority model instead of making the supervisor's
/// owner role synonymous with the answer actor. Empty declarations retain the
/// G689/G683 default of orchestration.
/// </summary>
internal static class PromptCapabilityResolver
{
    public const string DesignRole = "design";
    public const string OrchestrationRole = "orchestration";

    public static PromptCapabilityResolution Resolve(
        AgentPromptClassRecipe? recipe,
        IEnumerable<ShellCommandScopeDefinition>? scopes,
        string? requestedRole)
    {
        var role = string.IsNullOrWhiteSpace(requestedRole)
            ? OrchestrationRole
            : requestedRole.Trim().ToLowerInvariant();
        var recipeRoles = ParseRoles(recipe?.AnswerableBy);
        var scopeList = (scopes ?? []).ToArray();
        var scopeRoles = scopeList
            .SelectMany(scope => ParseRoles(scope.AnswerableBy))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var roles = scopeList.Length == 0
            ? recipeRoles
            : recipeRoles.Intersect(scopeRoles, StringComparer.Ordinal).ToArray();
        var riskTags = PromptRiskFloor.Normalize(
            (recipe?.RiskTags ?? []).Concat(scopeList.SelectMany(scope => scope.RiskTags)));
        var hardFloorTags = riskTags
            .Where(PromptRiskFloor.Tags.Contains)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
        var answerableBy = string.Join(",", roles);
        if (role == DesignRole && hardFloorTags.Length > 0)
        {
            return new PromptCapabilityResolution
            {
                Allowed = false,
                RequestedRole = role,
                AnswerableBy = answerableBy,
                RiskTags = riskTags,
                HardFloorTags = hardFloorTags,
                Summary = $"Design adjudication is refused by the hard risk floor: [{string.Join(", ", hardFloorTags)}].",
            };
        }

        var allowed = roles.Contains(role, StringComparer.Ordinal);
        return new PromptCapabilityResolution
        {
            Allowed = allowed,
            RequestedRole = role,
            AnswerableBy = answerableBy,
            RiskTags = riskTags,
            HardFloorTags = hardFloorTags,
            Summary = allowed
                ? $"Prompt authority declares answerable_by=[{answerableBy}] for actor '{role}'."
                : $"Actor '{role}' is not declared in answerable_by=[{answerableBy}].",
        };
    }

    public static string SelectDefaultActor(
        AgentPromptClassRecipe? recipe,
        IEnumerable<ShellCommandScopeDefinition>? scopes) =>
        Resolve(recipe, scopes, OrchestrationRole).Allowed
            ? OrchestrationRole
            : ParseRoles(recipe?.AnswerableBy).FirstOrDefault()
                ?? (scopes ?? []).SelectMany(scope => ParseRoles(scope.AnswerableBy)).FirstOrDefault()
                ?? OrchestrationRole;

    private static IReadOnlyList<string> ParseRoles(string? declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
        {
            return [OrchestrationRole];
        }

        return declaration
            .Split([',', '|', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(role => role.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed record PromptAdjudicationAuthorization
{
    public required string Decision { get; init; }
    public required string Rule { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> MatchedScopes { get; init; }
    public required IReadOnlyList<string> AnswerKeys { get; init; }
    public string? ExactAnswerScope { get; init; }
    public string? CommandDigest { get; init; }
    public string? DialogHash { get; init; }
    public required string AnswerableBy { get; init; }
    public required IReadOnlyList<string> RiskTags { get; init; }
    public required string DecisionActorRole { get; init; }
    public string? MechanicalExecutor { get; init; }
    public required string ScopeOrRuleId { get; init; }
}

/// <summary>
/// The one adjudication pipeline shared by supervision and the explicit
/// design-facing command. It combines exact class recognition, the stored
/// policy, capability resolution, and the risk floor before an answer exists.
/// </summary>
internal static class PromptAdjudicationPipeline
{
    public const string MechanicalExecutor = "herdr:agent send-keys";

    public static PromptAdjudicationAuthorization Evaluate(
        AgentPromptClassObservation classified,
        NotifyPreApprovalPolicy? policy,
        string? actorRole,
        string? cwd,
        IReadOnlyList<NotifyPromptAudit>? promptAudits = null,
        string? currentCycleId = null)
    {
        var shellAuthorization = classified.ShellCommand is { } shellPayload
            ? ShellCommandPolicyRegistry.Evaluate(
                shellPayload,
                policy?.ScopedPolicies ?? [],
                cwd,
                promptAudits,
                currentCycleId)
            : null;
        var matchedScopes = shellAuthorization?.MatchedScopes ?? [];
        var scopes = matchedScopes
            .Select(ShellCommandPolicyRegistry.FindScope)
            .Where(scope => scope is not null)
            .Cast<ShellCommandScopeDefinition>()
            .ToArray();
        var effectiveActor = actorRole is null
            ? PromptCapabilityResolver.SelectDefaultActor(classified.Recipe, scopes)
            : actorRole;
        var capability = PromptCapabilityResolver.Resolve(classified.Recipe, scopes, effectiveActor);

        if (shellAuthorization is not null)
        {
            var shellRule = shellAuthorization.Rule;
            var shellDecision = shellAuthorization.Decision;
            var shellSummary = shellAuthorization.Summary;
            if (shellDecision == "accept" && !capability.Allowed)
            {
                shellDecision = "escalate";
                shellRule += " (capability-denied)";
                shellSummary = $"The shell scope matched, but the answer was refused: {capability.Summary}";
            }

            return new PromptAdjudicationAuthorization
            {
                Decision = shellDecision,
                Rule = shellRule,
                Summary = shellSummary,
                MatchedScopes = shellAuthorization.MatchedScopes,
                AnswerKeys = shellDecision == "accept" ? shellAuthorization.AnswerKeys : [],
                ExactAnswerScope = shellDecision == "accept" ? shellAuthorization.ExactAnswerScope : null,
                CommandDigest = shellAuthorization.CommandDigest,
                DialogHash = shellAuthorization.DialogHash,
                AnswerableBy = capability.AnswerableBy,
                RiskTags = capability.RiskTags,
                DecisionActorRole = capability.RequestedRole,
                MechanicalExecutor = shellDecision == "accept" ? MechanicalExecutor : null,
                ScopeOrRuleId = shellRule,
            };
        }

        var escalateRule = policy?.Escalate.FirstOrDefault(rule =>
            rule.Applicable && RuleMatches(rule, classified.AgentKind, classified.PromptClass));
        var acceptRule = escalateRule is null && classified.Known && policy?.Applicable == true
            ? policy.Accept.FirstOrDefault(rule => RuleMatches(rule, classified.AgentKind, classified.PromptClass))
            : null;
        var decision = acceptRule is not null && capability.Allowed ? "accept" : "escalate";
        var rule = escalateRule?.ToString()
            ?? acceptRule?.ToString()
            ?? (policy?.Applicable == false ? "policy-inapplicable" : "unmatched");
        var summary = escalateRule is not null
            ? "A matching pre-approval rule explicitly escalates this prompt."
            : acceptRule is null
                ? classified.Known
                    ? "The exact literal class is known, but no applicable accept rule exists."
                    : "The observed text matched no recorded literal class; classification is unknown and escalate-only."
                : capability.Allowed
                    ? "The exact literal class and recorded accept rule match the declared authority capability."
                    : $"The accept rule matched, but the answer was refused: {capability.Summary}";
        if (acceptRule is not null && !capability.Allowed)
        {
            rule += " (capability-denied)";
        }

        return new PromptAdjudicationAuthorization
        {
            Decision = decision,
            Rule = rule,
            Summary = summary,
            MatchedScopes = [],
            AnswerKeys = decision == "accept" ? classified.Recipe?.AnswerKeys ?? [] : [],
            ExactAnswerScope = decision == "accept" ? classified.Recipe?.ExactAnswerScope : null,
            CommandDigest = null,
            DialogHash = null,
            AnswerableBy = capability.AnswerableBy,
            RiskTags = capability.RiskTags,
            DecisionActorRole = capability.RequestedRole,
            MechanicalExecutor = decision == "accept" ? MechanicalExecutor : null,
            ScopeOrRuleId = rule,
        };
    }

    private static bool RuleMatches(NotifyPreApprovalRule rule, string agentKind, string promptClass) =>
        string.Equals(rule.AgentKind, agentKind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(rule.PromptClass, promptClass, StringComparison.OrdinalIgnoreCase);
}

internal sealed record PromptDialogCasResult
{
    public required bool Matches { get; init; }
    public required string Summary { get; init; }
    public string? Cause { get; init; }
}

internal static class PromptDialogCas
{
    public const string TextHashMismatch = "text-hash-mismatch";
    public const string DialogChangedHashMismatch = "dialog-changed-hash-mismatch";
    public const string WrongProjectionHashMismatch = "wrong-projection-hash-mismatch";

    public static PromptDialogCasResult Verify(
        string expectedPane,
        string actualPane,
        long? expectedStateChangeSequence,
        long? actualStateChangeSequence,
        string expectedObservedTextHash,
        string actualObservedTextHash,
        bool expectedHashIsCanonical = false)
    {
        if (!string.Equals(expectedPane, actualPane, StringComparison.Ordinal))
        {
            return Refused("pane-changed", "The live dialog pane changed before execution.");
        }

        if (expectedStateChangeSequence is null || actualStateChangeSequence is null)
        {
            return Refused(
                "state-sequence-unavailable",
                "The live dialog has no comparable state-change sequence; derive --state-sequence from herdr agent list's state_change_seq for the pane. CAS refuses an unaudited answer.");
        }

        if (expectedStateChangeSequence != actualStateChangeSequence)
        {
            return Refused(
                "dialog-changed-sequence",
                $"The live dialog state-change sequence changed from {expectedStateChangeSequence} to {actualStateChangeSequence}; derive --state-sequence from herdr agent list's state_change_seq for the pane and refresh both CAS inputs.");
        }

        if (!string.Equals(expectedObservedTextHash, actualObservedTextHash, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(
                expectedHashIsCanonical ? DialogChangedHashMismatch : TextHashMismatch,
                expectedHashIsCanonical
                    ? "The live dialog changed before execution; refresh --state-sequence from herdr agent list's state_change_seq and --text-hash from the current trimmed detection read."
                    : "The supplied --text-hash does not match the canonical detection projection; the caller must hash the trimmed UTF-8 output of herdr agent read for the recorded pane.");
        }

        return new PromptDialogCasResult
        {
            Matches = true,
            Summary = "The live pane, state-change sequence, and observed-text hash match the adjudicated dialog.",
        };
    }

    public static PromptDialogCasResult ClassifyTextHashMismatch(
        string pane,
        long? initialStateChangeSequence,
        long? rereadStateChangeSequence,
        string initialCanonicalTextHash,
        string rereadCanonicalTextHash)
    {
        if (initialStateChangeSequence == rereadStateChangeSequence
            && string.Equals(initialCanonicalTextHash, rereadCanonicalTextHash, StringComparison.OrdinalIgnoreCase))
        {
            return Refused(
                WrongProjectionHashMismatch,
                $"The live dialog is unchanged, but the supplied --text-hash does not match the canonical detection projection for pane '{pane}'. Hash the trimmed UTF-8 output of herdr agent read {pane} --source detection --lines 200.");
        }

        return Refused(
            DialogChangedHashMismatch,
            "The live dialog changed before execution; refresh --state-sequence from herdr agent list's state_change_seq and --text-hash from the current trimmed detection read.");
    }

    public static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

    private static PromptDialogCasResult Refused(string cause, string summary) => new()
    {
        Matches = false,
        Cause = cause,
        Summary = summary,
    };
}
