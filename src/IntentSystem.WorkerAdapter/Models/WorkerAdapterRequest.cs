namespace IntentSystem.WorkerAdapter.Models;

/// <summary>
/// Thin supervisor -> worker adapter input contract for a single workflow run.
/// </summary>
public sealed record WorkerAdapterRequest
{
    public required string WorkflowDefinitionRef { get; init; }

    public required string RunId { get; init; }

    public required string TargetWorktree { get; init; }

    public required AdapterRuntimeEnvironment RuntimeEnv { get; init; }

    public required AdapterEventSink EventSink { get; init; }
}
