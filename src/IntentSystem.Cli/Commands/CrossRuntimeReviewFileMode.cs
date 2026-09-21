namespace IntentSystem.Cli.Commands;

internal enum CrossRuntimeReviewFileReadFailure
{
    Missing,
    Symlink,
    Directory,
    Empty,
    NotRegular,
    TooLarge,
    ReadError,
}

/// <summary>
/// Managed file metadata and bounded-read helpers for cross-runtime review.
/// The metadata checks happen before any file is opened, so a zero-length
/// entry such as a FIFO is refused without a potentially blocking read.
/// </summary>
internal static class CrossRuntimeReviewFileMode
{
    internal static bool IsSymlink(string path) =>
        TryGetFileSystemInfo(path, out var info) && IsSymlink(info);

    internal static bool IsDirectory(string path) =>
        TryGetFileSystemInfo(path, out var info) && IsDirectory(info);

    internal static bool IsRegularFile(string path) =>
        TryGetFileSystemInfo(path, out var info)
        && info.Exists
        && info is FileInfo
        && !IsSymlink(info)
        && !IsDirectory(info);

    internal static bool TryReadRegularFileBytes(
        string path,
        out byte[] bytes,
        out CrossRuntimeReviewFileReadFailure failure,
        out string error) =>
        TryReadRegularFileBytes(path, maxBytes: null, out bytes, out failure, out error);

    internal static bool TryReadRegularFileBytes(
        string path,
        int? maxBytes,
        out byte[] bytes,
        out CrossRuntimeReviewFileReadFailure failure,
        out string error)
    {
        bytes = [];
        failure = CrossRuntimeReviewFileReadFailure.ReadError;
        error = string.Empty;

        if (!TryGetFileSystemInfo(path, out var info))
        {
            failure = CrossRuntimeReviewFileReadFailure.Missing;
            error = "file does not exist";
            return false;
        }

        if (IsSymlink(info))
        {
            failure = CrossRuntimeReviewFileReadFailure.Symlink;
            error = "symlink";
            return false;
        }

        if (!info.Exists)
        {
            failure = CrossRuntimeReviewFileReadFailure.Missing;
            error = "file does not exist";
            return false;
        }

        if (IsDirectory(info))
        {
            failure = CrossRuntimeReviewFileReadFailure.Directory;
            error = "directory";
            return false;
        }

        if (info is not FileInfo fileInfo)
        {
            failure = CrossRuntimeReviewFileReadFailure.NotRegular;
            error = "not a regular file";
            return false;
        }

        long length;
        try
        {
            length = fileInfo.Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = CrossRuntimeReviewFileReadFailure.ReadError;
            error = exception.Message;
            return false;
        }

        // Do this before opening the file. On macOS a FIFO is reported with
        // Normal attributes and length zero, so this also covers FIFOs.
        if (length <= 0)
        {
            failure = CrossRuntimeReviewFileReadFailure.Empty;
            error = "empty file";
            return false;
        }

        if (maxBytes is not null && length > maxBytes.Value)
        {
            failure = CrossRuntimeReviewFileReadFailure.TooLarge;
            error = $"file exceeds the {maxBytes.Value}-byte read limit";
            return false;
        }

        if (length > int.MaxValue)
        {
            failure = CrossRuntimeReviewFileReadFailure.TooLarge;
            error = "file is too large to read";
            return false;
        }

        try
        {
            using var stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[(int)length];
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            bytes = offset == buffer.Length ? buffer : buffer[..offset];
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failure = CrossRuntimeReviewFileReadFailure.ReadError;
            error = exception.Message;
            return false;
        }
    }

    private static bool TryGetFileSystemInfo(string path, out FileSystemInfo info)
    {
        foreach (FileSystemInfo candidate in new FileSystemInfo[] { new FileInfo(path), new DirectoryInfo(path) })
        {
            try
            {
                // LinkTarget is intentionally checked before Exists so a
                // dangling symlink is still rejected as a symlink.
                if (candidate.LinkTarget is not null || candidate.Exists)
                {
                    info = candidate;
                    return true;
                }
            }
            catch (Exception) when (candidate is FileInfo or DirectoryInfo)
            {
                // Treat an inaccessible entry as unreadable/missing below;
                // callers must not open it speculatively.
            }
        }

        info = null!;
        return false;
    }

    private static bool IsSymlink(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null
                || info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception) when (info is FileInfo or DirectoryInfo)
        {
            return false;
        }
    }

    private static bool IsDirectory(FileSystemInfo info)
    {
        try
        {
            return info.Attributes.HasFlag(FileAttributes.Directory);
        }
        catch (Exception) when (info is FileInfo or DirectoryInfo)
        {
            return false;
        }
    }
}
