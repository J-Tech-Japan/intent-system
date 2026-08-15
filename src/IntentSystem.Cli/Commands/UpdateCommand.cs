using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The installation channels understood by G703. The channel is deliberately
/// not persisted: every invocation derives it again from the real executable
/// path so a copied or moved binary cannot carry stale installation metadata.
/// </summary>
internal enum UpdateChannel
{
    Unknown,
    DotnetTool,
    NpmGlobal,
    Npx,
    Standalone
}

internal static class UpdateChannelName
{
    internal static string For(UpdateChannel channel) => channel switch
    {
        UpdateChannel.DotnetTool => "dotnet-tool",
        UpdateChannel.NpmGlobal => "npm-global",
        UpdateChannel.Npx => "npx-cache",
        UpdateChannel.Standalone => "standalone",
        _ => "unknown"
    };
}

internal sealed record ExecutablePathResolution(
    string? OriginalPath,
    string? ResolvedPath,
    IReadOnlyList<string> PathHops,
    string? Error);

internal interface IExecutablePathResolver
{
    ExecutablePathResolution Resolve();
}

/// <summary>
/// Resolves the running process path and follows filesystem links before the
/// detector sees it. It performs no writes and keeps the complete path chain as
/// evidence for the operator.
/// </summary>
internal sealed class ProcessExecutablePathResolver : IExecutablePathResolver
{
    private const int MaximumLinkHops = 32;

    public ExecutablePathResolution Resolve() => ResolvePath(Environment.ProcessPath);

    internal static ExecutablePathResolution ResolvePath(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return new ExecutablePathResolution(null, null, Array.Empty<string>(), "Environment.ProcessPath was empty.");
        }

        try
        {
            var current = Path.GetFullPath(original);
            var hops = new List<string> { current };
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { current };

            for (var hop = 0; hop < MaximumLinkHops; hop++)
            {
                var link = new FileInfo(current).ResolveLinkTarget(returnFinalTarget: false);
                if (link is null)
                {
                    return new ExecutablePathResolution(original, current, hops, null);
                }

                var target = link.FullName;
                if (!Path.IsPathRooted(target))
                {
                    target = Path.GetFullPath(target, Path.GetDirectoryName(current)!);
                }

                if (!visited.Add(target))
                {
                    hops.Add(target);
                    return new ExecutablePathResolution(
                        original,
                        target,
                        hops,
                        "The executable path contains a symlink cycle.");
                }

                current = target;
                hops.Add(current);
            }

            return new ExecutablePathResolution(
                original,
                current,
                hops,
                $"The executable path exceeded the {MaximumLinkHops}-link resolution bound.");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return new ExecutablePathResolution(original, null, new[] { original }, exception.Message);
        }
    }
}

internal sealed record UpdateChannelDetection
{
    [JsonIgnore]
    internal UpdateChannel Kind { get; init; }

    [JsonPropertyName("channel")]
    public required string Channel { get; init; }

    [JsonPropertyName("executable_path")]
    public string? ExecutablePath { get; init; }

    [JsonPropertyName("resolved_executable_path")]
    public string? ResolvedExecutablePath { get; init; }

    [JsonPropertyName("path_hops")]
    public required IReadOnlyList<string> PathHops { get; init; }

    [JsonPropertyName("path_evidence")]
    public required string PathEvidence { get; init; }

    [JsonPropertyName("ambiguous")]
    public bool Ambiguous { get; init; }

