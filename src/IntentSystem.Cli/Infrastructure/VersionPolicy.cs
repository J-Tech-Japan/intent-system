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
