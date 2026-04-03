namespace IntentSystem.WorkerAdapter;

public static class WorkerAdapterRunArtifactPathResolver
{
    private const string WorkflowsDirectory = ".intent-cli/workflows";

    public static string Resolve(string executionUnit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        return $"{WorkflowsDirectory}/{executionUnit.Trim()}.run.json";
    }
}
