using System.Text;

namespace IntentSystem.Cli.Commands;

internal static class StrictUtf8FileReader
{
    private static readonly UTF8Encoding Encoding = new UTF8Encoding(false, throwOnInvalidBytes: true);

    internal static string ReadText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = File.ReadAllBytes(path);
        return Decode(bytes, path);
    }

    /// <summary>
    /// Strictly decodes an already captured byte snapshot. A leading BOM is
    /// stripped from the returned text for the existing text-reader contract;
    /// the caller's byte array is never changed.
    /// </summary>
    internal static string Decode(byte[] bytes, string path)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string text;
        try
        {
            text = Encoding.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new StrictUtf8FileReadException(path, exception.Index, exception);
        }

        return text.Length > 0 && text[0] == '\uFEFF'
            ? text[1..]
            : text;
    }

    /// <summary>
    /// Reads and strictly validates a file while retaining the exact bytes.
    /// File-body transmission routes use this instead of <see cref="ReadText"/>
    /// because a BOM is part of the file handed to <c>gh</c> on those routes.
    /// </summary>
    internal static byte[] ReadBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var bytes = File.ReadAllBytes(path);
        _ = Decode(bytes, path);
        return bytes;
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
