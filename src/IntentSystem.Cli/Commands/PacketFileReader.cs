namespace IntentSystem.Cli.Commands;

/// <summary>
/// G841: internal test seam for packet file reads guarded by this unit.
/// Defaults to <see cref="File"/>; tests may replace and must reset after use.
/// </summary>
internal static class PacketFileReader
{
    public static Func<string, string> ReadAllText { get; set; } = File.ReadAllText;

    public static Func<string, byte[]> ReadAllBytes { get; set; } = File.ReadAllBytes;
}
