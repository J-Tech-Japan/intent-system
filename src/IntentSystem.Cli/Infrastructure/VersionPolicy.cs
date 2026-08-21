using System.Globalization;
using System.Text.Json;

namespace IntentSystem.Cli.Infrastructure;

/// <summary>
/// G401: repository version policy read from <c>eng/version.json</c>.
///
/// The file stores two fields:
/// <list type="bullet">
///   <item><c>stableVersion</c> — the most recently shipped stable version (e.g. "0.2.0").</item>
///   <item><c>nextVersion</c> — the planned next stable release (e.g. "0.3.0").</item>
/// </list>
///
/// Main-branch CI preview builds derive their package version from
/// <c>nextVersion</c>: <c>0.3.0-preview.&lt;run&gt;.&lt;attempt&gt;</c>. Official
/// release builds are tag-driven and override the version at pack time.
///
/// <b>Version flow:</b>
/// <list type="number">
///   <item>During development towards <c>v0.3.0</c>, main CI builds produce <c>0.3.0-preview.N.A</c>.</item>
///   <item>A release tag <c>v0.3.0</c> triggers the release workflow, which produces <c>0.3.0</c>.</item>
///   <item>After releasing, bump both fields: <c>stableVersion → "0.3.0"</c> and <c>nextVersion → "0.4.0"</c>.</item>
/// </list>
///
/// Optional release-candidate builds can use <c>nextVersion</c> plus an <c>-rc.N</c> suffix;
/// the workflow determines the exact suffix.
/// </summary>
internal sealed record VersionPolicy
{
    public required string StableVersion { get; init; }
    public required string NextVersion { get; init; }

    /// <summary>
    /// Derive the preview package version for a main-branch CI build.
    /// Example: <c>NextVersion="0.3.0"</c>, runNumber=42, runAttempt=1
    /// → <c>"0.3.0-preview.42.1"</c>.
    /// </summary>
    public string DerivePreviewPackageVersion(string runNumber, string runAttempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(runAttempt);
        return $"{NextVersion}-preview.{runNumber}.{runAttempt}";
    }

    /// <summary>
    /// Determines whether the policy still needs the required post-release
    /// roll for <paramref name="releasedVersion"/>. A release older than the
    /// recorded stable line is not evidence of a new closeout obligation;
    /// the latest release at or beyond that line must have both fields
    /// settled to the released version and its next patch.
    /// </summary>
    public bool TryGetRequiredPostReleaseRoll(
        string releasedVersion,
        out VersionRollExpectation expectation)
    {
        expectation = null!;
        if (!TryNormalizeStableVersion(releasedVersion, out var released)
            || !TryNormalizeStableVersion(StableVersion, out var stable)
            || !TryNormalizeStableVersion(NextVersion, out var nextVersion))
        {
            return false;
        }

        var comparison = CompareNormalizedStableVersions(released, stable);
        if (comparison < 0)
        {
            return false;
        }

        var next = IncrementPatch(released);
        if (comparison == 0 && string.Equals(nextVersion, next, StringComparison.Ordinal))
        {
            return false;
        }

        expectation = new VersionRollExpectation
        {
            ReleasedVersion = released,
            ExpectedStableVersion = released,
            ExpectedNextVersion = next,
        };
        return true;
    }

    /// <summary>
    /// Normalizes a stable release tag or policy value to a strict
    /// <c>major.minor.patch</c> form. Prerelease tags are deliberately not
    /// accepted here; the stalled-work detector only compares published
    /// stable releases.
    /// </summary>
    public static bool TryNormalizeStableVersion(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        var parts = candidate.Split('.', StringSplitOptions.None);
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch)
            || major < 0
            || minor < 0
            || patch < 0)
        {
            return false;
        }

        normalized = $"{major}.{minor}.{patch}";
        return true;
    }

    /// <summary>Compares two values already accepted by <see cref="TryNormalizeStableVersion"/>.</summary>
    public static int CompareStableVersions(string left, string right)
    {
        if (!TryNormalizeStableVersion(left, out var normalizedLeft)
            || !TryNormalizeStableVersion(right, out var normalizedRight))
        {
            throw new ArgumentException("Both stable versions must be major.minor.patch values.");
        }

        return CompareNormalizedStableVersions(normalizedLeft, normalizedRight);
    }

    private static int CompareNormalizedStableVersions(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < 3; index++)
        {
            var comparison = int.Parse(leftParts[index], CultureInfo.InvariantCulture)
                .CompareTo(int.Parse(rightParts[index], CultureInfo.InvariantCulture));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static string IncrementPatch(string normalizedVersion)
    {
        var parts = normalizedVersion.Split('.');
        var patch = checked(int.Parse(parts[2], CultureInfo.InvariantCulture) + 1);
        return $"{parts[0]}.{parts[1]}.{patch}";
    }

    /// <summary>
    /// Read and parse the policy file from <paramref name="filePath"/>.
    /// Returns <c>null</c> when the file is missing, unreadable, or malformed.
    /// </summary>
    public static VersionPolicy? TryReadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            return null;
        }
        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("stableVersion", out var stableProp)
                || !root.TryGetProperty("nextVersion", out var nextProp))
            {
                return null;
            }
            var stable = stableProp.GetString();
            var next = nextProp.GetString();
            if (string.IsNullOrWhiteSpace(stable) || string.IsNullOrWhiteSpace(next))
            {
                return null;
            }
            return new VersionPolicy { StableVersion = stable, NextVersion = next };
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Convenience overload: reads from <c>eng/version.json</c> relative to
    /// <paramref name="repoRoot"/>. Returns <c>null</c> when missing or malformed.
    /// </summary>
    public static VersionPolicy? TryReadFromRepo(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        var filePath = Path.Combine(repoRoot, "eng", "version.json");
        return TryReadFromFile(filePath);
    }
}

internal sealed record VersionRollExpectation
{
    public required string ReleasedVersion { get; init; }
    public required string ExpectedStableVersion { get; init; }
    public required string ExpectedNextVersion { get; init; }
}
