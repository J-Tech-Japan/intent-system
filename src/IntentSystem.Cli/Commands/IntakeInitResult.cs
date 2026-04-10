namespace IntentSystem.Cli.Commands;

internal sealed record IntakeInitResult
{
    public required string Domain { get; init; }

    public required string WorkRepoPath { get; init; }

    public required bool InterviewWasSkipped { get; init; }

    public required IReadOnlyList<string> CreatedQuestionIds { get; init; }

    public required IReadOnlyList<string> GeneratedPaths { get; init; }

    public required IReadOnlyList<string> SkippedPaths { get; init; }
}
