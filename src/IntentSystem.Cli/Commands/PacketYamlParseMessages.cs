namespace IntentSystem.Cli.Commands;

/// <summary>G841: shared message composition for packet parse and read failures.</summary>
internal static class PacketYamlParseMessages
{
    public static string WarningText(string packetPath, string parserMessage) =>
        $"packet.yaml at '{packetPath}' could not be parsed: {parserMessage}";

    public static string ReadWarningText(string packetPath, string exceptionMessage) =>
        $"packet.yaml at '{packetPath}' could not be read: {exceptionMessage}";

    public static string RunsAuditDetail(string packetPath, string parserMessage) =>
        $"packet.yaml unparseable ({packetPath}): {parserMessage}";

    public static string ComposeCrossRuntimeParseDetail(string relativePacketPath, PacketYamlParseError error) =>
        ComposeParseDetail($"packet '{relativePacketPath}' could not be parsed", error);

    public static string ComposePublishFlowParseDetail(string packetPath, PacketYamlParseError error, bool changedAfterFirstRead = false)
    {
        var prefix = changedAfterFirstRead
            ? $"packet '{packetPath}' changed after it was first read and could not be parsed"
            : $"packet '{packetPath}' could not be parsed";
        return ComposeParseDetail(prefix, error);
    }

    public static string ComposePublishFlowReadDetail(string packetPath, string exceptionMessage, bool changedAfterFirstRead = false) =>
        changedAfterFirstRead
            ? $"packet '{packetPath}' changed after it was first read and could not be read: {exceptionMessage}"
            : $"packet '{packetPath}' could not be read: {exceptionMessage}";

    public static string ComposePacketDraftReadDetail(string packetPath, string exceptionMessage) =>
        $"packet '{packetPath}' could not be read: {exceptionMessage}";

    private static string ComposeParseDetail(string prefix, PacketYamlParseError error)
    {
        if (error.Line is int line && error.Column is int column)
        {
            return $"{prefix} at line {line}, column {column}: {error.Message}";
        }

        return $"{prefix}: {error.Message}";
    }
}

/// <summary>
/// G841: records at most one parse-warning per packet path per command run.
/// </summary>
internal sealed class PacketYamlParseWarningTracker
{
    private readonly List<string> _warnings;
    private readonly HashSet<string> _warnedPaths;

    public PacketYamlParseWarningTracker(List<string> warnings)
    {
        _warnings = warnings;
        _warnedPaths = new HashSet<string>(StringComparer.Ordinal);
    }

    public bool TryReadFields(string packetPath, out IReadOnlyDictionary<string, string>? fields)
    {
        fields = null;
        if (!File.Exists(packetPath))
        {
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(packetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (!PacketYamlDocument.TryParse(text, out var document, out var error) || document is null)
        {
            RecordWarning(packetPath, error);
            return false;
        }

        fields = document.Fields;
        return true;
    }

    public void RecordWarning(string packetPath, string parserMessage)
    {
        if (!_warnedPaths.Add(packetPath))
        {
            return;
        }

        _warnings.Add(PacketYamlParseMessages.WarningText(packetPath, parserMessage));
    }

    public void RecordReadWarning(string packetPath, string exceptionMessage)
    {
        if (!_warnedPaths.Add(packetPath))
        {
            return;
        }

        _warnings.Add(PacketYamlParseMessages.ReadWarningText(packetPath, exceptionMessage));
    }
}
