namespace IntentSystem.Cli.Commands;

internal enum IssueBodySizeBand
{
    Normal,
    Warning,
    OverLimit
}

/// <summary>
/// Intent-cli's own conservative 65,536-byte limit covers submitted body content counted in UTF-8 bytes.
/// It is not a bound on bytes on the wire; GitHub's actual boundary, its unit, and its
/// treatment of a JSON payload were not verified.
/// </summary>
internal static class IssueBodySizeLimits
{
    internal const int HardLimitBytes = 65_536;

    /// <summary>
    /// 58,000 is a budget choice: the self-imposed drafting budget held by hand
    /// since 2026-09-16, not a GitHub limit.
    /// </summary>
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
