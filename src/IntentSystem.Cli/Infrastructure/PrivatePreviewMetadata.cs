using System.Globalization;
using System.Reflection;

namespace IntentSystem.Cli.Infrastructure;

/// <summary>
/// G367: machine-readable private-preview metadata baked into a
/// CI-built <c>intent-cli</c> package. The CI workflow that runs on
/// every merge to <c>main</c> passes MSBuild properties
/// (<c>PrivatePreviewChannel</c>, <c>PrivatePreviewBuildTimestamp</c>,
/// <c>PrivatePreviewExpiresAt</c>, <c>PrivatePreviewSourceCommit</c>)
/// which the csproj copies into <see cref="AssemblyMetadataAttribute"/>
/// entries. Local source builds that do not set
/// <c>PrivatePreviewChannel</c> leave the assembly free of these
/// attributes, so <see cref="Read"/> returns <c>null</c> and the
/// binary has no expiry restriction — matching the issue acceptance
/// "Local source build without CI preview properties has no expiry
/// restriction."
///
/// Why a separate record: keeping this small, allocation-light, and
/// dependency-free lets <c>--version</c> append a single non-host
/// line without touching <c>.intent-cli/</c>, GitHub, or queue
/// state. The expiry calculation lives in
/// <see cref="ComputeExpiresAt"/> so unit tests can exercise the
/// 14-day window without spinning up a real GitHub Actions run.
/// </summary>
internal sealed record PrivatePreviewMetadata
{
    /// <summary>
    /// Standard private-preview window. Embedded preview artifacts
    /// expire 14 days after their CI build timestamp, after which
    /// the operator must download a fresh artifact from a later
    /// main-merge run.
    /// </summary>
    public static readonly TimeSpan PreviewWindow = TimeSpan.FromDays(14);

    public const string ChannelAttributeName = "PrivatePreviewChannel";
    public const string BuildTimestampAttributeName = "PrivatePreviewBuildTimestamp";
    public const string ExpiresAtAttributeName = "PrivatePreviewExpiresAt";
    public const string SourceCommitAttributeName = "PrivatePreviewSourceCommit";

    public const string ChannelPrivatePreview = "private-preview";

    public required string Channel { get; init; }
    public required DateTimeOffset BuildTimestamp { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string SourceCommit { get; init; }

    /// <summary>
    /// Pure helper: returns <paramref name="buildTimestamp"/> +
    /// <see cref="PreviewWindow"/>. Centralising the calculation
    /// keeps the workflow and the test surface honest: a unit test
    /// can assert the 14-day window without invoking
    /// <c>dotnet pack</c> or a GitHub Actions runner.
    /// </summary>
    public static DateTimeOffset ComputeExpiresAt(DateTimeOffset buildTimestamp)
    {
        return buildTimestamp + PreviewWindow;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="now"/> falls strictly
    /// after the recorded <see cref="ExpiresAt"/>. The boundary
    /// minute itself is still considered "in-window" so a clock skew
    /// of a few seconds does not flip an artifact to expired right
    /// at the cutoff.
    /// </summary>
    public bool IsExpired(DateTimeOffset now)
    {
        return now > ExpiresAt;
    }

    /// <summary>
    /// Build a metadata record from a dictionary of attribute values
    /// (typically pulled from
    /// <see cref="AssemblyMetadataAttribute"/> entries on the
    /// running assembly). Returns <c>null</c> when the required
    /// channel marker is absent, missing, or blank — that case
    /// signals "this is a plain source build, no expiry contract."
    /// </summary>
    public static PrivatePreviewMetadata? TryParse(IReadOnlyDictionary<string, string?> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(ChannelAttributeName, out var channel)
            || string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        attributes.TryGetValue(BuildTimestampAttributeName, out var builtAtRaw);
        attributes.TryGetValue(ExpiresAtAttributeName, out var expiresAtRaw);
        attributes.TryGetValue(SourceCommitAttributeName, out var sourceCommit);

        if (!TryParseTimestamp(builtAtRaw, out var builtAt))
        {
            return null;
        }
        if (!TryParseTimestamp(expiresAtRaw, out var expiresAt))
        {
            // Defensive: if CI forgot to pass an explicit expiry,
            // recompute it from the build timestamp so callers can
            // still tell when this artifact ages out.
            expiresAt = ComputeExpiresAt(builtAt);
        }

        return new PrivatePreviewMetadata
        {
            Channel = channel.Trim(),
            BuildTimestamp = builtAt,
            ExpiresAt = expiresAt,
            SourceCommit = string.IsNullOrWhiteSpace(sourceCommit) ? string.Empty : sourceCommit.Trim(),
        };
    }

    /// <summary>
    /// Read CI-baked metadata from the supplied assembly's
    /// <see cref="AssemblyMetadataAttribute"/> entries. Returns
    /// <c>null</c> for ordinary source builds.
    /// </summary>
    public static PrivatePreviewMetadata? Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var attrs = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => (string?)a.Value, StringComparer.Ordinal);
        return TryParse(attrs);
    }

    /// <summary>
    /// Format the metadata as a single-line, key=value summary
    /// suitable for appending to <c>intent-cli --version</c> output.
    /// Operators can grep this line to distinguish a CI private-
    /// preview artifact from a source build:
    /// <c>channel=private-preview built=2026-05-19T12:34:56Z expires=2026-06-02T12:34:56Z commit=f6cbf65</c>.
    /// </summary>
    public string ToVersionTrailer()
    {
        var builtAtIso = BuildTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var expiresAtIso = ExpiresAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var commitSegment = string.IsNullOrEmpty(SourceCommit) ? string.Empty : $" commit={SourceCommit}";
        return $"channel={Channel} built={builtAtIso} expires={expiresAtIso}{commitSegment}";
    }

    private static bool TryParseTimestamp(string? raw, out DateTimeOffset parsed)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            parsed = default;
            return false;
        }
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
    }
}
