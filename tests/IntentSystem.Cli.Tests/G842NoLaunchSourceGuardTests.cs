using System.Text.RegularExpressions;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G842: textual no-launch source guard using pinned allow-list, declared-type,
/// surface-reference, and repository-wide type-reference fixtures.
/// Drift from ba496314: +2 G840 NotifyCommand wait-clock rows; declared-type count 556→558.
/// Drift from c66f4936 fixture to 3da9e7a1 (G841):
///   IssuePublishFlowCommand CrossRuntimeDesignReviewField 16→19;
///   PreparedPacketCommitReadyAnalyzer MetadataValidateAnalyzer 2→0;
///   ReviewCrossRuntimeCommand NotifyCommand 0→1 (doc comment; G842 reword removes it on head).
/// Surface-file claim-read pins are five; NotifyCommand on ReviewCrossRuntimeCommand is not a pin.
/// </summary>
public sealed class G842NoLaunchSourceGuardTests
{
    private static readonly string[] LaunchTokens =
    [
        "Process", "ProcessStartInfo", "GitProcessRunner", "GitCommandRunner", "CheckoutFreshnessGitCommandRunner",
        "GitRemoteCommandRunner", "GhCommandRunner", "GhReviewCommandRunner", "HostStateGitRetryRunner",
        "NotifyProcessRunner", "ProcessUpdateRunner", "ShellGitRunner", "IGitCommandRunner", "IGitHubCommandRunner",
        "IGitRemoteCommandRunner", "IGitRunner", "INotifyProcessRunner", "IReviewCommandRunner", "IUpdateProcessRunner",
        "ProcessRunnerFactory", "NestedProviderLauncher", "DllImport", "LibraryImport", "NativeLibrary",
        "Interaction",
    ];

    private static readonly HashSet<string> ExemptReviewCrossRuntimeLines = new(StringComparer.Ordinal)
    {
        "public static Func<bool>? NestedProviderLauncher { get; set; }",
        "internal static Func<INotifyProcessRunner>? ProcessRunnerFactory { get; set; }",
    };

