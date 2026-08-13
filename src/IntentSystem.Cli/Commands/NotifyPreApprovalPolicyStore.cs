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
        if (AgentLaunchRecipeRegistry.TryFindPromptClass(rule.AgentKind, rule.PromptClass, out _))
        {
            error = string.Empty;
            return true;
        }

        var known = AgentLaunchRecipeRegistry.KnownPairs;
        error = $"Unknown prompt policy pair '{rule}'. Known values: "
            + (known.Count == 0 ? "none" : string.Join(", ", known)) + ".";
        return false;
    }

    public static NotifyPreApprovalPolicy WithCurrentApplicability(NotifyPreApprovalPolicy policy)
    {
        var accept = policy.Accept.Select(WithCurrentApplicability).ToArray();
        var escalate = policy.Escalate.Select(WithCurrentApplicability).ToArray();
        var rules = accept.Concat(escalate).ToArray();
        var unavailableKinds = rules
            .Where(rule => !rule.Applicable)
            .Select(rule => rule.AgentKind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var applicable = unavailableKinds.Length == 0;
        return policy with
        {
            Accept = accept,
            Escalate = escalate,
            Applicable = applicable,
            ApplicabilityStatus = applicable
                ? NotifyPromptClassProducerRegistry.ApplicableStatus
                : NotifyPromptClassProducerRegistry.InapplicableStatus,
            InapplicableAgentKinds = unavailableKinds,
            InapplicabilityReason = applicable
                ? null
                : "One or more recorded rules name an agent kind with no prompt-class producer. "
                    + "Those rules cannot currently apply; residual prompts are escalate-only by construction.",
        };
    }

    private static NotifyPreApprovalRule WithCurrentApplicability(NotifyPreApprovalRule rule)
    {
        var applicable = NotifyPromptClassProducerRegistry.HasProducer(rule.AgentKind);
        return rule with
        {
            Applicable = applicable,
            ApplicabilityStatus = applicable
                ? NotifyPromptClassProducerRegistry.ApplicableStatus
                : NotifyPromptClassProducerRegistry.InapplicableStatus,
            InapplicabilityReason = applicable
                ? null
                : NotifyPromptClassProducerRegistry.MissingReason(rule.AgentKind),
        };
    }

    private static bool ApplicabilityEquals(NotifyPreApprovalPolicy left, NotifyPreApprovalPolicy right) =>
        left.Applicable == right.Applicable
        && string.Equals(left.ApplicabilityStatus, right.ApplicabilityStatus, StringComparison.Ordinal)
        && left.InapplicableAgentKinds.SequenceEqual(right.InapplicableAgentKinds, StringComparer.OrdinalIgnoreCase)
        && string.Equals(left.InapplicabilityReason, right.InapplicabilityReason, StringComparison.Ordinal)
        && RulesApplicabilityEquals(left.Accept, right.Accept)
        && RulesApplicabilityEquals(left.Escalate, right.Escalate);

    private static bool RulesApplicabilityEquals(
        IReadOnlyList<NotifyPreApprovalRule> left,
        IReadOnlyList<NotifyPreApprovalRule> right) =>
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
