using System.Collections.Generic;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G367: covers the pure helpers behind the CI private-preview pack
/// workflow so the 14-day expiry contract and metadata parsing can be
/// asserted without spinning up a real GitHub Actions run.
/// </summary>
public sealed class PrivatePreviewMetadataTests
{
    [Fact]
    public void ComputeExpiresAt_AddsFourteenDayWindow()
    {
        var built = new DateTimeOffset(2026, 5, 19, 12, 34, 56, TimeSpan.Zero);
        var expires = PrivatePreviewMetadata.ComputeExpiresAt(built);
        Assert.Equal(TimeSpan.FromDays(14), expires - built);
        Assert.Equal(new DateTimeOffset(2026, 6, 2, 12, 34, 56, TimeSpan.Zero), expires);
    }

    [Fact]
    public void PreviewWindow_Is14Days()
    {
        // Guard the published constant so the workflow and runtime
        // never drift out of sync silently.
        Assert.Equal(TimeSpan.FromDays(14), PrivatePreviewMetadata.PreviewWindow);
    }

    [Fact]
    public void TryParse_ReadsAllAttributes()
    {
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [PrivatePreviewMetadata.ChannelAttributeName] = "private-preview",
            [PrivatePreviewMetadata.BuildTimestampAttributeName] = "2026-05-19T12:34:56Z",
            [PrivatePreviewMetadata.ExpiresAtAttributeName] = "2026-06-02T12:34:56Z",
            [PrivatePreviewMetadata.SourceCommitAttributeName] = "f6cbf65b1234567890",
        };

        var meta = PrivatePreviewMetadata.TryParse(attrs);

        Assert.NotNull(meta);
        Assert.Equal("private-preview", meta!.Channel);
        Assert.Equal(new DateTimeOffset(2026, 5, 19, 12, 34, 56, TimeSpan.Zero), meta.BuildTimestamp);
        Assert.Equal(new DateTimeOffset(2026, 6, 2, 12, 34, 56, TimeSpan.Zero), meta.ExpiresAt);
        Assert.Equal("f6cbf65b1234567890", meta.SourceCommit);
    }

    [Fact]
    public void TryParse_WithoutChannel_ReturnsNull()
    {
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [PrivatePreviewMetadata.BuildTimestampAttributeName] = "2026-05-19T12:34:56Z",
        };

        Assert.Null(PrivatePreviewMetadata.TryParse(attrs));
    }

    [Fact]
    public void TryParse_WithBlankChannel_ReturnsNull()
    {
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [PrivatePreviewMetadata.ChannelAttributeName] = "   ",
            [PrivatePreviewMetadata.BuildTimestampAttributeName] = "2026-05-19T12:34:56Z",
        };

        Assert.Null(PrivatePreviewMetadata.TryParse(attrs));
    }

    [Fact]
    public void TryParse_WithMissingExpiry_RecomputesFromBuildTimestamp()
    {
        // Defensive lane: if CI ever forgets to pass the explicit
        // expiry property, the runtime still reports a sane 14-day
        // window relative to the embedded build timestamp.
        var attrs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [PrivatePreviewMetadata.ChannelAttributeName] = "private-preview",
            [PrivatePreviewMetadata.BuildTimestampAttributeName] = "2026-05-19T00:00:00Z",
            [PrivatePreviewMetadata.SourceCommitAttributeName] = "abc1234",
        };

        var meta = PrivatePreviewMetadata.TryParse(attrs);

        Assert.NotNull(meta);
        Assert.Equal(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), meta!.ExpiresAt);
    }

    [Fact]
    public void IsExpired_ComparesAgainstUtcExpiry()
    {
        var meta = new PrivatePreviewMetadata
        {
            Channel = "private-preview",
            BuildTimestamp = new DateTimeOffset(2026, 5, 19, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
            SourceCommit = "abc1234",
        };

        Assert.False(meta.IsExpired(new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(meta.IsExpired(new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero)));
        Assert.True(meta.IsExpired(new DateTimeOffset(2026, 6, 2, 0, 0, 1, TimeSpan.Zero)));
        Assert.True(meta.IsExpired(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ToVersionTrailer_FormatsAsIsoUtcWithCommit()
    {
        var meta = new PrivatePreviewMetadata
        {
            Channel = "private-preview",
            BuildTimestamp = new DateTimeOffset(2026, 5, 19, 12, 34, 56, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 6, 2, 12, 34, 56, TimeSpan.Zero),
            SourceCommit = "f6cbf65",
        };

        Assert.Equal(
            "channel=private-preview built=2026-05-19T12:34:56Z expires=2026-06-02T12:34:56Z commit=f6cbf65",
            meta.ToVersionTrailer());
    }

    [Fact]
    public void ToVersionTrailer_BlankCommit_OmitsCommitSegment()
    {
        var meta = new PrivatePreviewMetadata
        {
            Channel = "private-preview",
            BuildTimestamp = new DateTimeOffset(2026, 5, 19, 12, 34, 56, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 6, 2, 12, 34, 56, TimeSpan.Zero),
            SourceCommit = "",
        };

        Assert.Equal(
            "channel=private-preview built=2026-05-19T12:34:56Z expires=2026-06-02T12:34:56Z",
            meta.ToVersionTrailer());
    }

    [Fact]
    public void Read_FromAssemblyWithoutMetadata_ReturnsNull()
    {
        // The test assembly is an ordinary source build with no
        // private-preview MSBuild properties, so reading its
        // AssemblyMetadata returns null — matching the contract that
        // local source builds carry no expiry restriction.
        var meta = PrivatePreviewMetadata.Read(typeof(PrivatePreviewMetadataTests).Assembly);
        Assert.Null(meta);
    }
}
