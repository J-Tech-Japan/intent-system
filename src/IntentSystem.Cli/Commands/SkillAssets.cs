using System.Reflection;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G559: the embedded skill assets and the per-platform install targets.
///
/// The skill ships as ONE source — <c>skills/&lt;name&gt;/SKILL.md</c> in the
/// repository, embedded into the tool package at build — because the field
/// evidence is that hand-copied skills drift: the operator's own
/// <c>host-review-loop</c> skill exists as separate copies under
/// <c>~/.claude/skills</c> and <c>~/.codex/skills</c> and the copies have
/// already diverged. One embedded source plus an installer that detects drift
/// is the shape that keeps every platform reading the same file.
/// </summary>
internal static class SkillAssets
{
    /// <summary>The single embedded skill. A list rather than a constant so a second skill needs no structural change.</summary>
    public static IReadOnlyList<EmbeddedSkill> All { get; } =
    [
        new EmbeddedSkill(
            Name: "intent-cli",
            Summary: "Dispatcher skill: routes an agent to the canonical `intent-cli guide ...` surfaces."),
    ];

    public static EmbeddedSkill? Find(string name) =>
        All.FirstOrDefault(skill => string.Equals(skill.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Reads the embedded SKILL.md body. Throws
    /// <see cref="InvalidOperationException"/> when the resource is missing —
    /// a packaging defect the caller must surface rather than paper over with
    /// an empty install.
    ///
    /// The resource name is resolved by matching the skill's directory segment
    /// rather than by a hardcoded string: MSBuild's <c>%(RecursiveDir)</c>
    /// keeps a path separator in the logical name, and which separator survives
    /// depends on the build host. Matching on the parts we control keeps the
    /// csproj a single glob — a second skill needs no build change.
    /// </summary>
    public static string ReadBody(EmbeddedSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        var assembly = typeof(SkillAssets).Assembly;
        var resourceName = ResolveResourceName(assembly, skill.Name)
            ?? throw new InvalidOperationException(
                $"embedded skill resource for '{skill.Name}' is missing from {assembly.GetName().Name}; "
                + "the package was built without its skill assets. "
                + $"Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ResolveResourceName(Assembly assembly, string skillName) =>
        assembly.GetManifestResourceNames().FirstOrDefault(name =>
            name.EndsWith("SKILL.md", StringComparison.Ordinal)
            && name.Contains($".skills.{skillName}", StringComparison.Ordinal));
}

internal sealed record EmbeddedSkill(string Name, string Summary);

/// <summary>
/// G559: where each platform reads skills from. Only the LOCATION differs —
/// all three accept the same SKILL.md, which is why one embedded source can
/// serve them.
/// </summary>
internal static class SkillTargets
{
    public const string Claude = "claude";
    public const string Codex = "codex";
    public const string Copilot = "copilot";
    public const string All = "all";

    public const string ScopeUser = "user";
    public const string ScopeRepo = "repo";

    public static IReadOnlyList<string> Installable { get; } = [Claude, Codex, Copilot];

    /// <summary>
    /// The scopes each platform actually defines. Codex reads a user-level
    /// skills directory only, so a repo-scoped request for it is rejected
    /// rather than silently written somewhere the platform will never look.
    /// </summary>
    public static IReadOnlyList<string> SupportedScopes(string target) => target switch
    {
        Claude => [ScopeRepo, ScopeUser],
        Codex => [ScopeUser],
        Copilot => [ScopeRepo],
        _ => [],
    };

    /// <summary>The scope used when the caller does not name one.</summary>
    public static string DefaultScope(string target) => target switch
    {
        Claude => ScopeRepo,
        Codex => ScopeUser,
        Copilot => ScopeRepo,
        _ => ScopeRepo,
    };

    public static bool IsKnownTarget(string target) =>
        Installable.Contains(target, StringComparer.Ordinal);

    /// <summary>
    /// Resolves the SKILL.md path for a target/scope. <paramref name="repoRoot"/>
    /// anchors repo scope; <paramref name="userHome"/> anchors user scope (a
    /// parameter rather than an ambient lookup so tests install into a
    /// temporary home instead of the developer's real one).
    /// </summary>
    public static string ResolveSkillFilePath(string target, string scope, string skillName, string repoRoot, string userHome)
    {
        var directory = ResolveSkillDirectory(target, scope, skillName, repoRoot, userHome);
        return Path.Combine(directory, "SKILL.md");
    }

    public static string ResolveSkillDirectory(string target, string scope, string skillName, string repoRoot, string userHome) =>
        (target, scope) switch
        {
            (Claude, ScopeRepo) => Path.Combine(repoRoot, ".claude", "skills", skillName),
            (Claude, ScopeUser) => Path.Combine(userHome, ".claude", "skills", skillName),
            (Codex, ScopeUser) => Path.Combine(userHome, ".codex", "skills", skillName),
            (Copilot, ScopeRepo) => Path.Combine(repoRoot, ".github", "skills", skillName),
            _ => throw new InvalidOperationException($"unsupported target/scope combination: {target}/{scope}"),
        };

    /// <summary>
    /// Validates a target/scope pair, producing the operator-facing error the
    /// contract requires for an unsupported combination.
    /// </summary>
    public static bool TryValidate(string target, string? scope, out string resolvedScope, out string error)
    {
        resolvedScope = string.Empty;
        error = string.Empty;

        if (!IsKnownTarget(target))
        {
            error = $"unknown target '{target}'. Supported: {string.Join(", ", Installable)}, or 'all'.";
            return false;
        }

        var supported = SupportedScopes(target);
        if (string.IsNullOrWhiteSpace(scope))
        {
            resolvedScope = DefaultScope(target);
            return true;
        }

        if (!supported.Contains(scope, StringComparer.Ordinal))
        {
            error =
                $"target '{target}' does not support scope '{scope}'. "
                + $"Supported scope(s) for '{target}': {string.Join(", ", supported)}. "
                + "The scope is the platform's own skill location, not a preference — writing elsewhere "
                + "would put the file where that platform never looks.";
            return false;
        }

        resolvedScope = scope;
        return true;
    }

    /// <summary>
    /// G559: the user home used for user-scoped installs. A settable seam so
    /// tests never write into the developer's real home directory.
    /// </summary>
    public static Func<string>? UserHomeFactory { get; set; }

    public static string ResolveUserHome() =>
        UserHomeFactory?.Invoke()
        ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