    private static readonly Regex DeclaredTypeRegex = new(
        @"^[ \t]*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file|unsafe|new|ref)[ \t]+)*(?:class|struct|interface|record(?:[ \t]+(?:class|struct))?)[ \t]+([A-Z][A-Za-z0-9_]*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WholeIdentifierRegex = new(
        @"(?<![A-Za-z0-9_])([A-Z][A-Za-z0-9_]*)(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string CrossRuntimeReviewFileModeRelativePath =
        "src/IntentSystem.Cli/Commands/CrossRuntimeReviewFileMode.cs";

    private static readonly string[] InteropTokens = ["DllImport", "LibraryImport", "NativeLibrary"];

    private static readonly string[] PinnedFileModeInteropEntryPoints = ["close", "lstat", "open", "read"];

    private static readonly string[] ForbiddenLaunchEntryPointNames = ["fork", "popen", "system", "vfork"];

    private static readonly string[] ForbiddenLaunchEntryPointPrefixes = ["exec", "posix_spawn"];

    private static readonly Regex InteropEntryPointRegex = new(
        @"EntryPoint\s*=\s*""([^""]+)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void AllowList_MatchesPinnedFixture_AndHasNoCrossRuntimeEntries()
    {
        var allowList = ReadAllowList();
        var fixture = ReadAllowListFixture().ToArray();
        Assert.Equal(fixture.Length, allowList.Count);
        foreach (var (path, token, count) in fixture)
        {
            Assert.True(allowList.TryGetValue((path, token), out var actual), $"{path} {token}");
            Assert.Equal(count, actual);
        }

        foreach (var (path, _, _) in fixture)
        {
            Assert.DoesNotContain("CrossRuntime", path, StringComparison.Ordinal);
            Assert.DoesNotContain("ReviewCrossRuntimeCommand", path, StringComparison.Ordinal);
            Assert.DoesNotContain("GuideSoloConductorCommand", path, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LaunchTokens_StayWithinPinnedAllowList()
    {
        var allowList = ReadAllowListFixture().ToDictionary(entry => (entry.Path, entry.Token), entry => entry.Count);
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var path in EnumerateScannedSourceFiles(repoRoot))
        {
            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            var content = string.Join(
                '\n',
                lines.Where(line => !(relative == "src/IntentSystem.Cli/Commands/ReviewCrossRuntimeCommand.cs"
                                      && ExemptReviewCrossRuntimeLines.Contains(line.Trim()))));

            foreach (var token in LaunchTokens)
            {
                var count = CountWholeIdentifiers(content, token);
                if (count == 0)
                {
                    continue;
                }

                if (relative == CrossRuntimeReviewFileModeRelativePath
                    && token is "DllImport" or "LibraryImport" or "NativeLibrary")
                {
                    continue;
                }

                Assert.True(
                    allowList.TryGetValue((relative, token), out var allowed),
                    $"unlisted launch token '{token}' in {relative}");
                Assert.True(
                    count <= allowed,
                    $"launch token '{token}' count {count} exceeds pinned {allowed} in {relative}");
            }
        }
    }

    [Fact]
    public void CrossRuntimeReviewFileMode_InteropBindingsArePinned()
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), CrossRuntimeReviewFileModeRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = File.ReadAllText(path);
        foreach (var token in InteropTokens)
        {
            var count = CountWholeIdentifiers(content, token);
            if (token == "DllImport")
            {
                Assert.Equal(4, count);
                continue;
            }

            Assert.Equal(0, count);
        }

        var entryPoints = InteropEntryPointRegex.Matches(content)
            .Select(match => match.Groups[1].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(PinnedFileModeInteropEntryPoints, entryPoints);

        foreach (var entryPoint in entryPoints)
        {
            Assert.DoesNotContain(entryPoint, ForbiddenLaunchEntryPointNames, StringComparer.Ordinal);
            Assert.False(
                ForbiddenLaunchEntryPointPrefixes.Any(prefix => entryPoint.StartsWith(prefix, StringComparison.Ordinal)),
                $"forbidden launch entry point prefix in {CrossRuntimeReviewFileModeRelativePath}: {entryPoint}");
        }
    }

    [Fact]
    public void CrossRuntimeSurfaceFiles_CarryNoInteropBeyondFileMode()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var commandsRoot = Path.Combine(repoRoot, "src", "IntentSystem.Cli", "Commands");
        foreach (var path in Directory.EnumerateFiles(commandsRoot, "CrossRuntime*.cs", SearchOption.TopDirectoryOnly))
        {
            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (string.Equals(relative, CrossRuntimeReviewFileModeRelativePath, StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(path);
            foreach (var token in InteropTokens)
            {
                var count = CountWholeIdentifiers(content, token);
                Assert.True(
                    count == 0,
                    $"{relative} must not reference interop token '{token}' (count {count})");
            }
        }
    }

    [Fact]
    public void SurfaceFilePins_AreExactlyFiveClaimReadReferences()
    {
        var expected = ReadSurfaceRefFixture().ToArray();
        var expectedKeys = expected.Select(entry => (entry.Path, entry.Identifier)).ToHashSet();
        var claimReadPins = ReadSurfaceRefFixture()
            .Where(entry => expectedKeys.Contains((entry.Path, entry.Identifier)))
            .Select(entry => (entry.Path, entry.Identifier, entry.Count))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ThenBy(entry => entry.Identifier, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, claimReadPins);
    }

    [Fact]
    public void SurfaceFiles_DoNotReferenceAllowListedTypesBeyondPinnedBaseReferences()
    {
        var allowListPaths = ReadAllowListFixture().Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        var declaredTypes = CollectDeclaredTypes(allowListPaths);
        var pinnedSurfaceReferences = ReadSurfaceRefFixture()
            .Where(entry => IsSurfaceFile(entry.Path))
            .ToDictionary(entry => (entry.Path, entry.Identifier), entry => entry.Count);

        foreach (var path in EnumerateScannedSourceFiles(RepoVersionPolicySource.RepoRoot()))
        {
            var relative = Path.GetRelativePath(RepoVersionPolicySource.RepoRoot(), path).Replace('\\', '/');
            if (!IsSurfaceFile(relative))
            {
                continue;
            }

            foreach (var typeName in declaredTypes)
            {
                var count = CountWholeIdentifiers(File.ReadAllText(path), typeName);
                if (count == 0)
                {
                    continue;
                }

                if (pinnedSurfaceReferences.TryGetValue((relative, typeName), out var allowed))
                {
                    Assert.True(count <= allowed, $"{relative} references {typeName} {count} > {allowed}");
                    continue;
                }

                Assert.Fail($"surface file {relative} references allow-listed type {typeName} ({count}) without a pinned base reference");
            }
        }
    }

    [Fact]
    public void RepositoryWide_TypeReferences_MatchPinnedFixture()
    {
        var pinned = ReadTypeRefFixture().ToDictionary(entry => (entry.Path, entry.Identifier), entry => entry.Count);
        var declaredTypes = ReadDeclaredTypeFixture().Select(entry => entry.Identifier).ToHashSet(StringComparer.Ordinal);
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            var content = File.ReadAllText(path);
            foreach (var typeName in declaredTypes)
            {
                var actual = CountWholeIdentifiers(content, typeName);
                if (actual == 0)
                {
                    continue;
                }

                if (pinned.TryGetValue((relative, typeName), out var allowed))
                {
                    Assert.True(
                        actual <= allowed,
                        $"{relative} {typeName} count {actual} exceeds pinned {allowed}");
                    continue;
                }

                Assert.Fail($"unlisted type reference '{typeName}' in {relative} ({actual})");
            }
        }

        foreach (var (relative, identifier, count) in ReadTypeRefFixture())
        {
            var path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"pinned type-ref file missing: {relative}");
            var actual = CountWholeIdentifiers(File.ReadAllText(path), identifier);
            Assert.True(
                actual <= count,
                $"{relative} {identifier} count {actual} exceeds pinned {count}");
        }
    }

    [Fact]
    public void ReviewCrossRuntimeCommand_ExemptSeamLines_AreTheOnlyLaunchTokenHits()
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "src/IntentSystem.Cli/Commands/ReviewCrossRuntimeCommand.cs");
        var hits = LaunchTokens.SelectMany(token => WholeIdentifierRegex.Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value)
                .Where(name => string.Equals(name, token, StringComparison.Ordinal)))
            .ToArray();
        Assert.Equal(["INotifyProcessRunner", "NestedProviderLauncher", "ProcessRunnerFactory"], hits.OrderBy(name => name, StringComparer.Ordinal));
    }

    private static bool IsSurfaceFile(string relativePath) =>
        relativePath.StartsWith("src/IntentSystem.Cli/Commands/CrossRuntime", StringComparison.Ordinal)
        || string.Equals(relativePath, "src/IntentSystem.Cli/Commands/ReviewCrossRuntimeCommand.cs", StringComparison.Ordinal)
        || string.Equals(relativePath, "src/IntentSystem.Cli/Commands/GuideSoloConductorCommand.cs", StringComparison.Ordinal)
        || !ReadBaseCsPaths().Contains(relativePath);

    private static HashSet<string> ReadBaseCsPaths() =>
        File.ReadAllLines(FixturePath("no-launch-base-cs-paths-ba496314.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<(string Path, string Token, int Count)> ReadAllowListFixture() =>
        File.ReadAllLines(FixturePath("no-launch-allowlist-ba496314.tsv"))
            .Skip(1)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0], parts[1], int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));

    private static Dictionary<(string Path, string Token), int> ReadAllowList() =>
        ReadAllowListFixture().ToDictionary(entry => (entry.Path, entry.Token), entry => entry.Count);

    private static IEnumerable<(string Path, string Identifier, int Count)> ReadTypeRefFixture() =>
        File.ReadAllLines(FixturePath("no-launch-type-refs-3da9e7a1.tsv"))
            .Skip(1)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0], parts[1], int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));

    private static IEnumerable<(string Identifier, string Path)> ReadDeclaredTypeFixture() =>
        File.ReadAllLines(FixturePath("no-launch-declared-types-ba496314.tsv"))
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0], parts[1]));

    private static IEnumerable<(string Path, string Identifier, int Count)> ReadSurfaceRefFixture() =>
        File.ReadAllLines(FixturePath("no-launch-surface-refs-ba496314.tsv"))
            .Skip(1)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0], parts[1], int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));

    private static HashSet<string> CollectDeclaredTypes(HashSet<string> allowListPaths)
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in allowListPaths)
        {
            var path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (Match match in DeclaredTypeRegex.Matches(File.ReadAllText(path)))
            {
                declared.Add(match.Groups[1].Value);
            }
        }

        return declared;
    }

    private static IEnumerable<string> EnumerateScannedSourceFiles(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");
        foreach (var path in Directory.EnumerateFiles(srcRoot, "*", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        var rootProps = Path.Combine(repoRoot, "Directory.Build.props");
        if (File.Exists(rootProps))
        {
            yield return rootProps;
        }
    }

    private static int CountWholeIdentifiers(string content, string identifier)
    {
        return WholeIdentifierRegex.Matches(content)
            .Count(match => string.Equals(match.Groups[1].Value, identifier, StringComparison.Ordinal));
    }

    private static string FixturePath(string name) =>
        Path.Combine(RepoVersionPolicySource.RepoRoot(), "tests", "IntentSystem.Cli.Tests", "Fixtures", "G842", name);
}
