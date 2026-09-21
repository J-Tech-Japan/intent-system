using System.Text;

namespace IntentSystem.Cli.Commands;

internal static class IssueBodyTextDecoder
{
    internal static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        using var reader = new StreamReader(
            new MemoryStream(bytes),
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
