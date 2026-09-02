using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The payload extracted from a <c>codex:shell-command</c> dialog. The
/// payload is evidence only: it does not carry an answer and cannot authorize
/// itself.
/// </summary>
internal sealed record ShellCommandPromptPayload
{
    public required string Command { get; init; }
    public required string DialogHash { get; init; }
    public required string CommandDigest { get; init; }
    public required ShellCommandParseResult Parse { get; init; }
}

internal sealed record ShellCommandParseResult
{
    public required bool Parsed { get; init; }
    public required IReadOnlyList<ShellCommandSegment> Segments { get; init; }
    public required string Normalized { get; init; }
    public required string Digest { get; init; }
    public string? Error { get; init; }
    public bool UnknownSyntax { get; init; }
}

internal sealed record ShellCommandSegment
{
    public required IReadOnlyList<string> Argv { get; init; }
    public required IReadOnlyList<ShellCommandRedirect> Redirects { get; init; }
    public string? OperatorToNext { get; init; }

    public bool HasHeredoc => Redirects.Any(redirect => redirect.IsHeredoc);
}

internal sealed record ShellCommandRedirect
{
    public required string Operator { get; init; }
    public required string Target { get; init; }
    public string? Body { get; init; }
    public bool IsHeredoc { get; init; }
}

/// <summary>
/// A team-recorded instance of a shipped shell scope. The class layer names
/// and extracts a command; this record is the only policy layer that may make
/// a shell command answerable. It deliberately contains both a persistence
/// bit and an operator-only acknowledgement so a persistent provider setting
/// can never be mistaken for a bounded per-dialog answer.
/// </summary>
internal sealed record NotifyScopedPromptPolicy
{
    [JsonPropertyName("policy_id")] public string PolicyId { get; init; } = string.Empty;
    [JsonPropertyName("agent_kind")] public string AgentKind { get; init; } = string.Empty;
    [JsonPropertyName("prompt_class")] public string PromptClass { get; init; } = string.Empty;
    [JsonPropertyName("scope")] public string Scope { get; init; } = string.Empty;
    [JsonPropertyName("decision")] public string Decision { get; init; } = "accept";
    [JsonPropertyName("argv_token_prefix")] public IReadOnlyList<string> ArgvTokenPrefix { get; init; } = [];
    [JsonPropertyName("category")] public string Category { get; init; } = string.Empty;
    [JsonPropertyName("cwd")] public string? Cwd { get; init; }
    [JsonPropertyName("root")] public string? Root { get; init; }
    [JsonPropertyName("path_constraints")] public IReadOnlyList<string> PathConstraints { get; init; } = [];
    [JsonPropertyName("effect_tags")] public IReadOnlyList<string> EffectTags { get; init; } = [];
    [JsonPropertyName("persistent")] public bool Persistent { get; init; }
    [JsonPropertyName("operator_only_persistence")] public bool OperatorOnlyPersistence { get; init; }
    [JsonPropertyName("answerable_by")] public string AnswerableBy { get; init; } = "orchestration";
    [JsonPropertyName("answer_keys")] public IReadOnlyList<string> AnswerKeys { get; init; } = [];
    [JsonPropertyName("scratch_ledger_paths")] public IReadOnlyList<string> ScratchLedgerPaths { get; init; } = [];
    [JsonPropertyName("scratch_ledger_cycle_id")] public string? ScratchLedgerCycleId { get; init; }
    [JsonPropertyName("command_digest")] public string? CommandDigest { get; init; }
    [JsonPropertyName("dialog_hash")] public string? DialogHash { get; init; }
    [JsonPropertyName("applicable")] public bool Applicable { get; init; }
    [JsonPropertyName("applicability_status")]
    public string ApplicabilityStatus { get; init; } = NotifyPromptClassProducerRegistry.InapplicableStatus;
    [JsonPropertyName("inapplicability_reason")] public string? InapplicabilityReason { get; init; }

    public override string ToString() => $"{AgentKind}:{PromptClass}:{Scope}:{Decision}";
}

internal sealed record ShellCommandScopeDefinition
{
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> AnswerKeys { get; init; }
    public required string Persistence { get; init; }
    public required string AnswerableBy { get; init; }
    public IReadOnlyList<string> RiskTags { get; init; } = [];
}

internal sealed record ShellCommandPolicyAuthorization
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
    public IReadOnlyList<string> RiskTags { get; init; } = [];
}

