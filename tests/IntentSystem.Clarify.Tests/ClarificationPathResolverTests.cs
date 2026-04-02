using IntentSystem.Clarify;

namespace IntentSystem.Clarify.Tests;

public sealed class ClarificationPathResolverTests
{
    [Fact]
    public void ResolveDirectory_GivenExecutionUnit_ReturnsDeterministicClarificationDirectory()
    {
        var path = ClarificationPathResolver.ResolveDirectory("A2");

        Assert.Equal(".intent-cli/clarifications/a2", path);
        Assert.DoesNotContain("/issues/", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDirectory_GivenExecutionUnitWithWhitespace_TrimsAndNormalizes()
    {
        var path = ClarificationPathResolver.ResolveDirectory("  B1  ");

        Assert.Equal(".intent-cli/clarifications/b1", path);
    }

    [Fact]
    public void ResolveItemPath_GivenExecutionUnitAndId_ReturnsDeterministicJsonArtifactPath()
    {
        var path = ClarificationPathResolver.ResolveItemPath("A2", "question-1");

        Assert.Equal(".intent-cli/clarifications/a2/question-1.json", path);
    }

    [Fact]
    public void ResolveItemPath_GivenSameInputs_ReturnsSamePath()
    {
        var first = ClarificationPathResolver.ResolveItemPath("A2", "question-1");
        var second = ClarificationPathResolver.ResolveItemPath("A2", "question-1");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ResolveDirectory_GivenNullOrWhitespaceExecutionUnit_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveDirectory(null!));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveDirectory(""));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveDirectory("   "));
    }

    [Fact]
    public void ResolveItemPath_GivenNullOrWhitespaceId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveItemPath("A2", null!));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveItemPath("A2", ""));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveItemPath("A2", "   "));
    }
}
