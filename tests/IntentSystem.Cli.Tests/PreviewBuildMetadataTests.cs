using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G401: unit coverage for <see cref="PreviewBuildMetadata"/> — parsing
/// OSS preview assembly attributes and the version trailer format.
/// </summary>
public sealed class PreviewBuildMetadataTests
{
    private static readonly DateTimeOffset SampleTimestamp =
        new DateTimeOffset(2026, 5, 24, 10, 0, 0, TimeSpan.Zero);

    // -----------------------------------------------------------------
    // TryParse
    // -----------------------------------------------------------------

    [Fact]
    public void TryParse_ValidAttributes_ReturnsMetadata()
    {
        var attrs = new Dictionary<string, string?>
        {
            ["PreviewChannel"] = "preview",
            ["PreviewBuildTimestamp"] = "2026-05-24T10:00:00Z",
            ["PreviewSourceCommit"] = "abc1234",
        };

        var meta = PreviewBuildMetadata.TryParse(attrs);

        Assert.NotNull(meta);
        Assert.Equal("preview", meta.Channel);
        Assert.Equal(SampleTimestamp, meta.BuildTimestamp);
        Assert.Equal("abc1234", meta.SourceCommit);
    }

    [Fact]
    public void TryParse_MissingChannel_ReturnsNull()
    {
        var attrs = new Dictionary<string, string?>
        {
            ["PreviewBuildTimestamp"] = "2026-05-24T10:00:00Z",
            ["PreviewSourceCommit"] = "abc1234",
        };

        Assert.Null(PreviewBuildMetadata.TryParse(attrs));
    }

    [Fact]
    public void TryParse_BlankChannel_ReturnsNull()
    {
        var attrs = new Dictionary<string, string?>
        {
            ["PreviewChannel"] = "   ",
            ["PreviewBuildTimestamp"] = "2026-05-24T10:00:00Z",
        };

        Assert.Null(PreviewBuildMetadata.TryParse(attrs));
    }

    [Fact]
    public void TryParse_InvalidTimestamp_ReturnsNull()
    {
        var attrs = new Dictionary<string, string?>
        {
            ["PreviewChannel"] = "preview",
            ["PreviewBuildTimestamp"] = "not-a-timestamp",
        };

        Assert.Null(PreviewBuildMetadata.TryParse(attrs));
    }

    [Fact]
    public void TryParse_EmptyAttributes_ReturnsNull()
    {
        Assert.Null(PreviewBuildMetadata.TryParse(new Dictionary<string, string?>()));
    }

    [Fact]
    public void TryParse_MissingSourceCommit_StillReturnsMetadata()
    {
        var attrs = new Dictionary<string, string?>
        {
            ["PreviewChannel"] = "preview",
            ["PreviewBuildTimestamp"] = "2026-05-24T10:00:00Z",
            // PreviewSourceCommit absent
        };

        var meta = PreviewBuildMetadata.TryParse(attrs);

        Assert.NotNull(meta);
        Assert.Equal(string.Empty, meta.SourceCommit);
    }

    // -----------------------------------------------------------------
    // ToVersionTrailer
    // -----------------------------------------------------------------

    [Fact]
    public void ToVersionTrailer_WithCommit_IncludesChannelBuiltCommit()
    {
        var meta = new PreviewBuildMetadata
        {
            Channel = PreviewBuildMetadata.ChannelPreview,
            BuildTimestamp = SampleTimestamp,
            SourceCommit = "abc1234",
        };

        var trailer = meta.ToVersionTrailer();

        Assert.Equal("channel=preview built=2026-05-24T10:00:00Z commit=abc1234", trailer);
    }

    [Fact]
    public void ToVersionTrailer_WithoutCommit_OmitsCommitSegment()
    {
        var meta = new PreviewBuildMetadata
        {
            Channel = PreviewBuildMetadata.ChannelPreview,
            BuildTimestamp = SampleTimestamp,
            SourceCommit = string.Empty,
        };

        var trailer = meta.ToVersionTrailer();

        Assert.Equal("channel=preview built=2026-05-24T10:00:00Z", trailer);
    }

    [Fact]
    public void ToVersionTrailer_DoesNotContainPrivatePreview()
    {
        var meta = new PreviewBuildMetadata
        {
            Channel = PreviewBuildMetadata.ChannelPreview,
            BuildTimestamp = SampleTimestamp,
            SourceCommit = "abc1234",
        };

        var trailer = meta.ToVersionTrailer();

        Assert.DoesNotContain("private-preview", trailer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToVersionTrailer_DoesNotContainExpires()
    {
        var meta = new PreviewBuildMetadata
        {
            Channel = PreviewBuildMetadata.ChannelPreview,
            BuildTimestamp = SampleTimestamp,
            SourceCommit = "abc1234",
        };

        var trailer = meta.ToVersionTrailer();

        Assert.DoesNotContain("expires", trailer, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------
    // Attribute name constants
    // -----------------------------------------------------------------

    [Fact]
    public void AttributeConstants_HaveExpectedValues()
    {
        Assert.Equal("PreviewChannel", PreviewBuildMetadata.ChannelAttributeName);
        Assert.Equal("PreviewBuildTimestamp", PreviewBuildMetadata.BuildTimestampAttributeName);
        Assert.Equal("PreviewSourceCommit", PreviewBuildMetadata.SourceCommitAttributeName);
        Assert.Equal("preview", PreviewBuildMetadata.ChannelPreview);
    }
}
