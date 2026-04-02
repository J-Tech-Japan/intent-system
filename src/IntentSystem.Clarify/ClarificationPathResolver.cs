namespace IntentSystem.Clarify;

/// <summary>
/// Resolves deterministic artifact paths for clarification items under
/// .intent-cli/clarifications/&lt;execution-unit&gt;/&lt;id&gt;/.
/// External artifact layout produces Markdown/YAML artifacts per the parent
/// baseline contract. Internal JSON serialization is a separate concern.
/// </summary>
public static class ClarificationPathResolver
{
    public static string ResolveDirectory(string executionUnit)
    {
        var sanitizedExecutionUnit = SanitizeExecutionUnit(executionUnit);
        return $"{ClarificationConstants.Directory}/{sanitizedExecutionUnit}";
    }

    public static string ResolveItemDirectory(string executionUnit, string id)
    {
        var directory = ResolveDirectory(executionUnit);
        var sanitizedId = SanitizeId(id);
        return $"{directory}/{sanitizedId}";
    }

    public static string ResolveQuestionPath(string executionUnit, string id)
    {
        var itemDirectory = ResolveItemDirectory(executionUnit, id);
        return $"{itemDirectory}/{ClarificationConstants.QuestionFileName}";
    }

    public static string ResolveContextPath(string executionUnit, string id)
    {
        var itemDirectory = ResolveItemDirectory(executionUnit, id);
        return $"{itemDirectory}/{ClarificationConstants.ContextFileName}";
    }

    private static string SanitizeExecutionUnit(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        return executionUnit.Trim().ToLowerInvariant();
    }

    private static string SanitizeId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id.Trim();
    }
}
