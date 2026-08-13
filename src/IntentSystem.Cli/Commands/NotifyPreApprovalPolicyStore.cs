using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyPreApprovalRule
{
    [JsonPropertyName("agent_kind")] public required string AgentKind { get; init; }
    [JsonPropertyName("prompt_class")] public required string PromptClass { get; init; }
    [JsonPropertyName("applicable")] public bool Applicable { get; init; }
    [JsonPropertyName("applicability_status")] public string ApplicabilityStatus { get; init; } =
        NotifyPromptClassProducerRegistry.InapplicableStatus;
    [JsonPropertyName("inapplicability_reason")] public string? InapplicabilityReason { get; init; }

    public override string ToString() => $"{AgentKind}:{PromptClass}";
}

internal sealed record NotifyPreApprovalPolicy
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("accept")] public required IReadOnlyList<NotifyPreApprovalRule> Accept { get; init; }
    [JsonPropertyName("escalate")] public required IReadOnlyList<NotifyPreApprovalRule> Escalate { get; init; }
    [JsonPropertyName("scoped_policies")] public IReadOnlyList<NotifyScopedPromptPolicy> ScopedPolicies { get; init; } = [];
    [JsonPropertyName("applicable")] public bool Applicable { get; init; }
    [JsonPropertyName("applicability_status")] public string ApplicabilityStatus { get; init; } =
        NotifyPromptClassProducerRegistry.InapplicableStatus;
    [JsonPropertyName("inapplicable_agent_kinds")] public IReadOnlyList<string> InapplicableAgentKinds { get; init; } = [];
    [JsonPropertyName("inapplicability_reason")] public string? InapplicabilityReason { get; init; }
}

