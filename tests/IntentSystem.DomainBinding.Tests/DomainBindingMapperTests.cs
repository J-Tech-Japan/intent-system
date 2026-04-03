using IntentSystem.DomainBinding.Models;

namespace IntentSystem.DomainBinding.Tests;

public sealed class DomainBindingMapperTests
{
    [Fact]
    public void ToProjectionReadySlice_GivenBackendFirstSource_PreservesAllProjectionContractFields()
    {
        var source = CreateSource();

        var projectionReady = DomainBindingMapper.ToProjectionReadySlice(source);

        Assert.Equal(source.ExecutionUnit, projectionReady.ExecutionUnit);
        Assert.Equal(source.Goal, projectionReady.Goal);
        Assert.Equal(source.TargetRepo, projectionReady.TargetRepo);
        Assert.Equal(source.TargetPath, projectionReady.TargetPath);
        Assert.Equal(source.TargetPart, projectionReady.TargetPart);
        Assert.Equal(source.Dependencies, projectionReady.Dependencies);
        Assert.Equal(source.SuccessSignal, projectionReady.SuccessSignal);
        Assert.Equal(source.ReviewMode, projectionReady.ReviewMode);
        Assert.Equal(source.CompletionAction, projectionReady.CompletionAction);
        Assert.Equal(source.DogfoodingTrack, projectionReady.DogfoodingTrack);
        Assert.Equal(source.EmbeddedCanonicalSummary, projectionReady.EmbeddedCanonicalSummary);
    }

    [Fact]
    public void ToSubSliceRow_GivenProjectionReadySlice_MapsToGenericProjectionInput()
    {
        var source = CreateSource();

        var row = DomainBindingMapper.ToSubSliceRow(source);

        Assert.Equal("F1", row.SourceExecutionUnit);
        Assert.Equal("Create a projection-ready binding for the backend execution slice.", row.Goal);
        Assert.Equal("J-Tech-Japan/intent-system", row.TargetRepo);
        Assert.Equal(".", row.TargetPath);
        Assert.Equal("domain binding", row.TargetPart);
        Assert.Equal(["A2", "B2"], row.DependsOnSubslices);
        Assert.Equal(["A2", "B2"], row.DependsOn);
        Assert.Equal("backend sub-slice can be reconstructed as projection-ready input", row.SuccessSignal);
        Assert.Equal("manual-review", row.ReviewMode);
        Assert.Equal("open-pr", row.CompletionAction);
        Assert.Equal("squash", row.LandingPolicy);
    }

    [Fact]
    public void ToSubSliceRow_GivenDomainSource_DoesNotInjectPrivateSummaryIntoGenericProjectionFields()
    {
        var source = CreateSource();

        var row = DomainBindingMapper.ToSubSliceRow(source);

        Assert.Empty(row.RelatedIntents);
        Assert.Empty(row.SourceConcepts);
    }

    private static DomainExecutionSource CreateSource()
    {
        return new DomainExecutionSource
        {
            SourceKind = DomainExecutionSourceKind.IssueReadySubSlice,
            DogfoodingTrack = DogfoodingTrack.BackendFirst,
            ExecutionUnit = "F1",
            Goal = "Create a projection-ready binding for the backend execution slice.",
            TargetRepo = "J-Tech-Japan/intent-system",
            TargetPath = ".",
            TargetPart = "domain binding",
            Dependencies = ["A2", "B2"],
            SuccessSignal = "backend sub-slice can be reconstructed as projection-ready input",
            ReviewMode = "manual-review",
            CompletionAction = "open-pr",
            LandingPolicy = "squash",
            EmbeddedCanonicalSummary = "Backend execution slice summary embedded for child-repo contract tests."
        };
    }
}
