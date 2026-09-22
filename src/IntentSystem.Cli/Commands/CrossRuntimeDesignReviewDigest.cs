using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G835: packet digest over the four packet files in a fixed order and byte framing.
/// </summary>
internal static class CrossRuntimeDesignReviewDigest
{
    public static readonly IReadOnlyList<string> PacketFileNames =
        ["packet.yaml", "github-body.md", "review-context.md", "implementation.md"];

    public sealed record PacketBytes(
        byte[] PacketYaml,
        byte[] GithubBody,
        byte[] ReviewContext,
        byte[] Implementation);

    public static string Compute(PacketBytes packet) =>
        Compute(
            packet.PacketYaml,
            packet.GithubBody,
            packet.ReviewContext,
            packet.Implementation);

    public static string Compute(
        byte[] packetYaml,
        byte[] githubBody,
        byte[] reviewContext,
        byte[] implementation)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(sha, PacketFileNames[0], packetYaml);
        AppendFile(sha, PacketFileNames[1], githubBody);
        AppendFile(sha, PacketFileNames[2], reviewContext);
        AppendFile(sha, PacketFileNames[3], implementation);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFile(IncrementalHash sha, string fileName, byte[] bytes)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fileName);
        sha.AppendData(nameBytes);
        sha.AppendData([0]);
        var lengthBytes = Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture));
        sha.AppendData(lengthBytes);
        sha.AppendData([0]);
        sha.AppendData(bytes);
    }

    public static bool TryReadFromDirectory(string packetDirectory, out PacketBytes packet, out string? missingPath)
    {
        packet = null!;
        missingPath = null;
        byte[] packetYaml;
        byte[] githubBody;
        byte[] reviewContext;
        byte[] implementation;
        foreach (var fileName in PacketFileNames)
        {
            var path = Path.Combine(packetDirectory, fileName);
            if (!File.Exists(path))
            {
                missingPath = path;
                return false;
            }
        }

        try
        {
            packetYaml = File.ReadAllBytes(Path.Combine(packetDirectory, PacketFileNames[0]));
            githubBody = File.ReadAllBytes(Path.Combine(packetDirectory, PacketFileNames[1]));
            reviewContext = File.ReadAllBytes(Path.Combine(packetDirectory, PacketFileNames[2]));
            implementation = File.ReadAllBytes(Path.Combine(packetDirectory, PacketFileNames[3]));
        }
        catch (IOException exception)
        {
            missingPath = exception.Message;
            return false;
        }

        packet = new PacketBytes(packetYaml, githubBody, reviewContext, implementation);
        return true;
    }

    public static bool TryReadFromDirectory(
        string packetDirectory,
        out PacketBytes packet,
        out string? missingPath,
        out string? unreadableFileName,
        out string? unreadableError)
    {
        packet = null!;
        missingPath = null;
        unreadableFileName = null;
        unreadableError = null;
        byte[] packetYaml;
        byte[] githubBody;
        byte[] reviewContext;
        byte[] implementation;
        foreach (var fileName in PacketFileNames)
        {
            var path = Path.Combine(packetDirectory, fileName);
            if (!File.Exists(path))
            {
                missingPath = path;
                return false;
            }
        }

        if (!TryReadPacketFile(packetDirectory, PacketFileNames[0], out packetYaml, out unreadableError))
        {
            unreadableFileName = PacketFileNames[0];
            return false;
        }

        if (!TryReadPacketFile(packetDirectory, PacketFileNames[1], out githubBody, out unreadableError))
        {
            unreadableFileName = PacketFileNames[1];
            return false;
        }

        if (!TryReadPacketFile(packetDirectory, PacketFileNames[2], out reviewContext, out unreadableError))
        {
            unreadableFileName = PacketFileNames[2];
            return false;
        }

        if (!TryReadPacketFile(packetDirectory, PacketFileNames[3], out implementation, out unreadableError))
        {
            unreadableFileName = PacketFileNames[3];
            return false;
        }

        packet = new PacketBytes(packetYaml, githubBody, reviewContext, implementation);
        return true;
    }

    private static bool TryReadPacketFile(
        string packetDirectory,
        string fileName,
        out byte[] bytes,
        out string? error)
    {
        try
        {
            bytes = File.ReadAllBytes(Path.Combine(packetDirectory, fileName));
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            bytes = [];
            error = exception.Message;
            return false;
        }
    }

    public static string ComputeFromDirectory(string packetDirectory)
    {
        if (!TryReadFromDirectory(packetDirectory, out var packet, out var missing))
        {
            throw new InvalidOperationException(missing ?? "packet file is missing.");
        }

        return Compute(packet);
    }
}