internal sealed record NotifyPreApprovalPolicyStatus
{
    [JsonPropertyName("recorded")] public required bool Recorded { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("default_decision")] public required string DefaultDecision { get; init; }
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("accept")] public required IReadOnlyList<NotifyPreApprovalRule> Accept { get; init; }
    [JsonPropertyName("escalate")] public required IReadOnlyList<NotifyPreApprovalRule> Escalate { get; init; }
    [JsonPropertyName("scoped_policies")] public required IReadOnlyList<NotifyScopedPromptPolicy> ScopedPolicies { get; init; }
    [JsonPropertyName("applicable")] public required bool Applicable { get; init; }
    [JsonPropertyName("applicability_status")] public required string ApplicabilityStatus { get; init; }
    [JsonPropertyName("inapplicable_agent_kinds")] public required IReadOnlyList<string> InapplicableAgentKinds { get; init; }
    [JsonPropertyName("inapplicability_reason")] public string? InapplicabilityReason { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyPreApprovalPolicyReadResult
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public NotifyPreApprovalPolicy? Policy { get; init; }
    public bool RefreshRequired { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// G682's applicability projection now delegates to G683's recipe-backed
/// producer registry. Tests may still force availability to exercise legacy
/// no-producer policy records.
/// </summary>
internal static class NotifyPromptClassProducerRegistry
{
    public const string ApplicableStatus = "applicable";
    public const string InapplicableStatus = "inapplicable-no-prompt-class-producer";
    public const string ScopedPolicyRequiredStatus = "inapplicable-scoped-policy-required";

    internal static Func<string, bool>? AvailabilityOverride { get; set; }

    public static bool HasProducer(string agentKind) =>
        AvailabilityOverride?.Invoke(agentKind) ?? AgentLaunchRecipeRegistry.HasPromptClassProducer(agentKind);

    public static string MissingReason(string agentKind) =>
        $"No prompt-class producer is registered for agent kind '{agentKind}'. This rule cannot currently apply; "
        + "residual prompts are escalate-only by construction until a recipe-backed producer is registered.";
}

/// <summary>
/// G666 durable per-team approval policy. This store records orchestration's
/// bounded adjudication authority; it never answers a prompt or sends input.
/// </summary>
internal static class NotifyPreApprovalPolicyStore
{
    public const string FileName = "pre-approval-policy.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal static Func<string, string, NotifySupervisionWriteResult>? WriteOverride { get; set; }

    public static string ResolvePath(string artifactRoot, string domain, string team) =>
        Path.Combine(NotifySupervisionStore.ResolveDirectory(artifactRoot, domain, team), FileName);

    public static NotifyPreApprovalPolicyReadResult Read(string artifactRoot, string domain, string team)
    {
        var path = ResolvePath(artifactRoot, domain, team);
        if (!File.Exists(path))
        {
            return new NotifyPreApprovalPolicyReadResult { Resolved = true, Path = path };
        }

        try
        {
            var policy = JsonSerializer.Deserialize<NotifyPreApprovalPolicy>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("The pre-approval policy was empty.");
            if (!string.Equals(policy.Domain, domain, StringComparison.Ordinal)
                || !string.Equals(policy.Team, team, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The policy identifies '{policy.Domain}/{policy.Team}', not requested '{domain}/{team}'.");
            }
            var current = WithCurrentApplicability(policy);
            return new NotifyPreApprovalPolicyReadResult
            {
                Resolved = true,
                Path = path,
                Policy = current,
                RefreshRequired = !ApplicabilityEquals(policy, current),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return new NotifyPreApprovalPolicyReadResult
            {
                Resolved = false,
                Path = path,
                Error = $"Pre-approval policy at '{path}' could not be read: {exception.Message}",
            };
        }
    }

    public static NotifySupervisionWriteResult Record(
        string artifactRoot,
        NotifyPreApprovalPolicy policy,
        bool write)
    {
        var path = ResolvePath(artifactRoot, policy.Domain, policy.Team);
        if (!TryValidateNoOverlap(policy.Accept, policy.Escalate, out var overlapError))
        {
            return new NotifySupervisionWriteResult(false, false, path, overlapError);
        }
        if (!TryValidateScopedPolicies(policy.ScopedPolicies, out var scopedPolicyError))
        {
            return new NotifySupervisionWriteResult(false, false, path, scopedPolicyError);
        }
        var current = WithCurrentApplicability(policy);
        var content = JsonSerializer.Serialize(current, JsonOptions) + Environment.NewLine;
        if (!write)
        {
            return new NotifySupervisionWriteResult(false, false, path, null);
        }

        if (WriteOverride is { } writeOverride)
        {
            return writeOverride(path, content);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new NotifySupervisionWriteResult(true, false, path, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new NotifySupervisionWriteResult(false, false, path, exception.Message);
        }
    }

    public static string Adjudicate(NotifyPreApprovalPolicy? policy, string agentKind, string promptClass)
    {
        if (policy?.Applicable != true)
        {
            return "escalate";
        }
        if (policy.Escalate.Any(rule => rule.Applicable && Matches(rule, agentKind, promptClass)))
        {
            return "escalate";
        }
        if (policy.Accept.Any(rule => rule.Applicable && Matches(rule, agentKind, promptClass)))
        {
            return "accept";
        }
        return "escalate";
    }

    public static bool TryParseRule(string value, out NotifyPreApprovalRule? rule)
    {
        rule = null;
        var parts = value.Split(':', StringSplitOptions.None);
        if (parts.Length != 2 || !Safe(parts[0]) || !Safe(parts[1]))
        {
            return false;
        }
        rule = new NotifyPreApprovalRule { AgentKind = parts[0], PromptClass = parts[1] };
        return true;
    }

    public static bool TryValidateRule(NotifyPreApprovalRule rule, out string error)
    {
        if (AgentLaunchRecipeRegistry.TryFindPromptClass(rule.AgentKind, rule.PromptClass, out var prompt))
        {
            if (prompt?.ScopedPolicyRequired == true)
            {
                error = $"Bare prompt policy pair '{rule}' is structurally refused; record a scoped shell policy instance instead. Known scopes: {string.Join(", ", ShellCommandPolicyRegistry.Scopes.Select(scope => scope.Name))}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var known = AgentLaunchRecipeRegistry.KnownPairs;
        error = $"Unknown prompt policy pair '{rule}'. Known values: "
            + (known.Count == 0 ? "none" : string.Join(", ", known)) + ".";
        return false;
    }

    public static bool TryValidateScopedPolicy(
        NotifyScopedPromptPolicy policy,
        out string error,
        bool requireScratchLedgerCycleId = true)
    {
        if (!AgentLaunchRecipeRegistry.TryFindPromptClass(policy.AgentKind, policy.PromptClass, out var prompt)
            || prompt?.ScopedPolicyRequired != true)
        {
            error = $"Scoped policy '{policy}' does not target a registered scoped-policy prompt class.";
            return false;
        }

        return ShellCommandPolicyRegistry.TryValidate(
            policy,
            out error,
            requireScratchLedgerCycleId);
    }

    public static bool TryValidateScopedPolicies(
        IReadOnlyList<NotifyScopedPromptPolicy> policies,
        out string error,
        bool requireScratchLedgerCycleId = true)
    {
        foreach (var policy in policies)
        {
            if (!TryValidateScopedPolicy(policy, out error, requireScratchLedgerCycleId))
            {
                return false;
            }
        }

        var duplicate = policies
            .GroupBy(policy => $"{policy.AgentKind}:{policy.PromptClass}:{policy.Scope}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            error = $"A scoped shell policy may define only one decision for '{duplicate.Key}'; overlapping scoped instances are refused.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateNoOverlap(
        IReadOnlyList<NotifyPreApprovalRule> accept,
        IReadOnlyList<NotifyPreApprovalRule> escalate,
        out string error)
    {
        var overlaps = accept
            .Select(rule => rule.ToString())
            .Intersect(escalate.Select(rule => rule.ToString()), StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (overlaps.Length == 0)
        {
            error = string.Empty;
            return true;
        }

        error = "A prompt policy pair cannot be recorded in both --pre-approve and --pre-escalate. "
            + $"Conflicting values: {string.Join(", ", overlaps)}. Remove the overlap; escalation remains the fail-closed runtime precedence.";
        return false;
    }

    public static NotifyPreApprovalPolicy WithCurrentApplicability(NotifyPreApprovalPolicy policy)
    {
        var accept = policy.Accept.Select(WithCurrentApplicability).ToArray();
        var escalate = policy.Escalate.Select(WithCurrentApplicability).ToArray();
        var scopedPolicies = policy.ScopedPolicies.Select(WithCurrentApplicability).ToArray();
        var rules = accept.Concat(escalate).ToArray();
        var unavailableKinds = rules
            .Where(rule => !rule.Applicable)
            .Select(rule => rule.AgentKind)
            .Concat(scopedPolicies.Where(scoped => !scoped.Applicable).Select(scoped => scoped.AgentKind))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasDeclarations = rules.Length > 0 || scopedPolicies.Length > 0;
        var applicable = hasDeclarations && unavailableKinds.Length == 0;
        return policy with
        {
            Accept = accept,
            Escalate = escalate,
            ScopedPolicies = scopedPolicies,
            Applicable = applicable,
            ApplicabilityStatus = applicable
                ? NotifyPromptClassProducerRegistry.ApplicableStatus
                : NotifyPromptClassProducerRegistry.InapplicableStatus,
            InapplicableAgentKinds = unavailableKinds,
            InapplicabilityReason = applicable
                ? null
                : "One or more recorded rules or scoped policies are unavailable or invalid. "
                    + "Those entries cannot currently apply; residual prompts are escalate-only by construction.",
        };
    }

    private static NotifyPreApprovalRule WithCurrentApplicability(NotifyPreApprovalRule rule)
    {
        var hasProducer = NotifyPromptClassProducerRegistry.HasProducer(rule.AgentKind);
        var scopedPolicyRequired = AgentLaunchRecipeRegistry.TryFindPromptClass(
            rule.AgentKind,
            rule.PromptClass,
            out var prompt)
            && prompt?.ScopedPolicyRequired == true;
        var applicable = hasProducer && !scopedPolicyRequired;
        return rule with
        {
            Applicable = applicable,
            ApplicabilityStatus = !hasProducer
                ? NotifyPromptClassProducerRegistry.InapplicableStatus
                : scopedPolicyRequired
                    ? NotifyPromptClassProducerRegistry.ScopedPolicyRequiredStatus
                    : NotifyPromptClassProducerRegistry.ApplicableStatus,
            InapplicabilityReason = !hasProducer
                ? NotifyPromptClassProducerRegistry.MissingReason(rule.AgentKind)
                : scopedPolicyRequired
                    ? $"Prompt class '{rule}' requires a scoped policy instance; a bare legacy rule cannot apply."
                    : null,
        };
    }

    private static NotifyScopedPromptPolicy WithCurrentApplicability(NotifyScopedPromptPolicy policy)
    {
        var hasProducer = NotifyPromptClassProducerRegistry.HasProducer(policy.AgentKind);
        var validationError = string.Empty;
        var valid = hasProducer && TryValidateScopedPolicy(policy, out validationError);
        return policy with
        {
            Applicable = valid,
            ApplicabilityStatus = !hasProducer
                ? NotifyPromptClassProducerRegistry.InapplicableStatus
                : valid
                    ? NotifyPromptClassProducerRegistry.ApplicableStatus
                    : "inapplicable-invalid-scoped-policy",
            InapplicabilityReason = !hasProducer
                ? NotifyPromptClassProducerRegistry.MissingReason(policy.AgentKind)
                : valid ? null : validationError,
        };
    }

    private static bool ApplicabilityEquals(NotifyPreApprovalPolicy left, NotifyPreApprovalPolicy right) =>
        left.Applicable == right.Applicable
        && string.Equals(left.ApplicabilityStatus, right.ApplicabilityStatus, StringComparison.Ordinal)
        && left.InapplicableAgentKinds.SequenceEqual(right.InapplicableAgentKinds, StringComparer.OrdinalIgnoreCase)
        && string.Equals(left.InapplicabilityReason, right.InapplicabilityReason, StringComparison.Ordinal)
        && RulesApplicabilityEquals(left.Accept, right.Accept)
        && RulesApplicabilityEquals(left.Escalate, right.Escalate)
        && ScopedApplicabilityEquals(left.ScopedPolicies, right.ScopedPolicies);

    private static bool RulesApplicabilityEquals(
        IReadOnlyList<NotifyPreApprovalRule> left,
        IReadOnlyList<NotifyPreApprovalRule> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.Applicable == pair.Second.Applicable
            && string.Equals(pair.First.ApplicabilityStatus, pair.Second.ApplicabilityStatus, StringComparison.Ordinal)
            && string.Equals(pair.First.InapplicabilityReason, pair.Second.InapplicabilityReason, StringComparison.Ordinal));

    private static bool ScopedApplicabilityEquals(
        IReadOnlyList<NotifyScopedPromptPolicy> left,
        IReadOnlyList<NotifyScopedPromptPolicy> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair =>
            pair.First.Applicable == pair.Second.Applicable
            && string.Equals(pair.First.ApplicabilityStatus, pair.Second.ApplicabilityStatus, StringComparison.Ordinal)
            && string.Equals(pair.First.InapplicabilityReason, pair.Second.InapplicabilityReason, StringComparison.Ordinal));

    private static bool Matches(NotifyPreApprovalRule rule, string agentKind, string promptClass) =>
        string.Equals(rule.AgentKind, agentKind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(rule.PromptClass, promptClass, StringComparison.OrdinalIgnoreCase);

    private static bool Safe(string value) => !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
