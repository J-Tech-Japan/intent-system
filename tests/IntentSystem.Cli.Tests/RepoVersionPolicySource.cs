using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G557: the single source tests derive version expectations from.
///
/// Before this, three tests hardcoded the `stableVersion` / `nextVersion` pair
/// as literals. The first live execution of the G554 post-release roll
/// (commit 00936844, nextVersion 0.6.1 → 0.6.2) turned child main red on all
/// three at once, and an unrelated PR inherited the red main and was frozen.
/// A literal is the wrong thing to assert here: the roll is a REQUIRED,
/// recurring step, so any test that pins the pair by value is a test that a
/// correct roll is guaranteed to break.
///
/// What actually needs guarding is the INVARIANT — the policy parses, and the
/// release-to-be-cut is strictly ahead of the published stable — which holds
/// across every roll. This helper reads <c>eng/version.json</c> and asserts
/// that property, so the expectations move with the file rather than against
/// it.
/// </summary>
internal static class RepoVersionPolicySource
{
    /// <summary>Reads the repository's own <c>eng/version.json</c>.</summary>
    public static VersionPolicy Read() => ReadFrom(RepoRoot());

    /// <summary>
    /// Reads a policy from an arbitrary root — used by the roll-simulation
    /// fixture, which writes a bumped <c>eng/version.json</c> and proves the
    /// derived assertions stay green against it.
    /// </summary>
    public static VersionPolicy ReadFrom(string root)
    {
        var policy = VersionPolicy.TryReadFromRepo(root);
        Assert.True(policy is not null, $"eng/version.json under '{root}' must be present and parseable.");
        return policy!;
    }

    /// <summary>
    /// The property every release-prep depends on: <c>nextVersion</c> (the
    /// release being cut) is strictly ahead of <c>stableVersion</c> (the last
    /// published line), so the tag cannot collide with the current stable.
    /// Version-agnostic by construction — a roll advances both fields and the
    /// assertion keeps holding.
    /// </summary>
    public static void AssertReleaseToBeCutIsAheadOfPublishedStable(VersionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var stable = ParseSemver(policy.StableVersion);
        var next = ParseSemver(policy.NextVersion);

        Assert.True(
            Compare(next, stable) > 0,
            $"eng/version.json nextVersion ({policy.NextVersion}) must be strictly ahead of stableVersion "
            + $"({policy.StableVersion}) to be release-ready.");
    }

    public static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "eng", "version.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate the repository root from {AppContext.BaseDirectory}.");
    }

    private static (int Major, int Minor, int Patch) ParseSemver(string version)
    {
        var core = (version ?? string.Empty).Split('-', 2)[0];
        var parts = core.Split('.');
        Assert.True(parts.Length >= 3, $"version '{version}' is not semver-shaped");
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major)
        {
            return a.Major.CompareTo(b.Major);
        }

        if (a.Minor != b.Minor)
        {
            return a.Minor.CompareTo(b.Minor);
        }

        return a.Patch.CompareTo(b.Patch);
    }
}
