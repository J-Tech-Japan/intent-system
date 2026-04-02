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
    public void ResolveItemDirectory_GivenExecutionUnitAndId_ReturnsDeterministicItemDirectory()
    {
        var path = ClarificationPathResolver.ResolveItemDirectory("A2", "question-1");

        Assert.Equal(".intent-cli/clarifications/a2/question-1", path);
    }

    [Fact]
    public void ResolveQuestionPath_GivenExecutionUnitAndId_ReturnsMarkdownArtifactPath()
    {
        var path = ClarificationPathResolver.ResolveQuestionPath("A2", "question-1");

        Assert.Equal(".intent-cli/clarifications/a2/question-1/question.md", path);
        Assert.DoesNotContain(".json", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveContextPath_GivenExecutionUnitAndId_ReturnsYamlArtifactPath()
    {
        var path = ClarificationPathResolver.ResolveContextPath("A2", "question-1");

        Assert.Equal(".intent-cli/clarifications/a2/question-1/context.yaml", path);
        Assert.DoesNotContain(".json", path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveQuestionPath_GivenSameInputs_ReturnsSamePath()
    {
        var first = ClarificationPathResolver.ResolveQuestionPath("A2", "question-1");
        var second = ClarificationPathResolver.ResolveQuestionPath("A2", "question-1");

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
    public void ResolveQuestionPath_GivenNullOrWhitespaceId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveQuestionPath("A2", null!));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveQuestionPath("A2", ""));
        Assert.ThrowsAny<ArgumentException>(() => ClarificationPathResolver.ResolveQuestionPath("A2", "   "));
    }
}
