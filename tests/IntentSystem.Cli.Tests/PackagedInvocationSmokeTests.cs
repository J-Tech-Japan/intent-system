using System.Diagnostics;
using System.Xml.Linq;

namespace IntentSystem.Cli.Tests;

public sealed class PackagedInvocationSmokeTests
{
    private static readonly Lock ProcessStateLock = new();

    [Fact]
    public void CliProject_DeclaresDotNetToolPackagingMetadata()
    {
        var document = XDocument.Load(Path.Combine(GetSolutionRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj"));
        var propertyGroup = document.Root?
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "PropertyGroup", StringComparison.Ordinal));

        Assert.NotNull(propertyGroup);
        Assert.Equal("true", GetPropertyValue(propertyGroup!, "PackAsTool"));
        Assert.Equal("intent-cli", GetPropertyValue(propertyGroup, "ToolCommandName"));
        Assert.Equal("intent-cli", GetPropertyValue(propertyGroup, "PackageId"));
        Assert.Equal("0.1.0", GetPropertyValue(propertyGroup, "Version"));
        Assert.Equal("README.md", GetPropertyValue(propertyGroup, "PackageReadmeFile"));
    }

    [Fact]
    public void DotnetToolExec_RunsPackagedCliAgainstHermeticFixture()
    {
        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var packageOutputDirectory = tempDirectory.CreateDirectory("packages");
            var fixtureRoot = tempDirectory.CreateDirectory(Path.Combine("smoke-repo"));
            tempDirectory.CreateDirectory(Path.Combine("smoke-repo", ".intent-cli"));
            tempDirectory.CreateFile(
                Path.Combine("smoke-repo", ".intent-cli", "config.toml"),
                """
                default_domain = "intent-cli"
                artifact_root = ".intent-cli"
                worktree_root = ".intent-cli/worktrees"
                """);

            var packOutputPath = tempDirectory.GetPath("pack.stdout.txt");
            var packErrorPath = tempDirectory.GetPath("pack.stderr.txt");
            var packResult = RunShellCommand(
                $"dotnet pack {QuoteForShell(Path.Combine(GetSolutionRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj"))} -o {QuoteForShell(packageOutputDirectory)} > {QuoteForShell(packOutputPath)} 2> {QuoteForShell(packErrorPath)}",
                GetSolutionRoot());

            var packLog = File.ReadAllText(packOutputPath) + File.ReadAllText(packErrorPath);

            Assert.Equal(0, packResult.ExitCode);
            Assert.Contains("Successfully created package", packLog, StringComparison.Ordinal);

            var invokeOutputPath = tempDirectory.GetPath("invoke.stdout.txt");
            var invokeErrorPath = tempDirectory.GetPath("invoke.stderr.txt");
            var invokeResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version 0.1.0 intent-cli project status > {QuoteForShell(invokeOutputPath)} 2> {QuoteForShell(invokeErrorPath)}",
                fixtureRoot);

            var invokeOutput = File.ReadAllText(invokeOutputPath);
            var invokeError = File.ReadAllText(invokeErrorPath);

            Assert.Equal(0, invokeResult.ExitCode);
            Assert.Contains("Domain: intent-cli", invokeOutput, StringComparison.Ordinal);
            Assert.Contains("Config path:", invokeOutput, StringComparison.Ordinal);
            Assert.Equal(string.Empty, invokeError.Trim(), ignoreCase: false);
        }
    }

    [Fact]
    public void Readme_DocumentsHermeticPackagedInvocationPaths()
    {
        var readme = File.ReadAllText(Path.Combine(GetSolutionRoot(), "README.md"));

        Assert.Contains("mkdir -p .artifacts/smoke-repo/.intent-cli", readme, StringComparison.Ordinal);
        Assert.Contains("cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'", readme, StringComparison.Ordinal);
        Assert.Contains("(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version 0.1.0 intent-cli project status)", readme, StringComparison.Ordinal);
        Assert.Contains("(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version 0.1.0 intent-cli project status)", readme, StringComparison.Ordinal);
    }

    private static ProcessResult RunShellCommand(string script, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/zsh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-lc");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start shell process.");

        if (!process.WaitForExit(milliseconds: 120000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Shell command did not exit within the timeout.");
        }

        return new ProcessResult(process.ExitCode);
    }

    private static string? GetPropertyValue(XElement propertyGroup, string propertyName)
    {
        return propertyGroup.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.Ordinal))?
            .Value;
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string QuoteForShell(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private sealed record ProcessResult(int ExitCode);

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-tool-pack-").FullName;

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

        public string GetPath(string relativePath)
        {
            return Path.Combine(rootPath, relativePath);
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