/// <summary>
/// G689's shell vocabulary and policy evaluator. This is intentionally
/// separate from <see cref="AgentLaunchRecipeRegistry"/>: the registry owns
/// shipped dialog recognition, while this type owns the narrower policy
/// instances teams may record against that vocabulary.
/// </summary>
internal static class ShellCommandPolicyRegistry
{
    public const string AgentKind = "codex";
    public const string PromptClass = "shell-command";
    public const string ProjectTestScope = "project-test";
    public const string OwnedScratchDeleteScope = "owned-scratch-delete";
    public const string ExactCommandOnceScope = "exact-command-once";
    private static readonly IReadOnlyDictionary<string, ShellCommandScopeDefinition> Definitions =
        new Dictionary<string, ShellCommandScopeDefinition>(StringComparer.Ordinal)
        {
            [ProjectTestScope] = new ShellCommandScopeDefinition
            {
                Name = ProjectTestScope,
                Category = "test-execution",
                Description =
                    "A recorded test invocation whose argv token sequence is bound to a project root. Tests execute arbitrary code and are not read-only.",
                AnswerKeys = ["y", "enter"],
                Persistence = "per-dialog",
                AnswerableBy = PromptCapabilityResolver.OrchestrationRole,
            },
            [OwnedScratchDeleteScope] = new ShellCommandScopeDefinition
            {
                Name = OwnedScratchDeleteScope,
                Category = "destructive-scratch-cleanup",
                Description =
                    "An exact rm -f path recorded in the current cycle's scratch ledger; stale wake identities are refused, destructive cleanup is orchestration-only, and a broad /tmp allowance is never valid.",
                AnswerKeys = ["y", "enter"],
                Persistence = "per-dialog",
                AnswerableBy = PromptCapabilityResolver.OrchestrationRole,
                RiskTags = [PromptRiskFloor.Destructive],
            },
            [ExactCommandOnceScope] = new ShellCommandScopeDefinition
            {
                Name = ExactCommandOnceScope,
                Category = "exact-command",
                Description =
                    "One normalized shell AST digest paired with the current dialog hash; the authorization is consumed after one bounded answer.",
                AnswerKeys = ["y", "enter"],
                Persistence = "single-use-per-dialog",
                AnswerableBy = PromptCapabilityResolver.OrchestrationRole,
            },
        };

    public static IReadOnlyList<ShellCommandScopeDefinition> Scopes => Definitions.Values
        .OrderBy(scope => scope.Name, StringComparer.Ordinal)
        .ToArray();

    public static bool IsScope(string scope) => Definitions.ContainsKey(scope);

    public static ShellCommandScopeDefinition? FindScope(string scope) =>
        Definitions.GetValueOrDefault(scope);

