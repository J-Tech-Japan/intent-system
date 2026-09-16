namespace IntentSystem.Cli.Commands;

/// <summary>
/// Test-only seam for unwritable-file fixtures. Production append paths use
/// this helper so tests can throw for one named path without file modes.
/// </summary>
internal static class GuardedFileWrite
{
    public static Action<string, string>? AppendLineFactory { get; set; }

    public static void AppendLine(string path, string line)
    {
        if (AppendLineFactory is not null)
        {
            AppendLineFactory(path, line);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(line);
        writer.Write('\n');
    }
}
