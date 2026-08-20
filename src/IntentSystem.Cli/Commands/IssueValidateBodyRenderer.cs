using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Output renderer for <c>issue validate-body</c> (G183). Produces a
/// deterministic human-readable summary (default) or stable snake_case JSON.
/// </summary>
internal static class IssueValidateBodyRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static void WriteText(TextWriter writer, IssueValidateBodyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine($"Source: {result.SourcePath}");
        if (result.IsValid)
        {
            writer.WriteLine("Result: valid — issue body satisfies the Child Issue Contract.");
            return;
        }

        writer.WriteLine("Result: invalid");

        if (result.MissingHeadings.Count > 0)
        {
            writer.WriteLine("Missing required headings:");
            foreach (var heading in result.MissingHeadings)
            {
                writer.WriteLine($"- {heading}");
            }
        }

        if (result.TargetPathsInvalid)
        {
            writer.WriteLine($"Target paths declaration: {result.TargetPathsReason ?? "invalid."}");
        }

        if (result.RelatedLinksInvalid)
        {
            writer.WriteLine($"Related Links: {result.RelatedLinksReason ?? "invalid."}");
        }
    }

    public static void WriteJson(TextWriter writer, IssueValidateBodyResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
    }
}
