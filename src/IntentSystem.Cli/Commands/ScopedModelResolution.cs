using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

internal sealed record ScopedModelResolutionRequest(
    string InformalName,
    string? RequestedModel,
    string RequestedEffort)
{
    public string Form => RequestedModel is null ? "informal" : "explicit";
}

internal sealed record ScopedModelResolutionTarget(
    string Domain,
    string Team,
    string Role,
    string WorkspaceId,
    string PaneId,
    string Kind,
    string? DeclaredModel,
    string? DeclaredEffort,
    IReadOnlyList<string> RoleAliases,
    string TopologyDigest);

/// <summary>
/// Exact baseline lookup key. Host, process generation, digest, alias spelling,
/// and timestamps are comparison/provenance evidence and intentionally do not
/// participate in lookup.
/// </summary>
internal sealed class ScopedModelResolutionScopeKey : IEquatable<ScopedModelResolutionScopeKey>
{
    public ScopedModelResolutionScopeKey(
        string domain,
        string team,
        string role,
        string workspaceId,
        string paneId,
        string kind,
        string requestForm,
        string? requestedModel,
        string informalName,
        string requestedEffort)
    {
        Domain = domain;
        Team = team;
        Role = role;
        WorkspaceId = workspaceId;
        PaneId = paneId;
        Kind = kind;
        RequestForm = requestForm;
        RequestedModel = requestedModel;
        InformalName = informalName;
        RequestedEffort = requestedEffort;
    }

    public string Domain { get; }
    public string Team { get; }
    public string Role { get; }
    public string WorkspaceId { get; }
    public string PaneId { get; }
    public string Kind { get; }
    public string RequestForm { get; }
    public string? RequestedModel { get; }
    /// <summary>Keyed only for informal requests; explicit form keeps this as attribution.</summary>
    public string InformalName { get; }
    public string RequestedEffort { get; }

    public static ScopedModelResolutionScopeKey Create(
        ScopedModelResolutionTarget target,
        ScopedModelResolutionRequest request) => new(
            target.Domain,
            target.Team,
            target.Role,
            target.WorkspaceId,
            target.PaneId,
            target.Kind,
            request.Form,
            request.RequestedModel,
            request.InformalName,
            request.RequestedEffort);

    public bool Matches(ModelResolutionLedgerEntry entry) =>
        entry.ScopeVersion == 1
        && Equal(entry.Domain, Domain)
        && Equal(entry.Team, Team)
        && Equal(entry.Role, Role)
        && Equal(entry.WorkspaceId, WorkspaceId)
        && Equal(entry.PaneId, PaneId)
        && Equal(entry.Kind, Kind)
        && Equal(entry.RequestForm, RequestForm)
        && Equal(entry.RequestedModel, RequestedModel)
        && (RequestForm == "explicit" || Equal(entry.InformalName, InformalName))
        && Equal(entry.RequestedEffort, RequestedEffort);

    public bool Equals(ScopedModelResolutionScopeKey? other) => other is not null
        && Equal(Domain, other.Domain)
        && Equal(Team, other.Team)
        && Equal(Role, other.Role)
        && Equal(WorkspaceId, other.WorkspaceId)
        && Equal(PaneId, other.PaneId)
        && Equal(Kind, other.Kind)
        && Equal(RequestForm, other.RequestForm)
        && Equal(RequestedModel, other.RequestedModel)
        && (RequestForm == "explicit" || Equal(InformalName, other.InformalName))
        && Equal(RequestedEffort, other.RequestedEffort);

