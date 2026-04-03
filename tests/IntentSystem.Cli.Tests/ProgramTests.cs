using IntentSystem.Cli;

namespace IntentSystem.Cli.Tests;

public sealed class ProgramTests
{
    private static readonly Lock ProcessStateLock = new();

    [Fact]
    public void Main_GivenProjectStatusCommand_ResolvesRepoRootAndWritesRuntimeBaseline()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            _ = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            tempDirectory.CreateFile(
                Path.Combine("repo", ".intent-cli", "config.toml"),
                """
                [project]
                domain = "intent-system"
                workflow_engine = "intent-cli"
                artifact_root = ".intent-cli/artifacts"
                """);
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(["project", "status"]);
            var output = consoleScope.Out.ToString();
            var repoRootLine = GetRequiredOutputLine(output, "Repo root: ");
            var configPathLine = GetRequiredOutputLine(output, "Config path: ");

            Assert.Equal(0, exitCode);
            Assert.Contains("Domain: intent-system", output, StringComparison.Ordinal);
            Assert.True(Directory.Exists(repoRootLine), $"Expected repo root directory to exist, but got '{repoRootLine}'.");
            Assert.True(File.Exists(configPathLine), $"Expected config path to exist, but got '{configPathLine}'.");
            Assert.EndsWith(
                Path.Combine("repo", ".intent-cli", "config.toml"),
                configPathLine,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, consoleScope.Error.ToString());
        }
    }

    [Fact]
    public void Main_GivenDirectoryWithoutIntentCliRoot_ReturnsExitCodeOne()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(["project", "status"]);

            Assert.Equal(1, exitCode);
            Assert.Contains("Could not find .intent-cli directory", consoleScope.Error.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, consoleScope.Out.ToString());
        }
    }

    [Fact]
    public void Main_GivenMissingConfigFile_ReturnsExitCodeOne()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var repoRoot = tempDirectory.CreateDirectory("repo");
            tempDirectory.CreateDirectory(Path.Combine("repo", ".intent-cli"));
            var workingDirectory = tempDirectory.CreateDirectory(Path.Combine("repo", "src", "feature"));
            using var consoleScope = new ConsoleScope();
            using var currentDirectoryScope = new CurrentDirectoryScope(workingDirectory);

            var exitCode = Program.Main(["project", "status"]);

            Assert.Equal(1, exitCode);
            Assert.Contains(
                Path.Combine(repoRoot, ".intent-cli", "config.toml"),
                consoleScope.Error.ToString(),
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, consoleScope.Out.ToString());
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string originalCurrentDirectory = Directory.GetCurrentDirectory();

        public CurrentDirectoryScope(string currentDirectory)
        {
            Directory.SetCurrentDirectory(currentDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
        }
    }

    private static string GetRequiredOutputLine(string output, string prefix)
    {
        var value = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal));

        return value is null
            ? throw new InvalidOperationException($"Expected output line starting with '{prefix}'.")
            : value[prefix.Length..];
    }

    private sealed class ConsoleScope : IDisposable
    {
        private readonly TextWriter originalError = Console.Error;
        private readonly TextWriter originalOut = Console.Out;

        public StringWriter Error { get; } = new();

        public StringWriter Out { get; } = new();

        public ConsoleScope()
        {
            Console.SetOut(Out);
            Console.SetError(Error);
        }

        public void Dispose()
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Out.Dispose();
            Error.Dispose();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-program-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
