namespace IntentSystem.Cli.Tests;

/// <summary>
/// G839: serializes <see cref="WorkerPrCommentPreflightCommand"/> static seam
/// assignments shared by preflight tests and planned-labels byte-identity capture.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorkerPrCommentPreflightSharedStateCollection
{
    public const string Name = "WorkerPrCommentPreflightSharedState";
}
