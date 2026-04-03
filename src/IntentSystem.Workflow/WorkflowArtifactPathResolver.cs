namespace IntentSystem.Workflow;

public static class WorkflowArtifactPathResolver
{
    private const string WorkflowsDirectory = ".intent-cli/workflows";

    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return $"{WorkflowsDirectory}/{executionUnit.Trim()}.yaml";
    }
}