    public override bool Equals(object? obj) => obj is ScopedModelResolutionScopeKey other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Domain, StringComparer.Ordinal);
        hash.Add(Team, StringComparer.Ordinal);
        hash.Add(Role, StringComparer.Ordinal);
        hash.Add(WorkspaceId, StringComparer.Ordinal);
        hash.Add(PaneId, StringComparer.Ordinal);
        hash.Add(Kind, StringComparer.Ordinal);
        hash.Add(RequestForm, StringComparer.Ordinal);
        hash.Add(RequestedModel, StringComparer.Ordinal);
        if (RequestForm != "explicit") hash.Add(InformalName, StringComparer.Ordinal);
        hash.Add(RequestedEffort, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static bool Equal(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}

internal sealed record ModelResolutionInvocationParseResult(
    bool Resolved,
    string Kind,
    string? Model,
    string? Effort,
    string? Reason)
{
    public static ModelResolutionInvocationParseResult Failure(string kind, string reason) =>
        new(false, kind, null, null, reason);
}

/// <summary>
/// Parses only the two measured model flag grammars. This tokenizer has no
/// shell expansion, command execution, or shell-operator interpretation.
/// </summary>
internal static class ModelResolutionInvocationParser
{
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--prompt", "--add-dir", "--system-prompt", "--append-system-prompt",
        "--output-format", "--permission-mode", "--approval-mode", "--sandbox",
        "--ask-for-approval", "--workdir", "--cwd", "--session-id",
        "--allowedTools", "--disallowedTools", "--tools", "--mcp-config",
    };

    // These measured valueless switches are recognized only so their presence
    // cannot make an otherwise valid argv ambiguous. They do not authorize or
    // recommend a launch recipe or permission choice.
    private static readonly HashSet<string> ClaudeBooleanOptions = new(StringComparer.Ordinal)
    {
        "--dangerously-skip-permissions",
        "--allow-dangerously-skip-permissions",
    };

    private static readonly HashSet<string> CodexBooleanOptions = new(StringComparer.Ordinal)
    {
        "--dangerously-bypass-approvals-and-sandbox",
    };

    public static ModelResolutionInvocationParseResult ParseInvocation(string invocation, string kind)
    {
        if (!TryTokenize(invocation, out var tokens, out var error))
            return ModelResolutionInvocationParseResult.Failure(kind, error);
        return ParseArgv(tokens, kind);
    }

    public static ModelResolutionInvocationParseResult ParseArgv(
        IReadOnlyList<string> argv,
        string kind)
    {
        var grammar = AgentLaunchRecipeRegistry.RecordedModelFlagGrammars
            .SingleOrDefault(item => string.Equals(item.Kind, kind, StringComparison.OrdinalIgnoreCase));
        if (grammar is null)
            return ModelResolutionInvocationParseResult.Failure(kind, "unsupported-kind");
        var canonicalKind = grammar.Kind;
        if (argv.Count == 0 || string.IsNullOrWhiteSpace(argv[0]))
            return ModelResolutionInvocationParseResult.Failure(canonicalKind, "argv-unreadable");

        var executable = Path.GetFileNameWithoutExtension(argv[0]);
        if (!string.Equals(executable, canonicalKind, StringComparison.OrdinalIgnoreCase))
            return ModelResolutionInvocationParseResult.Failure(canonicalKind, "provider-mismatch");

        string? model = null;
        string? effort = null;
        var modelCount = 0;
        var effortCount = 0;
        for (var index = 1; index < argv.Count; index++)
        {
            var token = argv[index];
            if (string.IsNullOrEmpty(token))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "argv-unreadable");
            if (token == "--") break;

            if (token.StartsWith("--config", StringComparison.Ordinal))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-config-option");
            if (token == "-m" || token.StartsWith("-m", StringComparison.Ordinal))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-model-short-flag");
            if (token.StartsWith("-c", StringComparison.Ordinal) && token != "-c")
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-inline-config");
            if (canonicalKind == "codex"
                && (token == "-p" || token.StartsWith("-p", StringComparison.Ordinal)
                    || token == "--profile" || token.StartsWith("--profile=", StringComparison.Ordinal)))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-codex-profile-option");
            if (token.StartsWith("--model=", StringComparison.Ordinal)
                || token.StartsWith("--effort=", StringComparison.Ordinal))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-inline-flag");

            if (token == "--model")
            {
                if (!TryReadFlagValue(argv, ref index, out var value, out var flagError))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, flagError);
                if (ContainsQuote(value) || value.Contains('='))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "ambiguous-model-value");
                modelCount++;
                model = value;
                if (modelCount > 1)
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "duplicate-model-flag");
                continue;
            }

            if (canonicalKind == "claude" && token == "--effort")
            {
                if (!TryReadFlagValue(argv, ref index, out var value, out var flagError))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, flagError);
                if (ContainsQuote(value) || value.Contains('='))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "ambiguous-effort-value");
                effortCount++;
                effort = value;
                if (effortCount > 1)
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "duplicate-effort-flag");
                continue;
            }

            if (canonicalKind == "codex" && token == "--effort")
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-effort-flag");

            if (token == "-c")
            {
                if (canonicalKind != "codex")
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-config-option");
                if (index + 1 >= argv.Count)
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "missing-config-value");

                var config = argv[++index];
                const string prefix = "model_reasoning_effort=";
                if (!config.StartsWith(prefix, StringComparison.Ordinal))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-config-key");
                var configuredEffort = config[prefix.Length..];
                if (configuredEffort.Length == 0
                    || configuredEffort.StartsWith("-", StringComparison.Ordinal)
                    || configuredEffort.Contains('=')
                    || configuredEffort.Any(char.IsWhiteSpace)
                    || ContainsQuote(config))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "ambiguous-effort-value");

                effortCount++;
                effort = configuredEffort;
                if (effortCount > 1)
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "duplicate-effort-flag");
                continue;
            }

            var isClaudePrompt = canonicalKind == "claude" && token == "-p";
            if (ValueOptions.Contains(token) || isClaudePrompt)
            {
                if (index + 1 >= argv.Count)
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "missing-option-value");
                var optionValue = argv[index + 1];
                if (string.IsNullOrWhiteSpace(optionValue) || optionValue.StartsWith("-", StringComparison.Ordinal))
                    return ModelResolutionInvocationParseResult.Failure(canonicalKind, "ambiguous-option-value");
                index++;
                continue;
            }

            if ((canonicalKind == "claude" && ClaudeBooleanOptions.Contains(token))
                || (canonicalKind == "codex" && CodexBooleanOptions.Contains(token)))
                continue;

            if (token.StartsWith("--model", StringComparison.Ordinal)
                || token.StartsWith("--effort", StringComparison.Ordinal))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "ambiguous-model-flag");

            if (token.StartsWith("-", StringComparison.Ordinal)
                && index + 1 < argv.Count
                && (argv[index + 1] is "--model" or "--effort" or "-c"
                    || argv[index + 1].StartsWith("--model=", StringComparison.Ordinal)
                    || argv[index + 1].StartsWith("--effort=", StringComparison.Ordinal)
                    || argv[index + 1].StartsWith("-c=", StringComparison.Ordinal)))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "ambiguous-option-value");

            if (token.StartsWith("-", StringComparison.Ordinal))
                return ModelResolutionInvocationParseResult.Failure(canonicalKind, "unsupported-option");
        }

        if (modelCount != 1 || effortCount != 1 || string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(effort))
            return ModelResolutionInvocationParseResult.Failure(canonicalKind, "model-effort-missing");
        return new ModelResolutionInvocationParseResult(true, canonicalKind, model, effort, null);
    }

    private static bool TryReadFlagValue(
        IReadOnlyList<string> argv,
        ref int index,
        out string value,
        out string error)
    {
        if (index + 1 >= argv.Count)
        {
            value = string.Empty;
            error = "missing-flag-value";
            return false;
        }
        var candidate = argv[++index];
        if (string.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("-", StringComparison.Ordinal))
        {
            value = string.Empty;
            error = "ambiguous-flag-value";
            return false;
        }
        value = candidate;
        error = string.Empty;
        return true;
    }

    private static bool TryTokenize(string input, out IReadOnlyList<string> tokens, out string error)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        var tokenStarted = false;
        var closedQuote = false;
        foreach (var character in input)
        {
            if (character is '\r' or '\n' or '\0')
            {
                tokens = [];
                error = "unsupported-invocation-character";
                return false;
            }
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                    closedQuote = true;
                }
                else
                {
                    current.Append(character);
                }
                continue;
            }
            if (char.IsWhiteSpace(character))
            {
                if (tokenStarted)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                    closedQuote = false;
                }
                continue;
            }
            if (closedQuote)
            {
                tokens = [];
                error = "quotes-must-enclose-complete-token";
                return false;
            }
            if (character is '\'' or '"')
            {
                if (tokenStarted)
                {
                    tokens = [];
                    error = "quotes-must-enclose-complete-token";
                    return false;
                }
                quote = character;
                tokenStarted = true;
                continue;
            }
            if (character is ';' or '&' or '|' or '<' or '>' or '$' or '`' or '(' or ')' or '\\')
            {
                tokens = [];
                error = "shell-operator-or-expansion-unsupported";
                return false;
            }
            tokenStarted = true;
            current.Append(character);
        }
        if (quote != '\0')
        {
            tokens = [];
            error = "unbalanced-quote";
            return false;
        }
        if (tokenStarted) result.Add(current.ToString());
        tokens = result;
        error = string.Empty;
        return true;
    }

    private static bool ContainsQuote(string value) => value.Contains('"') || value.Contains('\'');
}

