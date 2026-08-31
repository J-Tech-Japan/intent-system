using System.Diagnostics;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ClaimCommandG766Tests
{
    [Fact]
    public void MetadataBranchClaimsWithEmptyCanonicalFailClosedForEveryScope_G766()
    {
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G766-known", "alice", "implementation")],
            configureMetadataBranch: true);

        var known = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G766-known",
            "implementation");
        var neverExisted = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G766-never-existed",
            "implementation");

        Assert.False(known.Passed);
        Assert.Equal("metadata-branch-only", known.Status);
        Assert.False(neverExisted.Passed);
        Assert.Equal("metadata-branch-only", neverExisted.Status);
        Assert.Null(known.Holder);
        Assert.Null(known.HolderTeam);
        Assert.Contains("metadata", known.Detail, StringComparison.OrdinalIgnoreCase);

        using var commandOutput = new StringWriter();
        var commandExit = ClaimVerificationCommand.Execute(
            Context(repos.FirstClone),
            ["--scope", "execution-unit:G766-known", "--team", "implementation", "--format", "json"],
            commandOutput);

        Assert.Equal(1, commandExit);
        using var commandResult = JsonDocument.Parse(commandOutput.ToString());
        Assert.False(commandResult.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(
            "metadata-branch-only",
            commandResult.RootElement.GetProperty("status").GetString());
        Assert.False(commandResult.RootElement.TryGetProperty("holder", out _));
        Assert.False(commandResult.RootElement.TryGetProperty("holder_team", out _));
    }

    [Fact]
    public void NoClaimsAnywherePreservesTheParentNotConfiguredPayload_G766()
    {
        using var repos = new ClaimRepositories(
            [],
            configureMetadataBranch: false,
            writeConfigWithoutMetadataBranch: true);
        const string scope = "execution-unit:G766-no-store";

        using var output = new StringWriter();
        var exitCode = ClaimVerificationCommand.Execute(
            Context(repos.FirstClone),
            ["--scope", scope, "--team", "implementation", "--format", "json"],
            output);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "{\n"
            + "  \"passed\": true,\n"
            + "  \"status\": \"not-configured\",\n"
            + "  \"scope\": \"execution-unit:G766-no-store\",\n"
            + "  \"store_configured\": false,\n"
            + "  \"invoking_team\": \"implementation\",\n"
            + "  \"detail\": \"No claims store is configured; legacy single-team behavior applies unchanged.\"\n"
            + "}\n",
            output.ToString());
    }

    [Fact]
    public void MetadataBranchOnlyIsAnOwnershipStopForAdjacentClaimConsumers_G766()
    {
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G766-consumer", "alice", "implementation")],
            configureMetadataBranch: true);
        var claim = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G766-consumer",
            "implementation");
        var issue = new GitHubAutomationIssueCandidate
        {
            Number = 1669,
            Title = "G766 claim-store state",
            Url = "https://github.com/J-Tech-Japan/intent-system/issues/1669",
            CreatedAt = "2026-08-31T00:00:00Z",
            Labels =
            [
                new GitHubAutomationLabel { Name = "intent-target" },
                new GitHubAutomationLabel { Name = "intent-issue-in-progress" },
            ],
        };

        var workerClaim = WorkerClaimAnalyzer.Analyze(
            "issue",
            ["intent-target"],
            claim);
        var nextAction = WorkerNextActionAnalyzer.Analyze(
            "J-Tech-Japan/intent-system",
            [],
            [issue],
            new Dictionary<int, ClaimOwnershipVerification> { [1669] = claim });
        var issuePreflight = WorkerIssuePreflightAnalyzer.Analyze(
            new GitHubIssueLookupResult
            {
                Number = 1669,
                State = "OPEN",
                Title = "G766 claim-store state",
                Labels = [new GitHubIssueLabel { Name = "intent-target" }],
            },
            "J-Tech-Japan/intent-system",
            1669,
            repos.FirstClone,
            claim);

        Assert.False(workerClaim.Proceed);
        Assert.Contains(workerClaim.Errors, error => error.Contains("Claims store is configured", StringComparison.Ordinal));
        Assert.Equal(WorkerNextActionConstants.Actions.Wait, nextAction.Action);
        Assert.Equal(WorkerNextActionConstants.SourceClassifications.ClaimRefused, nextAction.SourceClassification);
        Assert.Equal(WorkerIssuePreflightConstants.Classifications.ClaimUnavailable, issuePreflight.Classification);
        Assert.False(issuePreflight.Actionable);
        Assert.Equal("metadata-branch-only", issuePreflight.ClaimStatus);
    }

    [Fact]
    public void MetadataBranchRecordNeverBecomesCanonicalOwnership_G766()
    {
        using var repos = new ClaimRepositories(
            [Record("execution-unit:G766-canonical", "canonical", "implementation")],
            [Record("execution-unit:G766-metadata-only", "metadata", "review")],
            configureMetadataBranch: true);

        var canonical = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G766-canonical",
            "implementation");
        var metadataOnly = ClaimOwnershipVerifier.Verify(
            repos.FirstClone,
            "execution-unit:G766-metadata-only",
            "review");

        Assert.Equal(ClaimOwnershipVerification.StatusOwned, canonical.Status);
        Assert.Equal("canonical", canonical.Holder);
        Assert.Equal(ClaimOwnershipVerification.StatusUnheld, metadataOnly.Status);
        Assert.Null(metadataOnly.Holder);
        Assert.Null(metadataOnly.HolderTeam);
    }

    [Fact]
    public void DocumentationMirrorsDescribeMetadataOnlyFailClosedState_G766()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "05-implementation-loop.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "05-implementation-loop.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("metadata-branch-only", document, StringComparison.Ordinal);
            Assert.Contains("not-configured", document, StringComparison.Ordinal);
            Assert.Contains("G763", document, StringComparison.Ordinal);
        }

        Assert.Contains("every scope", english, StringComparison.Ordinal);
        Assert.Contains("全ての scope", japanese, StringComparison.Ordinal);
    }

    private static CliContext Context(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = "intent-cli",
                ArtifactRoot = ".intent-cli",
                MetadataSourceBranch = "main-metadata",
            },
        },
    };

    private static ClaimRecord Record(string scope, string actor, string team) =>
        new("1", scope, actor, team, DateTimeOffset.Parse("2026-08-31T00:00:00Z"), "base-commit");

    private static string Serialize(ClaimRecord record) =>
        JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

    private static void WriteRecords(string repo, IEnumerable<ClaimRecord> records)
    {
        foreach (var record in records)
        {
            var relative = ClaimCommand.ClaimPath(record.Scope);
            var absolute = Path.Combine(repo, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, Serialize(record), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static string Run(string workdir, string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{fileName} {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    private sealed class ClaimRepositories : IDisposable
    {
        private readonly TempDirectory temp = new("claim-g766-repos-");

        public ClaimRepositories(
            IReadOnlyList<ClaimRecord> metadata,
            bool configureMetadataBranch,
            bool writeConfigWithoutMetadataBranch = false)
            : this([], metadata, configureMetadataBranch, writeConfigWithoutMetadataBranch)
        {
        }

        public ClaimRepositories(
            IReadOnlyList<ClaimRecord> canonical,
            IReadOnlyList<ClaimRecord> metadata,
            bool configureMetadataBranch,
            bool writeConfigWithoutMetadataBranch = false)
        {
            Bare = Path.Combine(temp.Path, "origin.git");
            var seed = Path.Combine(temp.Path, "seed");
            FirstClone = Path.Combine(temp.Path, "first");
            Directory.CreateDirectory(Bare);
            Run(Bare, "git", "init", "--bare", "--quiet");
            Directory.CreateDirectory(seed);
            Run(seed, "git", "init", "--quiet", "--initial-branch=main");
            Run(seed, "git", "config", "user.name", "g766-fixture");
            Run(seed, "git", "config", "user.email", "g766-fixture@example.invalid");
            File.WriteAllText(Path.Combine(seed, "README.md"), "g766 fixture\n");
            Run(seed, "git", "add", "README.md");
            Run(seed, "git", "commit", "--quiet", "-m", "seed");
            Run(seed, "git", "remote", "add", "origin", Bare);
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main");

            WriteRecords(seed, canonical);
            if (canonical.Count > 0)
            {
                Run(seed, "git", "add", "--", ClaimCommand.ClaimsDirectory);
                Run(seed, "git", "commit", "--quiet", "-m", "canonical claims");
                Run(seed, "git", "push", "--quiet", "origin", "main");
            }
            Run(seed, "git", "switch", "--quiet", "-c", "main-metadata");
            WriteRecords(seed, metadata);
            if (metadata.Count > 0)
            {
                Run(seed, "git", "add", "--", ClaimCommand.ClaimsDirectory);
                Run(seed, "git", "commit", "--quiet", "-m", "metadata claims");
            }
            Run(seed, "git", "push", "--quiet", "-u", "origin", "main-metadata");
            Run(Bare, "git", "symbolic-ref", "HEAD", "refs/heads/main");
            Run(temp.Path, "git", "clone", "--quiet", Bare, FirstClone);
            if (configureMetadataBranch || writeConfigWithoutMetadataBranch)
            {
                var configDirectory = Path.Combine(FirstClone, ".intent-cli");
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(
                    Path.Combine(configDirectory, "config.toml"),
                    "[project]\n"
                    + "domain = \"intent-cli\"\n"
                    + "artifact_root = \".intent-cli\"\n"
                    + (configureMetadataBranch
                        ? "metadata_source_branch = \"main-metadata\"\n"
                        : string.Empty));
            }
        }

        public string Bare { get; }
        public string FirstClone { get; }

        public void Dispose() => temp.Dispose();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
