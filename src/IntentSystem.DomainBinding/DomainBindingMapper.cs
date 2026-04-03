using IntentSystem.DomainBinding.Models;
using IntentSystem.Projection.Models;

namespace IntentSystem.DomainBinding;

public static class DomainBindingMapper
{
    public static ProjectionReadySlice ToProjectionReadySlice(DomainExecutionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ProjectionReadySlice
        {
            ExecutionUnit = source.ExecutionUnit,
            Goal = source.Goal,
            TargetRepo = source.TargetRepo,
            TargetPath = source.TargetPath,
            TargetPart = source.TargetPart,
            Dependencies = source.Dependencies,
            SuccessSignal = source.SuccessSignal,
            ReviewMode = source.ReviewMode,
            CompletionAction = source.CompletionAction,
            LandingPolicy = source.LandingPolicy,
            DogfoodingTrack = source.DogfoodingTrack,
            EmbeddedCanonicalSummary = source.EmbeddedCanonicalSummary
        };
    }

    public static SubSliceRow ToSubSliceRow(DomainExecutionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var projectionReady = ToProjectionReadySlice(source);
        return ToSubSliceRow(projectionReady);
    }

    public static SubSliceRow ToSubSliceRow(ProjectionReadySlice projectionReady)
    {
        ArgumentNullException.ThrowIfNull(projectionReady);

        return new SubSliceRow
        {
            SourceExecutionUnit = projectionReady.ExecutionUnit,
            Goal = projectionReady.Goal,
            TargetRepo = projectionReady.TargetRepo,
            TargetPath = projectionReady.TargetPath,
            TargetPart = projectionReady.TargetPart,
            DependsOnSubslices = projectionReady.Dependencies,
            DependsOn = projectionReady.Dependencies,
            RelatedIntents = [],
            SourceConcepts = [],
            SuccessSignal = projectionReady.SuccessSignal,
            ReviewMode = projectionReady.ReviewMode,
            CompletionAction = projectionReady.CompletionAction,
            LandingPolicy = projectionReady.LandingPolicy
        };
    }
}
