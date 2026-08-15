using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class UpdateCommandG703Tests
{
    [Fact]
    public void Resolver_FollowsExecutableSymlinkAndRetainsPathEvidence()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = NewFixtureRoot("symlink");
        var target = Path.Combine(root, "intent-cli");
        var link = Path.Combine(root, "intent-cli-link");
        File.WriteAllBytes(target, "standalone"u8.ToArray());
        File.CreateSymbolicLink(link, target);

        var resolution = ProcessExecutablePathResolver.ResolvePath(link);

        Assert.Null(resolution.Error);
        Assert.Equal(Path.GetFullPath(target), resolution.ResolvedPath);
        Assert.Equal(2, resolution.PathHops.Count);
        Assert.Contains(Path.GetFullPath(link), resolution.PathHops);
        Assert.Contains(Path.GetFullPath(target), resolution.PathHops);
    }

    [Theory]
    [InlineData("/Users/operator/.dotnet/tools/intent-cli", "dotnet-tool")]
    [InlineData("/usr/local/lib/node_modules/intent-system/bin/intent-cli", "npm-global")]
    [InlineData("/Users/operator/.npm/_npx/abc123/node_modules/intent-system/bin/intent-cli", "npx-cache")]
    [InlineData("/opt/intent-cli/intent-cli", "standalone")]
    public void Detector_ClassifiesSupportedRealPathShapes(string path, string expectedChannel)
    {
        var resolution = new ExecutablePathResolution(path, path, [path], null);

        var detection = UpdateChannelDetector.Detect(resolution);

        Assert.Equal(expectedChannel, detection.Channel);
        Assert.False(detection.Ambiguous);
        Assert.Contains("real path", detection.PathEvidence, StringComparison.Ordinal);
        Assert.Contains(path, detection.PathEvidence, StringComparison.Ordinal);
    }

    [Fact]
    public void Detector_FailsClosedForUnknownAndConflictingPaths()
    {
        var unknown = UpdateChannelDetector.Detect(ResolutionForPath("/opt/other-tool/other-command"));
        var ambiguous = UpdateChannelDetector.Detect(
            ResolutionForPath("/Users/operator/.dotnet/tools/node_modules/intent-system/bin/intent-cli"));

        Assert.Equal("unknown", unknown.Channel);
        Assert.False(unknown.Ambiguous);
        Assert.Contains("fail-closed", unknown.PathEvidence, StringComparison.Ordinal);
        Assert.Equal("unknown", ambiguous.Channel);
        Assert.True(ambiguous.Ambiguous);
        Assert.Contains("conflicting", ambiguous.DetectionError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotnetToolUpdate_EmitsDetectionBeforeSpawningExactCommand()
    {
        using var output = new StringWriter();
        var process = new RecordingProcessRunner(output);
        var dependencies = Dependencies(
            "/Users/operator/.dotnet/tools/intent-cli",
            processRunner: process,
            currentVersion: "1.0.0");

        var exitCode = UpdateCommand.Execute([], output, dependencies);

        Assert.Equal(0, exitCode);
        var invocation = Assert.Single(process.Invocations);
        Assert.Equal("dotnet", invocation.Executable);
        Assert.Equal(["tool", "update", "-g", "JTechJapan.IntentSystem.Cli"], invocation.Arguments);
        Assert.Contains("channel: `dotnet-tool`", process.OutputBeforeRun, StringComparison.Ordinal);
        Assert.Contains("dotnet tool update -g JTechJapan.IntentSystem.Cli", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NpmGlobalUpdate_UsesLatestPackageAndNpxIsGuidanceOnly()
    {
        using var npmOutput = new StringWriter();
        var npmProcess = new RecordingProcessRunner(npmOutput);
        var npmDependencies = Dependencies(
            "/usr/local/lib/node_modules/intent-system/bin/intent-cli",
            processRunner: npmProcess,
            currentVersion: "1.0.0");

        Assert.Equal(0, UpdateCommand.Execute([], npmOutput, npmDependencies));
        var npmInvocation = Assert.Single(npmProcess.Invocations);
        Assert.Equal("npm", npmInvocation.Executable);
        Assert.Equal(["install", "-g", "intent-system@latest"], npmInvocation.Arguments);

        using var npxOutput = new StringWriter();
        var npxProcess = new RecordingProcessRunner(npxOutput);
        var npxRelease = new RecordingReleaseClient();
        var npxDependencies = Dependencies(
            "/Users/operator/.npm/_npx/abc123/node_modules/intent-system/bin/intent-cli",
            processRunner: npxProcess,
            releaseClient: npxRelease,
            currentVersion: "1.0.0");

        Assert.Equal(0, UpdateCommand.Execute([], npxOutput, npxDependencies));
        Assert.Empty(npxProcess.Invocations);
        Assert.Equal(0, npxRelease.GetLatestCalls);
        Assert.Contains("guidance-only", npxOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("npx intent-system@latest <command>", npxOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckReportsCurrentLatestAndWouldBeActionWithoutProcessOrWrites()
    {
        using var output = new StringWriter();
        var process = new RecordingProcessRunner(output);
        var release = new RecordingReleaseClient();
        var dependencies = Dependencies(
            "/Users/operator/.dotnet/tools/intent-cli",
            processRunner: process,
            releaseClient: release,
            currentVersion: "1.0.0");

        var exitCode = UpdateCommand.Execute(["--check", "--format", "json"], output, dependencies);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("dotnet-tool", root.GetProperty("channel").GetString());
        var result = root.GetProperty("result");
        Assert.Equal("1.0.0", result.GetProperty("current_version").GetString());
        Assert.Equal("2.0.0", result.GetProperty("latest_version").GetString());
        Assert.Contains("dotnet tool update", result.GetProperty("would_be_action").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("check").GetBoolean());
        Assert.False(result.GetProperty("process_spawned").GetBoolean());
        Assert.False(result.GetProperty("writes_performed").GetBoolean());
        Assert.Empty(process.Invocations);
        Assert.Equal(1, release.GetLatestCalls);
    }

    [Fact]
    public void UnknownPathFailsClosedWithAllChannelManualGuidanceAndNoReleaseLookup()
    {
        using var output = new StringWriter();
        var release = new RecordingReleaseClient();
        var process = new RecordingProcessRunner(output);
        var dependencies = Dependencies(
            "/opt/other-tool/other-command",
            processRunner: process,
            releaseClient: release,
            currentVersion: "1.0.0");

        var exitCode = UpdateCommand.Execute(["--format", "json"], output, dependencies);

        Assert.Equal(1, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var root = document.RootElement;
        Assert.Equal("unknown", root.GetProperty("channel").GetString());
        var result = root.GetProperty("result");
        Assert.StartsWith("fail-closed", result.GetProperty("outcome").GetString(), StringComparison.Ordinal);
        var guidance = result.GetProperty("manual_guidance").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains(guidance, line => line!.StartsWith("dotnet tool update", StringComparison.Ordinal));
        Assert.Contains(guidance, line => line!.StartsWith("npm install", StringComparison.Ordinal));
        Assert.Contains(guidance, line => line!.StartsWith("npx intent-system", StringComparison.Ordinal));
        Assert.Contains(guidance, line => line!.Contains(".sha256", StringComparison.Ordinal));
        Assert.Empty(process.Invocations);
        Assert.Equal(0, release.GetLatestCalls);
    }

    [Fact]
    public void CorruptChecksumLeavesStandaloneBinaryByteIdentical()
    {
        var root = NewFixtureRoot("checksum");
        var target = Path.Combine(root, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        var original = "original-standalone-binary"u8.ToArray();
        File.WriteAllBytes(target, original);

        var archiveName = ArchiveName("2.0.0");
        var checksumName = $"{archiveName}.sha256";
        var archive = BuildArchive(target, "replacement-binary"u8.ToArray());
        var release = Release("2.0.0", archiveName, checksumName);
        var client = new RecordingReleaseClient(release, new Dictionary<string, byte[]>
        {
            [archiveName] = archive,
            [checksumName] = Encoding.UTF8.GetBytes(new string('0', 64) + "  " + archiveName + "\n")
        });
        var installer = new StandaloneUpdateInstaller(client);

        var exception = Assert.Throws<StandaloneUpdateException>(() => installer.Apply(target, release));

        Assert.False(exception.TargetWasModified);
        Assert.Contains("Checksum mismatch", exception.Message, StringComparison.Ordinal);
        Assert.Contains(target, exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(target));
    }

    [Fact]
    public void MismatchedChecksumFilenameLeavesStandaloneBinaryByteIdentical()
    {
        var root = NewFixtureRoot("checksum-name");
        var target = Path.Combine(root, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        var original = "original-standalone-binary"u8.ToArray();
        File.WriteAllBytes(target, original);

        var archiveName = ArchiveName("2.0.0");
        var checksumName = $"{archiveName}.sha256";
        var archive = BuildArchive(target, "replacement-binary"u8.ToArray());
        var digest = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var release = Release("2.0.0", archiveName, checksumName);
        var client = new RecordingReleaseClient(release, new Dictionary<string, byte[]>
        {
            [archiveName] = archive,
            [checksumName] = Encoding.UTF8.GetBytes($"{digest}  another-asset{Path.GetExtension(archiveName)}\n")
        });

        var exception = Assert.Throws<StandaloneUpdateException>(
            () => new StandaloneUpdateInstaller(client).Apply(target, release));

        Assert.False(exception.TargetWasModified);
        Assert.Contains("names", exception.Message, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllBytes(target));
    }

    [Fact]
    public void UpdateCommand_ChecksumFailureReportsNoTargetWrite()
    {
        var root = NewFixtureRoot("checksum-result");
        var target = Path.Combine(root, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        var original = "original-standalone-binary"u8.ToArray();
        File.WriteAllBytes(target, original);

        var archiveName = ArchiveName("2.0.0");
        var checksumName = $"{archiveName}.sha256";
        var archive = BuildArchive(target, "replacement-binary"u8.ToArray());
        var release = Release("2.0.0", archiveName, checksumName);
        var client = new RecordingReleaseClient(release, new Dictionary<string, byte[]>
        {
            [archiveName] = archive,
            [checksumName] = Encoding.UTF8.GetBytes(new string('0', 64) + "  " + archiveName + "\n")
        });
        var dependencies = Dependencies(
            target,
            releaseClient: client,
            currentVersion: "1.0.0",
            standaloneInstaller: new StandaloneUpdateInstaller(client));
        using var output = new StringWriter();

        Assert.Equal(1, UpdateCommand.Execute(["--format", "json"], output, dependencies));
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("writes_performed").GetBoolean());
        Assert.True(result.GetProperty("target_untouched_on_failure").GetBoolean());
        Assert.Equal(original, File.ReadAllBytes(target));
    }

    [Fact]
    public void StandaloneUpdate_VerifiesThenUsesTempRenameAndReplacesTarget()
    {
        var root = NewFixtureRoot("success");
        var target = Path.Combine(root, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        File.WriteAllBytes(target, "original"u8.ToArray());

        var archiveName = ArchiveName("2.0.0");
        var checksumName = $"{archiveName}.sha256";
        var archive = BuildArchive(target, "replacement-binary"u8.ToArray());
        var digest = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var release = Release("2.0.0", archiveName, checksumName);
        var client = new RecordingReleaseClient(release, new Dictionary<string, byte[]>
        {
            [archiveName] = archive,
            [checksumName] = Encoding.UTF8.GetBytes($"{digest}  {archiveName}\n")
        });

        var outcome = new StandaloneUpdateInstaller(client).Apply(target, release);

        Assert.Equal(archiveName, outcome.ArchiveName);
        Assert.Equal(digest, outcome.ExpectedChecksum);
        Assert.Equal(digest, outcome.ActualChecksum);
        Assert.Equal("replacement-binary", File.ReadAllText(target));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(root), path =>
            Path.GetFileName(path).StartsWith(".intent-cli-update-", StringComparison.Ordinal));
    }

    [Fact]
    public void WindowsSafeReplacerUsesReplacementSemanticsWithoutInPlaceWrite()
    {
        var root = NewFixtureRoot("windows-swap");
        var target = Path.Combine(root, "intent-cli.exe");
        var staged = Path.Combine(root, "intent-cli.exe.new");
        File.WriteAllBytes(target, "old"u8.ToArray());
        File.WriteAllBytes(staged, "new"u8.ToArray());

        // Model a running Windows apphost: the process keeps a read handle,
        // but permits delete/replace sharing. The replacement must still use
        // the separate staged path rather than writing through the handle.
        using (var runningHandle = new FileStream(
                   target,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            AtomicStandaloneBinaryReplacer.Replace(target, staged, windows: true);
        }

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(staged));
    }

    private static UpdateDependencies Dependencies(
        string executablePath,
        IUpdateProcessRunner? processRunner = null,
        IUpdateReleaseClient? releaseClient = null,
        string currentVersion = "1.0.0",
        IStandaloneUpdateInstaller? standaloneInstaller = null)
    {
        releaseClient ??= new RecordingReleaseClient();
        return new UpdateDependencies
        {
            ExecutablePathResolver = new StaticPathResolver(executablePath),
            ReleaseClient = releaseClient,
            ProcessRunner = processRunner ?? new RecordingProcessRunner(new StringWriter()),
            StandaloneInstaller = standaloneInstaller ?? new FakeStandaloneInstaller(),
            CurrentVersionProvider = () => currentVersion
        };
    }

    private static ExecutablePathResolution ResolutionForPath(string value) =>
        new(value, value, [value], null);

    private static string NewFixtureRoot(string name)
    {
        var root = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            ".artifacts",
            $"g703-update-tests-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ArchiveName(string version)
    {
        var rid = StandaloneUpdateInstaller.CurrentRuntimeIdentifier();
        return $"intent-cli-{version}-{rid}{(rid == "win-x64" ? ".zip" : ".tar.gz")}";
    }

    private static UpdateRelease Release(string version, string archiveName, string checksumName) =>
        new(
            $"v{version}",
            version,
            [
                new UpdateReleaseAsset(archiveName, new Uri($"https://example.invalid/{archiveName}")),
                new UpdateReleaseAsset(checksumName, new Uri($"https://example.invalid/{checksumName}"))
            ]);

    private static byte[] BuildArchive(string targetPath, byte[] contents)
    {
        var binaryName = System.IO.Path.GetFileName(targetPath);
        using var output = new MemoryStream();
        if (binaryName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            using (var entry = zip.CreateEntry(binaryName).Open())
            {
                entry.Write(contents, 0, contents.Length);
            }
        }
        else
        {
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            using (var tar = new TarWriter(gzip, leaveOpen: true))
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, binaryName)
                {
                    DataStream = new MemoryStream(contents)
                };
                tar.WriteEntry(entry);
            }
        }

        return output.ToArray();
    }

    private sealed class StaticPathResolver(string path) : IExecutablePathResolver
    {
        public ExecutablePathResolution Resolve() => ResolutionForPath(path);
    }

    private sealed class RecordingProcessRunner(StringWriter output) : IUpdateProcessRunner
    {
        public List<(string Executable, IReadOnlyList<string> Arguments)> Invocations { get; } = [];

        public string OutputBeforeRun { get; private set; } = string.Empty;

        public UpdateProcessResult Run(string executable, IReadOnlyList<string> arguments)
        {
            OutputBeforeRun = output.ToString();
            Invocations.Add((executable, arguments.ToArray()));
            return new UpdateProcessResult(0, "updated", string.Empty);
        }
    }

    private sealed class RecordingReleaseClient : IUpdateReleaseClient
    {
        private readonly IReadOnlyDictionary<string, byte[]> downloads;

        public RecordingReleaseClient(
            UpdateRelease? release = null,
            IReadOnlyDictionary<string, byte[]>? downloads = null)
        {
            Release = release ?? new UpdateRelease("v2.0.0", "2.0.0", []);
            this.downloads = downloads ?? new Dictionary<string, byte[]>();
        }

        public UpdateRelease Release { get; }

        public int GetLatestCalls { get; private set; }

        public UpdateRelease GetLatestRelease()
        {
            GetLatestCalls++;
            return Release;
        }

        public void Download(UpdateReleaseAsset asset, Stream destination)
        {
            if (!downloads.TryGetValue(asset.Name, out var bytes))
            {
                throw new InvalidOperationException($"No fake download for {asset.Name}.");
            }

            destination.Write(bytes, 0, bytes.Length);
        }
    }

    private sealed class FakeStandaloneInstaller : IStandaloneUpdateInstaller
    {
        public StandaloneUpdateOutcome Apply(string targetPath, UpdateRelease release) =>
            new("fake", "fake", "fake", targetPath);
    }
}

public sealed class GuideOnboardingG703Tests
{
    [Fact]
    public void OnboardingJsonAndMarkdownExposeTheSameMetadataFreeUpdateRoute()
    {
        using var jsonWriter = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = RepoVersionPolicySource.RepoRoot(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };

        Assert.Equal(0, GuideOnboardingCommand.Execute(context, ["--format", "json"], jsonWriter));
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var guidance = document.RootElement.GetProperty("update_channel_guidance");
        Assert.Equal("intent-cli update --check --format json", guidance.GetProperty("json_command").GetString());
        Assert.Equal("intent-cli update --check --format markdown", guidance.GetProperty("markdown_command").GetString());
        Assert.Contains("fully resolved executable", guidance.GetProperty("contract").GetString(), StringComparison.Ordinal);
        Assert.Contains("process_spawned=false", guidance.GetProperty("check_safety").GetString(), StringComparison.Ordinal);

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(context, [], markdownWriter));
        Assert.Contains("## Channel-aware update route (G703)", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("intent-cli update --check --format json", markdownWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("intent-cli update --check --format markdown", markdownWriter.ToString(), StringComparison.Ordinal);
    }
}

public sealed class UpdateDocsG703Tests
{
    [Fact]
    public void EnglishAndJapaneseDistributionDocsCarryTheChannelAndCheckContract()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "13-npm-distribution.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "13-npm-distribution.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("intent-cli update", document, StringComparison.Ordinal);
            Assert.Contains("dotnet tool update -g JTechJapan.IntentSystem.Cli", document, StringComparison.Ordinal);
            Assert.Contains("npm install -g intent-system@latest", document, StringComparison.Ordinal);
            Assert.Contains("npx intent-system@latest", document, StringComparison.Ordinal);
            Assert.Contains(".sha256", document, StringComparison.Ordinal);
            Assert.Contains("temp+rename", document, StringComparison.Ordinal);
            Assert.Contains("--check", document, StringComparison.Ordinal);
        }

        Assert.Contains("process_spawned=false", english, StringComparison.Ordinal);
        Assert.Contains("process_spawned=false", japanese, StringComparison.Ordinal);
        Assert.Contains("fail closed", english, StringComparison.Ordinal);
        Assert.Contains("fail closed", japanese, StringComparison.Ordinal);
    }
}
