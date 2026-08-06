namespace IntentSystem.Cli.Tests;

public sealed class ChildProcessEncodingGuardTests
{
    [Fact]
    public void EveryRedirectedChildStreamDeclaresUtf8Encoding()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(path =>
                     {
                         var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                         return relative.StartsWith("src/IntentSystem.Cli/", StringComparison.Ordinal)
                             || relative.StartsWith("src/IntentSystem.Review/", StringComparison.Ordinal);
                     }))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("RedirectStandardOutput", StringComparison.Ordinal)
                    && !lines[i].Contains("RedirectStandardError", StringComparison.Ordinal))
                {
                    continue;
                }

                var start = Math.Max(0, i - 12);
                var end = Math.Min(lines.Length, i + 13);
                var window = string.Join('\n', lines[start..end]);
                if (!window.Contains("StandardOutputEncoding", StringComparison.Ordinal)
                    || !window.Contains("StandardErrorEncoding", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "IntentSystem.Cli")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
