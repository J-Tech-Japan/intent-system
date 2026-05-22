using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G385: tests for the pure <see cref="GitHubCliJsonBoundary"/> that hardens
/// the github-only worker selector against contaminated <c>gh issue list</c> /
/// <c>gh pr list</c> stdout. Covers the clean macOS/zsh case, Windows
/// PowerShell-style BOM/trailing contamination (the JTJ_Estivo byte-366 error
/// shape), malformed JSON diagnostics, gh process-failure classification, and
/// secret-redacting preview sanitization.
/// </summary>
public sealed class GitHubCliJsonBoundaryTests
{
    private const string Call = "`gh issue list` for J-Tech-Japan/JTJ_Estivo";

    [Fact]
    public void ExtractJsonArray_ValidArray_SucceedsAndPreservesPayload()
    {
        const string stdout = "[{\"number\":78,\"title\":\"x\",\"labels\":[{\"name\":\"intent-target\"}]}]";

        var result = GitHubCliJsonBoundary.ExtractJsonArray(stdout, Call);

        Assert.True(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.Valid, result.Classification);
        Assert.Equal(stdout, result.Json);
    }

    [Fact]
    public void ExtractJsonArray_EmptyArray_Succeeds()
    {
        var result = GitHubCliJsonBoundary.ExtractJsonArray("[]", Call);

        Assert.True(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.Valid, result.Classification);
        Assert.Equal("[]", result.Json);
    }

    [Fact]
    public void ExtractJsonArray_LeadingBomAndWhitespace_IsNormalizedAndSucceeds()
    {
        // PowerShell / native-command capture can prepend a UTF-8 BOM and
        // surrounding whitespace — both are legitimate encoding artifacts, not
        // contamination, so valid JSON still parses.
        const string stdout = "\uFEFF\r\n  [{\"number\":78}]  \r\n";

        var result = GitHubCliJsonBoundary.ExtractJsonArray(stdout, Call);

        Assert.True(result.Succeeded);
        Assert.Equal("[{\"number\":78}]", result.Json);
    }

    [Fact]
    public void ExtractJsonArray_TrailingUpdateNotice_RefusesWithStructuredDiagnostic()
    {
        // Reproduces the JTJ_Estivo failure: a valid array followed by a gh
        // update notice on stdout. The parser fails at the 'u' after the
        // closing bracket — must refuse, not throw a raw exception.
        const string stdout =
            "[{\"number\":78}]update available: a new release of gh (2.40.0) is available";

        var result = GitHubCliJsonBoundary.ExtractJsonArray(stdout, Call);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.GithubJsonInvalid, result.Classification);
        Assert.Contains(Call, result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("worker next-action --github-only", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Equal("inspect-gh-output-and-retry", result.RecommendedAction);
    }

    [Fact]
    public void ExtractJsonArray_MalformedJson_ReportsByteAndLineLocation()
    {
        const string stdout = "[{\"number\":78,}]"; // trailing comma -> invalid

        var result = GitHubCliJsonBoundary.ExtractJsonArray(stdout, Call);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.GithubJsonInvalid, result.Classification);
        Assert.NotNull(result.ErrorByteOffset);
        Assert.NotNull(result.ErrorLineNumber);
        Assert.Contains("byte", result.DiagnosticMessage, StringComparison.Ordinal);
        Assert.Contains("line", result.DiagnosticMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractJsonArray_LeadingWarningLine_Refuses()
    {
        const string stdout = "[WARN] gh: rate limit nearly exhausted\n[{\"number\":78}]";

        var result = GitHubCliJsonBoundary.ExtractJsonArray(stdout, Call);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.GithubJsonInvalid, result.Classification);
    }

    [Fact]
    public void ExtractJsonArray_NonArrayJson_Refuses()
    {
        // A bare object is valid JSON but not the expected array shape.
        var result = GitHubCliJsonBoundary.ExtractJsonArray("{\"message\":\"Not Found\"}", Call);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.GithubJsonInvalid, result.Classification);
    }

    [Fact]
    public void ExtractJsonArray_EmptyStdout_Refuses()
    {
        var result = GitHubCliJsonBoundary.ExtractJsonArray("   \r\n  ", Call);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.GithubJsonInvalid, result.Classification);
        Assert.Contains("empty", result.DiagnosticMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractJsonArray_RequiresCallDescription()
    {
        Assert.Throws<ArgumentException>(
            () => GitHubCliJsonBoundary.ExtractJsonArray("[]", "  "));
    }

    [Theory]
    [InlineData("gh: To get started with GitHub CLI, please run: gh auth login")]
    [InlineData("HTTP 401: Bad credentials")]
    [InlineData("error: requires authentication")]
    public void ClassifyProcessFailure_AuthMarkers_AreAuthFailed(string stderr)
    {
        Assert.Equal(
            GitHubCliJsonBoundary.Classifications.GithubAuthFailed,
            GitHubCliJsonBoundary.ClassifyProcessFailure(stderr, stdout: string.Empty));
    }

    [Fact]
    public void ClassifyProcessFailure_GenericError_IsCommandFailed()
    {
        Assert.Equal(
            GitHubCliJsonBoundary.Classifications.GithubCommandFailed,
            GitHubCliJsonBoundary.ClassifyProcessFailure(
                stderr: "could not resolve to a Repository with the name 'owner/repo'",
                stdout: string.Empty));
    }

    [Fact]
    public void SanitizePreview_RedactsTokensAndCollapsesWhitespace()
    {
        const string raw = "auth failed\n  token ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 used";

        var preview = GitHubCliJsonBoundary.SanitizePreview(raw);

        Assert.DoesNotContain("ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", preview, StringComparison.Ordinal);
        Assert.Contains("***redacted***", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizePreview_RedactsBearerToken()
    {
        var preview = GitHubCliJsonBoundary.SanitizePreview("Authorization: Bearer abc.def.ghi-123");

        Assert.DoesNotContain("abc.def.ghi-123", preview, StringComparison.Ordinal);
        Assert.Contains("***redacted***", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizePreview_TruncatesLongOutput()
    {
        var preview = GitHubCliJsonBoundary.SanitizePreview(new string('x', 1000), maxLength: 50);

        Assert.Contains("(truncated)", preview, StringComparison.Ordinal);
        Assert.True(preview.Length < 1000);
    }
}
