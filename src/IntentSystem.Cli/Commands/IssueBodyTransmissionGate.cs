namespace IntentSystem.Cli.Commands;

internal enum IssueBodyTransmissionFailure
{
    None,
    TooLarge,
    InvalidUtf8,
}

internal abstract record IssueBodyTransmissionResult
{
    internal abstract int ByteCount { get; }

    internal bool IsAccepted => this is IssueBodyTransmissionAccepted;

    internal virtual IssueBodyTransmissionFailure Failure => IssueBodyTransmissionFailure.None;

    internal virtual int? InvalidByteOffset => null;
}

internal sealed record IssueBodyTransmissionAccepted(byte[] Bytes)
    : IssueBodyTransmissionResult
{
    internal override int ByteCount => Bytes.Length;

    /// <summary>
    /// Stages the exact validated snapshot. Refusals have no Stage method and
    /// therefore cannot yield a transmission path.
    /// </summary>
    internal IssueBodyStagedBody Stage() => IssueBodyFileStager.Stage(Bytes);
}

internal sealed record IssueBodyTransmissionRefusal(
    IssueBodyTransmissionFailure Kind,
    int CountedBytes,
    int? ByteOffset)
    : IssueBodyTransmissionResult
{
    internal override int ByteCount => CountedBytes;

    internal override IssueBodyTransmissionFailure Failure => Kind;

    internal override int? InvalidByteOffset => ByteOffset;
}

/// <summary>
/// Applies the common order for the file-body transmission gates: strict UTF-8
/// validation first, then the raw-byte hard limit. The byte array is the
/// snapshot that the staging primitive writes, so the count is exact for the
/// file passed to <c>gh</c>.
/// </summary>
internal static class IssueBodyTransmissionGate
{
    internal static IssueBodyTransmissionResult Evaluate(byte[] body, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);

        try
        {
            _ = StrictUtf8FileReader.Decode(body, displayPath);
        }
        catch (StrictUtf8FileReadException exception)
        {
            return new IssueBodyTransmissionRefusal(
                IssueBodyTransmissionFailure.InvalidUtf8,
                body.Length,
                exception.ByteOffset);
        }

        if (body.Length > IssueBodySizeLimits.HardLimitBytes)
        {
            return new IssueBodyTransmissionRefusal(
                IssueBodyTransmissionFailure.TooLarge,
                body.Length,
                ByteOffset: null);
        }

        return new IssueBodyTransmissionAccepted(body);
    }
}
