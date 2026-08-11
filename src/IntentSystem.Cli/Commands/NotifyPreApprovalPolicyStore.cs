using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

internal sealed record NotifyPreApprovalRule
{
    [JsonPropertyName("agent_kind")] public required string AgentKind { get; init; }
    [JsonPropertyName("prompt_class")] public required string PromptClass { get; init; }

    public override string ToString() => $"{AgentKind}:{PromptClass}";
}

internal sealed record NotifyPreApprovalPolicy
{
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("accept")] public required IReadOnlyList<NotifyPreApprovalRule> Accept { get; init; }
    [JsonPropertyName("escalate")] public required IReadOnlyList<NotifyPreApprovalRule> Escalate { get; init; }
}

internal sealed record NotifyPreApprovalPolicyStatus
{
    [JsonPropertyName("recorded")] public required bool Recorded { get; init; }
    [JsonPropertyName("status")] public required string Status { get; init; }
    [JsonPropertyName("default_decision")] public required string DefaultDecision { get; init; }
    [JsonPropertyName("path")] public required string Path { get; init; }
    [JsonPropertyName("accept")] public required IReadOnlyList<NotifyPreApprovalRule> Accept { get; init; }
    [JsonPropertyName("escalate")] public required IReadOnlyList<NotifyPreApprovalRule> Escalate { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}

internal sealed record NotifyPreApprovalPolicyReadResult
{
    public required bool Resolved { get; init; }
    public required string Path { get; init; }
    public NotifyPreApprovalPolicy? Policy { get; init; }
    public string? Error { get; init; }
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
            return new NotifyPreApprovalPolicyReadResult { Resolved = true, Path = path, Policy = policy };
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
        var content = JsonSerializer.Serialize(policy, JsonOptions) + Environment.NewLine;
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
        if (policy?.Accept.Any(rule => Matches(rule, agentKind, promptClass)) == true)
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

    private static bool Matches(NotifyPreApprovalRule rule, string agentKind, string promptClass) =>
        string.Equals(rule.AgentKind, agentKind, StringComparison.OrdinalIgnoreCase)
        && string.Equals(rule.PromptClass, promptClass, StringComparison.OrdinalIgnoreCase);

    private static bool Safe(string value) => !string.IsNullOrWhiteSpace(value)
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
