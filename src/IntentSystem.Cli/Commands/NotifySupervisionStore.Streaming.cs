using System.Security.Cryptography;
using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G827: bounded-memory primitives for supervision maintenance. Shrink and
/// archive used to read a JSONL file into one string, which fails outright
/// above the runtime's string limit (~1 GB) and needs several times the file
/// size below it. Everything here streams: memory is bounded by the longest
/// record, not by the file.
///
/// The line semantics are exactly those of the string implementation they
/// replace: UTF-8 with byte-order-mark detection (as
/// <c>File.ReadAllText(path, Utf8NoBom)</c>), lines split on <c>\r\n</c> or
/// <c>\n</c> only (a lone <c>\r</c> stays inside its line), and a file that is
/// empty or ends with <c>\n</c> has a trailing newline.
/// </summary>
internal static partial class NotifySupervisionStore
{
    private const int StreamBufferSize = 1 << 16;

    private static FileStream OpenSequentialRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            StreamBufferSize,
            FileOptions.SequentialScan);

    private static StreamReader OpenSupervisionTextReader(string path) =>
        new(OpenSequentialRead(path), Utf8NoBom, detectEncodingFromByteOrderMarks: true, StreamBufferSize);

    /// <summary>
    /// Whether the decoded text contains <c>\r\n</c> anywhere. The string
    /// implementation chose the output newline from this before splitting, so
    /// the streamed passes need it before they write the first separator.
    /// </summary>
    private static bool TextFileContainsCrLf(string path)
    {
        using var reader = OpenSupervisionTextReader(path);
        var buffer = new char[StreamBufferSize];
        var previousWasCarriageReturn = false;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            var span = buffer.AsSpan(0, read);
            if (previousWasCarriageReturn && span[0] == '\n')
            {
                return true;
            }

            if (span.IndexOf("\r\n".AsSpan(), StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            previousWasCarriageReturn = span[^1] == '\r';
        }

        return false;
    }

    private static string? HashFileOrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = OpenSequentialRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Replaces <paramref name="path"/> with the bytes of
    /// <paramref name="sourcePath"/> by streaming them into a same-directory
    /// temporary file, flushing it to disk, and renaming it over the target —
    /// the byte-for-byte streamed equivalent of
    /// <c>ReplaceAtomically(path, File.ReadAllText(sourcePath))</c> for a
    /// staged file this store wrote. The source is left in place so a pending
    /// transaction can still recover from it.
    /// </summary>
    private static void ReplaceFileAtomicallyFrom(string path, string sourcePath)
    {
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var source = OpenSequentialRead(sourcePath))
            using (var destination = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.WriteThrough))
            {
                source.CopyTo(destination, StreamBufferSize);
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// Reads decoded lines one at a time with the split semantics described on
    /// this partial. <see cref="StreamReader.ReadLine"/> is not used because it
    /// also splits on a lone <c>\r</c>.
    /// </summary>
    private sealed class SupervisionLineReader : IDisposable
    {
        private readonly StreamReader reader;
        private readonly char[] buffer = new char[StreamBufferSize];
        private readonly StringBuilder line = new();
        private int position;
        private int length;
        private bool sawCharacter;
        private bool lastCharacterWasNewline;
        private bool exhausted;

        public SupervisionLineReader(string path)
        {
            reader = OpenSupervisionTextReader(path);
        }

        /// <summary>
        /// The string implementation's <c>trailingNewline</c>: the text was
        /// empty or ended with <c>\n</c>. Meaningful once
        /// <see cref="TryReadLine"/> has returned false.
        /// </summary>
        public bool TrailingNewline => !sawCharacter || lastCharacterWasNewline;

        public bool TryReadLine(out string value)
        {
            value = string.Empty;
            if (exhausted)
            {
                return false;
            }

            while (true)
            {
                if (position == length)
                {
                    length = reader.Read(buffer, 0, buffer.Length);
                    position = 0;
                    if (length == 0)
                    {
                        exhausted = true;
                        if (!sawCharacter || lastCharacterWasNewline)
                        {
                            return false;
                        }

                        // The final segment had no terminator; it is a line.
                        value = line.ToString();
                        line.Clear();
                        return true;
                    }
                }

                sawCharacter = true;
                var span = buffer.AsSpan(position, length - position);
                var newline = span.IndexOf('\n');
                if (newline < 0)
                {
                    line.Append(span);
                    position = length;
                    lastCharacterWasNewline = false;
                    continue;
                }

                line.Append(span[..newline]);
                position += newline + 1;
                lastCharacterWasNewline = true;
                if (line.Length > 0 && line[^1] == '\r')
                {
                    line.Length--;
                }

                value = line.ToString();
                line.Clear();
                return true;
            }
        }

        public void Dispose() => reader.Dispose();
    }

    /// <summary>
    /// A write-only destination that counts and hashes the UTF-8 bytes written
    /// to it and, when staging, also writes them to a file. Counting mode and
    /// staging mode run the same pass, so a dry-run and a write cannot measure
    /// different results.
    /// </summary>
    private sealed class SupervisionTextSink : IDisposable
    {
        private readonly MeasuringStream measuring;
        private readonly StreamWriter writer;
        private readonly FileStream? file;

        private SupervisionTextSink(FileStream? file)
        {
            this.file = file;
            measuring = new MeasuringStream(file);
            writer = new StreamWriter(measuring, Utf8NoBom, StreamBufferSize, leaveOpen: true);
        }

        public string? StagePath { get; private init; }

        public static SupervisionTextSink Counting() => new(file: null);

        public static SupervisionTextSink Staging(string stagePath) =>
            new(new FileStream(
                stagePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.None))
            {
                StagePath = stagePath,
            };

        public void Write(string value) => writer.Write(value);

        public void Write(ReadOnlySpan<char> value) => writer.Write(value);

        /// <summary>Flushes, forces staged bytes to disk, and returns the byte count and SHA-256.</summary>
        public (long Bytes, string Sha256) Complete()
        {
            writer.Flush();
            file?.Flush(flushToDisk: true);
            return (measuring.BytesWritten, measuring.CompleteHash());
        }

        public void Dispose()
        {
            writer.Dispose();
            measuring.Dispose();
            file?.Dispose();
        }
    }

    private sealed class MeasuringStream : Stream
    {
        private readonly Stream? inner;
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private string? completedHash;

        public MeasuringStream(Stream? inner)
        {
            this.inner = inner;
        }

        public long BytesWritten { get; private set; }

        public string CompleteHash() =>
            completedHash ??= Convert.ToHexString(hash.GetHashAndReset());

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (completedHash is not null)
            {
                throw new InvalidOperationException("The measured supervision output was already completed.");
            }

            hash.AppendData(buffer);
            inner?.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override void Flush() => inner?.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hash.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
