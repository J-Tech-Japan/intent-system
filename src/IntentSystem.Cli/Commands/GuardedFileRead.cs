namespace IntentSystem.Cli.Commands;

/// <summary>
/// Test-only seam for unreadable-file fixtures. Production code reads through
/// this helper so tests can throw for one named path without file modes.
/// </summary>
internal static class GuardedFileRead
{
    public static Func<string, string>? ReadAllTextFactory { get; set; }

    public static string ReadAllText(string path) =>
        (ReadAllTextFactory ?? File.ReadAllText)(path);

    public static byte[] ReadAllBytes(string path) =>
        ReadAllTextFactory is null ? File.ReadAllBytes(path) : System.Text.Encoding.UTF8.GetBytes(ReadAllText(path));
}
