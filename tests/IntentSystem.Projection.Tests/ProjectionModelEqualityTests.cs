using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Tests;

public sealed class ProjectionModelEqualityTests
{
    [Fact]
    public void ProjectionContext_GivenEquivalentValues_HasStableHashSetBehavior()
    {
        var left = ProjectionTestData.CreateBundle().Context;
        var right = ProjectionTestData.CreateBundle().Context;

        var set = new HashSet<ProjectionContext> { left, right };

        Assert.Equal(left, right);
        Assert.Single(set);
    }

    [Fact]
    public void SubSliceRow_GivenEquivalentValues_HasStableHashSetBehavior()
    {
        var left = ProjectionTestData.CreateBundle().Row;
        var right = ProjectionTestData.CreateBundle().Row;

        var set = new HashSet<SubSliceRow> { left, right };

        Assert.Equal(left, right);
        Assert.Single(set);
    }
}
