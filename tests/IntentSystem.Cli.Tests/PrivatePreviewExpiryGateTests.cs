using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G368: covers the runtime expiry gate that fails closed when the
/// running binary is a CI-packed private-preview artifact whose
/// embedded <c>expires_at</c> has passed. All tests reset the static
/// override seams in <see cref="Dispose"/> so the gate is back to its
/// production read-from-running-assembly contract for subsequent
/// tests.
/// </summary>
public sealed class PrivatePreviewExpiryGateTests : IDisposable
{
    public PrivatePreviewExpiryGateTests()
    {
        PrivatePreviewExpiryGate.OverrideAssembly = null;
        PrivatePreviewExpiryGate.OverrideMetadata = null;
        PrivatePreviewExpiryGate.OverrideNow = null;
    }

    public void Dispose()
    {
        PrivatePreviewExpiryGate.OverrideAssembly = null;
        PrivatePreviewExpiryGate.OverrideMetadata = null;
        PrivatePreviewExpiryGate.OverrideNow = null;
    }

    [Fact]
    public void Check_NoMetadata_ReturnsOkAndEmitsNothing()
    {
        // Source-build path: the running test assembly has no
        // PrivatePreview AssemblyMetadata entries unless the CI pack
        // workflow built it, so leaving the overrides null exercises
        // the source-build pass-through.
        using var writer = new StringWriter();

        var result = PrivatePreviewExpiryGate.Check(writer);

        Assert.Equal(PrivatePreviewExpiryDecision.Ok, result);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Check_PreviewBuildWithFutureExpiry_ReturnsOkAndEmitsNothing()
    {
        PrivatePreviewExpiryGate.OverrideMetadata = new PrivatePreviewMetadata
        {
            Channel = PrivatePreviewMetadata.ChannelPrivatePreview,
            BuildTimestamp = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
            SourceCommit = "abc1234",
        };
        PrivatePreviewExpiryGate.OverrideNow = new DateTimeOffset(2026, 5, 25, 0, 0, 0, TimeSpan.Zero);
        using var writer = new StringWriter();

        var result = PrivatePreviewExpiryGate.Check(writer);

        Assert.Equal(PrivatePreviewExpiryDecision.Ok, result);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Check_PreviewBuildAtBoundaryDate_ReturnsOk()
    {
        // The boundary minute itself is still considered "in-window"
        // (consistent with PrivatePreviewMetadata.IsExpired). A small
        // clock skew between CI and the operator's host must not flip
        // a fresh artifact to expired right at the cutoff.
        PrivatePreviewExpiryGate.OverrideMetadata = new PrivatePreviewMetadata
        {
            Channel = PrivatePreviewMetadata.ChannelPrivatePreview,
            BuildTimestamp = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
            SourceCommit = "abc1234",
        };
        PrivatePreviewExpiryGate.OverrideNow = new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero);
        using var writer = new StringWriter();

        var result = PrivatePreviewExpiryGate.Check(writer);

        Assert.Equal(PrivatePreviewExpiryDecision.Ok, result);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void Check_PreviewBuildPastExpiry_ReturnsExpiredAndEmitsStructuredMessage()
    {
        PrivatePreviewExpiryGate.OverrideMetadata = new PrivatePreviewMetadata
        {
            Channel = PrivatePreviewMetadata.ChannelPrivatePreview,
            BuildTimestamp = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            SourceCommit = "f6cbf65",
        };
        PrivatePreviewExpiryGate.OverrideNow = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);
        using var writer = new StringWriter();

        var result = PrivatePreviewExpiryGate.Check(writer);

        Assert.Equal(PrivatePreviewExpiryDecision.Expired, result);
        var emitted = writer.ToString();
        Assert.Contains("private-preview artifact expired", emitted, StringComparison.Ordinal);
        Assert.Contains("expired_at: 2026-05-15T12:00:00Z", emitted, StringComparison.Ordinal);
        Assert.Contains("built: 2026-05-01T12:00:00Z", emitted, StringComparison.Ordinal);
        Assert.Contains("commit: f6cbf65", emitted, StringComparison.Ordinal);
        Assert.Contains("dotnet tool update --global --add-source", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_PreviewBuildOneSecondAfterExpiry_ReturnsExpired()
    {
        // Deterministic boundary: one second after expires_at must
        // flip to Expired so a stale build cannot linger by virtue of
        // sub-minute rounding.
        PrivatePreviewExpiryGate.OverrideMetadata = new PrivatePreviewMetadata
        {
            Channel = PrivatePreviewMetadata.ChannelPrivatePreview,
            BuildTimestamp = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            SourceCommit = "abc1234",
        };
        PrivatePreviewExpiryGate.OverrideNow = new DateTimeOffset(2026, 5, 15, 12, 0, 1, TimeSpan.Zero);
        using var writer = new StringWriter();

        var result = PrivatePreviewExpiryGate.Check(writer);

        Assert.Equal(PrivatePreviewExpiryDecision.Expired, result);
    }

    [Fact]
    public void BuildExpiredMessage_FormatsAllFields()
    {
        var meta = new PrivatePreviewMetadata
        {
            Channel = "private-preview",
            BuildTimestamp = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 5, 15, 12, 0, 0, TimeSpan.Zero),
            SourceCommit = "f6cbf65",
        };
        var now = new DateTimeOffset(2026, 5, 19, 6, 30, 0, TimeSpan.Zero);

        var message = PrivatePreviewExpiryGate.BuildExpiredMessage(meta, now);

        Assert.Contains("intent-cli: private-preview artifact expired on 2026-05-15T12:00:00Z (now 2026-05-19T06:30:00Z).", message, StringComparison.Ordinal);
        Assert.Contains("channel: private-preview", message, StringComparison.Ordinal);
        Assert.Contains("built: 2026-05-01T12:00:00Z", message, StringComparison.Ordinal);
        Assert.Contains("expired_at: 2026-05-15T12:00:00Z", message, StringComparison.Ordinal);
        Assert.Contains("commit: f6cbf65", message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildExpiredMessage_BlankCommit_RendersUnknownPlaceholder()
    {
        var meta = new PrivatePreviewMetadata
        {
            Channel = "private-preview",
            BuildTimestamp = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 5, 15, 0, 0, 0, TimeSpan.Zero),
            SourceCommit = "",
        };

        var message = PrivatePreviewExpiryGate.BuildExpiredMessage(meta, new DateTimeOffset(2026, 5, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("commit: <unknown>", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiredExitCode_IsDistinctFromGenericError()
    {
        // The reserved 78 exit code lets operators filter
        // expired-preview exits without inspecting message text.
        Assert.NotEqual(0, PrivatePreviewExpiryGate.ExpiredExitCode);
        Assert.NotEqual(1, PrivatePreviewExpiryGate.ExpiredExitCode);
        Assert.Equal(78, PrivatePreviewExpiryGate.ExpiredExitCode);
    }
}