    [JsonPropertyName("detection_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DetectionError { get; init; }
}

/// <summary>
/// Pure path-shape classification. Only the fully resolved path is classified;
/// the original path is retained for evidence. A path that is not clearly a
/// supported intent-cli executable or carries conflicting channel markers is
/// fail-closed rather than guessed.
/// </summary>
internal static class UpdateChannelDetector
{
    internal static UpdateChannelDetection Detect(ExecutablePathResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        if (string.IsNullOrWhiteSpace(resolution.ResolvedPath))
        {
            return Unknown(resolution, ambiguous: false, resolution.Error ?? "The executable real path could not be resolved.");
        }

        if (!string.IsNullOrWhiteSpace(resolution.Error))
        {
            return Unknown(
                resolution,
                ambiguous: false,
                $"The executable real path could not be fully resolved: {resolution.Error}");
        }

        var path = resolution.ResolvedPath!;
        var normalized = path.Replace('\\', '/');
        var segments = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fileName = Path.GetFileName(path);
        var isIntentCli = string.Equals(fileName, "intent-cli", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "intent-cli.exe", StringComparison.OrdinalIgnoreCase);

        if (!isIntentCli)
        {
            return Unknown(
                resolution,
                ambiguous: false,
                $"The resolved executable name '{fileName}' is not intent-cli or intent-cli.exe.");
        }

        var dotnetTool = HasAdjacentSegments(segments, ".dotnet", "tools");
        var npxCache = segments.Any(segment =>
            string.Equals(segment, "_npx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, ".npx", StringComparison.OrdinalIgnoreCase));
        var npmPackage = HasIntentNpmPackage(segments);

        // An npx cache path necessarily contains both `_npx` and
        // `node_modules/<intent-system package>`; those two markers describe
        // one channel, not an ambiguity. Dotnet plus either npm marker is a
        // genuine conflict and must fail closed.
        if (npxCache && !npmPackage)
        {
            return Unknown(
                resolution,
                ambiguous: false,
                "The npx cache marker was present without the intent-system npm package marker.");
        }

        if (dotnetTool && (npxCache || npmPackage))
        {
            return Unknown(
                resolution,
                ambiguous: true,
                "The resolved path contains conflicting installation-channel markers.");
        }

        var kind = dotnetTool
            ? UpdateChannel.DotnetTool
            : npxCache
                ? UpdateChannel.Npx
                : npmPackage
                    ? UpdateChannel.NpmGlobal
                    : UpdateChannel.Standalone;

        var evidence = BuildEvidence(
            resolution,
            kind,
            dotnetTool,
            npmPackage,
            npxCache,
            fileName);

        return new UpdateChannelDetection
        {
            Kind = kind,
            Channel = UpdateChannelName.For(kind),
            ExecutablePath = resolution.OriginalPath,
            ResolvedExecutablePath = resolution.ResolvedPath,
            PathHops = resolution.PathHops,
            PathEvidence = evidence,
            Ambiguous = false,
            DetectionError = resolution.Error
        };
    }

    private static bool HasIntentNpmPackage(IReadOnlyList<string> segments)
    {
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!string.Equals(segments[index], "node_modules", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var package = segments[index + 1];
            if (string.Equals(package, "intent-system", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(package, "@j-tech-japan", StringComparison.OrdinalIgnoreCase)
                && index + 2 < segments.Count
                && segments[index + 2].StartsWith("intent-cli-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAdjacentSegments(IReadOnlyList<string> segments, string first, string second)
    {
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (string.Equals(segments[index], first, StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[index + 1], second, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildEvidence(
        ExecutablePathResolution resolution,
        UpdateChannel kind,
        bool dotnetTool,
        bool npmPackage,
        bool npxCache,
        string fileName)
    {
        var hops = resolution.PathHops.Count == 0
            ? "none"
            : string.Join(" -> ", resolution.PathHops.Select(path => $"'{path}'"));
        return $"real path resolved through {hops}; executable={fileName}; "
            + $"markers(dotnet-tools={dotnetTool}, npm-package={npmPackage}, npx-cache={npxCache}); "
            + $"selected={UpdateChannelName.For(kind)}";
    }

    private static UpdateChannelDetection Unknown(
        ExecutablePathResolution resolution,
        bool ambiguous,
        string error)
    {
        var path = resolution.ResolvedPath ?? resolution.OriginalPath ?? "<unavailable>";
        var evidence = $"real path observed as '{path}'; "
            + (resolution.PathHops.Count > 0
                ? $"resolution hops={string.Join(" -> ", resolution.PathHops.Select(hop => $"'{hop}'"))}; "
                : string.Empty)
            + $"classification=fail-closed; reason={error}";
        return new UpdateChannelDetection
        {
            Kind = UpdateChannel.Unknown,
            Channel = UpdateChannelName.For(UpdateChannel.Unknown),
            ExecutablePath = resolution.OriginalPath,
            ResolvedExecutablePath = resolution.ResolvedPath,
            PathHops = resolution.PathHops,
            PathEvidence = evidence,
            Ambiguous = ambiguous,
            DetectionError = error
        };
    }
}

internal sealed record UpdateReleaseAsset(string Name, Uri DownloadUrl);

internal sealed record UpdateRelease(
    string TagName,
    string Version,
    IReadOnlyList<UpdateReleaseAsset> Assets);

internal interface IUpdateReleaseClient
{
    UpdateRelease GetLatestRelease();

    void Download(UpdateReleaseAsset asset, Stream destination);
}

/// <summary>Public GitHub Releases adapter used only by the update command.</summary>
internal sealed class GitHubUpdateReleaseClient : IUpdateReleaseClient
{
    private const string LatestReleaseUri =
        "https://api.github.com/repos/J-Tech-Japan/intent-system/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    public UpdateRelease GetLatestRelease()
    {
        using var response = Client.GetAsync(
                LatestReleaseUri,
                HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter()
            .GetResult();
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream();
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(tagName))
        {
            throw new InvalidOperationException("GitHub latest release did not contain tag_name.");
        }

        var assets = new List<UpdateReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement))
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(name)
                    && Uri.TryCreate(url, UriKind.Absolute, out var downloadUrl))
                {
                    assets.Add(new UpdateReleaseAsset(name, downloadUrl));
                }
            }
        }

        return new UpdateRelease(tagName, NormalizeVersion(tagName), assets);
    }

    public void Download(UpdateReleaseAsset asset, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(destination);

        using var response = Client.GetAsync(
                asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead)
            .GetAwaiter()
            .GetResult();
        response.EnsureSuccessStatusCode();
        using var source = response.Content.ReadAsStream();
        source.CopyTo(destination);
    }

    internal static string NormalizeVersion(string tagName) =>
        tagName.StartsWith('v') ? tagName[1..] : tagName;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("intent-cli-update", "G703"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

internal sealed record UpdateProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface IUpdateProcessRunner
{
    UpdateProcessResult Run(string executable, IReadOnlyList<string> arguments);
}

internal sealed class ProcessUpdateRunner : IUpdateProcessRunner
{
    public UpdateProcessResult Run(string executable, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new UpdateProcessResult(-1, string.Empty, $"Could not start {executable}.");
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new UpdateProcessResult(
                process.ExitCode,
                stdout.GetAwaiter().GetResult(),
                stderr.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException)
        {
            return new UpdateProcessResult(-1, string.Empty, exception.Message);
        }
    }
}

internal sealed record StandaloneUpdateOutcome(
    string ArchiveName,
    string ExpectedChecksum,
    string ActualChecksum,
    string TargetPath);

internal interface IStandaloneBinaryReplacer
{
    void Replace(string targetPath, string stagedPath);
}

/// <summary>
/// Uses an atomic same-volume replacement. Windows gets File.Replace, which
/// preserves the rename/replace semantics needed by a running installation;
/// unsupported filesystems fall back only when the platform explicitly says
/// File.Replace is unavailable. The current binary is never written in place.
/// </summary>
internal sealed class AtomicStandaloneBinaryReplacer : IStandaloneBinaryReplacer
{
    public void Replace(string targetPath, string stagedPath) =>
        Replace(targetPath, stagedPath, OperatingSystem.IsWindows());

    internal static void Replace(string targetPath, string stagedPath, bool windows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedPath);

        if (windows && File.Exists(targetPath))
        {
            try
            {
                File.Replace(stagedPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // Some Windows-compatible filesystems do not implement the
                // Replace API. MoveFileEx-style overwrite is the safe fallback
                // on those filesystems; never stream bytes into the target.
            }
        }

        File.Move(stagedPath, targetPath, overwrite: true);
    }
}

internal sealed class StandaloneUpdateException : InvalidOperationException
{
    internal StandaloneUpdateException(
        string message,
        string targetPath,
        bool targetWasModified,
        string? expectedChecksum = null,
        string? actualChecksum = null)
        : base(message)
    {
        TargetPath = targetPath;
        TargetWasModified = targetWasModified;
        ExpectedChecksum = expectedChecksum;
        ActualChecksum = actualChecksum;
    }

    internal string TargetPath { get; }

    internal bool TargetWasModified { get; }

    internal string? ExpectedChecksum { get; }

    internal string? ActualChecksum { get; }
}

/// <summary>
/// Downloads the release archive and sidecar into a sibling staging directory,
/// verifies the archive before extracting or replacing the running binary, and
/// then performs one same-volume temp+rename replacement. The original target
/// is untouched on every pre-replacement failure.
/// </summary>
internal sealed class StandaloneUpdateInstaller : IStandaloneUpdateInstaller
{
    private readonly IUpdateReleaseClient releaseClient;
    private readonly IStandaloneBinaryReplacer replacer;

    internal StandaloneUpdateInstaller(
        IUpdateReleaseClient releaseClient,
        IStandaloneBinaryReplacer? replacer = null)
    {
        this.releaseClient = releaseClient ?? throw new ArgumentNullException(nameof(releaseClient));
        this.replacer = replacer ?? new AtomicStandaloneBinaryReplacer();
    }

    public StandaloneUpdateOutcome Apply(string targetPath, UpdateRelease release)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(release);

        if (!File.Exists(targetPath))
        {
            throw new StandaloneUpdateException(
                $"The standalone executable '{targetPath}' does not exist; no replacement was attempted.",
                targetPath,
                targetWasModified: false);
        }

        var (archive, checksum) = SelectAssets(release);
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new StandaloneUpdateException(
                $"Could not determine the standalone executable directory for '{targetPath}'; no replacement was attempted.",
                targetPath,
                targetWasModified: false);
        }

        var stagingRoot = Path.Combine(
            targetDirectory,
            $".intent-cli-update-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(stagingRoot, archive.Name);
        var checksumPath = Path.Combine(stagingRoot, checksum.Name);
        var extractionRoot = Path.Combine(stagingRoot, "extract");
        var stagedBinaryPath = Path.Combine(stagingRoot, "replacement", Path.GetFileName(targetPath));
        var replaced = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);
            using (var archiveStream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                releaseClient.Download(archive, archiveStream);
            }

            using (var checksumStream = new FileStream(checksumPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                releaseClient.Download(checksum, checksumStream);
            }

            var expectedChecksum = ReadExpectedChecksum(checksumPath, archive.Name, targetPath);
            var actualChecksum = ComputeSha256(archivePath);
            if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new StandaloneUpdateException(
                    $"Checksum mismatch for release asset '{archive.Name}': expected {expectedChecksum}, actual {actualChecksum}. "
                    + $"The original standalone binary '{targetPath}' was left byte-identical; no replacement was attempted.",
                    targetPath,
                    targetWasModified: false,
                    expectedChecksum,
                    actualChecksum);
            }

            Directory.CreateDirectory(extractionRoot);
            ExtractArchive(archive.Name, archivePath, extractionRoot);
            var expectedBinaryName = Path.GetFileName(targetPath);
            var extractedBinaries = Directory.EnumerateFiles(
                    extractionRoot,
                    expectedBinaryName,
                    SearchOption.AllDirectories)
                .ToArray();
            if (extractedBinaries.Length != 1)
            {
                throw new StandaloneUpdateException(
                    $"Release asset '{archive.Name}' did not contain exactly one '{expectedBinaryName}' binary; "
                    + $"found {extractedBinaries.Length}. The original binary '{targetPath}' was left untouched.",
                    targetPath,
                    targetWasModified: false,
                    expectedChecksum,
                    actualChecksum);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(stagedBinaryPath)!);
            File.Copy(extractedBinaries[0], stagedBinaryPath, overwrite: true);
            PreserveUnixMode(extractedBinaries[0], stagedBinaryPath);
            replacer.Replace(targetPath, stagedBinaryPath);
            replaced = true;

            return new StandaloneUpdateOutcome(
                archive.Name,
                expectedChecksum,
                actualChecksum,
                targetPath);
        }
        catch (StandaloneUpdateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or CryptographicException
            or NotSupportedException)
        {
            var untouched = !replaced;
            throw new StandaloneUpdateException(
                $"Standalone update failed for '{targetPath}': {exception.Message}. "
                + (untouched
                    ? "The original binary was left untouched."
                    : "The replacement had already completed."),
                targetPath,
                targetWasModified: replaced);
        }
        finally
        {
            TryDeleteOwnedStagingDirectory(stagingRoot);
        }
    }

    internal static (UpdateReleaseAsset Archive, UpdateReleaseAsset Checksum) SelectAssets(UpdateRelease release)
    {
        var rid = CurrentRuntimeIdentifier();
        var extension = rid == "win-x64" ? ".zip" : ".tar.gz";
        var archiveName = $"intent-cli-{release.Version}-{rid}{extension}";
        var checksumName = $"{archiveName}.sha256";
        var archive = release.Assets.SingleOrDefault(asset =>
            string.Equals(asset.Name, archiveName, StringComparison.Ordinal));
        var checksum = release.Assets.SingleOrDefault(asset =>
            string.Equals(asset.Name, checksumName, StringComparison.Ordinal));
        if (archive is null || checksum is null)
        {
            throw new InvalidOperationException(
                $"Latest release '{release.TagName}' is missing the standalone assets '{archiveName}' and/or '{checksumName}'.");
        }

        return (archive, checksum);
    }

    internal static string CurrentRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
        {
            return "win-x64";
        }

        if (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64)
        {
            return "linux-x64";
        }

        if (OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
        {
            return "osx-arm64";
        }

        throw new PlatformNotSupportedException(
            $"No self-contained intent-cli release asset is defined for {System.Runtime.InteropServices.RuntimeInformation.OSDescription} / {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}.");
    }

    private static string ReadExpectedChecksum(string path, string archiveName, string targetPath)
    {
        var line = File.ReadLines(path)
            .Select(value => value.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var tokens = line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var token = tokens?.FirstOrDefault();
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
        {
            throw new StandaloneUpdateException(
                $"Checksum sidecar for '{archiveName}' did not contain a 64-character SHA-256 digest. "
                + $"The original binary '{targetPath}' was left untouched.",
                targetPath,
                targetWasModified: false);
        }

        if (tokens!.Length > 1)
        {
            var reportedName = tokens[1].TrimStart('*').Replace('\\', '/');
            if (!string.Equals(
                    Path.GetFileName(reportedName),
                    archiveName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new StandaloneUpdateException(
                    $"Checksum sidecar '{Path.GetFileName(path)}' names '{tokens[1]}' instead of '{archiveName}'. "
                    + $"The original binary '{targetPath}' was left untouched.",
                    targetPath,
                    targetWasModified: false);
            }
        }

        return token.ToLowerInvariant();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void ExtractArchive(string name, string archivePath, string extractionRoot)
    {
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractionRoot, overwriteFiles: true);
            return;
        }

        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = File.OpenRead(archivePath);
            using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, extractionRoot, overwriteFiles: true);
            return;
        }

        throw new InvalidDataException($"Unsupported standalone release archive '{name}'.");
    }

    private static void PreserveUnixMode(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
        catch (PlatformNotSupportedException)
        {
            // The bytes and atomic replacement remain valid on filesystems
            // that do not expose Unix mode bits.
        }
    }

    private static void TryDeleteOwnedStagingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A failed cleanup must not turn a successful verified swap into
            // an ambiguous update result. The directory is uniquely named and
            // owned by this invocation.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as the IOException case above.
        }
    }
}

internal interface IStandaloneUpdateInstaller
{
    StandaloneUpdateOutcome Apply(string targetPath, UpdateRelease release);
}

internal sealed class UpdateDependencies
{
    internal required IExecutablePathResolver ExecutablePathResolver { get; init; }

    internal required IUpdateReleaseClient ReleaseClient { get; init; }

    internal required IUpdateProcessRunner ProcessRunner { get; init; }

    internal required IStandaloneUpdateInstaller StandaloneInstaller { get; init; }

    internal required Func<string> CurrentVersionProvider { get; init; }

    internal static UpdateDependencies CreateDefault()
    {
        var releaseClient = new GitHubUpdateReleaseClient();
        return new UpdateDependencies
        {
            ExecutablePathResolver = new ProcessExecutablePathResolver(),
            ReleaseClient = releaseClient,
            ProcessRunner = new ProcessUpdateRunner(),
            StandaloneInstaller = new StandaloneUpdateInstaller(releaseClient),
            CurrentVersionProvider = VersionCommand.GetCurrentPackageVersion
        };
    }
}

internal sealed record UpdateOperationResult
{
    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("current_version")]
    public string? CurrentVersion { get; init; }

    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; init; }

    [JsonPropertyName("would_be_action")]
    public required string WouldBeAction { get; init; }

    [JsonPropertyName("action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Action { get; init; }

    [JsonPropertyName("check")]
    public bool Check { get; init; }

    [JsonPropertyName("process_spawned")]
    public bool ProcessSpawned { get; init; }

    [JsonPropertyName("writes_performed")]
    public bool WritesPerformed { get; init; }

    [JsonPropertyName("target_replaced")]
    public bool TargetReplaced { get; init; }

    [JsonPropertyName("target_untouched_on_failure")]
    public bool TargetUntouchedOnFailure { get; init; }

    [JsonPropertyName("command")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; init; }

    [JsonPropertyName("process_exit_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessExitCode { get; init; }

    [JsonPropertyName("process_stdout")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessStandardOutput { get; init; }

    [JsonPropertyName("process_stderr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessStandardError { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }

    [JsonPropertyName("manual_guidance")]
    public required IReadOnlyList<string> ManualGuidance { get; init; }

    [JsonIgnore]
    internal int ExitCode { get; init; }
}

internal static class UpdateManualGuidance
{
    internal static readonly IReadOnlyList<string> AllChannels =
    [
        "dotnet tool update -g JTechJapan.IntentSystem.Cli",
        "npm install -g intent-system@latest",
        "npx intent-system@latest <command>",
        "Download the matching intent-cli release asset, verify its .sha256 sidecar, then replace the standalone binary through a temp+rename swap."
    ];

    internal static readonly IReadOnlyList<string> Standalone =
    [
        "Download the matching intent-cli release asset and its .sha256 sidecar.",
        "Verify the archive SHA-256 before replacing the binary; if it mismatches, keep the original binary byte-identical and retry from the latest release."
    ];
}

/// <summary>
/// G703 channel-aware update command. It is intentionally independent of
/// <see cref="CliContext"/> so it can run from a bare metadata-free directory.
/// </summary>
internal static class UpdateCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";
    private const string PackageId = "JTechJapan.IntentSystem.Cli";
    private const string NpmPackage = "intent-system";
    private const string UsageLine =
        "Usage: intent-cli update [--check] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    internal static bool IsUpdateRequest(string[] args) =>
        args is { Length: > 0 }
        && string.Equals(args[0], "update", StringComparison.Ordinal);

    internal static int Execute(
        string[] args,
        TextWriter writer,
        UpdateDependencies? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine("intent-cli update");
            writer.WriteLine(UsageLine);
            writer.WriteLine("Derives the channel from the fully resolved executable path on every run.");
            writer.WriteLine("Use --check for current/latest/would-be action with no process spawn or writes.");
            return 0;
        }

        if (!TryParseArguments(args, out var check, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        dependencies ??= UpdateDependencies.CreateDefault();
        var resolution = dependencies.ExecutablePathResolver.Resolve();
        var detection = UpdateChannelDetector.Detect(resolution);
        var json = string.Equals(format, FormatJson, StringComparison.Ordinal);
        if (json)
        {
            BeginJsonEnvelope(writer, detection);
        }
        else
        {
            WriteDetectionMarkdown(writer, detection);
            writer.Flush();
        }

        UpdateOperationResult result;
        try
        {
            result = ExecuteOperation(check, detection, dependencies);
        }
        catch (StandaloneUpdateException exception)
        {
            result = new UpdateOperationResult
            {
                Outcome = "checksum-or-standalone-failure",
                CurrentVersion = TryGetCurrentVersion(dependencies),
                WouldBeAction = "verified standalone release asset + temp+rename swap",
                Action = "standalone checksum-verified replacement",
                Check = check,
                WritesPerformed = exception.TargetWasModified,
                TargetReplaced = exception.TargetWasModified,
                TargetUntouchedOnFailure = !exception.TargetWasModified,
                Error = exception.Message,
                ManualGuidance = UpdateManualGuidance.Standalone,
                ExitCode = 1
            };
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException
            or InvalidDataException)
        {
            result = new UpdateOperationResult
            {
                Outcome = check ? "check-failed" : "update-failed",
                CurrentVersion = TryGetCurrentVersion(dependencies),
                WouldBeAction = WouldBeAction(detection.Kind, latestVersion: null),
                Check = check,
                ProcessSpawned = false,
                WritesPerformed = false,
                Error = exception.Message,
                ManualGuidance = detection.Kind == UpdateChannel.Standalone
                    ? UpdateManualGuidance.Standalone
                    : UpdateManualGuidance.AllChannels,
                ExitCode = 1
            };
        }

        if (json)
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine("}");
        }
        else
        {
            WriteResultMarkdown(writer, result);
        }

        return result.ExitCode;
    }

    private static UpdateOperationResult ExecuteOperation(
        bool check,
        UpdateChannelDetection detection,
        UpdateDependencies dependencies)
    {
        if (detection.Kind == UpdateChannel.Unknown)
        {
            return new UpdateOperationResult
            {
                Outcome = detection.Ambiguous ? "fail-closed-ambiguous-path" : "fail-closed-unknown-path",
                WouldBeAction = "manual channel selection required",
                Check = check,
                Error = detection.DetectionError ?? "The installation channel could not be determined.",
                ManualGuidance = UpdateManualGuidance.AllChannels,
                ExitCode = 1
            };
        }

        var currentVersion = TryGetCurrentVersion(dependencies);
        if (!check && detection.Kind == UpdateChannel.Npx)
        {
            return new UpdateOperationResult
            {
                Outcome = "guidance-only",
                CurrentVersion = currentVersion,
                WouldBeAction = WouldBeAction(UpdateChannel.Npx, latestVersion: null),
                Action = "npx intent-system@latest <command>",
                Check = false,
                ProcessSpawned = false,
                WritesPerformed = false,
                ManualGuidance = ["npx intent-system@latest <command>", "For a persistent install, run: npm install -g intent-system@latest."],
                ExitCode = 0
            };
        }

        var release = dependencies.ReleaseClient.GetLatestRelease();
        var latestVersion = release.Version;
        var wouldBeAction = WouldBeAction(detection.Kind, latestVersion);
        if (check)
        {
            var checkCurrent = NormalizeComparableVersion(currentVersion);
            var checkLatest = NormalizeComparableVersion(latestVersion);
            var noOp = checkCurrent is not null
                && checkLatest is not null
                && string.Equals(checkCurrent, checkLatest, StringComparison.OrdinalIgnoreCase);
            return new UpdateOperationResult
            {
                Outcome = "check",
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                WouldBeAction = noOp ? "no action — current version is latest" : wouldBeAction,
                Check = true,
                ProcessSpawned = false,
                WritesPerformed = false,
                ManualGuidance = Array.Empty<string>(),
                ExitCode = 0
            };
        }

        if (NormalizeComparableVersion(currentVersion) is { } current
            && NormalizeComparableVersion(latestVersion) is { } latest
            && string.Equals(current, latest, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateOperationResult
            {
                Outcome = "already-current",
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                WouldBeAction = "no action — current version is latest",
                Check = false,
                ManualGuidance = Array.Empty<string>(),
                ExitCode = 0
            };
        }

        return detection.Kind switch
        {
            UpdateChannel.DotnetTool => RunPackageManagerUpdate(
                "dotnet",
                ["tool", "update", "-g", PackageId],
                currentVersion,
                latestVersion,
                wouldBeAction,
                dependencies.ProcessRunner),
            UpdateChannel.NpmGlobal => RunPackageManagerUpdate(
                "npm",
                ["install", "-g", $"{NpmPackage}@latest"],
                currentVersion,
                latestVersion,
                wouldBeAction,
                dependencies.ProcessRunner),
            UpdateChannel.Standalone => RunStandaloneUpdate(
                detection,
                currentVersion,
                latestVersion,
                wouldBeAction,
                release,
                dependencies),
            _ => throw new InvalidOperationException($"Unsupported update channel '{detection.Channel}'.")
        };
    }

    private static UpdateOperationResult RunPackageManagerUpdate(
        string executable,
        IReadOnlyList<string> arguments,
        string? currentVersion,
        string latestVersion,
        string wouldBeAction,
        IUpdateProcessRunner runner)
    {
        var process = runner.Run(executable, arguments);
        var success = process.ExitCode == 0;
        return new UpdateOperationResult
        {
            Outcome = success ? "updated" : "update-failed",
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            WouldBeAction = wouldBeAction,
            Action = FormatCommand(executable, arguments),
            Check = false,
            ProcessSpawned = true,
            WritesPerformed = true,
            Command = FormatCommand(executable, arguments),
            ProcessExitCode = process.ExitCode,
            ProcessStandardOutput = process.StandardOutput,
            ProcessStandardError = process.StandardError,
            Error = success ? null : $"{executable} exited with code {process.ExitCode}.",
            ManualGuidance = success ? Array.Empty<string>() : UpdateManualGuidance.AllChannels,
            ExitCode = success ? 0 : 1
        };
    }

    private static UpdateOperationResult RunStandaloneUpdate(
        UpdateChannelDetection detection,
        string? currentVersion,
        string latestVersion,
        string wouldBeAction,
        UpdateRelease release,
        UpdateDependencies dependencies)
    {
        var target = detection.ResolvedExecutablePath
            ?? throw new InvalidOperationException("The standalone executable real path was unavailable.");
        var outcome = dependencies.StandaloneInstaller.Apply(target, release);
        return new UpdateOperationResult
        {
            Outcome = "updated",
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            WouldBeAction = wouldBeAction,
            Action = "checksum-verified standalone temp+rename replacement",
            Check = false,
            WritesPerformed = true,
            TargetReplaced = true,
            TargetUntouchedOnFailure = false,
            ManualGuidance = Array.Empty<string>(),
            ExitCode = 0
        };
    }

    private static string WouldBeAction(UpdateChannel channel, string? latestVersion)
    {
        var version = string.IsNullOrWhiteSpace(latestVersion) ? "latest" : latestVersion;
        return channel switch
        {
            UpdateChannel.DotnetTool => "dotnet tool update -g JTechJapan.IntentSystem.Cli",
            UpdateChannel.NpmGlobal => "npm install -g intent-system@latest",
            UpdateChannel.Npx => "npx intent-system@latest <command> (guidance only; no mutation)",
            UpdateChannel.Standalone => $"download intent-cli-{version}-{RuntimeIdentifierForGuidance()}.<archive>, verify .sha256, then temp+rename",
            _ => "manual channel selection required"
        };
    }

    private static string RuntimeIdentifierForGuidance()
    {
        try
        {
            return StandaloneUpdateInstaller.CurrentRuntimeIdentifier();
        }
        catch (PlatformNotSupportedException)
        {
            return "<platform>";
        }
    }

    private static string? TryGetCurrentVersion(UpdateDependencies dependencies)
    {
        try
        {
            return dependencies.CurrentVersionProvider();
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeComparableVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var value = version.Trim();
        if (value.StartsWith("intent-cli ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["intent-cli ".Length..].Trim();
        }

        if (value.StartsWith('v'))
        {
            value = value[1..];
        }

        return value.Split(['+', '-'], 2, StringSplitOptions.None)[0];
    }

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { executable }.Concat(arguments.Select(ShellQuote)));

    private static string ShellQuote(string value) =>
        value.Any(char.IsWhiteSpace) ? $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'" : value;

    private static void WriteDetectionMarkdown(TextWriter writer, UpdateChannelDetection detection)
    {
        writer.WriteLine("## intent-cli update — channel detection");
        writer.WriteLine();
        writer.WriteLine($"- channel: `{detection.Channel}`");
        writer.WriteLine($"- executable path: `{detection.ExecutablePath ?? "<unavailable>"}`");
        writer.WriteLine($"- resolved real path: `{detection.ResolvedExecutablePath ?? "<unavailable>"}`");
        writer.WriteLine($"- path evidence: {detection.PathEvidence}");
        writer.WriteLine($"- fail-closed ambiguity: `{detection.Ambiguous}`");
        writer.WriteLine();
    }

    private static void BeginJsonEnvelope(TextWriter writer, UpdateChannelDetection detection)
    {
        var detectionJson = JsonSerializer.Serialize(detection, JsonOptions);
        writer.Write(detectionJson[..^1]);
        writer.Write(",\"result\":");
        writer.Flush();
    }

    private static void WriteResultMarkdown(TextWriter writer, UpdateOperationResult result)
    {
        writer.WriteLine("## intent-cli update — result");
        writer.WriteLine();
        writer.WriteLine($"- outcome: `{result.Outcome}`");
        writer.WriteLine($"- current version: `{result.CurrentVersion ?? "<unknown>"}`");
        writer.WriteLine($"- latest version: `{result.LatestVersion ?? "<not queried>"}`");
        writer.WriteLine($"- would-be action: {result.WouldBeAction}");
        writer.WriteLine($"- check: `{result.Check}`");
        writer.WriteLine($"- process spawned: `{result.ProcessSpawned}`");
        writer.WriteLine($"- writes performed: `{result.WritesPerformed}`");
        writer.WriteLine($"- target replaced: `{result.TargetReplaced}`");
        writer.WriteLine($"- target untouched on failure: `{result.TargetUntouchedOnFailure}`");
        if (!string.IsNullOrWhiteSpace(result.Command))
        {
            writer.WriteLine($"- command: `{result.Command}`");
        }
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine($"- error: {result.Error}");
        }
        if (result.ManualGuidance.Count > 0)
        {
            writer.WriteLine("- manual guidance:");
            foreach (var line in result.ManualGuidance)
            {
                writer.WriteLine($"  - {line}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out bool check,
        out string format,
        out string error)
    {
        check = false;
        format = FormatMarkdown;
        error = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--check":
                    check = true;
                    break;
                case "--format":
                    if (index + 1 >= args.Length)
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    format = args[++index];
                    if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(format, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{format}').";
                        return false;
                    }
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }
}
