using System.Diagnostics;
using System.Text.Json;
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
        // G407: NuGet package id and tool command name are intentionally different.
        // Install: dotnet tool install -g JTechJapan.IntentSystem.Cli
        // Command: intent-cli
        Assert.Equal("intent-cli", GetPropertyValue(propertyGroup, "ToolCommandName"));
        Assert.Equal("JTechJapan.IntentSystem.Cli", GetPropertyValue(propertyGroup, "PackageId"));
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
            // G407: package id is JTechJapan.IntentSystem.Cli; dotnet tool exec uses the
            // package id to locate the nupkg, then invokes the ToolCommandName (intent-cli).
            var invokeResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} JTechJapan.IntentSystem.Cli project status > {QuoteForShell(invokeOutputPath)} 2> {QuoteForShell(invokeErrorPath)}",
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
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} JTechJapan.IntentSystem.Cli automation summary --format json > {QuoteForShell(summaryOutputPath)} 2> {QuoteForShell(summaryErrorPath)}",
                fixtureRoot);

            var summaryOutput = File.ReadAllText(summaryOutputPath);
            var summaryError = File.ReadAllText(summaryErrorPath);

            Assert.Equal(0, summaryResult.ExitCode);
            Assert.Contains("\"automationCommandSurfaceVersion\"", summaryOutput, StringComparison.Ordinal);
            Assert.Contains("\"automationCommandCapabilities\"", summaryOutput, StringComparison.Ordinal);
            Assert.Equal(string.Empty, summaryError.Trim(), ignoreCase: false);

            // Use -- to pass --help through dotnet tool exec to intent-cli rather than having
            // dotnet tool exec intercept it and show its own help.
            var prTransitionHelpOutputPath = tempDirectory.GetPath("pr-transition.stdout.txt");
            var prTransitionHelpErrorPath = tempDirectory.GetPath("pr-transition.stderr.txt");
            var prTransitionHelpResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} JTechJapan.IntentSystem.Cli -- automation pr-transition --help > {QuoteForShell(prTransitionHelpOutputPath)} 2> {QuoteForShell(prTransitionHelpErrorPath)}",
                fixtureRoot);

            var prTransitionHelpOutput = File.ReadAllText(prTransitionHelpOutputPath);
            var prTransitionHelpError = File.ReadAllText(prTransitionHelpErrorPath);

            Assert.Equal(0, prTransitionHelpResult.ExitCode);
            Assert.Contains("review-start", prTransitionHelpOutput, StringComparison.Ordinal);
            Assert.Contains("request-update", prTransitionHelpOutput, StringComparison.Ordinal);
            Assert.Contains("approved", prTransitionHelpOutput, StringComparison.Ordinal);
            Assert.Equal(string.Empty, prTransitionHelpError.Trim(), ignoreCase: false);

            var topLevelHelpOutputPath = tempDirectory.GetPath("help.stdout.txt");
            var topLevelHelpErrorPath = tempDirectory.GetPath("help.stderr.txt");
            var topLevelHelpResult = RunShellCommand(
                $"dotnet tool exec --yes --source {QuoteForShell(packageOutputDirectory)} --version {QuoteForShell(packageVersion)} JTechJapan.IntentSystem.Cli -- --help > {QuoteForShell(topLevelHelpOutputPath)} 2> {QuoteForShell(topLevelHelpErrorPath)}",
                fixtureRoot);

            var topLevelHelpOutput = File.ReadAllText(topLevelHelpOutputPath);

            Assert.Equal(0, topLevelHelpResult.ExitCode);
            // G379: the default top-level help is chat-first — it leads with
            // workflow guides + the primary command groups (the full catalog
            // moved behind `intent-cli --help --all`).
            Assert.Contains("Primary command groups", topLevelHelpOutput, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Readme_DocumentsHermeticPackagedInvocationPaths()
    {
        // G422: packaged invocation smoke guidance moved from README.md to
        // docs/en/09-developer-reference.md. Verify the new canonical location.
        var devRef = File.ReadAllText(
            Path.Combine(GetSolutionRoot(), "docs", "en", "09-developer-reference.md"));

        Assert.Contains("mkdir -p .artifacts/smoke-repo/.intent-cli", devRef, StringComparison.Ordinal);
        Assert.Contains("cat > .artifacts/smoke-repo/.intent-cli/config.toml <<'EOF'", devRef, StringComparison.Ordinal);
        Assert.Contains("INTENT_CLI_BASE_VERSION=\"$(sed -n", devRef, StringComparison.Ordinal);
        Assert.Contains("\"nextVersion\"", devRef, StringComparison.Ordinal);
        Assert.Contains("export INTENT_CLI_LOCAL_VERSION=\"$INTENT_CLI_BASE_VERSION-local.$(date -u +%Y%m%d%H%M%S)\"", devRef, StringComparison.Ordinal);
        Assert.DoesNotContain("INTENT_CLI_LOCAL_VERSION=\"0.3.2-local", devRef, StringComparison.Ordinal);
        // G407: package id is JTechJapan.IntentSystem.Cli; dotnet tool exec / dnx uses the
        // package id to locate the nupkg. The installed command remains intent-cli.
        Assert.Contains("(cd .artifacts/smoke-repo && dotnet tool exec --yes --source ../packages --version \"$INTENT_CLI_LOCAL_VERSION\" JTechJapan.IntentSystem.Cli project status)", devRef, StringComparison.Ordinal);
        Assert.Contains("(cd .artifacts/smoke-repo && dnx --yes --source ../packages --version \"$INTENT_CLI_LOCAL_VERSION\" JTechJapan.IntentSystem.Cli project status)", devRef, StringComparison.Ordinal);
    }

    [Fact]
    public void HostRefreshScript_DerivesVersionAndVerifiesBeforePromotion()
    {
        var script = File.ReadAllText(Path.Combine(GetSolutionRoot(), "eng", "refresh-host-local-intent-cli.sh"));

        Assert.Contains("INTENT_CLI_BASE_VERSION=\"$(sed -n", script, StringComparison.Ordinal);
        Assert.Contains("\"nextVersion\"", script, StringComparison.Ordinal);
        Assert.Contains("$INTENT_CLI_BASE_VERSION-local.$LOCAL_STAMP.$$.g$CHILD_SHA", script, StringComparison.Ordinal);
        Assert.Contains("-p:Version=\"$INTENT_CLI_LOCAL_VERSION\"", script, StringComparison.Ordinal);
        Assert.Contains("--version \"\\$INTENT_CLI_LOCAL_VERSION\"", script, StringComparison.Ordinal);
        Assert.Contains("PACKAGE_ID=\"$(sed -n", script, StringComparison.Ordinal);
        Assert.Contains("\"$TEMP_WRAPPER_PATH\" --version", script, StringComparison.Ordinal);
        Assert.Contains("\"$TEMP_WRAPPER_PATH\" automation summary --format json", script, StringComparison.Ordinal);
        Assert.Contains("\"automationCommandSurfaceVersion\"", script, StringComparison.Ordinal);
        Assert.Contains("\"issue-publish\"", script, StringComparison.Ordinal);
        Assert.Contains("\"pr-transition.review-start\"", script, StringComparison.Ordinal);
        Assert.Contains("\"pr-transition.request-update\"", script, StringComparison.Ordinal);
        Assert.Contains("\"pr-transition.approved\"", script, StringComparison.Ordinal);
        Assert.Contains("$PACKAGE_ID -- \\\\", script, StringComparison.Ordinal);
        Assert.Contains("trap cleanup_candidate EXIT", script, StringComparison.Ordinal);
        Assert.Contains("Remedy: $remedy", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("\"$TEMP_WRAPPER_PATH\" automation summary --format json", StringComparison.Ordinal)
            < script.IndexOf("mv \"$TEMP_WRAPPER_PATH\" \"$WRAPPER_PATH\"", StringComparison.Ordinal));
        Assert.DoesNotContain("automation pr-transition --help", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--version 0.1.0", script, StringComparison.Ordinal);
        Assert.DoesNotContain("0.3.2-local", script, StringComparison.Ordinal);
        Assert.DoesNotContain("find \"$PACKAGES_DIR\"", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void HostRefreshGuidance_DocumentsFailClosedPromotionContract(string language)
    {
        var devRef = File.ReadAllText(
            Path.Combine(GetSolutionRoot(), "docs", language, "09-developer-reference.md"));

        Assert.Contains("eng/refresh-host-local-intent-cli.sh /path/to/host-repo", devRef, StringComparison.Ordinal);
        Assert.Contains("`nextVersion`", devRef, StringComparison.Ordinal);
        Assert.Contains("`automationCommandSurfaceVersion`", devRef, StringComparison.Ordinal);
        Assert.Contains("`.tmp`", devRef, StringComparison.Ordinal);
        Assert.Contains("byte-identical", devRef, StringComparison.Ordinal);
        Assert.Contains("remedy", devRef, StringComparison.Ordinal);
    }

    [Fact]
    public void HostRefreshScript_PromotesOnlyAfterCandidateVerification_AndWrapperRuns()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var hostRoot = tempDirectory.CreateDirectory("host");
            var binDirectory = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "bin"));
            var packagesDirectory = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "packages"));
            var wrapperPath = Path.Combine(binDirectory, "intent-cli");
            File.WriteAllText(wrapperPath, "old working wrapper\n");

            var fakeBin = tempDirectory.CreateDirectory("fake-bin");
            var fakeDotnetLog = tempDirectory.GetPath("fake-dotnet.log");
            CreateFakeDotnet(Path.Combine(fakeBin, "dotnet"));
            var environment = BuildRefreshEnvironment(fakeBin, fakeDotnetLog);

            var refresh = RunCapturedCommand(
                "/usr/bin/env",
                ["bash", Path.Combine(GetSolutionRoot(), "eng", "refresh-host-local-intent-cli.sh"), hostRoot],
                GetSolutionRoot(),
                environment);

            Assert.Equal(0, refresh.ExitCode);
            Assert.False(File.Exists(wrapperPath + ".tmp"));
            Assert.DoesNotContain("old working wrapper", File.ReadAllText(wrapperPath), StringComparison.Ordinal);

            var nextVersion = GetNextVersion();
            var package = Assert.Single(Directory.GetFiles(packagesDirectory, "*.nupkg"));
            Assert.Contains($"JTechJapan.IntentSystem.Cli.{nextVersion}-local.", Path.GetFileName(package), StringComparison.Ordinal);

            var invocation = RunCapturedCommand(
                wrapperPath,
                ["--version"],
                hostRoot,
                environment);

            Assert.Equal(0, invocation.ExitCode);
            Assert.Contains($"intent-cli {nextVersion}-local.", invocation.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("JTechJapan.IntentSystem.Cli -- --version", File.ReadAllText(fakeDotnetLog), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("version", "version invocation")]
    [InlineData("summary", "automation summary")]
    public void HostRefreshScript_VerificationFailure_PreservesInstalledWrapperAndCleansCandidate(
        string forcedFailure,
        string failedCheck)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var hostRoot = tempDirectory.CreateDirectory("host");
            var binDirectory = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "bin"));
            var packagesDirectory = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "packages"));
            var wrapperPath = Path.Combine(binDirectory, "intent-cli");
            var originalWrapper = new byte[] { 0x23, 0x21, 0x2f, 0x62, 0x69, 0x6e, 0x2f, 0x73, 0x68, 0x0a, 0x00, 0xff };
            File.WriteAllBytes(wrapperPath, originalWrapper);
            var oldPackagePath = Path.Combine(packagesDirectory, "JTechJapan.IntentSystem.Cli.0.8.1-local.old.nupkg");
            var oldPackage = new byte[] { 0x01, 0x02, 0x03, 0xfe };
            File.WriteAllBytes(oldPackagePath, oldPackage);

            var fakeBin = tempDirectory.CreateDirectory("fake-bin");
            var fakeDotnetLog = tempDirectory.GetPath("fake-dotnet.log");
            CreateFakeDotnet(Path.Combine(fakeBin, "dotnet"));
            var environment = BuildRefreshEnvironment(fakeBin, fakeDotnetLog, forcedFailure);

            var refresh = RunCapturedCommand(
                "/usr/bin/env",
                ["bash", Path.Combine(GetSolutionRoot(), "eng", "refresh-host-local-intent-cli.sh"), hostRoot],
                GetSolutionRoot(),
                environment);

            Assert.NotEqual(0, refresh.ExitCode);
            Assert.Equal(originalWrapper, File.ReadAllBytes(wrapperPath));
            Assert.Equal(oldPackage, File.ReadAllBytes(oldPackagePath));
            Assert.False(File.Exists(wrapperPath + ".tmp"));
            Assert.Equal(new[] { oldPackagePath }, Directory.GetFiles(packagesDirectory, "*.nupkg"));
            Assert.Contains(failedCheck, refresh.StandardError, StringComparison.Ordinal);
            Assert.Contains("The previously installed wrapper was not changed.", refresh.StandardError, StringComparison.Ordinal);
            Assert.Contains("Remedy:", refresh.StandardError, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HostRefreshScript_RejectsDerivedFixedVersionWithoutTouchingInstalledWrapper()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        lock (ProcessStateLock)
        {
            using var tempDirectory = new TemporaryDirectory();
            var hostRoot = tempDirectory.CreateDirectory("host");
            var binDirectory = tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "bin"));
            tempDirectory.CreateDirectory(Path.Combine("host", ".intent-cli", "packages"));
            var wrapperPath = Path.Combine(binDirectory, "intent-cli");
            const string originalWrapper = "old working wrapper\n";
            File.WriteAllText(wrapperPath, originalWrapper);

            var fakeBin = tempDirectory.CreateDirectory("fake-bin");
            var fakeDotnetLog = tempDirectory.GetPath("fake-dotnet.log");
            CreateFakeDotnet(Path.Combine(fakeBin, "dotnet"));
            var nextVersion = GetNextVersion();
            var environment = BuildRefreshEnvironment(fakeBin, fakeDotnetLog);
            environment["INTENT_CLI_LOCAL_VERSION"] = nextVersion;

            var refresh = RunCapturedCommand(
                "/usr/bin/env",
                ["bash", Path.Combine(GetSolutionRoot(), "eng", "refresh-host-local-intent-cli.sh"), hostRoot],
                GetSolutionRoot(),
                environment);

            Assert.NotEqual(0, refresh.ExitCode);
            Assert.Equal(originalWrapper, File.ReadAllText(wrapperPath));
            Assert.Contains($"must not reuse the derived fixed package version {nextVersion}", refresh.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(fakeDotnetLog));
        }
    }

    private static ProcessResult RunShellCommand(string script, string workingDirectory)
    {
        // G370: resolve the host shell at runtime so the packaged-
        // invocation smoke can run on GitHub-hosted Ubuntu (no zsh)
        // without losing the macOS dev-loop behavior.
        var startInfo = new ProcessStartInfo
        {
            FileName = IntentSystem.Cli.Tests.TestSupport.PortableShellResolver.Resolve(),
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

    private static Dictionary<string, string> BuildRefreshEnvironment(
        string fakeBin,
        string fakeDotnetLog,
        string forcedFailure = "")
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = fakeBin + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty),
            ["CHILD_INTENT_SYSTEM"] = GetSolutionRoot(),
            ["FAKE_DOTNET_LOG"] = fakeDotnetLog,
            ["FAKE_DOTNET_FAILURE"] = forcedFailure
        };
    }

    private static void CreateFakeDotnet(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The host refresh script requires a Unix shell.");
        }

        File.WriteAllText(
            path,
            """
            #!/usr/bin/env bash
            set -euo pipefail

            printf '%s\n' "$*" >> "$FAKE_DOTNET_LOG"

            if [[ "${1:-}" == "pack" ]]; then
              shift
              version=""
              output=""
              while (($# > 0)); do
                case "$1" in
                  -p:Version=*)
                    version="${1#-p:Version=}"
                    shift
                    ;;
                  -o)
                    output="$2"
                    shift 2
                    ;;
                  *)
                    shift
                    ;;
                esac
              done
              mkdir -p "$output"
              printf 'fake package\n' > "$output/JTechJapan.IntentSystem.Cli.$version.nupkg"
              exit 0
            fi

            if [[ "${1:-}" == "tool" && "${2:-}" == "exec" ]]; then
              all_arguments="$*"
              version=""
              payload=()
              while (($# > 0)); do
                case "$1" in
                  --version)
                    version="$2"
                    shift 2
                    ;;
                  --)
                    shift
                    payload=("$@")
                    break
                    ;;
                  *)
                    shift
                    ;;
                esac
              done

              if [[ "$all_arguments" != *"JTechJapan.IntentSystem.Cli --"* ]]; then
                echo "wrong package id" >&2
                exit 77
              fi

              if [[ "${payload[0]:-}" == "--version" ]]; then
                if [[ "${FAKE_DOTNET_FAILURE:-}" == "version" ]]; then
                  echo "forced version failure" >&2
                  exit 42
                fi
                echo "Skipping NuGet package signature verification."
                echo "intent-cli $version-fakesha-G591"
                exit 0
              fi

              if [[ "${payload[0]:-}" == "automation" && "${payload[1]:-}" == "summary" ]]; then
                if [[ "${FAKE_DOTNET_FAILURE:-}" == "summary" ]]; then
                  echo "forced automation summary failure" >&2
                  exit 43
                fi
                echo '{"automationCommandSurfaceVersion":"automation-command-surface/v1","automationCommandCapabilities":[{"capability":"issue-publish"},{"capability":"pr-transition.review-start"},{"capability":"pr-transition.request-update"},{"capability":"pr-transition.approved"}]}'
                exit 0
              fi
            fi

            echo "unexpected fake dotnet invocation: $*" >&2
            exit 78
            """);

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static CapturedProcessResult RunCapturedCommand(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Remove("INTENT_CLI_LOCAL_VERSION");
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(milliseconds: 120000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not exit within the timeout.");
        }

        return new CapturedProcessResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    private static string GetNextVersion()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(GetSolutionRoot(), "eng", "version.json")));
        return document.RootElement.GetProperty("nextVersion").GetString()
            ?? throw new InvalidOperationException("eng/version.json nextVersion was null.");
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
        return $"{GetNextVersion()}-local.{Guid.NewGuid():N}";
    }

    private sealed record ProcessResult(int ExitCode);

    private sealed record CapturedProcessResult(int ExitCode, string StandardOutput, string StandardError);

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
