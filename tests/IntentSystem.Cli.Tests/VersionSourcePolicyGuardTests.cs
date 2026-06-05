using System.Text.Json;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G468: guard tests that keep the package/display version derived from the
/// repository version policy (`eng/version.json`) instead of a stale
/// hard-coded csproj literal. These fail if `IntentSystem.Cli.csproj`
/// reintroduces a `&lt;Version&gt;x.y.z&lt;/Version&gt;` literal that bypasses
/// the policy, or if the policy file drifts from the csproj derivation.
/// </summary>
public sealed class VersionSourcePolicyGuardTests
{
    [Fact]
    public void Csproj_DoesNotHardcodeAVersionLiteral_AndDerivesFromPolicy()
    {
        var raw = File.ReadAllText(CsprojPath());

        // Strip XML comments first so documentation that *mentions* the old
        // literal (e.g. explaining the fix) is not mistaken for a real
        // hard-coded element.
        var csproj = System.Text.RegularExpressions.Regex.Replace(
            raw, "(?s)<!--.*?-->", string.Empty);

        // AC: a stale hard-coded csproj Version must not recur. A literal
        // semver `<Version>` element bypasses the policy and is exactly the
        // bug this slice fixes.
        var hardCodedVersion = System.Text.RegularExpressions.Regex.Match(
            csproj, @"<Version>\s*\d+\.\d+\.\d+[^<]*</Version>");
        Assert.False(
            hardCodedVersion.Success,
            $"IntentSystem.Cli.csproj contains a hard-coded <Version> literal ('{hardCodedVersion.Value}') that bypasses eng/version.json. Derive the version from the version policy instead.");

        // AC: the version must be derived from eng/version.json (nextVersion).
        Assert.Contains("eng/version.json", csproj, StringComparison.Ordinal);
        Assert.Contains("IntentSystemNextVersionFromPolicy", csproj, StringComparison.Ordinal);
        Assert.Contains("nextVersion", csproj, StringComparison.Ordinal);
        // The explicit -p:Version override path (release/preview) is preserved:
        // the derived default only applies when Version is empty.
        Assert.Contains("'$(Version)' == ''", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicyFile_IsCoherent_AndReadableByVersionPolicy()
    {
        var policyPath = Path.Combine(FindRepoRoot(), "eng", "version.json");
        Assert.True(File.Exists(policyPath), $"eng/version.json missing at {policyPath}");

        // The runtime VersionPolicy reader and the csproj derivation must
        // agree on the same source of truth.
        var policy = VersionPolicy.TryReadFromFile(policyPath);
        Assert.NotNull(policy);

        var stable = ParseVersion(policy!.StableVersion);
        var next = ParseVersion(policy.NextVersion);

        // Policy coherence: next is strictly ahead of stable so a local dev
        // build never collides with the latest released stable line.
        Assert.True(
            Compare(next, stable) > 0,
            $"eng/version.json nextVersion ({policy.NextVersion}) must be ahead of stableVersion ({policy.StableVersion}).");
    }

    [Fact]
    public void Csproj_NextVersionRegex_ExtractsTheSameValueAsVersionPolicy()
    {
        // Mirror the MSBuild derivation regex against the live file so a
        // change to the JSON shape that would silently break the csproj
        // derivation is caught by a test.
        var policyPath = Path.Combine(FindRepoRoot(), "eng", "version.json");
        var json = File.ReadAllText(policyPath);

        var match = System.Text.RegularExpressions.Regex.Match(json, "\"nextVersion\"\\s*:\\s*\"([^\"]+)\"");
        Assert.True(match.Success, "csproj-style nextVersion regex failed to match eng/version.json");

        var policy = VersionPolicy.TryReadFromFile(policyPath);
        Assert.NotNull(policy);
        Assert.Equal(policy!.NextVersion, match.Groups[1].Value);
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        // Tolerate prerelease suffixes (e.g. "0.3.6-preview.1.2").
        var core = version.Split('-', 2)[0];
        var parts = core.Split('.');
        Assert.True(parts.Length >= 3, $"version '{version}' is not semver-shaped");
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        return a.Patch.CompareTo(b.Patch);
    }

    private static string CsprojPath() =>
        Path.Combine(FindRepoRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}