internal sealed record ScopedRoleSelectionResult(
    bool Resolved,
    ScopedModelResolutionTarget? Target,
    string Reason,
    string? Detail);

/// <summary>Resolver-local alias handling for model identity; notify routing is untouched.</summary>
internal static class ScopedModelResolutionTargetSelector
{
    public static ScopedRoleSelectionResult Select(
        NotifyTeamTopology topology,
        string domain,
        string team,
        string requestedRole,
        string requestedKind)
    {
        if (!string.Equals(topology.Domain, domain, StringComparison.Ordinal)
            || !string.Equals(topology.Team, team, StringComparison.Ordinal))
            return Failure("identity-unavailable", "The topology's recorded domain/team identity does not match the requested scope.");
        var canonicalKind = AgentLaunchRecipeRegistry.RecordedModelFlagGrammars
            .SingleOrDefault(item => string.Equals(item.Kind, requestedKind, StringComparison.OrdinalIgnoreCase))?.Kind;
        if (canonicalKind is null)
            return Failure("target-absent", "The requested provider kind has no measured model/effort grammar.");
        if (!LogicalRoleNormalizer.TryNormalize(requestedRole, out var canonicalRole, out _)
            || canonicalRole is null)
            return Failure("target-absent", "The requested role is not a registered logical role or accepted alias.");

        var aliases = topology.Roles
            .Where(item => LogicalRoleNormalizer.TryNormalize(item.Key, out var normalized, out _)
                && string.Equals(normalized, canonicalRole, StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        if (aliases.Length == 0)
            return Failure("target-absent", "No recorded role or accepted alias matches the requested role.");

        var projected = aliases.Select(item => Project(item.Value, topology.WorkspaceId)).ToArray();
        if (projected.Any(item => item is null))
            return Failure("identity-unavailable", "A recorded role alias lacks a usable resident, workspace, pane, or provider kind.");
        var first = projected[0]!;
        var conflicts = aliases.Select((alias, index) => new
            {
                alias.Key,
                Fields = Differences(first, projected[index]!),
            })
            .Where(item => item.Fields.Count > 0)
            .Select(item => $"{item.Key} ({string.Join(", ", item.Fields)})")
            .ToArray();
        if (conflicts.Length > 0)
            return Failure("alias-conflict",
                $"Recorded aliases disagree with reference '{aliases[0].Key}': {string.Join("; ", conflicts)}.");
        if (!string.Equals(first.Resident, NotifyRecordedRole.HerdrResident, StringComparison.Ordinal))
            return Failure("target-absent", "The requested role is external and has no selectable herdr pane.");
        if (!string.Equals(first.Kind, canonicalKind, StringComparison.Ordinal))
            return Failure("target-absent", "The recorded role's provider kind does not match the requested kind.");

        var provenance = aliases.Select(item => item.Key).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var digest = ComputeSelectedDigest(topology, canonicalRole!, aliases, projected.Select(item => item!).ToArray());
        return new ScopedRoleSelectionResult(true, new ScopedModelResolutionTarget(
            domain,
            team,
            canonicalRole!,
            first.WorkspaceId,
            first.PaneId!,
            canonicalKind,
            first.Model,
            first.Effort,
            provenance,
            digest), "target-current", null);
    }

    private static RoleFields? Project(NotifyRecordedRole record, string topologyWorkspace)
    {
        if (record.Resident is not (NotifyRecordedRole.HerdrResident or NotifyRecordedRole.ExternalResident)
            || string.IsNullOrWhiteSpace(record.WorkspaceId ?? topologyWorkspace))
            return null;
        var kind = AgentLaunchRecipeRegistry.RecordedModelFlagGrammars
            .SingleOrDefault(item => string.Equals(item.Kind, record.Kind, StringComparison.OrdinalIgnoreCase))?.Kind;
        if (kind is null || record.Resident == NotifyRecordedRole.HerdrResident
            && string.IsNullOrWhiteSpace(record.PaneId))
            return null;
        return new RoleFields(
            record.Resident,
            record.WorkspaceId ?? topologyWorkspace,
            record.PaneId,
            kind,
            record.Model,
            record.ReasoningEffort);
    }

    private static string ComputeSelectedDigest(
        NotifyTeamTopology topology,
        string canonicalRole,
        IReadOnlyList<KeyValuePair<string, NotifyRecordedRole>> aliases,
        IReadOnlyList<RoleFields> fields)
    {
        var selected = aliases.Select((alias, index) => new
        {
            alias = alias.Key,
            resident = fields[index].Resident,
            workspace_id = fields[index].WorkspaceId,
            pane_id = fields[index].PaneId,
            kind = fields[index].Kind,
            model = fields[index].Model,
            reasoning_effort = fields[index].Effort,
        }).OrderBy(item => item.alias, StringComparer.Ordinal).ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            domain = topology.Domain,
            team = topology.Team,
            workspace_id = topology.WorkspaceId,
            role = canonicalRole,
            aliases = selected,
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static IReadOnlyList<string> Differences(RoleFields expected, RoleFields actual)
    {
        var differences = new List<string>();
        if (!string.Equals(expected.Resident, actual.Resident, StringComparison.Ordinal)) differences.Add("resident");
        if (!string.Equals(expected.WorkspaceId, actual.WorkspaceId, StringComparison.Ordinal)) differences.Add("workspace_id");
        if (!string.Equals(expected.PaneId, actual.PaneId, StringComparison.Ordinal)) differences.Add("pane_id");
        if (!string.Equals(expected.Kind, actual.Kind, StringComparison.Ordinal)) differences.Add("kind");
        if (!string.Equals(expected.Model, actual.Model, StringComparison.Ordinal)) differences.Add("model");
        if (!string.Equals(expected.Effort, actual.Effort, StringComparison.Ordinal)) differences.Add("reasoning_effort");
        return differences;
    }

    private static ScopedRoleSelectionResult Failure(string reason, string detail) =>
        new(false, null, reason, detail);

    private sealed record RoleFields(
        string Resident,
        string WorkspaceId,
        string? PaneId,
        string Kind,
        string? Model,
        string? Effort);
}
