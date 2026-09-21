namespace IntentSystem.Cli.Commands;

/// <summary>
/// Owns the private file handed to a <c>gh --body-file</c> transmission.
/// Unix creation modes are applied only on non-Windows platforms; Windows
/// relies on the user's temp-directory ACLs.
/// </summary>
internal static class IssueBodyFileStager
{
    // Test seams cover directory creation, creation-time observations, byte
    // writing and mode application. Production leaves them null and uses the
    // guarded implementations.
    internal static Action<string>? DirectoryCreateOverride { get; set; }

    internal static Action<string>? DirectoryCreatedObserver { get; set; }

    internal static Action<string, byte[]>? FileWriteOverride { get; set; }

    internal static Action<string>? FileCreatedObserver { get; set; }

    internal static Action<string>? FileModeOverride { get; set; }

    internal static Action<string>? DirectoryDeleteOverride { get; set; }

    internal static IssueBodyStagedBody Stage(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"intent-cli-body-{Guid.NewGuid():N}");
        var stagedBody = new IssueBodyStagedBody(directoryPath);

        try
        {
            CreateDirectory(directoryPath);
        }
        catch (Exception exception)
        {
            stagedBody.Dispose();
            throw new IssueBodyStagingException(exception);
        }

        try
        {
            WriteFile(stagedBody.Path, bytes);
            ApplyFileMode(stagedBody.Path);
            return stagedBody;
        }
        catch (Exception exception)
        {
            stagedBody.Dispose();
            throw new IssueBodyStagingException(exception);
        }
    }

    private static void CreateDirectory(string path)
    {
        if (DirectoryCreateOverride is not null)
        {
            DirectoryCreateOverride(path);
        }
        else if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        DirectoryCreatedObserver?.Invoke(path);
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        if (FileWriteOverride is not null)
        {
            FileWriteOverride(path, bytes);
            return;
        }

        var options = new FileStreamOptions
        {
            Access = FileAccess.Write,
            Mode = FileMode.CreateNew,
            Options = FileOptions.SequentialScan,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        FileCreatedObserver?.Invoke(path);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void ApplyFileMode(string path)
    {
        if (FileModeOverride is not null)
        {
            FileModeOverride(path);
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            // UnixCreateMode establishes 0600 at creation. Re-asserting the
            // exact mode also makes the invariant explicit after the write.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    internal static void TryDeleteOwnedDirectory(string path)
    {
        try
        {
            if (DirectoryDeleteOverride is not null)
            {
                DirectoryDeleteOverride(path);
                return;
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Cleanup is best-effort and must never mask the transmission or
            // staging exception that caused this invocation to finish.
        }
    }
}

/// <summary>
/// A successfully staged body owns its private directory until disposed.
/// Cleanup is deliberately best-effort and never replaces the transmission or
/// staging outcome that led to disposal.
/// </summary>
internal sealed class IssueBodyStagedBody : IDisposable
{
    private string? directoryPath;

    internal IssueBodyStagedBody(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        this.directoryPath = directoryPath;
        Path = System.IO.Path.Combine(directoryPath, "body.md");
    }

    internal string Path { get; }

    public void Dispose()
    {
        var ownedDirectory = Interlocked.Exchange(ref directoryPath, null);
        if (ownedDirectory is not null)
        {
            IssueBodyFileStager.TryDeleteOwnedDirectory(ownedDirectory);
        }
    }
}

internal sealed class IssueBodyStagingException : IOException
{
    internal IssueBodyStagingException(Exception innerException)
        : base(innerException.Message, innerException)
    {
    }
}
