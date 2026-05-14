using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G354: Pure analyzer that classifies missing Child Issue Contract sections
/// in a drafted packet's <c>github-body.md</c> as either
/// <c>mechanical-repairable</c> (boilerplate that can be derived without
/// product knowledge) or <c>product-clarification-required</c> (sections
/// that require a product decision before the packet can be published).
///
/// <para>Mechanical sections: <c>Verification</c> and <c>Related Links</c>.
/// These can be appended with standard templates; no operator input is needed.
/// For each mechanical gap the analyzer emits a repair guidance string that
/// the host agent can execute directly.</para>
///
/// <para>Product sections: all other required contract sections
/// (<c>Goal</c>, <c>Why This Slice Exists Now</c>, <c>Current Observed
/// State</c>, etc.). Missing product sections remain hard-stop
/// clarification-required gaps.</para>
///
/// <para>Never reads from or writes to the filesystem. Pure over the
/// supplied <paramref name="missingSections"/> list so that
/// <c>IntentNextSliceCommand</c> can re-use the same pre-computed missing
/// list without a second file read. The optional
/// <paramref name="packetDirectory"/> is used only to produce actionable
/// file paths in the repair guidance strings.</para>
/// </summary>
internal static class PacketContractGapAnalyzer
{
    public const string ClassificationMechanicalRepairable = "mechanical-repairable";
    public const string ClassificationProductClarificationRequired = "product-clarification-required";
    public const string ClassificationMixed = "mixed";
    public const string ClassificationNone = "none";

    /// <summary>
    /// Sections whose content can be derived mechanically from standard
    /// project conventions, without any product-specific decision. When
    /// ALL missing sections belong to this set, the packet is classified
    /// as <c>mechanical-repairable</c> and the host agent receives exact
    /// repair guidance to append.
    /// </summary>
    private static readonly HashSet<string> MechanicalSections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Verification",
            "Related Links"
        };

    /// <summary>
    /// Classify <paramref name="missingSections"/> and, for mechanical
    /// gaps, emit a repair guidance string that the host agent can
    /// apply directly (appending the missing heading to
    /// <c>github-body.md</c>).
    /// </summary>
    /// <param name="missingSections">
    /// The list of section headings absent from the packet's
    /// <c>github-body.md</c> (as produced by
    /// <c>IntentNextSliceCommand.BuildCandidate</c>).
    /// </param>
    /// <param name="packetDirectory">
    /// Optional absolute path to the packet directory. When supplied,
    /// repair guidance embeds the full path to <c>github-body.md</c>
    /// so the host agent can run the command without further lookup.
    /// When <c>null</c>, a relative placeholder is used instead.
    /// </param>
    public static PacketContractGapAnalysisResult Analyze(
        IReadOnlyList<string> missingSections,
        string? packetDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(missingSections);

        if (missingSections.Count == 0)
        {
            return new PacketContractGapAnalysisResult
            {
                Gaps = Array.Empty<PacketContractGapItem>(),
                HasMechanicalGaps = false,
                HasProductGaps = false,
                OverallClassification = ClassificationNone,
                RepairGuidance = Array.Empty<string>()
            };
        }

        var gaps = new List<PacketContractGapItem>(missingSections.Count);
        var repairGuidance = new List<string>();
        var hasMechanical = false;
        var hasProduct = false;

        foreach (var section in missingSections)
        {
            if (MechanicalSections.Contains(section))
            {
                hasMechanical = true;
                var guidance = BuildRepairGuidance(section, packetDirectory);
                gaps.Add(new PacketContractGapItem
                {
                    Section = section,
                    Classification = ClassificationMechanicalRepairable,
                    RepairGuidance = guidance
                });
                repairGuidance.Add(guidance);
            }
            else
            {
                hasProduct = true;
                gaps.Add(new PacketContractGapItem
                {
                    Section = section,
                    Classification = ClassificationProductClarificationRequired,
                    RepairGuidance = null
                });
            }
        }

        var overall = (hasMechanical, hasProduct) switch
        {
            (true, false) => ClassificationMechanicalRepairable,
            (false, true) => ClassificationProductClarificationRequired,
            (true, true) => ClassificationMixed,
            _ => ClassificationNone
        };

        return new PacketContractGapAnalysisResult
        {
            Gaps = gaps,
            HasMechanicalGaps = hasMechanical,
            HasProductGaps = hasProduct,
            OverallClassification = overall,
            RepairGuidance = repairGuidance
        };
    }

    private static string BuildRepairGuidance(string section, string? packetDirectory)
    {
        var bodyPath = packetDirectory is not null
            ? Path.Combine(packetDirectory, "github-body.md")
            : "github-body.md";

        return section switch
        {
            "Verification" =>
                $"Append the following section to `{bodyPath}`:\n" +
                "```markdown\n" +
                "## Verification\n\n" +
                "- Run `dotnet test` and confirm all tests pass.\n" +
                "- Run `intent-cli automation doctor --format json` to confirm CLI version.\n" +
                "```",

            "Related Links" =>
                $"Append the following section to `{bodyPath}`:\n" +
                "```markdown\n" +
                "## Related Links\n\n" +
                "- (none)\n" +
                "```",

            _ =>
                $"Add a `## {section}` heading with content to `{bodyPath}`."
        };
    }
}

/// <summary>
/// G354: Result of <c>PacketContractGapAnalyzer.Analyze</c>. Embedded in
/// <c>IntentNextSliceCandidate</c> so callers can inspect per-section
/// classifications without re-scanning the file.
/// </summary>
internal sealed record PacketContractGapAnalysisResult
{
    [JsonPropertyName("gaps")]
    public required IReadOnlyList<PacketContractGapItem> Gaps { get; init; }

    [JsonPropertyName("has_mechanical_gaps")]
    public required bool HasMechanicalGaps { get; init; }

    [JsonPropertyName("has_product_gaps")]
    public required bool HasProductGaps { get; init; }

    /// <summary>
    /// Overall classification: <c>mechanical-repairable</c>,
    /// <c>product-clarification-required</c>, <c>mixed</c>, or <c>none</c>.
    /// </summary>
    [JsonPropertyName("overall_classification")]
    public required string OverallClassification { get; init; }

    /// <summary>
    /// Human-readable repair instructions for each mechanical gap.
    /// Empty when <see cref="HasMechanicalGaps"/> is false.
    /// </summary>
    [JsonPropertyName("repair_guidance")]
    public required IReadOnlyList<string> RepairGuidance { get; init; }
}

/// <summary>
/// G354: One classified gap entry in <see cref="PacketContractGapAnalysisResult"/>.
/// </summary>
internal sealed record PacketContractGapItem
{
    [JsonPropertyName("section")]
    public required string Section { get; init; }

    /// <summary>
    /// <c>mechanical-repairable</c> or <c>product-clarification-required</c>.
    /// </summary>
    [JsonPropertyName("classification")]
    public required string Classification { get; init; }

    /// <summary>
    /// Non-null for mechanical sections; null for product sections.
    /// </summary>
    [JsonPropertyName("repair_guidance")]
    public string? RepairGuidance { get; init; }
}
