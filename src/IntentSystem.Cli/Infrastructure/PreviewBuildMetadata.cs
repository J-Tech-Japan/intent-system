using System.Globalization;
using System.Reflection;

namespace IntentSystem.Cli.Infrastructure;

/// <summary>
/// G401: machine-readable OSS preview build metadata baked into a
/// CI-built <c>intent-cli</c> package. Unlike the earlier private-preview
/// scheme (<see cref="PrivatePreviewMetadata"/> / G367), this carries
/// <b>no expiry contract</b> and uses <c>"preview"</c> as the channel
/// name rather than <c>"private-preview"</c>. Main-branch CI builds set
/// three MSBuild properties:
/// <list type="bullet">
///   <item><c>PreviewChannel=preview</c></item>
///   <item><c>PreviewBuildTimestamp=ISO-8601 UTC build timestamp</c></item>
///   <item><c>PreviewSourceCommit=full git SHA</c></item>
/// </list>
/// which the csproj copies into <see cref="AssemblyMetadataAttribute"/>
/// entries. A local source build that does not set <c>PreviewChannel</c>
/// leaves the assembly free of these attributes, so <see cref="Read"/>
/// returns <c>null</c> and the binary has no preview marker — matching
/// the acceptance that source builds remain unrestricted.
///
/// The <c>--version</c> output for a CI preview build is:
/// <code>
/// intent-cli 0.3.0-preview.42.1-abc1234-G401
/// channel=preview built=2026-05-24T10:00:00Z commit=abc1234
/// </code>
/// </summary>
internal sealed record PreviewBuildMetadata
{
    public const string ChannelAttributeName = "PreviewChannel";
    public const string BuildTimestampAttributeName = "PreviewBuildTimestamp";
    public const string SourceCommitAttributeName = "PreviewSourceCommit";

    public const string ChannelPreview = "preview";

    public required string Channel { get; init; }
    public required DateTimeOffset BuildTimestamp { get; init; }
    public required string SourceCommit { get; init; }

    /// <summary>
    /// Format the metadata as a single-line, key=value summary
    /// suitable for appending to <c>intent-cli --version</c> output.
    /// Operators can distinguish a CI preview artifact from a source build:
    /// <c>channel=preview built=2026-05-24T10:00:00Z commit=abc1234</c>.
    /// </summary>
    public string ToVersionTrailer()
    {
        var builtAtIso = BuildTimestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var commitSegment = string.IsNullOrEmpty(SourceCommit) ? string.Empty : $" commit={SourceCommit}";
        return $"channel={Channel} built={builtAtIso}{commitSegment}";
    }

    /// <summary>
    /// Build a metadata record from a dictionary of attribute values
    /// (typically pulled from <see cref="AssemblyMetadataAttribute"/> entries
    /// on the running assembly). Returns <c>null</c> when the required channel
    /// marker is absent, missing, or blank — signalling a plain source build.
    /// </summary>
    public static PreviewBuildMetadata? TryParse(IReadOnlyDictionary<string, string?> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(ChannelAttributeName, out var channel)
            || string.IsNullOrWhiteSpace(channel))
        {
            return null;
        }

        attributes.TryGetValue(BuildTimestampAttributeName, out var builtAtRaw);
        attributes.TryGetValue(SourceCommitAttributeName, out var sourceCommit);

        if (!TryParseTimestamp(builtAtRaw, out var builtAt))
        {
            return null;
        }

        return new PreviewBuildMetadata
        {
            Channel = channel.Trim(),
            BuildTimestamp = builtAt,
            SourceCommit = string.IsNullOrWhiteSpace(sourceCommit) ? string.Empty : sourceCommit.Trim(),
        };
    }

    /// <summary>
    /// Read CI-baked metadata from the supplied assembly's
    /// <see cref="AssemblyMetadataAttribute"/> entries. Returns
    /// <c>null</c> for ordinary source builds.
    /// </summary>
    public static PreviewBuildMetadata? Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var attrs = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => (string?)a.Value, StringComparer.Ordinal);
        return TryParse(attrs);
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
