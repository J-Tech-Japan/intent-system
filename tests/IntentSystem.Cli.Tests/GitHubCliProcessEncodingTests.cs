using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G484: the shared gh subprocess decoding contract. gh emits UTF-8 JSON; the
/// runner must decode it as UTF-8 regardless of the ambient Windows console
/// code page (cp932). These tests pin the decoding contract at the unit level
/// (no real subprocess): UTF-8 bytes carrying a Japanese issue title must
/// survive through the shared encoding and the JSON boundary, while decoding
/// the same bytes with a non-UTF-8 code page corrupts them — proving the
/// encoding choice is what fixes the bug.
/// </summary>
public sealed class ProcessOutputEncodingTests
{
    // gh issue list --json number,title,labels,url shape, with a Japanese title.
    private const string JapaneseTitle = "G484 日本語タイトルのテスト（cp932 回帰）";

    private static string JapaneseIssueListJson =>
        "[{\"number\":1069,\"title\":\"" + JapaneseTitle + "\",\"labels\":[],\"url\":\"https://github.com/J-Tech-Japan/intent-system/issues/1069\"}]";

    [Fact]
    public void Utf8NoBom_IsUtf8WithoutPreamble()
    {
        Assert.Equal("Unicode (UTF-8)", ProcessOutputEncoding.Utf8NoBom.EncodingName);
        // No BOM: gh JSON must not be prefixed with a byte-order mark on write.
        Assert.Empty(ProcessOutputEncoding.Utf8NoBom.GetPreamble());
    }

    [Fact]
    public void Utf8Decode_OfJapaneseGhJson_RoundTripsThroughBoundaryAndParser()
    {
        // What gh writes to the redirected stdout pipe: UTF-8 bytes.
        var ghStdoutBytes = Encoding.UTF8.GetBytes(JapaneseIssueListJson);

        // What the runner does (post-G484): decode with the pinned UTF-8 encoding.
        var decoded = ProcessOutputEncoding.Utf8NoBom.GetString(ghStdoutBytes);

        var extraction = GitHubCliJsonBoundary.ExtractJsonArray(decoded, "`gh issue list` for J-Tech-Japan/intent-system");
        Assert.True(extraction.Succeeded);
        Assert.Equal(GitHubCliJsonBoundary.Classifications.Valid, extraction.Classification);

        using var document = JsonDocument.Parse(extraction.Json);
        var title = document.RootElement[0].GetProperty("title").GetString();
        Assert.Equal(JapaneseTitle, title); // Japanese text intact end-to-end.
    }

    [Fact]
    public void NonUtf8Decode_OfJapaneseGhJson_CorruptsTitle_ProvingEncodingMatters()
    {
        var ghStdoutBytes = Encoding.UTF8.GetBytes(JapaneseIssueListJson);

        // Simulate the pre-G484 Windows cp932 path with a non-UTF-8 single-byte
        // code page (Latin1 is always available and, like cp932, mis-decodes the
        // multi-byte UTF-8 sequences in the Japanese title).
        var misdecoded = Encoding.Latin1.GetString(ghStdoutBytes);

        Assert.DoesNotContain(JapaneseTitle, misdecoded, StringComparison.Ordinal);

        // The mis-decoded payload either fails JSON parse or no longer carries
        // the original Japanese title — i.e. selection would break, which is the
        // reported bug. The UTF-8 path above is what keeps it valid.
        var extraction = GitHubCliJsonBoundary.ExtractJsonArray(misdecoded, "`gh issue list` for J-Tech-Japan/intent-system");
        var titleSurvived = extraction.Succeeded
            && JsonDocument.Parse(extraction.Json).RootElement[0].GetProperty("title").GetString() == JapaneseTitle;
        Assert.False(titleSurvived);
    }

    [Fact]
    public void Utf8Decode_OfAsciiGhJson_IsUnchanged_NoRegression()
    {
        const string asciiJson =
            "[{\"number\":1,\"title\":\"ASCII only title\",\"labels\":[],\"url\":\"https://example/1\"}]";
        var bytes = Encoding.UTF8.GetBytes(asciiJson);

        var decoded = ProcessOutputEncoding.Utf8NoBom.GetString(bytes);
        Assert.Equal(asciiJson, decoded);

        var extraction = GitHubCliJsonBoundary.ExtractJsonArray(decoded, "`gh issue list` for owner/repo");
        Assert.True(extraction.Succeeded);
    }
}
