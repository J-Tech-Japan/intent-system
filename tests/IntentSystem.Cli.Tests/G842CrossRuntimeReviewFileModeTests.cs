using System.Runtime.InteropServices;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class G842CrossRuntimeReviewFileModeTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g842-file-mode-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManagedMetadata_IdentifiesRegularDirectoryAndSymlink()
    {
        var regular = Path.Combine(root, "regular.txt");
        var directory = Path.Combine(root, "directory");
        var symlink = Path.Combine(root, "symlink");
        File.WriteAllText(regular, "regular");
        Directory.CreateDirectory(directory);
        File.CreateSymbolicLink(symlink, regular);

        Assert.True(CrossRuntimeReviewFileMode.IsRegularFile(regular));
        Assert.False(CrossRuntimeReviewFileMode.IsDirectory(regular));
        Assert.False(CrossRuntimeReviewFileMode.IsSymlink(regular));

        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(directory));
        Assert.True(CrossRuntimeReviewFileMode.IsDirectory(directory));
        Assert.False(CrossRuntimeReviewFileMode.IsSymlink(directory));

        Assert.False(CrossRuntimeReviewFileMode.IsRegularFile(symlink));
        Assert.False(CrossRuntimeReviewFileMode.IsDirectory(symlink));
        Assert.True(CrossRuntimeReviewFileMode.IsSymlink(symlink));
    }

    [Fact]
    public async Task EmptyFileAndFifo_AreRefusedWithoutABlockingRead()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return;
        }

        var empty = Path.Combine(root, "empty.txt");
        var fifo = Path.Combine(root, "pipe");
        File.WriteAllBytes(empty, []);
        Assert.Equal(0, MkFifo(fifo, 0x180));

        var emptyResult = await RunBounded(() => Read(empty));
        Assert.False(emptyResult.Success);
        Assert.Equal(CrossRuntimeReviewFileReadFailure.Empty, emptyResult.Failure);

        var fifoResult = await RunBounded(() => Read(fifo));
        Assert.False(fifoResult.Success);
        Assert.Equal(CrossRuntimeReviewFileReadFailure.Empty, fifoResult.Failure);
    }

    private static (bool Success, CrossRuntimeReviewFileReadFailure Failure) Read(string path)
    {
        var success = CrossRuntimeReviewFileMode.TryReadRegularFileBytes(
            path,
            maxBytes: 64,
            out _,
            out var failure,
            out _);
        return (success, failure);
    }

    private static async Task<T> RunBounded<T>(Func<T> operation)
    {
        var work = Task.Run(operation);
        var completed = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(work, completed);
        return await work;
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string pathname, int mode);
}
