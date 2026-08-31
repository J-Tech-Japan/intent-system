using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Writes one already-serialized record with the operating system's append
/// primitive. <see cref="FileMode.Append"/> is deliberately not used here:
/// the .NET implementation historically emulates append by seeking first,
/// which permits concurrent writers to select the same offset.
/// </summary>
internal static class AtomicAppendWriter
{
    private const int UnixO_WRONLY = 0x0001;
    private const int UnixDarwinO_APPEND = 0x0008;
    private const int UnixLinuxO_APPEND = 0x0400;

    private const uint WindowsFileAppendData = 0x0004;
    private const uint WindowsFileShareRead = 0x0001;
    private const uint WindowsFileShareWrite = 0x0002;
    private const uint WindowsOpenAlways = 4;
    private const uint WindowsFileAttributeNormal = 0x00000080;

    public static void Append(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        if (OperatingSystem.IsWindows())
        {
            AppendWindows(path, bytes);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            AppendUnix(path, UnixDarwinO_APPEND, bytes);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            AppendUnix(path, UnixLinuxO_APPEND, bytes);
            return;
        }

        throw new IOException($"The OS append primitive is unavailable on '{Environment.OSVersion.Platform}'.");
    }

    private static void AppendUnix(string path, int flags, byte[] bytes)
    {
        EnsureUnixFileExists(path);
        var descriptor = UnixOpen(path, UnixO_WRONLY | flags);
        if (descriptor < 0)
        {
            throw NativeIoException("open", path);
        }

        using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        var written = UnixWrite(descriptor, bytes, (nuint)bytes.Length);
        GC.KeepAlive(handle);
        if (written < 0)
        {
            throw NativeIoException("write", path);
        }

        if (written != bytes.Length)
        {
            throw new IOException(
                $"The OS append write for '{path}' completed only {written} of {bytes.Length} bytes.");
        }
    }

    private static void EnsureUnixFileExists(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 1,
            FileOptions.None);
    }


    private static void AppendWindows(string path, byte[] bytes)
    {
        using var handle = WindowsCreateFile(
            path,
            WindowsFileAppendData,
            WindowsFileShareRead | WindowsFileShareWrite,
            IntPtr.Zero,
            WindowsOpenAlways,
            WindowsFileAttributeNormal,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw NativeIoException("open", path);
        }

        if (!WindowsWriteFile(
                handle,
                bytes,
                checked((uint)bytes.Length),
                out var written,
                IntPtr.Zero))
        {
            throw NativeIoException("write", path);
        }

        if (written != bytes.Length)
        {
            throw new IOException(
                $"The OS append write for '{path}' completed only {written} of {bytes.Length} bytes.");
        }
    }

    private static IOException NativeIoException(string operation, string path) =>
        new($"The OS append {operation} failed for '{path}' (native error {Marshal.GetLastPInvokeError()}).");

    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int UnixOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern nint UnixWrite(int descriptor, byte[] buffer, nuint count);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle WindowsCreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WindowsWriteFile(
        SafeFileHandle handle,
        byte[] buffer,
        uint numberOfBytesToWrite,
        out uint numberOfBytesWritten,
        IntPtr overlapped);
}
