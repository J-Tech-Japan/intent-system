using System.Text;

namespace IntentSystem.Cli.Commands;

internal static class StrictUtf8FileReader
{
    private static readonly UTF8Encoding Encoding = new UTF8Encoding(false, throwOnInvalidBytes: true);

    internal static string ReadText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = File.ReadAllBytes(path);
        try
        {
            var text = Encoding.GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF'
                ? text[1..]
                : text;
        }
        catch (DecoderFallbackException exception)
        {
            throw new StrictUtf8FileReadException(path, exception.Index, exception);
        }
    }
}

internal sealed class StrictUtf8FileReadException : InvalidOperationException
{
    internal StrictUtf8FileReadException(string filePath, int byteOffset, Exception innerException)
        : base($"Issue body is not valid UTF-8 at byte offset {byteOffset} in {filePath}.", innerException)
    {
        FilePath = filePath;
        ByteOffset = byteOffset;
    }

    internal string FilePath { get; }

    internal int ByteOffset { get; }
}
