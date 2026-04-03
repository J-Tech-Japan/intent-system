namespace IntentSystem.Projection.Tests;

public sealed class PacketPathResolverTests
{
    [Fact]
    public void Resolve_GivenExecutionUnit_ReturnsDeterministicPaths()
    {
        var paths = PacketPathResolver.Resolve("A2");

        Assert.Equal(".intent-cli/issues/A2/implementation.md", paths.Implementation);
        Assert.Equal(".intent-cli/issues/A2/review-context.md", paths.ReviewContext);
        Assert.Equal(".intent-cli/issues/A2/packet.yaml", paths.Yaml);
    }

    [Fact]
    public void Resolve_GivenSameExecutionUnit_ReturnsSamePaths()
    {
        var first = PacketPathResolver.Resolve("A2");
        var second = PacketPathResolver.Resolve("A2");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_GivenExecutionUnitWithWhitespace_TrimsAndPreservesCasing()
    {
        var paths = PacketPathResolver.Resolve("  B1  ");

        Assert.Equal(".intent-cli/issues/B1/implementation.md", paths.Implementation);
    }

    [Fact]
    public void Resolve_GivenDifferentCasing_ReturnsDifferentPaths()
    {
        var paths = PacketPathResolver.Resolve("A2");
        var pathsLower = PacketPathResolver.Resolve("a2");

        Assert.NotEqual(paths, pathsLower);
    }

    [Fact]
    public void Resolve_GivenNullOrWhitespace_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => PacketPathResolver.Resolve(null!));
        Assert.ThrowsAny<ArgumentException>(() => PacketPathResolver.Resolve(""));
        Assert.ThrowsAny<ArgumentException>(() => PacketPathResolver.Resolve("   "));
    }
}
