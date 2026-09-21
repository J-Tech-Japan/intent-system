using System.Runtime.InteropServices;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G842: Unix file-mode probes for cross-runtime path and exit-file validation.
/// </summary>
internal static class CrossRuntimeReviewFileMode
{
    private const int UnixO_RDONLY = 0x0000;
    private const int UnixDarwinO_NONBLOCK = 0x0004;
    private const int UnixLinuxO_NONBLOCK = 0x0800;

    internal static bool TryGetUnixFileMode(string path, out ushort mode)
    {
        var buffer = new byte[144];
        if (UnixLstat(path, buffer) != 0)
        {
            mode = 0;
            return false;
        }

        mode = OperatingSystem.IsMacOS()
            ? BitConverter.ToUInt16(buffer, 4)
            : BitConverter.ToUInt16(buffer, 12);
        return true;
    }

    internal static bool IsUnixFifo(string path) =>
        TryGetUnixFileMode(path, out var mode) && (mode & 0xF000) == 0x1000;

    internal static bool IsUnixRegularFile(string path) =>
        TryGetUnixFileMode(path, out var mode) && (mode & 0xF000) == 0x8000;

    internal static bool TryReadRegularFileBytes(string path, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                bytes = File.ReadAllBytes(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = exception.Message;
                return false;
            }
        }

        if (IsUnixFifo(path))
        {
            error = "named pipe";
            return false;
        }

        if (!IsUnixRegularFile(path))
        {
            error = "not a regular file";
            return false;
        }

        var nonblock = OperatingSystem.IsMacOS() ? UnixDarwinO_NONBLOCK : UnixLinuxO_NONBLOCK;
        var descriptor = UnixOpen(path, UnixO_RDONLY | nonblock);
        if (descriptor < 0)
        {
            error = $"open failed (native error {Marshal.GetLastPInvokeError()})";
            return false;
        }

        try
        {
            var buffer = new byte[64];
            var read = UnixRead(descriptor, buffer, (nuint)buffer.Length);
            if (read < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                if (errno == 35 || errno == 11)
                {
                    error = "non-blocking read would block";
                    return false;
                }

                error = $"read failed (native error {errno})";
                return false;
            }

            if (read == 0)
            {
                bytes = [];
                return true;
            }

            using var stream = new MemoryStream();
            stream.Write(buffer, 0, (int)read);
            while (true)
            {
                read = UnixRead(descriptor, buffer, (nuint)buffer.Length);
                if (read < 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    if (errno == 35 || errno == 11)
                    {
                        error = "non-blocking read would block";
                        return false;
                    }

                    error = $"read failed (native error {errno})";
                    return false;
                }

                if (read == 0)
                {
                    break;
                }

                stream.Write(buffer, 0, (int)read);
            }

            bytes = stream.ToArray();
            return true;
        }
        finally
        {
            UnixClose(descriptor);
        }
    }

    [DllImport("libc", EntryPoint = "lstat", SetLastError = true)]
    private static extern int UnixLstat(string pathname, byte[] buf);

    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern nint UnixRead(int descriptor, byte[] buffer, nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int UnixClose(int descriptor);
}
