using IntentSystem.ConceptIntake.Models;

namespace IntentSystem.Cli.Commands;

internal sealed record IntakeCompileCoreResult
{
    public required string Domain { get; init; }

    public required bool IsReady { get; init; }

    public IntakeCompileRequest? Request { get; init; }

    public string? ArtifactPath { get; init; }

    public InterviewQueueItem? NextQuestion { get; init; }
}
