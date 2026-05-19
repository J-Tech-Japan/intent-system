using System.Reflection;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G360: process-level <c>--version</c> / <c>-v</c> handler that
/// emits the installed binary's identifier without touching host
/// state. Reads the assembly's
/// <see cref="AssemblyInformationalVersionAttribute"/> (baked at
/// pack/build time by IntentSystem.Cli.csproj). The full shape is
/// <c>intent-cli &lt;package-version&gt;[-&lt;short-sha&gt;]-&lt;latest-execution-unit&gt;</c>,
/// e.g. <c>intent-cli 0.2.0-f6cbf65-G357</c>. When the build did not
/// embed a git short SHA the SHA segment is omitted, but the latest
/// execution-unit segment is always present.
///
/// CRITICAL: this command must NEVER read <c>.intent-cli/</c>,
/// queue-state, packet directories, GitHub, or anything else that
/// makes <c>intent-cli --version</c> dependent on the current
/// working directory. Operators rely on it to verify the installed
/// binary from any cwd (home directory, child implementation repo,
/// fresh CI machine).
/// </summary>
internal static class VersionCommand
{
    /// <summary>Test seam: override the version string for tests.</summary>
    public static string? OverrideVersionString { get; set; }

    /// <summary>
    /// G367 test seam: override the resolved
    /// <see cref="PrivatePreviewMetadata"/> for tests so they can
    /// assert the version output contains (or omits) the optional
    /// private-preview trailer without rebuilding the assembly.
    /// Setting this to <c>null</c> falls back to reading the running
    /// assembly's <see cref="AssemblyMetadataAttribute"/> entries.
    /// </summary>
    public static PrivatePreviewMetadata? OverridePrivatePreviewMetadata { get; set; }

    /// <summary>
    /// G367 test seam: when <c>true</c>, <see cref="Execute"/> skips
    /// reading <see cref="PrivatePreviewMetadata.Read"/> against the
    /// real assembly so existing tests that pinned the single-line
    /// output stay deterministic regardless of whether the build
    /// embedded preview metadata.
    /// </summary>
    public static bool SuppressPrivatePreviewTrailer { get; set; }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="args"/> request the
    /// version output (the recognized shapes are <c>--version</c>,
    /// <c>-v</c>, or <c>version</c> as the first or only token).
    /// </summary>
    public static bool IsVersionRequest(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            return false;
        }
        var first = args[0];
        return string.Equals(first, "--version", StringComparison.Ordinal)
            || string.Equals(first, "-v", StringComparison.Ordinal)
            || string.Equals(first, "version", StringComparison.Ordinal);
    }

    public static int Execute(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var versionString = OverrideVersionString
            ?? BuildVersionString(typeof(VersionCommand).Assembly);
        writer.WriteLine(versionString);

        // G367: when the CI workflow built this assembly with the
        // private-preview metadata properties, append a single
        // additional line so operators can tell a CI preview artifact
        // from a source build without scraping the package itself.
        // Local builds (no AssemblyMetadata("PrivatePreviewChannel"))
        // are silent here, matching the issue acceptance that source
        // builds carry no expiry contract. The
        // <see cref="SuppressPrivatePreviewTrailer"/> seam keeps
        // existing single-line `--version` tests deterministic across
        // CI/source environments.
        if (OverrideVersionString is null && !SuppressPrivatePreviewTrailer)
        {
            var preview = OverridePrivatePreviewMetadata
                ?? PrivatePreviewMetadata.Read(typeof(VersionCommand).Assembly);
            if (preview is not null)
            {
                writer.WriteLine(preview.ToVersionTrailer());
            }
        }

        return 0;
    }

    /// <summary>
    /// Test seam exposing the pure builder so tests can verify the
    /// shape independently of the executing assembly's actual
    /// InformationalVersion.
    /// </summary>
    internal static string BuildVersionString(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            // Defensive: the .csproj always sets InformationalVersion,
            // so this fallback only fires for an assembly the build
            // contract did not produce.
            informational = assembly.GetName().Version?.ToString()
                ?? "0.0.0";
        }
        else
        {
            // Strip the `+<commit-sha>` SourceLink suffix the SDK appends
            // by default (e.g. "0.2.0-G360+abcdef..."). The canonical
            // G360 form embeds the short SHA in the dash-segment via the
            // build target; the `+` suffix is duplicate noise.
            var plusIndex = informational.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex >= 0)
            {
                informational = informational[..plusIndex];
            }
        }
        return $"intent-cli {informational}";
    }
}
