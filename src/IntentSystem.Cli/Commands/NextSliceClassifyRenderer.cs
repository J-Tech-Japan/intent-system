using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Output renderer for the read-only <c>next-slice classify</c> command (G185).
/// </summary>
internal static class NextSliceClassifyRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static void WriteText(TextWriter writer, NextSliceClassifyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"domain: {result.Domain}");
        writer.WriteLine($"classification: {result.Classification}");
        writer.WriteLine($"rationale: {result.Rationale}");

        writer.WriteLine("wip_refs:");
        if (result.WipRefs.Count == 0)
        {
            writer.WriteLine("  - (none)");
        }
        else
        {
            foreach (var wipRef in result.WipRefs)
            {
                writer.WriteLine($"  - {wipRef}");
            }
        }

        writer.WriteLine($"clarification_summary: {result.ClarificationSummary ?? "(none)"}");
        writer.WriteLine($"candidate_execution_unit: {result.CandidateExecutionUnit ?? "(none)"}");
        writer.WriteLine($"candidate_packet_path: {result.CandidatePacketPath ?? "(none)"}");
        writer.WriteLine($"recommended_next_action: {result.RecommendedNextAction}");

        if (!string.IsNullOrWhiteSpace(result.Reason))
        {
            writer.WriteLine($"reason: {result.Reason}");
        }
    }

    public static void WriteJson(TextWriter writer, NextSliceClassifyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    }
}
