using System.Text;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G474: machine-readable packet lifecycle retirement metadata. Stored in a
/// sidecar file (<c>lifecycle.yaml</c>) inside the packet directory
/// (<c>.intent-cli/issues/&lt;execution-unit&gt;/</c>) so that absorbed /
/// superseded / retired packets remain in git history as design artifacts but
/// are excluded from <c>intent-cli intent next-slice</c> issue-cut-ready
/// candidates. The packet content files are never mutated.
/// </summary>
internal static class PacketLifecycle
{
    internal const string SidecarFileName = "lifecycle.yaml";

    internal const string LifecycleReady = "ready";
    internal const string LifecycleAbsorbed = "absorbed";
    internal const string LifecycleRetired = "retired";
    internal const string LifecycleSuperseded = "superseded";

    // Legacy human-only markers (e.g. "STATUS: ABSORBED ... Do NOT seed or
    // publish") that pre-date machine-readable retirement metadata. They are
    // useful for people but are not safe for automation; the next-slice scanner
    // surfaces them as a repair recommendation instead of publishing.
    private static readonly Regex LegacyMarkerPattern = new(
        @"(?im)^\s*STATUS\s*:\s*(ABSORBED|RETIRED|SUPERSEDED)\b",
        RegexOptions.Compiled);

    private static readonly string[] PacketContentFiles =
    {
        "packet.yaml",
        "implementation.md",
        "review-context.md",
        "github-body.md"
    };

    /// <summary>
    /// Reads the lifecycle sidecar for a packet directory. Returns
    /// <see langword="null"/> when no sidecar exists or it cannot be read.
    /// </summary>
    internal static PacketLifecycleMetadata? TryRead(string packetDirectory)
    {
        var sidecarPath = Path.Combine(packetDirectory, SidecarFileName);
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(sidecarPath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var fields = ParseFlatYaml(text);
        if (!fields.TryGetValue("lifecycle", out var lifecycle) || string.IsNullOrWhiteSpace(lifecycle))
        {
            return null;
        }

        return new PacketLifecycleMetadata
        {
            Lifecycle = lifecycle.Trim(),
            AbsorbedBy = fields.GetValueOrDefault("absorbed_by"),
            SupersededBy = fields.GetValueOrDefault("superseded_by"),
            RetiredReason = fields.GetValueOrDefault("retired_reason"),
            RetiredAt = fields.GetValueOrDefault("retired_at")
        };
    }

    /// <summary>
    /// True when the lifecycle value marks the packet as no longer a valid
    /// next-slice publication candidate.
    /// </summary>
    internal static bool IsNonPublishable(PacketLifecycleMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return metadata.Lifecycle is LifecycleAbsorbed or LifecycleRetired or LifecycleSuperseded;
    }

    /// <summary>
    /// True when the packet directory carries a machine-readable
    /// non-publishable lifecycle marker.
    /// </summary>
    internal static bool IsRetired(string packetDirectory)
    {
        var metadata = TryRead(packetDirectory);
        return metadata is not null && IsNonPublishable(metadata);
    }

    /// <summary>
    /// Detects a stale human-only retirement marker (e.g.
    /// <c>STATUS: ABSORBED</c>) in any packet content file. Used to recommend
    /// converting to machine-readable metadata instead of publishing.
    /// </summary>
    internal static bool HasLegacyHumanMarker(string packetDirectory)
    {
        foreach (var fileName in PacketContentFiles)
        {
            var path = Path.Combine(packetDirectory, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (LegacyMarkerPattern.IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }

    internal static string SidecarPath(string packetDirectory)
        => Path.Combine(packetDirectory, SidecarFileName);

    /// <summary>
    /// Renders the deterministic sidecar contents for the given metadata.
    /// Only non-null fields are emitted.
    /// </summary>
    internal static string Render(PacketLifecycleMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var builder = new StringBuilder();
        builder.Append("lifecycle: ").Append(metadata.Lifecycle).Append('\n');
        AppendOptional(builder, "absorbed_by", metadata.AbsorbedBy);
        AppendOptional(builder, "superseded_by", metadata.SupersededBy);
        AppendOptional(builder, "retired_reason", metadata.RetiredReason);
        AppendOptional(builder, "retired_at", metadata.RetiredAt);
        return builder.ToString();
    }

    private static void AppendOptional(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(key).Append(": ").Append(QuoteIfNeeded(value)).Append('\n');
    }

    private static string QuoteIfNeeded(string value)
    {
        // Quote values containing characters that would break the flat
        // key: value parser (colons, leading/trailing whitespace, #).
        var needsQuote = value.Contains(':', StringComparison.Ordinal)
            || value.Contains('#', StringComparison.Ordinal)
            || value != value.Trim();
        if (!needsQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static Dictionary<string, string> ParseFlatYaml(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            value = Unquote(value);
            result[key] = value;
        }

        return result;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return value;
    }
}

/// <summary>
/// G474: parsed packet lifecycle metadata from the sidecar file.
/// </summary>
internal sealed record PacketLifecycleMetadata
{
    public required string Lifecycle { get; init; }

    public string? AbsorbedBy { get; init; }

    public string? SupersededBy { get; init; }

    public string? RetiredReason { get; init; }

    public string? RetiredAt { get; init; }
}
