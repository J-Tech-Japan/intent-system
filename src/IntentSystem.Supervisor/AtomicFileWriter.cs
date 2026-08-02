using System.Collections.Concurrent;
using System.Text;

namespace IntentSystem.Supervisor;

/// <summary>
/// Writes complete text files without exposing a truncated or partially
/// written target: bytes are flushed to a uniquely named temporary sibling,
/// then published with one overwrite-move.
/// </summary>
public static class AtomicFileWriter
{
    private static readonly ConcurrentDictionary<string, Action<string>> BeforeMoveHooks =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a path-scoped fault seam that runs after the temporary file
    /// has been flushed and before it is moved over the target. The callback
    /// receives the temporary file path.
    /// </summary>
    public static IDisposable RegisterBeforeMoveHook(string targetPath, Action<string> hook)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(hook);

        var key = Path.GetFullPath(targetPath);
        if (!BeforeMoveHooks.TryAdd(key, hook))
        {
            throw new InvalidOperationException($"a before-move hook is already registered for '{key}'.");
        }

        return new BeforeMoveHookRegistration(key);
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to a temporary sibling, flushes
    /// the file to disk, and atomically publishes it with one overwrite-move.
    /// </summary>
    public static void WriteAllText(string targetPath, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(contents);

        var fullTargetPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTargetPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 1024,
                leaveOpen: true))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            InvokeBeforeMoveHook(fullTargetPath, tempPath);
            File.Move(tempPath, fullTargetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void InvokeBeforeMoveHook(string targetPath, string tempPath)
    {
        if (!BeforeMoveHooks.IsEmpty && BeforeMoveHooks.TryGetValue(targetPath, out var hook))
        {
            hook(tempPath);
        }
    }

    private sealed class BeforeMoveHookRegistration : IDisposable
    {
        private readonly string key;

        public BeforeMoveHookRegistration(string key) => this.key = key;

        public void Dispose() => BeforeMoveHooks.TryRemove(key, out _);
    }
}
