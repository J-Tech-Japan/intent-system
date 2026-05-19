using System.Globalization;
using System.Reflection;

namespace IntentSystem.Cli.Infrastructure;

/// <summary>
/// G368: process-level runtime gate that fails-closed when the running
/// binary is a CI-packed private-preview artifact whose embedded
/// <c>expires_at</c> has passed. The gate has three properties that
/// keep it compatible with the rest of the entry-point contract:
///
///   1. Source builds (no <c>AssemblyMetadata("PrivatePreviewChannel")</c>)
///      always pass through. This preserves the G367 acceptance that
///      contributor builds carry no expiry contract.
///   2. The check reads only the running assembly's
///      <see cref="AssemblyMetadataAttribute"/> entries via
///      <see cref="PrivatePreviewMetadata.Read"/>. It never touches
///      <c>.intent-cli/</c>, queue-state, GitHub, or the filesystem
///      beyond the assembly's own image, so the gate works from any
///      cwd (home directory, fresh container, child implementation
///      repo) just like G360 <c>--version</c>.
///   3. <c>--version</c> / <c>-v</c> / <c>version</c> must still
///      succeed even on an expired build so operators have a stable
///      diagnosis surface; <see cref="Program"/> calls
///      <see cref="VersionCommand"/> before this gate runs.
///
/// On a passed check the gate emits no output and returns
/// <see cref="PrivatePreviewExpiryDecision.Ok"/>. On a failed check
/// the gate writes a structured operator message to the supplied
/// <see cref="TextWriter"/> and returns
/// <see cref="PrivatePreviewExpiryDecision.Expired"/>. The caller maps
/// <c>Expired</c> to a non-zero process exit code.
/// </summary>
internal static class PrivatePreviewExpiryGate
{
    /// <summary>
    /// Process exit code used by <see cref="Program"/> when the gate
    /// fails closed. Kept distinct from the "1" exit returned by
    /// command-level error lanes so operators can filter expired-
    /// preview exits without inspecting the message text.
    /// </summary>
    public const int ExpiredExitCode = 78;

    /// <summary>
    /// Test seam: override the assembly the gate reads metadata from.
    /// When set to <c>null</c> the gate inspects the running
    /// <c>IntentSystem.Cli</c> assembly.
    /// </summary>
    public static Assembly? OverrideAssembly { get; set; }

    /// <summary>
    /// Test seam: bypass the assembly-attribute read entirely and
    /// supply preview metadata directly. Returning non-null here wins
    /// over <see cref="OverrideAssembly"/>.
    /// </summary>
    public static PrivatePreviewMetadata? OverrideMetadata { get; set; }

    /// <summary>
    /// Test seam: pin the wall-clock "now" used for expiry
    /// comparisons. Production callers leave this <c>null</c> and the
    /// gate reads <see cref="DateTimeOffset.UtcNow"/>.
    /// </summary>
    public static DateTimeOffset? OverrideNow { get; set; }

    /// <summary>
    /// Inspect the running binary's embedded preview metadata against
    /// the current wall clock. See class docs for the three
    /// pass/fail contracts. The caller's <paramref name="writer"/>
    /// receives the structured operator message only on
    /// <see cref="PrivatePreviewExpiryDecision.Expired"/>.
    /// </summary>
    public static PrivatePreviewExpiryDecision Check(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var metadata = OverrideMetadata
            ?? PrivatePreviewMetadata.Read(OverrideAssembly ?? typeof(PrivatePreviewExpiryGate).Assembly);
        if (metadata is null)
        {
            // Source build: no preview contract, no expiry.
            return PrivatePreviewExpiryDecision.Ok;
        }

        var now = OverrideNow ?? DateTimeOffset.UtcNow;
        if (!metadata.IsExpired(now))
        {
            return PrivatePreviewExpiryDecision.Ok;
        }

        EmitExpiredMessage(writer, metadata, now);
        return PrivatePreviewExpiryDecision.Expired;
    }

    /// <summary>
    /// Pure-text formatter for the expired-preview operator message.
    /// Exposed so tests can pin the exact wording without going
    /// through <see cref="Check"/>.
    /// </summary>
    public static string BuildExpiredMessage(PrivatePreviewMetadata metadata, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var builtAtIso = metadata.BuildTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var expiresAtIso = metadata.ExpiresAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var nowIso = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        return string.Join(Environment.NewLine, new[]
        {
            $"intent-cli: private-preview artifact expired on {expiresAtIso} (now {nowIso}).",
            $"  channel: {metadata.Channel}",
            $"  built: {builtAtIso}",
            $"  expired_at: {expiresAtIso}",
            string.IsNullOrEmpty(metadata.SourceCommit)
                ? "  commit: <unknown>"
                : $"  commit: {metadata.SourceCommit}",
            string.Empty,
            "Download a newer artifact from a more recent `private-preview-pack` workflow run on `main` and reinstall:",
            "  dotnet tool update --global --add-source <downloaded-folder> --version <newer-version> intent-cli",
            string.Empty,
            "Source-only builds (no preview metadata) are unaffected; see README.md \"Private-preview install\" for the full flow.",
        });
    }

    private static void EmitExpiredMessage(TextWriter writer, PrivatePreviewMetadata metadata, DateTimeOffset now)
    {
        writer.WriteLine(BuildExpiredMessage(metadata, now));
    }
}

/// <summary>
/// G368: outcome of <see cref="PrivatePreviewExpiryGate.Check"/>.
/// Distinct from <see cref="bool"/> so future variants
/// (e.g. <c>WarnOnly</c> for soft-expiry warnings) can extend the
/// surface without changing call-site shape.
/// </summary>
internal enum PrivatePreviewExpiryDecision
{
    /// <summary>Source build, no preview metadata, OR an unexpired preview build.</summary>
    Ok,
    /// <summary>Preview build past its embedded <c>expires_at</c>; caller must fail closed.</summary>
    Expired,
}
