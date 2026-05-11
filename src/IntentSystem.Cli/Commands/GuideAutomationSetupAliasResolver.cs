using System.Collections.Generic;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G321: pure normalizer that turns the natural-language operator
/// vocabulary (Japanese + English, mixed-case, with/without hyphens) into
/// the canonical automation setup vocabulary the rest of the CLI uses
/// (<c>child-implement</c>, <c>host-review-next-slice</c>, <c>claude</c>,
/// <c>codex</c>, <c>unknown</c>, <c>host</c>, <c>child</c>). Keeps the
/// alias tables out of the command parser so they are easy to test and
/// extend without touching argument-handling code.
/// </summary>
internal static class GuideAutomationSetupAliasResolver
{
    public const string CanonicalPurposeChildImplement = "child-implement";
    public const string CanonicalPurposeHostReviewNextSlice = "host-review-next-slice";

    public const string CanonicalAgentClaude = "claude";
    public const string CanonicalAgentCodex = "codex";
    public const string CanonicalAgentUnknown = "unknown";

    public const string CanonicalCwdRoleHost = "host";
    public const string CanonicalCwdRoleChild = "child";

    /// <summary>
    /// G321: maps an operator-supplied purpose phrase to the canonical
    /// kind (<c>child-implement</c> / <c>host-review-next-slice</c>).
    /// Returns <c>null</c> when the phrase does not match any known
    /// alias so the caller can emit a usage error instead of guessing.
    /// </summary>
    public static string? ResolvePurpose(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var key = NormalizeKey(raw);
        return PurposeAliases.TryGetValue(key, out var canonical) ? canonical : null;
    }

