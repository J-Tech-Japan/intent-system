namespace IntentSystem.Clarify;

public static class ClarificationPathResolver
{
    public static string ResolveDirectory(string executionUnit)
    {
        var sanitizedExecutionUnit = SanitizeExecutionUnit(executionUnit);
        return $"{ClarificationConstants.Directory}/{sanitizedExecutionUnit}";
    }

    public static string ResolveItemPath(string executionUnit, string id)
    {
        var directory = ResolveDirectory(executionUnit);
        var sanitizedId = SanitizeId(id);
        return $"{directory}/{sanitizedId}{ClarificationConstants.JsonFileExtension}";
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
