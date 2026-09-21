namespace IntentSystem.Cli.Commands;

internal enum IssueBodySizeBand
{
    Normal,
    Warning,
    OverLimit
}

internal static class IssueBodySizeLimits
{
    internal const int HardLimitBytes = 65_536;
    internal const int WarningThresholdBytes = 58_000;

    internal static IssueBodySizeBand GetBand(int bodyBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyBytes);

        if (bodyBytes > HardLimitBytes)
        {
            return IssueBodySizeBand.OverLimit;
        }

        return bodyBytes >= WarningThresholdBytes
            ? IssueBodySizeBand.Warning
            : IssueBodySizeBand.Normal;
    }
}
