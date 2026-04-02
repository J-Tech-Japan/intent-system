namespace IntentSystem.Projection.Tests;

public sealed class PacketPathResolverTests
{
    [Fact]
    public void Resolve_GivenExecutionUnit_ReturnsDeterministicPaths()
    {
        var paths = PacketPathResolver.Resolve("A2");

        Assert.Equal(".takt/packets/a2/implementation.md", paths.Implementation);
        Assert.Equal(".takt/packets/a2/review-context.md", paths.ReviewContext);
        Assert.Equal(".takt/packets/a2/packet.yaml", paths.Yaml);
    }

    [Fact]
    public void Resolve_GivenSameExecutionUnit_ReturnsSamePaths()
    {
        var first = PacketPathResolver.Resolve("A2");
        var second = PacketPathResolver.Resolve("A2");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_GivenExecutionUnitWithWhitespace_TrimsAndNormalizes()
    {
        var paths = PacketPathResolver.Resolve("  B1  ");

        Assert.Equal(".takt/packets/b1/implementation.md", paths.Implementation);
    }

    [Fact]
    public void Resolve_GivenMixedCaseExecutionUnit_NormalizesToLowerCase()
    {
        var paths = PacketPathResolver.Resolve("A2");
        var pathsLower = PacketPathResolver.Resolve("a2");

        Assert.Equal(paths, pathsLower);
    }

    [Fact]
    public void Resolve_GivenNullOrWhitespace_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => PacketPathResolver.Resolve(null!));
        Assert.ThrowsAny<ArgumentException>(() => PacketPathResolver.Resolve(""));
        Assert.ThrowsAny<ArgumentException>(() => PacketPathResolver.Resolve("   "));
    }
}
