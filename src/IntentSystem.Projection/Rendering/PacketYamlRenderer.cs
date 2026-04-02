using System.Text;
using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Rendering;

public static class PacketYamlRenderer
{
    public static string Render(ImplementationIssuePacket implementationPacket, ReviewContextPacket reviewContextPacket)
    {
        ArgumentNullException.ThrowIfNull(implementationPacket);
        ArgumentNullException.ThrowIfNull(reviewContextPacket);

        var sb = new StringBuilder();

        sb.AppendLine("implementation_issue_packet:");
        AppendImplementationSection(sb, implementationPacket);
        sb.AppendLine("review_context_packet:");
        AppendReviewContextSection(sb, reviewContextPacket);

        return sb.ToString();
    }

    private static void AppendImplementationSection(StringBuilder sb, ImplementationIssuePacket packet)
    {
        AppendScalar(sb, "issue_title", packet.IssueTitle, indent: 2);
        AppendScalar(sb, "issue_kind", FormatIssueKind(packet.IssueKind), indent: 2);
        AppendScalar(sb, "source_execution_unit", packet.SourceExecutionUnit, indent: 2);
        AppendScalar(sb, "goal", packet.Goal, indent: 2);
        AppendList(sb, "in_scope", packet.InScope, indent: 2);
        AppendList(sb, "out_of_scope", packet.OutOfScope, indent: 2);
        AppendScalar(sb, "target_repo", packet.TargetRepo, indent: 2);
        AppendScalar(sb, "target_path", packet.TargetPath, indent: 2);
        AppendScalar(sb, "target_part", packet.TargetPart, indent: 2);
        AppendList(sb, "dependencies", packet.Dependencies, indent: 2);
        AppendList(sb, "technical_baseline", packet.TechnicalBaseline, indent: 2);
        AppendList(sb, "project_local_guide", packet.ProjectLocalGuide, indent: 2);
        AppendList(sb, "intent_baseline", packet.IntentBaseline, indent: 2);
        AppendList(sb, "intent_references", packet.IntentReferences, indent: 2);
        AppendList(sb, "rules_and_specs", packet.RulesAndSpecs, indent: 2);
        AppendList(sb, "acceptance_criteria", packet.AcceptanceCriteria, indent: 2);
        AppendList(sb, "verification_evidence", packet.VerificationEvidence, indent: 2);
        AppendScalar(sb, "review_mode", packet.ReviewMode, indent: 2);
        AppendScalar(sb, "completion_action", packet.CompletionAction, indent: 2);
        AppendScalar(sb, "landing_policy", packet.LandingPolicy, indent: 2);
    }

    private static void AppendReviewContextSection(StringBuilder sb, ReviewContextPacket packet)
    {
        AppendScalar(sb, "source_execution_unit", packet.SourceExecutionUnit, indent: 2);
        AppendScalar(sb, "parent_intent_root", packet.ParentIntentRoot, indent: 2);
        AppendList(sb, "intent_references", packet.IntentReferences, indent: 2);
        AppendList(sb, "rules_and_specs", packet.RulesAndSpecs, indent: 2);
        AppendList(sb, "acceptance_criteria", packet.AcceptanceCriteria, indent: 2);
        AppendList(sb, "deterministic_review_checks", packet.DeterministicReviewChecks, indent: 2);
        AppendScalar(sb, "clarification_return_path", packet.ClarificationReturnPath, indent: 2);
    }

    private static void AppendScalar(StringBuilder sb, string key, string value, int indent)
    {
        var prefix = new string(' ', indent);
        if (NeedsQuoting(value))
        {
            sb.AppendLine($"{prefix}{key}: \"{EscapeYamlString(value)}\"");
        }
        else
        {
            sb.AppendLine($"{prefix}{key}: {value}");
        }
    }

    private static void AppendList(StringBuilder sb, string key, IReadOnlyList<string> items, int indent)
    {
        var prefix = new string(' ', indent);
        if (items.Count == 0)
        {
            sb.AppendLine($"{prefix}{key}: []");
            return;
        }

        sb.AppendLine($"{prefix}{key}:");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (NeedsQuoting(item))
            {
                sb.AppendLine($"{prefix}  - \"{EscapeYamlString(item)}\"");
            }
            else
            {
                sb.AppendLine($"{prefix}  - {item}");
            }
        }
    }

    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is ':' or '#' or '\'' or '"' or '{' or '}' or '[' or ']'
                or ',' or '&' or '*' or '?' or '|' or '<' or '>'
                or '=' or '!' or '%' or '@' or '`' or '\n' or '\r')
            {
                return true;
            }
        }

        if (value.StartsWith(' ') || value.EndsWith(' '))
        {
            return true;
        }

        if (value.StartsWith('-'))
        {
            return true;
        }

        return false;
    }

    private static string EscapeYamlString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);
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
