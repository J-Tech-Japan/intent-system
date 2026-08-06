namespace IntentSystem.Cli.Tests;

public sealed class ChildProcessEncodingGuardTests
{
    [Fact]
    public void EveryRedirectedChildStreamDeclaresUtf8Encoding()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            var source = File.ReadAllText(file);
            var outputRedirects = lines.Count(line => line.Contains("RedirectStandardOutput =", StringComparison.Ordinal));
            var outputEncodings = lines.Count(line => line.Contains("StandardOutputEncoding =", StringComparison.Ordinal));
            var errorRedirects = lines.Count(line => line.Contains("RedirectStandardError =", StringComparison.Ordinal));
            var errorEncodings = lines.Count(line => line.Contains("StandardErrorEncoding =", StringComparison.Ordinal));
            if (outputRedirects != outputEncodings || errorRedirects != errorEncodings)
            {
                offenders.Add($"{Path.GetRelativePath(root, file)}:redirect/encoding counts {outputRedirects}/{outputEncodings}, {errorRedirects}/{errorEncodings}");
            }

            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("process.StartInfo.RedirectStandard", StringComparison.Ordinal))
                {
                    continue;
                }

                var start = Math.Max(0, i - 3);
                var end = Math.Min(lines.Length, i + 4);
                var window = string.Join('\n', lines[start..end]);
                if (!window.Contains("StandardOutputEncoding", StringComparison.Ordinal)
                    || !window.Contains("StandardErrorEncoding", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}");
                }
            }

            for (var search = 0; ;)
            {
                var marker = source.IndexOf("new ProcessStartInfo", search, StringComparison.Ordinal);
                if (marker < 0)
                {
                    break;
                }

                var open = source.IndexOf('{', marker);
                if (open < 0)
                {
                    break;
                }

                var depth = 0;
                var close = -1;
                for (var index = open; index < source.Length; index++)
                {
                    if (source[index] == '{') depth++;
                    if (source[index] == '}' && --depth == 0)
                    {
                        close = index;
                        break;
                    }
                }

                if (close < 0)
                {
                    break;
                }

                var initializer = source[(open + 1)..close];
                if ((initializer.Contains("RedirectStandardOutput", StringComparison.Ordinal)
                        || initializer.Contains("RedirectStandardError", StringComparison.Ordinal))
                    && (!initializer.Contains("StandardOutputEncoding", StringComparison.Ordinal)
                        || !initializer.Contains("StandardErrorEncoding", StringComparison.Ordinal)))
                {
                    var line = source[..marker].Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{line}");
                }

                search = close + 1;
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
