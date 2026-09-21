using System.Text;
using System.Text.Json;
using Xunit.Abstractions;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class IssueBodySizeLimitsTests
{
    [Fact]
    public void Constants_PinTheSharedValues()
    {
        Assert.Equal(65536, IssueBodySizeLimits.HardLimitBytes);
        Assert.Equal(58000, IssueBodySizeLimits.WarningThresholdBytes);
    }

    [Theory]
    [InlineData(57999, "Normal")]
    [InlineData(58000, "Warning")]
    [InlineData(58001, "Warning")]
    [InlineData(65535, "Warning")]
    [InlineData(65536, "Warning")]
    [InlineData(65537, "OverLimit")]
    public void GetBand_IsPureAndInclusive(int bodyBytes, string expected)
    {
        Assert.Equal(expected, IssueBodySizeLimits.GetBand(bodyBytes).ToString());
    }
}

public sealed class IssueBodyPayloadOverheadTests(ITestOutputHelper output)
{
    [Fact]
    public void EqualBodyByteCountsCanProduceDifferentJsonPayloadByteCounts()
    {
        const string title = "G847 payload measurement";
        var plainBody = new string('A', 60_000);
        var escapedBody = string.Concat(Enumerable.Repeat("\\\"\n", 20_000));
        var plainBodyBytes = Encoding.UTF8.GetByteCount(plainBody);
        var escapedBodyBytes = Encoding.UTF8.GetByteCount(escapedBody);
        var plainPayload = JsonSerializer.Serialize(new { title, body = plainBody });
        var escapedPayload = JsonSerializer.Serialize(new { title, body = escapedBody });
        var plainPayloadBytes = Encoding.UTF8.GetByteCount(plainPayload);
        var escapedPayloadBytes = Encoding.UTF8.GetByteCount(escapedPayload);

        Assert.Equal(plainBodyBytes, escapedBodyBytes);
        Assert.True(plainPayloadBytes > plainBodyBytes);
        Assert.True(escapedPayloadBytes > escapedBodyBytes);
        Assert.NotEqual(plainPayloadBytes, escapedPayloadBytes);

        output.WriteLine($"plain_body_bytes={plainBodyBytes}; plain_payload_bytes={plainPayloadBytes}");
        output.WriteLine($"escaped_body_bytes={escapedBodyBytes}; escaped_payload_bytes={escapedPayloadBytes}");
    }
}
