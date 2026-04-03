using IntentSystem.Projection.Models;

namespace IntentSystem.Projection.Tests;

public sealed class ReadOnlyListHashCodeExtensionsTests
{
    [Fact]
    public void AddValues_GivenEquivalentSequences_ProducesSameHashCode()
    {
        var left = new HashCode();
        left.Add("seed");
        ReadOnlyListHashCodeExtensions.AddValues(ref left, ["alpha", "beta", "gamma"]);

        var right = new HashCode();
        right.Add("seed");
        ReadOnlyListHashCodeExtensions.AddValues(ref right, ["alpha", "beta", "gamma"]);

        Assert.Equal(left.ToHashCode(), right.ToHashCode());
    }

    [Fact]
    public void AddValues_GivenDifferentSequences_ProducesDifferentHashCodes()
    {
        var left = new HashCode();
        left.Add("seed");
        ReadOnlyListHashCodeExtensions.AddValues(ref left, ["alpha", "beta", "gamma"]);

        var right = new HashCode();
        right.Add("seed");
        ReadOnlyListHashCodeExtensions.AddValues(ref right, ["alpha", "gamma", "beta"]);

        Assert.NotEqual(left.ToHashCode(), right.ToHashCode());
    }
}
