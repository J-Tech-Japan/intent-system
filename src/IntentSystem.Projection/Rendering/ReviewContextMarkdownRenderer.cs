using System.Text;
using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Rendering;

public static class ReviewContextMarkdownRenderer
{
    public static string Render(ReviewContextPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var sb = new StringBuilder();

        AppendExecutionUnitSection(sb, packet.SourceExecutionUnit);
        AppendScalarSection(sb, "Parent Intent Root", packet.ParentIntentRoot);
        AppendListSection(sb, "Intent References", packet.IntentReferences);
        AppendListSection(sb, "Rules And Specs", packet.RulesAndSpecs);
        AppendListSection(sb, "Acceptance Criteria", packet.AcceptanceCriteria);
        AppendListSection(sb, "Deterministic Review Checks", packet.DeterministicReviewChecks);
        AppendScalarSection(sb, "Clarification Return Path", packet.ClarificationReturnPath);

        return sb.ToString();
    }

    private static void AppendExecutionUnitSection(StringBuilder sb, string executionUnit)
    {
        sb.AppendLine("# Execution Unit");
        sb.AppendLine();
        sb.AppendLine($"`{executionUnit}`");
        sb.AppendLine();
    }

    private static void AppendScalarSection(StringBuilder sb, string heading, string content)
    {
        sb.AppendLine($"# {heading}");
        sb.AppendLine();
        sb.AppendLine(content);
        sb.AppendLine();
    }

    private static void AppendListSection(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        sb.AppendLine($"# {heading}");
        sb.AppendLine();

        if (items.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                sb.AppendLine($"- {items[i]}");
            }
        }

        sb.AppendLine();
    }
}
