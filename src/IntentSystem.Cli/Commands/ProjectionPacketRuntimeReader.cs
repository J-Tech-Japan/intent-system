using IntentSystem.Projection.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ProjectionPacketRuntimeReader
{
    private const string DefaultClarificationReturnPath = "intents/intent-cli/clarifications/open.md";

    internal sealed record RuntimePacket(
        string ExecutionUnit,
        string IssueTitle,
        string TargetRepo,
        string TargetPath,
        string TargetPart,
        IReadOnlyList<string> Dependencies,
        string ClarificationReturnPath);

    public static RuntimePacket Read(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        try
        {
            var packet = ProjectionPacketSerializer.Deserialize(yaml);
            return new RuntimePacket(
                packet.ImplementationIssuePacket.SourceExecutionUnit,
                packet.ImplementationIssuePacket.IssueTitle,
                packet.ImplementationIssuePacket.TargetRepo,
                packet.ImplementationIssuePacket.TargetPath,
                packet.ImplementationIssuePacket.TargetPart,
                packet.ImplementationIssuePacket.Dependencies,
                packet.ReviewContextPacket.ClarificationReturnPath);
        }
        catch (InvalidOperationException)
        {
            return ReadCurrentParentPacket(yaml);
        }
    }

    private static RuntimePacket ReadCurrentParentPacket(string yaml)
    {
        var rootValues = new Dictionary<string, object>(StringComparer.Ordinal);
        var sections = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        Dictionary<string, object>? currentSection = null;
        string? currentListKey = null;

        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                if (line.EndsWith(":", StringComparison.Ordinal))
                {
                    var sectionName = line[..^1];
                    currentSection = new Dictionary<string, object>(StringComparer.Ordinal);
                    sections[sectionName] = currentSection;
                    currentListKey = null;
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0)
                {
                    throw new InvalidOperationException(
                        $"Projection packet YAML contains invalid root field '{line}'.");
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].TrimStart();
                rootValues[key] = ParseScalar(value);
                currentSection = null;
                currentListKey = null;
                continue;
            }

            if (currentSection is null)
            {
                throw new InvalidOperationException(
                    "Projection packet YAML must declare a section before its nested fields.");
            }

            if (line.StartsWith("    - ", StringComparison.Ordinal))
            {
                if (currentListKey is null
                    || !currentSection.TryGetValue(currentListKey, out var listValue)
                    || listValue is not List<string> list)
                {
                    throw new InvalidOperationException(
                        $"Projection packet YAML contains list item without a list field: '{line.Trim()}'.");
                }

                list.Add(ParseScalar(line[6..]));
                continue;
            }

            if (!line.StartsWith("  ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Projection packet YAML contains invalid indentation: '{line}'.");
            }

            var trimmed = line[2..];
            var separatorIndex2 = trimmed.IndexOf(':');
            if (separatorIndex2 < 0)
            {
                throw new InvalidOperationException(
                    $"Projection packet YAML field line is missing ':': '{trimmed}'.");
            }

            var key2 = trimmed[..separatorIndex2];
            var value2 = trimmed[(separatorIndex2 + 1)..].TrimStart();

            if (value2.Length == 0)
            {
                currentSection[key2] = new List<string>();
                currentListKey = key2;
                continue;
            }

            currentListKey = null;
            currentSection[key2] = value2 == "[]"
                ? Array.Empty<string>()
                : ParseScalar(value2);
        }

        if (!rootValues.TryGetValue("execution_unit", out var executionUnitValue)
            || executionUnitValue is not string executionUnit
            || string.IsNullOrWhiteSpace(executionUnit))
        {
            throw new InvalidOperationException("Projection packet YAML must contain root field 'execution_unit'.");
        }

        if (!sections.TryGetValue("implementation_issue", out var implementationIssue))
        {
            throw new InvalidOperationException("Projection packet YAML must contain required section 'implementation_issue'.");
        }

        if (!implementationIssue.TryGetValue("issue_title", out var issueTitleValue)
            || issueTitleValue is not string issueTitle
            || string.IsNullOrWhiteSpace(issueTitle))
        {
            throw new InvalidOperationException("Implementation issue must contain required field 'issue_title'.");
        }

        if (!implementationIssue.TryGetValue("target_repo", out var targetRepoValue)
            || targetRepoValue is not string targetRepo
            || string.IsNullOrWhiteSpace(targetRepo))
        {
            throw new InvalidOperationException("Implementation issue must contain required field 'target_repo'.");
        }

        var targetPath = TryGetScalar(implementationIssue, "target_path") ?? ".";
        var targetPart = TryGetScalar(implementationIssue, "target_part") ?? issueTitle;

        return new RuntimePacket(
            executionUnit,
            issueTitle,
            targetRepo,
            targetPath,
            targetPart,
            GetOptionalList(implementationIssue, "dependencies"),
            DefaultClarificationReturnPath);
    }

    private static string? TryGetScalar(
        IReadOnlyDictionary<string, object> values,
        string key)
    {
        return values.TryGetValue(key, out var value)
            ? value as string
            : null;
    }

    private static IReadOnlyList<string> GetOptionalList(
        IReadOnlyDictionary<string, object> values,
        string key)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return [];
        }

        return value switch
        {
            string[] arrayValue => arrayValue,
            List<string> listValue => listValue,
            _ => throw new InvalidOperationException(
                $"Projection packet YAML field '{key}' must be a list.")
        };
    }

    private static string ParseScalar(string value)
    {
        if (value.Length >= 2
            && value[0] == '"'
            && value[^1] == '"')
        {
            return value[1..^1]
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\r", StringComparison.Ordinal)
                .Replace("\\\"", "\"", StringComparison.Ordinal)
                .Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return value;
    }
}
