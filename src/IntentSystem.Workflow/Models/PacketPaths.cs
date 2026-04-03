namespace IntentSystem.Workflow.Models;

/// <summary>
/// Packet paths referenced by a workflow definition.
/// </summary>
public sealed record WorkflowPacketPaths
{
    public required string Implementation { get; init; }

    public required string ReviewContext { get; init; }

    public required string Yaml { get; init; }
}
