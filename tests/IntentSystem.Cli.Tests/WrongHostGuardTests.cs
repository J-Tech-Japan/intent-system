using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class WrongHostGuardTests
{
    [Fact]
    public void Check_BoundHostMatchesHttpsRemote_ReturnsOk()
    {
        var result = WrongHostGuard.Check(
            "trace-forge-poc",
            "J-Tech-Japan/TraceForgeHost",
            "https://github.com/J-Tech-Japan/TraceForgeHost.git");

        Assert.Equal("ok", result.Status);
        Assert.Equal("J-Tech-Japan/TraceForgeHost", result.BoundHostRepo);
        Assert.Equal("J-Tech-Japan/TraceForgeHost", result.ObservedHostRepo);
        Assert.Empty(result.RemediationSteps);
    }

    [Fact]
    public void Check_BoundHostMatchesSshRemote_ReturnsOk()
    {
        var result = WrongHostGuard.Check(
            "trace-forge-poc",
            "J-Tech-Japan/TraceForgeHost",
            "git@github.com:J-Tech-Japan/TraceForgeHost.git");

        Assert.Equal("ok", result.Status);
        Assert.Equal("J-Tech-Japan/TraceForgeHost", result.ObservedHostRepo);
    }

    [Fact]
    public void Check_BoundHostDiffersFromObservedRemote_ReturnsWrongHostMismatch()
    {
        var result = WrongHostGuard.Check(
            "trace-forge-poc",
            "J-Tech-Japan/TraceForgeHost",
            "https://github.com/J-Tech-Japan/MyIntentHost.git");

        Assert.Equal("wrong-host", result.Status);
        Assert.Equal("J-Tech-Japan/TraceForgeHost", result.BoundHostRepo);
        Assert.Equal("J-Tech-Japan/MyIntentHost", result.ObservedHostRepo);
        Assert.Contains("Wrong-host operation detected", result.Summary, StringComparison.Ordinal);
        Assert.Contains("trace-forge-poc", result.Summary, StringComparison.Ordinal);
        Assert.Contains(result.RemediationSteps, s => s.Contains("`cd` to the canonical host repo", StringComparison.Ordinal));
        Assert.Contains(result.RemediationSteps, s => s.Contains("operator must explicitly migrate", StringComparison.Ordinal));
        Assert.Contains(result.RemediationSteps, s => s.Contains("Never silently rewrite host-binding.toml", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_NoBoundHost_ReturnsUnboundWithBootstrapHint()
    {
        var result = WrongHostGuard.Check(
            "trace-forge-poc",
            null,
            "https://github.com/J-Tech-Japan/TraceForgeHost.git");

        Assert.Equal("unbound", result.Status);
        Assert.Null(result.BoundHostRepo);
        Assert.Contains("intent init", result.RemediationSteps[0], StringComparison.Ordinal);
        Assert.Contains("--host-repo", result.RemediationSteps[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Check_BoundHostButObservedRemoteUnparseable_ReturnsUnboundWithCaptureHint()
    {
        var result = WrongHostGuard.Check(
            "trace-forge-poc",
            "J-Tech-Japan/TraceForgeHost",
            "/local/only/path");

        Assert.Equal("unbound", result.Status);
        Assert.Equal("J-Tech-Japan/TraceForgeHost", result.BoundHostRepo);
        Assert.Null(result.ObservedHostRepo);
        Assert.Contains(result.RemediationSteps, s => s.Contains("git -C", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_BothEmpty_ReturnsUnbound()
    {
        var result = WrongHostGuard.Check("trace-forge-poc", string.Empty, string.Empty);

        Assert.Equal("unbound", result.Status);
        Assert.Null(result.BoundHostRepo);
    }

    [Fact]
    public void Check_CaseInsensitiveOwnerRepoMatch()
    {
        var result = WrongHostGuard.Check(
            "trace-forge-poc",
            "J-Tech-Japan/TraceForgeHost",
            "https://github.com/j-tech-japan/traceforgehost");

        Assert.Equal("ok", result.Status);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("git@github.com:owner/repo", "owner/repo")]
    [InlineData("owner/repo", "owner/repo")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("not a url", null)]
    [InlineData("/local/only/path", null)]
    public void ExtractOwnerRepo_HandlesCommonShapes(string? input, string? expected)
    {
        Assert.Equal(expected, WrongHostGuard.ExtractOwnerRepo(input));
    }
}
