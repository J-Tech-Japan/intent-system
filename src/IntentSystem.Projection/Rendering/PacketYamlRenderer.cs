using System.Text;
using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Rendering;

public static class PacketYamlRenderer
{
    public static string Render(ImplementationIssuePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var sb = new StringBuilder();

        AppendScalar(sb, "issue_title", packet.IssueTitle);
        AppendScalar(sb, "issue_kind", FormatIssueKind(packet.IssueKind));
        AppendScalar(sb, "source_execution_unit", packet.SourceExecutionUnit);
        AppendScalar(sb, "goal", packet.Goal);
        AppendList(sb, "in_scope", packet.InScope);
        AppendList(sb, "out_of_scope", packet.OutOfScope);
        AppendScalar(sb, "target_repo", packet.TargetRepo);
        AppendScalar(sb, "target_path", packet.TargetPath);
        AppendScalar(sb, "target_part", packet.TargetPart);
        AppendList(sb, "dependencies", packet.Dependencies);
        AppendList(sb, "technical_baseline", packet.TechnicalBaseline);
        AppendList(sb, "project_local_guide", packet.ProjectLocalGuide);
        AppendList(sb, "intent_baseline", packet.IntentBaseline);
        AppendList(sb, "intent_references", packet.IntentReferences);
        AppendList(sb, "rules_and_specs", packet.RulesAndSpecs);
        AppendList(sb, "acceptance_criteria", packet.AcceptanceCriteria);
        AppendList(sb, "verification_evidence", packet.VerificationEvidence);
        AppendScalar(sb, "review_mode", packet.ReviewMode);
        AppendScalar(sb, "completion_action", packet.CompletionAction);
        AppendScalar(sb, "landing_policy", packet.LandingPolicy);

        return sb.ToString();
    }

    private static void AppendScalar(StringBuilder sb, string key, string value)
    {
        if (NeedsQuoting(value))
        {
            sb.AppendLine($"{key}: \"{EscapeYamlString(value)}\"");
        }
        else
        {
            sb.AppendLine($"{key}: {value}");
        }
    }

    private static void AppendList(StringBuilder sb, string key, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            sb.AppendLine($"{key}: []");
            return;
        }

        sb.AppendLine($"{key}:");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (NeedsQuoting(item))
            {
                sb.AppendLine($"  - \"{EscapeYamlString(item)}\"");
            }
            else
            {
                sb.AppendLine($"  - {item}");
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
