using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only cursor measurement for the completion-channel journal envelope.
/// The fixed overlap lets a caller resume at a complete line without rescanning
/// the entire history; no lifecycle or journal mutation is performed here.
/// </summary>
internal static class NotifyCompletionChannelJournalMeasurement
{
    internal const int FixedOverlapBytes = 4096;
    private const int BufferSize = 64 * 1024;

    internal static NotifyCompletionJournalScan Scan(
        string path,
        long cursor = 0,
        int overlapBytes = FixedOverlapBytes)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("completion-channel journal was not found.", path);
        }

        var length = new FileInfo(path).Length;
        if (cursor < 0 || cursor > length)
        {
            throw new ArgumentOutOfRangeException(nameof(cursor));
        }

        var physicalStart = cursor == 0 ? 0 : Math.Max(0, cursor - Math.Max(1, overlapBytes));
        var stopwatch = Stopwatch.StartNew();
        long completeRecords = 0;
        var lineIsCompleteCandidate = physicalStart == 0;
        var lineHasData = false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
        stream.Position = physicalStart;
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] == (byte)'\n')
                {
                    if (lineIsCompleteCandidate && lineHasData)
                    {
                        completeRecords++;
                    }

                    lineIsCompleteCandidate = true;
                    lineHasData = false;
                    continue;
                }

                lineHasData = true;
                if (!lineIsCompleteCandidate)
                {
                    continue;
                }
            }
        }

        stopwatch.Stop();
        return new NotifyCompletionJournalScan(
            physicalStart,
            length,
            length - physicalStart,
            completeRecords,
            stopwatch.Elapsed.TotalSeconds);
    }
}

internal sealed record NotifyCompletionJournalScan(
    long PhysicalStart,
    long EndOffset,
    long BytesScanned,
    long CompleteRecords,
    double ElapsedSeconds);
