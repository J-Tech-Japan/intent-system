using System.Text;
using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Rendering;

public static class ImplementationMarkdownRenderer
{
    public static string Render(ImplementationIssuePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var sb = new StringBuilder();

        AppendHeader(sb, packet);
        AppendSection(sb, "Goal", packet.Goal);
        AppendListSection(sb, "In Scope", packet.InScope);
        AppendListSection(sb, "Out Of Scope", packet.OutOfScope);
        AppendTargetSection(sb, packet);
        AppendListSection(sb, "Dependencies", packet.Dependencies);
        AppendListSection(sb, "Technical Baseline", packet.TechnicalBaseline);
        AppendListSection(sb, "Project Local Guide", packet.ProjectLocalGuide);
        AppendListSection(sb, "Intent Baseline", packet.IntentBaseline);
        AppendListSection(sb, "Intent References", packet.IntentReferences);
        AppendListSection(sb, "Rules And Specs", packet.RulesAndSpecs);
        AppendListSection(sb, "Acceptance Criteria", packet.AcceptanceCriteria);
        AppendVerificationSection(sb, packet);
        AppendWorkflowSection(sb, packet);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, ImplementationIssuePacket packet)
    {
        sb.AppendLine($"# {packet.IssueTitle}");
        sb.AppendLine();
        sb.AppendLine($"- **kind**: `{FormatIssueKind(packet.IssueKind)}`");
        sb.AppendLine($"- **execution-unit**: `{packet.SourceExecutionUnit}`");
        sb.AppendLine();
    }

    private static void AppendSection(StringBuilder sb, string heading, string content)
    {
        sb.AppendLine($"## {heading}");
        sb.AppendLine();
        sb.AppendLine(content);
        sb.AppendLine();
    }

    private static void AppendListSection(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        sb.AppendLine($"## {heading}");
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

    private static void AppendTargetSection(StringBuilder sb, ImplementationIssuePacket packet)
    {
        sb.AppendLine("## Target");
        sb.AppendLine();
        sb.AppendLine($"- **repo**: `{packet.TargetRepo}`");
        sb.AppendLine($"- **path**: `{packet.TargetPath}`");
        sb.AppendLine($"- **part**: `{packet.TargetPart}`");
        sb.AppendLine();
    }

    private static void AppendVerificationSection(StringBuilder sb, ImplementationIssuePacket packet)
    {
        sb.AppendLine("## Verification");
        sb.AppendLine();
        sb.AppendLine("- required evidence:");

        for (var i = 0; i < packet.VerificationEvidence.Count; i++)
        {
            sb.AppendLine($"  - `{packet.VerificationEvidence[i]}`");
        }

        sb.AppendLine();
    }

    private static void AppendWorkflowSection(StringBuilder sb, ImplementationIssuePacket packet)
    {
        sb.AppendLine("## Workflow");
        sb.AppendLine();
        sb.AppendLine($"- **review-mode**: `{packet.ReviewMode}`");
        sb.AppendLine($"- **completion-action**: `{packet.CompletionAction}`");
        sb.AppendLine($"- **landing-policy**: `{packet.LandingPolicy}`");
        sb.AppendLine();
    }

    private static string FormatIssueKind(IssueKind kind)
    {
        return kind switch
        {
            IssueKind.Feature => "feature",
            IssueKind.Bugfix => "bugfix",
            IssueKind.BoundaryFix => "boundary-fix",
            IssueKind.Verification => "verification",
            IssueKind.Refactor => "refactor",
            IssueKind.ClarificationFollowup => "clarification-followup",
            _ => kind.ToString().ToLowerInvariant()
        };
    }
}