    /// <summary>
    /// G321: maps an operator-supplied agent phrase to one of
    /// <c>claude</c>, <c>codex</c>, or <c>unknown</c>. Phrases that do
    /// not resolve to a known agent stay as <see cref="CanonicalAgentUnknown"/>
    /// so the scheduling-mechanism resolver MUST surface the operator ask
    /// instead of guessing.
    /// </summary>
    public static string ResolveAgent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return CanonicalAgentUnknown;
        }

        var key = NormalizeKey(raw);
        return AgentAliases.TryGetValue(key, out var canonical) ? canonical : CanonicalAgentUnknown;
    }

    /// <summary>
    /// G321: cwd-role inference.
    /// <para>
    /// When the operator did not pass <c>--cwd-role</c>, infer the role
    /// deterministically from the canonical purpose: host review/next-slice
    /// implies a host root cwd; child implement/update implies a child
    /// worktree cwd (with a parent host root reference supplied
    /// separately).
    /// </para>
    /// <para>
    /// When the operator did pass an explicit role, the caller is
    /// responsible for detecting + surfacing the conflict — this method
    /// always returns the inferred role and never silently overrides an
    /// explicit value.
    /// </para>
    /// </summary>
    public static string InferCwdRole(string canonicalPurpose)
    {
        return canonicalPurpose switch
        {
            CanonicalPurposeHostReviewNextSlice => CanonicalCwdRoleHost,
            _ => CanonicalCwdRoleChild
        };
    }

    /// <summary>
    /// G321: normalize an operator-supplied cwd-role value to the
    /// canonical vocabulary. Returns <c>null</c> for values that do not
    /// match so the caller can emit a usage error.
    /// </summary>
    public static string? ResolveCwdRole(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var key = NormalizeKey(raw);
        return CwdRoleAliases.TryGetValue(key, out var canonical) ? canonical : null;
    }

    private static string NormalizeKey(string raw)
    {
        // G321: lowercase + trim + collapse any non-alphanumeric punctuation
        // (space, tab, hyphen, underscore, slash, ampersand, etc.) into a
        // single hyphen so "Claude Code", "claude-code", "claude  code",
        // and "review & next slice" all hash to the same alias key. CJK
        // characters (実, 装, レ, etc.) pass through because
        // <c>char.IsLetterOrDigit</c> recognizes them as letters.
        var trimmed = raw.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(trimmed.Length);
        var lastWasSeparator = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSeparator = false;
            }
            else
            {
                if (!lastWasSeparator && builder.Length > 0)
                {
                    builder.Append('-');
                }
                lastWasSeparator = true;
            }
        }
        if (builder.Length > 0 && builder[builder.Length - 1] == '-')
        {
            builder.Length--;
        }
        return builder.ToString();
    }

    // ---------------------------------------------------------------
    // Alias tables. Keys are pre-normalized via NormalizeKey().
    // ---------------------------------------------------------------

    private static readonly Dictionary<string, string> PurposeAliases = new(StringComparer.Ordinal)
    {
        // Canonical names (always accepted).
        ["child-implement"] = CanonicalPurposeChildImplement,
        ["host-review-next-slice"] = CanonicalPurposeHostReviewNextSlice,

        // English child-implementation / PR-comment-update aliases.
        ["child"] = CanonicalPurposeChildImplement,
        ["child-loop"] = CanonicalPurposeChildImplement,
        ["child-implementation"] = CanonicalPurposeChildImplement,
        ["implementation"] = CanonicalPurposeChildImplement,
        ["implement"] = CanonicalPurposeChildImplement,
        ["implement-update"] = CanonicalPurposeChildImplement,
        ["pr-comment-update"] = CanonicalPurposeChildImplement,
        ["pr-comment-fix"] = CanonicalPurposeChildImplement,
        ["pr-comment"] = CanonicalPurposeChildImplement,
        ["implementation-and-pr-comment-update"] = CanonicalPurposeChildImplement,

        // English host-review / next-slice aliases.
        ["host"] = CanonicalPurposeHostReviewNextSlice,
        ["host-loop"] = CanonicalPurposeHostReviewNextSlice,
        ["host-review"] = CanonicalPurposeHostReviewNextSlice,
        ["review"] = CanonicalPurposeHostReviewNextSlice,
        ["review-and-next-slice"] = CanonicalPurposeHostReviewNextSlice,
        ["review-next-slice"] = CanonicalPurposeHostReviewNextSlice,
        ["next-slice"] = CanonicalPurposeHostReviewNextSlice,

        // Japanese child-implementation / PR-comment-update aliases.
        // NormalizeKey turns slash-separated phrases into hyphen-separated.
        ["実装"] = CanonicalPurposeChildImplement,
        ["実装更新"] = CanonicalPurposeChildImplement,
        ["prコメント更新"] = CanonicalPurposeChildImplement,
        ["実装-prコメント更新"] = CanonicalPurposeChildImplement,

        // Japanese host-review / next-slice aliases.
        ["レビュー"] = CanonicalPurposeHostReviewNextSlice,
        ["ホストレビュー"] = CanonicalPurposeHostReviewNextSlice,
        ["次スライス"] = CanonicalPurposeHostReviewNextSlice,
        ["レビュー-次スライス"] = CanonicalPurposeHostReviewNextSlice
    };

    private static readonly Dictionary<string, string> AgentAliases = new(StringComparer.Ordinal)
    {
        ["claude"] = CanonicalAgentClaude,
        ["claude-code"] = CanonicalAgentClaude,
        ["claudecode"] = CanonicalAgentClaude,
        ["codex"] = CanonicalAgentCodex,
        ["codex-cli"] = CanonicalAgentCodex,
        // Explicit unknown still resolves to canonical unknown.
        ["unknown"] = CanonicalAgentUnknown,
        ["generic"] = CanonicalAgentUnknown
    };

    private static readonly Dictionary<string, string> CwdRoleAliases = new(StringComparer.Ordinal)
    {
        ["host"] = CanonicalCwdRoleHost,
        ["host-root"] = CanonicalCwdRoleHost,
        ["parent"] = CanonicalCwdRoleHost,
        ["parent-host"] = CanonicalCwdRoleHost,
        ["parent-host-root"] = CanonicalCwdRoleHost,
        ["child"] = CanonicalCwdRoleChild,
        ["child-worktree"] = CanonicalCwdRoleChild,
        ["worktree"] = CanonicalCwdRoleChild,
        ["implementation"] = CanonicalCwdRoleChild
    };
}
