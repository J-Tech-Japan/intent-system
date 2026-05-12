using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G328: provenance recorded on a next-slice packet — which role
/// authored the packet (<c>design</c> vs <c>review-runtime</c>),
/// which host workspace produced it, and (for runtime-created packets
/// generated after a closeout) the source PR. Packets that pre-date
/// G328 do not record provenance; the resolver treats absent
/// provenance as <c>design</c> with <c>provenance_source =
/// default-design</c> so existing design-side packets continue to
/// publish unchanged.
///
/// Pure data — no I/O. The reader in
/// <see cref="NextSlicePacketProvenanceReader"/> performs the file
/// reads; this record is the analyzer-facing model.
/// </summary>
internal sealed record NextSlicePacketProvenance
{
    /// <summary><c>design</c> or <c>review-runtime</c>.</summary>
    [JsonPropertyName("created_by_role")]
    public required string CreatedByRole { get; init; }

    /// <summary>
    /// Optional workspace identity (repo / worktree label) that
    /// authored the packet. Useful when multiple review-runtime
    /// workspaces (e.g. IntentSystemReview, SekibanAsAServiceReview)
    /// can both create runtime packets — the host string tells the
    /// operator which one did.
    /// </summary>
    [JsonPropertyName("created_by_host")]
    public string? CreatedByHost { get; init; }

    /// <summary>
    /// Optional PR number of the closeout that triggered the
    /// runtime-created packet. Only meaningful when
    /// <see cref="CreatedByRole"/> is <c>review-runtime</c>.
    /// </summary>
    [JsonPropertyName("source_closeout_pr")]
    public int? SourceCloseoutPr { get; init; }

    /// <summary>
    /// Which artifact recorded the provenance:
    /// <c>packet.yaml</c> when read from the packet manifest, or
    /// <c>default-design</c> when no provenance block exists and the
    /// resolver assumed design ownership for legacy packets.
    /// </summary>
    [JsonPropertyName("provenance_source")]
    public required string ProvenanceSource { get; init; }
}

/// <summary>
/// G328: canonical role + provenance-source vocabulary.
/// </summary>
internal static class NextSlicePacketProvenanceConstants
{
    public const string RoleDesign = "design";
    public const string RoleReviewRuntime = "review-runtime";

    public const string ProvenanceSourcePacketYaml = "packet.yaml";
    public const string ProvenanceSourceDefaultDesign = "default-design";
}

/// <summary>
/// G328: read provenance from a packet directory. Looks for a
/// top-level <c>provenance:</c> block in <c>packet.yaml</c>:
///
/// <code>
/// provenance:
///   created_by_role: review-runtime
///   created_by_host: review-runtime-intent-system
///   source_closeout_pr: 758
/// </code>
///
/// When <c>packet.yaml</c> is absent, unreadable, or has no
/// <c>provenance</c> block, the reader returns the default-design
/// fallback so pre-G328 packets continue to work. Bad role values
/// (anything other than <c>design</c> / <c>review-runtime</c>) also
/// fall back to default-design — the resolver never throws.
/// </summary>
internal static class NextSlicePacketProvenanceReader
{
    /// <summary>
    /// Read the provenance for the packet at <paramref name="packetDirectory"/>.
    /// </summary>
    public static NextSlicePacketProvenance Read(string packetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packetDirectory);
        var packetYamlPath = Path.Combine(packetDirectory, "packet.yaml");
        return ReadFromText(File.Exists(packetYamlPath) ? File.ReadAllText(packetYamlPath) : null);
    }

    /// <summary>
    /// Read the provenance from the supplied <c>packet.yaml</c>
    /// text. Pure — does not touch the filesystem.
    /// </summary>
    public static NextSlicePacketProvenance ReadFromText(string? packetYamlText)
    {
        if (string.IsNullOrWhiteSpace(packetYamlText))
        {
            return DefaultDesign();
        }

        var (role, host, sourcePr) = ParseProvenanceBlock(packetYamlText);
        if (role is null)
        {
            return DefaultDesign();
        }

        if (!string.Equals(role, NextSlicePacketProvenanceConstants.RoleDesign, StringComparison.Ordinal)
            && !string.Equals(role, NextSlicePacketProvenanceConstants.RoleReviewRuntime, StringComparison.Ordinal))
        {
            // Unknown role; treat as design fallback (we never want
            // bad data on a packet to break next-slice planning).
            return DefaultDesign();
        }

        return new NextSlicePacketProvenance
        {
            CreatedByRole = role,
            CreatedByHost = host,
            SourceCloseoutPr = sourcePr,
            ProvenanceSource = NextSlicePacketProvenanceConstants.ProvenanceSourcePacketYaml
        };
    }

    private static NextSlicePacketProvenance DefaultDesign() =>
        new()
        {
            CreatedByRole = NextSlicePacketProvenanceConstants.RoleDesign,
            CreatedByHost = null,
            SourceCloseoutPr = null,
            ProvenanceSource = NextSlicePacketProvenanceConstants.ProvenanceSourceDefaultDesign
        };

    /// <summary>
    /// Minimal indentation-aware parser for the <c>provenance:</c>
    /// block. The packet.yaml schema is small and tightly controlled
    /// (it's a human-authored design artifact, not a free-form
    /// document), so a hand-rolled parser is preferable to pulling
    /// in a full YAML dependency just for one nested block.
    /// </summary>
    private static (string? Role, string? Host, int? SourcePr) ParseProvenanceBlock(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        // Find the top-level `provenance:` line. A top-level line has
        // zero leading whitespace.
        int provenanceLineIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]))
            {
                continue;
            }
            var trimmed = line.Trim();
            if (string.Equals(trimmed, "provenance:", StringComparison.Ordinal))
            {
                provenanceLineIndex = i;
                break;
            }
        }

        if (provenanceLineIndex < 0)
        {
            return (null, null, null);
        }

        string? role = null;
        string? host = null;
        int? sourcePr = null;

        // Walk subsequent indented lines until we hit a line that
        // dedents back to top level.
        for (var i = provenanceLineIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            if (!char.IsWhiteSpace(line[0]))
            {
                // Hit a new top-level key — provenance block is over.
                break;
            }

            var content = line.Trim();
            var colonIndex = content.IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }
            var key = content[..colonIndex].Trim();
            var value = content[(colonIndex + 1)..].Trim();
            // Strip optional surrounding quotes.
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            switch (key)
            {
                case "created_by_role":
                    role = value;
                    break;
                case "created_by_host":
                    host = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "source_closeout_pr":
                    if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                        && parsed > 0)
                    {
                        sourcePr = parsed;
                    }
                    break;
            }
        }

        return (role, host, sourcePr);
    }
}
