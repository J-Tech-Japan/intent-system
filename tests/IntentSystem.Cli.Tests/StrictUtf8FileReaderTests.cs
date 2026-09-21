using System.Text;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class StrictUtf8FileReaderTests
{
    [Fact]
    public void ReadText_InvalidFixtureNamesPathAndByteOffset()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = temporaryDirectory.WriteBytes("invalid.md", InvalidUtf8Fixture());

        var exception = Assert.Throws<StrictUtf8FileReadException>(() => StrictUtf8FileReader.ReadText(path));

        Assert.Equal(path, exception.FilePath);
        Assert.Equal(1000, exception.ByteOffset);
        Assert.Equal($"Issue body is not valid UTF-8 at byte offset 1000 in {path}.", exception.Message);
    }

    [Fact]
    public void ReadText_StripsExactlyOneLeadingUtf8Bom()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = temporaryDirectory.WriteBytes(
            "bom.md",
            [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, (byte)'A']);

        Assert.Equal("\uFEFFA", StrictUtf8FileReader.ReadText(path));
    }

    [Fact]
    public void ReadText_RefusesUtf16LittleEndianBom()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = temporaryDirectory.WriteBytes("utf16-le.md", [0xFF, 0xFE, (byte)'A', 0x00]);

        var exception = Assert.Throws<StrictUtf8FileReadException>(() => StrictUtf8FileReader.ReadText(path));

        Assert.Equal(path, exception.FilePath);
        Assert.Equal(0, exception.ByteOffset);
        Assert.Equal($"Issue body is not valid UTF-8 at byte offset 0 in {path}.", exception.Message);
    }

    private static byte[] InvalidUtf8Fixture()
    {
        var bytes = Enumerable.Repeat((byte)'A', 50_003).ToArray();
        bytes[1000] = 0xFF;
        return bytes;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("g847-strict-reader-").FullName;

        public string WriteBytes(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