    public static bool TryValidate(
        NotifyScopedPromptPolicy policy,
        out string error,
        bool requireScratchLedgerCycleId = true)
    {
        if (!string.Equals(policy.AgentKind, AgentKind, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(policy.PromptClass, PromptClass, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Scoped shell policies must target {AgentKind}:{PromptClass}.";
            return false;
        }

        if (!Definitions.TryGetValue(policy.Scope, out var scope))
        {
            error = $"Unknown shell scope '{policy.Scope}'. Known scopes: {string.Join(", ", Definitions.Keys.OrderBy(value => value, StringComparer.Ordinal))}.";
            return false;
        }

        if (!string.Equals(policy.Category, scope.Category, StringComparison.Ordinal))
        {
            error = $"Shell scope '{policy.Scope}' requires category '{scope.Category}'.";
            return false;
        }

        if (policy.Decision is not ("accept" or "escalate"))
        {
            error = "A scoped shell policy decision must be 'accept' or 'escalate'.";
            return false;
        }

        if (!string.Equals(policy.AnswerableBy, scope.AnswerableBy, StringComparison.Ordinal))
        {
            error = $"Shell scope '{policy.Scope}' declares answerable_by='{scope.AnswerableBy}'; the policy cannot override it with '{policy.AnswerableBy}'.";
            return false;
        }

        if (policy.Persistent && !policy.OperatorOnlyPersistence)
        {
            error = "Persistent shell allowances require an explicit operator_only_persistence=true record; the tool never presses a persistent option.";
            return false;
        }

        if (policy.ArgvTokenPrefix.Any(token => string.IsNullOrWhiteSpace(token)
            || token is ";" or "&&" or "||" or "|" or "(" or ")"
            || token.Contains("$(", StringComparison.Ordinal)
            || token.Contains((char)96)))
        {
            error = "argv_token_prefix must contain literal shell argv tokens, never shell operators or substitutions.";
            return false;
        }

        if (policy.AnswerKeys.Count > 0 && !policy.AnswerKeys.SequenceEqual(scope.AnswerKeys, StringComparer.Ordinal))
        {
            error = $"The bounded answer keys for scope '{policy.Scope}' must be [{string.Join(", ", scope.AnswerKeys)}]; custom or persistent key sequences are not accepted.";
            return false;
        }

        if (scope.Name == ProjectTestScope)
        {
            if (policy.ArgvTokenPrefix.Count < 2
                || !string.Equals(policy.ArgvTokenPrefix[0], "dotnet", StringComparison.Ordinal)
                || !string.Equals(policy.ArgvTokenPrefix[1], "test", StringComparison.Ordinal))
            {
                error = "project-test requires an argv_token_prefix beginning with the recorded dotnet test invocation.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(policy.Root) || string.IsNullOrWhiteSpace(policy.Cwd))
            {
                error = "project-test requires both the recorded project root and cwd.";
                return false;
            }

            if (policy.PathConstraints.Count == 0)
            {
                error = "project-test requires at least one path constraint under the recorded project root.";
                return false;
            }

            if (policy.PathConstraints.Any(path => !IsWithinFrom(path, policy.Root, policy.Cwd)))
            {
                error = "Every project-test path constraint must remain under the recorded project root.";
                return false;
            }

            if (policy.ArgvTokenPrefix.Skip(2).Any(token => LooksLikePath(token)
                && !InsideAnyPath(token, policy.Cwd, policy.Root, policy.PathConstraints)))
            {
                error = "Every path-looking project-test argv prefix token must remain under the recorded project root.";
                return false;
            }

            if (!policy.EffectTags.Any(tag => string.Equals(tag, "executes-tests", StringComparison.OrdinalIgnoreCase)))
            {
                error = "project-test must carry the effect tag 'executes-tests'; it is not a read-only scope.";
                return false;
            }
        }
        else if (scope.Name == OwnedScratchDeleteScope)
        {
            if (policy.ArgvTokenPrefix.Count == 0
                || !string.Equals(policy.ArgvTokenPrefix[0], "rm", StringComparison.Ordinal))
            {
                error = "owned-scratch-delete requires an rm argv_token_prefix.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(policy.Cwd)
                || policy.ScratchLedgerPaths.Count == 0
                || policy.PathConstraints.Count == 0)
            {
                error = "owned-scratch-delete requires the recorded cwd, exact scratch_ledger_paths, and path_constraints; a bare /tmp prefix is forbidden.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(policy.ScratchLedgerCycleId))
            {
                if (requireScratchLedgerCycleId)
                {
                    error = "owned-scratch-delete requires the current wake/cycle identity in scratch_ledger_cycle_id; an unbound scratch ledger cannot authorize deletion.";
                    return false;
                }
            }
            else if (!IsCycleIdentity(policy.ScratchLedgerCycleId))
            {
                error = "scratch_ledger_cycle_id must be a non-empty safe wake/cycle identity.";
                return false;
            }

            if (!policy.ScratchLedgerPaths.All(path => policy.PathConstraints.Any(constraint => SamePathFrom(path, constraint, policy.Cwd))))
            {
                error = "Every owned-scratch-delete scratch ledger path must be an exact path constraint.";
                return false;
            }

            if (policy.ArgvTokenPrefix.Skip(1).Any(flag => !IsDeleteFlag(flag)))
            {
                error = "owned-scratch-delete argv_token_prefix may contain only rm deletion flags; paths remain exact ledger entries.";
                return false;
            }

            if (!policy.EffectTags.Any(tag => string.Equals(tag, "destructive", StringComparison.OrdinalIgnoreCase)))
            {
                error = "owned-scratch-delete must carry the effect tag 'destructive'.";
                return false;
            }
        }
        else if (scope.Name == ExactCommandOnceScope)
        {
            if (!IsDigest(policy.CommandDigest) || !IsDigest(policy.DialogHash))
            {
                error = "exact-command-once requires 64-character command_digest and dialog_hash values.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static ShellCommandPolicyAuthorization Evaluate(
        ShellCommandPromptPayload payload,
        IReadOnlyList<NotifyScopedPromptPolicy> policies,
        string? cwd,
        IReadOnlyList<NotifyPromptAudit>? promptAudits = null,
        string? currentCycleId = null)
    {
        var rulePrefix = $"{AgentKind}:{PromptClass}";
        if (!payload.Parse.Parsed)
        {
            return Refused(
                rulePrefix,
                payload,
                "shell-ast-unparseable",
                $"The shell payload could not be parsed safely: {payload.Parse.Error ?? "unknown syntax"}. Fail closed to escalation.");
        }

        var validPolicies = policies
            .Where(policy => policy.Applicable
                && string.Equals(policy.AgentKind, AgentKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(policy.PromptClass, PromptClass, StringComparison.OrdinalIgnoreCase))
            .Where(policy => TryValidate(policy, out _))
            .ToArray();
        if (validPolicies.Length == 0)
        {
            return Refused(
                rulePrefix,
                payload,
                "shell-policy-unmatched",
                "The shell class is recognized, but no applicable scoped policy instance covers it; bare-class approval is impossible.");
        }

        var exact = validPolicies
            .Where(policy => string.Equals(policy.Scope, ExactCommandOnceScope, StringComparison.Ordinal)
                && string.Equals(policy.CommandDigest, payload.CommandDigest, StringComparison.OrdinalIgnoreCase)
                && string.Equals(policy.DialogHash, payload.DialogHash, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length > 0)
        {
            var exactPolicy = exact[0];
            var scopes = exact.Select(policy => policy.Scope).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (promptAudits?.Any(audit =>
                    string.Equals(audit.CommandDigest, payload.CommandDigest, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(audit.DialogHash, payload.DialogHash, StringComparison.OrdinalIgnoreCase)
                    && audit.MatchedScopes.Any(scopeName => string.Equals(scopeName, ExactCommandOnceScope, StringComparison.Ordinal))
                    && audit.Outcome is "authorized-before-execution" or "bounded-answer-execution-pending" or "bounded-answer-executed") == true)
            {
                return Refused(
                    Rule(rulePrefix, scopes),
                    payload,
                    "exact-command-once-consumed",
                    "The exact-command-once digest/dialog pair was already authorized or is execution-pending; no answer retry is permitted.",
                    scopes);
            }

            if (exactPolicy.Persistent)
            {
                return Refused(
                    Rule(rulePrefix, scopes),
                    payload,
                    "persistent-allowance-operator-only",
                    "The policy names a persistent allowance. The tool never presses a persistent provider option; an operator must handle it.",
                    scopes);
            }

            return Authorization(
                exactPolicy,
                payload,
                scopes,
                exactPolicy.Decision == "accept"
                    ? "The normalized shell AST and current dialog hash match the single-use exact-command-once scope."
                    : "The exact-command-once scope explicitly escalates this dialog.");
        }

        var matches = new List<NotifyScopedPromptPolicy>();
        foreach (var segment in payload.Parse.Segments)
        {
            var declaredOwnedPolicies = policies
                .Where(policy => policy.Applicable
                    && string.Equals(policy.AgentKind, AgentKind, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(policy.PromptClass, PromptClass, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(policy.Scope, OwnedScratchDeleteScope, StringComparison.Ordinal))
                .ToArray();
            if (IsOwnedScratchSegment(segment)
                && declaredOwnedPolicies.Length > 0
                && !validPolicies.Any(policy => string.Equals(policy.Scope, OwnedScratchDeleteScope, StringComparison.Ordinal)
                    && string.Equals(policy.ScratchLedgerCycleId, currentCycleId, StringComparison.Ordinal)))
            {
                return Refused(
                    Rule(rulePrefix, matches.Select(policy => policy.Scope)),
                    payload,
                    "owned-scratch-delete-stale-cycle",
                    "The owned-scratch-delete policy is bound to a different or missing wake/cycle identity; stale scratch ledger entries cannot authorize deletion.",
                    matches.Select(policy => policy.Scope).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
            }

            var candidates = validPolicies
                .Where(policy => string.Equals(policy.Scope, ProjectTestScope, StringComparison.Ordinal)
                    || string.Equals(policy.Scope, OwnedScratchDeleteScope, StringComparison.Ordinal))
                .Where(policy => MatchesSegment(policy, segment, cwd, currentCycleId))
                .OrderBy(policy => policy.Scope, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                var uncoveredTarget = FindUncoveredOwnedScratchTarget(
                    segment,
                    validPolicies,
                    cwd,
                    currentCycleId);
                return Refused(
                    Rule(rulePrefix, matches.Select(policy => policy.Scope)),
                    payload,
                    "shell-segment-out-of-scope",
                    "Every shell AST segment must be covered by a matching scoped policy; an unknown command, substitution, redirect, or out-of-scope path remains escalate-only."
                    + (uncoveredTarget is null ? string.Empty : $" Uncovered path: {uncoveredTarget}."),
                    matches.Select(policy => policy.Scope).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
            }

            matches.Add(candidates[0]);
        }

        var matchedScopes = matches
            .Select(policy => policy.Scope)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var escalation = matches.FirstOrDefault(policy => policy.Decision == "escalate");
        if (matches.Any(policy => policy.Persistent))
        {
            return Refused(
                Rule(rulePrefix, matchedScopes),
                payload,
                "persistent-allowance-operator-only",
                "A matching policy names a persistent allowance. The tool never presses a persistent provider option; an operator must handle it.",
                matchedScopes);
        }

        return Authorization(
            escalation ?? matches[0],
            payload,
            matchedScopes,
            escalation is null
                ? $"All {payload.Parse.Segments.Count} shell AST segment(s) are inside the matched scopes; answers remain bounded to this dialog."
                : "A matching scoped policy explicitly escalates this dialog.");
    }

    private static bool MatchesSegment(
        NotifyScopedPromptPolicy policy,
        ShellCommandSegment segment,
        string? cwd,
        string? currentCycleId)
    {
        var scope = policy.Scope;
        if (scope == ProjectTestScope)
        {
            if (segment.HasHeredoc || segment.Redirects.Count > 0 || !HasTokenPrefix(segment.Argv, policy.ArgvTokenPrefix))
            {
                return false;
            }

            if (policy.Cwd is null || cwd is null || !SamePathFrom(policy.Cwd, cwd, cwd) || policy.Root is null)
            {
                return false;
            }

            if (segment.Argv.Skip(policy.ArgvTokenPrefix.Count).Any(token => LooksLikePath(token)
                && !InsideAnyPath(token, cwd, policy.Root, policy.PathConstraints)))
            {
                return false;
            }

            return true;
        }

        if (scope == OwnedScratchDeleteScope)
        {
            if (!string.Equals(policy.ScratchLedgerCycleId, currentCycleId, StringComparison.Ordinal))
            {
                return false;
            }

            if (segment.Redirects.Count > 0
                || segment.HasHeredoc
                || cwd is null
                || segment.Argv.Count == 0
                || !string.Equals(segment.Argv[0], "rm", StringComparison.Ordinal))
            {
                return false;
            }

            if (!HasTokenPrefix(segment.Argv, policy.ArgvTokenPrefix))
            {
                return false;
            }

            var targets = segment.Argv.Skip(policy.ArgvTokenPrefix.Count)
                .Where(token => !token.StartsWith("-", StringComparison.Ordinal))
                .ToArray();
            var flags = segment.Argv.Skip(policy.ArgvTokenPrefix.Count)
                .Where(token => token.StartsWith("-", StringComparison.Ordinal))
                .ToArray();
            if (targets.Length == 0 || flags.Any(flag => !IsDeleteFlag(flag)))
            {
                return false;
            }

            return targets.All(target => policy.ScratchLedgerPaths.Any(path => SamePathFrom(path, target, cwd))
                && policy.PathConstraints.Any(path => SamePathFrom(path, target, cwd)));
        }

        return false;
    }

    private static bool IsOwnedScratchSegment(ShellCommandSegment segment) =>
        segment.Argv.Count > 0
        && string.Equals(segment.Argv[0], "rm", StringComparison.Ordinal);

    private static string? FindUncoveredOwnedScratchTarget(
        ShellCommandSegment segment,
        IReadOnlyList<NotifyScopedPromptPolicy> policies,
        string? cwd,
        string? currentCycleId)
    {
        if (!IsOwnedScratchSegment(segment) || cwd is null)
        {
            return null;
        }

        var eligiblePolicies = policies
            .Where(policy => string.Equals(policy.Scope, OwnedScratchDeleteScope, StringComparison.Ordinal))
            .Where(policy => string.Equals(policy.ScratchLedgerCycleId, currentCycleId, StringComparison.Ordinal))
            .Where(policy => HasTokenPrefix(segment.Argv, policy.ArgvTokenPrefix))
            .ToArray();
        if (eligiblePolicies.Length == 0)
        {
            return null;
        }

        return segment.Argv
            .Skip(1)
            .Where(token => !token.StartsWith("-", StringComparison.Ordinal))
            .FirstOrDefault(target => !eligiblePolicies.Any(policy =>
                policy.ScratchLedgerPaths.Any(path => SamePathFrom(path, target, cwd))
                && policy.PathConstraints.Any(path => SamePathFrom(path, target, cwd))));
    }

    private static ShellCommandPolicyAuthorization Authorization(
        NotifyScopedPromptPolicy policy,
        ShellCommandPromptPayload payload,
        IReadOnlyList<string> scopes,
        string summary) => new()
        {
            Decision = policy.Decision,
            Rule = Rule($"{AgentKind}:{PromptClass}", scopes),
            Summary = summary,
            MatchedScopes = scopes,
            AnswerKeys = policy.Decision == "accept" && policy.AnswerKeys.Count > 0
                ? policy.AnswerKeys
                : policy.Decision == "accept" ? FindScope(policy.Scope)!.AnswerKeys : [],
            ExactAnswerScope = policy.Decision == "accept"
                ? string.Join(", ", scopes.Select(scope =>
                    $"{scope}: {FindScope(scope)?.Description ?? "scoped shell policy"} per-dialog; persistent allowances are operator-only"))
                : null,
            CommandDigest = payload.CommandDigest,
            DialogHash = payload.DialogHash,
            AnswerableBy = FindScope(policy.Scope)?.AnswerableBy ?? PromptCapabilityResolver.OrchestrationRole,
            RiskTags = scopes
                .SelectMany(scope => FindScope(scope)?.RiskTags ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .ToArray(),
        };

    private static ShellCommandPolicyAuthorization Refused(
        string rule,
        ShellCommandPromptPayload payload,
        string cause,
        string summary,
        IReadOnlyList<string>? scopes = null) => new()
        {
            Decision = "escalate",
            Rule = rule + (rule.Contains(cause, StringComparison.Ordinal) ? string.Empty : $" ({cause})"),
            Summary = summary,
            MatchedScopes = scopes ?? [],
            AnswerKeys = [],
            ExactAnswerScope = null,
            CommandDigest = payload.CommandDigest,
            DialogHash = payload.DialogHash,
            AnswerableBy = ResolveAnswerableBy(scopes),
            RiskTags = (scopes ?? [])
                .SelectMany(scope => FindScope(scope)?.RiskTags ?? [])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .ToArray(),
        };

    private static string ResolveAnswerableBy(IReadOnlyList<string>? scopes)
    {
        if (scopes is null)
        {
            return PromptCapabilityResolver.OrchestrationRole;
        }

        var roles = scopes
            .Select(scope => FindScope(scope)?.AnswerableBy ?? PromptCapabilityResolver.OrchestrationRole)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
        return roles.Length == 0
            ? PromptCapabilityResolver.OrchestrationRole
            : string.Join(",", roles);
    }

    private static string Rule(string prefix, IEnumerable<string> scopes)
    {
        var values = scopes.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        return values.Length == 0 ? prefix : $"{prefix}[{string.Join('+', values)}]";
    }

    private static bool HasTokenPrefix(IReadOnlyList<string> tokens, IReadOnlyList<string> prefix) =>
        prefix.Count > 0
        && tokens.Count >= prefix.Count
        && tokens.Take(prefix.Count).SequenceEqual(prefix, StringComparer.Ordinal);

    private static bool InsideAnyPath(
        string token,
        string cwd,
        string root,
        IReadOnlyList<string> constraints)
    {
        var resolved = ResolvePath(token, cwd);
        if (resolved is null || !IsWithinFrom(resolved, root, cwd))
        {
            return false;
        }

        return constraints.Any(constraint => IsWithinFrom(resolved, constraint, cwd));
    }

    private static bool LooksLikePath(string token) =>
        token.StartsWith("/", StringComparison.Ordinal)
        || token.StartsWith("./", StringComparison.Ordinal)
        || token.StartsWith("../", StringComparison.Ordinal)
        || token.Contains('/', StringComparison.Ordinal)
        || token.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || token.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string? ResolvePath(string value, string? basePath)
    {
        if (value.StartsWith('~') || value.Contains('$', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(basePath ?? Directory.GetCurrentDirectory(), value));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsWithinFrom(string path, string root, string? basePath)
    {
        var resolvedPath = ResolvePath(path, basePath);
        var resolvedRoot = ResolvePath(root, basePath);
        if (resolvedPath is null || resolvedRoot is null)
        {
            return false;
        }

        var relative = Path.GetRelativePath(resolvedRoot, resolvedPath);
        return relative is "."
            || (!relative.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relative));
    }

    private static bool SamePathFrom(string left, string right, string? basePath)
    {
        var a = ResolvePath(left, basePath);
        var b = ResolvePath(right, basePath);
        return a is not null && b is not null
            && string.Equals(
                a,
                b,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool IsDeleteFlag(string value) => value is "-f" or "--force"
        or "-r" or "-R" or "-rf" or "-fr" or "--recursive";

    private static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(char.IsAsciiHexDigit);

    private static bool IsCycleIdentity(string value) => value.Length > 0
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

/// <summary>
/// Small, deliberately conservative shell lexer. It recognizes the operators
/// needed for scope decomposition and heredocs, but rejects command
/// substitution, subshells, asynchronous lists, unclosed quotes, and anything
/// it cannot prove safe. It is not intended to be a general shell.
/// </summary>
internal static class ShellCommandAstParser
{
    public static ShellCommandParseResult Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Failure("The shell payload was empty.");
        }

        var segments = new List<ShellCommandSegment>();
        var argv = new List<string>();
        var redirects = new List<ShellCommandRedirect>();
        var token = new StringBuilder();
        var tokenStarted = false;
        var index = 0;

        while (index < command.Length)
        {
            var character = command[index];
            if (char.IsWhiteSpace(character))
            {
                FlushToken();
                if (character == '\n' && (argv.Count > 0 || redirects.Count > 0))
                {
                    FinishSegment(";");
                }
                index++;
                continue;
            }

            if (character == '\\')
            {
                if (index + 1 >= command.Length)
                {
                    return Failure("A trailing escape is unknown shell syntax.");
                }

                if (command[index + 1] == '\n')
                {
                    index += 2;
                    continue;
                }

                token.Append(command[index + 1]);
                tokenStarted = true;
                index += 2;
                continue;
            }

            if (character is '\'' or '"')
            {
                if (!ReadQuoted(command, ref index, character, token))
                {
                    return Failure("An unclosed shell quote is unknown syntax.");
                }

                tokenStarted = true;
                continue;
            }

            if (character == '$')
            {
                return Failure("Shell variable expansion and command substitution are unknown syntax; fail closed.");
            }

            if (character == (char)96 || character is '(' or ')')
            {
                return Failure("Backticks and subshell syntax are not accepted by the shell AST verifier.");
            }

            if (character == '#'
                && !tokenStarted
                && (index == 0 || char.IsWhiteSpace(command[index - 1])))
            {
                var newline = command.IndexOf('\n', index);
                index = newline < 0 ? command.Length : newline;
                continue;
            }

            if (character is ';' or '|' or '&')
            {
                FlushToken();
                var op = ReadControlOperator(command, ref index);
                if (op is null)
                {
                    return Failure("An asynchronous or unsupported shell operator was found.");
                }

                if (argv.Count == 0 && redirects.Count == 0)
                {
                    return Failure($"The shell operator '{op}' did not have a command segment.");
                }

                FinishSegment(op);
                continue;
            }

            if (character is '<' or '>')
            {
                FlushToken();
                if (argv.Count == 0)
                {
                    return Failure("A redirect without a command segment is not in scope.");
                }

                var redirectOperator = ReadRedirectOperator(command, ref index);
                if (redirectOperator == "<<")
                {
                    SkipSpaces(command, ref index);
                    if (!TryReadWord(command, ref index, out var delimiter))
                    {
                        return Failure("A heredoc has no delimiter.");
                    }

                    SkipSpaces(command, ref index);
                    if (index >= command.Length || command[index] != '\n')
                    {
                        return Failure("A heredoc delimiter must end the command line.");
                    }

                    var bodyStart = ++index;
                    var bodyEnd = -1;
                    var afterDelimiter = -1;
                    while (index <= command.Length)
                    {
                        var lineEnd = command.IndexOf('\n', index);
                        if (lineEnd < 0)
                        {
                            lineEnd = command.Length;
                        }

                        var line = command[index..lineEnd].TrimEnd('\r');
                        if (string.Equals(line, delimiter, StringComparison.Ordinal))
                        {
                            bodyEnd = index;
                            afterDelimiter = lineEnd < command.Length ? lineEnd + 1 : lineEnd;
                            break;
                        }

                        if (lineEnd == command.Length)
                        {
                            break;
                        }

                        index = lineEnd + 1;
                    }

                    if (bodyEnd < 0)
                    {
                        return Failure("The heredoc delimiter was not found.");
                    }

                    var body = command[bodyStart..bodyEnd];
                    if (body.Contains("$(", StringComparison.Ordinal)
                        || body.Contains((char)96))
                    {
                        return Failure("Command substitution inside a heredoc is unknown syntax.");
                    }

                    redirects.Add(new ShellCommandRedirect
                    {
                        Operator = redirectOperator,
                        Target = delimiter,
                        Body = body,
                        IsHeredoc = true,
                    });
                    index = afterDelimiter;
                    continue;
                }

                SkipSpaces(command, ref index);
                if (!TryReadWord(command, ref index, out var target))
                {
                    return Failure($"Redirect '{redirectOperator}' has no target.");
                }

                redirects.Add(new ShellCommandRedirect
                {
                    Operator = redirectOperator,
                    Target = target,
                });
                continue;
            }

            token.Append(character);
            tokenStarted = true;
            index++;
        }

        FlushToken();
        if (argv.Count == 0 && redirects.Count == 0)
        {
            if (segments.Count == 0)
            {
                return Failure("The shell payload contained no command segment.");
            }

            if (segments[^1].OperatorToNext is not ";")
            {
                return Failure("The shell payload ended after a control operator.");
            }

            segments[^1] = segments[^1] with { OperatorToNext = null };
            var trailingNormalized = Normalize(segments);
            return new ShellCommandParseResult
            {
                Parsed = true,
                Segments = segments,
                Normalized = trailingNormalized,
                Digest = Digest(trailingNormalized),
            };
        }

        FinishSegment(null);
        var normalized = Normalize(segments);
        return new ShellCommandParseResult
        {
            Parsed = true,
            Segments = segments,
            Normalized = normalized,
            Digest = Digest(normalized),
        };

        void FlushToken()
        {
            if (!tokenStarted)
            {
                return;
            }

            argv.Add(token.ToString());
            token.Clear();
            tokenStarted = false;
        }

        void FinishSegment(string? operatorToNext)
        {
            FlushToken();
            segments.Add(new ShellCommandSegment
            {
                Argv = argv.ToArray(),
                Redirects = redirects.ToArray(),
                OperatorToNext = operatorToNext,
            });
            argv.Clear();
            redirects.Clear();
        }
    }

    private static bool ReadQuoted(string input, ref int index, char quote, StringBuilder token)
    {
        index++;
        while (index < input.Length)
        {
            var character = input[index];
            if (character == quote)
            {
                index++;
                return true;
            }

            // `$` performs expansion/command substitution in double quotes;
            // backticks are substitution syntax in every quoting context.
            // Reject both rather than treating an unverified payload as a
            // literal token that a scope could accidentally authorize.
            if (character == (char)96 || (quote == '"' && character == '$'))
            {
                return false;
            }

            if (quote == '"' && character == '\\')
            {
                if (index + 1 >= input.Length)
                {
                    return false;
                }

                token.Append(input[index + 1]);
                index += 2;
                continue;
            }

            token.Append(character);
            index++;
        }

        return false;
    }

    private static string? ReadControlOperator(string input, ref int index)
    {
        if (input[index] == '&')
        {
            if (index + 1 < input.Length && input[index + 1] == '&')
            {
                index += 2;
                return "&&";
            }

            return null;
        }

        if (input[index] == '|')
        {
            if (index + 1 < input.Length && input[index + 1] == '|')
            {
                index += 2;
                return "||";
            }

            index++;
            return "|";
        }

        index++;
        return ";";
    }

    private static string ReadRedirectOperator(string input, ref int index)
    {
        var start = index;
        if (input[index] == '<' && index + 1 < input.Length && input[index + 1] == '<')
        {
            index += 2;
            return "<<";
        }

        if (input[index] == '>' && index + 1 < input.Length && input[index + 1] == '>')
        {
            index += 2;
            return ">>";
        }

        index++;
        return input[start].ToString();
    }

    private static void SkipSpaces(string input, ref int index)
    {
        while (index < input.Length && input[index] is ' ' or '\t' or '\r')
        {
            index++;
        }
    }

    private static bool TryReadWord(string input, ref int index, out string word)
    {
        var builder = new StringBuilder();
        var started = false;
        while (index < input.Length)
        {
            var character = input[index];
            if (char.IsWhiteSpace(character) || character is ';' or '|' or '&' or '<' or '>')
            {
                break;
            }

            if (character is '\'' or '"')
            {
                if (!ReadQuoted(input, ref index, character, builder))
                {
                    word = string.Empty;
                    return false;
                }

                started = true;
                continue;
            }

            if (character == '$' || character == (char)96)
            {
                word = string.Empty;
                return false;
            }

            builder.Append(character);
            started = true;
            index++;
        }

        word = builder.ToString();
        return started && word.Length > 0;
    }

    private static string Normalize(IReadOnlyList<ShellCommandSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            AppendPart(builder, "segment", string.Empty);
            AppendPart(builder, "argv-count", segment.Argv.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var argument in segment.Argv)
            {
                // Length-prefix each AST value so `cmd "a b"` cannot share a
                // digest with `cmd a b` merely because both contain the same
                // printable characters after whitespace normalization.
                AppendPart(builder, "argv", argument);
            }

            foreach (var redirect in segment.Redirects)
            {
                AppendPart(builder, "redirect-op", redirect.Operator);
                AppendPart(builder, "redirect-target", redirect.Target);
                if (redirect.IsHeredoc)
                {
                    AppendPart(builder, "heredoc", redirect.Body ?? string.Empty);
                }
            }

            AppendPart(builder, "redirect-count", segment.Redirects.Count.ToString(CultureInfo.InvariantCulture));
            AppendPart(builder, "operator", segment.OperatorToNext ?? string.Empty);
        }

        return builder.ToString();
    }

    private static void AppendPart(StringBuilder builder, string name, string value) =>
        builder.Append(name).Append('[').Append(value.Length).Append(']').Append(':').Append(value).Append(';');

    private static string Digest(string normalized) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

    private static ShellCommandParseResult Failure(string error) => new()
    {
        Parsed = false,
        Segments = [],
        Normalized = string.Empty,
        Digest = string.Empty,
        Error = error,
        UnknownSyntax = true,
    };
}
