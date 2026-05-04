using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
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
            var packageVersion = CreateLocalPackageVersion();
            var packResult = RunShellCommand(
                $"dotnet pack {QuoteForShell(Path.Combine(GetSolutionRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj"))} -p:Version={QuoteForShell(packageVersion)} -o {QuoteForShell(packageOutputDirectory)} > {QuoteForShell(packOutputPath)} 2> {QuoteForShell(packErrorPath)}",
                GetSolutionRoot());

            var packLog = File.ReadAllText(packOutputPath) + File.ReadAllText(packErrorPath);

            Assert.Equal(0, packResult.ExitCode);
            Assert.Contains("Successfully created package", packLog, StringComparison.Ordinal);

            var invokeOutputPath = tempDirectory.GetPath("invoke.stdout.txt");
            var invokeErrorPath = tempDirectory.GetPath("invoke.stderr.txt");
            var invokeResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} intent-cli project status > {QuoteForShell(invokeOutputPath)} 2> {QuoteForShell(invokeErrorPath)}",
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
    public void DotnetToolExec_RunsPackagedAutomationSurfaceWithUniqueLocalVersion()
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

            var packageVersion = CreateLocalPackageVersion();
            var packResult = RunShellCommand(
                $"dotnet pack {QuoteForShell(Path.Combine(GetSolutionRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj"))} -p:Version={QuoteForShell(packageVersion)} -o {QuoteForShell(packageOutputDirectory)} > {QuoteForShell(tempDirectory.GetPath("pack.stdout.txt"))} 2> {QuoteForShell(tempDirectory.GetPath("pack.stderr.txt"))}",
                GetSolutionRoot());

            Assert.Equal(0, packResult.ExitCode);

            var summaryOutputPath = tempDirectory.GetPath("summary.stdout.txt");
            var summaryErrorPath = tempDirectory.GetPath("summary.stderr.txt");
            var summaryResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} intent-cli automation summary --format json > {QuoteForShell(summaryOutputPath)} 2> {QuoteForShell(summaryErrorPath)}",
                fixtureRoot);

            var summaryOutput = File.ReadAllText(summaryOutputPath);
            var summaryError = File.ReadAllText(summaryErrorPath);

            Assert.Equal(0, summaryResult.ExitCode);
            Assert.Contains("\"automationCommandSurfaceVersion\"", summaryOutput, StringComparison.Ordinal);
            Assert.Contains("\"automationCommandCapabilities\"", summaryOutput, StringComparison.Ordinal);
            Assert.Equal(string.Empty, summaryError.Trim(), ignoreCase: false);

            var prTransitionHelpErrorPath = tempDirectory.GetPath("pr-transition.stderr.txt");
            var prTransitionHelpResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} intent-cli automation pr-transition --help > {QuoteForShell(tempDirectory.GetPath("pr-transition.stdout.txt"))} 2> {QuoteForShell(prTransitionHelpErrorPath)}",
                fixtureRoot);

            var prTransitionHelpError = File.ReadAllText(prTransitionHelpErrorPath);

            Assert.Equal(0, prTransitionHelpResult.ExitCode);
            Assert.Equal(string.Empty, prTransitionHelpError.Trim(), ignoreCase: false);
        }
    }

    [Fact]
    public void Readme_DocumentsHermeticPackagedInvocationPaths()
    {
        var readme = File.ReadAllText(Path.Combine(GetSolutionRoot(), "README.md"));

        Assert.Contains("mkdir -p .artifacts/smoke-repo/.intent-cli", readme, StringComparison.Ordinal);
        Assert.Contains("cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'", readme, StringComparison.Ordinal);
        Assert.Contains("export INTENT_CLI_LOCAL_VERSION=\"0.1.0-local.$(date -u +%Y%m%d%H%M%S)\"", readme, StringComparison.Ordinal);
        Assert.Contains("(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version \"$INTENT_CLI_LOCAL_VERSION\" intent-cli project status)", readme, StringComparison.Ordinal);
        Assert.Contains("(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version \"$INTENT_CLI_LOCAL_VERSION\" intent-cli project status)", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void HostRefreshScript_UsesUniqueLocalVersionAndVerifiesAutomationSurface()
    {
        var script = File.ReadAllText(Path.Combine(GetSolutionRoot(), "ops", "refresh-host-local-intent-cli.sh"));

        Assert.Contains("0.1.0-local.$LOCAL_STAMP.$$.g$CHILD_SHA", script, StringComparison.Ordinal);
        Assert.Contains("-p:Version=\"$INTENT_CLI_LOCAL_VERSION\"", script, StringComparison.Ordinal);
        Assert.Contains("--version \"\\$INTENT_CLI_LOCAL_VERSION\"", script, StringComparison.Ordinal);
        Assert.Contains("find \"$PACKAGES_DIR\" -maxdepth 1 -type f -name 'intent-cli.*.nupkg' -delete", script, StringComparison.Ordinal);
        Assert.Contains("automation summary --format json", script, StringComparison.Ordinal);
        Assert.Contains("\"automationCommandSurfaceVersion\"", script, StringComparison.Ordinal);
        Assert.Contains("\"issue-publish\"", script, StringComparison.Ordinal);
        Assert.Contains("\"pr-transition.review-start\"", script, StringComparison.Ordinal);
        Assert.Contains("\"pr-transition.request-update\"", script, StringComparison.Ordinal);
        Assert.Contains("\"pr-transition.approved\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("automation pr-transition --help", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--version 0.1.0", script, StringComparison.Ordinal);
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

    private static string CreateLocalPackageVersion()
    {
        return $"0.1.0-local.{Guid.NewGuid():N}";
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
