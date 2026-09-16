using System.Security.Cryptography;
using System.Text;

namespace IntentSystem.Cli.Commands;

internal static class OrcaRunTeamModeLock
{
    public const string RelativeIgnorePath = ".intent-cli/locks/.gitignore";
    public const string RelativeLockPath = ".intent-cli/locks/team-mode.lock";

    public static string ResolveLockPath(string routingRoot) =>
        Path.GetFullPath(Path.Combine(routingRoot, RelativeLockPath.Replace('/', Path.DirectorySeparatorChar)));

    public static string ResolveIgnorePath(string routingRoot) =>
        Path.GetFullPath(Path.Combine(routingRoot, RelativeIgnorePath.Replace('/', Path.DirectorySeparatorChar)));

    public static void EnsureLockDirectory(string routingRoot)
    {
        var ignorePath = ResolveIgnorePath(routingRoot);
        var content = "*" + Environment.NewLine;
        var directory = Path.GetDirectoryName(ignorePath)!;
        Directory.CreateDirectory(directory);
        if (!File.Exists(ignorePath))
        {
            File.WriteAllText(ignorePath, content);
        }
    }

    public static FileStream? TryAcquire(string routingRoot, out string? busyMessage)
    {
        EnsureLockDirectory(routingRoot);
        var lockPath = ResolveLockPath(routingRoot);
        try
        {
            busyMessage = null;
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            busyMessage = exception.Message;
            return null;
        }
    }

    public static string ComputeDigest(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string ComputeDigest(string routingRoot)
    {
        var path = TeamModeStore.ResolvePath(routingRoot);
        if (!File.Exists(path))
        {
            return "absent";
        }

        return ComputeDigest(GuardedFileRead.ReadAllBytes(path));
    }
}
